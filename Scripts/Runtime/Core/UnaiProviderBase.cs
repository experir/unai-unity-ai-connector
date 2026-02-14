using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Config;
using UnAI.Http;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Utilities;

namespace UnAI.Core
{
    public abstract class UnaiProviderBase : IUnaiProvider
    {
        public abstract string ProviderId { get; }
        public abstract string DisplayName { get; }
        public virtual IReadOnlyList<UnaiModelInfo> KnownModels => Array.Empty<UnaiModelInfo>();
        public virtual bool SupportsToolCalling => false;

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

        public virtual async Task<UnaiChatResponse> ChatAsync(
            UnaiChatRequest request,
            CancellationToken cancellationToken = default)
        {
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
            request.Stream = true;
            ResolveModel(request);

            string url = BuildRequestUrl(request);
            string body = SerializeRequest(request);
            var headers = BuildHeaders();
            var parser = CreateStreamParser();

            UnaiChatResponse finalResponse = null;

            await UnaiHttpStreamClient.StreamPostAsync(
                url, body, headers, parser,
                onDelta: delta =>
                {
                    onDelta?.Invoke(delta);

                    if (delta.IsFinal)
                    {
                        finalResponse = new UnaiChatResponse
                        {
                            Content = delta.AccumulatedContent,
                            Role = UnaiRole.Assistant,
                            Model = request.Model,
                            ProviderId = ProviderId,
                            FinishReason = delta.FinishReason,
                            WasStreamed = true
                        };
                    }
                },
                onComplete: () =>
                {
                    finalResponse ??= new UnaiChatResponse
                    {
                        Content = "",
                        ProviderId = ProviderId,
                        WasStreamed = true
                    };
                    onComplete?.Invoke(finalResponse);
                },
                onError: error =>
                {
                    error.ProviderId = ProviderId;
                    onError?.Invoke(error);
                },
                cancellationToken);
        }

        protected void ResolveModel(UnaiChatRequest request)
        {
            if (string.IsNullOrEmpty(request.Model))
                request.Model = Config.DefaultModel;
        }
    }
}
