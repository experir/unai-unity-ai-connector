using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Core;
using UnAI.Memory;
using UnAI.Models;
using UnAI.Tools;
using UnityEngine;
using Debug = UnityEngine.Debug;

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
            int hallucinationNudges = 0;
            const int MaxHallucinationNudges = 2;
            var recentToolCalls = new List<string>(); // track for loop detection
            int consecutiveErrors = 0;
            string lastErrorContent = null;

            for (int step = 1; step <= _config.MaxSteps; step++)
            {
                token.ThrowIfCancellationRequested();

                var stepTimer = Stopwatch.StartNew();

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

                Debug.Log($"[UNAI] Step {step}: Response received. " +
                    $"Content length={response.Content?.Length ?? 0}, " +
                    $"NativeToolCalls={response.ToolCalls?.Count ?? 0}, " +
                    $"FinishReason={response.FinishReason}");

                // Record response
                _conversation.AddAssistant(response.Content, response.ToolCalls);

                // Check for tool calls (native)
                List<UnaiToolCall> toolCalls = response.ToolCalls;

                // Fallback: try parsing text-based tool calls from the response content
                // This handles models that output tool calls as JSON text rather than using native function calling
                if ((toolCalls == null || toolCalls.Count == 0) && hasTools
                    && !string.IsNullOrWhiteSpace(response.Content))
                {
                    var textToolCalls = UnaiToolSerializer.ParseTextToolCalls(response.Content);
                    if (textToolCalls is { Count: > 0 })
                    {
                        Debug.Log($"[UNAI] Step {step}: Parsed {textToolCalls.Count} text-based tool call(s) from response");
                        toolCalls = textToolCalls;
                    }
                    else
                    {
                        Debug.Log($"[UNAI] Step {step}: No tool calls detected (native or text). " +
                            $"Content preview: {(response.Content.Length > 200 ? response.Content.Substring(0, 200) + "..." : response.Content)}");
                    }
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

                        float toolStart = (float)Stopwatch.GetTimestamp();
                        var result = await ExecuteToolAsync(call, token);
                        float toolDuration = (float)(Stopwatch.GetTimestamp() - toolStart) / Stopwatch.Frequency * 1000f;

                        toolResults.Add(result);
                        _conversation.AddToolResult(result.ToolCallId, result.ToolName, result.Content);
                    // Track consecutive errors with same content
                    if (result.IsError)
                    {
                        if (result.Content == lastErrorContent)
                            consecutiveErrors++;
                        else
                        {
                            consecutiveErrors = 1;
                            lastErrorContent = result.Content;
                        }
                    }
                    else
                    {
                        consecutiveErrors = 0;
                        lastErrorContent = null;
                    }
                        OnToolResult?.Invoke(new UnaiAgentToolResultArgs
                        {
                            StepNumber = step,
                            Result = result,
                            ExecutionTimeMs = toolDuration
                        });
                    }
                }

                float stepDuration = (float)stepTimer.ElapsedMilliseconds;
                bool noToolCalls = toolCalls == null || toolCalls.Count == 0;
                bool isFinal = noToolCalls;

                // Hallucination detection: if no tool calls but response looks like the
                // model is describing actions it should have used tools for, nudge it
                // to actually use tools and continue the loop.
                if (noToolCalls && hasTools && step < _config.MaxSteps
                    && !string.IsNullOrWhiteSpace(response.Content)
                    && LooksLikeHallucinatedAction(response.Content))
                {
                    hallucinationNudges++;
                    if (hallucinationNudges <= MaxHallucinationNudges)
                    {
                        Debug.Log($"[UNAI] Step {step}: Detected hallucinated action in text response. Nudging model to use tools. (nudge {hallucinationNudges}/{MaxHallucinationNudges})");
                        var available = string.Join(", ", _tools.GetNames());
                        _conversation.AddUser(
                            "You described performing an action but did NOT actually call a tool. " +
                            "Nothing happened in the scene. You MUST use the provided tools to perform actions. " +
                            $"Available tools: {available}. " +
                            "Please call the appropriate tool(s) now to actually perform the action.");
                        isFinal = false;
                    }
                    else
                    {
                        Debug.LogWarning($"[UNAI] Step {step}: Hallucination nudge limit reached ({MaxHallucinationNudges}). Accepting response as final.");
                        isFinal = true;
                    }
                }

                // Loop/stuck detection: if the same tool calls keep repeating, break out
                string stopReason = null;
                if (toolCalls is { Count: > 0 })
                {
                    string callSignature = string.Join("|", toolCalls.Select(tc => $"{tc.ToolName}:{tc.ArgumentsJson}"));
                    recentToolCalls.Add(callSignature);

                    // Check last 4 calls for repetition
                    if (recentToolCalls.Count >= 4)
                    {
                        var last4 = recentToolCalls.Skip(recentToolCalls.Count - 4).ToList();
                        int duplicates = last4.Count(c => c == last4.Last());
                        if (duplicates >= 3)
                        {
                            Debug.LogWarning($"[UNAI] Step {step}: Detected stuck loop — same tool call repeated {duplicates} times in last 4 steps. Breaking out.");
                            isFinal = true;
                            stopReason = "stuck_loop";
                        }
                    }
                }

                // Error repetition detection: same error 3+ times means the model can't recover
                if (consecutiveErrors >= 3)
                {
                    Debug.LogWarning($"[UNAI] Step {step}: Same error repeated {consecutiveErrors} times. Model is stuck. Breaking out.");
                    isFinal = true;
                    stopReason = "stuck_error";
                }

                if (stopReason == null && isFinal) stopReason = "completed";
                if (step >= _config.MaxSteps) stopReason ??= "max_steps";

                lastStep = new UnaiAgentStep
                {
                    StepNumber = step,
                    Response = response,
                    ToolCalls = toolCalls,
                    ToolResults = toolResults,
                    DurationMs = stepDuration,
                    IsFinal = isFinal || step >= _config.MaxSteps,
                    StopReason = stopReason
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

            // Fuzzy match: try to find the intended tool
            if (tool == null)
            {
                tool = _tools.GetFuzzy(call.ToolName);
                if (tool != null)
                {
                    Debug.Log($"[UNAI] Fuzzy-matched tool '{call.ToolName}' → '{tool.Definition.Name}'");
                    call.ToolName = tool.Definition.Name;
                }
            }

            if (tool == null)
            {
                var available = string.Join(", ", _tools.GetNames());
                return new UnaiToolResult
                {
                    ToolCallId = call.Id,
                    ToolName = call.ToolName,
                    Content = $"Error: Unknown tool '{call.ToolName}'. " +
                              $"Available tools are: {available}. " +
                              $"Please use one of these exact tool names.",
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

        /// <summary>
        /// Detects when the model's text response describes performing Unity actions
        /// without actually calling tools (hallucinated actions), or outputs
        /// raw JSON tool call text instead of using native function calling.
        /// </summary>
        private bool LooksLikeHallucinatedAction(string content)
        {
            // Check for code blocks (model outputting scripts as text)
            if (content.Contains("```"))
                return true;

            // Check for raw JSON that looks like a tool call the model failed to invoke natively
            string trimmed = content.Trim();
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                // Check if the JSON contains any known tool name
                var toolNames = _tools.GetNames();
                foreach (var name in toolNames)
                {
                    if (trimmed.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            // Check for action verbs that suggest the model thinks it did something
            var actionPatterns = new[]
            {
                @"\b(I('ve|\s+have)|I\s+)\s*(created|added|attached|modified|updated|moved|renamed|deleted|removed|set|placed|spawned|instantiat)",
                @"\b(script|component|gameobject|object|prefab)\s+(has been|was|is)\s+(created|added|attached|modified|placed)",
                @"\bAdded\s+(script|component|a\s+new)",
                @"\bCreated\s+(a\s+new\s+)?(script|file|class)",
                @"\bHere('s|\s+is)\s+(the|a|your)\s+(script|code|component)",
            };

            foreach (var pattern in actionPatterns)
            {
                if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
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
