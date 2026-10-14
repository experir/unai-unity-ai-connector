# UNAI maintenance review

Reviewed on **2026-10-14** against local commit `41f2a0e` (2026-02-24), package version `1.0.0`. Evidence below records the initial source findings. Implementation notes describe the completed refresh. Validation now includes Unity 6000.3.23f1 regression tests, no-reload Play Mode, isolated module combinations, imported samples and player builds. Live cloud provider calls and third-party GUI MCP clients remain unverified.

[Back to README](../README.md)

## Fix first

### 1. Validate origins before exposing editor tools

**Implemented:** exact origin validation, loopback checks, generated bearer authentication, restricted CORS and default read-only tool exposure. Both MCP entry points expose explicit mutating-tool controls and token copy. The Windows hint now recommends an account-scoped URL reservation. Policy and real HTTP tests exercise authorization, rejection before dispatch and mutating-tool gating.

**Evidence:** [UnaiMcpServer.cs](../Scripts/MCP/UnaiMcpServer.cs), `HandleContext` and `SetCorsHeaders`, accepts requests without checking their origin, advertises wildcard CORS, and has no authentication check. The tool factory includes asset deletion, script modification and C# execution.

**Impact:** a local endpoint exposing these operations needs protection from browser-origin requests. The advertised [MCP 2025-03-26 transport specification](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports) requires origin validation and recommends authentication.

**Proposed fix:** reject untrusted origins before processing requests, restrict CORS, retain loopback binding, add local authentication and explicit controls for destructive tools. Replace the Windows startup hint recommending administrator access or `user=Everyone` with an account-scoped URL reservation.

**Verify:** an untrusted origin cannot initialize or call tools; an approved local client can. Test requests without Origin under an explicit local-client policy, preflight requests, and rejection before tool execution.

### 2. Decode streaming content as incremental UTF-8

**Implemented and verified:** a persistent strict UTF-8 decoder handles split characters and final flush. Three Unity EditMode regression tests pass.

**Evidence:** [UnaiStreamingDownloadHandler.cs](../Scripts/Runtime/Http/UnaiStreamingDownloadHandler.cs), `ReceiveData`, uses `char c = (char)data[i]`.

**Impact:** non-ASCII text such as `café`, CJK characters and emoji becomes corrupted; network chunks can split a single UTF-8 character.

**Proposed fix:** use a persistent UTF-8 `Decoder`, retain incomplete byte sequences across callbacks, and flush it at completion before dispatching remaining text.

**Verify:** SSE and NDJSON with accented text, emoji and CJK; split each multi-byte character at every possible byte boundary; also cover CRLF and the final line without a newline.

### 3. Keep provider model resolution separate from caller requests

**Implemented:** independent request snapshots, provider validation and duplicate-free fallback. Explicit model IDs apply to the primary provider; backups use their configured defaults. Regression coverage exercises both APIs and preserves caller data.

**Evidence:** [UnaiProviderBase.cs](../Scripts/Runtime/Core/UnaiProviderBase.cs), `ResolveModel`, writes the default into `request.Model`. [UnaiManager.cs](../Scripts/Runtime/Core/UnaiManager.cs) passes the same request through its fallback chain.

**Impact:** an OpenAI request without a model resolves to the OpenAI default, then an Anthropic fallback receives that same model string instead of its own default. Reusing the request also retains earlier model and stream mutations.

**Proposed fix:** create a resolved request for each provider without mutating the caller. Define a policy for explicitly specified models during cross-provider fallback rather than silently reusing an incompatible ID. Guard uninitialized, missing and disabled providers with actionable errors.

**Verify:** OpenAI-to-Anthropic fallback with no explicit model uses each provider's own default, preserves the original request, and handles explicit-model fallback according to the documented policy. Cover both streaming and non-streaming calls.

### 4. Hash the complete effective request for caching

**Implemented:** canonical structured keys include resolved models, messages, tools and provider context; lookups and insertion share an identity. Cached response snapshots are independent. Regression tests cover the delimiter collision, tool metadata, property order, implicit-model hits, endpoint changes and LRU eviction.

**Evidence:** [UnaiResponseCache.cs](../Scripts/Runtime/Utilities/UnaiResponseCache.cs), `BuildKey`, includes only provider ID, model, message role/content and options. It omits tool definitions, tool calls and tool result IDs/names. Its message serialization uses unescaped delimiters. The manager looks up the key before model resolution and stores it afterward.

**Impact:** different tool-bearing requests can share a cached result. A request with no model can miss the cache because its lookup and stored model fields differ. Different message lists can also produce the same delimiter string: one user message `a;User:b` collides with two user messages `a` and `b`.

