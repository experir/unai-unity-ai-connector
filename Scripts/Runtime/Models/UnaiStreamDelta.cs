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
    }
}
