# Browser validation

[Back to README](../README.md)

The `1.1.0-beta.1` WebGL player was exercised in Chrome on Windows with Unity 6000.3.23f1 and local Ollama 0.17.1 / `qwen3:0.6b`. Cloud providers were not contacted. The minimum Unity 6000.0 editor, other browsers and hosted HTTPS deployments remain unverified.

## WebGL checks

The disposable test player calls UNAI's real Ollama adapter. Two checks contact the local model directly across origins. The remaining checks use a loopback-only fixture with controlled response timing, UTF-8 fragments and CORS headers.

| Check | What it verifies |
|---|---|
| Ollama chat | Cross-origin JSON request, visible response and token usage |
| Ollama stream | Cross-origin NDJSON, final content and token usage |
| Slow stream | First callback arrives before the four-second response ends; Unicode and final metadata survive |
| Network cancellation | Cancelling an active download settles without completion |
| Callback cancellation | First visible delta can cancel without more content or completion |
| Deadline | A one-second request timeout ends a stalled stream |
| Malformed stream | Invalid NDJSON reports a streaming error, without successful completion |
| CORS denial | An endpoint without CORS authorization is rejected and classified as a network error |

Unity's WebGL transport may supply a placeholder HTTP 500 for a failed browser fetch. UNAI checks the transport result first, so CORS/connection failures are network errors while actual HTTP 500 responses remain server errors. Browser errors do not reveal the exact network cause to game code; use browser developer tools to distinguish CORS, DNS, TLS and connection failures. These behaviors follow Unity's [Web networking requirements](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-networking.html).

To reproduce, run these commands from the package checkout, choosing a new project directory:

```powershell
python Tools~/prepare_unity_project.py ../unai-browser-check --modules core
Copy-Item Tools~/UnaiBrowserValidation.cs ../unai-browser-check/Assets/
Copy-Item Tools~/UnaiBrowserValidation.jslib ../unai-browser-check/Assets/
Copy-Item Tools~/UnaiBrowserValidationBuild.cs ../unai-browser-check/Assets/Editor/
```

Build that project with the installed Unity executable and `-batchmode -nographics -quit -projectPath <project> -buildTarget WebGL -executeMethod UnaiBrowserValidationBuild.Build -logFile <log>`. It generates an uncompressed validation player with no provider keys. On Windows, launch a background editor with `Start-Process -WindowStyle Hidden`.

Start Ollama with `qwen3:0.6b` installed, then start the fixture host:

```powershell
python Tools~/serve_browser_validation.py ../unai-browser-check/Builds/WebGL ../unai-validation-results/browser
```

Open **http://localhost:8765** and click **Run browser checks** after the player loads. The host also binds **127.0.0.1:8766** for cross-origin fixtures; both ports must be free. Its allowed fixture origin is exactly `http://localhost:8765`. The page shows each result and saves `webgl-browser.json` in the report directory. The fixture request log contains only test paths, methods and origin/header names, not model messages or credentials.

The fixture deliberately cancels downloads and denies one preflight, so expected browser console warnings/errors may appear. A successful check means the error was handled as expected. Stop the host after testing, and remove the disposable project when no longer needed.

## MCP Inspector browser UI

The official MCP Inspector 2.11.0 web UI connected to the isolated Unity MCP server, negotiated `2025-11-25`, displayed its read-only catalog, called `get_selection`, and cancelled the delayed integration probe. A subsequent built-in tool call succeeded. This extends the previously tested [CLI and SDK checks](live-validation.md#mcp-clients); it does not establish Claude Desktop or Cursor GUI compatibility.

Use the temporary full-module project and client configuration from that guide. Run Inspector with Node 22.19.0 or newer, `--web --config .mcp.json`, and `MCP_AUTO_OPEN_ENABLED=false` when controlling the browser yourself. Keep its per-launch API authentication enabled. Open the URL printed by Inspector, connect to **unity**, select **Tools**, and execute a read-only tool. The default catalog also contains the delayed `integration_wait` probe for its **Cancel** control.

Inspector's web client uses a local Node backend for the actual MCP connection. This test verifies that UI workflow and transport together; it does not test a browser making a direct cross-origin MCP request. See the [official Inspector web guide](https://modelcontextprotocol.io/docs/2026-07-28/tools/inspector/web).

## Release limits

Local browser success is limited to the tested Unity/editor, Chrome, model and server combination. Remote-provider CORS policies, hosted HTTPS/mixed-content rules, browser local-network permissions and distribution of provider credentials require separate deployment checks. Use an application backend for shared cloud-provider secrets, as described in the [provider guide](providers.md#endpoints-and-keys).

Unity 6000.0 is explicitly **unverified** for this beta. The executable was unavailable locally, and licensed CI was not configured. See [upgrade and release notes](upgrading.md).
