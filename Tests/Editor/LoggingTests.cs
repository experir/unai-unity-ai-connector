using System.Collections.Generic;
using NUnit.Framework;
using UnAI.Config;
using UnAI.Models;
using UnAI.Providers.Gemini;
using UnAI.Utilities;

namespace UnAI.Tests
{
    public class LoggingTests
    {
        private sealed class Gemini : GeminiProvider
        {
            public string Url(bool stream) => BuildRequestUrl(new UnaiChatRequest { Model = "test", Stream = stream });
            public Dictionary<string,string> Headers() => BuildHeaders();
        }

        [TestCase("https://api.example/chat?key=SENTINEL&alt=sse")]
        [TestCase("https://api.example/chat?%6bey=SENTINEL&alt=sse")]
        [TestCase("https://user:SENTINEL@api.example/chat?api_key=SENTINEL")]
        public void CredentialUrlsAreRedacted(string url)
        {
            string safe = UnaiLogger.RedactUrl(url);
            Assert.IsFalse(safe.Contains("SENTINEL"));
            StringAssert.Contains("api.example", safe);
        }

        [Test]
        public void JsonAndFreeTextHideRegisteredSecrets()
        {
            UnaiLogger.RegisterSecret("SENTINEL_PRIVATE_KEY");
            string safe = UnaiLogger.RedactBody("{\"api_key\":\"another secret\",\"nested\":{\"password\":\"pw\"},\"text\":\"SENTINEL_PRIVATE_KEY\"}");
            Assert.IsFalse(safe.Contains("another secret"));
            Assert.IsFalse(safe.Contains("SENTINEL_PRIVATE_KEY"));
            Assert.IsFalse(safe.Contains("pw"));
            Assert.AreEqual("Error: [REDACTED]", UnaiLogger.RedactBody("Error: SENTINEL_PRIVATE_KEY"));
        }

        [Test]
        public void GeminiCredentialsAreHeadersNotUrls()
        {
            var provider = new Gemini();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = "https://api.example", ApiKey = "SENTINEL_PRIVATE_KEY" });
            Assert.IsFalse(provider.Url(false).Contains("key="));
            Assert.IsFalse(provider.Url(true).Contains("key="));
            Assert.AreEqual("SENTINEL_PRIVATE_KEY", provider.Headers()["x-goog-api-key"]);
        }
    }
}
