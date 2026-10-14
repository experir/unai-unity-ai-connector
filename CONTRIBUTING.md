# Contributing to UNAI

Thank you for your interest in contributing to UNAI! Every contribution helps make AI in Unity better for everyone.

## Ways to Contribute

- **Bug reports** — found something broken? [Open an issue](https://github.com/experir/unai-unity-ai-connector/issues/new)
- **Feature requests** — have an idea? Open an issue and describe the use case
- **Pull requests** — code fixes, new providers, new tools, docs improvements
- **Documentation** — typos, unclear instructions, missing examples
- **Examples** — share how you use UNAI in your project

## Getting Started

1. **Fork** the repository
2. **Clone** your fork as an embedded package in a Unity 6+ project's `Packages/` folder:
   ```
   cd YourProject/Packages
   git clone https://github.com/YOUR_USERNAME/unai-unity-ai-connector.git com.unai.universal-ai-connector
   ```
3. Open the project in Unity and let Package Manager install Newtonsoft JSON 3.2.2. Optional UI samples require Agent, Unity UI and TextMeshPro; see [installation notes](README.md#installation).
4. Create a **feature branch**: `git checkout -b feat/my-feature`

## Project Structure

```
Scripts/
├── Runtime/        # Core — providers, streaming, unified API (ships in builds)
├── Agent/          # Agent — tool calling, memory, reasoning (ships in builds)
├── Editor/         # Editor utilities — setup wizard, Hub window, inspectors
├── EditorAssistant/# AI chat window with 32 Unity tools (editor only)
├── MCP/            # MCP server for external AI clients (editor only)
Samples~/           # Optional UPM samples, imported through Package Manager
Docs/               # Versioned provider, MCP, troubleshooting and maintenance guides
```

The script modules have separate assemblies (`.asmdef`). Agent references Runtime; Editor Assistant and MCP both reference Agent and Runtime. Assistant and MCP discover each other through reflection. The optional UI sample references Agent, TextMeshPro and Unity UI. Follow the [module dependency table](README.md#modular-structure) when removing modules.

## Code Guidelines

- **C# conventions** — follow existing code style (PascalCase for public members, `_camelCase` for private fields)
- **No external dependencies** — everything must work with just Unity + Newtonsoft JSON
- **Editor vs Runtime** — if it uses `UnityEditor`, it goes in an editor-only assembly
- **Module dependencies** — avoid hard references between Assistant and MCP; verify removal together with dependent assemblies, including imported samples
- **XML docs** — add `<summary>` comments to public APIs
- **No `Debug.Log` spam** — use `[UNAI]` prefix for log messages, keep them minimal

## Pull Request Process

1. **One PR per feature/fix** — keep changes focused
2. **Test in Unity** — make sure it compiles and runs without errors or warnings
3. **Update CHANGELOG.md** — add your changes under `[Unreleased]`
4. **Describe what and why** — explain the change in the PR description
5. **Reference issues** — link related issues with `Fixes #123` or `Closes #123`

## Adding a New Provider

1. Create a new class in `Scripts/Runtime/Providers/YourProvider/`
2. Implement `IUnaiProvider`
3. Give it a unique `ProviderId`; there is no provider-type enum in the current implementation
4. Add its config to `UnaiGlobalConfig`, `AllProviders()`, and `UnaiProviderRegistry.InitializeBuiltInProviders()`, or explicitly initialize and register a custom provider from code
5. Update setup/editor configuration UI if needed
6. Update the README provider table and [provider capabilities guide](Docs/providers.md)
7. Verify request serialization, responses, streaming, cancellation and errors, including tools and structured output where implemented

## Adding a New Editor Tool

1. Add your tool class in `Scripts/EditorAssistant/`
2. Implement `IUnaiTool` (extend `UnaiEditorTool`)
3. Register it in `UnaiAssistantToolsFactory.CreateEditorToolRegistry()`
4. The default MCP window loads that shared factory through reflection; implementing the interface alone does not register the tool

## Validation and release notes

For runtime changes, verify compilation in Unity and add targeted regression coverage for changed behavior. Test non-streaming and streaming separately. Use sanitized fixtures for automated checks; never commit API keys or private request logs. Record live provider/model and MCP client versions when claiming compatibility.

For documentation changes, check local links and code snippets against the source. Keep the Newtonsoft JSON version consistent with `package.json`, document actual module dependencies, and preview the README logo on light and dark backgrounds. Documentation is maintained in this repository.

Before a release, verify a clean UPM installation, optional-module removal with dependents, sample dependencies, package/version agreement, and the supported Unity build targets. See the [maintenance review](Docs/maintenance-review.md) for known gaps and prioritized work.

## Reporting Bugs

Please include:
- Unity version
- OS (Windows/Mac/Linux)
- Provider being used
- Steps to reproduce
- Console error output (full stack trace)

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold this code.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
