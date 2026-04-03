using UnityEngine;

namespace UnAI.MCP
{
    /// <summary>
    /// Configuration for the MCP server. Create via Assets → Create → UnityMcp → Server Config.
    /// </summary>
    [CreateAssetMenu(fileName = "McpServerConfig", menuName = "UnityMcp/Server Config")]
    public class McpServerConfig : ScriptableObject
    {
        [Header("Server Identity")]
        [Tooltip("Name reported to MCP clients in the initialize handshake.")]
        public string ServerName    = "unity-mcp-server";

        [Tooltip("Semantic version of this server.")]
        public string ServerVersion = "1.0.0";

        [Tooltip("Optional instructions shown to the AI about how to use this server.")]
        [TextArea(3, 6)]
        public string Instructions  = "Unity MCP Server. Use the provided tools to interact with the Unity Editor and Runtime.";

        [Header("Transport — StreamableHTTP")]
        [Tooltip("TCP port the HTTP server listens on.")]
        [Range(1024, 65535)]
        public int Port = 3333;

        [Tooltip("Base path for the MCP endpoint (must start with /).")]
        public string BasePath = "/mcp";

        [Tooltip("Allow cross-origin requests (needed when the MCP client runs in a browser or remote process).")]
        public bool AllowCors = true;

        [Tooltip("Auto-start the server when the component Awakes (Runtime) or the Editor loads (Editor).")]
        public bool AutoStart = true;

        [Tooltip("Log all incoming/outgoing JSON-RPC messages to the Unity Console.")]
        public bool VerboseLogging = false;

        // ── Derived helpers ────────────────────────────────────────────────────

        public string ListenPrefix => $"http://localhost:{Port}{BasePath}/";
        public string SseEndpoint  => $"http://localhost:{Port}{BasePath}/sse";
        public string PostEndpoint => $"http://localhost:{Port}{BasePath}/message";
    }
}