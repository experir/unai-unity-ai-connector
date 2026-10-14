using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnAI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

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
            if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            if (ttlSeconds <= 0 || float.IsNaN(ttlSeconds) || float.IsInfinity(ttlSeconds))
                throw new ArgumentOutOfRangeException(nameof(ttlSeconds));
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
                    response = CopyResponse(entry.Response);
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
                Response = CopyResponse(response),
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
        public static string BuildKey(string providerId, UnaiChatRequest request, string providerContext = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var data = new JObject
            {
                ["provider"] = providerId,
                ["context"] = providerContext,
                ["model"] = request.Model,
                ["messages"] = JToken.FromObject(request.Messages),
                ["options"] = request.Options == null ? JValue.CreateNull() : JToken.FromObject(request.Options),
                ["tools"] = request.Tools == null ? JValue.CreateNull() : JToken.FromObject(request.Tools)
            };

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Canonicalize(data).ToString(Formatting.None)));

            // Convert to hex string
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        private static JToken Canonicalize(JToken token)
        {
            if (token is JObject obj)
                return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new JProperty(p.Name, Canonicalize(p.Value))));
            if (token is JArray array)
                return new JArray(array.Select(Canonicalize));
            return token.DeepClone();
        }

        private static UnaiChatResponse CopyResponse(UnaiChatResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            var copy = JsonConvert.DeserializeObject<UnaiChatResponse>(JsonConvert.SerializeObject(response));
            copy.RawResponse = response.RawResponse;
            copy.WasStreamed = response.WasStreamed;
            return copy;
        }

        private class CacheEntry
        {
            public UnaiChatResponse Response;
            public DateTime CreatedAt;
            public LinkedListNode<string> Node;
        }
    }
}