**Proposed fix:** hash a deterministic structured representation of the full resolved request, including tools and message metadata, together with provider/endpoint identity. Use the same identity for lookup and insertion; invalidate on configuration changes and avoid returning a mutable shared response object.

**Verify:** changing a tool schema, tool arguments, tool result ID, endpoint or resolved model changes the key. The delimiter collision above disappears. Identical implicit-model requests produce cache hits.

### 5. Apply streaming timeouts and preserve final metadata

**Implemented and verified:** streaming timeouts, explicit error settlement, editor coroutine pumping, final-event flushing and merged usage metadata. Retries and fallback stop after visible output. Local HTTP fixtures cover timeout, cancellation, Unicode, trailing usage, malformed payloads, partial-output replay protection and callback exceptions.

**Evidence:** [UnaiHttpStreamClient.cs](../Scripts/Runtime/Http/UnaiHttpStreamClient.cs) does not take `TimeoutSeconds` or set `request.timeout`. [OpenAICompatibleBase.cs](../Scripts/Runtime/Providers/OpenAICompatibleBase.cs) marks a `finish_reason` chunk final, and [SseLineParser.cs](../Scripts/Runtime/Streaming/SseLineParser.cs) then ignores later chunks, including trailing usage.

**Impact:** a stream can wait indefinitely without caller cancellation; usage arriving after the finish chunk is discarded. Provider retry and manager fallback can also replay content after partial output, with no reset signal to consumers.

**Proposed fix:** propagate the configured timeout, finalize at transport completion while merging content/tool/usage state, surface parser failures, and define retry/fallback behavior after any visible output. Ensure callbacks or parser exceptions cannot leave the returned task pending.

**Verify:** a stalled stream reaches its deadline, cancellation settles the task, trailing usage survives, split tool arguments are assembled, malformed chunks are reported, and retry after partial content does not silently duplicate output or tool side effects.

### 6. Prevent provider keys from reaching diagnostics

**Implemented:** Gemini uses the `x-goog-api-key` header. Central diagnostics redact credential query parameters, JSON secret fields and registered provider secrets, including failure text. The inspector reports key presence without leaking a prefix. Tests use sentinel values.

**Evidence:** [GeminiProvider.cs](../Scripts/Runtime/Providers/Gemini/GeminiProvider.cs) puts the key in a URL query parameter. [UnaiLogger.cs](../Scripts/Runtime/Utilities/UnaiLogger.cs) writes that URL unchanged when raw JSON logging is enabled.

**Impact:** enabling raw diagnostics can write a cloud API key to `Library/UnAI` logs.

**Proposed fix:** redact credential-bearing query parameters centrally before logging, review header-based authentication supported by the provider, and keep raw logging opt-in. Avoid including keys in errors or exported diagnostics.

**Verify:** a sentinel API key never appears in request/response logs, including failure paths; useful endpoint information remains available.

## Complete the refresh

### 7. Test provider capabilities before updating model suggestions

**Implemented and verified:** separate OpenAI completion limits, compatible streaming-usage opt-in, capability controls, endpoint normalization, paginated account/server model discovery and an editor model picker. Gemini tool parts/signatures round-trip in non-streaming and streaming responses. New Claude/Gemini config defaults and suggestions were checked against official catalogs on 2026-10-14; existing config assets retain their models. All nine serializers and local model API fixtures pass. Live cloud calls remain unverified; a separate Responses adapter remains a future extension.

**Evidence:** model IDs and context sizes are hardcoded in provider `KnownModels` and `UnaiGlobalConfig`. [OpenAICompatibleBase.cs](../Scripts/Runtime/Providers/OpenAICompatibleBase.cs) sends `max_tokens` for every compatible service and unconditionally adds streaming usage options. Anthropic's JSON behavior is prompt-based; Cohere native tools are not implemented in its adapter.

**Proposed fix:** separate provider-specific parameter mappings from generic compatibility behavior, add capability flags, validate unsupported options, and refresh model suggestions/defaults against provider documentation and real fixtures. Add model discovery where available while keeping manual IDs. OpenAI's [Chat Completions reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) documents `max_tokens` as deprecated in favor of `max_completion_tokens`; keep generic server compatibility when adapting this.

**Verify:** request/response fixtures per adapter, including reasoning models, rejected options, tool calls and JSON output. Record the tested provider/model/date rather than presenting every suggested model as currently supported. Consider a separate OpenAI Responses adapter as an extension, with its own model and tool mapping.

### 8. Package examples as optional UPM samples

