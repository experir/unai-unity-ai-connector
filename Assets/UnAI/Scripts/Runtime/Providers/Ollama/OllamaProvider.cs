using System.Collections.Generic;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers.Ollama
{
    public class OllamaProvider : UnaiProviderBase
    {
        public override string ProviderId => "ollama";
        public override string DisplayName => "Ollama (Local)";

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
                messages.Add(new JObject
                {
                    ["role"] = msg.Role.ToString().ToLowerInvariant(),
                    ["content"] = msg.Content
                });
            }
            obj["messages"] = messages;

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

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);
            return new UnaiChatResponse
            {
                Content = root["message"]?["content"]?.ToString() ?? "",
                Role = UnaiRole.Assistant,
                Model = root["model"]?.ToString(),
                FinishReason = root["done"]?.Value<bool>() == true ? "stop" : null
            };
        }

        protected override ISseLineParser CreateStreamParser()
        {
            return new NdjsonLineParser(jsonLine =>
            {
                var root = JObject.Parse(jsonLine);
                bool done = root["done"]?.Value<bool>() ?? false;
                string content = root["message"]?["content"]?.ToString() ?? "";

                return new UnaiStreamDelta
                {
                    Content = content,
                    IsFinal = done,
                    FinishReason = done ? "stop" : null,
                    EventType = done ? "done" : "delta"
                };
            });
        }

        protected override bool ValidateConfig()
        {
            return !string.IsNullOrEmpty(Config.BaseUrl);
        }
    }
}
