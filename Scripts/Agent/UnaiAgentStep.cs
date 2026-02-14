using System.Collections.Generic;
using UnAI.Models;
using UnAI.Tools;

namespace UnAI.Agent
{
    public class UnaiAgentStep
    {
        public int StepNumber { get; set; }
        public UnaiChatResponse Response { get; set; }
        public List<UnaiToolCall> ToolCalls { get; set; }
        public List<UnaiToolResult> ToolResults { get; set; }
        public float DurationMs { get; set; }
        public bool IsFinal { get; set; }
        public string StopReason { get; set; }
    }
}
