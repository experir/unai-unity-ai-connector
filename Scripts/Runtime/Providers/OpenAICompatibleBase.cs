using System.Collections.Generic;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Tools;
using UnAI.Utilities;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers
{
    public abstract class OpenAICompatibleBase : UnaiProviderBase
    {
        protected virtual string ChatEndpointPath => "/v1/chat/completions";

        public override bool SupportsToolCalling => true;

        protected override string BuildRequestUrl(UnaiChatRequest request)
        {
            return Config.BaseUrl.TrimEnd('/') + ChatEndpointPath;
        }

        protected override Dictionary<string, string> BuildHeaders()
        {
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            };

            string apiKey = Config.ResolvedApiKey;
            if (!string.IsNullOrEmpty(apiKey))
                headers["Authorization"] = $"Bearer {apiKey}";

            if (Config.CustomHeaders != null)
            {
                foreach (var kvp in Config.CustomHeaders)
                {
                    if (!string.IsNullOrEmpty(kvp.Key))
                        headers[kvp.Key] = kvp.Value;
                }
            }

            return headers;
        }

        protected override string SerializeRequest(UnaiChatRequest request)
        {
            var obj = new JObject
            {
                ["model"] = request.Model,
                ["stream"] = request.Stream,
                ["messages"] = SerializeMessages(request.Messages)
            };

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

            if (request.Options != null)
            {
                if (request.Options.Temperature.HasValue)
                    obj["temperature"] = request.Options.Temperature.Value;
                if (request.Options.MaxTokens.HasValue)
                    obj["max_tokens"] = request.Options.MaxTokens.Value;
                if (request.Options.TopP.HasValue)
                    obj["top_p"] = request.Options.TopP.Value;
                if (request.Options.StopSequences is { Length: > 0 })
                    obj["stop"] = JArray.FromObject(request.Options.StopSequences);

                if (request.Options.ExtraParameters != null)
                {
                    foreach (var kvp in request.Options.ExtraParameters)
                        obj[kvp.Key] = JToken.FromObject(kvp.Value);
                }
            }

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        protected JArray SerializeMessages(List<UnaiChatMessage> messages)
        {
            var arr = new JArray();
            foreach (var msg in messages)
            {
                if (msg.Role == UnaiRole.Tool)
                {
                    arr.Add(new JObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = msg.ToolCallId,
                        ["content"] = msg.Content
                    });
                }
                else if (msg.Role == UnaiRole.Assistant && msg.ToolCalls is { Count: > 0 })
                {
                    var msgObj = new JObject
                    {
                        ["role"] = "assistant"
                    };
                    if (!string.IsNullOrEmpty(msg.Content))
                        msgObj["content"] = msg.Content;

                    var toolCallsArr = new JArray();
                    foreach (var tc in msg.ToolCalls)
                    {
                        toolCallsArr.Add(new JObject
                        {
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = tc.ToolName,
                                ["arguments"] = tc.ArgumentsJson ?? "{}"
                            }
                        });
                    }
                    msgObj["tool_calls"] = toolCallsArr;
                    arr.Add(msgObj);
                }
                else
                {
                    arr.Add(new JObject
                    {
                        ["role"] = msg.Role.ToString().ToLowerInvariant(),
                        ["content"] = msg.Content
                    });
                }
            }
            return arr;
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);
            var choice = root["choices"]?[0];
            var message = choice?["message"];

            var response = new UnaiChatResponse
            {
                Content = message?["content"]?.ToString() ?? "",
                Role = UnaiRole.Assistant,
                Model = root["model"]?.ToString(),
                FinishReason = choice?["finish_reason"]?.ToString(),
                Usage = ParseUsage(root["usage"])
            };

            if (message?["tool_calls"] is JArray toolCalls && toolCalls.Count > 0)
            {
                response.ToolCalls = new List<UnaiToolCall>();
                foreach (var tc in toolCalls)
                {
                    response.ToolCalls.Add(new UnaiToolCall
                    {
                        Id = tc["id"]?.ToString(),
                        ToolName = tc["function"]?["name"]?.ToString(),
                        ArgumentsJson = tc["function"]?["arguments"]?.ToString()
                    });
                }
            }

            return response;
        }

        protected UnaiUsageInfo ParseUsage(JToken usageToken)
        {
            if (usageToken == null) return null;
            return new UnaiUsageInfo
            {
                PromptTokens = usageToken["prompt_tokens"]?.Value<int>() ?? 0,
                CompletionTokens = usageToken["completion_tokens"]?.Value<int>() ?? 0,
                TotalTokens = usageToken["total_tokens"]?.Value<int>() ?? 0
            };
        }

        protected override ISseLineParser CreateStreamParser()
        {
            return new SseLineParser(
                deltaFactory: (eventType, jsonData) =>
                {
                    var root = JObject.Parse(jsonData);
                    var choice = root["choices"]?[0];
                    var delta = choice?["delta"];
                    string content = delta?["content"]?.ToString() ?? "";
                    string finishReason = choice?["finish_reason"]?.ToString();
                    bool isFinal = !string.IsNullOrEmpty(finishReason) && finishReason != "null";

                    return new UnaiStreamDelta
                    {
                        Content = content,
                        IsFinal = isFinal,
                        FinishReason = isFinal ? finishReason : null,
                        EventType = eventType
                    };
                },
                doneMarker: "[DONE]");
        }
    }
}
