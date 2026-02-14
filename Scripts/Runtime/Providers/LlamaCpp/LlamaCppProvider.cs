namespace UnAI.Providers.LlamaCpp
{
    public class LlamaCppProvider : OpenAICompatibleBase
    {
        public override string ProviderId => "llamacpp";
        public override string DisplayName => "llama.cpp (Local)";

        protected override bool ValidateConfig() => !string.IsNullOrEmpty(Config.BaseUrl);
    }
}
