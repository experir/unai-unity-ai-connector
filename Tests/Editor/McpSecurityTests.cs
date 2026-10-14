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
using UnityEngine.TestTools;

namespace UnAI.Tests
{
    public class McpSecurityTests
    {
        private sealed class Tool : IUnaiTool
        {
            public bool ReadOnly;
            public int Calls;
            public string Name;
            public UnaiToolDefinition Definition => new() { Name = Name, IsReadOnly = ReadOnly };
            public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(new UnaiToolResult { Content = "ok" });
            }
        }

        [TestCase(null, true)]
        [TestCase("http://localhost:3389", true)]
        [TestCase("https://foreign.example", false)]
        [TestCase("null", false)]
        [TestCase("", false)]
        [TestCase("http://localhost:3389/", false)]
        public void OriginsAreExplicitlyRestricted(string origin, bool expected)
            => Assert.AreEqual(expected, UnaiMcpAccessPolicy.IsAllowedOrigin(origin, "http://localhost:3389"));

        [Test]
        public void TokenRequiredAndGeneratedTokensAreIndependent()
        {
            string token = UnaiMcpAccessPolicy.CreateToken();
            Assert.AreNotEqual(token, UnaiMcpAccessPolicy.CreateToken());
            Assert.IsFalse(UnaiMcpAccessPolicy.IsAuthorized(null, token));
            Assert.IsFalse(UnaiMcpAccessPolicy.IsAuthorized("Bearer wrong", token));
            Assert.IsTrue(UnaiMcpAccessPolicy.IsAuthorized("Bearer " + token, token));
            Assert.IsFalse(UnaiMcpAccessPolicy.IsAuthorized("Bearer ", ""));
        }

        [Test]
        public async Task MutatingToolsAreHiddenAndCannotExecuteWithoutOptIn()
        {
            var read = new Tool { Name = "read", ReadOnly = true };
            var write = new Tool { Name = "write" };
            var tools = new UnaiToolRegistry();
            tools.Register(read);
            tools.Register(write);
            var listing = JObject.Parse(await UnaiMcpProtocol.HandleRequest(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", tools, default));
            Assert.AreEqual(1, ((JArray)listing["result"]["tools"]).Count);
            string call = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"write\"}}";
            Assert.IsNotNull(JObject.Parse(await UnaiMcpProtocol.HandleRequest(call, tools, default))["error"]);
            Assert.AreEqual(0, write.Calls);
            Assert.IsNotNull(JObject.Parse(await UnaiMcpProtocol.HandleRequest(call, tools, default, true))["result"]);
            Assert.AreEqual(1, write.Calls);
        }

        private static async Task<int> Post(string url, string token = null, string origin = null)
        {
            var request = WebRequest.CreateHttp(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json, text/event-stream";
            if (token != null) request.Headers["Authorization"] = "Bearer " + token;
            if (origin != null) request.Headers["Origin"] = origin;
            using (var writer = new StreamWriter(await request.GetRequestStreamAsync()))
                await writer.WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"fixture\",\"version\":\"1\"}}}");
            try { using var response = (HttpWebResponse)await request.GetResponseAsync(); return (int)response.StatusCode; }
            catch (WebException ex) when (ex.Response is HttpWebResponse)
            { using var response = (HttpWebResponse)ex.Response; return (int)response.StatusCode; }
        }

        [UnityTest]
        public IEnumerator HttpRejectsForeignOriginsAndMissingTokensBeforeDispatch()
        {
            var finder = new TcpListener(IPAddress.Loopback, 0);
            finder.Start(); int port = ((IPEndPoint)finder.LocalEndpoint).Port; finder.Stop();
            var server = new UnaiMcpServer();
            server.Start(port, new UnaiToolRegistry());
            try
            {
                Assert.IsTrue(server.IsRunning);
                var foreign = Post(server.Url, server.AccessToken, "https://foreign.example");
                var missing = Post(server.Url);
                var allowed = Post(server.Url, server.AccessToken);
                var all = Task.WhenAll(foreign, missing, allowed);
                var until = DateTime.UtcNow.AddSeconds(8);
                while (!all.IsCompleted && DateTime.UtcNow < until) yield return null;
                Assert.IsTrue(all.IsCompleted);
                if (all.IsFaulted) throw all.Exception.GetBaseException();
                Assert.AreEqual(403, foreign.Result);
                Assert.AreEqual(401, missing.Result);
                Assert.AreEqual(200, allowed.Result);
            }
            finally { server.Stop(); }
        }
    }
}
