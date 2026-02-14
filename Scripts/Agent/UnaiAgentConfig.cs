using UnAI.Memory;
using UnAI.Models;

namespace UnAI.Agent
{
    [System.Serializable]
    public class UnaiAgentConfig
    {
        public int MaxSteps { get; set; } = 10;
        public int TimeoutSeconds { get; set; } = 300;
        public string ProviderId { get; set; }
        public string Model { get; set; }
        public UnaiRequestOptions Options { get; set; }
        public UnaiMemoryStrategy MemoryStrategy { get; set; } = UnaiMemoryStrategy.TruncateOldest;
        public int? MaxContextTokens { get; set; }
        public bool UseStreaming { get; set; }
        public string SystemPrompt { get; set; }
    }
}
