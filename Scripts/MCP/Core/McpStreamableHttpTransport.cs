using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Agent;
using UnityEngine;

namespace UnAI.MCP
{
      /// <summary>
    /// Streamable-HTTP MCP transport.
    ///
    /// Spec: https://spec.modelcontextprotocol.io/specification/basic/transports/#streamable-http
    ///
    /// Routes:
    ///   GET  /mcp/sse     → opens SSE stream; sends "endpoint" event with POST URL
    ///   POST /mcp/message → accepts JSON-RPC, replies inline OR pushes to SSE stream
    ///
    /// Session management:
    ///   Each GET /sse creates a session. Session-ID is communicated via the "endpoint" event
    ///   query parameter and echoed back in POST requests via ?sessionId=...
    /// </summary>
    public class McpStreamableHttpTransport : IDisposable
    {
        // ── Fields ────────────────────────────────────────────────────────────

        private readonly McpServerConfig  _config;
        private readonly McpDispatcher    _dispatcher;

        private HttpListener              _listener;
        private CancellationTokenSource   _cts;
        private Task                      _listenerTask;

        // sessionId → SSE writer queue
        private readonly ConcurrentDictionary<string, SseSession> _sessions
            = new ConcurrentDictionary<string, SseSession>();


        public bool IsRunning => _listener?.IsListening ?? false;

        // ── Constructor ───────────────────────────────────────────────────────

        public McpStreamableHttpTransport(McpServerConfig config, McpDispatcher dispatcher)
        {
            _config     = config;
            _dispatcher = dispatcher;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Start()
        {
            if (IsRunning) return;

            _cts      = new CancellationTokenSource();
            _listener = new HttpListener();

            // Register prefixes
            _listener.Prefixes.Add($"http://+:{_config.Port}{_config.BasePath}/");
            try { _listener.Start(); }
            catch (Exception ex)
            {
                Debug.LogError($"[McpTransport] Failed to start HttpListener: {ex.Message}");
                return;
            }

            _listenerTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            Debug.Log($"[McpTransport] Listening on port {_config.Port}{_config.BasePath}");
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }

            foreach (var s in _sessions.Values)
                s.Dispose();
            _sessions.Clear();

            Debug.Log("[McpTransport] Stopped.");
        }

        public void Dispose() => Stop();

