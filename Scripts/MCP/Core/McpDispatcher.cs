using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Agent;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.MCP
{
    /// <summary>
    /// Handles MCP JSON-RPC method dispatch.
    /// Parses incoming request JSON → calls the right handler → returns response JSON.
    /// </summary>
    public class McpDispatcher
    {
        private readonly UnaiToolRegistry    _tools;
        private readonly McpResourceRegistry _resources;
        private readonly McpServerConfig     _config;

        public McpDispatcher(UnaiToolRegistry tools, McpResourceRegistry resources, McpServerConfig config)
        {
            _tools     = tools;
            _resources = resources;
            _config    = config;
        }

        // ── Public entry point ─────────────────────────────────────────────────

        /// <summary>
        /// Process a raw JSON-RPC request string.
        /// Returns null for notifications (no id).
        /// </summary>
        public async Task<string> HandleAsync(string requestJson, CancellationToken ct)
        {
            JsonRpcRequest req = null;
            try
            {
                req = JsonConvert.DeserializeObject<JsonRpcRequest>(requestJson);
            }
            catch (Exception ex)
            {
                return BuildError(null, JsonRpcError.ParseError, "Parse error: " + ex.Message);
            }

            if (req == null)
                return BuildError(null, JsonRpcError.InvalidRequest, "Invalid request");

            // Notification (no id) — no response
            if (req.Id == null || req.Id.Type == JTokenType.Null)
            {
                _ = HandleNotificationAsync(req);
                return null;
            }

            try
            {
                string resultJson = await RouteAsync(req,ct);
                return BuildSuccess(req.Id, resultJson);
            }
            catch (McpMethodNotFoundException ex)
            {
                return BuildError(req.Id, JsonRpcError.MethodNotFound, ex.Message);
            }
            catch (McpInvalidParamsException ex)
            {
                return BuildError(req.Id, JsonRpcError.InvalidParams, ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return BuildError(req.Id, JsonRpcError.InternalError, "Internal error: " + ex.Message);
            }
        }

        // ── Routing ────────────────────────────────────────────────────────────

        private async Task<string> RouteAsync(JsonRpcRequest req, CancellationToken ct)
        {
            switch (req.Method)
            {
                case "initialize":
                    return HandleInitialize();

                case "ping":
                    return "{}";

                case "tools/list":
                    return HandleToolsList();

                case "tools/call":
                    return await HandleToolCallAsync(req.Params,ct);

                case "resources/list":
                    return HandleResourcesList();

                case "resources/read":
                    return await HandleResourceReadAsync(req.Params);

                default:
                    throw new McpMethodNotFoundException($"Method not found: {req.Method}");
            }
        }

        private Task HandleNotificationAsync(JsonRpcRequest notification)
        {
            Debug.Log($"[McpDispatcher] Notification: {notification.Method}");
            return Task.CompletedTask;
        }

        // ── Handler implementations ────────────────────────────────────────────

        private string HandleInitialize()
        {
            var result = new McpInitializeResult
            {
                ProtocolVersion = "2024-11-05",
                ServerInfo = new McpServerInfo
                {
                    Name    = _config.ServerName,
                    Version = _config.ServerVersion
                },
                Capabilities = new McpCapabilities
                {
                    Tools     = new McpToolsCapability    { ListChanged = false },
                    Resources = new McpResourcesCapability{ ListChanged = false, Subscribe = false },
                    Logging   = new McpLoggingCapability()
                },
                Instructions = _config.Instructions
            };
            return JsonConvert.SerializeObject(result);
        }

        private string HandleToolsList()
        {
            var toolDefs = _tools.GetAllDefinitions();
            var result = new McpToolsListResult();

            foreach (var def in toolDefs)
            {
                var toolDef = new McpToolDefinition
                {
                    Name = def.Name,
                    Description = def.Description ?? "",
                    InputSchema = def.ParametersSchema ?? new JObject { ["type"] = "object" }
                };
                result.Tools.Add(toolDef);
            }

            return JsonConvert.SerializeObject(result);
        }

        private async Task<string> HandleToolCallAsync(JObject paramsObj, CancellationToken ct)
        {
            if (paramsObj == null)
                throw new McpInvalidParamsException("Missing params in tools/call request");

            var callParams = paramsObj.ToObject<McpToolCallParams>();
            if (string.IsNullOrEmpty(callParams?.Name))
                throw new McpInvalidParamsException("Missing 'name' in tools/call params");

            string toolName = callParams.Name;
            string argsJson = callParams.Arguments?.ToString(Formatting.None) ?? "{}";

            var tool = _tools.Get(toolName) ?? _tools.GetFuzzy(toolName);
            if (tool == null)
                throw new McpMethodNotFoundException($"Tool not found: {toolName}");

            var call = new UnaiToolCall
            {
                Id = $"mcp_{Guid.NewGuid():N}".Substring(0, 24),
                ToolName = tool.Definition.Name,
                ArgumentsJson = argsJson
            };

            var toolResult = await tool.ExecuteAsync(call, ct);

            var result = new McpToolCallResult
            {
                Content = new List<McpContent>
                {
                    new McpContent
                    {
                        Type = "text",
                        Text = toolResult.Content ?? ""
                    }
                },
                IsError = toolResult.IsError
            };

            return JsonConvert.SerializeObject(result);
        }

        private string HandleResourcesList()
        {
            var result = new McpResourcesListResult();

            if (_resources != null)
            {
                foreach (var r in _resources.GetAllResources())
                {
                    result.Resources.Add(new McpResourceDefinition
                    {
                        Uri = r.Uri,
                        Name = r.Name,
                        Description = r.Description,
                        MimeType = r.MimeType ?? "text/plain"
                    });
                }
            }

            return JsonConvert.SerializeObject(result);
        }

        private async Task<string> HandleResourceReadAsync(JObject paramsObj)
        {
            if (paramsObj == null)
                throw new McpInvalidParamsException("Missing params in resources/read request");

            var readParams = paramsObj.ToObject<McpResourceReadParams>();
            if (string.IsNullOrEmpty(readParams?.Uri))
                throw new McpInvalidParamsException("Missing 'uri' in resources/read params");

            if (_resources == null)
                throw new McpMethodNotFoundException($"Resource not found: {readParams.Uri}");

            var content = await _resources.ReadAsync(readParams.Uri);
            if (content == null)
                throw new McpMethodNotFoundException($"Resource not found: {readParams.Uri}");

            var result = new McpResourceReadResult
            {
                Contents = new List<McpResourceContent>
                {
                    new McpResourceContent
                    {
                        Uri = readParams.Uri,
                        MimeType = content.MimeType ?? "text/plain",
                        Text = content.Text ?? ""
                    }
                }
            };

            return JsonConvert.SerializeObject(result);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string BuildSuccess(JToken id, string resultJson)
        {
            var response = new JsonRpcResponse
            {
                Id = id,
                Result = JsonConvert.DeserializeObject(resultJson)
            };
            return JsonConvert.SerializeObject(response);
        }

        private static string BuildError(JToken id, int code, string message)
        {
            var response = new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcErrorInfo
                {
                    Code = code,
                    Message = message
                }
            };
            return JsonConvert.SerializeObject(response);
        }
    }

    // ── Exceptions ─────────────────────────────────────────────────────────────

    public class McpMethodNotFoundException : Exception
    {
        public McpMethodNotFoundException(string message) : base(message) { }
    }

    public class McpInvalidParamsException : Exception
    {
        public McpInvalidParamsException(string message) : base(message) { }
    }

    // ── Placeholder types for resources (to be implemented elsewhere) ──────────

    public class McpResourceRegistry
    {
        public virtual IReadOnlyList<McpResourceEntry> GetAllResources() => Array.Empty<McpResourceEntry>();
        public virtual Task<McpResourceContentEntry> ReadAsync(string uri) => Task.FromResult<McpResourceContentEntry>(null);
    }

    public class McpResourceEntry
    {
        public string Uri { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MimeType { get; set; }
    }

    public class McpResourceContentEntry
    {
        public string MimeType { get; set; }
        public string Text { get; set; }
    }
}
