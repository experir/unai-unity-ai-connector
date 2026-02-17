using System;
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
using UnAI.Utilities;

namespace UnAI.Core
{
    public static class UnaiProviderRegistry
    {
        private static readonly Dictionary<string, IUnaiProvider> _providers = new();
        private static readonly Dictionary<string, PendingProvider> _pending = new();
        private static bool _initialized;

        public static void Register(IUnaiProvider provider)
        {
            _providers[provider.ProviderId] = provider;
            _pending.Remove(provider.ProviderId);
        }

        public static IUnaiProvider Get(string providerId)
        {
            if (_providers.TryGetValue(providerId, out var provider))
                return provider;

            if (_pending.TryGetValue(providerId, out var pending))
            {
                var resolved = Resolve(pending);
                if (resolved != null)
                    return resolved;
            }

            return null;
        }

        public static IEnumerable<IUnaiProvider> GetAll()
        {
            ResolveAll();
            return _providers.Values;
        }

        public static bool Has(string providerId) =>
            _providers.ContainsKey(providerId) || _pending.ContainsKey(providerId);

        public static bool Unregister(string providerId)
        {
            _pending.Remove(providerId);
            return _providers.Remove(providerId);
        }

        public static void Clear()
        {
            _providers.Clear();
            _pending.Clear();
            _initialized = false;
        }

        public static void InitializeBuiltInProviders(UnaiGlobalConfig config)
        {
            if (_initialized) return;

            RegisterLazy<OpenAIProvider>(config.OpenAI);
            RegisterLazy<AnthropicProvider>(config.Anthropic);
            RegisterLazy<GeminiProvider>(config.Gemini);
            RegisterLazy<MistralProvider>(config.Mistral);
            RegisterLazy<CohereProvider>(config.Cohere);
            RegisterLazy<OllamaProvider>(config.Ollama);
            RegisterLazy<LMStudioProvider>(config.LMStudio);
            RegisterLazy<LlamaCppProvider>(config.LlamaCpp);
            RegisterLazy<OpenAICompatibleProvider>(config.OpenAICompatible);

            _initialized = true;
        }

        /// <summary>
        /// Registers a provider to be created and initialized lazily on first use.
        /// The provider is only instantiated when Get() or GetAll() is called.
        /// </summary>
        public static void RegisterLazy<T>(UnaiProviderConfig config) where T : IUnaiProvider, new()
        {
            if (!config.Enabled) return;
            _pending[config.ProviderId] = new PendingProvider
            {
                Factory = () => new T(),
                Config = config
            };
        }

        /// <summary>
        /// Returns the number of providers that have been lazily registered but not yet initialized.
        /// </summary>
        public static int PendingCount => _pending.Count;

        /// <summary>
        /// Returns the number of providers that have been fully initialized.
        /// </summary>
        public static int InitializedCount => _providers.Count;

        private static IUnaiProvider Resolve(PendingProvider pending)
        {
            try
            {
                var provider = pending.Factory();
                provider.Initialize(pending.Config);
                _providers[provider.ProviderId] = provider;
                _pending.Remove(provider.ProviderId);
                UnaiLogger.LogVerbose($"[UNAI] Lazy-initialized provider: {provider.ProviderId}");
                return provider;
            }
            catch (Exception ex)
            {
                UnaiLogger.LogError($"[UNAI] Failed to initialize provider '{pending.Config.ProviderId}': {ex.Message}");
                _pending.Remove(pending.Config.ProviderId);
                return null;
            }
        }

        private static void ResolveAll()
        {
            if (_pending.Count == 0) return;

            // Copy keys to avoid modifying during iteration
            var keys = new List<string>(_pending.Keys);
            foreach (var key in keys)
            {
                if (_pending.TryGetValue(key, out var pending))
                    Resolve(pending);
            }
        }

        private class PendingProvider
        {
            public Func<IUnaiProvider> Factory;
            public UnaiProviderConfig Config;
        }
    }
}
