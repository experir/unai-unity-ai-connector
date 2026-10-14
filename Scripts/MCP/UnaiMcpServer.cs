using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Tools;
using UnAI.Utilities;
using UnityEditor;
using UnityEngine;

namespace UnAI.MCP
{
    public class UnaiMcpServer
    {
        private sealed class Session
        {
            public string Version;
            public volatile bool Initialized;
            public DateTime LastUsed = DateTime.UtcNow;
            public readonly ConcurrentDictionary<string, CancellationTokenSource> Calls = new();
            public void Cancel()
            {
                foreach (var call in Calls.Values)
                    try { call.Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        private sealed class Pending
        {
            public string Body;
            public Session Session;
            public CancellationToken Token;
            public readonly TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public int Port { get; private set; }
        public bool IsRunning { get; private set; }
        public int ConnectedClients => _sessions.Count;
        public string Url => $"http://localhost:{Port}/mcp";
        public string AccessToken { get; } = UnaiMcpAccessPolicy.CreateToken();
        public bool AllowMutatingTools { get; set; }
        public int MaxRequestBytes { get; set; } = 1024 * 1024;
        public int RequestTimeoutSeconds { get; set; } = 30;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private UnaiToolRegistry _tools;
        private readonly ConcurrentQueue<Pending> _pending = new();
        private readonly ConcurrentDictionary<string, Session> _sessions = new();
        private int _activeRequests;

        public void Start(int port, UnaiToolRegistry tools)
        {
            if (IsRunning) return;
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            if (MaxRequestBytes < 1 || RequestTimeoutSeconds < 1) throw new InvalidOperationException("MCP request limits must be positive.");
            Port = port;
            _tools = tools;
            _cts = new CancellationTokenSource();
            UnaiLogger.RegisterSecret(AccessToken);
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                IsRunning = true;
                EditorApplication.update += Pump;
                AssemblyReloadEvents.beforeAssemblyReload += Stop;
                EditorApplication.quitting += Stop;
                Debug.Log($"[UNAI MCP] Server started on {Url}; {_tools.GetAllDefinitions().Count(d => AllowMutatingTools || d.IsReadOnly)} tools exposed.");
                var listener = _listener;
                var token = _cts.Token;
                _ = Task.Run(() => ListenLoop(listener, token));
            }
            catch (Exception ex)
            {
                Stop();
                Debug.LogError($"[UNAI MCP] Failed to start server: {ex.Message}");
                if (ex is HttpListenerException)
                    Debug.LogError($"[UNAI MCP] If Windows requires an HTTP URL reservation, reserve only http://localhost:{port}/ for your Windows account. See Docs/mcp.md.");
            }
        }

        public void Stop()
        {
            IsRunning = false;
            EditorApplication.update -= Pump;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.quitting -= Stop;
            _cts?.Cancel();
            foreach (var session in _sessions.Values) session.Cancel();
            _sessions.Clear();
            while (_pending.TryDequeue(out var pending)) pending.Result.TrySetCanceled();
            try { _listener?.Close(); } catch (ObjectDisposedException) { }
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }

        private void Pump()
        {
            foreach (var entry in _sessions)
                if (DateTime.UtcNow - entry.Value.LastUsed > TimeSpan.FromMinutes(30) && _sessions.TryRemove(entry.Key, out var expired))
                    expired.Cancel();
            while (_pending.TryDequeue(out var pending)) Execute(pending);
        }

        private async void Execute(Pending pending)
        {
            try
            {
                pending.Token.ThrowIfCancellationRequested();
                var result = await UnaiMcpProtocol.HandleRequest(pending.Body, _tools, pending.Token, AllowMutatingTools,
                    (method, parameters) =>
                    {
                        if (method == "notifications/initialized") pending.Session.Initialized = true;
                        if (method == "notifications/cancelled")
                        {
                            string key = parameters["requestId"]?.ToString(Formatting.None);
                            if (key != null && pending.Session.Calls.TryGetValue(key, out var call))
                                try { call.Cancel(); } catch (ObjectDisposedException) { }
                        }
                    });
                pending.Result.TrySetResult(result);
            }
            catch (OperationCanceledException) { pending.Result.TrySetCanceled(); }
            catch (Exception ex) { pending.Result.TrySetException(ex); }
        }

        private async Task ListenLoop(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { var context = await listener.GetContextAsync(); _ = HandleContext(context, ct); }
                catch (Exception ex) when (ct.IsCancellationRequested || ex is ObjectDisposedException) { break; }
                catch (Exception ex) { Debug.LogWarning($"[UNAI MCP] Listener failed: {ex.Message}"); break; }
            }
        }

        private async Task HandleContext(HttpListenerContext context, CancellationToken serverToken)
        {
            var request = context.Request;
            var response = context.Response;
            int active = Interlocked.Increment(ref _activeRequests);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                var ct = deadline.Token;
                if (active > 32) { Error(response, 429, "Too many active requests."); return; }
                if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address)
                    || !UnaiMcpAccessPolicy.IsAllowedOrigin(request.Headers["Origin"], $"http://localhost:{Port}"))
                { Error(response, 403, "Local client or origin not allowed."); return; }
                if (request.Headers["Origin"] is string origin)
                {
                    response.Headers["Access-Control-Allow-Origin"] = origin;
                    response.Headers["Vary"] = "Origin";
                }
                if (request.Url.AbsolutePath.TrimEnd('/') != "/mcp") { Error(response, 404, "Use /mcp."); return; }
                if (request.HttpMethod == "OPTIONS")
                {
                    response.Headers["Access-Control-Allow-Methods"] = "POST, DELETE, OPTIONS";
                    response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, MCP-Session-Id, MCP-Protocol-Version";
                    response.StatusCode = 204; return;
                }
                if (!UnaiMcpAccessPolicy.IsAuthorized(request.Headers["Authorization"], AccessToken))
                { response.Headers["WWW-Authenticate"] = "Bearer"; Error(response, 401, "A valid local bearer token is required."); return; }
                string version = request.Headers["MCP-Protocol-Version"];
                if (version != null && !UnaiMcpProtocol.SupportsVersion(version))
                { Error(response, 400, "Unsupported MCP-Protocol-Version."); return; }
                // This server uses JSON responses. Streamable HTTP permits GET 405 when no SSE stream is offered.
                if (request.HttpMethod == "GET")
                { response.Headers["Allow"] = "POST, DELETE, OPTIONS"; response.StatusCode = 405; return; }
                string sessionId = request.Headers["MCP-Session-Id"];
                if (request.HttpMethod == "DELETE")
                {
                    if (sessionId == null) { Error(response, 400, "MCP-Session-Id is required."); return; }
                    if (!_sessions.TryRemove(sessionId, out var removed)) { Error(response, 404, "Unknown session."); return; }
                    removed.Cancel(); response.StatusCode = 204; return;
                }
                if (request.HttpMethod != "POST")
                { response.Headers["Allow"] = "POST, DELETE, OPTIONS"; response.StatusCode = 405; return; }
                if (!string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                { Error(response, 415, "Content-Type must be application/json."); return; }
                var accept = (request.AcceptTypes ?? Array.Empty<string>()).Select(v => v.Split(';')[0].Trim()).ToArray();
                if (!accept.Contains("application/json") || !accept.Contains("text/event-stream"))
                { Error(response, 406, "Accept must include application/json and text/event-stream."); return; }
                if (request.ContentLength64 > MaxRequestBytes) { Error(response, 413, "Request body is too large."); return; }
                string body;
                try { body = await ReadBody(request, ct); }
                catch (InvalidDataException) { Error(response, 413, "Request body is too large."); return; }
                JObject message;
                try { message = JObject.Parse(body); }
                catch (JsonException) { response.StatusCode = 400; Write(response, UnaiMcpProtocol.MakeError(null, -32700, "Invalid JSON-RPC object.")); return; }
                string method = message["method"]?.ToString();
                bool initialize = method == "initialize";
                Session session;
                if (initialize)
                {
                    sessionId = Guid.NewGuid().ToString("N");
                    session = new Session { Version = UnaiMcpProtocol.SelectVersion((message["params"] as JObject)?["protocolVersion"]?.ToString()) };
                    lock (_sessions)
                    {
                        if (_sessions.Count >= 64) { Error(response, 429, "Too many sessions."); return; }
                        if (!_sessions.TryAdd(sessionId, session)) { Error(response, 503, "Could not initialize session."); return; }
                    }
                }
                else
                {
                    if (sessionId == null) { Error(response, 400, "MCP-Session-Id is required after initialize."); return; }
                    if (!_sessions.TryGetValue(sessionId, out session)) { Error(response, 404, "Unknown or expired session."); return; }
                    if (version != null && version != session.Version) { Error(response, 400, "Protocol version differs from the negotiated session."); return; }
                    if (method is "tools/list" or "tools/call" && !session.Initialized)
                    { response.StatusCode = 400; Write(response, UnaiMcpProtocol.MakeError(message["id"], -32000, "Send notifications/initialized before using tools.")); return; }
                }
                session.LastUsed = DateTime.UtcNow;
                string callId = message.Property("id") != null && !initialize ? message["id"].ToString(Formatting.None) : null;
                if (callId != null && !session.Calls.TryAdd(callId, deadline)) { Error(response, 409, "A request with this id is already active in the session."); return; }
                var pending = new Pending { Body = body, Session = session, Token = ct };
                try
                {
                    using var registration = ct.Register(() => pending.Result.TrySetCanceled());
                    _pending.Enqueue(pending);
                    string result = await pending.Result.Task;
                    if (initialize)
                    {
                        if (result == null || JObject.Parse(result)["error"] != null)
                            _sessions.TryRemove(sessionId, out _);
                        else response.Headers["MCP-Session-Id"] = sessionId;
                    }
                    response.Headers["MCP-Protocol-Version"] = session.Version;
                    response.StatusCode = result == null ? 202 : 200;
                    if (result != null) Write(response, result);
                }
                finally
                {
                    if (callId != null) session.Calls.TryRemove(callId, out _);
                    if (initialize && (pending.Result.Task.IsCanceled || pending.Result.Task.IsFaulted))
                        _sessions.TryRemove(sessionId, out _);
                }
            }
            catch (OperationCanceledException) { Error(response, serverToken.IsCancellationRequested ? 503 : 408, "Request cancelled or deadline reached."); }
            catch (DecoderFallbackException) { Error(response, 400, "Request body must be UTF-8."); }
            catch (Exception ex) { Debug.LogWarning($"[UNAI MCP] Request failed: {ex.GetType().Name}"); Error(response, 500, "Could not process request."); }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
                try { response.Close(); } catch (Exception) { }
            }
        }

        private async Task<string> ReadBody(HttpListenerRequest request, CancellationToken ct)
        {
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            using var registration = ct.Register(() => { try { request.InputStream.Close(); } catch (Exception) { } });
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int count;
                try { count = await request.InputStream.ReadAsync(buffer, 0, buffer.Length, ct); }
                catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
                if (count == 0) break;
                if (body.Length + count > MaxRequestBytes) throw new InvalidDataException();
                body.Write(buffer, 0, count);
            }
            return new UTF8Encoding(false, true).GetString(body.ToArray());
        }

        private static void Error(HttpListenerResponse response, int status, string message)
        {
            try { response.StatusCode = status; Write(response, new JObject { ["error"] = message }.ToString(Formatting.None)); }
            catch (Exception) { }
        }

        private static void Write(HttpListenerResponse response, string body)
        {
            response.ContentType = "application/json; charset=utf-8";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
    }
}
