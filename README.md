# UNAI - Universal AI Connector for Unity

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Unity 6+](https://img.shields.io/badge/Unity-6000.0%2B-black.svg)](https://unity.com)

Connect Unity to **any AI provider** — OpenAI, Anthropic Claude, Google Gemini, Mistral, Cohere, Ollama, LM Studio, llama.cpp, xAI Grok, DeepSeek, and more — with a single, unified C# API. Use cloud models or run local LLMs. Swap providers with one line of code.

**One package, four ways to use AI in Unity:**

| Mode | What it does | Example |
|------|-------------|---------|
| **Runtime Chat** | Call any LLM from your game at runtime | NPC dialogue, AI mechanics, chatbots |
| **AI Agent** | Multi-step reasoning with tool calling and memory | Autonomous NPCs, game assistants |
| **Editor Assistant** | AI chat window inside the Unity Editor (32 tools) | Scene inspection, GameObject creation, script editing |
| **MCP Server** | Expose Unity tools to external AI clients via MCP protocol | Control Unity from Claude Desktop, Cursor, or any MCP client |

All pure C#. No Node.js, no Python, no external processes, no separate frameworks.

## Why One Package Instead of Many?

Most Unity AI solutions force you to choose: a chat SDK for one provider, a separate agent framework, a separate MCP server, a separate editor tool — each with its own dependencies, update cycles, and integration issues.

**UNAI is different.** One package gives you everything:

- **Runtime + Editor + Agent + MCP** — all built on the same `IUnaiProvider` interface and `IUnaiTool` system
- **Tools you write once work everywhere** — the same `IUnaiTool` implementation works in your game, in the editor assistant, AND is automatically exposed via MCP to external clients like Claude Desktop
- **No dependency conflicts** — one package means one Newtonsoft JSON reference, one version to track, one update to apply
- **Modular by design** — don't need agents? Delete the folder. Don't need MCP? Delete the folder. Each module has its own assembly definition — no compile errors, no orphaned references
- **Zero external dependencies** — the MCP server is a pure C# `HttpListener`, not a Node.js sidecar. The agent loop is C#, not a Python framework. Everything runs inside Unity's process with direct access to the Unity API

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
- **MCP Server** — built-in Model Context Protocol server lets Claude Desktop, Cursor, and other MCP clients control Unity
- **Editor AI Assistant** — built-in editor window with 32 tools for scene inspection, GameObject creation, physics setup, asset management, and more
- **Structured output / JSON mode** — force JSON responses with optional schema validation across all providers
- **Conversation memory** — token-aware history management with automatic truncation
- **Real-time streaming** — token-by-token responses on the main thread, perfect for chat UIs
- **Async/await** — native C# `Task`-based API, no coroutine boilerplate
- **Cross-platform** — Windows, Mac, Linux, Android, iOS, and WebGL via `UnityWebRequest`
- **Local AI support** — run models offline with Ollama, LM Studio, or llama.cpp
- **OpenAI-compatible** — works with any provider that follows the OpenAI API format (xAI Grok, DeepSeek, Perplexity, vLLM, etc.)
- **Provider fallback chain** — automatic failover to backup providers when a request fails
- **Response caching** — LRU cache with TTL for non-streaming requests
- **Lazy provider init** — providers are instantiated on first use, not at startup
- **Zero external dependencies** — only requires Newtonsoft JSON (auto-installed)
- **Runtime provider switching** — change AI backends on the fly without restarting
- **ScriptableObject config** — inspector-friendly setup with env var overrides for production
- **Modular** — 4 independent modules (core, agent, editor assistant, MCP) — use only what you need, delete the rest

## Use Cases

- **AI agents for games** — NPCs with tool use, memory, and multi-step reasoning
- **MCP bridge to Unity** — let Claude Desktop, Cursor, or any MCP client inspect and modify your Unity project
- **Editor AI assistant** — inspect scenes, create objects, set up physics, read scripts from a chat window
- **NPC dialogue** and AI-driven characters in games
- **In-editor AI tools** — coding assistants, content generation, automated scene setup
- **AI-powered game mechanics** — procedural quests, adaptive difficulty, dynamic storytelling
- **Chatbot and virtual assistant** apps built in Unity
- **Rapid prototyping** with local models (Ollama), deploying with cloud APIs (OpenAI, Claude)

## Installation

### Via Git URL (recommended)

In Unity, go to **Window > Package Manager > + > Add package from git URL** and enter:

```
https://github.com/experir/unai-unity-ai-connector.git
```

### Manual

Clone this repo into your project's `Packages` directory (e.g. `Packages/com.unai.universal-ai-connector`).

### Installing Newtonsoft JSON

UNAI requires **Newtonsoft JSON** (`com.unity.nuget.newtonsoft-json`). It is usually installed automatically, but if you get compile errors about missing `Newtonsoft.Json`, install it manually:

1. Open **Window > Package Manager**
2. Click **+ > Add package by name**
3. Enter `com.unity.nuget.newtonsoft-json` and click **Add**

Alternatively, add it directly to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

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

### Built-in tools (32)

| Category | Tools |
|----------|-------|
| **Scene** | `inspect_scene`, `find_gameobject`, `get_selection`, `focus_scene_view` |
| **GameObjects** | `create_gameobject`, `modify_gameobject`, `inspect_gameobject`, `duplicate_gameobject` |
| **Components** | `add_component_configured`, `create_physics_setup`, `set_layer_tag`, `component_properties` |
| **Materials & Lighting** | `create_material`, `create_light` |
| **Prefabs** | `create_prefab` |
| **Scripts & Assets** | `read_script`, `create_script`, `modify_script`, `list_assets`, `search_project` |
| **Asset Management** | `manage_assets` (create folder, move, copy, delete, rename, refresh, find by type) |
| **Packages** | `manage_packages` (list, add, remove Unity packages) |
| **Play Mode** | `play_mode` (play, pause, stop, step, status) |
| **Code Execution** | `execute_csharp` (compile and run C# with full Unity API access) |
| **Capture** | `capture_screenshot` (Game View or Scene View to PNG) |
| **Testing** | `run_tests` (EditMode / PlayMode via Test Runner) |
| **Project** | `get_project_settings` |
| **Batch** | `batch_execute` (multiple operations in one atomic Undo step) |
| **Editor** | `execute_menu_item`, `undo`, `get_console_logs`, `log_message` |

The assistant uses the same agent system, so it reasons through multi-step tasks and calls tools automatically.

## MCP Server

UNAI includes a built-in **Model Context Protocol (MCP)** server that exposes all 32 editor tools to external AI clients. Pure C# — no Node.js, no npm, no external processes.

### Start the server

Open **Window > UnAI > MCP Server** and click **Start Server**.

### Connect from Claude Desktop

Add this to your Claude Desktop config (Settings > Developer > Edit Config):

```json
{
  "mcpServers": {
    "unity": {
      "url": "http://localhost:3389/mcp"
    }
  }
}
```

Now Claude Desktop can inspect your scenes, create GameObjects, set up physics, read scripts, and more — all through natural language.

### How it works

- Implements the **MCP Streamable HTTP** transport (JSON-RPC 2.0 over HTTP + SSE)
- Runs inside Unity's process via `HttpListener` — full access to Unity API
- Tool calls execute on the Unity main thread (safe for all Unity operations)
- All `IUnaiTool` implementations are automatically exposed as MCP tools
- Works with any MCP-compatible client: Claude Desktop, Cursor, custom clients

### Write once, use everywhere

The same `IUnaiTool` you write for your game agent is automatically available:
1. **In your game** — via the agent's `UnaiToolRegistry`
2. **In the editor** — via the Editor AI Assistant window
3. **Via MCP** — to any external AI client like Claude Desktop

No adapters, no wrappers, no duplicate code.

## Modular Structure

UNAI is split into **4 independent modules** with clean dependency boundaries. Each module has its own assembly definition — **delete any optional folder and the project compiles with zero errors**.

```
┌─────────────────────────────────────────────────────────────────┐
│                        YOUR UNITY PROJECT                       │
│                                                                 │
│  ┌─────────────────────┐   ┌──────────────────────────────────┐ │
│  │  Editor Assistant   │   │          MCP Server              │ │
│  │  (Scripts/          │   │  (Scripts/MCP/)                  │ │
│  │   EditorAssistant/) │   │                                  │ │
│  │                     │   │  Exposes tools to Claude Desktop,│ │
│  │  AI chat window     │   │  Cursor, or any MCP client       │ │
│  │  32 built-in tools  │   │  Pure C# HttpListener            │ │
│  │  Debug panel + MCP  │   │  JSON-RPC 2.0 + SSE              │ │
│  │  controls           │   │  Own standalone window            │ │
│  │                     │   │                                  │ │
│  │  EDITOR ONLY        │   │  EDITOR ONLY                     │ │
│  └────────┬────────────┘   └──────────┬───────────────────────┘ │
│           │ depends on                │ depends on              │
│           │ (hard ref)                │ (hard ref)              │
│           ▼                           ▼                         │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                     Agent System                           │ │
│  │                  (Scripts/Agent/)                           │ │
│  │                                                            │ │
│  │  UnaiAgent           Observe-think-act reasoning loop      │ │
│  │  UnaiToolRegistry    Register and execute IUnaiTool        │ │
│  │  UnaiConversation    Token-aware memory management         │ │
│  │  UnaiToolSerializer  Text-based tool call parsing          │ │
│  │                                                            │ │
│  │  RUNTIME + EDITOR          ◄── works in shipped games      │ │
│  └────────────────────────────────┬───────────────────────────┘ │
│                                   │ depends on                  │
│                                   │ (hard ref)                  │
│                                   ▼                             │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                        Core                                │ │
│  │                   (Scripts/Runtime/)                        │ │
│  │                                                            │ │
│  │  UnaiManager           Singleton entry point               │ │
│  │  UnaiProviderRegistry  Provider lookup (lazy init)         │ │
│  │  IUnaiProvider         Unified interface for all LLMs      │ │
│  │    ├─ OpenAICompatibleBase  (OpenAI, Mistral, LM Studio,  │ │
│  │    │                         llama.cpp, xAI, DeepSeek)     │ │
│  │    ├─ AnthropicProvider     (Claude)                       │ │
│  │    ├─ GeminiProvider        (Google Gemini)                │ │
│  │    ├─ CohereProvider        (Command R/A)                  │ │
│  │    └─ OllamaProvider        (Local models)                 │ │
│  │                                                            │ │
│  │  RUNTIME + EDITOR          ◄── always required             │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Editor Scripts (Scripts/Editor/)                           │ │
│  │  Config inspector, setup wizard — always keep with Core    │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘

Key: ──── hard assembly reference (asmdef)
     Editor Assistant ◄──► MCP Server: NO direct reference
     (they discover each other via reflection at runtime)
```

### Module details

| Module | Folder | Assembly | Platform | Can delete? |
|--------|--------|----------|----------|-------------|
| **Core** | `Scripts/Runtime/` | `UnAI.Runtime` | All | No — always required |
| **Agent** | `Scripts/Agent/` | `UnAI.Agent` | All | Yes — if you only need chat |
| **Editor Assistant** | `Scripts/EditorAssistant/` | `UnAI.EditorAssistant` | Editor | Yes — MCP and Core still work |
| **MCP Server** | `Scripts/MCP/` | `UnAI.MCP` | Editor | Yes — Assistant and Core still work |

### Pick what you need

| You want... | Keep these folders | Delete these |
|---|---|---|
| Just chat API in your game | `Runtime/`, `Editor/` | `Agent/`, `EditorAssistant/`, `MCP/` |
| Runtime agents with tools | `Runtime/`, `Agent/`, `Editor/` | `EditorAssistant/`, `MCP/` |
| Editor assistant only | `Runtime/`, `Agent/`, `EditorAssistant/`, `Editor/` | `MCP/` |
| MCP server only | `Runtime/`, `Agent/`, `MCP/`, `Editor/` | `EditorAssistant/` |
| Everything | Keep all | Nothing |

### How modules discover each other

Editor Assistant and MCP Server are **sibling modules** — neither depends on the other. When both are installed:

- The **Assistant window** detects MCP via reflection and shows a "MCP Server" foldout in its Debug panel (start/stop, port, status)
- The **MCP window** detects EditorAssistant via reflection and loads the 32 tools from it
- If either module is missing, the other **still works** — no compile errors, no runtime errors, just graceful degradation

This means you get **one unified window** when both are present, but each module remains fully independent.

### IUnaiTool — write once, use everywhere

Any tool implementing `IUnaiTool` automatically works in all three contexts:

```
                    ┌─────────────────┐
                    │   Your IUnaiTool │
                    │   implementation │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
     ┌────────────┐  ┌────────────┐  ┌────────────┐
     │  In-game   │  │  Editor    │  │  MCP       │
     │  Agent     │  │  Assistant │  │  (external  │
     │  (runtime) │  │  (editor)  │  │   clients)  │
     └────────────┘  └────────────┘  └────────────┘
```

No adapters, no wrappers — the same C# class works at runtime in your game, in the editor chat window, and exposed to Claude Desktop via MCP.

## Technical Details

All HTTP goes through `UnityWebRequest` (works on every platform including WebGL). Streaming uses a custom `DownloadHandlerScript` that parses SSE/NDJSON in real-time on the main thread. Tool calling uses native provider APIs where available (OpenAI, Anthropic, Gemini) with text-based fallback for others. The MCP server uses `System.Net.HttpListener` (built into .NET) with SSE for server-initiated messages — no Node.js, no external processes.

## Configuration

The `UnaiGlobalConfig` ScriptableObject stores:
- Default provider selection
- Per-provider: base URL, API key, env var name, default model, timeout, retries, custom headers

API key resolution order:
1. Environment variable (if configured)
2. ScriptableObject field (fallback)

## Platform Support

| Platform | Chat | Streaming | Agent | MCP Server | Notes |
|----------|------|-----------|-------|------------|-------|
| Windows/Mac/Linux | Yes | Yes | Yes | Yes (Editor) | Full support |
| Android/iOS | Yes | Yes | Yes | N/A | Full runtime support |
| WebGL | Yes | Varies | Yes | N/A | Streaming depends on browser; CORS required |

## Requirements

- Unity 6 (6000.0+)
- Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`) - added automatically

## License

MIT - see [LICENSE](LICENSE). Use it however you want, just keep the copyright notice.
