using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnAI.MCP
{
    public class UnaiMcpSseClient
    {
        public string Id { get; }
        public DateTime ConnectedAt { get; }

        private readonly HttpListenerResponse _response;
        private readonly StreamWriter _writer;
        private bool _closed;

        public bool IsClosed => _closed;

        public UnaiMcpSseClient(HttpListenerResponse response)
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            ConnectedAt = DateTime.UtcNow;
            _response = response;

            _response.ContentType = "text/event-stream";
            _response.Headers.Add("Cache-Control", "no-cache");
            _response.Headers.Add("Connection", "keep-alive");
            _response.Headers.Add("Access-Control-Allow-Origin", "*");

            _writer = new StreamWriter(_response.OutputStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        public async Task SendEvent(string data)
        {
            if (_closed) return;
            try
            {
                await _writer.WriteAsync($"data: {data}\n\n");
                await _writer.FlushAsync();
            }
            catch
            {
                _closed = true;
            }
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

        public async Task BroadcastEvent(string data)
        {
            foreach (var kvp in _clients)
            {
                if (kvp.Value.IsClosed)
                {
                    _clients.TryRemove(kvp.Key, out _);
                    continue;
                }

                try
                {
                    await kvp.Value.SendEvent(data);
                }
                catch
                {
                    _clients.TryRemove(kvp.Key, out _);
                }
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
