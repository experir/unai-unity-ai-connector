using System;
using System.Text;
using UnityEngine.Networking;

namespace UnAI.Http
{
    public class UnaiStreamingDownloadHandler : DownloadHandlerScript
    {
        private readonly StringBuilder _lineBuffer = new(512);
        private readonly Action<string> _onLineReceived;
        private readonly Action _onComplete;
        private bool _isComplete;

        private static readonly int BufferSize = 4096;

        public UnaiStreamingDownloadHandler(
            Action<string> onLineReceived,
            Action onComplete)
            : base(new byte[BufferSize])
        {
            _onLineReceived = onLineReceived;
            _onComplete = onComplete;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (_isComplete) return false;

            for (int i = 0; i < dataLength; i++)
            {
                char c = (char)data[i];

                if (c == '\n')
                {
                    if (_lineBuffer.Length > 0 && _lineBuffer[_lineBuffer.Length - 1] == '\r')
                        _lineBuffer.Length -= 1;

                    string line = _lineBuffer.ToString();
                    _lineBuffer.Clear();
                    _onLineReceived?.Invoke(line);
                }
                else
                {
                    _lineBuffer.Append(c);
                }
            }

            return true;
        }

        protected override void CompleteContent()
        {
            _isComplete = true;

            if (_lineBuffer.Length > 0)
            {
                string remaining = _lineBuffer.ToString();
                _lineBuffer.Clear();
                _onLineReceived?.Invoke(remaining);
            }

            _onComplete?.Invoke();
        }

        protected override byte[] GetData() => null;
        protected override string GetText() => null;
    }
}
