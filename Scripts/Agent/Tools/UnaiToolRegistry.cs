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
        public void Clear() => _tools.Clear();
    }
}
