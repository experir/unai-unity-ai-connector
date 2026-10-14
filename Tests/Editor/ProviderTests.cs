using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnAI.Tools;
using UnAI.Streaming;
using UnityEngine.TestTools;

namespace UnAI.Tests
{
    public class ProviderTests
    {
        private static UnaiProviderBase Provider(string id, UnaiProviderConfig config = null)
        {
            var type = typeof(UnaiProviderBase).Assembly.GetTypes().First(t => !t.IsAbstract
                && typeof(UnaiProviderBase).IsAssignableFrom(t)
                && ((UnaiProviderBase)Activator.CreateInstance(t)).ProviderId == id);
            var provider = (UnaiProviderBase)Activator.CreateInstance(type);
            provider.Initialize(config ?? new UnaiProviderConfig { BaseUrl = "https://api.example", ApiKey = "fixture-key" });
            return provider;
        }

        private static object Call(UnaiProviderBase provider, string method, params object[] args)
        {
            try { return provider.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(provider, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }

        private static JObject Payload(UnaiProviderBase p, UnaiChatRequest request)
            => JObject.Parse((string)Call(p, "SerializeRequest", request));

        [TestCase("openai")]
        [TestCase("anthropic")]
        [TestCase("gemini")]
        [TestCase("mistral")]
        [TestCase("cohere")]
        [TestCase("ollama")]
        [TestCase("lmstudio")]
        [TestCase("llamacpp")]
        [TestCase("openai-compatible")]
        public void EveryAdapterSerializesMinimalChat(string id)
        {
            var payload = Payload(Provider(id), new UnaiChatRequest
            { Model = "fixture-model", Messages = new() { UnaiChatMessage.User("hello") } });
            Assert.IsTrue(payload["messages"] is JArray || payload["contents"] is JArray);
            StringAssert.Contains("hello", payload.ToString());
        }

        [Test]
        public void OpenAiUsesCompletionLimitWithoutChangingCompatibleServers()
        {
            var request = new UnaiChatRequest { Model = "gpt-5.2", Messages = new() { UnaiChatMessage.User("hello") },
                Options = new UnaiRequestOptions { MaxTokens = 50 } };
            var openai = Payload(Provider("openai"), request);
            Assert.IsNull(openai["max_tokens"]);
            Assert.AreEqual(50, openai["max_completion_tokens"].Value<int>());
            Assert.AreEqual(50, Payload(Provider("openai-compatible"), request)["max_tokens"].Value<int>());
        }

        [Test]
        public void CompatibleStreamingUsageIsOptInAndV1PathsAreNormalized()
        {
            var config = new UnaiProviderConfig { BaseUrl = "https://api.example/v1" };
            var p = Provider("openai-compatible", config);
            var request = new UnaiChatRequest { Model = "fixture-model", Stream = true, Messages = new() { UnaiChatMessage.User("hi") } };
            Assert.IsNull(Payload(p, request)["stream_options"]);
            config.IncludeStreamingUsage = true;
            Assert.IsNotNull(Payload(p, request)["stream_options"]);
            Assert.AreEqual("https://api.example/v1/chat/completions", Call(p, "BuildRequestUrl", request));
        }

        [Test]
        public void UnsupportedReasoningSamplingFailsBeforeSending()
        {
            var request = new UnaiChatRequest { Model = "gpt-5", Messages = new() { UnaiChatMessage.User("hello") },
                Options = new UnaiRequestOptions { Temperature = 0.5f } };
            Assert.Throws<ArgumentException>(() => Payload(Provider("openai"), request));
        }

        [Test]
        public void GeminiSignaturePartsRoundTripThroughToolHistory()
        {
            var p = Provider("gemini");
            var response = (UnaiChatResponse)Call(p, "DeserializeResponse",
                "{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"summary\",\"thoughtSignature\":\"signature-1\"},{\"functionCall\":{\"id\":\"native-call-1\",\"name\":\"weather\",\"args\":{\"city\":\"Rome\"}},\"thoughtSignature\":\"signature-2\"}]},\"finishReason\":\"STOP\"}]}");
            Assert.AreEqual("", response.Content);
            var request = new UnaiChatRequest { Messages = new() { UnaiChatMessage.AssistantWithToolCalls(response.ToolCalls) } };
            var parts = Payload(p, request.Clone())["contents"][0]["parts"];
            Assert.AreEqual("signature-1", parts[0]["thoughtSignature"].ToString());
            Assert.AreEqual("signature-2", parts[1]["thoughtSignature"].ToString());
            request.Messages.Add(UnaiChatMessage.ToolResult(response.ToolCalls[0].Id, "weather", "sunny"));
            Assert.AreEqual("native-call-1", Payload(p, request)["contents"][1]["parts"][0]["functionResponse"]["id"].ToString());
        }

        [Test]
        public void AnthropicSignedToolHistoryRoundTrips()
        {
            var p = Provider("anthropic");
            var response = (UnaiChatResponse)Call(p, "DeserializeResponse",
                "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"fixture summary\",\"signature\":\"opaque-signature\"},{\"type\":\"tool_use\",\"id\":\"native-call-1\",\"name\":\"weather\",\"input\":{\"city\":\"Rome\"}}]}");
            var request = new UnaiChatRequest { Messages = new() { UnaiChatMessage.AssistantWithToolCalls(response.ToolCalls) } };
            Assert.AreEqual("", response.Content);
            Assert.AreEqual("opaque-signature", Payload(p, request.Clone())["messages"][0]["content"][0]["signature"].ToString());
        }

        private static UnaiStreamDelta Event(ISseLineParser parser, string data)
        {
            parser.ProcessLine("data: " + data);
            return parser.ProcessLine("");
        }

        [Test]
        public void AnthropicStreamingAssemblesToolsThinkingAndUsage()
        {
            var p = Provider("anthropic");
            var parser = (ISseLineParser)Call(p, "CreateStreamParser");
            Event(parser, "{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":5}}}");
            Event(parser, "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"\"}}");
            Event(parser, "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"fixture summary\"}}");
            Event(parser, "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"signature_delta\",\"signature\":\"opaque-signature\"}}");
            Event(parser, "{\"type\":\"content_block_stop\",\"index\":0}");
            Event(parser, "{\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"native-call-1\",\"name\":\"weather\",\"input\":{}}}");
            Event(parser, "{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"city\\\":\"}}");
            Event(parser, "{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"\\\"Rome\\\"}\"}}");
            Event(parser, "{\"type\":\"content_block_stop\",\"index\":1}");
            var usage = Event(parser, "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":7}}");
            Assert.IsFalse(usage.IsFinal);
            var final = Event(parser, "{\"type\":\"message_stop\"}");
            Assert.IsTrue(final.IsFinal);
            Assert.AreEqual(12, final.Usage.TotalTokens);
            Assert.AreEqual("native-call-1", final.ToolCalls[0].Id);
            Assert.AreEqual("Rome", final.ToolCalls[0].GetArguments()["city"].ToString());
            var request = new UnaiChatRequest { Messages = new() { UnaiChatMessage.AssistantWithToolCalls(final.ToolCalls) } };
            Assert.AreEqual("opaque-signature", Payload(p, request.Clone())["messages"][0]["content"][0]["signature"].ToString());
        }

