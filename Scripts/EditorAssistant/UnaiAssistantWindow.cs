using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnAI.Agent;
using UnAI.Config;
using UnAI.Core;
using UnAI.Memory;
using UnAI.Models;
using UnAI.Tools;
using System.Reflection;
using UnAI.Utilities;
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

        // UI state (serialized to survive domain reload)
        [SerializeField] private string _inputText = "";
        private Vector2 _scrollPos;
        private bool _scrollToBottom;
        [SerializeField] private int _selectedProviderIndex;
        [SerializeField] private string _modelOverride = "";
        [SerializeField] private int _maxSteps = 10;
        private string[] _providerNames = Array.Empty<string>();
        private string[] _providerIds = Array.Empty<string>();

        // Chat history for display — serialized to survive domain reload
        [SerializeField] private List<ChatEntry> _chatEntries = new();

        // Conversation messages — serialized so the agent can be restored after
        // Unity recompiles (domain reload). We store the raw JSON because Unity
        // cannot natively serialize the Newtonsoft-based models with generics.
        [SerializeField] private string _serializedConversation;

        // Flag: was a request in flight when domain reload happened?
        [SerializeField] private bool _wasProcessingBeforeReload;

        // Session-level debug counters (serialized to survive domain reload)
        [SerializeField] private int _sessionRequestCount;
        [SerializeField] private int _sessionTotalPromptTokens;
        [SerializeField] private int _sessionTotalCompletionTokens;
        [SerializeField] private int _sessionTotalTokens;
        [SerializeField] private float _sessionTotalTimeMs;

        // Debug panel state
        [SerializeField] private bool _debugFoldout;
        [SerializeField] private bool _debugShowPerMessage = true;
        [SerializeField] private bool _autoSaveEnabled;
        [SerializeField] private int _responseFormatIndex; // 0=Text, 1=JsonObject, 2=JsonSchema

        // MCP server state (accessed via reflection to avoid hard dependency on UnAI.MCP)
        [SerializeField] private bool _mcpFoldout;
        [SerializeField] private int _mcpPort = 3389;
        [SerializeField] private bool _mcpAutoStart;
        private object _mcpServer; // UnaiMcpServer instance (or null if MCP module not present)
        private bool _mcpAvailable = true; // false if MCP assembly not found

        // Per-request tracking (not serialized — transient during a single request)
        private float _requestStartTime;
        private int _requestStepCount;
        private int _requestPromptTokens;
        private int _requestCompletionTokens;

        private GUIStyle _userStyle;
        private GUIStyle _assistantStyle;
        private GUIStyle _toolStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _debugStyle;
        private GUIStyle _debugHeaderStyle;
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
            RestoreAfterDomainReload();

            if (_mcpAutoStart && _mcpAvailable)
                EnsureMcpStarted();
        }

        private void OnDisable()
        {
            // Save conversation state before potential domain reload
            SaveConversationState();
            CancelRequest();
        }

        private void OnDestroy()
        {
            McpInvoke("Stop");
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

            _debugStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                richText = true,
                wordWrap = true,
                padding = new RectOffset(14, 10, 1, 1),
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };

            _debugHeaderStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 10
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
            DrawDebugPanel();
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
                "Create one via Window > UnAI > Hub > Core — Setup Wizard,\n" +
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
            // Config row
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Config:", GUILayout.Width(45));
            EditorGUI.BeginChangeCheck();
            _config = (UnaiGlobalConfig)EditorGUILayout.ObjectField(
                _config, typeof(UnaiGlobalConfig), false, GUILayout.MinWidth(120));
            if (EditorGUI.EndChangeCheck())
            {
                RefreshProviderList();
                ResetAgent();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Provider / model row
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

            // Max steps
            EditorGUILayout.LabelField("Steps:", GUILayout.Width(38));
            EditorGUI.BeginChangeCheck();
            _maxSteps = EditorGUILayout.IntField(_maxSteps,
                EditorStyles.toolbarTextField, GUILayout.Width(30));
            if (EditorGUI.EndChangeCheck())
            {
                _maxSteps = Mathf.Clamp(_maxSteps, 1, 50);
                ResetAgent();
            }

            GUILayout.FlexibleSpace();

            // Clear button
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                CancelRequest();
                _chatEntries.Clear();
                _agent = null;
                _serializedConversation = null;
                _wasProcessingBeforeReload = false;
                _sessionRequestCount = 0;
                _sessionTotalPromptTokens = 0;
                _sessionTotalCompletionTokens = 0;
                _sessionTotalTokens = 0;
                _sessionTotalTimeMs = 0;
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

                string tools = "inspect_scene, create_gameobject, create_physics_setup,\n" +
                               "manage_assets, manage_packages, play_mode,\n" +
                               "component_properties, batch_execute, capture_screenshot,\n" +
                               "search_project, execute_csharp, and more (32 tools)";
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
                _requestStartTime = Time.realtimeSinceStartup;
                _requestStepCount = 0;
                _requestPromptTokens = 0;
                _requestCompletionTokens = 0;

                var step = await _agent.RunAsync(message, _cts.Token);

                float elapsedMs = (Time.realtimeSinceStartup - _requestStartTime) * 1000f;

                // The events already added tool call/result entries during execution.
                // Add the final assistant response.
                if (step?.Response != null && !string.IsNullOrEmpty(step.Response.Content))
                {
                    var entry = new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = step.Response.Content
                    };
                    PopulateDebugInfo(entry, step, elapsedMs);
                    _chatEntries.Add(entry);
                }

                if (step?.StopReason == "max_steps")
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = "(Reached maximum steps limit. You can increase Max Steps in the toolbar, or try rephrasing your request.)"
                    });
                }
                else if (step?.StopReason == "stuck_loop")
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = "(The AI got stuck repeating the same action. Try rephrasing your request or breaking it into smaller steps.)"
                    });
                }
                else if (step?.StopReason == "stuck_error")
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = "(The AI kept hitting the same error and couldn't recover. Try a different approach or simplify the request.)"
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
            catch (UnaiRequestException reqEx)
            {
                _chatEntries.Add(new ChatEntry
                {
                    Type = ChatEntryType.Assistant,
                    Content = $"⚠ {reqEx.ErrorInfo.UserFriendlyMessage}"
                });
                Debug.LogException(reqEx);
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
                _wasProcessingBeforeReload = false;
                _streamingContent = "";
                _scrollToBottom = true;
                SaveConversationState();
                AutoSaveIfEnabled();
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

            var options = new UnaiRequestOptions();
            if (_responseFormatIndex > 0)
                options.ResponseFormat = (UnaiResponseFormat)_responseFormatIndex;

            var config = new UnaiAgentConfig
            {
                ProviderId = providerId,
                Model = model,
                MaxSteps = _maxSteps,
                TimeoutSeconds = 120,
                UseStreaming = true,
                SystemPrompt = BuildSystemPrompt(),
                Options = options.ResponseFormat != UnaiResponseFormat.Text ? options : null
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

            _agent.OnStepComplete += args =>
            {
                _requestStepCount = args.Step.StepNumber;
                if (args.Step.Response?.Usage != null)
                {
                    _requestPromptTokens += args.Step.Response.Usage.PromptTokens;
                    _requestCompletionTokens += args.Step.Response.Usage.CompletionTokens;
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
            _serializedConversation = null;
        }

        private void CancelRequest()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _isProcessing = false;
        }

        /// <summary>
        /// Saves the current agent conversation to a serialized field so it
        /// survives Unity domain reloads (triggered by script compilation).
        /// </summary>
        private void SaveConversationState()
        {
            if (_isProcessing)
                _wasProcessingBeforeReload = true;

            if (_agent?.Conversation?.Messages != null && _agent.Conversation.Messages.Count > 0)
            {
                try
                {
                    _serializedConversation = Newtonsoft.Json.JsonConvert.SerializeObject(
                        _agent.Conversation.Messages,
                        Newtonsoft.Json.Formatting.None,
                        new Newtonsoft.Json.JsonSerializerSettings
                        {
                            TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None
                        });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UNAI] Failed to save conversation state: {ex.Message}");
                    _serializedConversation = null;
                }
            }
        }

        /// <summary>
        /// After a domain reload, restores the agent with its prior conversation
        /// so the user can continue where they left off.
        /// If the agent was mid-loop (e.g. after creating a script), it auto-resumes.
        /// </summary>
        private void RestoreAfterDomainReload()
        {
            // Nothing to restore
            if (_chatEntries == null || _chatEntries.Count == 0)
                return;

            bool wasProcessing = _wasProcessingBeforeReload;
            _wasProcessingBeforeReload = false;
            _isProcessing = false;
            _streamingContent = "";

            // Restore conversation into a fresh agent
            if (!string.IsNullOrEmpty(_serializedConversation))
            {
                try
                {
                    var messages = Newtonsoft.Json.JsonConvert.DeserializeObject<List<UnaiChatMessage>>(
                        _serializedConversation);

                    if (messages is { Count: > 0 })
                    {
                        EnsureAgent();
                        foreach (var msg in messages)
                        {
                            // Skip system messages — the agent already has its own
                            if (msg.Role != UnaiRole.System)
                                _agent.Conversation.Add(msg);
                        }
                        Debug.Log($"[UNAI] Restored {messages.Count} conversation messages after domain reload.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UNAI] Failed to restore conversation: {ex.Message}");
                    wasProcessing = false; // can't auto-resume without conversation
                }

                _serializedConversation = null;
            }
            else
            {
                wasProcessing = false; // nothing to resume from
            }

            // If the agent was mid-loop when recompilation happened, auto-resume
            if (wasProcessing && _agent != null)
            {
                _chatEntries.Add(new ChatEntry
                {
                    Type = ChatEntryType.Assistant,
                    Content = "(Script compiled successfully. Resuming...)"
                });
                _scrollToBottom = true;
                Repaint();
                AutoResume();
            }
        }

        /// <summary>
        /// Automatically continues the agent loop after a domain reload
        /// caused by script creation. Sends a context message so the model
        /// knows compilation succeeded and it should proceed.
        /// </summary>
        private async void AutoResume()
        {
            _isProcessing = true;
            _streamingContent = "";
            _scrollToBottom = true;

            try
            {
                EnsureAgent();
                _cts = new CancellationTokenSource();
                _requestStartTime = Time.realtimeSinceStartup;
                _requestStepCount = 0;
                _requestPromptTokens = 0;
                _requestCompletionTokens = 0;

                var step = await _agent.ContinueAsync(
                    "The script was created and compiled successfully. Unity has reloaded. " +
                    "Continue with the next steps of the original request.",
                    _cts.Token);

                float elapsedMs = (Time.realtimeSinceStartup - _requestStartTime) * 1000f;

                if (step?.Response != null && !string.IsNullOrEmpty(step.Response.Content))
                {
                    var entry = new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = step.Response.Content
                    };
                    PopulateDebugInfo(entry, step, elapsedMs);
                    _chatEntries.Add(entry);
                }

                if (step?.StopReason == "max_steps")
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = "(Reached maximum steps limit. You can increase Max Steps in the toolbar, or try rephrasing your request.)"
                    });
                }
                else if (step?.StopReason == "stuck_loop")
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = "(The AI got stuck repeating the same action. Try rephrasing your request or breaking it into smaller steps.)"
                    });
                }
                else if (step?.StopReason == "stuck_error")
                {
                    _chatEntries.Add(new ChatEntry
                    {
                        Type = ChatEntryType.Assistant,
                        Content = "(The AI kept hitting the same error and couldn't recover. Try a different approach or simplify the request.)"
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
            catch (UnaiRequestException reqEx)
            {
                _chatEntries.Add(new ChatEntry
                {
                    Type = ChatEntryType.Assistant,
                    Content = $"⚠ {reqEx.ErrorInfo.UserFriendlyMessage}"
                });
                Debug.LogException(reqEx);
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
                _wasProcessingBeforeReload = false;
                _streamingContent = "";
                _scrollToBottom = true;
                SaveConversationState();
                AutoSaveIfEnabled();
                Repaint();
            }
        }

        private string BuildSystemPrompt()
        {
            return
                "You are UNAI Assistant, an AI helper embedded in the Unity Editor. " +
                "You help developers work with their Unity projects by inspecting scenes, finding and creating GameObjects, " +
                "reading and creating scripts, listing assets, and more.\n\n" +
                "CRITICAL RULES:\n" +
                "1. You MUST call tools to perform ANY action. NEVER just describe or narrate an action.\n" +
                "2. If you want to create a script, you MUST call the 'create_script' tool. Do NOT just write code in your response.\n" +
                "3. If you want to create or modify a GameObject, you MUST call the appropriate tool.\n" +
                "4. NEVER say 'I created', 'I added', or 'Here is the script' without having actually called a tool first.\n" +
                "5. ONLY use the tools provided to you. NEVER invent or guess tool names.\n" +
                "6. Do NOT pass null values in tool arguments. Omit optional parameters instead of setting them to null.\n\n" +
                "AVAILABLE TOOLS:\n" +
                "- 'create_gameobject': Create GameObjects (supports 'components' list like ['Rigidbody', 'Camera', 'Light'])\n" +
                "- 'modify_gameobject': Transform, rename, add/remove components, delete existing GameObjects\n" +
                "- 'add_component_configured': Add a component and set its properties in one call (e.g. Rigidbody with mass=5, useGravity=false)\n" +
                "- 'create_prefab': Save a scene GameObject as a Prefab asset\n" +
                "- 'create_material': Create materials with color, shader, metallic, emission, and optionally apply to a GameObject\n" +
                "- 'create_light': Create lights (Directional, Point, Spot, Area) with color, intensity, range, shadows\n" +
                "- 'create_script': Create new C# scripts with 'path' and 'content'\n" +
                "- 'modify_script': Edit existing scripts (find/replace, insert at line, or overwrite)\n" +
                "- 'read_script': Read C# file contents\n" +
                "- 'inspect_scene': List all root GameObjects in the scene\n" +
                "- 'find_gameobject': Search for GameObjects by name, tag, or component\n" +
                "- 'inspect_gameobject': Get detailed info about a specific GameObject\n" +
                "- 'list_assets': List project assets by folder and type\n" +
                "- 'search_project': Full-text search across project scripts and text assets\n" +
                "- 'duplicate_gameobject': Clone a GameObject with all components and children, optionally rename and reparent\n" +
                "- 'set_layer_tag': Set layer and/or tag on a GameObject, optionally recursive to children\n" +
                "- 'get_project_settings': Read Unity project settings (physics, quality, time, player)\n" +
                "- 'focus_scene_view': Focus the Scene View camera on a specific GameObject\n" +
                "- 'create_physics_setup': Add Rigidbody + Collider + PhysicMaterial in one call\n" +
                "- 'play_mode': Control Play Mode — play, pause, stop, step one frame, or check status\n" +
                "- 'manage_assets': Asset database operations — create folders, move, copy, delete, rename, refresh, find by type\n" +
                "- 'manage_packages': Package Manager — list installed packages, add or remove packages\n" +
                "- 'run_tests': Open Unity Test Runner for EditMode or PlayMode tests\n" +
                "- 'capture_screenshot': Capture a screenshot of the Game View or Scene View as PNG\n" +
                "- 'component_properties': Read or write any serialized property on any component via reflection\n" +
                "- 'execute_csharp': Compile and run arbitrary C# code with full Unity API access. Use 'output' StringBuilder to return data\n" +
                "- 'batch_execute': Execute multiple tool calls in a single atomic batch (one Undo step)\n" +
                "- 'get_selection': Get currently selected objects\n" +
                "- 'execute_menu_item': Run any Unity menu command (e.g. 'GameObject/Light/Directional Light')\n" +
                "- 'undo': Undo previous action(s)\n" +
                "- 'get_console_logs': Read Unity Console messages (errors, warnings, logs)\n" +
                "- 'log_message': Write to Unity Console\n\n" +
                "WORKFLOW: Think about what tools you need, then call them one by one. " +
                "Use 'batch_execute' when you need to perform multiple quick operations in sequence. " +
                "After all tool calls are done, give a brief summary of what was accomplished.";
        }

        private static readonly string AutoSavePath =
            System.IO.Path.Combine(Application.dataPath, "..", "Library", "UnAI", "autosave.json");

        private void AutoSaveIfEnabled()
        {
            if (!_autoSaveEnabled) return;
            if (_agent?.Conversation == null || _agent.Conversation.MessageCount == 0) return;

            try
            {
                _agent.Conversation.SaveToFile(AutoSavePath);
                UnaiLogger.LogVerbose($"[UNAI] Auto-saved conversation to: {AutoSavePath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[UNAI] Auto-save failed: {ex.Message}");
            }
        }

        private void PopulateDebugInfo(ChatEntry entry, UnaiAgentStep step, float elapsedMs)
        {
            entry.HasDebugInfo = true;
            entry.ElapsedMs = elapsedMs;
            entry.StepCount = _requestStepCount;
            entry.StopReason = step?.StopReason;
            entry.Model = step?.Response?.Model;

            int promptTok = _requestPromptTokens;
            int completionTok = _requestCompletionTokens;

            // If step-level accumulation didn't capture tokens, try the final response
            if (promptTok == 0 && completionTok == 0 && step?.Response?.Usage != null)
            {
                promptTok = step.Response.Usage.PromptTokens;
                completionTok = step.Response.Usage.CompletionTokens;
            }

            // Fallback: estimate tokens from content when provider doesn't report usage
            if (promptTok == 0 && completionTok == 0)
            {
                if (_agent?.Conversation?.Messages != null)
                    promptTok = UnaiTokenEstimator.EstimateMessages(_agent.Conversation.Messages);
                if (!string.IsNullOrEmpty(step?.Response?.Content))
                    completionTok = UnaiTokenEstimator.EstimateTokens(step.Response.Content);
                entry.IsTokenEstimated = true;
            }

            entry.PromptTokens = promptTok;
            entry.CompletionTokens = completionTok;
            entry.TotalTokens = promptTok + completionTok;

            // Update session counters
            _sessionRequestCount++;
            _sessionTotalPromptTokens += promptTok;
            _sessionTotalCompletionTokens += completionTok;
            _sessionTotalTokens += promptTok + completionTok;
            _sessionTotalTimeMs += elapsedMs;
        }

        private void DrawDebugPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _debugFoldout = EditorGUILayout.Foldout(_debugFoldout, "Debug", true, _debugHeaderStyle);

            if (_debugFoldout)
            {
                EditorGUI.indentLevel++;

                // Session summary
                EditorGUILayout.LabelField("Session", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Requests: {_sessionRequestCount}   |   " +
                    $"Tokens: {_sessionTotalTokens} (prompt: {_sessionTotalPromptTokens}, completion: {_sessionTotalCompletionTokens})   |   " +
                    $"Time: {FormatDuration(_sessionTotalTimeMs)}",
                    _debugStyle);

                // Provider init stats
                EditorGUILayout.LabelField(
                    $"Providers: {UnaiProviderRegistry.InitializedCount} initialized   |   " +
                    $"{UnaiProviderRegistry.PendingCount} pending (lazy)",
                    _debugStyle);

                // Cache stats (only shown when manager is available)
                if (UnaiManager.Instance != null)
                {
                    var cache = UnaiManager.Instance.Cache;
                    EditorGUILayout.LabelField(
                        $"Cache: {cache.Count} entries   |   " +
                        $"Hits: {cache.Hits}   |   Misses: {cache.Misses}   |   " +
                        $"Hit rate: {cache.HitRate:P0}",
                        _debugStyle);
                }

                // Fallback chain info
                if (_config?.FallbackProviderIds is { Count: > 0 })
                {
                    string chain = string.Join(" > ", _config.FallbackProviderIds);
                    EditorGUILayout.LabelField($"Fallback chain: {chain}", _debugStyle);
                }

                EditorGUILayout.Space(4);

                // Per-message toggle
                _debugShowPerMessage = EditorGUILayout.Toggle("Show per-message stats", _debugShowPerMessage);

                if (_debugShowPerMessage)
                {
                    EditorGUILayout.Space(2);

                    // Show debug info for each assistant entry that has it (most recent first)
                    for (int i = _chatEntries.Count - 1; i >= 0; i--)
                    {
                        var entry = _chatEntries[i];
                        if (entry.Type != ChatEntryType.Assistant || !entry.HasDebugInfo) continue;

                        string contentPreview = entry.Content;
                        if (contentPreview.Length > 50)
                            contentPreview = contentPreview.Substring(0, 50) + "...";

                        string tokLabel = entry.IsTokenEstimated
                            ? $"~{entry.TotalTokens} tok (est.)"
                            : $"{entry.TotalTokens} tok";

                        string line = $"#{_chatEntries.Count - i}  " +
                            $"{FormatDuration(entry.ElapsedMs)}  |  " +
                            $"{tokLabel}  |  " +
                            $"{entry.StepCount} step(s)  |  " +
                            $"{entry.Model ?? "?"}  |  " +
                            $"{entry.StopReason ?? "?"}";

                        EditorGUILayout.LabelField(line, _debugStyle);
                    }

                    if (_chatEntries.All(e => e.Type != ChatEntryType.Assistant || !e.HasDebugInfo))
                    {
                        EditorGUILayout.LabelField("No request data yet.", _debugStyle);
                    }
                }

                EditorGUILayout.Space(4);

                // Raw JSON logging toggle
                EditorGUI.BeginChangeCheck();
                bool rawLog = EditorGUILayout.Toggle("Log raw JSON to file", UnaiLogger.RawJsonLoggingEnabled);
                if (EditorGUI.EndChangeCheck())
                    UnaiLogger.RawJsonLoggingEnabled = rawLog;

                if (UnaiLogger.RawJsonLoggingEnabled)
                {
                    EditorGUILayout.LabelField(UnaiLogger.LogFilePath, _debugStyle);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(14);
                    if (GUILayout.Button("Open Log File", EditorStyles.miniButton, GUILayout.Width(100)))
                    {
                        if (System.IO.File.Exists(UnaiLogger.LogFilePath))
                            EditorUtility.RevealInFinder(UnaiLogger.LogFilePath);
                        else
                            EditorUtility.DisplayDialog("UNAI", "No log file yet. Send a request first.", "OK");
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(4);

                // Response format
                EditorGUILayout.LabelField("Request Options", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _responseFormatIndex = EditorGUILayout.Popup("Response Format",
                    _responseFormatIndex, new[] { "Text", "JSON Object", "JSON Schema" });
                if (EditorGUI.EndChangeCheck())
                    ResetAgent();

                EditorGUILayout.Space(4);

                // Conversation persistence
                EditorGUILayout.LabelField("Conversation", EditorStyles.boldLabel);

                _autoSaveEnabled = EditorGUILayout.Toggle("Auto-save after each request", _autoSaveEnabled);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);

                GUI.enabled = _agent?.Conversation != null && _agent.Conversation.MessageCount > 0;

                if (GUILayout.Button("Save JSON", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    string path = EditorUtility.SaveFilePanel(
                        "Save Conversation", "", "conversation.json", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _agent.Conversation.SaveToFile(path);
                        Debug.Log($"[UNAI] Conversation saved to: {path}");
                    }
                }

                if (GUILayout.Button("Export MD", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    string path = EditorUtility.SaveFilePanel(
                        "Export Conversation as Markdown", "", "conversation.md", "md");
                    if (!string.IsNullOrEmpty(path))
                    {
                        System.IO.File.WriteAllText(path, _agent.Conversation.ExportMarkdown());
                        Debug.Log($"[UNAI] Conversation exported to: {path}");
                    }
                }

                GUI.enabled = true;

                if (GUILayout.Button("Load JSON", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    string path = EditorUtility.OpenFilePanel(
                        "Load Conversation", "", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        EnsureAgent();
                        _agent.Conversation.LoadFromFile(path);

                        // Rebuild chat entries from loaded messages
                        _chatEntries.Clear();
                        foreach (var msg in _agent.Conversation.Messages)
                        {
                            switch (msg.Role)
                            {
                                case UnaiRole.User:
                                    _chatEntries.Add(new ChatEntry
                                        { Type = ChatEntryType.User, Content = msg.Content });
                                    break;
                                case UnaiRole.Assistant:
                                    _chatEntries.Add(new ChatEntry
                                        { Type = ChatEntryType.Assistant, Content = msg.Content });
                                    break;
                                case UnaiRole.Tool:
                                    _chatEntries.Add(new ChatEntry
                                    {
                                        Type = ChatEntryType.ToolResult,
                                        ToolName = msg.ToolName,
                                        Content = msg.Content
                                    });
                                    break;
                            }
                        }

                        _scrollToBottom = true;
                        Debug.Log($"[UNAI] Conversation loaded from: {path} ({_agent.Conversation.MessageCount} messages)");
                        Repaint();
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);

                // MCP Server section
                _mcpFoldout = EditorGUILayout.Foldout(_mcpFoldout, "MCP Server", true);
                if (_mcpFoldout)
                {
                    DrawMcpSection();
                }

                EditorGUI.indentLevel--;

                // Reset session button
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset Session Stats", EditorStyles.miniButton, GUILayout.Width(130)))
                {
                    _sessionRequestCount = 0;
                    _sessionTotalPromptTokens = 0;
                    _sessionTotalCompletionTokens = 0;
                    _sessionTotalTokens = 0;
                    _sessionTotalTimeMs = 0;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMcpSection()
        {
            if (!_mcpAvailable)
            {
                EditorGUILayout.LabelField("MCP module not installed (Scripts/MCP/ folder missing).", _debugStyle);
                return;
            }

            bool running = McpGetBool("IsRunning");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(14);

            // Status indicator + start/stop
            string statusLabel = running ? "Running" : "Stopped";
            Color statusColor = running ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            var prevColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            EditorGUILayout.LabelField(statusLabel, EditorStyles.boldLabel, GUILayout.Width(55));
            GUI.contentColor = prevColor;

            if (running)
            {
                if (GUILayout.Button("Stop", EditorStyles.miniButton, GUILayout.Width(40)))
                    McpInvoke("Stop");

                int clients = McpGetInt("ConnectedClients");
                EditorGUILayout.LabelField($"Clients: {clients}", _debugStyle, GUILayout.Width(70));
            }
            else
            {
                _mcpPort = EditorGUILayout.IntField(_mcpPort, GUILayout.Width(50));
                _mcpPort = Mathf.Clamp(_mcpPort, 1024, 65535);

                if (GUILayout.Button("Start", EditorStyles.miniButton, GUILayout.Width(40)))
                    EnsureMcpStarted();
            }

            EditorGUILayout.EndHorizontal();

            _mcpAutoStart = EditorGUILayout.Toggle("Auto-start on load", _mcpAutoStart);

            if (running)
            {
                string url = McpGetString("Url");

                // URL copy
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);
                EditorGUILayout.TextField(url, EditorStyles.miniTextField);
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    GUIUtility.systemCopyBuffer = url;
                    Debug.Log($"[UNAI MCP] URL copied: {url}");
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(
                    $"Claude Desktop config: {{ \"mcpServers\": {{ \"unity\": {{ \"url\": \"{url}\" }} }} }}",
                    _debugStyle);
            }
        }

        // ── MCP reflection helpers (no hard dependency on UnAI.MCP assembly) ──

        private static Type _mcpServerType;

        private void EnsureMcpStarted()
        {
            try
            {
                if (_mcpServer == null)
                {
                    _mcpServerType ??= FindMcpType("UnAI.MCP.UnaiMcpServer");
                    if (_mcpServerType == null)
                    {
                        _mcpAvailable = false;
                        return;
                    }
                    _mcpServer = Activator.CreateInstance(_mcpServerType);
                }

                var tools = UnaiAssistantToolsFactory.CreateEditorToolRegistry();
                var startMethod = _mcpServerType.GetMethod("Start");
                startMethod?.Invoke(_mcpServer, new object[] { _mcpPort, tools });
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UNAI] MCP server start failed: {ex.Message}");
                _mcpAvailable = false;
            }
        }

        private void McpInvoke(string methodName)
        {
            if (_mcpServer == null) return;
            try
            {
                _mcpServerType?.GetMethod(methodName)?.Invoke(_mcpServer, null);
            }
            catch { }
        }

        private bool McpGetBool(string propName)
        {
            if (_mcpServer == null) return false;
            try { return (bool)(_mcpServerType?.GetProperty(propName)?.GetValue(_mcpServer) ?? false); }
            catch { return false; }
        }

        private int McpGetInt(string propName)
        {
            if (_mcpServer == null) return 0;
            try { return (int)(_mcpServerType?.GetProperty(propName)?.GetValue(_mcpServer) ?? 0); }
            catch { return 0; }
        }

        private string McpGetString(string propName)
        {
            if (_mcpServer == null) return "";
            try { return _mcpServerType?.GetProperty(propName)?.GetValue(_mcpServer)?.ToString() ?? ""; }
            catch { return ""; }
        }

        private static Type FindMcpType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        private static string FormatDuration(float ms)
        {
            if (ms < 1000f) return $"{ms:F0}ms";
            return $"{ms / 1000f:F2}s";
        }

        private static string TruncateArgs(string json)
        {
            if (string.IsNullOrEmpty(json)) return "{}";
            return json.Length > 100 ? json.Substring(0, 100) + "..." : json;
        }

        private enum ChatEntryType { User, Assistant, ToolCall, ToolResult }

        [Serializable]
        private class ChatEntry
        {
            [SerializeField] public ChatEntryType Type;
            [SerializeField] public string Content;
            [SerializeField] public string ToolName;
            [SerializeField] public string ToolArgs;

            // Debug metadata (populated on Assistant entries)
            [SerializeField] public float ElapsedMs;
            [SerializeField] public int PromptTokens;
            [SerializeField] public int CompletionTokens;
            [SerializeField] public int TotalTokens;
            [SerializeField] public string Model;
            [SerializeField] public int StepCount;
            [SerializeField] public string StopReason;
            [SerializeField] public bool HasDebugInfo;
            [SerializeField] public bool IsTokenEstimated;
        }
    }
}
