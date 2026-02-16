using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Response cache for non-streaming requests. Shared across all providers.
        /// Use request.UseCache = true to opt in per request.
        /// </summary>
        public UnaiResponseCache Cache { get; } = new();

        public IUnaiProvider ActiveProvider =>
            UnaiProviderRegistry.Get(_activeProviderId ?? _config?.DefaultProviderId);

        /// <summary>
        /// Fired when a request fails on one provider and is retried on a fallback.
        /// Provides the failed provider ID, the fallback provider ID, and the error.
        /// </summary>
        public event Action<UnaiProviderFallbackArgs> OnProviderFallback;

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

        public async Task<UnaiChatResponse> ChatAsync(
            UnaiChatRequest request,
            CancellationToken ct = default)
        {
            var provider = ActiveProvider;

            // Cache check (before fallback — a cache hit skips everything)
            if (request.UseCache)
            {
                string key = UnaiResponseCache.BuildKey(provider.ProviderId, request);
                if (Cache.TryGet(key, out var cached))
                {
                    UnaiLogger.LogVerbose($"[UNAI] Cache hit for request (key={key[..8]}...)");
                    return cached;
                }
            }

            // Try active provider, then fallbacks
            var providersToTry = BuildProviderChain(provider);

            UnaiRequestException lastException = null;
            foreach (var p in providersToTry)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var response = await p.ChatAsync(request, ct);

                    if (request.UseCache)
                    {
                        string key = UnaiResponseCache.BuildKey(p.ProviderId, request);
                        Cache.Put(key, response);
                    }

                    return response;
                }
                catch (UnaiRequestException ex) when (ex.ErrorInfo.IsRetryable && p != providersToTry[^1])
                {
                    lastException = ex;
                    var nextIdx = providersToTry.IndexOf(p) + 1;
                    var next = providersToTry[nextIdx];
                    UnaiLogger.Log($"[UNAI] Provider '{p.ProviderId}' failed ({ex.ErrorInfo.ErrorType}). " +
                                   $"Falling back to '{next.ProviderId}'.");
                    OnProviderFallback?.Invoke(new UnaiProviderFallbackArgs
                    {
                        FailedProviderId = p.ProviderId,
                        FallbackProviderId = next.ProviderId,
                        Error = ex.ErrorInfo
                    });
                }
            }

            // All providers failed — throw the last error
            throw lastException!;
        }

        public async Task ChatStreamAsync(
            UnaiChatRequest request,
            Action<UnaiStreamDelta> onDelta,
            Action<UnaiChatResponse> onComplete = null,
            Action<UnaiErrorInfo> onError = null,
            CancellationToken ct = default)
        {
            var provider = ActiveProvider;
            var providersToTry = BuildProviderChain(provider);

            for (int i = 0; i < providersToTry.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = providersToTry[i];
                bool failed = false;
                UnaiErrorInfo streamError = null;

                await p.ChatStreamAsync(
                    request, onDelta,
                    onComplete: response => onComplete?.Invoke(response),
                    onError: error =>
                    {
                        if (error.IsRetryable && i < providersToTry.Count - 1)
                        {
                            failed = true;
                            streamError = error;
                        }
                        else
                        {
                            onError?.Invoke(error);
                        }
                    },
                    ct);

                if (!failed) return;

                var next = providersToTry[i + 1];
                UnaiLogger.Log($"[UNAI] Streaming provider '{p.ProviderId}' failed ({streamError.ErrorType}). " +
                               $"Falling back to '{next.ProviderId}'.");
                OnProviderFallback?.Invoke(new UnaiProviderFallbackArgs
                {
                    FailedProviderId = p.ProviderId,
                    FallbackProviderId = next.ProviderId,
                    Error = streamError
                });
            }
        }

        public async Task<string> QuickChatAsync(
            string userMessage,
            string systemPrompt = null,
            bool useCache = false,
            CancellationToken ct = default)
        {
            var request = new UnaiChatRequest { UseCache = useCache };
            if (!string.IsNullOrEmpty(systemPrompt))
                request.Messages.Add(UnaiChatMessage.System(systemPrompt));
            request.Messages.Add(UnaiChatMessage.User(userMessage));

            var response = await ChatAsync(request, ct);
            return response.Content;
        }

        private List<IUnaiProvider> BuildProviderChain(IUnaiProvider primary)
        {
            var chain = new List<IUnaiProvider> { primary };

            if (_config?.FallbackProviderIds == null) return chain;

            foreach (var id in _config.FallbackProviderIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (id == primary.ProviderId) continue;
                var fallback = UnaiProviderRegistry.Get(id);
                if (fallback != null && fallback.IsConfigured)
                    chain.Add(fallback);
            }

            return chain;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }

    public class UnaiProviderFallbackArgs
    {
        public string FailedProviderId { get; set; }
        public string FallbackProviderId { get; set; }
        public UnaiErrorInfo Error { get; set; }
    }
}
