using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnAI.Samples.NpcDialogue
{
    /// <summary>
    /// Dialogue box for <see cref="UnaiNpc"/>: shows the NPC's line as it streams in,
    /// takes the player's reply, and shows the gold/inventory HUD.
    /// </summary>
    public class UnaiDialogueUI : MonoBehaviour
    {
        [Header("Dialogue panel")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private Image _portrait;
        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_Text _lineLabel;
        [SerializeField] private TMP_Text _actionLabel;
        [SerializeField] private TMP_InputField _input;
        [SerializeField] private Button _sendButton;
        [SerializeField] private Button _leaveButton;

        [Header("HUD")]
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private TMP_Text _goldLabel;
        [SerializeField] private TMP_Text _inventoryLabel;

        public static UnaiDialogueUI Instance { get; private set; }

        private UnaiNpc _npc;
        private CancellationTokenSource _cts;
        private bool _ready;

        private void Awake() => Instance = this;

        private void Start()
        {
            UnaiSampleSetup.EnsureInputModule();
            UnaiSampleSetup.EnableEmoji();
            _panel.SetActive(false);
            _sendButton.onClick.AddListener(Send);
            _input.onSubmit.AddListener(_ => Send());
            _leaveButton.onClick.AddListener(Close);

            if (UnaiPlayerState.Instance != null) UnaiPlayerState.Instance.Changed += RefreshHud;
            RefreshHud();

            _ready = UnaiSampleSetup.TryEnsureManager(gameObject, out string error);
            _hintLabel.text = _ready
                ? $"Click a villager to talk  ·  {UnaiSampleSetup.DescribeActiveProvider()}"
                : error;
        }

        public void Open(UnaiNpc npc)
        {
            if (!_ready || npc == _npc) return;
            Close();
            _npc = npc;
            _npc.ActionTaken += OnNpcAction;
            _panel.SetActive(true);
            _portrait.color = npc.Color;
            _nameLabel.text = npc.DisplayName;
            _lineLabel.text = UnaiSampleSetup.PrepareText(npc.LastLine);
            _actionLabel.text = "";
            SetWaiting(false);
        }

        public void Close()
        {
            _cts?.Cancel();
            if (_npc != null) _npc.ActionTaken -= OnNpcAction;
            _npc = null;
            _panel.SetActive(false);
        }

        private async void Send()
        {
            string text = _input.text.Trim();
            if (_npc == null || _cts != null || text.Length == 0) return;
            _input.text = "";

            var npc = _npc;
            _cts = new CancellationTokenSource();
            _actionLabel.text = "";
            _lineLabel.text = "…";
            SetWaiting(true);
            try
            {
                string line = await npc.SayAsync(text, partial =>
                {
                    if (_npc == npc && !string.IsNullOrWhiteSpace(partial)) _lineLabel.text = UnaiSampleSetup.PrepareText(partial);
                }, _cts.Token);
                if (_npc == npc) _lineLabel.text = UnaiSampleSetup.PrepareText(line);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (_npc == npc) _lineLabel.text = $"<color=#ff8080>{ex.Message}</color>";
                Debug.LogException(ex);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                if (_npc == npc) SetWaiting(false);
            }
        }

        private void OnNpcAction(string toolName)
        {
            _actionLabel.text = toolName switch
            {
                "list_wares" => "checks the shelves…",
                "sell_item" => "wraps up your purchase…",
                _ => $"uses {toolName}…"
            };
        }

        private void SetWaiting(bool waiting)
        {
            _input.interactable = !waiting;
            _sendButton.interactable = !waiting;
            if (!waiting) _input.ActivateInputField();
        }

        private void RefreshHud()
        {
            var player = UnaiPlayerState.Instance;
            if (player == null) return;
            _goldLabel.text = $"{player.Gold} gold";
            _inventoryLabel.text = player.Inventory.Count == 0
                ? "Inventory: empty"
                : "Inventory: " + string.Join(", ", player.Inventory);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            if (UnaiPlayerState.Instance != null) UnaiPlayerState.Instance.Changed -= RefreshHud;
        }
    }
}
