using UnityEditor;

namespace UnAI.MCP
{
    /// <summary>
    /// Menu item redirect — MCP server controls are now integrated into the
    /// main UNAI Assistant window (Debug > MCP Server foldout).
    /// This class just provides the menu entry for discoverability.
    /// </summary>
    public static class UnaiMcpMenuRedirect
    {
        [MenuItem("Window/UnAI/MCP Server")]
        public static void OpenAssistantWithMcp()
        {
            // Open the main assistant window — user can find MCP in the Debug foldout
            EditorApplication.ExecuteMenuItem("Window/UnAI/AI Assistant");
        }
    }
}
