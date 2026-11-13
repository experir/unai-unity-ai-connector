# Editor Assistant playground

An almost empty scene for trying UNAI's AI tools inside the Unity Editor. There is nothing to play: you build the scene by chatting.

## Try it

1. Keep UNAI's **Editor Assistant** module, install `com.unity.ugui` **2.0.0** (for the instruction text), then import this sample.
2. Create a config with **Window > UnAI > Setup Wizard** and choose a provider and model with tool calling support.
3. Open `EditorAssistantPlayground.unity`, then **Window > UnAI > AI Assistant**.
4. Ask for something, for example:
   - "What's in this scene?"
   - "Create a 5x5 grid of cubes with random colors on the ground."
   - "Add a Rigidbody to every cube, drop them from 5 meters, and enter Play Mode."
   - "Create a warm orange point light above the center."
   - "Write a script that slowly rotates an object, and attach it to the selected object."

Scene changes go through Undo, so **Ctrl+Z** reverts them. Scripts and assets the assistant creates are written to disk; use version control to review them. The **READ ME** object in the scene has the same instructions in the Inspector.

## Use it from other AI clients (MCP)

Open **Window > UnAI > MCP Server** and click **Start Server**. MCP clients such as Claude Code can then use the same tools. Read-only tools are exposed by default. Enable **Allow mutating tools** before starting the server to let clients change the scene. See [MCP setup](https://github.com/experir/unai-unity-ai-connector/blob/main/Docs/mcp.md).
