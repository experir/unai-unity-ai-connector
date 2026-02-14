using System.Collections.Generic;
using UnAI.Models;

namespace UnAI.Providers.OpenAI
{
    public class OpenAIProvider : OpenAICompatibleBase
    {
        public override string ProviderId => "openai";
        public override string DisplayName => "OpenAI";

        public override IReadOnlyList<UnaiModelInfo> KnownModels => new[]
        {
            new UnaiModelInfo { Id = "gpt-5.2", DisplayName = "GPT-5.2", ProviderId = "openai", MaxContextTokens = 200000 },
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
