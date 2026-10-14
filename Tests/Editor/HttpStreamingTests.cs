using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnAI.Config;
using UnAI.Http;
using UnAI.Models;
using UnAI.Providers.OpenAICompatible;
using UnAI.Streaming;
using UnityEngine.TestTools;
using UnityEngine.Networking;

namespace UnAI.Tests
{
    public class HttpStreamingTests
    {
        [TestCase(500, UnityWebRequest.Result.ConnectionError, UnaiErrorType.Network)]
        [TestCase(401, UnityWebRequest.Result.ConnectionError, UnaiErrorType.Network)]
        [TestCase(500, UnityWebRequest.Result.ProtocolError, UnaiErrorType.ServerError)]
        [TestCase(401, UnityWebRequest.Result.ProtocolError, UnaiErrorType.Authentication)]
        public void TransportFailuresTakePrecedenceOverPlaceholderHttpStatuses(
            int statusCode, UnityWebRequest.Result result, UnaiErrorType expected)
        {
            var classify = typeof(UnaiHttpClient).GetMethod("ClassifyError", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.AreEqual(expected, classify.Invoke(null, new object[] { statusCode, result }));
        }

        private sealed class Provider : OpenAICompatibleProvider
        {
            public ISseLineParser Parser() => CreateStreamParser();
        }

        private static HttpListener Listen(out string url)
        {
            var portFinder = new TcpListener(IPAddress.Loopback, 0);
            portFinder.Start();
            int port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
            portFinder.Stop();
            url = $"http://localhost:{port}";
            var listener = new HttpListener();
            listener.Prefixes.Add(url + "/");
            listener.Start();
            return listener;
        }

        private static async Task Reply(HttpListener listener, string body, int statusCode = 200)
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/event-stream";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        private static IEnumerator Wait(Task task, double seconds = 8)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(task.IsCompleted, "HTTP task must always settle before the test deadline.");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        [UnityTest]
        public IEnumerator TrailingUsageAndUnicodeReachFinalResponseInEditMode()
        {
            using var listener = Listen(out string url);
            var provider = new Provider();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = url, DefaultModel = "test", MaxRetries = 0 });
            var server = Reply(listener,
                "data: {\"choices\":[{\"delta\":{\"content\":\"café 😀\"},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":3,\"total_tokens\":5}}\n\n" +
                "data: [DONE]\n\n");
            UnaiChatResponse response = null;
            UnaiErrorInfo error = null;
            var task = provider.ChatStreamAsync(new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") } },
                _ => { }, r => response = r, e => error = e);
            yield return Wait(task);
            yield return Wait(server);
            Assert.IsNull(error);
            Assert.AreEqual("café 😀", response.Content);
            Assert.AreEqual("stop", response.FinishReason);
            Assert.AreEqual(5, response.Usage.TotalTokens);
        }

        [UnityTest]
        public IEnumerator StalledStreamHonorsTimeoutWithoutCompletion()
        {
            using var listener = Listen(out string url);
            var provider = new Provider();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = url, DefaultModel = "test", MaxRetries = 0, TimeoutSeconds = 1 });
            var accept = listener.GetContextAsync();
            UnaiErrorInfo error = null;
            bool complete = false;
            var task = provider.ChatStreamAsync(new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") } },
                _ => { }, _ => complete = true, e => error = e);
            yield return Wait(task);
            Assert.IsNotNull(error);
            Assert.IsFalse(complete);
            if (accept.IsCompleted) accept.Result.Response.Close();
        }

        [UnityTest]
        public IEnumerator CancellationSettlesStalledStream()
        {
            using var listener = Listen(out string url);
            var provider = new Provider();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = url, DefaultModel = "test", MaxRetries = 0 });
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var task = provider.ChatStreamAsync(new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") } },
                _ => { }, cancellationToken: cts.Token);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(task.IsCanceled);
        }

        [UnityTest]
        public IEnumerator CancellationFromDeltaDoesNotCompleteBufferedResponse()
        {
            using var listener = Listen(out string url);
            var provider = new Provider();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = url, DefaultModel = "test", MaxRetries = 0 });
            var server = Reply(listener,
                "data: {\"choices\":[{\"delta\":{\"content\":\"first\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"second\"}}]}\n\n" +
                "data: [DONE]\n\n");
            using var cts = new CancellationTokenSource();
            int deltas = 0;
            bool completed = false;
            var task = provider.ChatStreamAsync(new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") } },
                _ => { deltas++; cts.Cancel(); }, _ => completed = true, cancellationToken: cts.Token);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(task.IsCanceled);
            Assert.IsFalse(completed);
            Assert.AreEqual(1, deltas, "Do not deliver more buffered deltas after cancellation.");
            yield return Wait(server);
        }

        [UnityTest]
        public IEnumerator PartialOutputIsNotAutomaticallyRetried()
        {
            using var listener = Listen(out string url);
            var provider = new Provider();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = url, DefaultModel = "test", MaxRetries = 3 });
            var server = Reply(listener,
                "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n", 500);
            UnaiErrorInfo error = null;
            var task = provider.ChatStreamAsync(new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") } },
                _ => { }, onError: e => error = e);
            yield return Wait(task);
            yield return Wait(server);
            Assert.IsTrue(error.HasPartialOutput);
            Assert.AreEqual(UnaiErrorType.ServerError, error.ErrorType);
        }

        [UnityTest]
        public IEnumerator CompletionCallbackExceptionFaultsTask()
        {
            using var listener = Listen(out string url);
            var provider = new Provider();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = url, DefaultModel = "test", MaxRetries = 0 });
            var server = Reply(listener, "data: [DONE]\n\n");
            var task = provider.ChatStreamAsync(new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") } },
                _ => { }, _ => throw new InvalidOperationException("callback failure"));
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(task.IsFaulted);
            Assert.AreEqual("callback failure", task.Exception.GetBaseException().Message);
            yield return Wait(server);
        }

        [UnityTest]
        public IEnumerator MalformedPayloadSettlesAsStreamingError()
        {
            using var listener = Listen(out string url);
            var provider = new Provider();
            provider.Initialize(new UnaiProviderConfig { BaseUrl = url, DefaultModel = "test", MaxRetries = 0 });
            var server = Reply(listener, "data: not-json\n\n");
            UnaiErrorInfo error = null;
            var task = provider.ChatStreamAsync(new UnaiChatRequest { Messages = new() { UnaiChatMessage.User("hello") } },
                _ => { }, onError: e => error = e);
            yield return Wait(task);
            yield return Wait(server);
            Assert.AreEqual(UnaiErrorType.StreamingError, error.ErrorType);
        }
    }
}
