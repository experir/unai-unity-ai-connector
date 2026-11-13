using UnAI.Core;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.Agent.Http
{
    /// <summary>
    /// Scene component for <see cref="UnaiAgentHttpServer"/>: other apps on this computer can chat with
    /// a UnaiAgent running in the Editor or in a desktop build. Register the tools the agent may use in
    /// <see cref="Tools"/> (for example from another component's Awake). Needs a UnaiManager in the scene.
    /// </summary>
    [AddComponentMenu("UnAI/Agent HTTP API")]
    public class UnaiAgentHttpComponent : MonoBehaviour
    {
        [SerializeField] private int _port = 3390;
        [Tooltip("Leave empty to generate a new random token each time the server starts.")]
        [SerializeField] private string _accessToken;
        [Tooltip("Empty uses the UnaiManager's default provider.")]
        [SerializeField] private string _providerId;
        [TextArea(3, 8)]
        [SerializeField] private string _systemPrompt = "You are an assistant running inside a Unity application.";
        [SerializeField] private int _maxSteps = 6;
        [Tooltip("Let reasoning models think before answering. Slower; off suits interactive use.")]
        [SerializeField] private bool _allowReasoning;

        private UnaiAgentHttpServer _server;

        /// <summary>Tools available to every session's agent.</summary>
        public UnaiToolRegistry Tools { get; } = new();
        public UnaiAgentHttpServer Server => _server;
        /// <summary>The token clients must send; set once the server is running.</summary>
        public string AccessToken => _server?.AccessToken;

        private void OnEnable()
        {
            if (!UnaiAgentHttpServer.IsSupportedPlatform)
            {
                Debug.LogWarning("[UNAI Agent API] Not available on this platform (Editor and desktop builds only).");
                return;
            }
            _server ??= new UnaiAgentHttpServer(CreateAgent);
            _server.Start(_port, _accessToken);
        }

        private void OnDisable() => _server?.Stop();

        private UnaiAgent CreateAgent()
        {
            var config = new UnaiAgentConfig
            {
                SystemPrompt = _systemPrompt,
                MaxSteps = _maxSteps,
                ProviderId = string.IsNullOrEmpty(_providerId) ? null : _providerId,
                Options = new UnAI.Models.UnaiRequestOptions { Reasoning = _allowReasoning ? null : false }
            };
            var provider = string.IsNullOrEmpty(_providerId)
                ? UnaiManager.Instance?.ActiveProvider
                : UnaiProviderRegistry.Get(_providerId);
            return provider != null ? new UnaiAgent(provider, config, Tools) : new UnaiAgent(config, Tools);
        }
    }
}
