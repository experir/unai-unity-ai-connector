using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnAI.Models
{
    public enum UnaiResponseFormat
    {
        Text,
        JsonObject,
        JsonSchema
    }

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

        [JsonProperty("responseFormat")]
        public UnaiResponseFormat ResponseFormat { get; set; } = UnaiResponseFormat.Text;

        [JsonProperty("jsonSchemaName")]
        public string JsonSchemaName { get; set; }

        [JsonProperty("jsonSchema")]
        public JObject JsonSchema { get; set; }

        [JsonProperty("extraParameters")]
        public Dictionary<string, object> ExtraParameters { get; set; }
    }
}
