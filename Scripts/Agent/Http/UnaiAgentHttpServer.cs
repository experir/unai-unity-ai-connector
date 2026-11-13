using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Models;
using UnityEngine;

namespace UnAI.Agent.Http
{
    /// <summary>
    /// Lets other apps on this computer talk to a UnaiAgent over HTTP, in the Editor and in desktop builds.
    /// Listens on localhost only, requires a bearer token on every request, refuses browser pages
    /// (requests with an Origin header), and runs the agent on the Unity main thread. Each session
    /// has its own conversation memory.
    ///
    /// Endpoints (JSON):
    ///   GET  /health                                   -> { "status": "ok" }
    ///   POST /agent/chat  { "message", "session"? }    -> { "session", "reply", "toolCalls": [ { "name", "arguments", "result", "isError" } ] }
    ///   POST /agent/reset { "session"? }               -> { "session", "reset": true }
    /// </summary>
    public sealed class UnaiAgentHttpServer
    {
        private const string DefaultSession = "default";
        private const int MaxSessions = 100;

        private readonly Func<UnaiAgent> _createAgent;
        private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
        private HttpListener _listener;
        private SynchronizationContext _mainThread;
        private CancellationTokenSource _cts;

        private sealed class Session
        {
            public UnaiAgent Agent;
            public readonly SemaphoreSlim Gate = new(1, 1);
        }

        /// <param name="createAgent">Creates the agent for a new session. Called on the main thread.</param>
        public UnaiAgentHttpServer(Func<UnaiAgent> createAgent)
        {
            _createAgent = createAgent ?? throw new ArgumentNullException(nameof(createAgent));
        }

        public bool IsRunning => _listener?.IsListening == true;
        public int Port { get; private set; }
        public string Url => $"http://localhost:{Port}/";
        /// <summary>Clients send it as "Authorization: Bearer &lt;token&gt;".</summary>
        public string AccessToken { get; private set; }
        public int MaxRequestBytes { get; set; } = 256 * 1024;

        public static bool IsSupportedPlatform =>
            Application.isEditor || Application.platform is RuntimePlatform.WindowsPlayer
                or RuntimePlatform.OSXPlayer or RuntimePlatform.LinuxPlayer;

        /// <summary>Starts listening. Call on the Unity main thread. Empty token generates a random one.</summary>
        public void Start(int port, string accessToken = null)
        {
            if (IsRunning) return;
            if (!IsSupportedPlatform)
                throw new PlatformNotSupportedException("The agent HTTP API runs in the Editor and in Windows, macOS and Linux builds.");
            _mainThread = SynchronizationContext.Current
                ?? throw new InvalidOperationException("Start the agent HTTP API from the Unity main thread.");

            AccessToken = string.IsNullOrEmpty(accessToken) ? CreateToken() : accessToken;
            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _ = ListenAsync(_listener, _cts.Token);
            Debug.Log($"[UNAI Agent API] Listening on {Url} (localhost only, bearer token required).");
        }

        public void Stop()
        {
            if (_listener == null) return;
            _cts?.Cancel();
            try { _listener.Close(); } catch (ObjectDisposedException) { }
            _listener = null;
            _sessions.Clear();
            Debug.Log("[UNAI Agent API] Stopped.");
        }

        private async Task ListenAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception) when (ct.IsCancellationRequested || !listener.IsListening) { return; }
                catch (HttpListenerException) { continue; }

