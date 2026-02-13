using System.Collections.Generic;
using Newtonsoft.Json;

namespace UnAI.Models
{
    [System.Serializable]
    public class UnaiRequestOptions
    {
        [JsonProperty("temperature")]
        public float? Temperature { get; set; }

        [JsonProperty("maxTokens")]
        public int? MaxTokens { get; set; }

        [JsonProperty("topP")]
        public float? TopP { get; set; }

        [JsonProperty("stopSequences")]
        public string[] StopSequences { get; set; }

        [JsonProperty("extraParameters")]
        public Dictionary<string, object> ExtraParameters { get; set; }
    }
}
