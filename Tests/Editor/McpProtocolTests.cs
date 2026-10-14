using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnAI.MCP;
using UnAI.Tools;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace UnAI.Tests
{
    public class McpProtocolTests
    {
        private sealed class DelayedTool : IUnaiTool
        {
            public int Calls;
            public UnaiToolDefinition Definition => new() { Name = "wait", IsReadOnly = true };
            public async Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
            {
                Calls++;
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return new UnaiToolResult { Content = "done" };
            }
        }

        private sealed class Reply
        {
            public int Status;
            public string Body;
            public string Session;
        }

        private static UnaiMcpServer Start(UnaiToolRegistry tools = null)
        {
            var finder = new TcpListener(IPAddress.Loopback, 0);
            finder.Start(); int port = ((IPEndPoint)finder.LocalEndpoint).Port; finder.Stop();
            var server = new UnaiMcpServer();
            server.Start(port, tools ?? new UnaiToolRegistry());
            Assert.IsTrue(server.IsRunning);
            return server;
        }

        private static async Task<Reply> Send(UnaiMcpServer server, string body, string session = null,
            string method = "POST", string version = null)
        {
            var request = WebRequest.CreateHttp(server.Url);
            request.Method = method;
            request.ContentType = "application/json";
            request.Accept = "application/json, text/event-stream";
            request.Headers["Authorization"] = "Bearer " + server.AccessToken;
            if (session != null) request.Headers["MCP-Session-Id"] = session;
            if (version != null) request.Headers["MCP-Protocol-Version"] = version;
            if (method == "POST")
            {
                request.ContentLength = System.Text.Encoding.UTF8.GetByteCount(body);
                using (var writer = new StreamWriter(await request.GetRequestStreamAsync())) await writer.WriteAsync(body);
            }
            HttpWebResponse response;
            try { response = (HttpWebResponse)await request.GetResponseAsync(); }
            catch (WebException ex) when (ex.Response is HttpWebResponse) { response = (HttpWebResponse)ex.Response; }
            catch (WebException ex) { throw new InvalidOperationException($"MCP fixture request failed: {method} {body}", ex); }
            using (response)
            using (var reader = new StreamReader(response.GetResponseStream()))
                return new Reply { Status = (int)response.StatusCode, Body = await reader.ReadToEndAsync(), Session = response.Headers["MCP-Session-Id"] };
        }

        private static string Initialize(string version) => $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"{version}\",\"capabilities\":{{}},\"clientInfo\":{{\"name\":\"fixture\",\"version\":\"1\"}}}}}}";
        private const string Initialized = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
        private const string Tools = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}";
        private static IEnumerator Await(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(task.IsCompleted, "MCP task must settle.");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        [TestCase("2025-06-18", "2025-06-18")]
        [TestCase("2025-11-25", "2025-11-25")]
        [TestCase("unknown", "2025-11-25")]
        public async Task InitializeNegotiatesSupportedVersions(string requested, string expected)
        {
            var result = JObject.Parse(await UnaiMcpProtocol.HandleRequest(Initialize(requested), new UnaiToolRegistry(), default));
            Assert.AreEqual(expected, result["result"]["protocolVersion"].ToString());
        }

        [TestCase("[]")]
        [TestCase("{\"jsonrpc\":\"2.0\",\"id\":1}")]
        [TestCase("{\"jsonrpc\":\"2.0\",\"id\":{},\"method\":\"ping\"}")]
        public async Task InvalidRpcShapeGetsAnError(string body)
            => Assert.IsNotNull(JObject.Parse(await UnaiMcpProtocol.HandleRequest(body, new UnaiToolRegistry(), default))["error"]);

        [Test]
        public async Task NullIdIsARequestAndResponsesAreAcknowledgedSilently()
        {
            string result = await UnaiMcpProtocol.HandleRequest("{\"jsonrpc\":\"2.0\",\"id\":null,\"method\":\"ping\"}", new UnaiToolRegistry(), default);
            Assert.IsNotNull(result);
            Assert.IsNull(await UnaiMcpProtocol.HandleRequest("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", new UnaiToolRegistry(), default));
        }

        [UnityTest]
        public IEnumerator HttpSessionsAndHeadersFollowNegotiatedProtocol()
        {
            var server = Start();
            try
            {
                var init = Send(server, Initialize("2025-06-18")); yield return Await(init);
                Assert.AreEqual(200, init.Result.Status); Assert.IsNotEmpty(init.Result.Session);
                var note = Send(server, Initialized, init.Result.Session); yield return Await(note);
                Assert.AreEqual(202, note.Result.Status); Assert.AreEqual("", note.Result.Body);
                var list = Send(server, Tools, init.Result.Session, version: "2025-06-18"); yield return Await(list);
                Assert.AreEqual(200, list.Result.Status);
                var invalid = Send(server, Tools, init.Result.Session, version: "bad"); yield return Await(invalid);
                Assert.AreEqual(400, invalid.Result.Status);
                var unknown = Send(server, Tools, "missing"); yield return Await(unknown); Assert.AreEqual(404, unknown.Result.Status);
                var get = Send(server, null, init.Result.Session, "GET"); yield return Await(get); Assert.AreEqual(405, get.Result.Status);
                var close = Send(server, null, init.Result.Session, "DELETE"); yield return Await(close); Assert.AreEqual(204, close.Result.Status);
                Assert.AreEqual(0, server.ConnectedClients);
            }
            finally { server.Stop(); }
        }

        [UnityTest]
        public IEnumerator UnityWebRequestClientCanInitializeAndListTools()
        {
            var server = Start();
            try
            {
                using var request = UnityWebRequest.Post(server.Url, Initialize("2025-11-25"), "application/json");
                request.SetRequestHeader("Accept", "application/json, text/event-stream");
                request.SetRequestHeader("Authorization", "Bearer " + server.AccessToken);
                yield return request.SendWebRequest();
                Assert.AreEqual(200, request.responseCode);
                string session = request.GetResponseHeader("MCP-Session-Id");
                using var note = UnityWebRequest.Post(server.Url, Initialized, "application/json");
                note.SetRequestHeader("Accept", "application/json, text/event-stream");
                note.SetRequestHeader("Authorization", "Bearer " + server.AccessToken);
                note.SetRequestHeader("MCP-Session-Id", session);
                yield return note.SendWebRequest();
                Assert.AreEqual(202, note.responseCode);
                using var list = UnityWebRequest.Post(server.Url, Tools, "application/json");
                list.SetRequestHeader("Accept", "application/json, text/event-stream");
                list.SetRequestHeader("Authorization", "Bearer " + server.AccessToken);
                list.SetRequestHeader("MCP-Session-Id", session);
                yield return list.SendWebRequest();
                Assert.AreEqual(200, list.responseCode);
                Assert.IsNotNull(JObject.Parse(list.downloadHandler.text)["result"]["tools"]);
            }
            finally { server.Stop(); }
        }

        [UnityTest]
        public IEnumerator CancellationAndDeadlinesSettleActiveTools()
        {
            var tool = new DelayedTool(); var tools = new UnaiToolRegistry(); tools.Register(tool);
            var server = Start(tools);
            try
            {
                var init = Send(server, Initialize("2025-11-25")); yield return Await(init);
                var note = Send(server, Initialized, init.Result.Session); yield return Await(note);
                var call = Send(server, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"wait\"}}", init.Result.Session);
                var until = DateTime.UtcNow.AddSeconds(5);
                while (tool.Calls == 0 && DateTime.UtcNow < until) yield return null;
                Assert.AreEqual(1, tool.Calls);
                var cancel = Send(server, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":3}}", init.Result.Session);
                yield return Await(cancel); yield return Await(call);
                Assert.AreEqual(202, cancel.Result.Status); Assert.AreEqual(408, call.Result.Status);
                server.RequestTimeoutSeconds = 1;
                var deadline = Send(server, "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"wait\"}}", init.Result.Session);
                yield return Await(deadline); Assert.AreEqual(408, deadline.Result.Status);
                server.MaxRequestBytes = 32;
                var large = Send(server, Initialize("2025-11-25")); yield return Await(large); Assert.AreEqual(413, large.Result.Status);
            }
            finally { server.Stop(); }
        }
    }
}
