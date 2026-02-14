using Newtonsoft.Json.Linq;

namespace UnAI.Tools
{
    [System.Serializable]
    public class UnaiToolCall
    {
        public string Id { get; set; }
        public string ToolName { get; set; }
        public string ArgumentsJson { get; set; }

        public JObject GetArguments() => JObject.Parse(ArgumentsJson ?? "{}");
    }
}
