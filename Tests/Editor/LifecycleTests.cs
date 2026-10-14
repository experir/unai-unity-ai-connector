using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnAI.Tests
{
    public class LifecycleTests
    {
        [Test]
        public void VersionMatchesInstalledPackageAndRuntimeConstant()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnaiVersion).Assembly);
            Assert.IsNotNull(package);
            Assert.AreEqual(package.version, UnaiVersion.Get());
            Assert.AreEqual(package.version, UnaiVersion.Current);
        }

        [Test]
        public void NewConfigurationsReplaceBuiltInsAndDisabledProvidersDisappear()
        {
            var first = ScriptableObject.CreateInstance<UnaiGlobalConfig>();
            var second = ScriptableObject.CreateInstance<UnaiGlobalConfig>();
            try
            {
                UnaiProviderRegistry.Clear();
                UnaiProviderRegistry.InitializeBuiltInProviders(first);
                var before = UnaiProviderRegistry.Get("ollama");
                UnaiProviderRegistry.InitializeBuiltInProviders(second);
                Assert.AreNotSame(before, UnaiProviderRegistry.Get("ollama"));
                second.Ollama.Enabled = false;
                UnaiProviderRegistry.InitializeBuiltInProviders(second, force: true);
                Assert.IsNull(UnaiProviderRegistry.Get("ollama"));
                Assert.IsTrue(UnaiProviderRegistry.Unregister("lmstudio"));
                Assert.IsFalse(UnaiProviderRegistry.Has("lmstudio"));
            }
            finally
            {
                UnaiProviderRegistry.Clear();
                Object.DestroyImmediate(first); Object.DestroyImmediate(second);
            }
        }

        [UnityTest]
        public IEnumerator PlayModeWithoutReloadResetsStaticStateAndRestoresManager()
        {
            bool enabled = EditorSettings.enterPlayModeOptionsEnabled;
            var options = EditorSettings.enterPlayModeOptions;
            var config = ScriptableObject.CreateInstance<UnaiGlobalConfig>();
            var go = new GameObject("UNAI lifecycle fixture");
            var manager = go.AddComponent<UnaiManager>();
            manager.Initialize(config);
            manager.Cache.Put("old", new UnaiChatResponse { Content = "stale" });
            // A pending custom provider should not survive the runtime subsystem boundary.
            UnaiProviderRegistry.RegisterLazy<UnAI.Providers.LMStudio.LMStudioProvider>(
                new UnaiProviderConfig { ProviderId = "stale", BaseUrl = "http://localhost:1" });
            try
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                yield return new EnterPlayMode();
                bool stale = UnaiProviderRegistry.Has("stale");
                bool restored = UnaiManager.Instance == manager;
                int cached = manager.Cache.Count;
                yield return new ExitPlayMode();
                Assert.IsFalse(stale);
                Assert.IsTrue(restored);
                Assert.AreEqual(0, cached);
            }
            finally
            {
                EditorSettings.enterPlayModeOptionsEnabled = enabled;
                EditorSettings.enterPlayModeOptions = options;
                Object.DestroyImmediate(go); Object.DestroyImmediate(config);
                UnaiProviderRegistry.Clear();
            }
        }
    }
}
