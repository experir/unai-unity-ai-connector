using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnAI.Agent;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnAI.Tools;
using UnityEditor;
using UnityEngine;

namespace UnAI.Editor.Assistant
{
    public class UnaiAssistantWindow : EditorWindow
    {
        // State
        private UnaiGlobalConfig _config;
        private UnaiAgent _agent;
        private CancellationTokenSource _cts;
        private bool _isProcessing;
        private string _streamingContent = "";

        // UI state
        private string _inputText = "";
        private Vector2 _scrollPos;
        private bool _scrollToBottom;
        private int _selectedProviderIndex;
        private string _modelOverride = "";
        private string[] _providerNames = Array.Empty<string>();
        private string[] _providerIds = Array.Empty<string>();

        // Chat history for display
        private readonly List<ChatEntry> _chatEntries = new();

        private GUIStyle _userStyle;
        private GUIStyle _assistantStyle;
        private GUIStyle _toolStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInitialized;

        [MenuItem("Window/UnAI/AI Assistant")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnaiAssistantWindow>("UNAI Assistant");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            LoadConfig();
            RefreshProviderList();
        }

        private void OnDisable()
        {
            CancelRequest();
        }

        private void LoadConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:UnaiGlobalConfig");
            if (guids.Length > 0)
                _config = AssetDatabase.LoadAssetAtPath<UnaiGlobalConfig>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void RefreshProviderList()
        {
            if (_config == null) return;

            var providers = _config.AllProviders()
                .Where(p => p.Enabled)
                .ToList();

            _providerIds = providers.Select(p => p.ProviderId).ToArray();
            _providerNames = providers.Select(p =>
            {
                string name = p.ProviderId;
                // Capitalize first letter
                if (name.Length > 0)
                    name = char.ToUpper(name[0]) + name.Substring(1);
                return name;
            }).ToArray();

            // Select the default provider
            if (_config.DefaultProviderId != null)
            {
                int idx = Array.IndexOf(_providerIds, _config.DefaultProviderId);
                if (idx >= 0) _selectedProviderIndex = idx;
            }
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _userStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                padding = new RectOffset(10, 10, 6, 6),
                richText = true,
                normal = { textColor = new Color(0.85f, 0.85f, 1f) }
            };

            _assistantStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                padding = new RectOffset(10, 10, 6, 6),
                richText = true,
                normal = { textColor = new Color(0.9f, 0.95f, 0.9f) }
            };

            _toolStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                padding = new RectOffset(10, 10, 4, 4),
                richText = true,
                fontSize = 10,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _inputStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                padding = new RectOffset(10, 10, 2, 2)
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitializeStyles();

            if (_config == null)
            {
                DrawNoConfig();
                return;
            }

            DrawToolbar();
            DrawChatArea();
            DrawInputArea();

