namespace UnAI.Models
{
    [System.Serializable]
    public class UnaiUsageInfo
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }
}
