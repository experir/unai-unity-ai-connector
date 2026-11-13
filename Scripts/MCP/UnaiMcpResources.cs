using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnAI.MCP
{
    /// <summary>A read-only piece of Unity context that MCP clients can list and read (MCP "resources").</summary>
    public sealed class UnaiMcpResource
    {
        /// <summary>Unique URI, e.g. unity://scene/hierarchy.</summary>
        public string Uri;
        public string Name;
        public string Description;
        public string MimeType = "text/plain";
        /// <summary>Produces the content. Runs on the Unity main thread.</summary>
        public Func<CancellationToken, Task<string>> Read;
    }

    /// <summary>
    /// Resources exposed by the MCP server: project info, scene hierarchy, selection and recent console
    /// messages, plus any registered with <see cref="Register"/>. Resources are read-only, so they are
    /// available even when mutating tools are disabled.
    /// </summary>
    [InitializeOnLoad]
    public static class UnaiMcpResources
    {
        private const int MaxConsoleEntries = 200;
        private const int MaxHierarchyObjects = 2000;

        private static readonly Dictionary<string, UnaiMcpResource> Custom = new(StringComparer.Ordinal);
        private static readonly Queue<string> ConsoleEntries = new();
        private static readonly object ConsoleLock = new();

        static UnaiMcpResources()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
        }

        /// <summary>Adds or replaces a resource. Built-in URIs can't be replaced.</summary>
        public static void Register(UnaiMcpResource resource)
        {
            if (resource == null || string.IsNullOrEmpty(resource.Uri) || resource.Read == null)
                throw new ArgumentException("A resource needs a Uri and a Read function.");
            if (BuiltIn.Any(r => r.Uri == resource.Uri))
                throw new ArgumentException($"'{resource.Uri}' is a built-in resource.");
            Custom[resource.Uri] = resource;
        }

        public static bool Unregister(string uri) => Custom.Remove(uri);

        public static IReadOnlyList<UnaiMcpResource> All => BuiltIn.Concat(Custom.Values).ToList();

        public static UnaiMcpResource Find(string uri) => All.FirstOrDefault(r => r.Uri == uri);

        private static readonly UnaiMcpResource[] BuiltIn =
        {
            new()
            {
                Uri = "unity://project/info", Name = "Project info",
                Description = "Unity version, product, build target, render pipeline, open scenes and scenes in the build.",
                Read = _ => Task.FromResult(ProjectInfo())
            },
            new()
            {
                Uri = "unity://scene/hierarchy", Name = "Scene hierarchy",
                Description = "Every GameObject in the open scenes, indented by parent, with its components.",
                Read = _ => Task.FromResult(SceneHierarchy())
            },
            new()
            {
                Uri = "unity://selection", Name = "Selection",
                Description = "The objects selected in the Editor, with transform details for GameObjects.",
                Read = _ => Task.FromResult(SelectionInfo())
            },
            new()
            {
                Uri = "unity://console/recent", Name = "Recent console messages",
                Description = $"The last {MaxConsoleEntries} Console messages since the Editor loaded, oldest first.",
                Read = _ => Task.FromResult(RecentConsole())
            }
        };

        private static string ProjectInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Product: {PlayerSettings.productName} ({PlayerSettings.companyName})");
            sb.AppendLine($"Unity: {Application.unityVersion}");
            sb.AppendLine($"Build target: {EditorUserBuildSettings.activeBuildTarget}");
            var pipeline = GraphicsSettings.currentRenderPipeline;
            sb.AppendLine($"Render pipeline: {(pipeline != null ? pipeline.GetType().Name + " (" + pipeline.name + ")" : "Built-in")}");
            sb.AppendLine($"Play Mode: {(EditorApplication.isPlaying ? "playing" : "editing")}");
            sb.AppendLine("Open scenes:");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                sb.AppendLine($"  {(string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path)}{(scene.isDirty ? " *modified" : "")}");
            }
            sb.AppendLine("Scenes in build:");
            foreach (var scene in EditorBuildSettings.scenes)
                sb.AppendLine($"  {(scene.enabled ? "" : "(disabled) ")}{scene.path}");
            return sb.ToString();
        }

        private static string SceneHierarchy()
        {
            var sb = new StringBuilder();
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                sb.AppendLine($"Scene: {scene.name}");
                foreach (var root in scene.GetRootGameObjects())
                    if (!AppendObject(sb, root.transform, 1, ref count)) break;
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                sb.AppendLine($"Prefab being edited: {stage.assetPath}");
                AppendObject(sb, stage.prefabContentsRoot.transform, 1, ref count);
            }
            if (count >= MaxHierarchyObjects)
                sb.AppendLine($"(Stopped after {MaxHierarchyObjects} objects.)");
            return sb.ToString();
        }

        private static bool AppendObject(StringBuilder sb, Transform t, int depth, ref int count)
        {
            if (++count > MaxHierarchyObjects) return false;
            var components = t.GetComponents<Component>()
                .Where(c => c != null && c is not Transform)
                .Select(c => c.GetType().Name);
            sb.Append(' ', depth * 2).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" (inactive)");
            string list = string.Join(", ", components);
            if (list.Length > 0) sb.Append(" [").Append(list).Append(']');
            sb.AppendLine();
            foreach (Transform child in t)
                if (!AppendObject(sb, child, depth + 1, ref count)) return false;
            return true;
        }

        private static string SelectionInfo()
        {
            if (Selection.objects.Length == 0) return "Nothing is selected.";
            var sb = new StringBuilder();
            foreach (var obj in Selection.objects)
            {
                if (obj is GameObject go)
                {
                    var t = go.transform;
                    sb.AppendLine($"GameObject: {Path(t)}");
                    sb.AppendLine($"  position {t.position}, rotation {t.eulerAngles}, scale {t.localScale}");
                    sb.AppendLine($"  components: {string.Join(", ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name))}");
                }
                else
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    sb.AppendLine($"{obj.GetType().Name}: {(string.IsNullOrEmpty(path) ? obj.name : path)}");
                }
            }
            return sb.ToString();
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (message != null && message.StartsWith("[UNAI MCP]", StringComparison.Ordinal)) return; // the server's own chatter
            string entry = type is LogType.Error or LogType.Exception or LogType.Assert
                ? $"[{type}] {message}\n{FirstLines(stackTrace, 3)}"
                : $"[{type}] {message}";
            lock (ConsoleLock)
            {
                ConsoleEntries.Enqueue(entry.TrimEnd());
                while (ConsoleEntries.Count > MaxConsoleEntries) ConsoleEntries.Dequeue();
            }
        }

        private static string FirstLines(string text, int lines) =>
            string.Join("\n", (text ?? "").Split('\n').Take(lines).Select(l => "    " + l.Trim()));

        private static string RecentConsole()
        {
            lock (ConsoleLock)
                return ConsoleEntries.Count == 0 ? "No Console messages yet." : string.Join("\n", ConsoleEntries);
        }
    }
}