            if (_scrollToBottom)
            {
                _scrollToBottom = false;
                Repaint();
            }
        }

        private void DrawNoConfig()
        {
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "No UnaiGlobalConfig found.\n\n" +
                "Create one via Window > UnAI > Create Global Config,\n" +
                "or assign a config to the UnaiManager in your scene.",
                MessageType.Warning);

            if (GUILayout.Button("Refresh"))
            {
                LoadConfig();
                RefreshProviderList();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Provider selector
            EditorGUI.BeginChangeCheck();
            _selectedProviderIndex = EditorGUILayout.Popup(
                _selectedProviderIndex, _providerNames,
                EditorStyles.toolbarPopup, GUILayout.Width(120));
            if (EditorGUI.EndChangeCheck())
            {
                _modelOverride = "";
                ResetAgent();
            }

            // Model field
            EditorGUILayout.LabelField("Model:", GUILayout.Width(42));
            string defaultModel = GetSelectedProviderConfig()?.DefaultModel ?? "";
            string placeholder = string.IsNullOrEmpty(_modelOverride) ? defaultModel : _modelOverride;
            _modelOverride = EditorGUILayout.TextField(_modelOverride,
                EditorStyles.toolbarTextField, GUILayout.MinWidth(120));

            GUILayout.FlexibleSpace();

            // Clear button
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                CancelRequest();
                _chatEntries.Clear();
                _agent = null;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawChatArea()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            if (_chatEntries.Count == 0 && !_isProcessing)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField("UNAI AI Assistant", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "Ask me to inspect your scene, find objects, create GameObjects, read scripts, and more.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
                EditorGUILayout.Space(8);

                string tools = "inspect_scene, find_gameobject, create_gameobject,\n" +
                               "inspect_gameobject, read_script, list_assets,\n" +
                               "get_selection, log_message";
                EditorGUILayout.LabelField("Available tools:", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField(tools,
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true, fontSize = 10 });
            }

            foreach (var entry in _chatEntries)
            {
                DrawChatEntry(entry);
            }

            // Show streaming content
            if (_isProcessing && !string.IsNullOrEmpty(_streamingContent))
            {
                DrawMessageBubble("Assistant", _streamingContent + " ...", _assistantStyle, _headerStyle);
            }

            if (_scrollToBottom)
            {
                _scrollPos.y = float.MaxValue;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawChatEntry(ChatEntry entry)
        {
            switch (entry.Type)
            {
                case ChatEntryType.User:
                    DrawMessageBubble("You", entry.Content, _userStyle, _headerStyle);
                    break;
                case ChatEntryType.Assistant:
                    DrawMessageBubble("Assistant", entry.Content, _assistantStyle, _headerStyle);
                    break;
                case ChatEntryType.ToolCall:
                    EditorGUILayout.LabelField($"  > Tool: {entry.ToolName}({entry.ToolArgs})", _toolStyle);
                    break;
                case ChatEntryType.ToolResult:
                    string preview = entry.Content;
                    if (preview.Length > 200) preview = preview.Substring(0, 200) + "...";
                    EditorGUILayout.LabelField($"  < {entry.ToolName}: {preview}", _toolStyle);
                    break;
            }
        }

        private void DrawMessageBubble(string header, string content, GUIStyle style, GUIStyle headerSt)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(header, headerSt);
            EditorGUILayout.LabelField(content, style);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginHorizontal();

            // Multi-line input
            _inputText = EditorGUILayout.TextArea(_inputText, _inputStyle,
                GUILayout.MinHeight(40), GUILayout.MaxHeight(80));

            EditorGUILayout.BeginVertical(GUILayout.Width(60));

            GUI.enabled = !_isProcessing && !string.IsNullOrWhiteSpace(_inputText);
            if (GUILayout.Button("Send", GUILayout.Height(38)))
            {
                SendMessage();
            }
            GUI.enabled = true;

            if (_isProcessing)
            {
                if (GUILayout.Button("Cancel"))
                {
                    CancelRequest();
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // Handle Enter key (Shift+Enter for newline)
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && !e.shift
                && GUI.GetNameOfFocusedControl() != "" && !_isProcessing
                && !string.IsNullOrWhiteSpace(_inputText))
            {
                SendMessage();
                e.Use();
            }
        }

        private async void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(_inputText) || _isProcessing) return;

            string message = _inputText.Trim();
            _inputText = "";
            _isProcessing = true;
            _streamingContent = "";
            _scrollToBottom = true;

            _chatEntries.Add(new ChatEntry
            {
                Type = ChatEntryType.User,
                Content = message
            });

            try
            {
                EnsureAgent();
                _cts = new CancellationTokenSource();

                var step = await _agent.RunAsync(message, _cts.Token);

                // The events already added tool call/result entries during execution.
                // Add the final assistant response.
                if (step?.Response != null && !string.IsNullOrEmpty(step.Response.Content))
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = step.Response.Content
                    });
                }

                if (step?.StopReason == "max_steps")
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = "(Reached maximum steps limit)"
                    });
                }
            }
            catch (OperationCanceledException)
            {
                _chatEntries.Add(new ChatEntry
                {
                    Type = ChatEntryType.Assistant,
                    Content = "(Cancelled)"
                });
            }
            catch (Exception ex)
            {
                _chatEntries.Add(new ChatEntry
                {
                    Type = ChatEntryType.Assistant,
                    Content = $"Error: {ex.Message}"
                });
                Debug.LogException(ex);
            }
            finally
            {
                _isProcessing = false;
                _streamingContent = "";
                _scrollToBottom = true;
                Repaint();
            }
        }

        private void EnsureAgent()
        {
            if (_agent != null) return;

            EnsureProvidersInitialized();

            string providerId = GetSelectedProviderId();
            string model = !string.IsNullOrEmpty(_modelOverride)
                ? _modelOverride
                : GetSelectedProviderConfig()?.DefaultModel;

            var tools = UnaiAssistantToolsFactory.CreateEditorToolRegistry();

            var config = new UnaiAgentConfig
            {
                ProviderId = providerId,
                Model = model,
                MaxSteps = 10,
                TimeoutSeconds = 120,
                UseStreaming = true,
                SystemPrompt = BuildSystemPrompt()
            };

            _agent = new UnaiAgent(config, tools);

            _agent.OnToolCall += args =>
            {
                _chatEntries.Add(new ChatEntry
                {
                    Type = ChatEntryType.ToolCall,
                    ToolName = args.ToolCall.ToolName,
                    ToolArgs = TruncateArgs(args.ToolCall.ArgumentsJson)
                });
                _scrollToBottom = true;
                Repaint();
            };

            _agent.OnToolResult += args =>
            {
                _chatEntries.Add(new ChatEntry
                {
                    Type = ChatEntryType.ToolResult,
                    ToolName = args.Result.ToolName,
                    Content = args.Result.Content
                });
                _scrollToBottom = true;
                Repaint();
            };

            _agent.OnStreamDelta += args =>
            {
                if (!string.IsNullOrEmpty(args.Delta.Content))
                {
                    _streamingContent = args.Delta.AccumulatedContent ?? _streamingContent + args.Delta.Content;
                    _scrollToBottom = true;
                    Repaint();
                }
            };
        }

        private void EnsureProvidersInitialized()
        {
            if (_config == null) return;

            string providerId = GetSelectedProviderId();
            if (UnaiProviderRegistry.Has(providerId)) return;

            // Initialize providers from config in editor context
            UnaiProviderRegistry.InitializeBuiltInProviders(_config);
        }

        private string GetSelectedProviderId()
        {
            if (_selectedProviderIndex >= 0 && _selectedProviderIndex < _providerIds.Length)
                return _providerIds[_selectedProviderIndex];
            return _config?.DefaultProviderId ?? "openai";
        }

        private UnaiProviderConfig GetSelectedProviderConfig()
        {
            string id = GetSelectedProviderId();
            return _config?.GetProviderConfig(id);
        }

        private void ResetAgent()
        {
            CancelRequest();
            _agent = null;
        }

        private void CancelRequest()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _isProcessing = false;
        }

        private string BuildSystemPrompt()
        {
            return
                "You are UNAI Assistant, an AI helper embedded in the Unity Editor. " +
                "You help developers work with their Unity projects by inspecting scenes, finding and creating GameObjects, " +
                "reading scripts, listing assets, and more.\n\n" +
                "Guidelines:\n" +
                "- Use the available tools to answer questions about the user's project.\n" +
                "- When creating or modifying objects, always confirm what you did.\n" +
                "- Keep responses concise and focused on the task.\n" +
                "- If you need more information, use inspection tools before answering.\n" +
                "- All object creation supports Undo (Ctrl+Z).";
        }

        private static string TruncateArgs(string json)
        {
            if (string.IsNullOrEmpty(json)) return "{}";
            return json.Length > 100 ? json.Substring(0, 100) + "..." : json;
        }

        private enum ChatEntryType { User, Assistant, ToolCall, ToolResult }

        private class ChatEntry
        {
            public ChatEntryType Type;
            public string Content;
            public string ToolName;
            public string ToolArgs;
        }
    }
}
