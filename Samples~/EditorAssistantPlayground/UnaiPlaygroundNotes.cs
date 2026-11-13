using UnityEngine;

namespace UnAI.Samples.EditorAssistantPlayground
{
    /// <summary>
    /// Instructions shown in the Inspector. This scene has no runtime code:
    /// it is an empty space to try UNAI's Editor Assistant and MCP server.
    /// </summary>
    public class UnaiPlaygroundNotes : MonoBehaviour
    {
        [TextArea(3, 10)]
        public string HowToStart =
            "This scene is intentionally empty. You don't need Play Mode.\n\n" +
            "1. Open Window > UnAI > AI Assistant.\n" +
            "2. Ask it to build something in this scene (see the example prompts below).\n" +
            "3. Scene changes go through Undo (Ctrl+Z). New scripts and assets are written to disk.";

        [TextArea(3, 14)]
        public string ExamplePrompts =
            "- What's in this scene?\n" +
            "- Create a 5x5 grid of cubes with random colors on the ground.\n" +
            "- Add a Rigidbody to every cube, drop them from 5 meters, and enter Play Mode.\n" +
            "- Create a warm orange point light above the center.\n" +
            "- Write a script that slowly rotates an object, and attach it to the selected object.\n" +
            "- Make a prefab from the selected object and save it in Assets/Prefabs.\n" +
            "- Take a screenshot of the Scene view.";

        [TextArea(3, 10)]
        public string UseFromOtherAiClients =
            "Window > UnAI > MCP Server > Start Server exposes the same tools to MCP clients.\n" +
            "Read-only tools are exposed by default; enable 'Allow mutating tools' before starting " +
            "to let clients change the scene. For Claude Code:\n" +
            "claude mcp add --transport http unity http://localhost:3389/mcp --header \"Authorization: Bearer <token>\"";
    }
}
