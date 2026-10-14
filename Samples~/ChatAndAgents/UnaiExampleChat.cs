using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TMPro;
using UnAI.Core;
using UnAI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace UnAI.Examples
{
    public class UnaiExampleChat : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Dropdown _providerDropdown;
        [SerializeField] private TMP_InputField _systemPromptInput;
        [SerializeField] private TMP_InputField _userMessageInput;
        [SerializeField] private TMP_Text _responseText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _logText;
        [SerializeField] private Button _sendButton;
        [SerializeField] private Button _streamButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _clearLogButton;
        [SerializeField] private ScrollRect _responseScroll;
        [SerializeField] private ScrollRect _logScroll;

        [Header("Debug Panel (Optional)")]
        [SerializeField] private GameObject _debugPanel;
        [SerializeField] private Button _debugToggleButton;
        [SerializeField] private TMP_Text _debugText;

        [Header("Response Format (Optional)")]
        [SerializeField] private TMP_Dropdown _responseFormatDropdown;

        private string[] _providerIds = Array.Empty<string>();
        private readonly List<string> _logEntries = new();
        private CancellationTokenSource _cts;
        private bool _busy;

        // Session debug counters
        private int _sessionRequestCount;
        private int _sessionTotalTokens;
        private int _sessionTotalPromptTokens;
        private int _sessionTotalCompletionTokens;
        private float _sessionTotalTimeMs;
        private bool _debugVisible;

        private void Start()
        {
            EnsureManager();

            if (UnaiManager.Instance == null || UnaiManager.Instance.Config == null)
            {
                ShowMissingConfigOverlay();
                return;
            }

            SetupProviderDropdown();
            SetupButtons();

            _systemPromptInput.text = "You are a helpful assistant.";
            _userMessageInput.text = "Hello! Tell me a short joke.";
            _statusText.text = "Ready. Select a provider and send a message.";
            _responseText.text = "";

            SetupDebugPanel();
            SetupResponseFormatDropdown();

            Log("UNAI Example Chat ready.");
            Log("Set environment variables (OPENAI_API_KEY, etc.) or configure keys in the UnaiGlobalConfig asset.");
        }

        private void EnsureManager()
        {
            if (UnaiManager.Instance != null) return;

            var manager = GetComponent<UnaiManager>();
            if (manager == null)
                manager = FindAnyObjectByType<UnaiManager>();
            if (manager == null)
                manager = gameObject.AddComponent<UnaiManager>();
        }

        private void SetupProviderDropdown()
        {
            var providers = UnaiProviderRegistry.GetAll().ToList();
            _providerIds = providers.Select(p => p.ProviderId).ToArray();

            _providerDropdown.ClearOptions();
            _providerDropdown.AddOptions(providers.Select(p =>
            {
                string status = p.IsConfigured ? "OK" : "No Key";
                return $"{p.DisplayName} [{status}]";
            }).ToList());

            if (UnaiManager.Instance != null)
            {
                string active = UnaiManager.Instance.ActiveProvider?.ProviderId ?? "";
                int idx = Array.IndexOf(_providerIds, active);
                if (idx >= 0) _providerDropdown.value = idx;
            }

            _providerDropdown.onValueChanged.AddListener(index =>
            {
                if (index < _providerIds.Length)
                {
                    UnaiManager.Instance?.SetActiveProvider(_providerIds[index]);
                    Log($"Switched to: {_providerIds[index]}");
                }
            });
        }

        private void SetupButtons()
        {
            _sendButton.onClick.AddListener(() => SendChat(false));
            _streamButton.onClick.AddListener(() => SendChat(true));
            _cancelButton.onClick.AddListener(CancelRequest);
            _clearLogButton.onClick.AddListener(() =>
            {
                _logEntries.Clear();
                _logText.text = "";
            });

            UpdateButtonStates();
        }

        private void SetupResponseFormatDropdown()
        {
            if (_responseFormatDropdown == null) return;
            _responseFormatDropdown.ClearOptions();
            _responseFormatDropdown.AddOptions(new List<string> { "Text", "JSON Object", "JSON Schema" });
            _responseFormatDropdown.value = 0;
        }

        private void SetupDebugPanel()
        {
            if (_debugPanel != null)
                _debugPanel.SetActive(false);

            if (_debugToggleButton != null)
            {
                _debugToggleButton.onClick.AddListener(() =>
                {
                    _debugVisible = !_debugVisible;
                    if (_debugPanel != null)
                        _debugPanel.SetActive(_debugVisible);
                });
            }

            UpdateDebugText();
        }

        private void UpdateDebugText()
        {
            if (_debugText == null) return;

            string avgTime = _sessionRequestCount > 0
                ? FormatDuration(_sessionTotalTimeMs / _sessionRequestCount)
                : "-";

            _debugText.text =
                $"<b>Session Stats</b>\n" +
                $"Requests: {_sessionRequestCount}\n" +
                $"Tokens: {_sessionTotalTokens} (prompt: {_sessionTotalPromptTokens}, completion: {_sessionTotalCompletionTokens})\n" +
                $"Total time: {FormatDuration(_sessionTotalTimeMs)}  |  Avg: {avgTime}/req";
        }

        private static string FormatDuration(float ms)
        {
            if (ms < 1000f) return $"{ms:F0}ms";
            return $"{ms / 1000f:F2}s";
        }

        private void UpdateButtonStates()
        {
            _sendButton.interactable = !_busy;
            _streamButton.interactable = !_busy;
            _cancelButton.interactable = _busy;
        }

        private async void SendChat(bool stream)
        {
            if (UnaiManager.Instance == null) { Log("ERROR: No UnaiManager."); return; }
            var provider = UnaiManager.Instance.ActiveProvider;
            if (provider == null) { Log("ERROR: No active provider."); return; }
            if (!provider.IsConfigured)
                Log($"WARNING: '{provider.ProviderId}' may not be configured (missing key/URL).");

            _responseText.text = "";
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _busy = true;
            UpdateButtonStates();

            float startTime = Time.realtimeSinceStartup;

            var request = new UnaiChatRequest();

            int formatIdx = _responseFormatDropdown != null ? _responseFormatDropdown.value : 0;
            if (formatIdx > 0)
                request.Options = new UnaiRequestOptions { ResponseFormat = (UnaiResponseFormat)formatIdx };

            string sys = _systemPromptInput.text.Trim();
            if (!string.IsNullOrEmpty(sys))
                request.Messages.Add(UnaiChatMessage.System(sys));
            request.Messages.Add(UnaiChatMessage.User(_userMessageInput.text));

            string msgPreview = _userMessageInput.text.Length > 60
                ? _userMessageInput.text[..60] + "..." : _userMessageInput.text;
            Log($">> {provider.DisplayName} | stream={stream} | \"{msgPreview}\"");

            try
            {
                if (stream)
                {
                    _statusText.text = "Streaming...";

                    await UnaiManager.Instance.ChatStreamAsync(
                        request,
                        onDelta: delta =>
                        {
                            _responseText.text = delta.AccumulatedContent;
                            _statusText.text = $"Streaming... {delta.AccumulatedContent.Length} chars";
                            ScrollToBottom(_responseScroll);
                        },
                        onComplete: response =>
                        {
                            _statusText.text = FormatResult(Time.realtimeSinceStartup - startTime, response);
                            Log(_statusText.text);
                        },
                        onError: error =>
                        {
                            _statusText.text = $"ERROR [{error.ErrorType}]: {error.Message}";
                            if (!string.IsNullOrEmpty(error.RawResponse))
                                _responseText.text = error.RawResponse;
                            Log(_statusText.text);
                        },
                        ct: _cts.Token);
                }
                else
                {
                    _statusText.text = "Waiting for response...";

                    var response = await UnaiManager.Instance.ChatAsync(request, _cts.Token);
                    _responseText.text = response.Content;
                    _statusText.text = FormatResult(Time.realtimeSinceStartup - startTime, response);
                    Log(_statusText.text);
                    ScrollToBottom(_responseScroll);
                }
            }
            catch (UnaiRequestException ex)
            {
                _statusText.text = $"ERROR [{ex.ErrorInfo.ErrorType}]: {ex.Message}";
                _responseText.text = ex.ErrorInfo.RawResponse ?? ex.Message;
                Log(_statusText.text);
            }
            catch (OperationCanceledException)
            {
                _statusText.text = "Cancelled.";
                Log("Cancelled.");
            }
            catch (Exception ex)
            {
                _statusText.text = $"EXCEPTION: {ex.GetType().Name} - {ex.Message}";
                Log(_statusText.text);
                Debug.LogException(ex);
            }
            finally
            {
                _busy = false;
                UpdateButtonStates();
            }
        }

        private void CancelRequest()
        {
            _cts?.Cancel();
            Log("Cancellation requested.");
        }

        private string FormatResult(float elapsed, UnaiChatResponse response)
        {
            float elapsedMs = elapsed * 1000f;
            string result = $"Done in {FormatDuration(elapsedMs)}";
            if (!string.IsNullOrEmpty(response.Model)) result += $" | Model: {response.Model}";
            if (response.Usage != null)
                result += $" | Tokens: {response.Usage.PromptTokens}+{response.Usage.CompletionTokens}={response.Usage.TotalTokens}";
            result += $" | {_responseText.text.Length} chars";

            // Accumulate session stats
            _sessionRequestCount++;
            _sessionTotalTimeMs += elapsedMs;
            if (response.Usage != null)
            {
                _sessionTotalPromptTokens += response.Usage.PromptTokens;
                _sessionTotalCompletionTokens += response.Usage.CompletionTokens;
                _sessionTotalTokens += response.Usage.TotalTokens;
            }
            UpdateDebugText();

            return result;
        }

        private void Log(string message)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logEntries.Add(entry);
            _logText.text = string.Join("\n", _logEntries);
            Debug.Log($"[UNAI Example] {message}");
            ScrollToBottom(_logScroll);
        }

        private static void ScrollToBottom(ScrollRect scroll)
        {
            if (scroll != null)
                Canvas.ForceUpdateCanvases();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 0f;
        }

        private void ShowMissingConfigOverlay()
        {
            Debug.LogWarning("[UNAI] No UnaiGlobalConfig assigned. Assign one in the Inspector on the UnaiManager component.");

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            // Create overlay
            var overlay = new GameObject("MissingConfigOverlay");
            overlay.transform.SetParent(canvas.transform, false);

            var overlayRect = overlay.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;

            var overlayImage = overlay.AddComponent<Image>();
            overlayImage.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

            // Create message text
            var textObj = new GameObject("Message");
            textObj.transform.SetParent(overlay.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.1f, 0.3f);
            textRect.anchorMax = new Vector2(0.9f, 0.7f);
            textRect.sizeDelta = Vector2.zero;

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text =
                "<size=24><b>UnaiGlobalConfig not assigned</b></size>\n\n" +
                "<size=16>" +
                "The <b>UnaiManager</b> needs a <b>UnaiGlobalConfig</b> asset to work.\n\n" +
                "<b>How to fix:</b>\n" +
                "  1. Create a config: <i>Assets > Create > UnAI > Global Configuration</i>\n" +
                "  2. Configure your API keys in the new asset\n" +
                "  3. Select the <b>UnaiManager</b> GameObject and drag the config into the\n" +
                "     <b>Configuration > Config</b> field in the Inspector\n\n" +
                "Or use <b>Window > UnAI > Hub > Core  -  Setup Wizard</b> for guided setup." +
                "</size>";
            text.color = new Color(0.9f, 0.9f, 0.9f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
