using System;
using System.Text;
using UnityEngine.Networking;

namespace UnAI.Http
{
    public class UnaiStreamingDownloadHandler : DownloadHandlerScript
    {
        private readonly StringBuilder _lineBuffer = new(512);
        private readonly Decoder _decoder = new UTF8Encoding(false, true).GetDecoder();
        private readonly char[] _characters = new char[4097];
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

            if (data == null || dataLength == 0) return true;
            int offset = 0;
            while (offset < dataLength)
            {
                _decoder.Convert(data, offset, dataLength - offset,
                    _characters, 0, _characters.Length, false,
                    out int bytesUsed, out int charsUsed, out _);
                offset += bytesUsed;
                ProcessCharacters(charsUsed);
            }
            return true;
        }

        private void ProcessCharacters(int count)
        {
            for (int i = 0; i < count; i++)
            {
                char c = _characters[i];
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

        }

        protected override void CompleteContent()
        {
            _isComplete = true;
            int count = _decoder.GetChars(Array.Empty<byte>(), 0, 0, _characters, 0, true);
            ProcessCharacters(count);

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
