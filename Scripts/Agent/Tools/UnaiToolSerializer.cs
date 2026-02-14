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
            if (string.IsNullOrWhiteSpace(content)) return false;

            string trimmed = content.Trim();

            // Find JSON object boundaries
            int start = trimmed.IndexOf('{');
            int end = trimmed.LastIndexOf('}');
            if (start < 0 || end <= start) return false;

            try
            {
                string json = trimmed.Substring(start, end - start + 1);
                var obj = JObject.Parse(json);

                string toolName = obj["tool"]?.ToString();
                if (string.IsNullOrEmpty(toolName)) return false;

                call = new UnaiToolCall
                {
                    Id = $"text_{System.Guid.NewGuid():N}".Substring(0, 16),
                    ToolName = toolName,
                    ArgumentsJson = obj["arguments"]?.ToString(Formatting.None) ?? "{}"
                };
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