        [Test]
        public void DisabledToolsFailBeforeHttp()
        {
            var p = Provider("openai-compatible", new UnaiProviderConfig { BaseUrl = "https://api.example", EnableToolCalling = false });
            Assert.IsFalse(p.SupportsToolCalling);
            Assert.ThrowsAsync<NotSupportedException>(() => p.ChatAsync(new UnaiChatRequest
            { Messages = new() { UnaiChatMessage.User("hello") }, Tools = new() { new UnaiToolDefinition { Name = "tool" } } }));
        }

        [UnityTest]
        public IEnumerator ModelDiscoveryUsesGetAndNormalizesEachApiShape()
        {
            foreach (string id in new[] { "openai-compatible", "anthropic", "gemini", "ollama" })
            {
                var finder = new TcpListener(IPAddress.Loopback, 0);
                finder.Start(); int port = ((IPEndPoint)finder.LocalEndpoint).Port; finder.Stop();
                using var listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start();
                var server = ServeModels(listener, id);
                var p = Provider(id, new UnaiProviderConfig { BaseUrl = $"http://localhost:{port}", ApiKey = "fixture-key", MaxRetries = 0 });
                var models = p.ListModelsAsync();
                var all = Task.WhenAll(server, models);
                var until = DateTime.UtcNow.AddSeconds(8);
                while (!all.IsCompleted && DateTime.UtcNow < until) yield return null;
                Assert.IsTrue(all.IsCompleted, id);
                if (all.IsFaulted) throw all.Exception.GetBaseException();
                Assert.AreEqual(id == "anthropic" ? 2 : 1, models.Result.Count, id);
                Assert.AreEqual("model-1", models.Result[0].Id);
            }
        }

        private static async Task ServeModels(HttpListener listener, string id)
        {
            for (int page = 0; page < (id == "anthropic" ? 2 : 1); page++)
            {
                var ctx = await listener.GetContextAsync();
                Assert.AreEqual("GET", ctx.Request.HttpMethod);
                string body = id == "gemini" ? "{\"models\":[{\"name\":\"models/model-1\",\"supportedGenerationMethods\":[\"generateContent\"]}]}"
                    : id == "ollama" ? "{\"models\":[{\"model\":\"model-1\"}]}"
                    : id == "anthropic" ? $"{{\"data\":[{{\"id\":\"model-{page+1}\"}}],\"has_more\":{(page == 0 ? "true" : "false")},\"last_id\":\"model-{page+1}\"}}"
                    : "{\"data\":[{\"id\":\"model-1\"}]}";
                if (page == 1) Assert.AreEqual("model-1", ctx.Request.QueryString["after_id"]);
                ctx.Response.ContentType = "application/json";
                using var writer = new StreamWriter(ctx.Response.OutputStream);
                await writer.WriteAsync(body);
                writer.Close(); ctx.Response.Close();
            }
        }
    }
}