        // ── Accept loop ───────────────────────────────────────────────────────

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = HandleContextAsync(ctx, ct);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException ex) when (ct.IsCancellationRequested)
                {
                    _ = ex; break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[McpTransport] Accept error: {ex.Message}");
                }
            }
        }

        // ── Request routing ───────────────────────────────────────────────────

        private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var req  = ctx.Request;
            var resp = ctx.Response;

            // CORS pre-flight / headers
            if (_config.AllowCors)
            {
                resp.AddHeader("Access-Control-Allow-Origin",  "*");
                resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept, Mcp-Session-Id");
            }

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = 204;
                resp.Close();
                return;
            }

            string path = req.Url.AbsolutePath.TrimEnd('/');

            if (req.HttpMethod == "GET" && path.EndsWith("/sse"))
            {
                await HandleSseAsync(ctx, ct);
            }
            else if (req.HttpMethod == "POST" && path.EndsWith("/message"))
            {
                await HandlePostAsync(ctx, ct);
            }
            else if (req.HttpMethod == "DELETE")
            {
                HandleDelete(ctx);
            }
            else
            {
                resp.StatusCode = 404;
                resp.Close();
            }
        }

        // ── SSE handler ───────────────────────────────────────────────────────

        private async Task HandleSseAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var resp      = ctx.Response;
            resp.StatusCode = 200;
            resp.ContentType = "text/event-stream";
            resp.AddHeader("Cache-Control", "no-cache");
            resp.AddHeader("Connection", "keep-alive");
            resp.AddHeader("X-Accel-Buffering", "no");

            string sessionId = Guid.NewGuid().ToString("N");
            string postUrl   = $"http://localhost:{_config.Port}{_config.BasePath}/message?sessionId={sessionId}";

            var session = new SseSession(sessionId, resp.OutputStream);
            _sessions[sessionId] = session;

            if (_config.VerboseLogging)
                Debug.Log($"[McpTransport] SSE session opened: {sessionId}");

            try
            {
                // Send initial "endpoint" event so client knows where to POST
                await session.SendEventAsync("endpoint", postUrl, ct);

                // Keep alive — pump messages from the session queue until client disconnects
                while (!ct.IsCancellationRequested && !session.IsClosed)
                {
                    if (session.OutboundQueue.TryDequeue(out string msg))
                    {
                        await session.SendEventAsync("message", msg, ct);
                    }
                    else
                    {
                        // Heartbeat ping every 15 s
                        bool timedOut = await session.WaitForMessageOrTimeoutAsync(15_000, ct);
                        if (!timedOut)
                            await session.SendEventAsync("ping", "{}", ct);
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                if (_config.VerboseLogging)
                    Debug.LogWarning($"[McpTransport] SSE session {sessionId} error: {ex.Message}");
            }
            finally
            {
                _sessions.TryRemove(sessionId, out _);
                session.Dispose();
                if (_config.VerboseLogging)
                    Debug.Log($"[McpTransport] SSE session closed: {sessionId}");
            }
        }

        // ── POST handler ──────────────────────────────────────────────────────

        private async Task HandlePostAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var req  = ctx.Request;
            var resp = ctx.Response;

            // Read body
            string body;
            using (var sr = new StreamReader(req.InputStream, Encoding.UTF8))
                body = await sr.ReadToEndAsync();

            if (_config.VerboseLogging)
                Debug.Log($"[McpTransport] → {body}");

            // Parse sessionId from query string (optional)
            string sessionId = req.QueryString["sessionId"];

            // Dispatch on a background thread, but allow main-thread callbacks
            string responseJson = await DispatchOnMainThreadAsync(body,ct);

            if (_config.VerboseLogging && responseJson != null)
                Debug.Log($"[McpTransport] ← {responseJson}");

            if (responseJson == null)
            {
                // Notification — 202 Accepted, no body
                resp.StatusCode = 202;
                resp.Close();
                return;
            }

            // If client has an active SSE session, push there (but also reply inline)
            if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId, out var session))
                session.Enqueue(responseJson);

            // Always write inline response (client may use it directly)
            byte[] bytes = Encoding.UTF8.GetBytes(responseJson);
            resp.StatusCode  = 200;
            resp.ContentType = "application/json";
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            resp.Close();
        }

        private void HandleDelete(HttpListenerContext ctx)
        {
            string sessionId = ctx.Request.QueryString["sessionId"];
            if (!string.IsNullOrEmpty(sessionId) && _sessions.TryRemove(sessionId, out var s))
                s.Dispose();

            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
        }

        // ── Main-thread dispatch ──────────────────────────────────────────────

        /// <summary>
        /// Run dispatcher on Unity main thread (required for Unity API calls),
        /// then return result on the calling thread.
        /// </summary>
        private Task<string> DispatchOnMainThreadAsync(string json, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            ct.Register(() => tcs.TrySetCanceled(ct));

            MainThreadDispatcher.Instance.RunOnMainThread(async () =>
            {
                try
                {
                    string result = await _dispatcher.HandleAsync(json,ct);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }
    }

    // ─── SSE Session ──────────────────────────────────────────────────────────

    internal class SseSession : IDisposable
    {
        public string SessionId { get; }
        public ConcurrentQueue<string> OutboundQueue { get; } = new ConcurrentQueue<string>();
        public bool IsClosed { get; private set; }

        private readonly Stream _stream;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _signal    = new SemaphoreSlim(0, int.MaxValue);

        public SseSession(string sessionId, Stream stream)
        {
            SessionId = sessionId;
            _stream   = stream;
        }

        public void Enqueue(string data)
        {
            OutboundQueue.Enqueue(data);
            _signal.Release();
        }

        /// <summary>Returns true if timed out (no message), false if message arrived.</summary>
        public async Task<bool> WaitForMessageOrTimeoutAsync(int timeoutMs, CancellationToken ct)
        {
            bool got = await _signal.WaitAsync(timeoutMs, ct);
            return !got; // true = timed out
        }

        public async Task SendEventAsync(string eventName, string data, CancellationToken ct)
        {
            if (IsClosed) return;
            await _writeLock.WaitAsync(ct);
            try
            {
                string payload = $"event: {eventName}\ndata: {data}\n\n";
                byte[] bytes   = Encoding.UTF8.GetBytes(payload);
                await _stream.WriteAsync(bytes, 0, bytes.Length, ct);
                await _stream.FlushAsync(ct);
            }
            catch
            {
                IsClosed = true;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            IsClosed = true;
            try { _stream?.Close(); }
            catch
            {
                // ignored
            }
        }
    }
}