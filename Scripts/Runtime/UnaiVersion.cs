namespace UnAI
{
    /// <summary>
    /// Single source of truth for the UNAI package version.
    /// Reads from package.json at edit-time; falls back to the baked constant at runtime.
    /// </summary>
    public static class UnaiVersion
    {
        /// <summary>Baked version — checked against package.json by CI.</summary>
        public const string Current = "1.1.0-beta.1";

#if UNITY_EDITOR
        /// <summary>
        /// Returns the version read from package.json when running in the editor,
        /// or the baked <see cref="Current"/> constant at runtime.
        /// </summary>
        public static string Get()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnaiVersion).Assembly);
            if (package != null && !string.IsNullOrEmpty(package.version)) return package.version;
            // Also support a manual copy anywhere under Assets.
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("UnaiVersion t:MonoScript"))
            {
                string script = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var folder = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(script));
                while (folder != null && folder.FullName.StartsWith(System.IO.Path.GetFullPath("Assets"), System.StringComparison.OrdinalIgnoreCase))
                {
                    string manifest = System.IO.Path.Combine(folder.FullName, "package.json");
                    if (System.IO.File.Exists(manifest))
                    {
                        var data = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(manifest));
                        if (data["name"]?.ToString() == "com.unai.universal-ai-connector")
                            return data["version"]?.ToString() ?? Current;
                    }
                    folder = folder.Parent;
                }
            }
            return Current;
        }
#else
        /// <summary>
        /// Returns the baked version constant at runtime.
        /// </summary>
        public static string Get() => Current;
#endif
    }
}
