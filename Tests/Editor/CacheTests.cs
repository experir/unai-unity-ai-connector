using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnAI.Models;
using UnAI.Tools;
using UnAI.Utilities;

namespace UnAI.Tests
{
    public class CacheTests
    {
        [Test]
        public void DelimitersCannotCollideWithMessageBoundaries()
        {
            var one = new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("a;User:b") } };
            var two = new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("a"), UnaiChatMessage.User("b") } };
            Assert.AreNotEqual(UnaiResponseCache.BuildKey("p", one), UnaiResponseCache.BuildKey("p", two));
        }

        [Test]
        public void ToolsMetadataAndEndpointChangeIdentity()
        {
            var request = new UnaiChatRequest
            {
                Messages = new() { UnaiChatMessage.ToolResult("call-1", "weather", "sunny") },
                Tools = new() { new UnaiToolDefinition { Name = "weather", ParametersSchema = JObject.Parse("{\"type\":\"object\"}") } }
            };
            string before = UnaiResponseCache.BuildKey("p", request, "endpoint-1");
            Assert.AreNotEqual(before, UnaiResponseCache.BuildKey("p", request, "endpoint-2"));
            request.Messages[0].ToolCallId = "call-2";
            Assert.AreNotEqual(before, UnaiResponseCache.BuildKey("p", request, "endpoint-1"));
            request.Messages[0].ToolCallId = "call-1";
            request.Tools[0].ParametersSchema["required"] = new JArray("city");
            Assert.AreNotEqual(before, UnaiResponseCache.BuildKey("p", request, "endpoint-1"));
        }

        [Test]
        public void EquivalentPropertyOrdersHaveSameIdentity()
        {
            var request = new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") },
                Options = new UnaiRequestOptions { JsonSchema = JObject.Parse("{\"type\":\"object\",\"properties\":{}}") } };
            string before = UnaiResponseCache.BuildKey("p", request);
            request.Options.JsonSchema = JObject.Parse("{\"properties\":{},\"type\":\"object\"}");
            Assert.AreEqual(before, UnaiResponseCache.BuildKey("p", request));
        }

        [Test]
        public void CacheIsolatesStoredAndReturnedResponsesAndEvictsLru()
        {
            var cache = new UnaiResponseCache(2);
            var response = new UnaiChatResponse { Content = "original", RawResponse = "raw", WasStreamed = false };
            cache.Put("a", response);
            response.Content = "mutated";
            cache.Put("b", new UnaiChatResponse());
            Assert.IsTrue(cache.TryGet("a", out var first));
            Assert.AreEqual("original", first.Content);
            Assert.AreEqual("raw", first.RawResponse);
            first.Content = "changed returned value";
            cache.Put("c", new UnaiChatResponse());
            Assert.IsFalse(cache.TryGet("b", out _));
            Assert.IsTrue(cache.TryGet("a", out var second));
            Assert.AreEqual("original", second.Content);
        }
    }
}
