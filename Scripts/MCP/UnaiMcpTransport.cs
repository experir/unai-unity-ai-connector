using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnAI.MCP
{
    public class UnaiMcpSseClient
    {
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        
        public string Id { get; }
        public DateTime ConnectedAt { get; }

        private readonly HttpListenerResponse _response;
        private readonly StreamWriter _writer;
        
        private volatile bool _closed;
        private int _eventId = 0;
        public bool IsClosed => _closed; 

        public UnaiMcpSseClient(HttpListenerResponse response)
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            ConnectedAt = DateTime.UtcNow;
            _response = response;

            _response.ContentType = "text/event-stream; charset=utf-8";
            _response.Headers.Set("Cache-Control", "no-cache, no-transform");
            _response.Headers.Set("Connection", "keep-alive");
            _response.Headers.Set("X-Accel-Buffering", "no");
            _response.Headers.Set("Access-Control-Allow-Origin", "*");

            _writer = new StreamWriter(_response.OutputStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }
        
        /// <summary>
        /// 发送心跳注释，保持连接（不触发客户端事件）
        /// </summary>
        public async Task SendKeepAliveAsync()
        {
            if (_closed) return;

            await _writeLock.WaitAsync();
            try
            {
                // 以 : 开头的行是注释，浏览器/客户端忽略但连接保活
                await _writer.WriteLineAsync($": keepalive {DateTime.UtcNow:HH:mm:ss}");
                await _writer.WriteLineAsync();
                await _writer.FlushAsync();
            }
            catch
            {
                _closed = true;
            }
            finally
            {
                _writeLock.Release();
            }
        }
        
        /// <summary>
        /// Send standard SSE event
        /// </summary>
        /// <param name="data">Event data (JSON string)</param>
        /// <param name="eventType">Event type, null means no event: field</param>
        public async Task SendEventAsync(string data, string eventType = null)
        {
            if (_closed) return;

            await _writeLock.WaitAsync();
            try
            {
                var sb = new StringBuilder();

                // id field: for client reconnection recovery
                sb.AppendLine($"id: {Interlocked.Increment(ref _eventId)}");

                // event field: client can listen with addEventListener('message') or specific type
                if (!string.IsNullOrEmpty(eventType))
                    sb.AppendLine($"event: {eventType}");

                // data field: split multi-line content
                foreach (var line in SplitLines(data))
                    sb.AppendLine($"data: {line}");

                // Empty line: marks end of event (required)
                sb.AppendLine();

                await _writer.WriteAsync(sb.ToString());
                await _writer.FlushAsync();
            }
            catch (Exception ex)
            {
                _closed = true;
                Debug.LogWarning($"[UNAI SSE] Client {Id} write failed: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }
        
        // SSE data field cannot contain line breaks, split into multiple data: lines
        private static IEnumerable<string> SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield return string.Empty;
                yield break;
            }
            foreach (var line in text.Split('\n'))
                yield return line.TrimEnd('\r');
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try
            {
                _writer?.Close();
                _response?.Close();
            }
            catch { }
        }
    }

    public class UnaiMcpTransport
    {
        private readonly ConcurrentDictionary<string, UnaiMcpSseClient> _clients = new();

        public int ClientCount => _clients.Count;

        public UnaiMcpSseClient AddClient(HttpListenerResponse response)
        {
            var client = new UnaiMcpSseClient(response);
            _clients[client.Id] = client;
            Debug.Log($"[UNAI MCP] SSE client connected: {client.Id}");
            return client;
        }

        public void RemoveClient(string id)
        {
            if (_clients.TryRemove(id, out var client))
            {
                client.Close();
                Debug.Log($"[UNAI MCP] SSE client disconnected: {id}");
            }
        }
        
        public bool HasClient(string id)
        {
            return _clients.ContainsKey(id);
        }
        
        /// <summary>
        /// Send notification to a specific client
        /// </summary>
        public async Task SendToClient(string clientId, string method, object @params)
        {
            if (!_clients.TryGetValue(clientId, out var client) || client.IsClosed)
                return;
                
            var notification = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = JObject.FromObject(@params)
            };
            
            await client.SendEventAsync(notification.ToString(Formatting.None), eventType: "message");
        }
        
        /// <summary>
        /// Broadcast notification to all connected clients
        /// </summary>
        public async Task BroadcastNotification(string method, object @params)
        {
            var notification = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = JObject.FromObject(@params)
            };

            foreach (var kvp in _clients)
            {
                if (kvp.Value.IsClosed) 
                { 
                    _clients.TryRemove(kvp.Key, out _); 
                    continue; 
                }
                await kvp.Value.SendEventAsync(notification.ToString(Formatting.None), eventType: "message");
            }
        }

        public void CloseAll()
        {
            foreach (var kvp in _clients)
            {
                kvp.Value.Close();
            }
            _clients.Clear();
        }

        public void CleanupDisconnected()
        {
            foreach (var kvp in _clients)
            {
                if (kvp.Value.IsClosed)
                    _clients.TryRemove(kvp.Key, out _);
            }
        }
    }
}