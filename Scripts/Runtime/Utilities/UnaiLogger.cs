using System;
using System.IO;
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
                // Escape the body for embedding in JSON
                string escapedBody = body?.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") ?? "";

                string line = $"{{\"ts\":\"{DateTime.UtcNow:O}\",\"dir\":\"{direction}\",\"url\":\"{url}\",\"body\":\"{escapedBody}\"}}";
                File.AppendAllText(LogFilePath, line + "\n");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UNAI] Failed to write raw JSON log: {ex.Message}");
            }
        }
    }
}
