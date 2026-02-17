using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Tools;
using UnAI.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers.Ollama
{
    public class OllamaProvider : UnaiProviderBase
    {
        public override string ProviderId => "ollama";
        public override string DisplayName => "Ollama (Local)";
        public override bool SupportsToolCalling => true;

        protected override string BuildRequestUrl(UnaiChatRequest request)
        {
            return Config.BaseUrl.TrimEnd('/') + "/api/chat";
        }

        protected override Dictionary<string, string> BuildHeaders()
        {
            return new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            };
        }

        protected override string SerializeRequest(UnaiChatRequest request)
        {
            var obj = new JObject
            {
                ["model"] = request.Model,
                ["stream"] = request.Stream
            };

            var messages = new JArray();
            foreach (var msg in request.Messages)
            {
                var msgObj = new JObject
                {
                    ["role"] = msg.Role.ToString().ToLowerInvariant(),
                    ["content"] = msg.Content ?? ""
                };

                // Serialize assistant tool calls
                if (msg.Role == UnaiRole.Assistant && msg.ToolCalls is { Count: > 0 })
                {
                    var toolCallsArr = new JArray();
                    foreach (var tc in msg.ToolCalls)
                    {
                        var funcArgs = !string.IsNullOrEmpty(tc.ArgumentsJson)
                            ? JObject.Parse(tc.ArgumentsJson)
                            : new JObject();
                        toolCallsArr.Add(new JObject
                        {
                            ["function"] = new JObject
                            {
                                ["name"] = tc.ToolName,
                                ["arguments"] = funcArgs
                            }
                        });
                    }
                    msgObj["tool_calls"] = toolCallsArr;
                }

                messages.Add(msgObj);
            }
            obj["messages"] = messages;

            // Serialize tool definitions (Ollama native tool calling)
            if (request.Tools is { Count: > 0 })
            {
                var toolsArray = new JArray();
                foreach (var tool in request.Tools)
                {
                    toolsArray.Add(new JObject
                    {
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = tool.Name,
                            ["description"] = tool.Description,
                            ["parameters"] = tool.ParametersSchema ?? new JObject { ["type"] = "object" }
                        }
                    });
                }
                obj["tools"] = toolsArray;
            }

            if (request.Options?.ResponseFormat is UnaiResponseFormat.JsonObject)
            {
                obj["format"] = "json";
            }
            else if (request.Options?.ResponseFormat == UnaiResponseFormat.JsonSchema && request.Options.JsonSchema != null)
            {
                obj["format"] = request.Options.JsonSchema;
            }

            if (request.Options != null)
            {
                var options = new JObject();
                if (request.Options.Temperature.HasValue)
                    options["temperature"] = request.Options.Temperature.Value;
                if (request.Options.MaxTokens.HasValue)
                    options["num_predict"] = request.Options.MaxTokens.Value;
                if (request.Options.TopP.HasValue)
                    options["top_p"] = request.Options.TopP.Value;
                if (request.Options.StopSequences is { Length: > 0 })
                    options["stop"] = JArray.FromObject(request.Options.StopSequences);

                if (options.Count > 0)
                    obj["options"] = options;
            }

            return obj.ToString(Formatting.None);
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);
            var message = root["message"];

            var response = new UnaiChatResponse
            {
                Content = message?["content"]?.ToString() ?? "",
                Role = UnaiRole.Assistant,
                Model = root["model"]?.ToString(),
                FinishReason = root["done"]?.Value<bool>() == true ? "stop" : null
            };

            // Parse native tool calls from Ollama response
            if (message?["tool_calls"] is JArray toolCalls && toolCalls.Count > 0)
            {
                response.ToolCalls = new List<UnaiToolCall>();
                foreach (var tc in toolCalls)
                {
                    var func = tc["function"];
                    response.ToolCalls.Add(new UnaiToolCall
                    {
                        Id = $"ollama_{System.Guid.NewGuid():N}".Substring(0, 24),
                        ToolName = func?["name"]?.ToString(),
                        ArgumentsJson = func?["arguments"]?.ToString(Formatting.None) ?? "{}"
                    });
                }
                UnaiLogger.LogVerbose($"[UNAI] Ollama: parsed {response.ToolCalls.Count} native tool call(s)");
            }
            else if (!string.IsNullOrWhiteSpace(response.Content))
            {
                // Some models output tool calls as JSON text in content instead of using native tool_calls.
                // Try to parse the content as a tool call JSON.
                var parsed = TryParseContentAsToolCall(response.Content);
                if (parsed != null)
                {
                    response.ToolCalls = parsed;
                    UnaiLogger.LogVerbose($"[UNAI] Ollama: parsed {parsed.Count} tool call(s) from content JSON");
                }
            }

            return response;
        }

        /// <summary>
        /// Attempts to parse the response content as a JSON tool call.
        /// Handles formats like {"name": "tool", "parameters": {...}} or {"tool": "name", "arguments": {...}}
        /// </summary>
        private static List<UnaiToolCall> TryParseContentAsToolCall(string content)
        {
            string trimmed = content.Trim();
            if (!trimmed.StartsWith("{")) return null;

            try
            {
                var obj = JObject.Parse(trimmed);
                string toolName = obj["name"]?.ToString()
                    ?? obj["tool"]?.ToString()
                    ?? obj["function"]?.ToString();

                if (string.IsNullOrEmpty(toolName)) return null;

                var argsToken = obj["parameters"] ?? obj["arguments"] ?? obj["args"];
                var call = new UnaiToolCall
                {
                    Id = $"ollama_content_{Guid.NewGuid():N}".Substring(0, 24),
                    ToolName = toolName,
                    ArgumentsJson = argsToken?.ToString(Formatting.None) ?? "{}"
                };

                return new List<UnaiToolCall> { call };
            }
            catch
            {
                return null;
            }
        }

        protected override ISseLineParser CreateStreamParser()
        {
            return new OllamaNdjsonLineParser();
        }

        /// <summary>
        /// When tools are present, fall back to non-streaming for reliable native tool call parsing.
        /// Ollama's streaming mode can produce malformed JSON when the model outputs tool calls as text.
        /// </summary>
        public override async Task ChatStreamAsync(
            UnaiChatRequest request,
            Action<UnaiStreamDelta> onDelta,
            Action<UnaiChatResponse> onComplete = null,
            Action<UnaiErrorInfo> onError = null,
            CancellationToken cancellationToken = default)
        {
            if (request.Tools is { Count: > 0 })
            {
                UnaiLogger.LogVerbose("[UNAI] Ollama: Using non-streaming mode for tool-calling request");
                try
                {
                    var response = await ChatAsync(request, cancellationToken);

                    // Emit a single final delta so the UI still gets content updates
                    onDelta?.Invoke(new UnaiStreamDelta
                    {
                        Content = response.Content,
                        AccumulatedContent = response.Content,
                        IsFinal = true,
                        FinishReason = response.FinishReason ?? "stop",
                        EventType = "done",
                        ToolCalls = response.ToolCalls
                    });

                    onComplete?.Invoke(response);
                }
                catch (UnaiRequestException ex)
                {
                    onError?.Invoke(ex.ErrorInfo);
                }
                return;
            }

            await base.ChatStreamAsync(request, onDelta, onComplete, onError, cancellationToken);
        }

        protected override bool ValidateConfig()
        {
            return !string.IsNullOrEmpty(Config.BaseUrl);
        }
    }

    /// <summary>
    /// Custom NDJSON parser for Ollama that handles tool calls in streaming mode.
    /// Ollama sends tool calls in the final message when streaming.
    /// </summary>
    internal class OllamaNdjsonLineParser : ISseLineParser
    {
        private readonly System.Text.StringBuilder _accumulated = new();
        private List<UnaiToolCall> _pendingToolCalls;

        public bool IsComplete { get; private set; }

        public UnaiStreamDelta ProcessLine(string line)
        {
            if (IsComplete || string.IsNullOrWhiteSpace(line)) return null;

            try
            {
                var root = JObject.Parse(line);
                bool done = root["done"]?.Value<bool>() ?? false;
                var message = root["message"];
                string content = message?["content"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(content))
                    _accumulated.Append(content);

                // Parse tool calls from the message (Ollama includes them in stream messages)
                if (message?["tool_calls"] is JArray toolCalls && toolCalls.Count > 0)
                {
                    _pendingToolCalls = new List<UnaiToolCall>();
                    foreach (var tc in toolCalls)
                    {
                        var func = tc["function"];
                        _pendingToolCalls.Add(new UnaiToolCall
                        {
                            Id = $"ollama_{System.Guid.NewGuid():N}".Substring(0, 24),
                            ToolName = func?["name"]?.ToString(),
                            ArgumentsJson = func?["arguments"]?.ToString(Formatting.None) ?? "{}"
                        });
                    }
                    UnaiLogger.LogVerbose($"[UNAI] Ollama stream: parsed {_pendingToolCalls.Count} tool call(s)");
                }

                var delta = new UnaiStreamDelta
                {
                    Content = content,
                    AccumulatedContent = _accumulated.ToString(),
                    IsFinal = done,
                    FinishReason = done ? "stop" : null,
                    EventType = done ? "done" : "delta",
                    ToolCalls = _pendingToolCalls
                };

                if (done)
                    IsComplete = true;

                return delta;
            }
            catch (System.Exception ex)
            {
                UnaiLogger.LogWarning($"[UNAI] Failed to parse Ollama NDJSON line: {ex.Message}");
                return null;
            }
        }

        public void Reset()
        {
            _accumulated.Clear();
            _pendingToolCalls = null;
            IsComplete = false;
        }
    }
}
