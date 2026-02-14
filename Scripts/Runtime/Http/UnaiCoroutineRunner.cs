using System.Collections;
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
                    Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<Runner>();
                }
                return _instance;
            }
        }

        public static Coroutine Run(IEnumerator coroutine) => Instance.StartCoroutine(coroutine);

        public static void Stop(Coroutine coroutine)
        {
            if (_instance != null && coroutine != null)
                _instance.StopCoroutine(coroutine);
        }
    }
}
