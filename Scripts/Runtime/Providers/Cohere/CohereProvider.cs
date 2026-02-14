using System.Collections.Generic;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers.Cohere
{
    public class CohereProvider : UnaiProviderBase
    {
        public override string ProviderId => "cohere";
        public override string DisplayName => "Cohere";

        public override IReadOnlyList<UnaiModelInfo> KnownModels => new[]
        {
            new UnaiModelInfo { Id = "command-a-03-2025", DisplayName = "Command A", ProviderId = "cohere", MaxContextTokens = 256000 },
            new UnaiModelInfo { Id = "command-r-plus-08-2024", DisplayName = "Command R+", ProviderId = "cohere", MaxContextTokens = 128000 },
            new UnaiModelInfo { Id = "command-r-08-2024", DisplayName = "Command R", ProviderId = "cohere", MaxContextTokens = 128000 },
        };

        protected override string BuildRequestUrl(UnaiChatRequest request)
        {
            return Config.BaseUrl.TrimEnd('/') + "/v2/chat";
        }

        protected override Dictionary<string, string> BuildHeaders()
        {
            return new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = $"Bearer {Config.ResolvedApiKey}",
                ["Accept"] = "text/event-stream"
            };
        }

        protected override string SerializeRequest(UnaiChatRequest request)
        {
            var obj = new JObject
            {
                ["model"] = request.Model,
                ["stream"] = request.Stream,
            };

            var messages = new JArray();
            foreach (var msg in request.Messages)
            {
                string role = msg.Role switch
                {
                    UnaiRole.System => "system",
                    UnaiRole.User => "user",
                    UnaiRole.Assistant => "assistant",
                    _ => "user"
                };
                messages.Add(new JObject
                {
                    ["role"] = role,
                    ["content"] = msg.Content
                });
            }
            obj["messages"] = messages;

            if (request.Options?.Temperature.HasValue == true)
                obj["temperature"] = request.Options.Temperature.Value;
            if (request.Options?.MaxTokens.HasValue == true)
                obj["max_tokens"] = request.Options.MaxTokens.Value;
            if (request.Options?.TopP.HasValue == true)
                obj["p"] = request.Options.TopP.Value;
            if (request.Options?.StopSequences is { Length: > 0 })
                obj["stop_sequences"] = JArray.FromObject(request.Options.StopSequences);

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);
            string content = root["message"]?["content"]?[0]?["text"]?.ToString() ?? "";

            return new UnaiChatResponse
            {
                Content = content,
                Role = UnaiRole.Assistant,
                Model = root["model"]?.ToString(),
                FinishReason = root["finish_reason"]?.ToString(),
                Usage = ParseCohereUsage(root["usage"])
            };
        }

        private UnaiUsageInfo ParseCohereUsage(JToken usage)
        {
            if (usage == null) return null;
            int input = usage["tokens"]?["input_tokens"]?.Value<int>() ?? 0;
            int output = usage["tokens"]?["output_tokens"]?.Value<int>() ?? 0;
            return new UnaiUsageInfo
            {
                PromptTokens = input,
                CompletionTokens = output,
                TotalTokens = input + output
            };
        }

        protected override ISseLineParser CreateStreamParser()
        {
            return new SseLineParser(
                deltaFactory: (eventType, jsonData) =>
                {
                    var root = JObject.Parse(jsonData);
                    string type = root["type"]?.ToString();

                    if (type == "content-delta")
                    {
                        string text = root["delta"]?["message"]?["content"]?["text"]?.ToString() ?? "";
                        return new UnaiStreamDelta
                        {
                            Content = text,
                            EventType = type
                        };
                    }

                    if (type == "message-end")
                    {
                        return new UnaiStreamDelta
                        {
                            Content = "",
                            IsFinal = true,
                            FinishReason = root["delta"]?["finish_reason"]?.ToString(),
                            EventType = type
                        };
                    }

                    return null;
                },
                doneMarker: "__COHERE_NO_DONE__");
        }

        protected override bool ValidateConfig()
        {
            return base.ValidateConfig() && !string.IsNullOrEmpty(Config.ResolvedApiKey);
        }
    }
}
