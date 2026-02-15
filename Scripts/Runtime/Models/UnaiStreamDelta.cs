using System.Collections.Generic;
using UnAI.Tools;

namespace UnAI.Models
{
    [System.Serializable]
    public class UnaiStreamDelta
    {
        public string Content { get; set; }
        public string AccumulatedContent { get; set; }
        public bool IsFinal { get; set; }
        public string FinishReason { get; set; }
        public string EventType { get; set; }
        public List<UnaiToolCall> ToolCalls { get; set; }
        public UnaiUsageInfo Usage { get; set; }
    }
}
