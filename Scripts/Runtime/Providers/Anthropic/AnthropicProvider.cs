using System.Collections.Generic;
using System.Linq;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers.Anthropic
{
    public class AnthropicProvider : UnaiProviderBase
    {
        public override string ProviderId => "anthropic";
        public override string DisplayName => "Anthropic Claude";
        public override bool SupportsToolCalling => true;

        private const string AnthropicVersion = "2023-06-01";

        public override IReadOnlyList<UnaiModelInfo> KnownModels => new[]
        {
            new UnaiModelInfo { Id = "claude-opus-4-6", DisplayName = "Claude Opus 4.6", ProviderId = "anthropic", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "claude-sonnet-4-5-20250929", DisplayName = "Claude Sonnet 4.5", ProviderId = "anthropic", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "claude-haiku-4-5-20251001", DisplayName = "Claude Haiku 4.5", ProviderId = "anthropic", MaxContextTokens = 200000 },
        };

        protected override string BuildRequestUrl(UnaiChatRequest request)
        {
            return Config.BaseUrl.TrimEnd('/') + "/v1/messages";
        }

        protected override Dictionary<string, string> BuildHeaders()
        {
            return new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["x-api-key"] = Config.ResolvedApiKey,
                ["anthropic-version"] = AnthropicVersion
            };
        }

        protected override string SerializeRequest(UnaiChatRequest request)
        {
            var obj = new JObject
            {
                ["model"] = request.Model,
                ["stream"] = request.Stream,
                ["max_tokens"] = request.Options?.MaxTokens ?? 4096
            };

            var systemMsg = request.Messages.FirstOrDefault(m => m.Role == UnaiRole.System);
            string systemContent = systemMsg?.Content;

            if (request.Options?.ResponseFormat is UnaiResponseFormat.JsonObject or UnaiResponseFormat.JsonSchema)
            {
                string jsonHint = "\n\nYou must respond with valid JSON only. No markdown, no explanation — just the JSON object.";
                if (request.Options.ResponseFormat == UnaiResponseFormat.JsonSchema && request.Options.JsonSchema != null)
                    jsonHint += $"\n\nRespond according to this JSON schema:\n{request.Options.JsonSchema.ToString(Formatting.Indented)}";
                systemContent = (systemContent ?? "") + jsonHint;
            }

            if (!string.IsNullOrEmpty(systemContent))
                obj["system"] = systemContent;

            if (request.Tools is { Count: > 0 })
            {
                var toolsArray = new JArray();
                foreach (var tool in request.Tools)
                {
                    toolsArray.Add(new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["input_schema"] = tool.ParametersSchema ?? new JObject { ["type"] = "object" }
                    });
                }
                obj["tools"] = toolsArray;
            }

            var messages = new JArray();
            foreach (var msg in request.Messages.Where(m => m.Role != UnaiRole.System))
            {
                if (msg.Role == UnaiRole.Tool)
                {
                    messages.Add(new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray(new JObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = msg.ToolCallId,
                            ["content"] = msg.Content
                        })
                    });
                }
                else if (msg.Role == UnaiRole.Assistant && msg.ToolCalls is { Count: > 0 })
                {
                    var contentBlocks = new JArray();
                    if (!string.IsNullOrEmpty(msg.Content))
                        contentBlocks.Add(new JObject { ["type"] = "text", ["text"] = msg.Content });

                    foreach (var tc in msg.ToolCalls)
                    {
                        contentBlocks.Add(new JObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.Id,
                            ["name"] = tc.ToolName,
                            ["input"] = JObject.Parse(tc.ArgumentsJson ?? "{}")
                        });
                    }
                    messages.Add(new JObject
                    {
                        ["role"] = "assistant",
                        ["content"] = contentBlocks
                    });
                }
                else
                {
                    messages.Add(new JObject
                    {
                        ["role"] = msg.Role == UnaiRole.User ? "user" : "assistant",
                        ["content"] = msg.Content
                    });
                }
            }
            obj["messages"] = messages;

            if (request.Options != null)
            {
                if (request.Options.Temperature.HasValue)
                    obj["temperature"] = request.Options.Temperature.Value;
                if (request.Options.TopP.HasValue)
                    obj["top_p"] = request.Options.TopP.Value;
                if (request.Options.StopSequences is { Length: > 0 })
                    obj["stop_sequences"] = JArray.FromObject(request.Options.StopSequences);
            }

            return obj.ToString(Formatting.None);
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);

            string content = "";
            List<UnaiToolCall> toolCalls = null;

            if (root["content"] is JArray contentArray)
            {
                foreach (var block in contentArray)
                {
                    string type = block["type"]?.ToString();
                    if (type == "text")
                    {
                        content += block["text"]?.ToString() ?? "";
                    }
                    else if (type == "tool_use")
                    {
                        toolCalls ??= new List<UnaiToolCall>();
                        toolCalls.Add(new UnaiToolCall
                        {
                            Id = block["id"]?.ToString(),
                            ToolName = block["name"]?.ToString(),
                            ArgumentsJson = block["input"]?.ToString(Formatting.None) ?? "{}"
                        });
                    }
                }
            }

            int inputTokens = root["usage"]?["input_tokens"]?.Value<int>() ?? 0;
            int outputTokens = root["usage"]?["output_tokens"]?.Value<int>() ?? 0;

            return new UnaiChatResponse
            {
                Content = content,
                Role = UnaiRole.Assistant,
                Model = root["model"]?.ToString(),
                FinishReason = root["stop_reason"]?.ToString(),
                ToolCalls = toolCalls,
                Usage = new UnaiUsageInfo
                {
                    PromptTokens = inputTokens,
                    CompletionTokens = outputTokens,
                    TotalTokens = inputTokens + outputTokens
                }
            };
        }

        protected override ISseLineParser CreateStreamParser()
        {
            // Accumulate usage across streaming events:
            // message_start has input_tokens, message_delta has output_tokens
            int inputTokens = 0;
            int outputTokens = 0;

            return new SseLineParser(
                deltaFactory: (eventType, jsonData) =>
                {
                    var root = JObject.Parse(jsonData);
                    string type = root["type"]?.ToString();

                    switch (type)
                    {
                        case "message_start":
                        {
                            var msgUsage = root["message"]?["usage"];
                            if (msgUsage != null)
                                inputTokens = msgUsage["input_tokens"]?.Value<int>() ?? 0;
                            return null;
                        }
                        case "content_block_delta":
                            return new UnaiStreamDelta
                            {
                                Content = root["delta"]?["text"]?.ToString() ?? "",
                                EventType = type
                            };
                        case "message_delta":
                        {
                            var deltaUsage = root["usage"];
                            if (deltaUsage != null)
                                outputTokens = deltaUsage["output_tokens"]?.Value<int>() ?? 0;

                            return new UnaiStreamDelta
                            {
                                Content = "",
                                IsFinal = true,
                                FinishReason = root["delta"]?["stop_reason"]?.ToString(),
                                EventType = type,
                                Usage = new UnaiUsageInfo
                                {
                                    PromptTokens = inputTokens,
                                    CompletionTokens = outputTokens,
                                    TotalTokens = inputTokens + outputTokens
                                }
                            };
                        }
                        case "message_stop":
                            return new UnaiStreamDelta
                            {
                                Content = "",
                                IsFinal = true,
                                EventType = type
                            };
                        default:
                            return null;
                    }
                },
                doneMarker: "__ANTHROPIC_NO_DONE__");
        }

        protected override bool ValidateConfig()
        {
            return base.ValidateConfig() && !string.IsNullOrEmpty(Config.ResolvedApiKey);
        }
    }
}
