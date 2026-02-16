using System.Collections.Generic;
using UnityEngine;

namespace UnAI.Config
{
    [CreateAssetMenu(fileName = "UnaiGlobalConfig", menuName = "UnAI/Global Configuration", order = 1)]
    public class UnaiGlobalConfig : ScriptableObject
    {
        [Header("General Settings")]
        [Tooltip("The default provider ID to use when none is specified.")]
        public string DefaultProviderId = "openai";

        [Tooltip("Enable verbose debug logging.")]
        public bool DebugLogging;

        [Tooltip("Ordered list of provider IDs to try when the active provider fails " +
                 "(rate limit, server error, network error). Leave empty to disable fallback.")]
        public List<string> FallbackProviderIds = new();

        [Header("Cloud Providers")]
        public UnaiProviderConfig OpenAI = new()
        {
            ProviderId = "openai",
            BaseUrl = "https://api.openai.com",
            ApiKeyEnvironmentVariable = "OPENAI_API_KEY",
            DefaultModel = "gpt-5.2"
        };

        public UnaiProviderConfig Anthropic = new()
        {
            ProviderId = "anthropic",
            BaseUrl = "https://api.anthropic.com",
            ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY",
            DefaultModel = "claude-sonnet-4-5-20250929"
        };

        public UnaiProviderConfig Gemini = new()
        {
            ProviderId = "gemini",
            BaseUrl = "https://generativelanguage.googleapis.com",
            ApiKeyEnvironmentVariable = "GEMINI_API_KEY",
            DefaultModel = "gemini-2.5-flash"
        };

        public UnaiProviderConfig Mistral = new()
        {
            ProviderId = "mistral",
            BaseUrl = "https://api.mistral.ai",
            ApiKeyEnvironmentVariable = "MISTRAL_API_KEY",
            DefaultModel = "mistral-large-latest"
        };

        public UnaiProviderConfig Cohere = new()
        {
            ProviderId = "cohere",
            BaseUrl = "https://api.cohere.com",
            ApiKeyEnvironmentVariable = "COHERE_API_KEY",
            DefaultModel = "command-a-03-2025"
        };

        [Header("Local Providers")]
        public UnaiProviderConfig Ollama = new()
        {
            ProviderId = "ollama",
            BaseUrl = "http://localhost:11434",
            DefaultModel = "llama3.2"
        };

        public UnaiProviderConfig LMStudio = new()
        {
            ProviderId = "lmstudio",
            BaseUrl = "http://localhost:1234",
            DefaultModel = ""
        };

        public UnaiProviderConfig LlamaCpp = new()
        {
            ProviderId = "llamacpp",
            BaseUrl = "http://localhost:8080",
            DefaultModel = ""
        };

        [Header("Custom")]
        public UnaiProviderConfig OpenAICompatible = new()
        {
            ProviderId = "openai-compatible",
            BaseUrl = "",
            DefaultModel = ""
        };

        public IEnumerable<UnaiProviderConfig> AllProviders()
        {
            yield return OpenAI;
            yield return Anthropic;
            yield return Gemini;
            yield return Mistral;
            yield return Cohere;
            yield return Ollama;
            yield return LMStudio;
            yield return LlamaCpp;
            yield return OpenAICompatible;
        }

        public UnaiProviderConfig GetProviderConfig(string providerId)
        {
            foreach (var config in AllProviders())
            {
                if (config.ProviderId == providerId)
                    return config;
            }
            return null;
        }
    }
}
