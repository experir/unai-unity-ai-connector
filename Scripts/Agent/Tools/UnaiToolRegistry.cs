using System;
using System.Collections.Generic;
using System.Linq;

namespace UnAI.Tools
{
    public class UnaiToolRegistry
    {
        private readonly Dictionary<string, IUnaiTool> _tools = new();

        public void Register(IUnaiTool tool) => _tools[tool.Definition.Name] = tool;
        public bool Unregister(string name) => _tools.Remove(name);
        public IUnaiTool Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;
        public IReadOnlyList<UnaiToolDefinition> GetAllDefinitions() =>
            _tools.Values.Select(t => t.Definition).ToList();
        public IReadOnlyCollection<IUnaiTool> GetAll() => _tools.Values;
        public IReadOnlyList<string> GetNames() => _tools.Keys.ToList();
        public void Clear() => _tools.Clear();

        /// <summary>
        /// Try to find a tool by fuzzy matching: case-insensitive, underscore/camelCase variants.
        /// Returns null if no reasonable match found.
        /// </summary>
        public IUnaiTool GetFuzzy(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Exact match
            if (_tools.TryGetValue(name, out var exact)) return exact;

            // Case-insensitive match
            var lower = name.ToLowerInvariant();
            foreach (var kvp in _tools)
            {
                if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            // Normalize: remove underscores/hyphens and compare
            string normalized = lower.Replace("_", "").Replace("-", "");
            foreach (var kvp in _tools)
            {
                string toolNorm = kvp.Key.ToLowerInvariant().Replace("_", "").Replace("-", "");
                if (toolNorm == normalized) return kvp.Value;
            }

            // Partial match: if the name contains a tool name or vice versa
            foreach (var kvp in _tools)
            {
                string toolLower = kvp.Key.ToLowerInvariant();
                if (toolLower.Contains(normalized) || normalized.Contains(toolLower))
                    return kvp.Value;
            }

            return null;
        }
    }
}
