using System.Collections.Generic;
using System.Linq;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers.Anthropic
{
    public class AnthropicProvider : UnaiProviderBase
    {
        public override string ProviderId => "anthropic";
        public override string DisplayName => "Anthropic Claude";

        private const string AnthropicVersion = "2023-06-01";

        public override IReadOnlyList<UnaiModelInfo> KnownModels => new[]
        {
            new UnaiModelInfo { Id = "claude-sonnet-4-20250514", DisplayName = "Claude Sonnet 4", ProviderId = "anthropic", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "claude-opus-4-20250514", DisplayName = "Claude Opus 4", ProviderId = "anthropic", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "claude-3-5-haiku-20241022", DisplayName = "Claude 3.5 Haiku", ProviderId = "anthropic", MaxContextTokens = 200000 },
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
            if (systemMsg != null)
                obj["system"] = systemMsg.Content;

            var messages = new JArray();
            foreach (var msg in request.Messages.Where(m => m.Role != UnaiRole.System))
            {
                messages.Add(new JObject
                {
                    ["role"] = msg.Role == UnaiRole.User ? "user" : "assistant",
                    ["content"] = msg.Content
                });
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

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);

            string content = "";
            if (root["content"] is JArray contentArray)
            {
                foreach (var block in contentArray)
                {
                    if (block["type"]?.ToString() == "text")
                        content += block["text"]?.ToString() ?? "";
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
            return new SseLineParser(
                deltaFactory: (eventType, jsonData) =>
                {
                    var root = JObject.Parse(jsonData);
                    string type = root["type"]?.ToString();

                    return type switch
                    {
                        "content_block_delta" => new UnaiStreamDelta
                        {
                            Content = root["delta"]?["text"]?.ToString() ?? "",
                            EventType = type
                        },
                        "message_delta" => new UnaiStreamDelta
                        {
                            Content = "",
                            IsFinal = true,
                            FinishReason = root["delta"]?["stop_reason"]?.ToString(),
                            EventType = type
                        },
                        "message_stop" => new UnaiStreamDelta
                        {
                            Content = "",
                            IsFinal = true,
                            EventType = type
                        },
                        _ => null
                    };
                },
                doneMarker: "__ANTHROPIC_NO_DONE__");
        }

        protected override bool ValidateConfig()
        {
            return base.ValidateConfig() && !string.IsNullOrEmpty(Config.ResolvedApiKey);
        }
    }
}
