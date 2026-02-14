using System;
using System.Collections.Generic;
using UnAI.Models;

namespace UnAI.Memory
{
    public static class UnaiTokenEstimator
    {
        private const float CharsPerToken = 3.5f;
        private const int MessageOverhead = 4;

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (int)Math.Ceiling(text.Length / CharsPerToken) + MessageOverhead;
        }

        public static int EstimateMessages(IReadOnlyList<UnaiChatMessage> messages, string systemPrompt = null)
        {
            int total = 0;
            if (!string.IsNullOrEmpty(systemPrompt))
                total += EstimateTokens(systemPrompt);
            foreach (var msg in messages)
                total += EstimateTokens(msg.Content);
            return total;
        }
    }
}
