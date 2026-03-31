using System;
using System.Collections.Concurrent;
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
        public string Url => $"http://0.0.0.0:{Port}/mcp";

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private UnaiToolRegistry _tools;
        private readonly UnaiMcpTransport _transport = new();
        private readonly ConcurrentDictionary<string, string> _sessionMap = new();

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
                //_listener.Prefixes.Add($"http://0.0.0.0:{port}/");
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                IsRunning = true;

                Debug.Log($"[UNAI MCP] Server started on http://0.0.0.0:{Port}/mcp");
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
                    Debug.LogError("[UNAI MCP] Hint: On Windows, you may need to run Unity as Administrator, " +
                                   "or grant permission with: netsh http add urlacl url=http://+:" + port + "/ user=Everyone");
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
            catch
            {
                // ignored
            }

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

            // CORS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                SetCorsHeaders(response);
                response.StatusCode = 204;
                response.Close();
                return;
            }

            SetCorsHeaders(response);

            string path = request.Url.AbsolutePath.TrimEnd('/');

            if (path != "/mcp")
            {
                response.StatusCode = 404;
                WriteResponse(response, "{\"error\": \"Not found. Use /mcp endpoint.\"}");
                return;
            }

            if (request.HttpMethod == "GET")
            {
                // SSE connection (for backward compatibility)
                await HandleSseConnection(response, ct);
            }
            else if (request.HttpMethod == "POST")
            {
                // Streamable HTTP JSON-RPC request
                await HandleStreamableHttp(request, response, context, ct);
            }
            else
            {
                response.StatusCode = 405;
                WriteResponse(response, "{\"error\": \"Method not allowed. Use GET (SSE) or POST (JSON-RPC).\"}");
            }
        }

        private async Task HandleSseConnection(HttpListenerResponse response, CancellationToken ct)
        {
            var client = _transport.AddClient(response);
            
            // Store mapping for this client
            _sessionMap[client.Id] = client.Id;
            
            // Send initial endpoint event so client knows where to POST
            await client.SendEventAsync(Url, eventType: "endpoint");
            
            // Send client ID to the client
            var clientInfo = new JObject
            {
                ["clientId"] = client.Id,
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };
            await client.SendEventAsync(clientInfo.ToString(Formatting.None), eventType: "client_ready");

            // Keep connection alive until cancelled or disconnected
            try
            {
                while (!ct.IsCancellationRequested && !client.IsClosed)
                {
                    await Task.Delay(15000, ct); // Keep-alive interval
                    await client.SendKeepAliveAsync();
                    _transport.CleanupDisconnected();
                    
                    // Clean up stale session mappings
                    foreach (var kvp in _sessionMap)
                    {
                        if (!_transport.HasClient(kvp.Key))
                            _sessionMap.TryRemove(kvp.Key, out _);
                    }
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _transport.RemoveClient(client.Id);
                _sessionMap.TryRemove(client.Id, out _);
            }
        }

        private async Task HandleStreamableHttp(HttpListenerRequest request, HttpListenerResponse response,
            HttpListenerContext context, CancellationToken ct)
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

            // Get or create session ID (Streamable HTTP uses mcp-session-id header)
            string sessionId = GetOrCreateSessionId(context);
            
            // Set session ID in response header
            if (!string.IsNullOrEmpty(sessionId))
            {
                response.Headers.Add("mcp-session-id", sessionId);
            }

            // Execute tool calls on Unity main thread
            string result = null;
            var tcs = new TaskCompletionSource<string>();

            EditorApplication.delayCall += async () =>
            {
                try
                {
                    result = await UnaiMcpProtocol.HandleRequest(body, _tools, _transport, sessionId, ct);
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

        private string GetOrCreateSessionId(HttpListenerContext context)
        {
            // Try to get session ID from header (Streamable HTTP standard)
            var sessionId = context.Request.Headers["mcp-session-id"];
            if (!string.IsNullOrEmpty(sessionId) && _sessionMap.ContainsKey(sessionId))
                return sessionId;
            
            // Try X-Client-Id for backward compatibility
            var clientId = context.Request.Headers["X-Client-Id"];
            if (!string.IsNullOrEmpty(clientId) && _sessionMap.ContainsKey(clientId))
                return clientId;
            
            // Create new session for initialize request
            var newSessionId = Guid.NewGuid().ToString("N");
            _sessionMap[newSessionId] = newSessionId;
            Debug.Log($"[UNAI MCP] Created new session: {newSessionId}");
            return newSessionId;
        }

        private static void SetCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Client-Id, mcp-session-id");
            response.Headers.Add("Access-Control-Expose-Headers", "mcp-session-id");
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