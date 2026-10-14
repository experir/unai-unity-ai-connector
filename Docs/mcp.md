# MCP server

[Back to README](../README.md)

UNAI runs an editor-only HTTP server inside Unity using `HttpListener`. It exposes tools through JSON-RPC at `http://localhost:3389/mcp` by default. Keep Unity open while using it.

## Start and connect

1. Keep `Runtime`, `Agent`, `MCP`, and `EditorAssistant` installed to expose the built-in editor tools.
2. Open **Window > UnAI > MCP Server** and click **Start Server**.
3. Check the Unity Console for the endpoint and exposed tool count.
4. Click **Copy Bearer Token**, then configure your client's `Authorization: Bearer <token>` header. Tokens are specific to a server instance and change after recreation or domain reload.
5. Enable **Allow mutating tools** before starting only when you want tools that change the project. Read-only tools are the default.

For Claude Code, the documented HTTP registration command is:

```sh
claude mcp add --transport http unity http://localhost:3389/mcp --header "Authorization: Bearer <token>"
claude mcp get unity
```

Use `/mcp` inside Claude Code to inspect connection status. Claude Code 2.1.34's HTTP health check passed locally; MCP Inspector 2.11.0 called a built-in Unity tool, and official SDK 1.32.1 probes passed permissions, cancellation and reload/reconnection checks. No Claude model session was invoked. See [reproduction steps](live-validation.md#mcp-clients) and the [official Claude Code MCP guide](https://code.claude.com/docs/en/mcp).

For other clients, use their local HTTP server configuration with the URL shown in Unity. Their JSON settings formats differ. The old Claude Desktop example containing only a `url` in its developer config has been removed; do not assume that config accepts HTTP servers. Clients that support only stdio need a separate bridge, which UNAI does not include.

## Built-in and custom tools

The MCP window loads `UnaiAssistantToolsFactory.CreateEditorToolRegistry()` through reflection. Without `EditorAssistant`, it starts with an empty registry. The server can compile independently of the assistant, but it does not include its own copy of the editor tools.

Implementing `IUnaiTool` does not automatically register a tool in every context. Register runtime tools with the agent's `UnaiToolRegistry`. For the default editor assistant and MCP window, add editor tools to the shared factory. When starting a server from code, pass your own registry to `UnaiMcpServer.Start(port, tools)`.

Tools run on Unity's main thread. The default read-only catalog contains scene/object inspection, selection, script reading, asset listing/search and project settings. Tools with side effects are hidden and rejected unless mutating tools are enabled. Custom tools default to mutating; set `Definition.IsReadOnly = true` only when all their operations have no side effects.

## Current protocol limitations

The implementation negotiates MCP `2025-11-25` or `2025-06-18`. POST requests return JSON; accepted notifications and responses return HTTP 202 with no body. A JSON-only Streamable HTTP server may return HTTP 405 for GET, which this server does. Legacy HTTP+SSE transport and JSON-RPC batch requests are not supported.

`initialize` issues an `MCP-Session-Id`. Clients must include it on subsequent requests and send `notifications/initialized` before listing or calling tools. Include the negotiated `MCP-Protocol-Version` when supported by the client. Unknown sessions return 404; unsupported/mismatched protocol headers return 400. DELETE ends a session. Idle sessions expire after 30 minutes.

Client cancellation is scoped to its session through `notifications/cancelled` and the original `requestId`. Request deadlines and editor shutdown/reload cancel queued and active work. Cancellation is cooperative and does not undo tool changes already made. Defaults are a 30-second request deadline, 1 MiB request body, 32 concurrent HTTP requests and 64 sessions. Configure `RequestTimeoutSeconds` and `MaxRequestBytes` on a custom server if needed.

Origin validation and bearer authentication run before dispatch. Native local clients may omit Origin, but still need the token; browsers must use the exact server origin. CORS is restricted. The server binds to `localhost` and rejects non-loopback peers. Custom tools are not exposed as read-only unless explicitly marked.

Unity tests exercise initialize/list/call/cancel exchanges with .NET HTTP and UnityWebRequest clients, including version negotiation, notification status, authorization, deadlines and request-size limits. Independent CLI clients and the [Inspector browser UI](browser-validation.md#mcp-inspector-browser-ui) were exercised; Claude Code/Desktop and Cursor GUI sessions remain unverified. The server supports the two documented 2025 protocol revisions, not the 2026 revision. See the [MCP transport specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports) and [maintenance review](maintenance-review.md).

## Troubleshooting

- **No tools:** restore `EditorAssistant`, then restart the MCP server and check the Console's tool count.
- **Connection refused:** check that Unity is open, the server is running, and the client uses the configured port and `/mcp` path.
- **Port already in use:** stop the conflicting process or choose another port before starting the server.
- **Windows listener permission error:** inspect the `HttpListenerException`. If an HTTP URL reservation is required, have it scoped to the exact localhost URL and your Windows account. Avoid broad `Everyone` permissions or running the editor as administrator as a routine workaround.
- **Client handshake fails:** confirm HTTP transport support, bearer header, session ID and negotiated version, then inspect the initialize exchange. Report the client version and sanitized Unity logs; a listening endpoint alone does not prove protocol compatibility.
