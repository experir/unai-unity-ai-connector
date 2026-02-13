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
            new UnaiModelInfo { Id = "gpt-4o", DisplayName = "GPT-4o", ProviderId = "openai", MaxContextTokens = 128000 },
            new UnaiModelInfo { Id = "gpt-4o-mini", DisplayName = "GPT-4o Mini", ProviderId = "openai", MaxContextTokens = 128000 },
            new UnaiModelInfo { Id = "gpt-4-turbo", DisplayName = "GPT-4 Turbo", ProviderId = "openai", MaxContextTokens = 128000 },
            new UnaiModelInfo { Id = "o1", DisplayName = "o1", ProviderId = "openai", MaxContextTokens = 200000 },
            new UnaiModelInfo { Id = "o3-mini", DisplayName = "o3-mini", ProviderId = "openai", MaxContextTokens = 200000 },
        };

        protected override bool ValidateConfig()
        {
            return base.ValidateConfig() && !string.IsNullOrEmpty(Config.ResolvedApiKey);
        }
    }
}
