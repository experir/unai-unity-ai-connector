using System.Threading;
using System.Threading.Tasks;

namespace UnAI.Tools
{
    public interface IUnaiTool
    {
        UnaiToolDefinition Definition { get; }
        Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default);
    }
}
