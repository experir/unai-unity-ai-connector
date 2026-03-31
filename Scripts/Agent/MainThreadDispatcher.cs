// ============================================================
//  MainThreadDispatcher.cs  —  子线程 → Unity 主线程调度
// ============================================================
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnAI.Agent
{
    public class MainThreadDispatcher
#if UNITY_EDITOR
        : IDisposable
#else
        : MonoBehaviour
#endif
    {
        private static MainThreadDispatcher _instance;
        public  static MainThreadDispatcher Instance => _instance ??= Create();

        private readonly ConcurrentQueue<Action> _queue = new();

        private static MainThreadDispatcher Create()
        {
            var d = new MainThreadDispatcher();
#if UNITY_EDITOR
            EditorApplication.update += d.Tick;
#else
            var go = new GameObject("[MCP] MainThreadDispatcher");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MainThreadDispatcherMb>().Init(d);
#endif
            return d;
        }

        public Task<T> RunOnMainThread<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
            return tcs.Task;
        }

        public Task RunOnMainThread(Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() => { try { action(); tcs.SetResult(true); } catch (Exception e) { tcs.SetException(e); } });
            return tcs.Task;
        }

        public Task RunOnMainThread(Func<Task> asyncAction)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(async () => { try { await asyncAction(); tcs.SetResult(true); } catch (Exception e) { tcs.SetException(e); } });
            return tcs.Task;
        }

        internal void Tick()
        {
            int cap = 32;
            while (cap-- > 0 && _queue.TryDequeue(out var a))
                try { a(); } catch (Exception e) { McpLogger.Error($"Dispatcher error: {e.Message}"); }
        }

#if UNITY_EDITOR
        public void Dispose() { EditorApplication.update -= Tick; _instance = null; }
#endif
    }

#if !UNITY_EDITOR
    internal class MainThreadDispatcherMb : MonoBehaviour
    {
        private MainThreadDispatcher _d;
        public void Init(MainThreadDispatcher d) => _d = d;
        private void Update() => _d?.Tick();
    }
#endif

    // ── Logger ────────────────────────────────────────────────
    public enum McpLogLevel { Debug, Info, Warn, Error }

    public static class McpLogger
    {
        public static McpLogLevel MinLevel { get; set; } = McpLogLevel.Info;
        private const string T = "<color=#00BFFF>[MCP]</color>";

        public static void Debug(string m) { if (MinLevel <= McpLogLevel.Debug) UnityEngine.Debug.Log($"{T} [D] {m}"); }
        public static void Info(string m)  { if (MinLevel <= McpLogLevel.Info)  UnityEngine.Debug.Log($"{T} {m}"); }
        public static void Warn(string m)  { if (MinLevel <= McpLogLevel.Warn)  UnityEngine.Debug.LogWarning($"{T} ⚠ {m}"); }
        public static void Error(string m) { if (MinLevel <= McpLogLevel.Error) UnityEngine.Debug.LogError($"{T} ✖ {m}"); }
        public static void Exception(Exception e, string ctx = "")
            => UnityEngine.Debug.LogError($"{T} [{ctx}] {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
    }
}
