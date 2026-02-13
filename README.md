# UNAI - Universal AI Connector for Unity

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](Assets/UnAI/LICENSE)
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
- **Real-time streaming** — token-by-token responses on the main thread, perfect for chat UIs
- **Async/await** — native C# `Task`-based API, no coroutine boilerplate
- **Cross-platform** — Windows, Mac, Linux, Android, iOS, and WebGL via `UnityWebRequest`
- **Local AI support** — run models offline with Ollama, LM Studio, or llama.cpp
- **OpenAI-compatible** — works with any provider that follows the OpenAI API format (xAI Grok, DeepSeek, Perplexity, vLLM, etc.)
- **Zero dependencies** — only requires Newtonsoft JSON (auto-installed)
- **Runtime provider switching** — change AI backends on the fly without restarting
- **ScriptableObject config** — inspector-friendly setup with env var overrides for production

## Use Cases

- NPC dialogue and AI-driven characters in games
- In-editor AI coding and content generation tools
- AI-powered game mechanics (procedural quests, adaptive difficulty)
- Chatbot and virtual assistant apps built in Unity
- Rapid prototyping with local models, deploying with cloud APIs

## Installation

### Via Git URL (recommended)

In Unity, go to **Window > Package Manager > + > Add package from git URL** and enter:

```
https://github.com/experir/unity-unai-universal-ai-connector.git?path=Assets/UnAI
```

### Manual

Clone this repo and copy the `Assets/UnAI` folder into your project's `Assets` directory.

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

## Architecture

```
UnaiManager (MonoBehaviour, optional singleton)
    -> UnaiProviderRegistry (static, holds all providers)
        -> IUnaiProvider (interface)
            -> UnaiProviderBase (template method pattern)
                -> OpenAICompatibleBase (shared by 5 providers)
                    -> OpenAIProvider, MistralProvider, LMStudioProvider, etc.
                -> AnthropicProvider, GeminiProvider, CohereProvider, OllamaProvider
```

All HTTP goes through `UnityWebRequest` (works on every platform including WebGL). Streaming uses a custom `DownloadHandlerScript` that parses SSE/NDJSON in real-time on the main thread.

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

MIT - see [LICENSE](Assets/UnAI/LICENSE). Use it however you want, just keep the copyright notice.
