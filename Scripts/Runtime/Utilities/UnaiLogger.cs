using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnAI.Utilities
{
    public static class UnaiLogger
    {
        public static bool VerboseEnabled { get; set; }

        /// <summary>
        /// When enabled, all HTTP request/response JSON bodies are appended
        /// to a log file for debugging provider-specific issues.
        /// </summary>
        public static bool RawJsonLoggingEnabled { get; set; }

        private static readonly string LogDirectory = Path.Combine(Application.dataPath, "..", "Library", "UnAI");
        private static string _logFilePath;
        private static readonly HashSet<string> Secrets = new(StringComparer.Ordinal);
        private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "key", "apikey", "api_key", "api-key", "token", "access_token", "secret", "client_secret",
            "password", "authorization", "signature", "sig", "x-goog-api-key", "x-api-key"
        };

        public static void RegisterSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return;
            lock (Secrets) Secrets.Add(secret);
        }

        private static string RedactKnownSecrets(string text)
        {
            if (text == null) return null;
            lock (Secrets)
                foreach (var secret in Secrets.OrderByDescending(s => s.Length))
                    text = text.Replace(secret, "[REDACTED]");
            return text;
        }

        public static string RedactUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return RedactKnownSecrets(url);
            var builder = new UriBuilder(uri) { UserName = "", Password = "", Fragment = "" };
            builder.Query = string.Join("&", uri.Query.TrimStart('?').Split('&').Select(part =>
            {
                var pair = part.Split(new[] { '=' }, 2);
                return SensitiveNames.Contains(Uri.UnescapeDataString(pair[0]))
                    ? pair[0] + "=%5BREDACTED%5D" : part;
            }));
            return RedactKnownSecrets(builder.Uri.AbsoluteUri);
        }

        public static string RedactBody(string body)
        {
            if (body == null) return null;
            try
            {
                var token = JToken.Parse(body);
                if (token is JContainer container)
                    foreach (var property in container.Descendants().OfType<JProperty>().ToList())
                        if (SensitiveNames.Contains(property.Name)) property.Value = "[REDACTED]";
                body = token.ToString(Formatting.None);
            }
            catch (JsonReaderException) { }
            return RedactKnownSecrets(body);
        }

        /// <summary>
        /// Returns the path of the current raw JSON log file.
        /// A new file is created per session (based on the first log call).
        /// </summary>
        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                {
                    Directory.CreateDirectory(LogDirectory);
                    _logFilePath = Path.Combine(LogDirectory, $"raw_log_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
                }
                return _logFilePath;
            }
        }

        public static void Log(string message) => Debug.Log(message);
        public static void LogVerbose(string message) { if (VerboseEnabled) Debug.Log(message); }
        public static void LogWarning(string message) => Debug.LogWarning(message);
        public static void LogError(string message) => Debug.LogError(message);

        /// <summary>
        /// Appends a raw JSON request or response to the log file.
        /// Each entry is a single JSON line (JSONL format) with timestamp,
        /// direction, URL, and the raw body.
        /// </summary>
        public static void LogRawJson(string direction, string url, string body)
        {
            if (!RawJsonLoggingEnabled) return;

            try
            {
                string line = JsonConvert.SerializeObject(new
                {
                    ts = DateTime.UtcNow.ToString("O"), dir = direction,
                    url = RedactUrl(url), body = RedactBody(body)
                });
                File.AppendAllText(LogFilePath, line + "\n");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UNAI] Failed to write raw JSON log: {ex.Message}");
            }
        }
    }
}
