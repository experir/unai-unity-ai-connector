using UnAI.Models;

namespace UnAI.Streaming
{
    public interface ISseLineParser
    {
        UnaiStreamDelta ProcessLine(string line);
        bool IsComplete { get; }
        void Reset();
    }
}
