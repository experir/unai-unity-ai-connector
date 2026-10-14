# Live integration checks

[Back to README](../README.md)

These checks are explicit developer actions. The ordinary Unity regression suite uses fixtures and makes no cloud calls. Use disposable validation projects, never an active game project, for the MCP permission and recompilation probes.

## MCP clients

On 2026-10-14, Unity 6000.3.23f1 on Windows passed:

| Client | Verified behavior |
|---|---|
| Claude Code 2.1.34 | HTTP connection health check, including initialization and bearer authentication; no model session was invoked |
| MCP Inspector 2.11.0 CLI | Called the built-in `get_selection` tool successfully |
| Official MCP TypeScript SDK 1.32.1 | Tool discovery/calls, invalid-token rejection, mutation gating, cancellation, and reconnection after script recompilation with a replacement token |

These results do not establish Cursor or Claude Desktop GUI compatibility. The server negotiates the supported 2025 protocol revisions; it does not implement the 2026 protocol. The Inspector check used its `legacy` protocol era. See the [official Inspector guide](https://modelcontextprotocol.io/docs/2026-07-28/tools/inspector/cli) and [Claude Code MCP guide](https://code.claude.com/docs/en/mcp).

To reproduce, create a full validation project using [CONTRIBUTING.md](../CONTRIBUTING.md#reproducible-checks). Copy `Tools~/UnaiIntegrationServer.cs` to its `Assets/Editor/` directory. Create a separate temporary client directory outside the repository, and copy `Tools~/mcp_smoke_test.mjs` there. In that client directory, using Node 22.19.0 or newer:

```powershell
npm install --no-audit --no-fund --ignore-scripts @modelcontextprotocol/sdk@1.32.1 @modelcontextprotocol/inspector@2.11.0
```

Start Unity with `-batchmode -nographics -projectPath <validation-project> -executeMethod UnaiIntegrationServer.Start -unaiIntegrationDir <client-directory> -logFile <log-path>`. Keep it running; do not add `-quit`. When launching in the background on Windows, use `Start-Process -WindowStyle Hidden`.

Wait for `server.json` in the client directory, then run these commands from that directory:

```powershell
node mcp_smoke_test.mjs . mcp-sdk-report.json
npx --no-install @modelcontextprotocol/inspector --cli --config .mcp.json --server unity --protocol-era legacy --method tools/call --tool-name get_selection --tool-args-json '{}' --format json --output mcp-inspector-report.json --stored-auth-only
$env:CLAUDE_CONFIG_DIR = Join-Path $PWD 'claude-config'
claude mcp list
```

The SDK probe enables mutation access only inside this disposable project and calls a probe with no project edits. It disables mutation access again, cancels a delayed probe, and triggers script recompilation. The helper restarts on the same port, rotates its token, and rewrites its client configuration. A normal MCP window requires restarting the server and updating the client token after domain reload.

`server.json` and `.mcp.json` contain a temporary bearer token. Do not commit or share them. Stop the fixture with:

```powershell
[IO.File]::WriteAllText((Join-Path $PWD 'control.json'), '{"action":"stop"}')
```

After Unity exits, remove the temporary client directory and validation project. Keep only sanitized reports. The SDK probe is a smoke check; Unity's regression suite covers session/version errors, deadlines, payload limits and shutdown behavior separately.

## Provider runner

Copy `Tools~/UnaiLiveProviderValidation.cs` to `Assets/Editor/` of a Core or full validation project. It contacts **only** providers explicitly listed in `UNAI_LIVE_PROVIDERS`. Supported IDs are `openai`, `anthropic`, `gemini` and `ollama`. Each requires an explicit `UNAI_LIVE_<PROVIDER>_MODEL`; cloud adapters also require the environment key listed in the [provider guide](providers.md#endpoints-and-keys).

Ollama 0.17.1 with `qwen3:0.6b` passed all six live checks on Unity 6000.3.23f1 on Windows, including token usage in chat/stream responses and cancellation after the first visible delta. The local check uses temperature 0. Cloud adapters were not contacted.

For the local Ollama check, start Ollama and install a tool-capable model, then set:

```powershell
$env:UNAI_LIVE_PROVIDERS = 'ollama'
$env:UNAI_LIVE_OLLAMA_MODEL = 'qwen3:0.6b'
$env:UNAI_LIVE_MAX_TOKENS = '1024'
$env:UNAI_LIVE_REPORT = Join-Path $PWD 'ollama-live-report.json'
```

Start Unity with `-batchmode -nographics -projectPath <validation-project> -executeMethod UnaiLiveProviderValidation.Run -logFile <log-path>`. Omit `-quit`: the runner exits with code 0 only when every selected provider passes, and code 1 on failure. An empty provider list fails without contacting a provider.

The runner exercises model discovery, chat, streaming, a tool-result history round trip, tools through the streaming API, and cancellation from a visible delta. It records model IDs and check results, without response bodies or credentials. The default output limit is 1,024 tokens per request; `UNAI_LIVE_MAX_TOKENS` accepts 128–4,096. Small limits can leave reasoning models without visible output. This runner does not validate JSON against a schema or guarantee identical model text across runs.

Cloud tests use provider credits: expect six short inference requests per selected provider plus model discovery. There are no automatic retries. The token limit is not a monetary spending cap. Set the provider's account limits and authorize spending before explicitly selecting a cloud adapter. For example, set `UNAI_LIVE_PROVIDERS=openai` and `UNAI_LIVE_OPENAI_MODEL` to an available Chat Completions model; use the corresponding variables for Anthropic or Gemini. See the [OpenAI Chat Completions reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create).

Ollama requests containing tools use a non-streaming transport behind `ChatStreamAsync`, so their report records `toolResponseWasStreamed=false`. This is buffered tool support, not incremental tool-argument streaming. See [Ollama tool calling](https://docs.ollama.com/capabilities/tool-calling) and [response metadata](https://docs.ollama.com/api/chat).

Cloud calls were deliberately skipped during this refresh. Model suggestions verified against documentation are not proof of live account availability. The minimum supported Unity 6000.0 run remains pending: that editor executable was unavailable, and an additional editor was not installed with approximately 6 GB free on the machine. Licensed CI is also pending configuration.
