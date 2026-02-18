namespace UnAI
{
    /// <summary>
    /// Single source of truth for the UNAI package version.
    /// Reads from package.json at edit-time; falls back to the baked constant at runtime.
    /// </summary>
    public static class UnaiVersion
    {
        /// <summary>Baked version — keep in sync with package.json (automated by CI or the Hub window).</summary>
        public const string Current = "1.0.0";

#if UNITY_EDITOR
        private static string _resolved;

        /// <summary>
        /// Returns the version read from package.json when running in the editor,
        /// or the baked <see cref="Current"/> constant at runtime.
        /// </summary>
        public static string Get()
        {
            if (_resolved != null) return _resolved;

            string packageJsonPath = GetPackageJsonPath();
            if (packageJsonPath != null && System.IO.File.Exists(packageJsonPath))
            {
                string json = System.IO.File.ReadAllText(packageJsonPath);
                // Tiny extraction — avoids pulling in Newtonsoft just for one field.
                const string key = "\"version\"";
                int idx = json.IndexOf(key);
                if (idx >= 0)
                {
                    int colon = json.IndexOf(':', idx + key.Length);
                    int open = json.IndexOf('"', colon + 1);
                    int close = json.IndexOf('"', open + 1);
                    if (open >= 0 && close > open)
                    {
                        _resolved = json.Substring(open + 1, close - open - 1);
                        return _resolved;
                    }
                }
            }

            _resolved = Current;
            return _resolved;
        }

        private static string GetPackageJsonPath()
        {
            // Try to find it relative to this script's assembly location.
            string[] guids = UnityEditor.AssetDatabase.FindAssets("package t:TextAsset",
                new[] { "Assets/unai-unity-ai-connector" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("package.json"))
                    return path;
            }

            // Fallback — common path
            string fallback = "Assets/unai-unity-ai-connector/package.json";
            return System.IO.File.Exists(fallback) ? fallback : null;
        }
#else
        /// <summary>
        /// Returns the baked version constant at runtime.
        /// </summary>
        public static string Get() => Current;
#endif
    }
}
