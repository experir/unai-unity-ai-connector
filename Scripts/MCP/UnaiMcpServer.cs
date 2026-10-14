using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Tools;
using UnityEditor;
using UnityEngine;

namespace UnAI.MCP
{
    public class UnaiMcpServer
    {
        public int Port { get; private set; }
        public bool IsRunning { get; private set; }
        public int ConnectedClients => _transport.ClientCount;
        public string Url => $"http://localhost:{Port}/mcp";
        public string AccessToken { get; } = UnaiMcpAccessPolicy.CreateToken();
        public bool AllowMutatingTools { get; set; }

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private UnaiToolRegistry _tools;
        private readonly UnaiMcpTransport _transport = new();

        public void Start(int port, UnaiToolRegistry tools)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[UNAI MCP] Server is already running.");
                return;
            }

            Port = port;
            _tools = tools;
            _cts = new CancellationTokenSource();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                IsRunning = true;

                Debug.Log($"[UNAI MCP] Server started on {Url}");
                Debug.Log($"[UNAI MCP] Exposing {tools.GetAllDefinitions().Count} tools via MCP protocol.");

                // Start accepting connections on a background thread
                Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Debug.LogError($"[UNAI MCP] Failed to start server: {ex.Message}");

                if (ex is HttpListenerException)
                {
                    Debug.LogError("[UNAI MCP] On Windows, if an HTTP URL reservation is required, " +
                                   "reserve only http://localhost:" + port + "/ for your Windows account. " +
                                   "See Docs/mcp.md for account-scoped setup.");
                }
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();
            _transport.CloseAll();

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }

            _listener = null;
            IsRunning = false;
            Debug.Log("[UNAI MCP] Server stopped.");
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
                catch (HttpListenerException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Debug.LogError($"[UNAI MCP] Listener error: {ex.Message}");
                }
            }
        }

        private async Task HandleContext(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address)
                || !UnaiMcpAccessPolicy.IsAllowedOrigin(request.Headers["Origin"], $"http://localhost:{Port}"))
            {
                response.StatusCode = 403;
                WriteResponse(response, "{\"error\":\"Local client or origin not allowed.\"}");
                return;
            }

            // CORS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                SetCorsHeaders(request, response);
                response.StatusCode = 204;
                response.Close();
                return;
            }

            SetCorsHeaders(request, response);
            if (!UnaiMcpAccessPolicy.IsAuthorized(request.Headers["Authorization"], AccessToken))
            {
                response.StatusCode = 401;
                response.Headers.Add("WWW-Authenticate", "Bearer");
                WriteResponse(response, "{\"error\":\"A valid local MCP bearer token is required.\"}");
                return;
            }

            string path = request.Url.AbsolutePath.TrimEnd('/');

            if (path != "/mcp")
            {
                response.StatusCode = 404;
                WriteResponse(response, "{\"error\": \"Not found. Use /mcp endpoint.\"}");
                return;
            }

            if (request.HttpMethod == "GET")
            {
                // SSE connection
                await HandleSseConnection(response, ct);
            }
            else if (request.HttpMethod == "POST")
            {
                // JSON-RPC request
                await HandleJsonRpc(request, response, ct);
            }
            else
            {
                response.StatusCode = 405;
                WriteResponse(response, "{\"error\": \"Method not allowed. Use GET (SSE) or POST (JSON-RPC).\"}");
            }
        }

        private async Task HandleJsonRpc(HttpListenerRequest request, HttpListenerResponse response,
            CancellationToken ct)
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                response.StatusCode = 400;
                WriteResponse(response, "{\"error\": \"Empty request body\"}");
                return;
            }

            // Execute tool calls on Unity main thread
            string result = null;
            var tcs = new TaskCompletionSource<string>();

            EditorApplication.delayCall += async () =>
            {
                try
                {
                    result = await UnaiMcpProtocol.HandleRequest(body, _tools, ct, AllowMutatingTools);
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };

            try
            {
                result = await tcs.Task;
            }
            catch (Exception ex)
            {
                var errorResponse = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new JObject
                    {
                        ["code"] = -32603,
                        ["message"] = $"Internal error: {ex.Message}"
                    }
                };
                result = errorResponse.ToString(Formatting.None);
            }

            if (result == null)
            {
                // Notification — no response needed
                response.StatusCode = 204;
                response.Close();
                return;
            }

            response.StatusCode = 200;
            response.ContentType = "application/json";
            WriteResponse(response, result);
        }

        private async Task HandleSseConnection(HttpListenerResponse response, CancellationToken ct)
        {
            var client = _transport.AddClient(response);

            // Send initial endpoint event so client knows where to POST
            await client.SendEvent(JsonConvert.SerializeObject(new
            {
                @event = "endpoint",
                url = Url
            }));

            // Keep connection alive until cancelled or disconnected
            try
            {
                while (!ct.IsCancellationRequested && !client.IsClosed)
                {
                    await Task.Delay(15000, ct); // Keep-alive interval
                    _transport.CleanupDisconnected();
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _transport.RemoveClient(client.Id);
            }
        }

        private static void SetCorsHeaders(HttpListenerRequest request, HttpListenerResponse response)
        {
            string origin = request.Headers["Origin"];
            if (origin != null)
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
                response.Headers.Add("Vary", "Origin");
            }
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }

        private static void WriteResponse(HttpListenerResponse response, string body)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UNAI MCP] Failed to write response: {ex.Message}");
            }
        }
    }
}