**Implemented:** examples now live in `Samples~`, with separate minimal chat and UI/agent samples declared in `package.json`. UI assembly references and dependencies are explicit.

**Evidence:** [package.json](../package.json) has an empty `samples` list, while [UnAI.Examples.asmdef](../Samples~/ChatAndAgents/UnAI.Examples.asmdef) references `UnAI.Agent` and `Unity.TextMeshPro`. Example scripts also use Unity UI. Removing Agent alone leaves an unresolved assembly reference.

**Proposed fix:** move examples into `Samples~`, declare them in `package.json`, document their UI dependencies, and separate chat-only from agent samples. Include a minimal runtime example that does not require a UI package. Verify package installation in a clean Unity project and resolve UI assembly references explicitly.

**Verify:** core-only installation compiles without optional examples; each imported sample compiles with its documented dependencies; removing optional modules and their dependents compiles.

### 9. Finish MCP transport interoperability

**Implemented and verified:** supported-version negotiation, session lifecycle, initialized notifications, 202 acknowledgements, JSON-only Streamable HTTP (GET 405), header/shape validation, bounded requests and session-scoped cancellation. Main-thread work is queued safely and cancelled on stop/reload. Two HTTP client implementations exercise the live Unity endpoint; GUI clients remain unverified.

**Evidence:** [UnaiMcpProtocol.cs](../Scripts/MCP/UnaiMcpProtocol.cs) fixes the protocol version to `2025-03-26`. The server returns 204 for accepted notifications, instead of that revision's required 202, and its GET SSE stream sends an endpoint payload without completing an interoperability test.

**Proposed fix:** choose supported protocol revisions explicitly, negotiate initialization, align notification status codes and SSE behavior, validate requests/headers, and settle pending main-thread work when the server stops or the editor reloads. Add bounded request sizes and request deadlines.

**Verify:** transport conformance tests plus real initialize/list/call/cancel sessions with at least two HTTP clients. Document their exact versions. Keep client setup instructions separate from a claim of tested server support.

### 10. Add Unity checks and reliable package lifecycle handling

**Implemented and verified:** package metadata uses Unity package APIs, built-in provider configurations can be rebound, and static registry/logging/singleton state resets at the runtime subsystem boundary. A real Play Mode test with both domain and scene reload disabled verifies manager restoration and cache reset. Version and reinitialization tests pass. Reusable validation projects and CI checks support the package, samples and module combinations. A cloud Unity license is required to enable the Unity CI jobs.

**Evidence:** there are no checked-in tests or CI workflows. [UnaiVersion.cs](../Scripts/Runtime/UnaiVersion.cs) finds `package.json` under a fixed `Assets/unai-unity-ai-connector` path, despite the recommended UPM installation. [UnaiProviderRegistry.cs](../Scripts/Runtime/Core/UnaiProviderRegistry.cs) ignores later configurations after `_initialized` becomes true. Static registry state also needs testing with domain reload disabled.

**Proposed fix:** resolve package metadata via Unity's package APIs, keep the runtime constant in sync through an actual release check, define reinitialization/reset semantics, and add Unity EditMode coverage for parsers, serialization, cache, fallback and module compilation. Add a clean install and player build check before releasing.

**Verify:** Git URL and embedded package installs report the same version, a second configuration takes effect according to the defined policy, and entering Play Mode with domain reload disabled does not retain unintended state. Compile on the minimum supported Unity version and a current Unity 6 release.

## Documentation changes applied in this refresh

- Removed the retired product-site documentation links and replaced them with repository guides.
- Added a rounded dark SVG logo container, retaining the existing logo artwork.
- Removed dated model lists from the README; documented manual model selection and actual adapter capabilities.
- Corrected the Newtonsoft JSON version, optional-module requirements, tool-registration claims, JSON-validation claims and client setup guidance.
- Added key handling, main-thread usage, cancellation, diagnostics and platform caveats tied to the implementation.
- Updated contribution instructions and added an Unreleased changelog section.

## Validation and release follow-up

See [Refresh validation](validation.md) for the test/build matrix and explicit limits of the checks.

The refresh has been implemented in focused commits. The package version remains `1.0.0`; no release tag has been published. Regression fixtures verify the adapters without spending cloud-provider credits. Before a tagged release, run the enabled Unity CI matrix on the minimum supported editor, verify live provider accounts and named GUI MCP clients, and publish the tested compatibility matrix.

Unity CI needs repository license configuration and `UNITY_CI_ENABLED=true`; metadata checks run without a license. The README logo asset lives under `Docs/assets~` so Unity does not import documentation artwork or require an SVG importer. A future website can reuse the versioned guides when pages are available.
