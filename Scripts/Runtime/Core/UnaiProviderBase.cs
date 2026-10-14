using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Config;
using UnAI.Http;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Utilities;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace UnAI.Core
{
    public abstract class UnaiProviderBase : IUnaiProvider, IUnaiModelProvider
    {
        public abstract string ProviderId { get; }
        public abstract string DisplayName { get; }
        public virtual IReadOnlyList<UnaiModelInfo> KnownModels => Array.Empty<UnaiModelInfo>();
        public virtual bool SupportsToolCalling => false;
        protected virtual string ModelsEndpointPath => null;
        public bool SupportsModelDiscovery => ModelsEndpointPath != null;

        protected UnaiProviderConfig Config { get; private set; }

        public bool IsConfigured => Config != null && ValidateConfig();

        public virtual void Initialize(UnaiProviderConfig config)
        {
            Config = config;
        }

        protected virtual bool ValidateConfig()
        {
            return !string.IsNullOrEmpty(Config.BaseUrl);
        }

        protected abstract string BuildRequestUrl(UnaiChatRequest request);
        protected abstract Dictionary<string, string> BuildHeaders();
        protected abstract string SerializeRequest(UnaiChatRequest request);
        protected abstract UnaiChatResponse DeserializeResponse(string json);
        protected abstract ISseLineParser CreateStreamParser();

        protected string BuildEndpointUrl(string path)
        {
            string baseUrl = Config.BaseUrl.TrimEnd('/');
            if (baseUrl.EndsWith("/v1", StringComparison.Ordinal) && path.StartsWith("/v1/", StringComparison.Ordinal))
                path = path.Substring(3);
            return baseUrl + path;
        }

        public async Task<IReadOnlyList<UnaiModelInfo>> ListModelsAsync(CancellationToken ct = default)
        {
            if (!SupportsModelDiscovery) throw new NotSupportedException($"'{ProviderId}' does not implement model discovery.");
            if (!IsConfigured) throw new InvalidOperationException($"Configure '{ProviderId}' before listing models.");
            var result = new List<UnaiModelInfo>();
            string url = BuildEndpointUrl(ModelsEndpointPath);
            var visited = new HashSet<string>();
            while (url != null)
            {
                ct.ThrowIfCancellationRequested();
                if (!visited.Add(url) || visited.Count > 100) throw new FormatException("Model API exceeded pagination limits or repeated a cursor.");
                var response = await UnaiRetryPolicy.ExecuteAsync(
                    () => UnaiHttpClient.GetAsync(url, BuildHeaders(), ct, Config.TimeoutSeconds), Config.MaxRetries, ct);
                if (response.error != null)
                {
                    response.error.ProviderId = ProviderId;
                    throw new UnaiRequestException(response.error);
                }
                var root = JObject.Parse(response.body);
                var entries = root["data"] as JArray ?? root["models"] as JArray
                    ?? throw new FormatException("Model API response has no data/models array.");
                foreach (var entry in entries)
                {
                    if (ProviderId == "gemini" && entry["supportedGenerationMethods"] is JArray methods
                        && !methods.Any(m => m.ToString() == "generateContent")) continue;
                    string id = entry["id"]?.ToString() ?? entry["model"]?.ToString() ?? entry["name"]?.ToString();
                    if (string.IsNullOrEmpty(id)) continue;
                    if (ProviderId == "gemini" && id.StartsWith("models/", StringComparison.Ordinal)) id = id.Substring(7);
                    result.Add(new UnaiModelInfo
                    {
                        Id = id, DisplayName = entry["display_name"]?.ToString() ?? entry["displayName"]?.ToString() ?? id,
                        ProviderId = ProviderId, MaxContextTokens = entry["inputTokenLimit"]?.Value<int>()
                    });
                }
                string page = root["nextPageToken"]?.ToString();
                string after = root["has_more"]?.Value<bool>() == true ? root["last_id"]?.ToString() : null;
                if (root["has_more"]?.Value<bool>() == true && string.IsNullOrEmpty(after))
                    throw new FormatException("Model API has more pages but no cursor.");
                url = !string.IsNullOrEmpty(page) ? BuildEndpointUrl(ModelsEndpointPath) + "?pageToken=" + Uri.EscapeDataString(page)
                    : !string.IsNullOrEmpty(after) ? BuildEndpointUrl(ModelsEndpointPath) + "?after_id=" + Uri.EscapeDataString(after) : null;
            }
            return result.GroupBy(m => m.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        }

        public virtual async Task<UnaiChatResponse> ChatAsync(
            UnaiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);
            cancellationToken.ThrowIfCancellationRequested();
            request = request.Clone();
            request.Stream = false;
            ResolveModel(request);

            string url = BuildRequestUrl(request);
            string body = SerializeRequest(request);
            var headers = BuildHeaders();

            var (responseBody, statusCode, error) = await UnaiRetryPolicy.ExecuteAsync(
                () => UnaiHttpClient.PostAsync(url, body, headers, cancellationToken, Config.TimeoutSeconds),
                Config.MaxRetries,
                cancellationToken);

            if (error != null)
            {
                error.ProviderId = ProviderId;
                throw new UnaiRequestException(error);
            }

            var response = DeserializeResponse(responseBody);
            response.ProviderId = ProviderId;
            response.RawResponse = responseBody;
            return response;
        }

        public virtual async Task ChatStreamAsync(
            UnaiChatRequest request,
            Action<UnaiStreamDelta> onDelta,
            Action<UnaiChatResponse> onComplete = null,
            Action<UnaiErrorInfo> onError = null,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);
            cancellationToken.ThrowIfCancellationRequested();
            request = request.Clone();
            request.Stream = true;
            ResolveModel(request);

            string url = BuildRequestUrl(request);
            string body = SerializeRequest(request);
            var headers = BuildHeaders();

            int maxRetries = Config.MaxRetries;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var parser = CreateStreamParser();
                UnaiChatResponse finalResponse = null;
                UnaiErrorInfo streamError = null;
                bool emittedOutput = false;
                finalResponse = new UnaiChatResponse
                {
                    Content = "", Model = request.Model, ProviderId = ProviderId, WasStreamed = true
                };

                await UnaiHttpStreamClient.StreamPostAsync(
                    url, body, headers, parser,
                    onDelta: delta =>
                    {
                        if (!string.IsNullOrEmpty(delta.Content) || delta.ToolCalls is { Count: > 0 })
                            emittedOutput = true;
                        finalResponse.Content = delta.AccumulatedContent ?? finalResponse.Content;
                        finalResponse.FinishReason = delta.FinishReason ?? finalResponse.FinishReason;
                        finalResponse.ToolCalls = delta.ToolCalls ?? finalResponse.ToolCalls;
                        finalResponse.Usage = delta.Usage ?? finalResponse.Usage;
                        onDelta?.Invoke(delta);
                    },
                    onComplete: () =>
                    {
                        onComplete?.Invoke(finalResponse);
                    },
                    onError: error =>
                    {
                        error.ProviderId = ProviderId;
                        error.HasPartialOutput = emittedOutput;
                        streamError = error;
                    },
                    cancellationToken, Config.TimeoutSeconds);

                // If no error, we're done
                if (streamError == null)
                    return;

                // If the error is not retryable or we've exhausted retries, report it
                if (!streamError.IsRetryable || emittedOutput || attempt >= maxRetries)
                {
                    onError?.Invoke(streamError);
                    return;
                }

                // Use Retry-After header if available, otherwise exponential backoff
                float delaySeconds;
                if (streamError.RetryAfterSeconds.HasValue)
                {
                    delaySeconds = streamError.RetryAfterSeconds.Value + UnityEngine.Random.Range(0.1f, 1f);
                }
                else if (streamError.ErrorType == UnaiErrorType.RateLimit)
                {
                    // Rate limits need longer waits (5s, 10s, 20s base)
                    delaySeconds = 5f * MathF.Pow(2, attempt) + UnityEngine.Random.Range(0f, 2f);
                }
                else
                {
                    delaySeconds = MathF.Pow(2, attempt) + UnityEngine.Random.Range(0f, 0.5f);
                }
                UnaiLogger.Log($"[UNAI] Retrying streaming request (attempt {attempt + 1}/{maxRetries}) after {delaySeconds:F1}s: {streamError.Message}");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        protected void ResolveModel(UnaiChatRequest request)
        {
            if (string.IsNullOrEmpty(request.Model))
                request.Model = Config.DefaultModel;
        }

        protected void ValidateRequest(UnaiChatRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Messages == null || request.Messages.Count == 0)
                throw new ArgumentException("At least one chat message is required.", nameof(request));
            if (!IsConfigured || !Config.Enabled)
                throw new InvalidOperationException($"Provider '{ProviderId}' is not configured or enabled. Check its base URL and API key.");
            if (request.Tools is { Count: > 0 } && !SupportsToolCalling)
                throw new NotSupportedException($"'{ProviderId}' is configured without native tool calling.");
            if (request.Options?.ResponseFormat == UnaiResponseFormat.JsonSchema)
            {
                if (!Config.EnableJsonSchema) throw new NotSupportedException("JSON Schema is disabled for this provider.");
                if (request.Options.JsonSchema == null) throw new ArgumentException("JsonSchema output requires a schema.");
            }
        }
    }
}
