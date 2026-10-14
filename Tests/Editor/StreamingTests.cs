using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnAI.Http;
using UnAI.Models;
using UnAI.Streaming;

namespace UnAI.Tests
{
    public class StreamingTests
    {
        private sealed class Handler : UnaiStreamingDownloadHandler
        {
            public Handler(Action<string> line) : base(line, null) { }
            public bool Feed(byte[] bytes) => ReceiveData(bytes, bytes.Length);
            public void Finish() => CompleteContent();
        }

        [Test]
        public void Utf8SurvivesEveryByteBoundaryAndFinalLine()
        {
            const string text = "data: café 漢字 😀\r\n\r\nfinal è🚀";
            var bytes = Encoding.UTF8.GetBytes(text);
            for (int split = 1; split < bytes.Length; split++)
            {
                var lines = new List<string>();
                using var handler = new Handler(lines.Add);
                handler.Feed(bytes[..split]);
                handler.Feed(bytes[split..]);
                handler.Finish();
                CollectionAssert.AreEqual(new[] { "data: café 漢字 😀", "", "final è🚀" }, lines,
                    $"UTF-8 split at byte {split}");
            }
        }

        [Test]
        public void Utf8SurvivesOneByteChunks()
        {
            var lines = new List<string>();
            using var handler = new Handler(lines.Add);
            foreach (byte value in Encoding.UTF8.GetBytes("☀️café\n"))
                handler.Feed(new[] { value });
            handler.Finish();
            CollectionAssert.AreEqual(new[] { "☀️café" }, lines);
        }

        [Test]
        public void SseFlushesAnEventWithoutFinalBlankLine()
        {
            var parser = new SseLineParser((_, data) => new UnaiStreamDelta { Content = data });
            Assert.IsNull(parser.ProcessLine("data: hello"));
            var final = parser.Complete();
            Assert.AreEqual("hello", final.AccumulatedContent);
            Assert.IsTrue(final.IsFinal);
            Assert.IsTrue(parser.IsComplete);
        }

        [Test]
        public void MalformedSseIsReported()
        {
            var parser = new SseLineParser((_, _) => throw new Exception("bad payload"));
            parser.ProcessLine("data: malformed");
            Assert.Throws<FormatException>(() => parser.ProcessLine(""));
        }

        [Test]
        public void TruncatedUtf8FailsInsteadOfCorruptingText()
        {
            using var handler = new Handler(_ => { });
            handler.Feed(new byte[] { 0xf0, 0x9f });
            handler.Finish();
            Assert.IsInstanceOf<DecoderFallbackException>(handler.ProcessingError);
        }
    }
}
