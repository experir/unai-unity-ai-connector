using System.Collections.Generic;
using Newtonsoft.Json;
using UnAI.Tools;

namespace UnAI.Models
{
    [System.Serializable]
    public class UnaiChatResponse
    {
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("role")]
        public UnaiRole Role { get; set; } = UnaiRole.Assistant;

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("providerId")]
        public string ProviderId { get; set; }

        [JsonProperty("usage")]
        public UnaiUsageInfo Usage { get; set; }

        [JsonProperty("finishReason")]
        public string FinishReason { get; set; }

        [JsonIgnore]
        public string RawResponse { get; set; }

        [JsonIgnore]
        public bool WasStreamed { get; set; }

        [JsonProperty("toolCalls", NullValueHandling = NullValueHandling.Ignore)]
        public List<UnaiToolCall> ToolCalls { get; set; }

        [JsonIgnore]
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }
}
