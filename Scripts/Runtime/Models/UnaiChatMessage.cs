using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnAI.Models
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum UnaiRole
    {
        System,
        User,
        Assistant
    }

    [System.Serializable]
    public class UnaiChatMessage
    {
        [JsonProperty("role")]
        public UnaiRole Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        public UnaiChatMessage() { }

        public UnaiChatMessage(UnaiRole role, string content)
        {
            Role = role;
            Content = content;
        }

        public static UnaiChatMessage System(string content) => new(UnaiRole.System, content);
        public static UnaiChatMessage User(string content) => new(UnaiRole.User, content);
        public static UnaiChatMessage Assistant(string content) => new(UnaiRole.Assistant, content);
    }
}
