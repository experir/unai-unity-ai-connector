using System.Collections.Generic;
using UnAI.Models;

namespace UnAI.Providers.Mistral
{
    public class MistralProvider : OpenAICompatibleBase
    {
        public override string ProviderId => "mistral";
        public override string DisplayName => "Mistral AI";

        public override IReadOnlyList<UnaiModelInfo> KnownModels => new[]
        {
            new UnaiModelInfo { Id = "mistral-large-latest", DisplayName = "Mistral Large", ProviderId = "mistral", MaxContextTokens = 128000 },
            new UnaiModelInfo { Id = "mistral-small-latest", DisplayName = "Mistral Small", ProviderId = "mistral", MaxContextTokens = 128000 },
            new UnaiModelInfo { Id = "codestral-latest", DisplayName = "Codestral", ProviderId = "mistral", MaxContextTokens = 256000 },
        };

        protected override bool ValidateConfig()
        {
            return base.ValidateConfig() && !string.IsNullOrEmpty(Config.ResolvedApiKey);
        }
    }
}
