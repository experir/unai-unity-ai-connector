using UnAI.Editor.Assistant;
using UnityEditor;
using UnityEngine;

namespace UnAI.MCP
{
    public class UnaiMcpEditorWindow : EditorWindow
    {
        [SerializeField] private int _port = 3389;
        [SerializeField] private bool _autoStart;

        private UnaiMcpServer _server;
        private GUIStyle _statusStyle;
        private GUIStyle _urlStyle;
        private bool _stylesInitialized;
        private double _lastRepaintTime;

        [MenuItem("Window/UnAI/MCP Server")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnaiMcpEditorWindow>("UNAI MCP Server");
            window.minSize = new Vector2(350, 200);
        }

        private void OnEnable()
        {
            _server ??= new UnaiMcpServer();

            if (_autoStart && !_server.IsRunning)
                StartServer();

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnDestroy()
        {
            _server?.Stop();
        }

        private void OnEditorUpdate()
        {
            // Repaint periodically when running to update client count
            if (_server is { IsRunning: true } && EditorApplication.timeSinceStartup - _lastRepaintTime > 2.0)
            {
                _lastRepaintTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _statusStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            _urlStyle = new GUIStyle(EditorStyles.textField)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.Space(8);

            // Status
            bool running = _server is { IsRunning: true };
            string statusText = running ? "RUNNING" : "STOPPED";
            Color statusColor = running ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.4f, 0.4f);

            _statusStyle.normal.textColor = statusColor;
            EditorGUILayout.LabelField(statusText, _statusStyle);

            EditorGUILayout.Space(4);

            // Port config (disabled when running)
            EditorGUI.BeginDisabledGroup(running);
            _port = EditorGUILayout.IntField("Port", _port);
            _port = Mathf.Clamp(_port, 1024, 65535);
            EditorGUI.EndDisabledGroup();

            _autoStart = EditorGUILayout.Toggle("Auto-start on load", _autoStart);

            EditorGUILayout.Space(8);

            // Start / Stop button
            if (running)
            {
                if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
                    _server.Stop();

                EditorGUILayout.Space(8);

                // Connection info
                EditorGUILayout.LabelField("Connection URL:", EditorStyles.boldLabel);
                EditorGUILayout.TextField(_server.Url, _urlStyle);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"Connected clients: {_server.ConnectedClients}");

                EditorGUILayout.Space(8);

                // Claude Desktop config helper
                EditorGUILayout.LabelField("Claude Desktop Config:", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Add this to your Claude Desktop config\n" +
                    "(Settings > Developer > Edit Config):\n\n" +
                    "{\n" +
                    "  \"mcpServers\": {\n" +
                    "    \"unity\": {\n" +
                    $"      \"url\": \"{_server.Url}\"\n" +
                    "    }\n" +
                    "  }\n" +
                    "}",
                    MessageType.Info);

                if (GUILayout.Button("Copy URL to Clipboard"))
                {
                    GUIUtility.systemCopyBuffer = _server.Url;
                    Debug.Log($"[UNAI MCP] URL copied: {_server.Url}");
                }
            }
            else
            {
                if (GUILayout.Button("Start Server", GUILayout.Height(30)))
                    StartServer();

                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "Start the MCP server to expose Unity tools to external AI clients " +
                    "like Claude Desktop, Cursor, or any MCP-compatible application.\n\n" +
                    "No Node.js or external dependencies required.",
                    MessageType.Info);
            }
        }

        private void StartServer()
        {
            _server ??= new UnaiMcpServer();
            var tools = UnaiAssistantToolsFactory.CreateEditorToolRegistry();
            _server.Start(_port, tools);
            Repaint();
        }
    }
}
