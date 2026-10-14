using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnAI.Http
{
    internal static class UnaiCoroutineRunner
    {
        private class Runner : MonoBehaviour { }

        private static Runner _instance;

        private static Runner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[UNAI CoroutineRunner]");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    if (Application.isPlaying)
                        UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<Runner>();
                }
                return _instance;
            }
        }

        public static Coroutine Run(IEnumerator coroutine)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (EditorRoutines.Count == 0)
                {
                    UnityEditor.EditorApplication.update += TickEditor;
                    UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopEditor;
                }
                EditorRoutines.Add(coroutine);
                return null;
            }
#endif
            return Instance.StartCoroutine(coroutine);
        }

        internal static IEnumerator Guard(IEnumerator routine, Action<Exception> onError, Action onAborted)
        {
            bool finished = false;
            try
            {
                while (true)
                {
                    bool moved = false;
                    Exception failure = null;
                    try { moved = routine.MoveNext(); }
                    catch (Exception ex) { failure = ex; }
                    if (failure != null)
                    {
                        finished = true;
                        onError(failure);
                        yield break;
                    }
                    if (!moved) { finished = true; yield break; }
                    yield return routine.Current;
                }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                if (!finished) onAborted();
            }
        }

#if UNITY_EDITOR
        private static readonly List<IEnumerator> EditorRoutines = new();
        private static void TickEditor()
        {
            for (int i = EditorRoutines.Count - 1; i >= 0; i--)
                if (!EditorRoutines[i].MoveNext()) EditorRoutines.RemoveAt(i);
            if (EditorRoutines.Count == 0) StopEditor();
        }

        private static void StopEditor()
        {
            UnityEditor.EditorApplication.update -= TickEditor;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopEditor;
            foreach (var routine in EditorRoutines) (routine as IDisposable)?.Dispose();
            EditorRoutines.Clear();
        }
#endif

        public static void Stop(Coroutine coroutine)
        {
            if (_instance != null && coroutine != null)
                _instance.StopCoroutine(coroutine);
        }
    }
}
