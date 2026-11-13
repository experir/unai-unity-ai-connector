using System;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnAI.Core;
using UnAI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace UnAI.Samples.InGameChat
{
    /// <summary>
    /// A ready-to-use in-game chat window: message bubbles, streaming replies, Stop and New chat.
    /// Uses only the Core module. History is kept in a plain list and sent with every request.
    /// </summary>
    public class UnaiChatWindow : MonoBehaviour
    {
        [Header("Assistant")]
        [TextArea(3, 8)]
        [SerializeField] private string _systemPrompt =
            "You are Nova, a friendly companion inside a video game. " +
            "Answer in a warm, concise way (1-3 short paragraphs). Use plain text, no markdown.";
        [TextArea(2, 4)]
        [SerializeField] private string _greeting =
            "Hi, I'm Nova! Ask me anything: game tips, lore ideas, or just chat.";
        [Tooltip("How many recent messages are sent as context with each request.")]
        [SerializeField] private int _maxHistoryMessages = 20;
        [Tooltip("Let reasoning models (e.g. qwen3 on Ollama) think before answering. Much slower; off suits chat.")]
        [SerializeField] private bool _allowReasoning;

        [Header("UI")]
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private RectTransform _messageList;
        [SerializeField] private GameObject _playerBubbleTemplate;
        [SerializeField] private GameObject _assistantBubbleTemplate;
        [SerializeField] private GameObject _systemBubbleTemplate;
        [SerializeField] private TMP_InputField _input;
        [SerializeField] private Button _sendButton;
        [SerializeField] private TMP_Text _sendButtonLabel;
        [SerializeField] private Button _newChatButton;
        [SerializeField] private TMP_Text _providerLabel;
        [SerializeField] private float _maxBubbleWidth = 460f;

        private readonly List<UnaiChatMessage> _history = new();
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _ready;

        private void Start()
        {
            UnaiSampleSetup.EnsureInputModule();
            UnaiSampleSetup.EnableEmoji();
            _playerBubbleTemplate.SetActive(false);
            _assistantBubbleTemplate.SetActive(false);
            _systemBubbleTemplate.SetActive(false);

            _sendButton.onClick.AddListener(OnSendClicked);
            _input.onSubmit.AddListener(_ => { if (!_busy) OnSendClicked(); });
            _newChatButton.onClick.AddListener(NewChat);

            _ready = UnaiSampleSetup.TryEnsureManager(gameObject, out string error);
            _providerLabel.text = _ready ? UnaiSampleSetup.DescribeActiveProvider() : "Not configured";
            if (!_ready)
            {
                AddBubble(_systemBubbleTemplate, error);
                _input.interactable = _sendButton.interactable = false;
                return;
            }
            NewChat();
        }

        public void NewChat()
        {
            _cts?.Cancel();
            _history.Clear();
            for (int i = _messageList.childCount - 1; i >= 0; i--)
            {
                var child = _messageList.GetChild(i).gameObject;
                if (child != _playerBubbleTemplate && child != _assistantBubbleTemplate && child != _systemBubbleTemplate)
                    Destroy(child);
            }
            AddBubble(_assistantBubbleTemplate, _greeting);
            _history.Add(UnaiChatMessage.Assistant(_greeting));
            _input.ActivateInputField();
        }

        private async void OnSendClicked()
        {
            if (!_ready) return;
            if (_busy)
            {
                _cts?.Cancel();
                return;
            }

            string text = _input.text.Trim();
            if (text.Length == 0) return;
            _input.text = "";

            AddBubble(_playerBubbleTemplate, text);
            _history.Add(UnaiChatMessage.User(text));
            var reply = AddBubble(_assistantBubbleTemplate, "…");

            var request = new UnaiChatRequest
            {
                Options = new UnaiRequestOptions { Reasoning = _allowReasoning ? null : false }
            };
            request.Messages.Add(UnaiChatMessage.System(_systemPrompt));
            for (int i = Mathf.Max(0, _history.Count - _maxHistoryMessages); i < _history.Count; i++)
                request.Messages.Add(_history[i]);

            SetBusy(true);
            _cts = new CancellationTokenSource();
            string content = null;
            string errorMessage = null;
            try
            {
                await UnaiManager.Instance.ChatStreamAsync(
                    request,
                    onDelta: delta =>
                    {
                        content = delta.AccumulatedContent;
                        SetBubbleText(reply, content);
                    },
                    onComplete: response => content = response.Content ?? content,
                    onError: error => errorMessage = error.UserFriendlyMessage ?? error.Message,
                    ct: _cts.Token);
            }
            catch (OperationCanceledException)
            {
                content = string.IsNullOrEmpty(content) ? "(stopped)" : content + " (stopped)";
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetBusy(false);
            }

            if (reply == null) return; // window was reset while streaming
            if (errorMessage != null)
            {
                Destroy(reply);
                AddBubble(_systemBubbleTemplate, errorMessage);
                _history.RemoveAt(_history.Count - 1); // let the player retry the same message
                return;
            }
            SetBubbleText(reply, content);
            _history.Add(UnaiChatMessage.Assistant(content ?? ""));
            _input.ActivateInputField();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _sendButtonLabel.text = busy ? "Stop" : "Send";
            _newChatButton.interactable = !busy;
        }

        private GameObject AddBubble(GameObject template, string text)
        {
            var bubble = Instantiate(template, _messageList);
            bubble.SetActive(true);
            SetBubbleText(bubble, text);
            return bubble;
        }

        private void SetBubbleText(GameObject bubble, string text)
        {
            if (bubble == null) return;
            var label = bubble.GetComponentInChildren<TMP_Text>(true);
            text = UnaiSampleSetup.PrepareText(text);
            label.text = text;

            // Size the bubble to its text, up to the maximum width.
            var bubbleLayout = label.transform.parent.GetComponent<LayoutElement>();
            var padding = label.transform.parent.GetComponent<LayoutGroup>().padding;
            float maxTextWidth = _maxBubbleWidth - padding.horizontal;
            float textWidth = Mathf.Min(label.GetPreferredValues(text, maxTextWidth, 0f).x, maxTextWidth);
            bubbleLayout.preferredWidth = Mathf.Ceil(textWidth) + padding.horizontal;

            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        private void OnDestroy() => _cts?.Cancel();
    }
}
