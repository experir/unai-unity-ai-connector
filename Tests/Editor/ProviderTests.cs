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
                "{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"summary\",\"thoughtSignature\":\"signature-1\"},{\"functionCall\":{\"name\":\"weather\",\"args\":{\"city\":\"Rome\"}},\"thoughtSignature\":\"signature-2\"}]},\"finishReason\":\"STOP\"}]}");
            Assert.AreEqual("", response.Content);
            var request = new UnaiChatRequest { Messages = new() { UnaiChatMessage.AssistantWithToolCalls(response.ToolCalls) } };
            var parts = Payload(p, request.Clone())["contents"][0]["parts"];
            Assert.AreEqual("signature-1", parts[0]["thoughtSignature"].ToString());
            Assert.AreEqual("signature-2", parts[1]["thoughtSignature"].ToString());
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
