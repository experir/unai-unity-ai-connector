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

        // 用于跨 ReceiveData 调用保留不完整的 UTF-8 多字节序列
        private readonly byte[] _utf8Remainder = new byte[3];
        private int _utf8RemainderLength = 0;

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

            // 拼接上次残留的不完整字节
            byte[] buffer;
            int bufferLength;
            if (_utf8RemainderLength > 0)
            {
                buffer = new byte[_utf8RemainderLength + dataLength];
                Array.Copy(_utf8Remainder, 0, buffer, 0, _utf8RemainderLength);
                Array.Copy(data, 0, buffer, _utf8RemainderLength, dataLength);
                bufferLength = buffer.Length;
                _utf8RemainderLength = 0;
            }
            else
            {
                buffer = data;
                bufferLength = dataLength;
            }

            // 找到最后一个完整 UTF-8 字符的边界，避免截断多字节字符
            int decodeLength = FindSafeDecodeLength(buffer, bufferLength);

            // 保存尾部不完整的字节留到下次
            int remainder = bufferLength - decodeLength;
            if (remainder > 0)
            {
                Array.Copy(buffer, decodeLength, _utf8Remainder, 0, remainder);
                _utf8RemainderLength = remainder;
            }

            // 正确解码为 UTF-8 字符串
            string chunk = Encoding.UTF8.GetString(buffer, 0, decodeLength);

            // 按行分割
            foreach (char c in chunk)
            {
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

            // 处理残留的字节（正常情况下不应有，但做防御处理）
            if (_utf8RemainderLength > 0)
            {
                string remaining = Encoding.UTF8.GetString(_utf8Remainder, 0, _utf8RemainderLength);
                _lineBuffer.Append(remaining);
                _utf8RemainderLength = 0;
            }

            if (_lineBuffer.Length > 0)
            {
                string remaining = _lineBuffer.ToString();
                _lineBuffer.Clear();
                _onLineReceived?.Invoke(remaining);
            }

            _onComplete?.Invoke();
        }

        // 找到最后一个完整 UTF-8 字符的安全边界
        // UTF-8 续字节特征：10xxxxxx，即 (byte & 0xC0) == 0x80
        private static int FindSafeDecodeLength(byte[] buffer, int length)
        {
            int i = length - 1;
            while (i >= 0 && (buffer[i] & 0xC0) == 0x80)
                i--;

            if (i < 0) return 0;

            // 判断从 i 开始的首字节需要几个续字节
            byte b = buffer[i];
            int expectedTotal;
            if ((b & 0x80) == 0x00)      expectedTotal = 1; // 单字节 ASCII
            else if ((b & 0xE0) == 0xC0) expectedTotal = 2; // 2字节序列
            else if ((b & 0xF0) == 0xE0) expectedTotal = 3; // 3字节序列（中文）
            else if ((b & 0xF8) == 0xF0) expectedTotal = 4; // 4字节序列（emoji）
            else return i; // 无法识别，截到此处

            int actualBytes = length - i;

            // 如果首字节后续字节不够，说明这个字符被截断了，排除掉
            return actualBytes >= expectedTotal ? length : i;
        }

        protected override byte[] GetData() => null;
        protected override string GetText() => null;
    }
}