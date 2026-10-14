using System.Collections.Generic;
using System.Linq;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers.Gemini
{
    public class GeminiProvider : UnaiProviderBase
    {
        public override string ProviderId => "gemini";
        public override string DisplayName => "Google Gemini";
        public override bool SupportsToolCalling => Config?.EnableToolCalling ?? true;
        protected override string ModelsEndpointPath => "/v1beta/models";

        public override IReadOnlyList<UnaiModelInfo> KnownModels => new[]
        {
            new UnaiModelInfo { Id = "gemini-3.8-flash", DisplayName = "Gemini 3.8 Flash", ProviderId = "gemini" },
            new UnaiModelInfo { Id = "gemini-3.5-flash-lite", DisplayName = "Gemini 3.5 Flash-Lite", ProviderId = "gemini" },
            new UnaiModelInfo { Id = "gemini-2.5-flash", DisplayName = "Gemini 2.5 Flash", ProviderId = "gemini", MaxContextTokens = 1048576 },
            new UnaiModelInfo { Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash Lite", ProviderId = "gemini", MaxContextTokens = 1048576 },
            new UnaiModelInfo { Id = "gemini-2.5-pro", DisplayName = "Gemini 2.5 Pro", ProviderId = "gemini", MaxContextTokens = 1048576 },
        };

        protected override string BuildRequestUrl(UnaiChatRequest request)
        {
            string baseUrl = Config.BaseUrl.TrimEnd('/');
            string model = System.Uri.EscapeDataString(request.Model);

            if (request.Stream)
                return $"{baseUrl}/v1beta/models/{model}:streamGenerateContent?alt=sse";
            else
                return $"{baseUrl}/v1beta/models/{model}:generateContent";
        }

        protected override Dictionary<string, string> BuildHeaders()
        {
            return new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["x-goog-api-key"] = Config.ResolvedApiKey
            };
        }

        protected override string SerializeRequest(UnaiChatRequest request)
        {
            var obj = new JObject();
            string systemContent = null;
            var contents = new JArray();

            foreach (var msg in request.Messages)
            {
                if (msg.Role == UnaiRole.System)
                {
                    systemContent = msg.Content;
                    continue;
                }

                if (msg.Role == UnaiRole.Tool)
                {
                    contents.Add(new JObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JArray(new JObject
                        {
                            ["functionResponse"] = new JObject
                            {
                                ["name"] = msg.ToolName,
                                ["response"] = new JObject { ["result"] = msg.Content }
                            }
                        })
                    });
                    continue;
                }

                if (msg.Role == UnaiRole.Assistant && msg.ToolCalls is { Count: > 0 })
                {
                    var originalParts = msg.ToolCalls[0].ProviderMetadata?["geminiParts"] as JArray;
                    if (originalParts != null)
                    {
                        contents.Add(new JObject { ["role"] = "model", ["parts"] = originalParts.DeepClone() });
                        continue;
                    }
                    var parts = new JArray();
                    if (!string.IsNullOrEmpty(msg.Content))
                        parts.Add(new JObject { ["text"] = msg.Content });

                    foreach (var tc in msg.ToolCalls)
                    {
                        parts.Add(new JObject
                        {
                            ["functionCall"] = new JObject
                            {
                                ["name"] = tc.ToolName,
                                ["args"] = JObject.Parse(tc.ArgumentsJson ?? "{}")
                            }
                        });
                    }
                    contents.Add(new JObject { ["role"] = "model", ["parts"] = parts });
                    continue;
                }

                string role = msg.Role == UnaiRole.User ? "user" : "model";
                contents.Add(new JObject
                {
                    ["role"] = role,
                    ["parts"] = new JArray(new JObject { ["text"] = msg.Content })
                });
            }

            obj["contents"] = contents;

            if (systemContent != null)
            {
                obj["systemInstruction"] = new JObject
                {
                    ["parts"] = new JArray(new JObject { ["text"] = systemContent })
                };
            }

            if (request.Tools is { Count: > 0 })
            {
                var declarations = new JArray();
                foreach (var tool in request.Tools)
                {
                    declarations.Add(new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = tool.ParametersSchema ?? new JObject { ["type"] = "object" }
                    });
                }
                obj["tools"] = new JArray(new JObject
                {
                    ["functionDeclarations"] = declarations
                });
            }

            var genConfig = new JObject();
            if (request.Options?.Temperature.HasValue == true)
                genConfig["temperature"] = request.Options.Temperature.Value;
            if (request.Options?.MaxTokens.HasValue == true)
                genConfig["maxOutputTokens"] = request.Options.MaxTokens.Value;
            if (request.Options?.TopP.HasValue == true)
                genConfig["topP"] = request.Options.TopP.Value;
            if (request.Options?.StopSequences is { Length: > 0 })
                genConfig["stopSequences"] = JArray.FromObject(request.Options.StopSequences);

            if (request.Options?.ResponseFormat is UnaiResponseFormat.JsonObject or UnaiResponseFormat.JsonSchema)
            {
                genConfig["responseMimeType"] = "application/json";
                if (request.Options.ResponseFormat == UnaiResponseFormat.JsonSchema && request.Options.JsonSchema != null)
                    genConfig["responseSchema"] = request.Options.JsonSchema;
            }

            if (genConfig.Count > 0)
                obj["generationConfig"] = genConfig;

            return obj.ToString(Formatting.None);
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);
            var candidate = (root["candidates"] as JArray)?.FirstOrDefault();
            var parts = candidate?["content"]?["parts"] as JArray;

            string text = "";
            List<UnaiToolCall> toolCalls = null;

            if (parts != null)
            {
                foreach (var part in parts)
                {
                    if (part["text"] != null)
                    {
                        if (part["thought"]?.Value<bool>() != true) text += part["text"].ToString();
                    }
                    else if (part["functionCall"] is JObject fc)
                    {
                        toolCalls ??= new List<UnaiToolCall>();
                        toolCalls.Add(new UnaiToolCall
                        {
                            Id = $"gemini_{System.Guid.NewGuid():N}".Substring(0, 16),
                            ToolName = fc["name"]?.ToString(),
                            ArgumentsJson = fc["args"]?.ToString(Formatting.None) ?? "{}"
                        });
                    }
                }
            }

            if (toolCalls is { Count: > 0 })
                toolCalls[0].ProviderMetadata = new JObject { ["geminiParts"] = parts.DeepClone() };

            return new UnaiChatResponse
            {
                Content = text,
                Role = UnaiRole.Assistant,
                Model = "",
                FinishReason = candidate?["finishReason"]?.ToString(),
                ToolCalls = toolCalls,
                Usage = ParseGeminiUsage(root["usageMetadata"])
            };
        }

        private UnaiUsageInfo ParseGeminiUsage(JToken meta)
        {
            if (meta == null) return null;
            return new UnaiUsageInfo
            {
                PromptTokens = meta["promptTokenCount"]?.Value<int>() ?? 0,
                CompletionTokens = meta["candidatesTokenCount"]?.Value<int>() ?? 0,
                TotalTokens = meta["totalTokenCount"]?.Value<int>() ?? 0
            };
        }

        protected override ISseLineParser CreateStreamParser()
        {
            var originalParts = new JArray();
            var toolCalls = new List<UnaiToolCall>();
            return new SseLineParser(
                deltaFactory: (eventType, jsonData) =>
                {
                    var root = JObject.Parse(jsonData);
                    var candidate = (root["candidates"] as JArray)?.FirstOrDefault();
                    if (candidate?["content"]?["parts"] is JArray parts)
                        foreach (var part in parts) originalParts.Add(part.DeepClone());
                    var response = DeserializeResponse(jsonData);
                    if (response.ToolCalls != null) toolCalls.AddRange(response.ToolCalls);
                    if (toolCalls.Count > 0)
                        toolCalls[0].ProviderMetadata = new JObject { ["geminiParts"] = originalParts.DeepClone() };

                    return new UnaiStreamDelta
                    {
                        Content = response.Content,
                        IsFinal = response.FinishReason != null,
                        FinishReason = response.FinishReason,
                        ToolCalls = toolCalls.Count > 0 ? new List<UnaiToolCall>(toolCalls) : null,
                        Usage = response.Usage,
                        EventType = eventType
                    };
                },
                doneMarker: "__GEMINI_NO_DONE__");
        }

        protected override bool ValidateConfig()
        {
            return base.ValidateConfig() && !string.IsNullOrEmpty(Config.ResolvedApiKey);
        }
    }
}
