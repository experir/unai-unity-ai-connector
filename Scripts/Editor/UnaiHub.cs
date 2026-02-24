using System;
using UnityEditor;
using UnityEngine;

namespace UnAI.Editor
{
    public class UnaiHub : EditorWindow
    {
        private Texture2D _logo;
        private Vector2 _scroll;

        // ── Module detection cache (refreshed on every OnEnable / domain reload) ──
        private bool _hasEditorAssistant;
        private bool _hasMcp;
        private bool _hasAgent;
        private bool _hasExamples;

        [MenuItem("Window/UnAI/Hub")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnaiHub>("UNAI Hub");
            window.minSize = new Vector2(340, 420);
        }

        private void OnEnable()
        {
            _logo = Resources.Load<Texture2D>("UnaiLogo");
            DetectModules();
        }

        private void OnFocus()
        {
            // Re-detect in case the user just deleted / re-added a folder.
            DetectModules();
        }

        // ───────────────────────────────────────────────────────────────────
        //  Module detection — uses Type.GetType so there is zero hard
        //  dependency on any optional assembly.
        // ───────────────────────────────────────────────────────────────────
        private void DetectModules()
        {
            _hasEditorAssistant = TypeExists("UnAI.Editor.Assistant.UnaiAssistantWindow, UnAI.EditorAssistant");
            _hasMcp             = TypeExists("UnAI.MCP.UnaiMcpEditorWindow, UnAI.MCP");
            _hasAgent           = TypeExists("UnAI.Agent.UnaiAgent, UnAI.Agent");
            _hasExamples        = AssetDatabase.IsValidFolder("Assets/unai-unity-ai-connector/Examples");
        }

        private static bool TypeExists(string assemblyQualifiedName)
        {
            return Type.GetType(assemblyQualifiedName) != null;
        }

        // ───────────────────────────────────────────────────────────────────
        //  GUI
        // ───────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            EditorGUILayout.Space(12);
            DrawModuleButtons();
            EditorGUILayout.Space(12);
            DrawUtilities();
            GUILayout.FlexibleSpace();
            DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        // ── Header: logo + version ──────────────────────────────────────
        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical();

            if (_logo != null)
            {
                const float maxW = 64;
                float aspect = (float)_logo.height / _logo.width;
                float w = Mathf.Min(maxW, position.width - 40);
                float h = w * aspect;
                Rect logoRect = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
                logoRect.x = (position.width - w) * 0.5f;
                GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit);
                EditorGUILayout.Space(4);
            }

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("UNAI — Universal AI Connector", titleStyle);

