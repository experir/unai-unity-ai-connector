# UNAI - Universal AI Connector for Unity

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Unity 6+](https://img.shields.io/badge/Unity-6000.0%2B-black.svg)](https://unity.com)

Connect Unity to **any AI provider** — OpenAI, Anthropic Claude, Google Gemini, Mistral, Cohere, Ollama, LM Studio, llama.cpp, xAI Grok, DeepSeek, and more — with a single, unified C# API. Use cloud models or run local LLMs. Swap providers with one line of code.

**AI for Unity games, tools, and apps** — chat completions, real-time streaming, async/await, cross-platform (Windows, Mac, Linux, Android, iOS, WebGL).

## Supported Providers

| Provider | Type | Streaming | Auth |
|----------|------|-----------|------|
| **OpenAI** (GPT-5.2, GPT-5, GPT-5 Mini/Nano) | Cloud | SSE | API Key |
| **Anthropic** (Claude Opus 4.6, Sonnet 4.5, Haiku 4.5) | Cloud | SSE | API Key |
| **Google Gemini** (3 Pro/Flash, 2.5 Pro/Flash) | Cloud | SSE | API Key |
| **Mistral** (Large, Medium, Small, Codestral, Devstral) | Cloud | SSE | API Key |
| **Cohere** (Command A, Command R/R+) | Cloud | SSE | API Key |
| **Ollama** | Local | NDJSON | None |
| **LM Studio** | Local | SSE | None |
| **llama.cpp** | Local | SSE | None |
| **Any OpenAI-compatible API** | Custom | SSE | Optional |

> **Tip:** Providers like **xAI (Grok)**, **DeepSeek**, and **Perplexity** use OpenAI-compatible APIs and work out of the box with the **OpenAI-compatible** provider - just set the base URL and API key.

## Features

- **Unified API** — one interface for all providers, switch with a single line
- **AI Agent system** — multi-step reasoning with tool calling, conversation memory, and observe-think-act loop
- **Tool/Function calling** — native support for OpenAI, Anthropic, and Gemini; text-based fallback for all others
- **Editor AI Assistant** — built-in editor window that can inspect scenes, create GameObjects, read scripts, and more
- **Conversation memory** — token-aware history management with automatic truncation
- **Real-time streaming** — token-by-token responses on the main thread, perfect for chat UIs
- **Async/await** — native C# `Task`-based API, no coroutine boilerplate
- **Cross-platform** — Windows, Mac, Linux, Android, iOS, and WebGL via `UnityWebRequest`
- **Local AI support** — run models offline with Ollama, LM Studio, or llama.cpp
- **OpenAI-compatible** — works with any provider that follows the OpenAI API format (xAI Grok, DeepSeek, Perplexity, vLLM, etc.)
- **Zero dependencies** — only requires Newtonsoft JSON (auto-installed)
- **Runtime provider switching** — change AI backends on the fly without restarting
- **ScriptableObject config** — inspector-friendly setup with env var overrides for production
- **Modular** — 3 independent modules (core, agent, editor assistant) — use only what you need, delete the rest

## Use Cases

- **AI agents** for games — NPCs with tool use, memory, and multi-step reasoning
- **Editor AI assistant** — inspect scenes, create objects, read scripts from a chat window
- NPC dialogue and AI-driven characters in games
- In-editor AI coding and content generation tools
- AI-powered game mechanics (procedural quests, adaptive difficulty)
- Chatbot and virtual assistant apps built in Unity
- Rapid prototyping with local models, deploying with cloud APIs

## Installation

### Via Git URL (recommended)

In Unity, go to **Window > Package Manager > + > Add package from git URL** and enter:

```
https://github.com/experir/unai-unity-ai-connector.git
```

### Manual

Clone this repo into your project's `Packages` directory (e.g. `Packages/com.unai.universal-ai-connector`).

## Quick Start

### 1. Create a config

**Assets > Create > UnAI > Global Configuration**

Or use the setup wizard: **Window > UnAI > Setup Wizard**

### 2. Set your API keys

Option A: Set environment variables (recommended for production):
```
OPENAI_API_KEY=sk-...
ANTHROPIC_API_KEY=sk-ant-...
GEMINI_API_KEY=AI...
```

Option B: Paste keys directly in the inspector (for development only).

### 3. Add UnaiManager to your scene

Add the `UnaiManager` component to any GameObject and assign your config asset.

### 4. Use it

```csharp
using UnAI.Core;
using UnAI.Models;

// Simple one-liner
string reply = await UnaiManager.Instance.QuickChatAsync("What is Unity?");

// Full request with options
var request = new UnaiChatRequest
{
    Messages = new()
    {
        UnaiChatMessage.System("You are a game design assistant."),
        UnaiChatMessage.User("Give me 3 ideas for a puzzle mechanic.")
    },
    Options = new UnaiRequestOptions { Temperature = 0.8f, MaxTokens = 500 }
};
var response = await UnaiManager.Instance.ChatAsync(request);
Debug.Log(response.Content);
```

### Streaming

```csharp
await UnaiManager.Instance.ChatStreamAsync(request,
    onDelta: delta =>
    {
        // Update UI in real-time (runs on main thread)
        myText.text = delta.AccumulatedContent;
    },
    onComplete: response => Debug.Log("Done!"),
    onError: error => Debug.LogError(error.Message)
);
```

### Switch providers at runtime

```csharp
UnaiManager.Instance.SetActiveProvider("ollama");   // local model
UnaiManager.Instance.SetActiveProvider("anthropic"); // cloud
UnaiManager.Instance.SetActiveProvider("openai");    // cloud
```

### Use a specific provider directly

```csharp
var claude = UnaiProviderRegistry.Get("anthropic");
var response = await claude.ChatAsync(new UnaiChatRequest
{
    Model = "claude-sonnet-4-5-20250929",
    Messages = new() { UnaiChatMessage.User("Hello Claude!") }
});
```

### Cancel a request

```csharp
var cts = new CancellationTokenSource();
_ = UnaiManager.Instance.ChatStreamAsync(request, onDelta: ..., ct: cts.Token);

// Later...
cts.Cancel();
```

## AI Agent System

The agent system adds multi-step reasoning with tool calling and conversation memory on top of the chat API.

### Agent with custom tools

```csharp
using UnAI.Agent;
using UnAI.Tools;

// Create tools
var tools = new UnaiToolRegistry();
tools.Register(new MyWeatherTool());   // implements IUnaiTool
tools.Register(new MyDatabaseTool());

// Create and run agent
var agent = new UnaiAgent(new UnaiAgentConfig
{
    SystemPrompt = "You are a helpful game assistant.",
    MaxSteps = 5,
    UseStreaming = true
}, tools);

agent.OnToolCall += args => Debug.Log($"Calling: {args.ToolCall.ToolName}");
agent.OnToolResult += args => Debug.Log($"Result: {args.Result.Content}");

var result = await agent.RunAsync("What's the weather in the player's city?");
Debug.Log(result.Response.Content);

// Continue the conversation (keeps memory)
var followUp = await agent.ContinueAsync("What about tomorrow?");
```

### Implementing a custom tool

```csharp
using UnAI.Tools;
using Newtonsoft.Json.Linq;

public class MyWeatherTool : IUnaiTool
{
    public UnaiToolDefinition Definition => new()
    {
        Name = "get_weather",
        Description = "Get current weather for a city.",
        ParametersSchema = JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""city"": { ""type"": ""string"", ""description"": ""City name"" }
            },
            ""required"": [""city""]
        }")
    };

    public async Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct)
    {
        var args = call.GetArguments();
        string city = args["city"].ToString();
        // ... fetch weather data ...
        return new UnaiToolResult { Content = $"Sunny, 22C in {city}" };
    }
}
```

### Conversation memory (standalone)

```csharp
using UnAI.Memory;

var conversation = new UnaiConversation { SystemPrompt = "You are an NPC shopkeeper." };
conversation.AddUser("What do you sell?");

// Build request with automatic token-aware truncation
var request = conversation.BuildRequest(maxContextTokens: 4096);
var response = await UnaiManager.Instance.ChatAsync(request);

conversation.AddAssistant(response.Content);
// Conversation remembers full history, truncates oldest when needed
```

## Editor AI Assistant

Open **Window > UnAI > AI Assistant** to get a chat-powered assistant directly in the Unity Editor.

### Built-in tools

| Tool | Description |
|------|-------------|
| `inspect_scene` | List all GameObjects in the active scene hierarchy |
| `find_gameobject` | Find GameObjects by name, tag, or component |
| `create_gameobject` | Create a GameObject with position and components (Undo supported) |
| `inspect_gameobject` | Get transform, components, and properties of a GameObject |
| `read_script` | Read contents of a C# script file |
| `list_assets` | List assets in a project folder |
| `get_selection` | Get currently selected objects in the editor |
| `log_message` | Write a message to the Unity Console |

The assistant uses the same agent system, so it reasons through multi-step tasks and calls tools automatically.

## Modular Structure

UNAI is split into **3 independent modules**. Use what you need, delete what you don't:

| Module | Folder | Assembly | Purpose |
|--------|--------|----------|---------|
| **Core** | `Scripts/Runtime/` | `UnAI.Runtime` | Chat API, providers, streaming — always required |
| **Agent System** | `Scripts/Agent/` | `UnAI.Agent` | Tool calling, memory, agent loop — for in-game AI agents |
| **Editor Assistant** | `Scripts/EditorAssistant/` | `UnAI.EditorAssistant` | Editor AI window — for developer tooling |

```
Scripts/
  Runtime/            <- Core chat API (always keep)
  Agent/              <- Agent system (delete to remove agent features)
  EditorAssistant/    <- Editor AI assistant (delete to remove editor tools)
  Editor/             <- Core editor scripts (config inspector, setup wizard)
```

**Just want chat?** Delete `Scripts/Agent/` and `Scripts/EditorAssistant/` — the core chat API works standalone.

**Want agents but no editor assistant?** Delete only `Scripts/EditorAssistant/`.

**Want everything?** Keep all folders as-is.

Each module has its own assembly definition, so removing a folder cleanly removes that feature with no compile errors.

## Architecture

```
Scripts/EditorAssistant/            (UnAI.EditorAssistant - editor only)
  UnaiAssistantWindow               AI chat window in Unity Editor
  UnaiAssistantTools                Built-in editor tools
    |
Scripts/Agent/                      (UnAI.Agent - runtime)
  UnaiAgent                         Observe-think-act loop
  Memory/UnaiConversation           Token-aware conversation history
  Tools/UnaiToolRegistry            Tool registration + execution
    |
Scripts/Runtime/                    (UnAI.Runtime - core, always required)
  UnaiManager                       MonoBehaviour singleton entry point
  UnaiProviderRegistry              Static provider lookup
  IUnaiProvider -> UnaiProviderBase Template method pattern
    -> OpenAICompatibleBase         Shared by 5 providers
    -> AnthropicProvider, GeminiProvider, CohereProvider, OllamaProvider
```

All HTTP goes through `UnityWebRequest` (works on every platform including WebGL). Streaming uses a custom `DownloadHandlerScript` that parses SSE/NDJSON in real-time on the main thread. Tool calling uses native provider APIs where available (OpenAI, Anthropic, Gemini) with text-based fallback for others.

## Configuration

The `UnaiGlobalConfig` ScriptableObject stores:
- Default provider selection
- Per-provider: base URL, API key, env var name, default model, timeout, retries, custom headers

API key resolution order:
1. Environment variable (if configured)
2. ScriptableObject field (fallback)

## Platform Support

| Platform | Chat | Streaming | Notes |
|----------|------|-----------|-------|
| Windows/Mac/Linux | Yes | Yes | Full support |
| Android/iOS | Yes | Yes | Full support |
| WebGL | Yes | Varies | Streaming depends on browser; CORS required |

## Requirements

- Unity 6 (6000.0+)
- Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`) - added automatically

## License

MIT - see [LICENSE](LICENSE). Use it however you want, just keep the copyright notice.
