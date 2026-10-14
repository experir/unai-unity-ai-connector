# Troubleshooting and platform notes

[Back to README](../README.md)

## Before your first request

Use Unity 6 (`6000.0+`), allow Package Manager to install `com.unity.nuget.newtonsoft-json` **3.2.2**, create a config, enable your selected provider, and assign the config to a scene's `UnaiManager`. A manager created from code needs `Initialize(config)` before use.

The HTTP layer starts Unity coroutines. Invoke requests from Unity's main thread, such as a component event or editor UI callback.

| Symptom | What to check |
|---|---|
| Missing `Newtonsoft.Json` | Install `com.unity.nuget.newtonsoft-json` 3.2.2 through Package Manager; match `package.json` |
| Missing TextMeshPro/UI or `UnAI.Agent` in examples | The optional UI sample requires `com.unity.ugui` 2.0.0 and Agent; install its dependencies or use Minimal Runtime Chat |
| `UnaiManager.Instance` is null | Add a manager to the loaded scene; wait for its `Awake` before calling it |
| Provider is null or cannot send requests | Initialize the registry, enable the provider, check its ID and base URL, and assign the config |
| 401 / 403 | Check the resolved key and account access; environment variables take precedence over the asset field |
| Invalid model / 400 | Enter a model ID available to your account and start with no optional request settings |
| 404 on an OpenAI-compatible server | The adapter appends `/v1/chat/completions`; avoid duplicating `/v1` in `BaseUrl` |
| 429 | Check provider limits; retryable requests use backoff and optional configured fallback |
| Garbled accents or emoji while streaming | The streaming handler decodes UTF-8 incrementally; inspect server encoding and report a sanitized reproduction if text is still corrupted |
| Streaming never finishes | Streaming currently ignores `TimeoutSeconds`; pass a cancellation token with a deadline |
| JSON response does not match your schema | Enforcement differs by adapter; validate the result in your application |

## Cancellation and error handling

Non-streaming requests throw `UnaiRequestException` for provider HTTP failures. Streaming reports provider failures through `onError`; cancellation can throw `OperationCanceledException` when awaiting either API. Catch cancellation separately from provider errors.

Inside an async method, for example:

```csharp
using var cts = new System.Threading.CancellationTokenSource(
    System.TimeSpan.FromSeconds(30));

try
{
    await UnAI.Core.UnaiManager.Instance.ChatStreamAsync(
        request,
        onDelta: delta => UnityEngine.Debug.Log(delta.AccumulatedContent),
        onError: error => UnityEngine.Debug.LogError(error.Message),
        ct: cts.Token);
}
catch (System.OperationCanceledException)
{
    UnityEngine.Debug.Log("Request cancelled or deadline reached.");
}
```

Create a separate request for each operation. Providers currently mutate `Model` and `Stream`; do not share one request between simultaneous calls.

## Platform expectations

The runtime uses `UnityWebRequest`; this describes the implementation, not a verified platform test matrix.

| Target | Notes |
|---|---|
| Windows, macOS, Linux | Runtime chat and agents; MCP and editor assistant operate in the Unity Editor |
| Android, iOS | Check device networking, TLS and local-server reachability; `localhost` is the device |
| WebGL | Browser CORS and HTTPS rules apply; confirm streaming behavior in the target browser; no editor MCP server in a player build |

Use a backend for shared cloud-provider secrets in distributed builds. On WebGL, the endpoint must allow the page's origin and support the intended browser request behavior. On mobile or WebGL, do not assume desktop environment variables will be present.

## Diagnostics

Verbose logging is controlled by `DebugLogging`. Raw JSON logging is a separate opt-in setting (`UnaiLogger.RawJsonLoggingEnabled`) and writes to `Library/UnAI` relative to the project.

Raw logs contain prompts and responses. The current Gemini adapter puts its API key in the request URL, which raw logging records. Keep raw logging disabled unless needed, and redact keys, private prompts, personal data and tool arguments before sharing logs.

For bug reports, include Unity version, OS or build target, provider and model ID, streaming/non-streaming mode, reproduction steps, and sanitized errors. For MCP, include the client version and transport. See [CONTRIBUTING.md](../CONTRIBUTING.md).
