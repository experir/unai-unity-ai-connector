using System;
using System.Text;
using UnAI.Models;
using UnAI.Utilities;

namespace UnAI.Streaming
{
    public class SseLineParser : ISseLineParser
    {
        private string _currentEventType;
        private string _currentData;
        private readonly Func<string, string, UnaiStreamDelta> _deltaFactory;
        private readonly string _doneMarker;
        private readonly StringBuilder _accumulated = new();

        public bool IsComplete { get; private set; }

        public SseLineParser(
            Func<string, string, UnaiStreamDelta> deltaFactory,
            string doneMarker = "[DONE]")
        {
            _deltaFactory = deltaFactory;
            _doneMarker = doneMarker;
        }

        public UnaiStreamDelta ProcessLine(string line)
        {
            if (IsComplete) return null;

            // Empty line = end of SSE event block, dispatch
            if (string.IsNullOrEmpty(line))
            {
                if (_currentData != null)
                {
                    string eventType = _currentEventType;
                    string data = _currentData;

                    _currentEventType = null;
                    _currentData = null;

                    if (data.Trim() == _doneMarker)
                    {
                        IsComplete = true;
                        return new UnaiStreamDelta
                        {
                            Content = "",
                            AccumulatedContent = _accumulated.ToString(),
                            IsFinal = true,
                            EventType = "done"
                        };
                    }

                    try
                    {
                        var delta = _deltaFactory(eventType, data);
                        if (delta != null)
                        {
                            if (!string.IsNullOrEmpty(delta.Content))
                                _accumulated.Append(delta.Content);
                            delta.AccumulatedContent = _accumulated.ToString();

                            if (delta.IsFinal)
                                IsComplete = true;
                        }
                        return delta;
                    }
                    catch (Exception ex)
                    {
                        UnaiLogger.LogWarning($"[UNAI] Failed to parse stream chunk: {ex.Message}");
                        return null;
                    }
                }
                return null;
            }

            // Parse SSE fields
            if (line.StartsWith("event:"))
            {
                _currentEventType = line.Substring(6).Trim();
            }
            else if (line.StartsWith("data:"))
            {
                string dataValue = line.Substring(5).TrimStart();
                if (_currentData != null)
                    _currentData += "\n" + dataValue;
                else
                    _currentData = dataValue;
            }
            // Ignore "id:", "retry:", and comment lines starting with ":"

            return null;
        }

        public void Reset()
        {
            _currentEventType = null;
            _currentData = null;
            _accumulated.Clear();
            IsComplete = false;
        }
    }
}
