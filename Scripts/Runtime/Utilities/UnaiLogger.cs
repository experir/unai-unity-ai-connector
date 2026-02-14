using UnityEngine;

namespace UnAI.Utilities
{
    public static class UnaiLogger
    {
        public static bool VerboseEnabled { get; set; }

        public static void Log(string message) => Debug.Log(message);
        public static void LogVerbose(string message) { if (VerboseEnabled) Debug.Log(message); }
        public static void LogWarning(string message) => Debug.LogWarning(message);
        public static void LogError(string message) => Debug.LogError(message);
    }
}
