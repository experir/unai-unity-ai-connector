using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Config;
using UnAI.Models;

namespace UnAI.Core
{
    public interface IUnaiProvider
    {
        string ProviderId { get; }
        string DisplayName { get; }
        bool IsConfigured { get; }
        IReadOnlyList<UnaiModelInfo> KnownModels { get; }
        bool SupportsToolCalling { get; }

        void Initialize(UnaiProviderConfig config);

        Task<UnaiChatResponse> ChatAsync(
            UnaiChatRequest request,
            CancellationToken cancellationToken = default);

        Task ChatStreamAsync(
            UnaiChatRequest request,
            Action<UnaiStreamDelta> onDelta,
            Action<UnaiChatResponse> onComplete = null,
            Action<UnaiErrorInfo> onError = null,
            CancellationToken cancellationToken = default);
    }
}
