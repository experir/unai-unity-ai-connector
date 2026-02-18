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
2. **Clone** your fork into a Unity 6+ project's `Assets/` folder:
   ```
   cd YourProject/Assets
   git clone https://github.com/YOUR_USERNAME/unai-unity-ai-connector.git
   ```
3. Open the project in Unity and let it compile
4. Create a **feature branch**: `git checkout -b feat/my-feature`

## Project Structure

```
Scripts/
├── Runtime/        # Core — providers, streaming, unified API (ships in builds)
├── Agent/          # Agent — tool calling, memory, reasoning (ships in builds)
├── Editor/         # Editor utilities — setup wizard, Hub window, inspectors
├── EditorAssistant/# AI chat window with 32 Unity tools (editor only)
├── MCP/            # MCP server for external AI clients (editor only)
Examples/           # Demo scenes and sample scripts
```

Each folder is an independent assembly (`.asmdef`). Modules discover each other via **reflection** — no hard cross-references between optional modules.

## Code Guidelines

- **C# conventions** — follow existing code style (PascalCase for public members, `_camelCase` for private fields)
- **No external dependencies** — everything must work with just Unity + Newtonsoft JSON
- **Editor vs Runtime** — if it uses `UnityEditor`, it goes in an editor-only assembly
- **Zero compile errors on deletion** — any optional module must be deletable without breaking others
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
3. Add the provider to `UnaiProviderType` enum
4. Add config fields to `UnaiGlobalConfig`
5. Add a custom property drawer if needed
6. Update the README provider table
7. Add an example if possible

## Adding a New Editor Tool

1. Add your tool class in `Scripts/EditorAssistant/`
2. Implement `IUnaiTool` (extend `UnaiEditorTool`)
3. Register it in `UnaiAssistantToolsFactory.CreateEditorToolRegistry()`
4. The MCP server will automatically pick it up via reflection

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
