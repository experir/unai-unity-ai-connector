using System.Collections.Generic;
using UnAI.Models;
using Newtonsoft.Json.Linq;
using System;

namespace UnAI.Providers.OpenAI
{
    public class OpenAIProvider : OpenAICompatibleBase
    {
        public override string ProviderId => "openai";
        public override string DisplayName => "OpenAI";
        protected override bool RequestStreamingUsage => true;

        protected override string SerializeRequest(UnaiChatRequest request)
        {
            var root = JObject.Parse(base.SerializeRequest(request));
            if (root.TryGetValue("max_tokens", out var maximum))
            {
                if (root["max_completion_tokens"] == null) root["max_completion_tokens"] = maximum;
                root.Remove("max_tokens");
            }
            bool reasoning = request.Model?.StartsWith("o", StringComparison.Ordinal) == true
                || request.Model?.StartsWith("gpt-5", StringComparison.Ordinal) == true
                || request.Model?.StartsWith("gpt-6", StringComparison.Ordinal) == true;
            string effort = root["reasoning_effort"]?.ToString();
            bool defaultNone = request.Model?.StartsWith("gpt-5.2", StringComparison.Ordinal) == true && effort == null;
            if (reasoning && !defaultNone && effort != "none"
                && (request.Options?.Temperature != null || request.Options?.TopP != null))
                throw new ArgumentException("Sampling options require a model/effort combination that supports them. Omit Temperature/TopP for reasoning models, or select supported reasoning_effort through ExtraParameters.");
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        public override IReadOnlyList<UnaiModelInfo> KnownModels => new[]
        {
            new UnaiModelInfo { Id = "gpt-5.2", DisplayName = "GPT-5.2", ProviderId = "openai", MaxContextTokens = 400000 },
            new UnaiModelInfo { Id = "gpt-5", DisplayName = "GPT-5", ProviderId = "openai", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "gpt-5-mini", DisplayName = "GPT-5 Mini", ProviderId = "openai", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "gpt-5-nano", DisplayName = "GPT-5 Nano", ProviderId = "openai", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "o3-mini", DisplayName = "o3-mini", ProviderId = "openai", MaxContextTokens = 200000 },
        };

        protected override bool ValidateConfig()
        {
            return base.ValidateConfig() && !string.IsNullOrEmpty(Config.ResolvedApiKey);
        }
    }
}
