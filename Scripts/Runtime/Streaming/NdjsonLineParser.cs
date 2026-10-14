using System;
using System.Text;
using UnAI.Models;
using UnAI.Utilities;

namespace UnAI.Streaming
{
    public class NdjsonLineParser : ISseLineParser
    {
        private readonly Func<string, UnaiStreamDelta> _deltaFactory;
        private readonly StringBuilder _accumulated = new();

        public bool IsComplete { get; private set; }

        public NdjsonLineParser(Func<string, UnaiStreamDelta> deltaFactory)
        {
            _deltaFactory = deltaFactory;
        }

        public UnaiStreamDelta ProcessLine(string line)
        {
            if (IsComplete || string.IsNullOrWhiteSpace(line)) return null;

            try
            {
                var delta = _deltaFactory(line);
                if (delta == null) return null;

                if (!string.IsNullOrEmpty(delta.Content))
                    _accumulated.Append(delta.Content);

                delta.AccumulatedContent = _accumulated.ToString();

                if (delta.IsFinal)
                    IsComplete = true;

                return delta;
            }
            catch (Exception ex)
            {
                throw new FormatException("Invalid NDJSON event payload.", ex);
            }
        }

        public void Reset()
        {
            _accumulated.Clear();
            IsComplete = false;
        }
    }
}