            var versionStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 11
            };
            EditorGUILayout.LabelField($"v{UnaiVersion.Get()}", versionStyle);

            EditorGUILayout.EndVertical();
        }

        // ── Module buttons ──────────────────────────────────────────────
        private const float ModuleRowHeight = 36;

        private void DrawModuleButtons()
        {
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            // Core — always present (runtime)
            DrawModuleRow(
                "⚡  Core",
                "9 providers · Streaming · Unified API",
                "runtime",
                true,
                () => UnaiSetupWizard.ShowWindow());

            // Agent (runtime)
            DrawModuleRow(
                "🤖  Agent",
                "Tool calling · Memory · Reasoning",
                "runtime",
                _hasAgent,
                null);

            // Editor Assistant (editor only)
            DrawModuleRow(
                "🖥  Editor Assistant",
                "32 Unity tools · AI chat window",
                "editor",
                _hasEditorAssistant,
                () => OpenWindowByType("UnAI.Editor.Assistant.UnaiAssistantWindow, UnAI.EditorAssistant",
                    "UNAI Assistant"));

            // MCP Server (editor only)
            DrawModuleRow(
                "🔌  MCP Server",
                "Claude Desktop · Cursor · any MCP client",
                "editor",
                _hasMcp,
                () => OpenWindowByType("UnAI.MCP.UnaiMcpEditorWindow, UnAI.MCP",
                    "UNAI MCP Server"));

            // Examples
            DrawModuleRow(
                "📂  Examples",
                "Demo scenes and sample scripts",
                "dev",
                _hasExamples,
                () =>
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                        "Assets/unai-unity-ai-connector/Examples");
                    if (obj != null)
                    {
                        EditorGUIUtility.PingObject(obj);
                        Selection.activeObject = obj;
                    }
                });
        }

        /// <summary>
        /// Draws a single module row: [ emoji  Name  (description)  tag  ✓/✗ ]
        /// If <paramref name="onClick"/> is null or module is missing, the button is disabled.
        /// </summary>
        private static void DrawModuleRow(string label, string description, string tag, bool installed, Action onClick)
        {
            bool wasEnabled = GUI.enabled;
            GUI.enabled = installed && onClick != null;

            EditorGUILayout.BeginHorizontal(GUILayout.Height(ModuleRowHeight));

            // Main button area
            if (GUILayout.Button("", GUILayout.Height(ModuleRowHeight), GUILayout.ExpandWidth(true)))
            {
                onClick?.Invoke();
            }

            // Draw content over the button
            Rect btnRect = GUILayoutUtility.GetLastRect();

            // Module name
            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 0, 0, 0)
            };
            Rect nameRect = new Rect(btnRect.x, btnRect.y, btnRect.width * 0.4f, btnRect.height * 0.55f);
            GUI.Label(nameRect, label, nameStyle);

            // Description below name
            var descStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 0, 0, 0),
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };
            Rect descRect = new Rect(btnRect.x, btnRect.y + btnRect.height * 0.45f, btnRect.width * 0.65f, btnRect.height * 0.55f);
            GUI.Label(descRect, description, descStyle);

            // Tag (runtime / editor / dev)
            var tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = tag == "runtime" ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.5f, 0.7f, 1f) },
                padding = new RectOffset(0, 36, 0, 0)
            };
            Rect tagRect = new Rect(btnRect.x + btnRect.width * 0.5f, btnRect.y, btnRect.width * 0.5f, btnRect.height);
            GUI.Label(tagRect, tag, tagStyle);

            // Installed check mark
            var checkStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 14,
                padding = new RectOffset(0, 8, 0, 0),
                normal = { textColor = installed ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.5f, 0.5f, 0.5f) }
            };
            Rect checkRect = new Rect(btnRect.x + btnRect.width - 30, btnRect.y, 26, btnRect.height);
            GUI.Label(checkRect, installed ? "✓" : "✗", checkStyle);

            GUI.enabled = wasEnabled;
            EditorGUILayout.EndHorizontal();
        }

        // ── Utility shortcuts ───────────────────────────────────────────
        private void DrawUtilities()
        {
            EditorGUILayout.LabelField("Links", EditorStyles.boldLabel);

            if (GUILayout.Button("Open Documentation ↗", GUILayout.Height(26)))
            {
                Application.OpenURL("https://github.com/experir/unai-unity-ai-connector");
            }
        }

        // ── Footer ─────────────────────────────────────────────────────
        private void DrawFooter()
        {
            EditorGUILayout.Space(8);
            var footerStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);

            string modules = string.Join(" · ",
                new[]
                {
                    "Core ✓",
                    _hasAgent           ? "Agent ✓" : null,
                    _hasEditorAssistant ? "Assistant ✓" : null,
                    _hasMcp             ? "MCP ✓" : null,
                });
            EditorGUILayout.LabelField(modules, footerStyle);
            EditorGUILayout.Space(4);
        }

        // ── Helpers ────────────────────────────────────────────────────
        private static void OpenWindowByType(string assemblyQualifiedName, string title)
        {
            Type t = Type.GetType(assemblyQualifiedName);
            if (t == null)
            {
                Debug.LogWarning($"[UNAI Hub] Could not find type: {assemblyQualifiedName}");
                return;
            }

            var window = GetWindow(t, false, title);
            window.Show();
            window.Focus();
        }
    }
}
