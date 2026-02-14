using UnAI.Models;
using UnAI.Tools;

namespace UnAI.Agent
{
    public class UnaiAgentThinkingArgs
    {
        public int StepNumber { get; set; }
        public int MessageCount { get; set; }
        public int EstimatedTokens { get; set; }
    }

    public class UnaiAgentToolCallArgs
    {
        public int StepNumber { get; set; }
        public UnaiToolCall ToolCall { get; set; }
    }

    public class UnaiAgentToolResultArgs
    {
        public int StepNumber { get; set; }
        public UnaiToolResult Result { get; set; }
        public float ExecutionTimeMs { get; set; }
    }

    public class UnaiAgentStepCompleteArgs
    {
        public UnaiAgentStep Step { get; set; }
    }

    public class UnaiAgentStreamDeltaArgs
    {
        public int StepNumber { get; set; }
        public UnaiStreamDelta Delta { get; set; }
    }
}
