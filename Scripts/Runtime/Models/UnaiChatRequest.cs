using System.Collections.Generic;
using Newtonsoft.Json;
using UnAI.Tools;

namespace UnAI.Models
{
    [System.Serializable]
    public class UnaiChatRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("messages")]
        public List<UnaiChatMessage> Messages { get; set; } = new();

        [JsonProperty("options")]
        public UnaiRequestOptions Options { get; set; }

        [JsonIgnore]
        public bool Stream { get; set; }

        [JsonIgnore]
        public List<UnaiToolDefinition> Tools { get; set; }

        /// <summary>
        /// When true, the response may be served from cache if an identical
        /// request was made recently. Only applies to non-streaming calls.
        /// </summary>
        [JsonIgnore]
        public bool UseCache { get; set; }
    }
}
