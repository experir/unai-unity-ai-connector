using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnAI.Tools;

namespace UnAI.Models
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum UnaiRole
    {
        System,
        User,
        Assistant,
        Tool
    }

    [System.Serializable]
    public class UnaiChatMessage
    {
        [JsonProperty("role")]
        public UnaiRole Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("toolCalls", NullValueHandling = NullValueHandling.Ignore)]
        public List<UnaiToolCall> ToolCalls { get; set; }

        [JsonProperty("toolCallId", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        [JsonProperty("toolName", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolName { get; set; }

        public UnaiChatMessage() { }

        public UnaiChatMessage(UnaiRole role, string content)
        {
            Role = role;
            Content = content;
        }

        public static UnaiChatMessage System(string content) => new(UnaiRole.System, content);
        public static UnaiChatMessage User(string content) => new(UnaiRole.User, content);
        public static UnaiChatMessage Assistant(string content) => new(UnaiRole.Assistant, content);

        public static UnaiChatMessage AssistantWithToolCalls(List<UnaiToolCall> toolCalls, string content = null)
            => new(UnaiRole.Assistant, content ?? "") { ToolCalls = toolCalls };

        public static UnaiChatMessage ToolResult(string toolCallId, string toolName, string content)
            => new(UnaiRole.Tool, content) { ToolCallId = toolCallId, ToolName = toolName };
    }
}
