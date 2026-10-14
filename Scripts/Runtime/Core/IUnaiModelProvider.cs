using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Models;

namespace UnAI.Core
{
    public interface IUnaiModelProvider
    {
        bool SupportsModelDiscovery { get; }
        Task<IReadOnlyList<UnaiModelInfo>> ListModelsAsync(CancellationToken ct = default);
    }
}
