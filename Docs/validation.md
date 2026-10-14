# Refresh validation

[Back to README](../README.md)

Validated locally on **2026-10-14**, using **Unity 6000.3.23f1 on Windows**. These checks use isolated projects, provider fixtures, independent MCP clients and live local Ollama inference; no cloud-provider credits were used.

| Check | Result |
|---|---|
| Unity regression suite | 73 tests passed |
| Play Mode with domain and scene reload disabled | Registry reset, singleton restoration and cache clearing passed |
| Core-only package and minimal sample | Compiled and built a Windows player; no Unity UI or Agent dependency |
| Full package with UI/Agent samples | Compiled and built Windows and WebGL players |
| Agent-only, Assistant-only, MCP-only module combinations | Compiled successfully with required dependencies |
| MCP endpoint | .NET HTTP and UnityWebRequest initialize/list exchanges, tool calls, cancellation, authorization, deadlines and size limits passed |
| Independent MCP clients | Claude Code 2.1.34 health check, MCP Inspector 2.11.0 CLI tool call, official SDK 1.32.1 permissions/cancellation/recompile probes passed |
| MCP Inspector browser UI | Connection, read-only catalog, built-in tool call, cancellation and a subsequent tool call passed |
| WebGL runtime in Chrome/Windows | Eight checks passed: live Ollama CORS/chat/streaming, incremental Unicode delivery, network/callback cancellation, timeout, malformed stream and CORS denial |
| Live local inference | Ollama 0.17.1 + qwen3:0.6b discovery, chat, streaming/usage, tool history, buffered tools through the streaming API and cancellation passed |
| Provider fixtures | All nine serializers, model catalogs/pagination, stream metadata, signed tool history and native call IDs passed |
| Package and documentation checks | Versions, metadata, optional sample paths and local links passed |
| README logo | Original artwork preserved; rounded dark square previewed on light and dark backgrounds |

The regression suite includes UTF-8 byte-boundary cases, fallback request isolation, cache collision/invalidation cases, HTTP streaming fixtures, MCP sessions and provider round trips. It also covers cancellation from a fully buffered response callback, Ollama metadata/error events, and transport failures taking precedence over Unity's placeholder HTTP statuses. Scripts in `Tools~` reproduce the validation projects and player builds; see [CONTRIBUTING.md](../CONTRIBUTING.md), [live integration checks](live-validation.md) and [browser validation](browser-validation.md).

GitHub's metadata check needs no license. Unity CI is configured for 6000.0.68f1 and 6000.3.23f1, and Core/full Windows/WebGL builds, but licensed jobs stay skipped until Unity credentials and `UNITY_CI_ENABLED=true` are configured. The 6000.0 editor executable was unavailable locally, so that minimum-version run remains pending.

Live cloud accounts, mobile/macOS/Linux players, other browsers, hosted HTTPS deployments, and Claude Desktop/Cursor GUI sessions were not exercised. The WebGL runtime checks establish the tested localhost Ollama/fixture combination; they do not establish remote-provider CORS or hosted deployment behavior. Model suggestions are checked against official documentation, not live cloud-account availability.
