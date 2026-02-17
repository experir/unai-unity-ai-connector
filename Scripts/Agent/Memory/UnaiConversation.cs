using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnAI.Models;
using UnAI.Tools;

namespace UnAI.Memory
{
    public class UnaiConversation
    {
        private readonly List<UnaiChatMessage> _messages = new();

        public string SystemPrompt { get; set; }
        public IReadOnlyList<UnaiChatMessage> Messages => _messages;
        public int MessageCount => _messages.Count;

        public UnaiChatMessage AddUser(string content)
        {
            var msg = UnaiChatMessage.User(content);
            _messages.Add(msg);
            return msg;
        }

        public UnaiChatMessage AddAssistant(string content, List<UnaiToolCall> toolCalls = null)
        {
            var msg = toolCalls is { Count: > 0 }
                ? UnaiChatMessage.AssistantWithToolCalls(toolCalls, content)
                : UnaiChatMessage.Assistant(content);
            _messages.Add(msg);
            return msg;
        }

        public UnaiChatMessage AddToolResult(string toolCallId, string toolName, string content)
        {
            var msg = UnaiChatMessage.ToolResult(toolCallId, toolName, content);
            _messages.Add(msg);
            return msg;
        }

        public void Add(UnaiChatMessage message) => _messages.Add(message);

        public UnaiChatRequest BuildRequest(
            string model = null,
            UnaiRequestOptions options = null,
            List<UnaiToolDefinition> tools = null,
            int? maxContextTokens = null,
            UnaiMemoryStrategy strategy = UnaiMemoryStrategy.TruncateOldest)
        {
            var request = new UnaiChatRequest
            {
                Model = model,
                Options = options,
                Tools = tools
            };

            if (!string.IsNullOrEmpty(SystemPrompt))
                request.Messages.Add(UnaiChatMessage.System(SystemPrompt));

            var fitted = ApplyStrategy(_messages, maxContextTokens, strategy);
            request.Messages.AddRange(fitted);

            return request;
        }

        public void Clear() => _messages.Clear();

        public int EstimateTokenCount() =>
            UnaiTokenEstimator.EstimateMessages(_messages, SystemPrompt);

        // ── Persistence ──────────────────────────────────────────────

        /// <summary>
        /// Serializes the conversation (system prompt + all messages) to a JSON string.
        /// </summary>
        public string SaveToJson()
        {
            var data = new ConversationData
            {
                SystemPrompt = SystemPrompt,
                Messages = new List<UnaiChatMessage>(_messages),
                SavedAt = DateTime.UtcNow.ToString("O")
            };
            return JsonConvert.SerializeObject(data, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        /// <summary>
        /// Saves the conversation to a JSON file at the given path.
        /// Creates parent directories if needed.
        /// </summary>
        public void SaveToFile(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, SaveToJson(), Encoding.UTF8);
        }

        /// <summary>
        /// Loads conversation data from a JSON string, replacing current state.
        /// </summary>
        public void LoadFromJson(string json)
        {
            var data = JsonConvert.DeserializeObject<ConversationData>(json);
            if (data == null) return;

            _messages.Clear();
            SystemPrompt = data.SystemPrompt;
            if (data.Messages != null)
            {
                foreach (var msg in data.Messages)
                    _messages.Add(msg);
            }
        }

        /// <summary>
        /// Loads conversation data from a JSON file.
        /// </summary>
        public void LoadFromFile(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            LoadFromJson(json);
        }

        /// <summary>
        /// Exports the conversation as a readable Markdown string.
        /// </summary>
        public string ExportMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# UNAI Conversation");
            sb.AppendLine();
            sb.AppendLine($"*Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(SystemPrompt))
            {
                sb.AppendLine("## System Prompt");
                sb.AppendLine();
                sb.AppendLine(SystemPrompt);
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var msg in _messages)
            {
                switch (msg.Role)
                {
                    case UnaiRole.User:
                        sb.AppendLine("### User");
                        sb.AppendLine();
                        sb.AppendLine(msg.Content);
                        sb.AppendLine();
                        break;
                    case UnaiRole.Assistant:
                        sb.AppendLine("### Assistant");
                        sb.AppendLine();
                        if (!string.IsNullOrEmpty(msg.Content))
                        {
                            sb.AppendLine(msg.Content);
                            sb.AppendLine();
                        }
                        if (msg.ToolCalls is { Count: > 0 })
                        {
                            foreach (var tc in msg.ToolCalls)
                            {
                                sb.AppendLine($"> **Tool call:** `{tc.ToolName}({tc.ArgumentsJson})`");
                            }
                            sb.AppendLine();
                        }
                        break;
                    case UnaiRole.Tool:
                        sb.AppendLine($"> **Tool result** (`{msg.ToolName}`):");
                        string preview = msg.Content;
                        if (preview != null && preview.Length > 500)
                            preview = preview.Substring(0, 500) + "...";
                        sb.AppendLine($"> {preview}");
                        sb.AppendLine();
                        break;
                    case UnaiRole.System:
                        // System messages in the middle of conversation (rare)
                        sb.AppendLine($"*[System: {msg.Content}]*");
                        sb.AppendLine();
                        break;
                }
            }

            return sb.ToString();
        }

        [Serializable]
        private class ConversationData
        {
            [JsonProperty("systemPrompt")] public string SystemPrompt;
            [JsonProperty("messages")] public List<UnaiChatMessage> Messages;
            [JsonProperty("savedAt")] public string SavedAt;
        }

        private List<UnaiChatMessage> ApplyStrategy(
            List<UnaiChatMessage> messages, int? maxTokens, UnaiMemoryStrategy strategy)
        {
            if (!maxTokens.HasValue || strategy == UnaiMemoryStrategy.KeepAll)
                return new List<UnaiChatMessage>(messages);

            int budget = maxTokens.Value;
            int systemTokens = UnaiTokenEstimator.EstimateTokens(SystemPrompt);
            int responseReserve = 1024;
            int available = budget - systemTokens - responseReserve;
            if (available <= 0)
                return new List<UnaiChatMessage>(messages);

            if (strategy == UnaiMemoryStrategy.TruncateOldest)
            {
                var result = new List<UnaiChatMessage>();
                int tokenCount = 0;

                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    int msgTokens = UnaiTokenEstimator.EstimateTokens(messages[i].Content);
                    if (tokenCount + msgTokens > available)
                        break;
                    tokenCount += msgTokens;
                    result.Insert(0, messages[i]);
                }
                return result;
            }

            // SummarizeOld: keep last N messages, prefix with summary placeholder
            // Full summarization requires an LLM call, handled by the agent layer
            return new List<UnaiChatMessage>(messages);
        }
    }
}
