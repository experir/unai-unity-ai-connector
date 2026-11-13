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

        /// <summary>
        /// Whether a reasoning ("thinking") model should reason before answering. Null keeps the model's default.
        /// Set false for fast, conversational replies: reasoning can add thousands of hidden tokens.
        /// Currently applied by Ollama (<c>think</c>); other providers ignore it.
        /// </summary>
        [JsonProperty("reasoning")]
        public bool? Reasoning { get; set; }

        [JsonProperty("extraParameters")]
        public Dictionary<string, object> ExtraParameters { get; set; }
    }
}
