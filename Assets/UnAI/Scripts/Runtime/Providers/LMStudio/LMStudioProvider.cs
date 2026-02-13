namespace UnAI.Providers.LMStudio
{
    public class LMStudioProvider : OpenAICompatibleBase
    {
        public override string ProviderId => "lmstudio";
        public override string DisplayName => "LM Studio (Local)";

        protected override bool ValidateConfig() => !string.IsNullOrEmpty(Config.BaseUrl);
    }
}
