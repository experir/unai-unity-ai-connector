using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Agent;
using UnAI.Core;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.Agent.Http
{
    public class UnaiAgentHttpServer : MonoBehaviour
    {
        private const int DefaultPort = 17997;
        private const string DefaultApiKey = "unai-default-key";

        public int Port = DefaultPort;
        public string ApiKey = DefaultApiKey;

        private bool _isRunning;
        public bool IsRunning => _isRunning;
        public string Url => $"http://0.0.0.0:{Port}";

        private string _cachedVersion;
        private SynchronizationContext _unityContext;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

        private readonly ConcurrentQueue<string> _logQueue = new();

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _cachedVersion = UnaiVersion.Get();
            _unityContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        private void Start()
        {
            StartServer(Port, ApiKey);
        }

        private void Update()
        {
            while (_logQueue.TryDequeue(out var msg))
            {
                if (msg.StartsWith("[ERR]"))
                    Debug.LogError(msg);
                else if (msg.StartsWith("[WARN]"))
                    Debug.LogWarning(msg);
                else
                    Debug.Log(msg);
            }
        }

        private void Log(string msg) => _logQueue.Enqueue(msg);
        private void LogError(string msg) => _logQueue.Enqueue($"[ERR] {msg}");
        private void LogWarn(string msg) => _logQueue.Enqueue($"[WARN] {msg}");

        private void OnDestroy()
        {
            Stop();
        }

        public void StartServer(int port, string apiKey)
        {
            if (_isRunning)
            {
                LogWarn("[UNAI Agent HTTP] Server is already running.");
                return;
            }

            Port = port;
            ApiKey = apiKey;
            _cts = new CancellationTokenSource();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                _isRunning = true;

                Log($"[UNAI Agent HTTP] Server started on http://0.0.0.0:{Port}");
                Log($"[UNAI Agent HTTP] API Key: {(string.IsNullOrEmpty(ApiKey) ? "(none)" : "set")}");

                Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                LogError($"[UNAI Agent HTTP] Failed to start server: {ex.Message}");
                if (ex is HttpListenerException)
                {
                    LogError("[UNAI Agent HTTP] Hint: On Windows, you may need to run as Administrator, " +
                             "or grant permission with: netsh http add urlacl url=http://+:" + port + "/ user=Everyone");
                }
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _cts?.Cancel();

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }

            _listener = null;
            _isRunning = false;
            _sessions.Clear();
            Log("[UNAI Agent HTTP] Server stopped.");
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = HandleContext(context, ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        LogError($"[UNAI Agent HTTP] Listener error: {ex.Message}");
                }
            }
        }

        private async Task HandleContext(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.HttpMethod == "OPTIONS")
            {
                SetCorsHeaders(response);
                response.StatusCode = 204;
                response.Close();
                return;
            }

            SetCorsHeaders(response);
            string path = request.Url.AbsolutePath.TrimEnd('/');

            try
            {
                if (path == "/health" && request.HttpMethod == "GET")
                {
                    await HandleHealth(response);
                }
                else if (path == "/agent/chat" && request.HttpMethod == "POST")
                {
                    await HandleChat(request, response, ct);
                }
                else if (path == "/agent/run" && request.HttpMethod == "POST")
                {
                    await HandleRun(request, response, ct);
                }
                else if (path == "/agent/reset" && request.HttpMethod == "POST")
                {
                    await HandleReset(request, response);
                }
                else
                {
                    response.StatusCode = 404;
                    WriteJson(response, new JObject { ["error"] = "Not found" });
                }
            }
            catch (Exception ex)
            {
                LogError($"[UNAI Agent HTTP] Request error: {ex.Message}");
                response.StatusCode = 500;
                WriteJson(response, new JObject { ["error"] = ex.Message });
            }
        }

        private async Task HandleHealth(HttpListenerResponse response)
        {
            response.StatusCode = 200;
            response.ContentType = "application/json";
            WriteJson(response, new JObject
            {
                ["status"] = "ok",
                ["server"] = "UnAI Agent HTTP",
                ["version"] = _cachedVersion ?? "unknown",
                ["sessions"] = _sessions.Count
            });
        }

        private async Task HandleChat(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
        {
            var apiRequest = await ParseRequest<AgentApiRequest>(request);
            if (apiRequest == null)
            {
                WriteJson(response, new JObject { ["error"] = "Invalid request body" });
                return;
            }

            if (!ValidateAuth(apiRequest.ApiKey))
            {
                WriteJson(response, new JObject { ["error"] = "Unauthorized" }, 401);
                return;
            }

            var session = GetOrCreateSession(apiRequest.SessionId);
            var config = BuildConfig(apiRequest);

            UnaiAgentStep result = null;
            Exception agentError = null;

            var tcs = new TaskCompletionSource<bool>();
            _unityContext.Post(async _ =>
            {
                try
                {
                    var agent = new UnaiAgent(session.Provider, config, session.Tools);
                    result = await agent.RunAsync(apiRequest.Message, ct);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    agentError = ex;
                    tcs.TrySetResult(false);
                }
            }, null);

            await tcs.Task;

            if (agentError != null)
            {
                response.StatusCode = 500;
                WriteJson(response, new JObject { ["error"] = agentError.Message });
                return;
            }

            response.StatusCode = 200;
            response.ContentType = "application/json";
            WriteJson(response, new JObject
            {
                ["sessionId"] = session.Id,
                ["content"] = result?.Response?.Content ?? "",
                ["stopReason"] = result?.StopReason
            });
        }

        private async Task HandleRun(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
        {
            var apiRequest = await ParseRequest<AgentApiRequest>(request);
            if (apiRequest == null)
            {
                WriteJson(response, new JObject { ["error"] = "Invalid request body" });
                return;
            }

            if (!ValidateAuth(apiRequest.ApiKey))
            {
                WriteJson(response, new JObject { ["error"] = "Unauthorized" }, 401);
                return;
            }

            var session = GetOrCreateSession(apiRequest.SessionId);

            if (apiRequest.McpTools != null && apiRequest.McpTools.Count > 0)
            {
                session.Tools = ConvertMcpTools(apiRequest.McpTools);
            }

            var config = BuildConfig(apiRequest);

            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            var output = response.OutputStream;
            var encoder = new UTF8Encoding();

            session.SetStream(output, encoder);

            var tcs = new TaskCompletionSource<bool>();
            _unityContext.Post(async _ =>
            {
                try
                {
                    var agent = new UnaiAgent(session.Provider, config, session.Tools);
                    session.Agent = agent;
                    session.ConfigureEvents(
                        onThinking: e => session.SendEvent(AgentSseEvents.Thinking, e),
                        onToolCall: e => session.SendEvent(AgentSseEvents.ToolCall, e),
                        onToolResult: e => session.SendEvent(AgentSseEvents.ToolResult, e),
                        onDelta: e => session.SendEvent(AgentSseEvents.Delta, e),
                        onComplete: e => session.SendEvent(AgentSseEvents.Complete, e)
                    );

                    await agent.RunAsync(apiRequest.Message, ct);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    session.SendEvent(AgentSseEvents.Error, new { message = ex.Message });
                    tcs.TrySetResult(false);
                }
                finally
                {
                    session.SetStream(null, null);
                }
            }, null);

            await tcs.Task;
        }

        private async Task HandleReset(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = await ReadBody(request);
            var json = JObject.Parse(body);
            var sessionId = json["sessionId"]?.ToString();

            if (!string.IsNullOrEmpty(sessionId) && _sessions.TryRemove(sessionId, out var session))
            {
                session.Dispose();
            }

            response.StatusCode = 200;
            WriteJson(response, new JObject { ["status"] = "ok" });
        }

        private bool ValidateAuth(string apiKey)
        {
            if (string.IsNullOrEmpty(ApiKey)) return true;
            return !string.IsNullOrEmpty(apiKey) && apiKey == ApiKey;
        }

        private AgentSession GetOrCreateSession(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId, out var existing))
                return existing;

            var newSession = new AgentSession
            {
                Id = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
                Provider = UnaiManager.Instance?.ActiveProvider
            };

            _sessions[newSession.Id] = newSession;
            return newSession;
        }

        private UnaiAgentConfig BuildConfig(AgentApiRequest request)
        {
            return new UnaiAgentConfig
            {
                MaxSteps = request.Config?.MaxSteps ?? 10,
                TimeoutSeconds = request.Config?.TimeoutSeconds ?? 300,
                SystemPrompt = request.Config?.SystemPrompt,
                Model = request.Config?.Model,
                UseStreaming = true
            };
        }

        private UnaiToolRegistry ConvertMcpTools(System.Collections.Generic.List<McpToolDefinition> mcpTools)
        {
            var registry = new UnaiToolRegistry();

            foreach (var mcp in mcpTools)
            {
                var def = mcp.ToUnaiToolDefinition();
                var tool = new DynamicTool(def);
                registry.Register(tool);
            }

            return registry;
        }

        private async Task<T> ParseRequest<T>(HttpListenerRequest request) where T : class
        {
            var body = await ReadBody(request);
            if (string.IsNullOrEmpty(body)) return null;
            return JsonConvert.DeserializeObject<T>(body);
        }

        private async Task<string> ReadBody(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            return await reader.ReadToEndAsync();
        }

        private void SetCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Api-Key");
        }

        private void WriteJson(HttpListenerResponse response, JObject obj, int statusCode = 200)
        {
            var bytes = Encoding.UTF8.GetBytes(obj.ToString(Formatting.None));
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Flush();
            response.Close();
        }
    }

    internal class AgentSession : IDisposable
    {
        public string Id { get; set; }
        public IUnaiProvider Provider { get; set; }
        public UnaiToolRegistry Tools { get; set; } = new();
        public UnaiAgent Agent { get; set; }

        private Stream _stream;
        private Encoding _encoder;
        private readonly TaskCompletionSource<bool> _completeTcs = new();
        private bool _disposed;

        public void SetStream(Stream stream, Encoding encoder)
        {
            _stream = stream;
            _encoder = encoder;
        }

        public void ConfigureEvents(
            Action<UnAI.Agent.SseThinkingEvent> onThinking,
            Action<UnAI.Agent.SseToolCallEvent> onToolCall,
            Action<UnAI.Agent.SseToolResultEvent> onToolResult,
            Action<UnAI.Agent.SseDeltaEvent> onDelta,
            Action<UnAI.Agent.SseCompleteEvent> onComplete)
        {
            if (Agent == null) return;

            Agent.OnThinking += args => onThinking?.Invoke(new UnAI.Agent.SseThinkingEvent
            {
                Step = args.StepNumber,
                MessageCount = args.MessageCount,
                EstimatedTokens = args.EstimatedTokens
            });

            Agent.OnToolCall += args => onToolCall?.Invoke(new UnAI.Agent.SseToolCallEvent
            {
                Step = args.StepNumber,
                Tool = args.ToolCall?.ToolName,
                Args = args.ToolCall?.GetArguments()
            });

            Agent.OnToolResult += args => onToolResult?.Invoke(new UnAI.Agent.SseToolResultEvent
            {
                Step = args.StepNumber,
                Tool = args.Result?.ToolName,
                Result = args.Result?.Content,
                IsError = args.Result?.IsError ?? false
            });

            Agent.OnStreamDelta += args => onDelta?.Invoke(new UnAI.Agent.SseDeltaEvent
            {
                Content = args.Delta?.Content,
                Step = args.StepNumber
            });

            Agent.OnStepComplete += args => onComplete?.Invoke(new UnAI.Agent.SseCompleteEvent
            {
                Content = args.Step?.Response?.Content,
                StopReason = args.Step?.StopReason
            });
        }

        public async Task WaitForComplete()
        {
            await _completeTcs.Task;
        }

        public void NotifyComplete()
        {
            _completeTcs.TrySetResult(true);
        }

        public void SendEvent(string eventType, object data)
        {
            if (_stream == null || _disposed) return;

            try
            {
                var json = JsonConvert.SerializeObject(data);
                var message = $"event: {eventType}\ndata: {json}\n\n";
                var bytes = _encoder.GetBytes(message);
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NotifyComplete();
            _stream?.Close();
        }
    }

    internal class DynamicTool : IUnaiTool
    {
        public UnaiToolDefinition Definition { get; }

        public DynamicTool(UnaiToolDefinition definition)
        {
            Definition = definition;
        }

        public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct)
        {
            return Task.FromResult(new UnaiToolResult
            {
                ToolCallId = call.Id,
                ToolName = call.ToolName,
                Content = $"Tool '{call.ToolName}' executed. Args: {call.ArgumentsJson}",
                IsError = false
            });
        }
    }
}
