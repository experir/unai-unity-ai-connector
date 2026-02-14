using System;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Config;
using UnAI.Models;
using UnAI.Utilities;
using UnityEngine;

namespace UnAI.Core
{
    public class UnaiManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private UnaiGlobalConfig _config;

        [Header("Runtime State")]
        [SerializeField] private string _activeProviderId;

        public static UnaiManager Instance { get; private set; }

        public UnaiGlobalConfig Config => _config;

        public IUnaiProvider ActiveProvider =>
            UnaiProviderRegistry.Get(_activeProviderId ?? _config?.DefaultProviderId);

        /// <summary>
        /// Initialize with a config at runtime (e.g. from code without inspector assignment).
        /// </summary>
        public void Initialize(UnaiGlobalConfig config)
        {
            _config = config;
            UnaiLogger.VerboseEnabled = _config.DebugLogging;
            UnaiProviderRegistry.InitializeBuiltInProviders(_config);
            _activeProviderId = _config.DefaultProviderId;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Config may be null if Initialize() will be called from code
            if (_config != null)
                Initialize(_config);
        }

        public void SetActiveProvider(string providerId)
        {
            if (!UnaiProviderRegistry.Has(providerId))
            {
                UnaiLogger.LogError($"[UNAI] Provider '{providerId}' is not registered.");
                return;
            }
            _activeProviderId = providerId;
            UnaiLogger.LogVerbose($"[UNAI] Active provider set to: {providerId}");
        }

        public Task<UnaiChatResponse> ChatAsync(
            UnaiChatRequest request,
            CancellationToken ct = default)
        {
            return ActiveProvider.ChatAsync(request, ct);
        }

        public Task ChatStreamAsync(
            UnaiChatRequest request,
            Action<UnaiStreamDelta> onDelta,
            Action<UnaiChatResponse> onComplete = null,
            Action<UnaiErrorInfo> onError = null,
            CancellationToken ct = default)
        {
            return ActiveProvider.ChatStreamAsync(request, onDelta, onComplete, onError, ct);
        }

        public async Task<string> QuickChatAsync(
            string userMessage,
            string systemPrompt = null,
            CancellationToken ct = default)
        {
            var request = new UnaiChatRequest();
            if (!string.IsNullOrEmpty(systemPrompt))
                request.Messages.Add(UnaiChatMessage.System(systemPrompt));
            request.Messages.Add(UnaiChatMessage.User(userMessage));

            var response = await ActiveProvider.ChatAsync(request, ct);
            return response.Content;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
