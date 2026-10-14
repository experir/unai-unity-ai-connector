using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.Tests
{
    public class RequestTests
    {
        private sealed class Provider : IUnaiProvider
        {
            public string ProviderId { get; set; }
            public bool Fail;
            public string SeenModel;
            public int Calls;
            public bool IsConfigured => true;
            public string DisplayName => ProviderId;
            public bool SupportsToolCalling => true;
            public System.Collections.Generic.IReadOnlyList<UnaiModelInfo> KnownModels => Array.Empty<UnaiModelInfo>();
            public void Initialize(UnaiProviderConfig config) { }
            public Task<UnaiChatResponse> ChatAsync(UnaiChatRequest request, CancellationToken cancellationToken = default)
            {
                Calls++;
                SeenModel = request.Model;
                request.Messages[0].Content = "provider changed snapshot";
                if (Fail) throw new UnaiRequestException(new UnaiErrorInfo { ErrorType = UnaiErrorType.Network });
                return Task.FromResult(new UnaiChatResponse { Content = "ok", Model = SeenModel });
            }
            public async Task ChatStreamAsync(UnaiChatRequest request, Action<UnaiStreamDelta> onDelta,
                Action<UnaiChatResponse> onComplete = null, Action<UnaiErrorInfo> onError = null, CancellationToken cancellationToken = default)
            {
                try { onComplete?.Invoke(await ChatAsync(request, cancellationToken)); }
                catch (UnaiRequestException ex) { onError?.Invoke(ex.ErrorInfo); }
            }
        }

        [TestCase(false, null)]
        [TestCase(false, "explicit-primary-model")]
        [TestCase(true, null)]
        [TestCase(true, "explicit-primary-model")]
        public async Task FallbackUsesBackupDefaultWithoutMutatingCaller(bool stream, string model)
        {
            var config = ScriptableObject.CreateInstance<UnaiGlobalConfig>();
            var go = new GameObject();
            try
            {
                UnaiProviderRegistry.Clear();
                config.OpenAI.DefaultModel = "primary-default";
                config.Anthropic.DefaultModel = "backup-default";
                config.FallbackProviderIds.Add("anthropic");
                var manager = go.AddComponent<UnaiManager>();
                manager.Initialize(config);
                var primary = new Provider { ProviderId = "openai", Fail = true };
                var backup = new Provider { ProviderId = "anthropic" };
                UnaiProviderRegistry.Register(primary);
                UnaiProviderRegistry.Register(backup);
                var request = new UnaiChatRequest { Model = model };
                request.Messages.Add(UnaiChatMessage.User("original"));
                if (stream) await manager.ChatStreamAsync(request, _ => { });
                else await manager.ChatAsync(request);
                Assert.AreEqual(model ?? "primary-default", primary.SeenModel);
                Assert.AreEqual("backup-default", backup.SeenModel);
                Assert.AreEqual(model, request.Model);
                Assert.IsFalse(request.Stream);
                Assert.AreEqual("original", request.Messages[0].Content);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(config);
                UnaiProviderRegistry.Clear();
            }
        }

        [Test]
        public async Task ImplicitModelCachesAndEndpointChangesInvalidate()
        {
            var config = ScriptableObject.CreateInstance<UnaiGlobalConfig>();
            var go = new GameObject();
            try
            {
                UnaiProviderRegistry.Clear();
                var manager = go.AddComponent<UnaiManager>();
                manager.Initialize(config);
                var provider = new Provider { ProviderId = "openai" };
                UnaiProviderRegistry.Register(provider);
                var request = new UnaiChatRequest { UseCache = true, Messages = new() { UnaiChatMessage.User("hello") } };
                await manager.ChatAsync(request);
                await manager.ChatAsync(request);
                Assert.AreEqual(1, provider.Calls);
                config.OpenAI.BaseUrl = "https://different.example";
                await manager.ChatAsync(request);
                Assert.AreEqual(2, provider.Calls);
                Assert.IsNull(request.Model);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(config);
                UnaiProviderRegistry.Clear();
            }
        }

        [Test]
        public void ClonePreservesAndIsolatesTools()
        {
            var request = new UnaiChatRequest
            {
                Tools = new() { new UnaiToolDefinition { Name = "tool", ParametersSchema = JObject.Parse("{\"type\":\"object\"}") } }
            };
            request.Messages.Add(UnaiChatMessage.ToolResult("call-1", "tool", "answer"));
            var copy = request.Clone();
            copy.Tools[0].ParametersSchema["type"] = "changed";
            Assert.AreEqual("object", request.Tools[0].ParametersSchema["type"].ToString());
            Assert.AreEqual("call-1", copy.Messages[0].ToolCallId);
            Assert.AreEqual("tool", copy.Messages[0].ToolName);
        }
    }
}
