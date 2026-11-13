using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnAI.Agent;
using UnAI.Agent.Http;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnAI.Tools;
using UnityEngine.TestTools;

namespace UnAI.Tests
{
    public class AgentHttpApiTests
    {
        /// <summary>Echoes the number of user messages it has seen; asks for roll_dice when told "roll".</summary>
        private sealed class FakeProvider : IUnaiProvider
        {
            public string ProviderId => "fake";
            public string DisplayName => "Fake";
            public bool IsConfigured => true;
            public IReadOnlyList<UnaiModelInfo> KnownModels => Array.Empty<UnaiModelInfo>();
            public bool SupportsToolCalling => true;
            public void Initialize(UnaiProviderConfig config) { }

            public Task<UnaiChatResponse> ChatAsync(UnaiChatRequest request, CancellationToken ct = default)
            {
                var last = request.Messages.Last();
                if (last.Role == UnaiRole.Tool)
                    return Task.FromResult(new UnaiChatResponse { Content = "You rolled " + last.Content });
                if (last.Content == "roll")
                    return Task.FromResult(new UnaiChatResponse
                    {
                        ToolCalls = new List<UnaiToolCall> { new() { Id = "c1", ToolName = "roll_dice", ArgumentsJson = "{\"sides\":6}" } }
                    });
                int users = request.Messages.Count(m => m.Role == UnaiRole.User);
                return Task.FromResult(new UnaiChatResponse { Content = $"echo {users}: {last.Content}" });
            }

            public Task ChatStreamAsync(UnaiChatRequest request, Action<UnaiStreamDelta> onDelta,
                Action<UnaiChatResponse> onComplete = null, Action<UnaiErrorInfo> onError = null, CancellationToken ct = default)
                => throw new NotSupportedException();
        }

        private sealed class DiceTool : IUnaiTool
        {
            public UnaiToolDefinition Definition { get; } = new() { Name = "roll_dice", Description = "Roll a die", IsReadOnly = true };
            public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
                => Task.FromResult(new UnaiToolResult { Content = "4" });
        }

        private UnaiAgentHttpServer _server;
        private static readonly HttpClient Client = new();

        [SetUp]
        public void StartServer()
        {
            var finder = new TcpListener(IPAddress.Loopback, 0);
            finder.Start(); int port = ((IPEndPoint)finder.LocalEndpoint).Port; finder.Stop();
            var tools = new UnaiToolRegistry();
            tools.Register(new DiceTool());
            _server = new UnaiAgentHttpServer(() => new UnaiAgent(new FakeProvider(), new UnaiAgentConfig { MaxSteps = 4 }, tools));
            _server.Start(port);
        }

        [TearDown]
        public void StopServer() => _server.Stop();

        private Task<(int status, JObject body)> Send(string method, string path, object body = null,
            string token = "valid", string origin = null)
        {
            string url = _server.Url.TrimEnd('/') + path;
            string auth = token == "valid" ? _server.AccessToken : token;
            return Task.Run(async () =>
            {
                var message = new HttpRequestMessage(new HttpMethod(method), url);
                if (auth != null) message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + auth);
                if (origin != null) message.Headers.TryAddWithoutValidation("Origin", origin);
                if (body != null) message.Content = new StringContent(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json");
                var response = await Client.SendAsync(message);
                string text = await response.Content.ReadAsStringAsync();
                return ((int)response.StatusCode, string.IsNullOrEmpty(text) ? new JObject() : JObject.Parse(text));
            });
        }

        private static IEnumerator Wait<T>(Task<T> task)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(task.IsCompleted, "timed out");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        [UnityTest]
        public IEnumerator RejectsMissingTokensWrongTokensAndWebPages()
        {
            var noToken = Send("GET", "/health", token: null);
            yield return Wait(noToken);
            Assert.AreEqual(401, noToken.Result.status);

            var wrong = Send("GET", "/health", token: "guess");
            yield return Wait(wrong);
            Assert.AreEqual(401, wrong.Result.status);

            var page = Send("POST", "/agent/chat", new { message = "hi" }, origin: "https://evil.example");
            yield return Wait(page);
            Assert.AreEqual(403, page.Result.status);

            var ok = Send("GET", "/health");
            yield return Wait(ok);
            Assert.AreEqual(200, ok.Result.status);
            Assert.AreEqual("ok", ok.Result.body["status"].ToString());
        }

        [UnityTest]
        public IEnumerator SessionsKeepTheirOwnMemoryUntilReset()
        {
            var first = Send("POST", "/agent/chat", new { message = "hello", session = "a" });
            yield return Wait(first);
            Assert.AreEqual(200, first.Result.status);
            Assert.AreEqual("echo 1: hello", first.Result.body["reply"].ToString());

            var second = Send("POST", "/agent/chat", new { message = "again", session = "a" });
            yield return Wait(second);
            Assert.AreEqual("echo 2: again", second.Result.body["reply"].ToString(), "Session a remembers");

            var other = Send("POST", "/agent/chat", new { message = "hi", session = "b" });
            yield return Wait(other);
            Assert.AreEqual("echo 1: hi", other.Result.body["reply"].ToString(), "Session b is separate");

            var reset = Send("POST", "/agent/reset", new { session = "a" });
            yield return Wait(reset);
            var fresh = Send("POST", "/agent/chat", new { message = "new", session = "a" });
            yield return Wait(fresh);
            Assert.AreEqual("echo 1: new", fresh.Result.body["reply"].ToString(), "Reset clears memory");
        }

        [UnityTest]
        public IEnumerator ReportsToolCallsWithTheirResults()
        {
            var roll = Send("POST", "/agent/chat", new { message = "roll" });
            yield return Wait(roll);
            Assert.AreEqual(200, roll.Result.status);
            Assert.AreEqual("You rolled 4", roll.Result.body["reply"].ToString());
            var call = roll.Result.body["toolCalls"][0];
            Assert.AreEqual("roll_dice", call["name"].ToString());
            Assert.AreEqual(6, (int)call["arguments"]["sides"]);
            Assert.AreEqual("4", call["result"].ToString());
        }

        [UnityTest]
        public IEnumerator ValidatesRequests()
        {
            var empty = Send("POST", "/agent/chat", new { message = "  " });
            yield return Wait(empty);
            Assert.AreEqual(400, empty.Result.status);

            var wrongMethod = Send("GET", "/agent/chat");
            yield return Wait(wrongMethod);
            Assert.AreEqual(405, wrongMethod.Result.status);

            var unknown = Send("GET", "/nope");
            yield return Wait(unknown);
            Assert.AreEqual(404, unknown.Result.status);
        }
    }
}
