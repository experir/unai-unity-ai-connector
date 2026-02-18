# Changelog

All notable changes to UNAI will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- **Hub window** — central entry point showing all modules, install status, and version (`Window > UnAI > Hub`)
- **UnaiVersion** — single source of truth for package version (reads `package.json`)
- CONTRIBUTING.md, CODE_OF_CONDUCT.md, and GitHub issue/PR templates
- Logo in README and Hub window
- GitHub badges (license, Unity version, release, stars, issues, PRs welcome)

### Fixed
- CS0618 warnings for obsolete `PlayerSettings.GetScriptingBackend` / `GetApiCompatibilityLevel` APIs (now using `NamedBuildTarget`)
- CS0414 warnings for unused fields in `UnaiAssistantWindow`
- Removed redundant "Create Global Config" menu item (use Hub → Core instead)
