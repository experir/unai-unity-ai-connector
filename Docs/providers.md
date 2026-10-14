# Provider configuration

[Back to README](../README.md)

Use **Window > UnAI > Hub > Core — Setup Wizard** to create a `UnaiGlobalConfig` asset. Assign it to `UnaiManager` for runtime requests. Set `DefaultProviderId` to one of the IDs below and enable that provider.

## Endpoints and keys

These are the defaults in the package, not a list of currently available models. Set `DefaultModel` to an ID available in your provider account or local server. The editor's model suggestions are hardcoded and may be outdated; you can enter a model ID manually.

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

OpenAI-compatible providers append `/v1/chat/completions` to `BaseUrl`. For example, use `http://localhost:1234`, rather than `http://localhost:1234/v1`. If your service's URL already ends in `/v1`, including it will produce a duplicated path with the current implementation. Check the actual endpoint path before configuring a custom service.

Key resolution checks the configured environment variable first, then the asset's `ApiKey` field. Restart Unity and, when relevant, Unity Hub after changing environment variables so the editor inherits them.

Environment variables are useful for development and trusted server processes. They do not protect a provider secret in a distributed game. For shipped desktop, mobile, or WebGL clients, route requests through a backend you control; never include a shared provider API key in a config asset or player build.

## Local models

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

The shared OpenAI-compatible serializer currently maps `MaxTokens` to `max_tokens`. OpenAI documents this parameter as deprecated in favor of `max_completion_tokens`, with model-specific restrictions. A dedicated OpenAI parameter update needs compatibility tests; see the [maintenance review](maintenance-review.md). [OpenAI Chat Completions reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create).

`ExtraParameters` is currently applied by the OpenAI-compatible serializer only. Do not assume an option is forwarded by every adapter.

## Fallback and caching

`FallbackProviderIds` sets the ordered backup providers for retryable failures through `UnaiManager`. Authentication and invalid-request errors do not trigger fallback. A standalone `UnaiAgent` calls its selected provider directly and does not use the manager's fallback chain.

Requests with `UseCache = true` opt into the manager's non-streaming response cache. Streaming responses are not cached.

The current fallback path mutates a request's model, and the cache key omits tool definitions and tool-call metadata. Cross-provider fallback and caching tool-bearing requests need fixes before relying on them; see the [maintenance review](maintenance-review.md).
