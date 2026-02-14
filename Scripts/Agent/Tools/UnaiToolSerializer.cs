using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnAI.Tools
{
    public static class UnaiToolSerializer
    {
        public static string ToTextDescription(IReadOnlyList<UnaiToolDefinition> tools)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You have access to the following tools. To call a tool, respond with ONLY a JSON object in this exact format:");
            sb.AppendLine("{\"tool\": \"tool_name\", \"arguments\": {\"param\": \"value\"}}");
            sb.AppendLine();
            sb.AppendLine("To call multiple tools, respond with one JSON object per line:");
            sb.AppendLine("{\"tool\": \"tool1\", \"arguments\": {...}}");
            sb.AppendLine("{\"tool\": \"tool2\", \"arguments\": {...}}");
            sb.AppendLine();
            sb.AppendLine("Available tools:");
            foreach (var tool in tools)
            {
                sb.AppendLine($"- {tool.Name}: {tool.Description}");
                if (tool.ParametersSchema != null)
                    sb.AppendLine($"  Parameters: {tool.ParametersSchema.ToString(Formatting.None)}");
            }
            sb.AppendLine();
            sb.AppendLine("If you don't need to call a tool, respond normally with text.");
            return sb.ToString();
        }

        public static bool TryParseTextToolCall(string content, out UnaiToolCall call)
        {
            call = null;
            var calls = ParseTextToolCalls(content);
            if (calls == null || calls.Count == 0) return false;
            call = calls[0];
            return true;
        }

        public static List<UnaiToolCall> ParseTextToolCalls(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            var results = new List<UnaiToolCall>();
            string trimmed = content.Trim();
            int pos = 0;

            while (pos < trimmed.Length)
            {
                int start = trimmed.IndexOf('{', pos);
                if (start < 0) break;

                // Find the matching closing brace by counting depth
                int depth = 0;
                int end = -1;
                bool inString = false;
                bool escape = false;
                for (int i = start; i < trimmed.Length; i++)
                {
                    char c = trimmed[i];
                    if (escape) { escape = false; continue; }
                    if (c == '\\' && inString) { escape = true; continue; }
                    if (c == '"') { inString = !inString; continue; }
                    if (inString) continue;
                    if (c == '{') depth++;
                    else if (c == '}') { depth--; if (depth == 0) { end = i; break; } }
                }

                if (end < 0) break;

                try
                {
                    string json = trimmed.Substring(start, end - start + 1);
                    var obj = JObject.Parse(json);
                    string toolName = obj["tool"]?.ToString();
                    if (!string.IsNullOrEmpty(toolName))
                    {
                        results.Add(new UnaiToolCall
                        {
                            Id = $"text_{System.Guid.NewGuid():N}".Substring(0, 16),
                            ToolName = toolName,
                            ArgumentsJson = obj["arguments"]?.ToString(Formatting.None) ?? "{}"
                        });
                    }
                }
                catch { /* skip malformed JSON */ }

                pos = end + 1;
            }

            return results.Count > 0 ? results : null;
        }
    }
}
