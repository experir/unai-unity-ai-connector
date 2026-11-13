# Changelog

All notable changes to UNAI will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0-beta.2] - 2026-11-13

Beta prerelease: new samples, local server auto-start, MCP resources and an agent HTTP API. Tested on Unity 6000.6.8f1 / Windows; **Unity 6000.0 remains unverified**. See [upgrade notes](Docs/upgrading.md).

### Added
- NPC Dialogue sample: talkable NPCs with personas, memory and streaming, plus a shop NPC that sells items through agent tools
- In-Game Chat UI sample: chat window with message bubbles, streaming replies, Stop and New chat (Core only)
- Editor Assistant Playground sample: empty scene with instructions and example prompts for the Editor Assistant and MCP server
- Samples pick the UI input module matching the project (Input System package or Input Manager) and render in Built-in, URP and HDRP
- Chat, NPC and Chat UI and Agents samples render emojis with the system color emoji font (Windows, macOS/iOS, Android) and remove them where none is available
- `Tools~/build_sample_scenes.py` regenerates the sample scenes in batch mode
- `UnaiRequestOptions.Reasoning` turns model reasoning on or off; Ollama sends it as `think`. Thinking models such as qwen3 answer up to ~100x faster with it off
- MCP resources: `unity://project/info`, `unity://scene/hierarchy`, `unity://selection` and `unity://console/recent`, plus `UnaiMcpResources.Register` for your own. Idea from PR #1 by jojo-WJ
- Agent HTTP API (`UnaiAgentHttpComponent`): other apps on the same computer can chat with a UnaiAgent in the Editor or a desktop build. Localhost only, bearer token, per-session memory. Idea from PR #1 by jojo-WJ
- Local servers start automatically: when Ollama, LM Studio or llama.cpp is off, requests start it (Editor and desktop builds) and wait until it answers. Configure with Auto Start Server and Server Start Command; nothing is downloaded

### Fixed
- Replaced deprecated `FindFirstObjectByType` in `UnaiManager` (warning on Unity 6.3+)
- Network errors from local providers now say to start the local server instead of checking the internet connection

## [1.1.0-beta.1] - 2026-10-14

Beta prerelease with **Unity 6000.0 explicitly unverified**. See [upgrade instructions](Docs/upgrading.md), [validation coverage](Docs/validation.md) and [browser checks](Docs/browser-validation.md) before adopting it.

### Added
- WebGL browser validation player and loopback fixtures for CORS, incremental streaming, Unicode, cancellation and deadlines
- Explicit MCP client probes and opt-in live-provider validation tooling with sanitized reports
- Reproducible module/sample validation projects and GitHub metadata, Unity test and player build workflows
- Account/server model discovery, editor model selection and provider capability controls
- Optional UPM samples, including minimal runtime chat without UI or Agent dependencies
- Repository guides for provider configuration, MCP setup and troubleshooting
- Source-based maintenance review with prioritized fixes and verification criteria
- README logo in a rounded dark square for visibility on light and dark themes

### Changed
- Replaced retired website documentation links with repository links
- Corrected JSON dependency version, module requirements, tool registration and provider capability claims
- Replaced the old Claude Desktop config example with HTTP client guidance and documented MCP limitations
- Clarified API key handling for distributed builds, cancellation, raw logging and platform verification
- Updated contribution setup and validation instructions

### Fixed
- WebGL transport/CORS failures are classified as network errors before Unity's placeholder HTTP status
- Ollama retains token usage, finish reasons and tool-result names; malformed/error stream payloads fail visibly
- Cancellation from a stream callback suppresses remaining buffered deltas and completion
- UI sample uses the current TextMeshPro wrapping API without obsolete API warnings
- Native Gemini call IDs and Anthropic streamed tools/signed content survive tool-history round trips
- UPM version lookup, provider reinitialization and static state with domain/scene reload disabled
- MCP version negotiation, sessions, 202 acknowledgements, request limits and cancellation/shutdown handling
- OpenAI completion limits, compatible endpoint normalization and optional streaming usage
- Gemini tool signature/history preservation and current Claude/Gemini model suggestions
- Gemini header authentication and central diagnostic credential redaction
- Local MCP requires a bearer token, validates origins and exposes only read-only tools by default
- Streaming timeouts, trailing usage, final-event flushing and task settlement in Editor and Play Mode
- Automatic streaming retry/fallback stops after partial output to avoid replaying content or tools
- Cache identity includes complete resolved requests and provider context; cached responses are isolated
- Provider fallback uses independent request snapshots and each backup's default model
- Streaming UTF-8 decoding preserves split accented characters, CJK and emoji

## [1.0.0] - 2026-02-06

### Added
- Unified chat completion API across all providers
- Real-time streaming with SSE and NDJSON support
- 9 providers: OpenAI, Anthropic Claude, Google Gemini, Mistral, Cohere, Ollama, LM Studio, llama.cpp, OpenAI-compatible
- ScriptableObject configuration with environment variable override for API keys
- Cross-platform HTTP via UnityWebRequest (Desktop, Mobile, WebGL)
- Retry logic with exponential backoff
- Custom editor inspector with password-masked API key fields
- Setup wizard (Window > UnAI > Setup Wizard)
- Runtime provider switching
- CancellationToken support for aborting requests
- **Hub window** â€” central entry point showing all modules, install status, and version (`Window > UnAI > Hub`)
- **UnaiVersion** â€” single source of truth for package version (reads `package.json`)
- CONTRIBUTING.md, CODE_OF_CONDUCT.md, and GitHub issue/PR templates
- Logo in README and Hub window
- GitHub badges (license, Unity version, release, stars, issues, PRs welcome)

### Fixed
- CS0618 warnings for obsolete `PlayerSettings.GetScriptingBackend` / `GetApiCompatibilityLevel` APIs (now using `NamedBuildTarget`)
- CS0414 warnings for unused fields in `UnaiAssistantWindow`
- Removed redundant "Create Global Config" menu item (use Hub â†’ Core instead)
