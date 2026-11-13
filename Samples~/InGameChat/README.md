# In-game chat UI

A ready-to-use chat window for your game: message bubbles, replies that stream in token by token, a Stop button and New chat. It uses only the Core module.

## Run it

1. Install `com.unity.ugui` **2.0.0** (Unity UI and TextMeshPro) through Package Manager, then import this sample.
2. Create a config with **Window > UnAI > Setup Wizard** and choose a provider and model. Local models through Ollama or LM Studio need no API key.
3. Open `InGameChat.unity` and enter Play Mode. Import TextMeshPro essential resources if Unity asks.

In the Editor, the sample finds your `UnaiGlobalConfig` automatically. For a player build, assign it to the `UnaiManager` in the scene.

## Make it yours

- Select **Chat Window** and edit **System Prompt** and **Greeting** to change who the assistant is.
- The bubbles are plain UI objects under **Chat Window > Messages**. Restyle the inactive templates and every new message follows.
- `UnaiChatWindow.cs` is about 180 lines. Copy it into your project as a starting point.

Shipping a game: keep shared cloud API keys on a backend you control, not in the build.
