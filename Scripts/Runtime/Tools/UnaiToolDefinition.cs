using Newtonsoft.Json.Linq;

namespace UnAI.Tools
{
    [System.Serializable]
    public class UnaiToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JObject ParametersSchema { get; set; }
    }
}
