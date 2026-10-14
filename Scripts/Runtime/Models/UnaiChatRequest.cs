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

        /// <summary>Creates an independent snapshot, including tools and message metadata.</summary>
        public UnaiChatRequest Clone()
        {
            var copy = JsonConvert.DeserializeObject<UnaiChatRequest>(JsonConvert.SerializeObject(this));
            copy.Stream = Stream;
            copy.UseCache = UseCache;
            copy.Tools = Tools?.ConvertAll(tool => new UnaiToolDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                IsReadOnly = tool.IsReadOnly,
                ParametersSchema = (Newtonsoft.Json.Linq.JObject)tool.ParametersSchema?.DeepClone()
            });
            return copy;
        }
    }
}
