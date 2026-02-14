namespace UnAI.Tools
{
    [System.Serializable]
    public class UnaiToolResult
    {
        public string ToolCallId { get; set; }
        public string ToolName { get; set; }
        public string Content { get; set; }
        public bool IsError { get; set; }
    }
}
