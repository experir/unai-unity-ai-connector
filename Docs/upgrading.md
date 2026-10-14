# Upgrading from 1.0.0 to 1.1.0-beta.1

[Back to README](../README.md)

The refresh is prepared as **1.1.0-beta.1** on `main`. No release tag or GitHub release has been published. This is a beta candidate for evaluation in a copy of your Unity project. Unity 6000.3.23f1 on Windows has been tested; the minimum supported 6000.0 editor run is still pending. See the [validation matrix](validation.md) and [changelog](../CHANGELOG.md).

## Package and samples

Update your embedded checkout or the Git package dependency through Unity Package Manager. Remove any duplicate manual copy of UNAI when switching to UPM: importing both copies produces duplicate assemblies and GUIDs. Keep application configuration assets and your own scripts outside the package.

Examples are now optional UPM samples under `Samples~`. They are not automatically imported with the package. If you used the old `Examples` folder, preserve your modifications and scene references before replacing it, then import the appropriate sample from Package Manager:

| Sample | Dependencies |
|---|---|
| Minimal Runtime Chat | Core only |
| Chat UI and Agents | Core, Agent, `com.unity.ugui` 2.0.0 (includes TextMeshPro) |

Sample GUIDs were retained during the move. Avoid keeping both the old example assets and an imported copy with the same GUIDs. Merge local modifications into one copy. Core-only installations no longer need UI just to compile the package. Newtonsoft JSON remains pinned to 3.2.2.

When removing modules, also remove their dependents and affected imported samples using the [module dependency table](../README.md#modular-structure).

## MCP clients

Existing unauthenticated client configurations must change:

1. Start the server in **Window > UnAI > MCP Server**.
2. Copy its bearer token and configure `Authorization: Bearer <token>` in the client.
3. Use a client supporting Streamable HTTP; the server returns JSON responses and does not provide legacy HTTP+SSE.
4. Leave mutating tools disabled for inspection. Enable **Allow mutating tools** explicitly when project edits are needed.

Tokens are per server instance. After script/domain reload, restart the normal MCP server and replace the client token; an old token is rejected. Custom clients must initialize a session, retain `MCP-Session-Id`, send `notifications/initialized`, and use the negotiated 2025 protocol revision. Notification acknowledgements now return HTTP 202. See the full [MCP guide](mcp.md).

Custom tools are treated as mutating by default. Set `Definition.IsReadOnly=true` only if every operation is read-only. Tool calls use exact registered names.

## Requests and provider behavior

Existing serialized config assets keep their selected models. New Claude/Gemini defaults are suggestions checked against documentation; select a model actually available to your account. The editor's **Models** action queries the provider/server catalog. Live cloud accounts have not been checked in this refresh.

Fallback now resolves each backup's configured default model. An explicit request model applies only to the primary provider. Request and cache snapshots are independent, so modifying a returned response does not modify another request or cache entry.

Streaming cancellation throws `OperationCanceledException`, including cancellation from a delta callback. Completion and additional buffered deltas are suppressed after cancellation. Automatic retries/fallback stop once content or tools have been emitted, so applications can decide how to handle partial output without duplicated content.

Keep the full returned `UnaiToolCall` objects when appending assistant tool history. Gemini thought signatures and native call IDs, and Anthropic signed content, travel in provider metadata. Use `UnaiChatMessage.AssistantWithToolCalls(response.ToolCalls, response.Content)` followed by `ToolResult(...)` with the returned ID/name instead of recreating tool calls from name/arguments alone.

OpenAI now uses `max_completion_tokens`; generic compatible services retain `max_tokens`. Generic streaming usage is opt-in through the provider config. Disable native tools or JSON Schema options for servers/models that do not support them. Ollama now preserves usage and finish reasons; tool-bearing streaming API requests remain buffered. See [provider configuration](providers.md).

## Before a stable release

The beta has local regression, build, MCP CLI and Ollama evidence. Cloud-provider checks were skipped by request, and no GUI client session was exercised. The prepared [live runner](live-validation.md#provider-runner) is available when credentials and a spending limit are authorized.

The minimum Unity 6000.0 run and licensed Unity CI still need completion. The installed machine only has a complete 6000.3.23f1 editor. [Official release metadata](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.0.68f1&platform=WINDOWS&architecture=X86_64) lists a 4.1 GB download and 7.8 GB installed size for 6000.0.68f1, before validation-project caches; another editor was not installed with approximately 6 GB free. Configure the repository's Unity license credentials and `UNITY_CI_ENABLED=true` to run the existing CI matrix, or install the minimum editor when space is available. Metadata CI runs without a license.
