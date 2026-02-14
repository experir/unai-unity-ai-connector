using System.Collections.Generic;
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
