namespace UnAI.Models
{
    [System.Serializable]
    public class UnaiModelInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string ProviderId { get; set; }
        public int? MaxContextTokens { get; set; }
    }
}
