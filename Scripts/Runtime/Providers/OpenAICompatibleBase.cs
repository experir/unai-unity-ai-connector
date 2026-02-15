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

            // Request usage stats in streaming responses
            if (request.Stream)
            {
                obj["stream_options"] = new JObject { ["include_usage"] = true };
            }

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
            // Tool call accumulation state across streaming deltas.
            // OpenAI streams tool calls in chunks keyed by index — we must
            // accumulate id, name, and argument fragments before emitting
            // the completed list on the final delta.
            var toolCallAccum = new Dictionary<int, (string id, string name, System.Text.StringBuilder args)>();

            return new SseLineParser(
                deltaFactory: (eventType, jsonData) =>
                {
                    var root = JObject.Parse(jsonData);
                    var choice = root["choices"]?[0];
                    var delta = choice?["delta"];
                    string content = delta?["content"]?.ToString() ?? "";
                    string finishReason = choice?["finish_reason"]?.ToString();
                    bool isFinal = !string.IsNullOrEmpty(finishReason) && finishReason != "null";

                    // Accumulate streamed tool_calls chunks
                    if (delta?["tool_calls"] is JArray tcArray)
                    {
                        foreach (var tc in tcArray)
                        {
                            int idx = tc["index"]?.Value<int>() ?? 0;
                            string id = tc["id"]?.ToString();
                            string name = tc["function"]?["name"]?.ToString();
                            string argChunk = tc["function"]?["arguments"]?.ToString();

                            if (!toolCallAccum.TryGetValue(idx, out var entry))
                            {
                                entry = (id, name, new System.Text.StringBuilder());
                                toolCallAccum[idx] = entry;
                            }
                            else
                            {
                                // Update id/name if provided in a later chunk (shouldn't happen, but be safe)
                                if (!string.IsNullOrEmpty(id)) entry.id = id;
                                if (!string.IsNullOrEmpty(name)) entry.name = name;
                                toolCallAccum[idx] = entry;
                            }

                            if (!string.IsNullOrEmpty(argChunk))
                                toolCallAccum[idx].args.Append(argChunk);
                        }
                    }

                    // Parse usage if present (available when stream_options.include_usage = true)
                    UnaiUsageInfo usage = null;
                    if (root["usage"] is JObject usageObj && usageObj.HasValues)
                    {
                        usage = new UnaiUsageInfo
                        {
                            PromptTokens = usageObj["prompt_tokens"]?.Value<int>() ?? 0,
                            CompletionTokens = usageObj["completion_tokens"]?.Value<int>() ?? 0,
                            TotalTokens = usageObj["total_tokens"]?.Value<int>() ?? 0
                        };
                    }

                    var streamDelta = new UnaiStreamDelta
                    {
                        Content = content,
                        IsFinal = isFinal,
                        FinishReason = isFinal ? finishReason : null,
                        EventType = eventType,
                        Usage = usage
                    };

                    // When the stream is done and we accumulated tool calls, attach them
                    if (isFinal && toolCallAccum.Count > 0)
                    {
                        streamDelta.ToolCalls = new List<UnaiToolCall>();
                        foreach (var kvp in toolCallAccum)
                        {
                            streamDelta.ToolCalls.Add(new UnaiToolCall
                            {
                                Id = kvp.Value.id,
                                ToolName = kvp.Value.name,
                                ArgumentsJson = kvp.Value.args.ToString()
                            });
                        }
                    }

                    return streamDelta;
                },
                doneMarker: "[DONE]");
        }
    }
}
