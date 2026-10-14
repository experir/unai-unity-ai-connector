# MCP server

[Back to README](../README.md)

UNAI runs an editor-only HTTP server inside Unity using `HttpListener`. It exposes tools through JSON-RPC at `http://localhost:3389/mcp` by default. Keep Unity open while using it.

## Start and connect

1. Keep `Runtime`, `Agent`, `MCP`, and `EditorAssistant` installed to expose the built-in editor tools.
2. Open **Window > UnAI > MCP Server** and click **Start Server**.
3. Check the Unity Console for the endpoint and exposed tool count.
4. Configure a client that supports local HTTP MCP servers.

For Claude Code, the documented HTTP registration command is:

```sh
claude mcp add --transport http unity http://localhost:3389/mcp
claude mcp get unity
```

Use `/mcp` inside Claude Code to inspect connection status. This is client configuration guidance, not a claim that this UNAI revision has passed an end-to-end client compatibility test. See the [official Claude Code MCP guide](https://code.claude.com/docs/en/mcp).

For other clients, use their local HTTP server configuration with the URL shown in Unity. Their JSON settings formats differ. The old Claude Desktop example containing only a `url` in its developer config has been removed; do not assume that config accepts HTTP servers. Clients that support only stdio need a separate bridge, which UNAI does not include.

## Built-in and custom tools

The MCP window loads `UnaiAssistantToolsFactory.CreateEditorToolRegistry()` through reflection. Without `EditorAssistant`, it starts with an empty registry. The server can compile independently of the assistant, but it does not include its own copy of the editor tools.

Implementing `IUnaiTool` does not automatically register a tool in every context. Register runtime tools with the agent's `UnaiToolRegistry`. For the default editor assistant and MCP window, add editor tools to the shared factory. When starting a server from code, pass your own registry to `UnaiMcpServer.Start(port, tools)`.

Tools run on Unity's main thread. Some tools can delete assets, modify scripts, install packages, or execute C#. Review the available tools before giving a client access.

## Current protocol limitations

The implementation advertises MCP `2025-03-26` and handles `initialize`, `ping`, `tools/list`, and `tools/call`. It uses HTTP POST for JSON-RPC and GET for SSE connections. Full Streamable HTTP compliance and compatibility with each client have not been established.

Known gaps from source review include missing origin validation, wildcard CORS, no authentication, and HTTP 204 for accepted notifications where the advertised specification requires 202. The SSE connection also needs interoperability testing. The [MCP transport specification](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports) describes the transport and origin-validation requirements.

The server binds to `localhost`, but local binding alone does not replace origin checks. Use it only with trusted local clients while these gaps remain. See the [maintenance review](maintenance-review.md) for the proposed fixes and tests.

## Troubleshooting

- **No tools:** restore `EditorAssistant`, then restart the MCP server and check the Console's tool count.
- **Connection refused:** check that Unity is open, the server is running, and the client uses the configured port and `/mcp` path.
- **Port already in use:** stop the conflicting process or choose another port before starting the server.
- **Windows listener permission error:** inspect the `HttpListenerException`. If an HTTP URL reservation is required, have it scoped to the exact localhost URL and your Windows account. Avoid broad `Everyone` permissions or running the editor as administrator as a routine workaround.
- **Client handshake fails:** confirm HTTP transport support and inspect the initialize exchange. Report the client version and sanitized Unity logs; a listening endpoint alone does not prove protocol compatibility.
