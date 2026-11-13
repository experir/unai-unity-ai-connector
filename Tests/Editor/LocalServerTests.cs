using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnityEngine.TestTools;

namespace UnAI.Tests
{
    public class LocalServerTests
    {
        private static int FreePort()
        {
            var finder = new TcpListener(IPAddress.Loopback, 0);
            finder.Start();
            int port = ((IPEndPoint)finder.LocalEndpoint).Port;
            finder.Stop();
            return port;
        }

        private static UnaiProviderBase Provider(UnaiProviderConfig config)
        {
            var type = typeof(UnaiProviderBase).Assembly.GetTypes().First(t => !t.IsAbstract
                && typeof(UnaiProviderBase).IsAssignableFrom(t)
                && ((UnaiProviderBase)Activator.CreateInstance(t)).ProviderId == config.ProviderId);
            var provider = (UnaiProviderBase)Activator.CreateInstance(type);
            provider.Initialize(config);
            return provider;
        }

        /// <summary>A llama.cpp config pointing at a port where nothing listens, with no way to start a server.</summary>
        private static UnaiProviderConfig OffServer() => new()
        {
            ProviderId = "llamacpp", BaseUrl = $"http://localhost:{FreePort()}", DefaultModel = "fixture", MaxRetries = 3
        };

        private static UnaiChatRequest Hello() => new() { Messages = new() { UnaiChatMessage.User("hi") } };

        [Test]
        public void OnlyLocalProvidersOnThisMachineAreManaged()
        {
            Assert.IsTrue(UnaiLocalServer.IsLocalProvider("ollama"));
            Assert.IsTrue(UnaiLocalServer.IsLocalProvider("lmstudio"));
            Assert.IsTrue(UnaiLocalServer.IsLocalProvider("llamacpp"));
            Assert.IsFalse(UnaiLocalServer.IsLocalProvider("openai"));
            Assert.IsFalse(UnaiLocalServer.IsLocalProvider(null));

            Assert.IsTrue(UnaiLocalServer.IsOnThisMachine("http://localhost:11434"));
            Assert.IsTrue(UnaiLocalServer.IsOnThisMachine("http://127.0.0.1:1234/v1"));
            Assert.IsFalse(UnaiLocalServer.IsOnThisMachine("http://192.168.1.20:11434"));
            Assert.IsFalse(UnaiLocalServer.IsOnThisMachine("not a url"));
        }

        [Test]
        public void StartCommandUsesTheConfiguredExecutableAndArguments()
        {
            string exe = Path.Combine(Path.GetTempPath(), "unai fake server.exe");
            File.WriteAllText(exe, "");
            try
            {
                var command = UnaiLocalServer.ResolveCommand(new UnaiProviderConfig
                {
                    ProviderId = "llamacpp", ServerStartCommand = $"\"{exe}\" -m model.gguf --port 8080"
                });
                Assert.IsTrue(command.HasValue);
                Assert.AreEqual(exe, command.Value.file);
                Assert.AreEqual("-m model.gguf --port 8080", command.Value.args);
            }
            finally
            {
                File.Delete(exe);
            }

            Assert.IsNull(UnaiLocalServer.ResolveCommand(new UnaiProviderConfig { ProviderId = "llamacpp" }),
                "llama.cpp has no default: it needs a model path");
            Assert.IsNull(UnaiLocalServer.ResolveCommand(new UnaiProviderConfig
            {
                ProviderId = "ollama", ServerStartCommand = Path.Combine(Path.GetTempPath(), "missing-ollama.exe") + " serve"
            }));
        }

        [Test]
        public void DisabledOrRemoteServersAreLeftAlone()
        {
            var disabled = OffServer();
            disabled.AutoStartServer = false;
            var task = UnaiLocalServer.EnsureRunningAsync(disabled);
            Assert.IsTrue(task.IsCompleted);
            Assert.IsNull(task.Result);

            var remote = new UnaiProviderConfig { ProviderId = "ollama", BaseUrl = "http://10.0.0.5:11434" };
            task = UnaiLocalServer.EnsureRunningAsync(remote);
            Assert.IsTrue(task.IsCompleted);
            Assert.IsNull(task.Result);
        }

        [UnityTest]
        public IEnumerator ChatFailsFastWithAClearErrorWhenTheServerCannotBeStarted()
        {
            var chat = Provider(OffServer()).ChatAsync(Hello());
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!chat.IsCompleted && DateTime.UtcNow < until) yield return null;

            Assert.IsTrue(chat.IsFaulted, "No retries against a server that is off");
            var error = ((UnaiRequestException)chat.Exception.GetBaseException()).ErrorInfo;
            Assert.AreEqual(UnaiErrorType.LocalServerUnavailable, error.ErrorType);
            StringAssert.Contains("Server Start Command", error.UserFriendlyMessage);
            Assert.IsTrue(error.IsRetryable, "Lets the manager fall back to another provider");
        }

        [UnityTest]
        public IEnumerator StreamingReportsTheServerErrorThroughOnError()
        {
            UnaiErrorInfo reported = null;
            bool completed = false;
            Task stream = Provider(OffServer()).ChatStreamAsync(Hello(),
                onDelta: _ => { }, onComplete: _ => completed = true, onError: e => reported = e);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!stream.IsCompleted && DateTime.UtcNow < until) yield return null;

            Assert.IsTrue(stream.IsCompleted);
            Assert.IsFalse(completed);
            Assert.AreEqual(UnaiErrorType.LocalServerUnavailable, reported?.ErrorType);
        }
    }
}
