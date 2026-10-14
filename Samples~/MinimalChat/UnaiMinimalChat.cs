using System;
using System.Threading;
using UnAI.Config;
using UnAI.Core;
using UnityEngine;

namespace UnAI.Samples
{
    public class UnaiMinimalChat : MonoBehaviour
    {
        [SerializeField] private UnaiGlobalConfig _config;
        [SerializeField] private string _prompt = "Tell me a short joke.";
        private CancellationTokenSource _cts;

        private async void Start()
        {
            if (_config == null)
            {
                Debug.LogError("[UNAI Sample] Assign a UnaiGlobalConfig before starting.");
                return;
            }

            var manager = UnaiManager.Instance;
            if (manager == null)
            {
                manager = gameObject.AddComponent<UnaiManager>();
                manager.Initialize(_config);
            }
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                Debug.Log(await manager.QuickChatAsync(_prompt, ct: _cts.Token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogError($"[UNAI Sample] {ex.Message}"); }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OnDestroy() => _cts?.Cancel();
    }
}
