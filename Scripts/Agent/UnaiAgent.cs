using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Core;
using UnAI.Memory;
using UnAI.Models;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.Agent
{
    public class UnaiAgent
    {
        private readonly UnaiAgentConfig _config;
        private readonly UnaiToolRegistry _tools;
        private readonly UnaiConversation _conversation;
        private readonly IUnaiProvider _provider;

        public event Action<UnaiAgentThinkingArgs> OnThinking;
        public event Action<UnaiAgentToolCallArgs> OnToolCall;
        public event Action<UnaiAgentToolResultArgs> OnToolResult;
        public event Action<UnaiAgentStepCompleteArgs> OnStepComplete;
        public event Action<UnaiAgentStreamDeltaArgs> OnStreamDelta;

        public UnaiConversation Conversation => _conversation;
        public UnaiToolRegistry Tools => _tools;

        public UnaiAgent(UnaiAgentConfig config, UnaiToolRegistry tools = null)
        {
            _config = config ?? new UnaiAgentConfig();
            _tools = tools ?? new UnaiToolRegistry();
            _conversation = new UnaiConversation { SystemPrompt = _config.SystemPrompt };

            string providerId = _config.ProviderId ??
                UnaiManager.Instance?.Config?.DefaultProviderId ?? "openai";
            _provider = UnaiProviderRegistry.Get(providerId);
        }

        public UnaiAgent(IUnaiProvider provider, UnaiAgentConfig config, UnaiToolRegistry tools = null)
        {
            _config = config ?? new UnaiAgentConfig();
            _tools = tools ?? new UnaiToolRegistry();
            _conversation = new UnaiConversation { SystemPrompt = _config.SystemPrompt };
            _provider = provider;
        }

        public async Task<UnaiAgentStep> RunAsync(string userMessage, CancellationToken ct = default)
        {
            _conversation.AddUser(userMessage);
            return await ExecuteLoopAsync(ct);
        }

        public async Task<UnaiAgentStep> ContinueAsync(string userMessage, CancellationToken ct = default)
        {
            _conversation.AddUser(userMessage);
            return await ExecuteLoopAsync(ct);
        }

        private async Task<UnaiAgentStep> ExecuteLoopAsync(CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_config.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var token = linked.Token;

            UnaiAgentStep lastStep = null;

            for (int step = 1; step <= _config.MaxSteps; step++)
            {
                token.ThrowIfCancellationRequested();

                float stepStart = Time.realtimeSinceStartup;

                // Build effective system prompt (inject tool descriptions for non-tool providers)
                string effectiveSystemPrompt = _config.SystemPrompt;
                bool hasTools = _tools.GetAll().Count > 0;
                if (!_provider.SupportsToolCalling && hasTools)
                {
                    effectiveSystemPrompt = (_config.SystemPrompt ?? "") + "\n\n" +
                        UnaiToolSerializer.ToTextDescription(_tools.GetAllDefinitions());
                }
                _conversation.SystemPrompt = effectiveSystemPrompt;

                var toolDefs = _provider.SupportsToolCalling && hasTools
                    ? _tools.GetAllDefinitions().ToList()
                    : null;

                int? maxCtx = _config.MaxContextTokens ?? GetModelContextWindow();
                var request = _conversation.BuildRequest(
                    _config.Model, _config.Options, toolDefs, maxCtx, _config.MemoryStrategy);

                OnThinking?.Invoke(new UnaiAgentThinkingArgs
                {
                    StepNumber = step,
                    MessageCount = request.Messages.Count,
                    EstimatedTokens = UnaiTokenEstimator.EstimateMessages(request.Messages)
                });

                // Call LLM
                UnaiChatResponse response;
                if (_config.UseStreaming)
                    response = await CallStreamingAsync(request, step, token);
                else
                    response = await _provider.ChatAsync(request, token);

                // Record response
                _conversation.AddAssistant(response.Content, response.ToolCalls);

                // Check for tool calls
                List<UnaiToolCall> toolCalls = response.ToolCalls;

                // For non-native-tool providers, try parsing text-based tool calls
                if (toolCalls == null && !_provider.SupportsToolCalling && hasTools)
                {
                    toolCalls = UnaiToolSerializer.ParseTextToolCalls(response.Content);
                }

                List<UnaiToolResult> toolResults = null;

                if (toolCalls is { Count: > 0 })
                {
                    toolResults = new List<UnaiToolResult>();

                    foreach (var call in toolCalls)
                    {
                        OnToolCall?.Invoke(new UnaiAgentToolCallArgs
                        {
                            StepNumber = step,
                            ToolCall = call
                        });

                        float toolStart = Time.realtimeSinceStartup;
                        var result = await ExecuteToolAsync(call, token);
                        float toolDuration = (Time.realtimeSinceStartup - toolStart) * 1000f;

                        toolResults.Add(result);
                        _conversation.AddToolResult(result.ToolCallId, result.ToolName, result.Content);

                        OnToolResult?.Invoke(new UnaiAgentToolResultArgs
                        {
                            StepNumber = step,
                            Result = result,
                            ExecutionTimeMs = toolDuration
                        });
                    }
                }

                float stepDuration = (Time.realtimeSinceStartup - stepStart) * 1000f;
                bool isFinal = toolCalls == null || toolCalls.Count == 0;

                lastStep = new UnaiAgentStep
                {
                    StepNumber = step,
                    Response = response,
                    ToolCalls = toolCalls,
                    ToolResults = toolResults,
                    DurationMs = stepDuration,
                    IsFinal = isFinal || step >= _config.MaxSteps,
                    StopReason = isFinal ? "completed" : (step >= _config.MaxSteps ? "max_steps" : null)
                };

                OnStepComplete?.Invoke(new UnaiAgentStepCompleteArgs { Step = lastStep });

                if (isFinal)
                    return lastStep;
            }

            if (lastStep != null)
            {
                lastStep.StopReason = "max_steps";
                lastStep.IsFinal = true;
            }
            return lastStep;
        }

        private async Task<UnaiToolResult> ExecuteToolAsync(UnaiToolCall call, CancellationToken ct)
        {
            var tool = _tools.Get(call.ToolName);
            if (tool == null)
            {
                return new UnaiToolResult
                {
                    ToolCallId = call.Id,
                    ToolName = call.ToolName,
                    Content = $"Error: Unknown tool '{call.ToolName}'",
                    IsError = true
                };
            }

            try
            {
                var result = await tool.ExecuteAsync(call, ct);
                result.ToolCallId = call.Id;
                result.ToolName = call.ToolName;
                return result;
            }
            catch (Exception ex)
            {
                return new UnaiToolResult
                {
                    ToolCallId = call.Id,
                    ToolName = call.ToolName,
                    Content = $"Error executing tool: {ex.Message}",
                    IsError = true
                };
            }
        }

        private async Task<UnaiChatResponse> CallStreamingAsync(
            UnaiChatRequest request, int step, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<UnaiChatResponse>();

            await _provider.ChatStreamAsync(
                request,
                onDelta: delta =>
                {
                    OnStreamDelta?.Invoke(new UnaiAgentStreamDeltaArgs
                    {
                        StepNumber = step,
                        Delta = delta
                    });
                },
                onComplete: response => tcs.TrySetResult(response),
                onError: error => tcs.TrySetException(new UnaiRequestException(error)),
                ct);

            return await tcs.Task;
        }

        private int? GetModelContextWindow()
        {
            if (_provider == null) return null;
            string modelId = _config.Model;
            if (string.IsNullOrEmpty(modelId)) return null;
            var modelInfo = _provider.KnownModels.FirstOrDefault(m => m.Id == modelId);
            return modelInfo?.MaxContextTokens;
        }
    }
}
