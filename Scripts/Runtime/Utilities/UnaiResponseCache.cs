using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnAI.Models;
using Newtonsoft.Json;

namespace UnAI.Utilities
{
    public class UnaiResponseCache
    {
        private readonly int _maxEntries;
        private readonly float _ttlSeconds;
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly LinkedList<string> _lruOrder = new();

        public int Count => _cache.Count;
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public float HitRate => (Hits + Misses) > 0 ? (float)Hits / (Hits + Misses) : 0f;

        public UnaiResponseCache(int maxEntries = 100, float ttlSeconds = 300f)
        {
            _maxEntries = maxEntries;
            _ttlSeconds = ttlSeconds;
        }

        public bool TryGet(string key, out UnaiChatResponse response)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                float age = (float)(DateTime.UtcNow - entry.CreatedAt).TotalSeconds;
                if (age < _ttlSeconds)
                {
                    // Move to front of LRU
                    _lruOrder.Remove(entry.Node);
                    _lruOrder.AddFirst(entry.Node);
                    Hits++;
                    response = entry.Response;
                    return true;
                }

                // Expired — remove
                _lruOrder.Remove(entry.Node);
                _cache.Remove(key);
            }

            Misses++;
            response = null;
            return false;
        }

        public void Put(string key, UnaiChatResponse response)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                // Update existing entry
                _lruOrder.Remove(existing.Node);
                _cache.Remove(key);
            }

            // Evict LRU if at capacity
            while (_cache.Count >= _maxEntries && _lruOrder.Count > 0)
            {
                string evictKey = _lruOrder.Last.Value;
                _lruOrder.RemoveLast();
                _cache.Remove(evictKey);
            }

            var node = _lruOrder.AddFirst(key);
            _cache[key] = new CacheEntry
            {
                Response = response,
                CreatedAt = DateTime.UtcNow,
                Node = node
            };
        }

        public void Clear()
        {
            _cache.Clear();
            _lruOrder.Clear();
            Hits = 0;
            Misses = 0;
        }

        /// <summary>
        /// Builds a deterministic cache key from a chat request and provider context.
        /// The key is a SHA256 hash of the provider ID, model, messages, and options.
        /// </summary>
        public static string BuildKey(string providerId, UnaiChatRequest request)
        {
            var sb = new StringBuilder();
            sb.Append(providerId ?? "");
            sb.Append('|');
            sb.Append(request.Model ?? "");
            sb.Append('|');

            foreach (var msg in request.Messages)
            {
                sb.Append(msg.Role.ToString());
                sb.Append(':');
                sb.Append(msg.Content ?? "");
                sb.Append(';');
            }

            if (request.Options != null)
            {
                sb.Append('|');
                sb.Append(JsonConvert.SerializeObject(request.Options, Formatting.None));
            }

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));

            // Convert to hex string
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        private class CacheEntry
        {
            public UnaiChatResponse Response;
            public DateTime CreatedAt;
            public LinkedListNode<string> Node;
        }
    }
}
