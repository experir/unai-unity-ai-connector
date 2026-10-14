# Changelog

All notable changes to UNAI will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0-beta.1] - 2026-10-14

Beta candidate prepared on `main`; no release tag has been published. See [upgrade instructions](Docs/upgrading.md) and [validation coverage](Docs/validation.md) before adopting it.

### Added
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
