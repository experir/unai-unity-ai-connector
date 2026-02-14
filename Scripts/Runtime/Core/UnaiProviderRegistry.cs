using System.Collections.Generic;
using UnAI.Config;
using UnAI.Providers.Anthropic;
using UnAI.Providers.Cohere;
using UnAI.Providers.Gemini;
using UnAI.Providers.LlamaCpp;
using UnAI.Providers.LMStudio;
using UnAI.Providers.Mistral;
using UnAI.Providers.Ollama;
using UnAI.Providers.OpenAI;
using UnAI.Providers.OpenAICompatible;

namespace UnAI.Core
{
    public static class UnaiProviderRegistry
    {
        private static readonly Dictionary<string, IUnaiProvider> _providers = new();
        private static bool _initialized;

        public static void Register(IUnaiProvider provider)
        {
            _providers[provider.ProviderId] = provider;
        }

        public static IUnaiProvider Get(string providerId)
        {
            return _providers.TryGetValue(providerId, out var provider) ? provider : null;
        }

        public static IEnumerable<IUnaiProvider> GetAll() => _providers.Values;

        public static bool Has(string providerId) => _providers.ContainsKey(providerId);

        public static bool Unregister(string providerId) => _providers.Remove(providerId);

        public static void Clear()
        {
            _providers.Clear();
            _initialized = false;
        }

        public static void InitializeBuiltInProviders(UnaiGlobalConfig config)
        {
            if (_initialized) return;

            RegisterIfEnabled(new OpenAIProvider(), config.OpenAI);
            RegisterIfEnabled(new AnthropicProvider(), config.Anthropic);
            RegisterIfEnabled(new GeminiProvider(), config.Gemini);
            RegisterIfEnabled(new MistralProvider(), config.Mistral);
            RegisterIfEnabled(new CohereProvider(), config.Cohere);
            RegisterIfEnabled(new OllamaProvider(), config.Ollama);
            RegisterIfEnabled(new LMStudioProvider(), config.LMStudio);
            RegisterIfEnabled(new LlamaCppProvider(), config.LlamaCpp);
            RegisterIfEnabled(new OpenAICompatibleProvider(), config.OpenAICompatible);

            _initialized = true;
        }

        private static void RegisterIfEnabled(IUnaiProvider provider, UnaiProviderConfig config)
        {
            if (config.Enabled)
            {
                provider.Initialize(config);
                Register(provider);
            }
        }
    }
}
