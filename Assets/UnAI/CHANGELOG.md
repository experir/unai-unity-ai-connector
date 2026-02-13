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
