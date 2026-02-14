using UnityEngine;

namespace UnAI.Config
{
    [System.Serializable]
    public class UnaiProviderConfig
    {
        [Tooltip("Unique provider identifier (e.g. 'openai', 'anthropic')")]
        public string ProviderId;

        [Tooltip("Whether this provider is enabled")]
        public bool Enabled = true;

        [Tooltip("Base URL for the provider's API. Leave empty for default.")]
        public string BaseUrl;

        [Tooltip("API key for editor/development use. For production, use environment variables.")]
        public string ApiKey;

        [Tooltip("Name of the environment variable that holds the API key at runtime.")]
        public string ApiKeyEnvironmentVariable;

        [Tooltip("Default model to use if not specified in request.")]
        public string DefaultModel;

        [Tooltip("Request timeout in seconds.")]
        public int TimeoutSeconds = 120;

        [Tooltip("Maximum retry attempts for retryable errors.")]
        public int MaxRetries = 3;

        [Tooltip("Custom headers to include with every request.")]
        public SerializableKeyValuePair[] CustomHeaders;

        public string ResolvedApiKey =>
            UnaiEnvironment.GetApiKey(ApiKeyEnvironmentVariable, ApiKey);
    }

    [System.Serializable]
    public struct SerializableKeyValuePair
    {
        public string Key;
        public string Value;
    }
}
