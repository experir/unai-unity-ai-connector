using System;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Agent;
using UnAI.Core;
using UnAI.Models;
using UnAI.Tools;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnAI.Samples.NpcDialogue
{
    /// <summary>
    /// Implement on a component next to <see cref="UnaiNpc"/> to give that NPC tools
    /// (for example a shop). Tools let the NPC change the game, not just talk.
    /// </summary>
    public interface IUnaiNpcToolProvider
    {
        void RegisterTools(UnaiToolRegistry registry);
        string ToolInstructions { get; }
    }

    /// <summary>
    /// A talkable NPC: a persona, a greeting and its own conversation memory, powered by a UnaiAgent.
    /// Click the NPC (needs a Collider and a PhysicsRaycaster on the camera) to open the dialogue.
    /// </summary>
    public class UnaiNpc : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private string _displayName = "Villager";
        [SerializeField] private Color _color = new(0.9f, 0.6f, 0.2f);
        [TextArea(4, 12)]
        [SerializeField] private string _persona = "A friendly villager.";
        [TextArea(2, 4)]
        [SerializeField] private string _greeting = "Hello, traveller!";
        [SerializeField] private int _maxSteps = 4;
        [Tooltip("Let reasoning models (e.g. qwen3 on Ollama) think before answering. Much slower; off suits dialogue.")]
        [SerializeField] private bool _allowReasoning;

        private UnaiAgent _agent;
        private Vector3 _baseScale;

        public string DisplayName => _displayName;
        public Color Color => _color;
        public string LastLine { get; private set; }

        /// <summary>Raised when the NPC uses a tool, with a short description for the UI.</summary>
        public event Action<string> ActionTaken;

        private void Awake()
        {
            _baseScale = transform.localScale;
            LastLine = _greeting;
        }

        public async Task<string> SayAsync(string playerLine, Action<string> onPartialLine, CancellationToken ct)
        {
            var agent = GetOrCreateAgent();
            void OnDelta(UnaiAgentStreamDeltaArgs args)
            {
                // Providers without native tool calling write tool calls as JSON text; don't show those.
                string text = args.Delta.AccumulatedContent ?? "";
                string start = text.TrimStart();
                if (start.StartsWith("{") || start.StartsWith("```")) return;
                onPartialLine?.Invoke(text);
            }
            void OnTool(UnaiAgentToolCallArgs args) => ActionTaken?.Invoke(args.ToolCall.ToolName);

            agent.OnStreamDelta += OnDelta;
            agent.OnToolCall += OnTool;
            try
            {
                var step = await agent.RunAsync(playerLine, ct);
                LastLine = step.Response?.Content?.Trim();
                if (string.IsNullOrEmpty(LastLine)) LastLine = "…";
                return LastLine;
            }
            catch (OperationCanceledException)
            {
                // Keep the memory consistent: every player line gets an answer.
                agent.Conversation.AddAssistant("(The conversation was interrupted.)");
                throw;
            }
            finally
            {
                agent.OnStreamDelta -= OnDelta;
                agent.OnToolCall -= OnTool;
            }
        }

        private UnaiAgent GetOrCreateAgent()
        {
            if (_agent != null) return _agent;

            var tools = new UnaiToolRegistry();
            string toolInstructions = "";
            foreach (var provider in GetComponents<IUnaiNpcToolProvider>())
            {
                provider.RegisterTools(tools);
                toolInstructions += "\n" + provider.ToolInstructions;
            }

            string systemPrompt =
                $"You are {_displayName}, a character in a small fantasy village in a video game.\n" +
                $"{_persona}\n\n" +
                "Rules: stay in character at all times. Reply with spoken dialogue only, 1-3 short " +
                "sentences, no narration, no stage directions, no markdown. Never say you are an AI." +
                toolInstructions;

            _agent = new UnaiAgent(UnaiManager.Instance.ActiveProvider, new UnaiAgentConfig
            {
                SystemPrompt = systemPrompt,
                MaxSteps = _maxSteps,
                TimeoutSeconds = 60,
                UseStreaming = true,
                Options = new UnaiRequestOptions { Reasoning = _allowReasoning ? null : false }
            }, tools);

            // The greeting is shown without calling the model; add it to memory so the NPC knows it said it.
            _agent.Conversation.AddAssistant(_greeting);
            return _agent;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (UnaiDialogueUI.Instance != null) UnaiDialogueUI.Instance.Open(this);
        }

        public void OnPointerEnter(PointerEventData eventData) => transform.localScale = _baseScale * 1.08f;
        public void OnPointerExit(PointerEventData eventData) => transform.localScale = _baseScale;
    }
}