                // Requests are handled on the main thread: agents, providers and tools use Unity APIs.
                _mainThread.Post(_ => _ = HandleAsync(context, ct), null);
            }
        }

        private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;
            try
            {
                if (request.Headers["Origin"] != null)
                {
                    await Reply(response, 403, Error("Requests from web pages are not accepted."));
                    return;
                }
                if (!IsAuthorized(request.Headers["Authorization"]))
                {
                    response.AddHeader("WWW-Authenticate", "Bearer");
                    await Reply(response, 401, Error("Missing or invalid bearer token."));
                    return;
                }

                string path = request.Url.AbsolutePath.TrimEnd('/');
                switch (path)
                {
                    case "/health" when request.HttpMethod == "GET":
                        await Reply(response, 200, new JObject { ["status"] = "ok" });
                        return;
                    case "/agent/chat" when request.HttpMethod == "POST":
                        await HandleChat(request, response, ct);
                        return;
                    case "/agent/reset" when request.HttpMethod == "POST":
                        var body = await ReadBody(request, response);
                        if (body == null) return;
                        string session = SessionId(body);
                        _sessions.Remove(session);
                        await Reply(response, 200, new JObject { ["session"] = session, ["reset"] = true });
                        return;
                    case "/health":
                    case "/agent/chat":
                    case "/agent/reset":
                        await Reply(response, 405, Error($"{request.HttpMethod} is not allowed here."));
                        return;
                    default:
                        await Reply(response, 404, Error("Not found. Endpoints: GET /health, POST /agent/chat, POST /agent/reset."));
                        return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UNAI Agent API] {ex.Message}");
                try { await Reply(response, 500, Error("Internal error.")); } catch { /* client gone */ }
            }
        }

        private async Task HandleChat(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
        {
            var body = await ReadBody(request, response);
            if (body == null) return;
            if (body["message"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(body["message"].ToString()))
            {
                await Reply(response, 400, Error("'message' must be a non-empty string."));
                return;
            }

            string sessionId = SessionId(body);
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                if (_sessions.Count >= MaxSessions)
                {
                    await Reply(response, 429, Error($"Too many sessions (max {MaxSessions}). Reset unused ones."));
                    return;
                }
                _sessions[sessionId] = session = new Session { Agent = _createAgent() };
            }

            var toolCalls = new JArray();
            var pending = new Dictionary<string, JObject>();
            void OnToolCall(UnaiAgentToolCallArgs args)
            {
                var entry = new JObject
                {
                    ["name"] = args.ToolCall.ToolName,
                    ["arguments"] = ParseOrString(args.ToolCall.ArgumentsJson)
                };
                toolCalls.Add(entry);
                if (args.ToolCall.Id != null) pending[args.ToolCall.Id] = entry;
            }
            void OnToolResult(UnaiAgentToolResultArgs args)
            {
                if (args.Result.ToolCallId != null && pending.TryGetValue(args.Result.ToolCallId, out var entry))
                {
                    entry["result"] = args.Result.Content;
                    entry["isError"] = args.Result.IsError;
                }
            }

            await session.Gate.WaitAsync(ct);
            session.Agent.OnToolCall += OnToolCall;
            session.Agent.OnToolResult += OnToolResult;
            try
            {
                var step = await session.Agent.RunAsync(body["message"].ToString(), ct);
                await Reply(response, 200, new JObject
                {
                    ["session"] = sessionId,
                    ["reply"] = step.Response?.Content ?? "",
                    ["toolCalls"] = toolCalls
                });
            }
            catch (UnaiRequestException ex)
            {
                await Reply(response, 502, Error(ex.ErrorInfo.UserFriendlyMessage));
            }
            finally
            {
                session.Agent.OnToolCall -= OnToolCall;
                session.Agent.OnToolResult -= OnToolResult;
                session.Gate.Release();
            }
        }

        private async Task<JObject> ReadBody(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.ContentLength64 > MaxRequestBytes)
            {
                await Reply(response, 413, Error("Request body is too large."));
                return null;
            }
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            string text = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text)) return new JObject();
            try
            {
                if (JToken.Parse(text) is JObject obj) return obj;
            }
            catch (JsonException) { }
            await Reply(response, 400, Error("The body must be a JSON object."));
            return null;
        }

        private static string SessionId(JObject body) =>
            body["session"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(body["session"].ToString())
                ? body["session"].ToString() : DefaultSession;

        private static JToken ParseOrString(string json)
        {
            try { return JToken.Parse(string.IsNullOrEmpty(json) ? "{}" : json); }
            catch (JsonException) { return json; }
        }

        private bool IsAuthorized(string authorization)
        {
            if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
            string supplied = authorization.Substring(7);
            if (supplied.Length != AccessToken.Length) return false;
            int difference = 0;
            for (int i = 0; i < AccessToken.Length; i++) difference |= supplied[i] ^ AccessToken[i];
            return difference == 0;
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static JObject Error(string message) => new() { ["error"] = message };

        private static async Task Reply(HttpListenerResponse response, int status, JObject body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            response.StatusCode = status;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }
    }
}
