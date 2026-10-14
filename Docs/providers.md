# Provider configuration

[Back to README](../README.md)

Use **Window > UnAI > Hub > Core — Setup Wizard** to create a `UnaiGlobalConfig` asset. Assign it to `UnaiManager` for runtime requests. Set `DefaultProviderId` to one of the IDs below and enable that provider.

## Endpoints and keys

These are the defaults in the package, not a list of currently available models. Set `DefaultModel` to an ID available in your provider account or local server. The static model suggestions are a fallback. In the Editor Assistant, click **Models** to fetch the account/server catalog, select a returned ID, or enter an ID manually.

| Provider | ID | Base URL | Key environment variable |
|---|---|---|---|
| OpenAI | `openai` | `https://api.openai.com` | `OPENAI_API_KEY` |
| Anthropic | `anthropic` | `https://api.anthropic.com` | `ANTHROPIC_API_KEY` |
| Gemini | `gemini` | `https://generativelanguage.googleapis.com` | `GEMINI_API_KEY` |
| Mistral | `mistral` | `https://api.mistral.ai` | `MISTRAL_API_KEY` |
| Cohere | `cohere` | `https://api.cohere.com` | `COHERE_API_KEY` |
| Ollama | `ollama` | `http://localhost:11434` | None by default |
| LM Studio | `lmstudio` | `http://localhost:1234` | None by default |
| llama.cpp | `llamacpp` | `http://localhost:8080` | None by default |
| OpenAI-compatible | `openai-compatible` | Set your server URL | Configure as needed |

OpenAI-compatible providers accept a host base URL or one ending in `/v1`; the adapter normalizes both to a single `/v1/chat/completions` path. Do not include `/chat/completions` in the base URL. Custom gateway paths still need to match your server.

Key resolution checks the configured environment variable first, then the asset's `ApiKey` field. Restart Unity and, when relevant, Unity Hub after changing environment variables so the editor inherits them.

Environment variables are useful for development and trusted server processes. They do not protect a provider secret in a distributed game. For shipped desktop, mobile, or WebGL clients, route requests through a backend you control; never include a shared provider API key in a config asset or player build.

## Local models

Live checks on 2026-10-14 passed with Ollama 0.17.1 and `qwen3:0.6b` on Unity 6000.3.23f1/Windows: discovery, chat, streaming/usage, tool history and cancellation. Tool-bearing `ChatStreamAsync` requests use a buffered non-streaming transport in this adapter. This verifies that specific local model/server combination; cloud account availability remains untested. See [live integration checks](live-validation.md).

1. Start your local model server and load or download a model.
2. Enable its provider in the config and set `DefaultModel` to the loaded model's ID. LM Studio and llama.cpp default to an empty model field.
3. Set `DefaultProviderId` to `ollama`, `lmstudio`, or `llamacpp`.
4. Make a short non-streaming chat request before trying tools or streaming.

`localhost` refers to the device running Unity. A mobile build cannot reach your development computer through `localhost`; use a reachable server address and configure the network accordingly.

## Capabilities in this implementation

| Provider adapter | Native tool payloads | JSON output behavior |
|---|---|---|
| OpenAI and OpenAI-compatible adapters, including Mistral, LM Studio and llama.cpp | Serialized; server and model must support them | Sends `response_format`; support depends on server and model |
| Anthropic | Serialized | Adds JSON/schema instructions to the system prompt; no native schema enforcement in this adapter |
| Gemini | Serialized | Sends JSON MIME type and optional response schema |
| Ollama | Serialized; model must support tools | Sends JSON format or schema |
| Cohere | Not implemented in this adapter; agent uses text fallback | Sends JSON response format and optional schema |

UNAI does not perform client-side JSON Schema validation. Validate returned content in your application when its structure matters. A provider exposing a tool API does not mean every model supports tool calling.

## Request options

`UnaiRequestOptions` exposes `Temperature`, `MaxTokens`, `TopP`, `StopSequences`, `ResponseFormat`, and schema fields. Support varies by provider and model. Start with no options, then add the settings your model accepts.

The OpenAI adapter maps `MaxTokens` to `max_completion_tokens`; generic compatible adapters keep `max_tokens`. Typed sampling options are rejected for reasoning model/effort combinations without supported sampling. Leave options unset unless your model accepts them. [OpenAI Chat Completions reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create).

`ExtraParameters` is currently applied by the OpenAI-compatible serializer only. Do not assume an option is forwarded by every adapter.

## Fallback and caching

`FallbackProviderIds` sets the ordered backup providers for retryable failures through `UnaiManager`. Authentication and invalid-request errors do not trigger fallback. A standalone `UnaiAgent` calls its selected provider directly and does not use the manager's fallback chain.

Requests with `UseCache = true` opt into the manager's non-streaming response cache. Streaming responses are not cached.

Fallback uses an independent request per provider. An explicit model ID applies to the primary provider only; backups use their own configured defaults. Cache identity includes the resolved model, full message/tool data, endpoint, credentials and registry revision. Cached responses are copied on insertion and retrieval.

## Capability controls and model discovery

Provider configs expose `EnableToolCalling`, `EnableJsonSchema` and `IncludeStreamingUsage`. Disable native tools or schema options for servers/models that do not support them. The agent uses text fallback when native tools are disabled. Generic compatible streaming usage is opt-in; OpenAI always requests usage. These controls describe adapter behavior and cannot prove a model supports a feature.

`IUnaiModelProvider.ListModelsAsync()` fetches catalogs for OpenAI-compatible adapters (`/v1/models`), Anthropic (`/v1/models`), Gemini (`/v1beta/models`) and Ollama (`/api/tags`), handling pagination where provided. Cohere discovery is not implemented. Catalogs may contain models unsuitable for chat; choose one supported by the adapter endpoint. Discovery is an explicit user action and does not automatically overwrite a config. See the official [OpenAI model API](https://developers.openai.com/api/reference/resources/models/methods/list), [Anthropic model API](https://platform.claude.com/docs/en/api/models/list), and [Ollama model API](https://docs.ollama.com/api/tags).

New config assets use `claude-sonnet-5-5` and `gemini-3.8-flash`, based on provider catalogs checked on 2026-10-14. Existing serialized config assets retain their chosen model IDs. OpenAI retains `gpt-5.2` as its default, with its documented 400,000-token context window. These are documentation-verified suggestions; live account availability has not been tested. [Claude models](https://platform.claude.com/docs/en/models/overview), [Gemini models](https://ai.google.dev/gemini-api/docs/models), [GPT-5.2](https://developers.openai.com/api/docs/models/gpt-5.2).

Gemini tool history retains original response parts, thought signatures and native call IDs, including streamed tool calls. Anthropic retains signed content blocks and assembles streamed tool argument fragments before replaying tool history. Thought summaries are excluded from visible response text. See [Gemini thinking](https://ai.google.dev/gemini-api/docs/thinking) and [Anthropic streaming](https://platform.claude.com/docs/en/build-with-claude/streaming).
