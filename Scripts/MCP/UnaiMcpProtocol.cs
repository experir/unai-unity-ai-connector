using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.MCP
{
    public static class UnaiMcpProtocol
    {
        public const string ProtocolVersion = "2025-11-25";
        public static bool SupportsVersion(string version) => version is "2025-11-25" or "2025-06-18";
        public static string SelectVersion(string requested) => SupportsVersion(requested) ? requested : ProtocolVersion;
        public const string ServerName = "unai-unity";
        public static string ServerVersion => UnaiVersion.Get();

        public static async Task<string> HandleRequest(string jsonBody, UnaiToolRegistry tools, CancellationToken ct,
            bool allowMutatingTools = false, Action<string, JObject> onNotification = null)
        {
            JObject request;
            try
            {
                var parsed = JToken.Parse(jsonBody);
                if (parsed is not JObject obj) return MakeError(null, -32600, "Invalid Request: expected one JSON-RPC object.");
                request = obj;
            }
            catch (Exception ex)
            {
                return MakeError(null, -32700, $"Parse error: {ex.Message}");
            }

            string jsonrpc = request["jsonrpc"]?.ToString();
            if (request["jsonrpc"]?.Type != JTokenType.String || jsonrpc != "2.0")
                return MakeError(request["id"], -32600, "Invalid Request: jsonrpc must be '2.0'");

            string method = request["method"]?.ToString();
            var id = request["id"];
            if (id != null && id.Type is not (JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Null))
                return MakeError(null, -32600, "Invalid Request: unsupported id type.");
            if (request["method"] == null && id != null && (request["result"] != null || request["error"] != null))
                return null; // An accepted response has no response of its own.
            if (request["method"]?.Type != JTokenType.String || string.IsNullOrEmpty(method))
                return MakeError(id, -32600, "Invalid Request: method must be a nonempty string.");
            if (request["params"] != null && request["params"] is not JObject)
                return MakeError(id, -32602, "Invalid params: expected an object.");
            var parameters = request["params"] as JObject ?? new JObject();

            // Notifications (no id) — acknowledge silently
            if (request.Property("id") == null)
            {
                onNotification?.Invoke(method, parameters);
                HandleNotification(method);
                return null; // No response for notifications
            }

            try
            {
                return method switch
                {
                    "initialize" => HandleInitialize(id, parameters),
                    "ping" => MakeResult(id, new JObject()),
                    "tools/list" => HandleToolsList(id, tools, allowMutatingTools),
                    "tools/call" => await HandleToolsCall(id, parameters, tools, ct, allowMutatingTools),
                    _ => MakeError(id, -32601, $"Method not found: {method}")
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UNAI MCP] Error handling '{method}': {ex.Message}");
                return MakeError(id, -32603, "Internal error while executing the request.");
            }
        }

        private static void HandleNotification(string method)
        {
            switch (method)
            {
                case "notifications/initialized":
                    Debug.Log("[UNAI MCP] Client initialized successfully.");
                    break;
                case "notifications/cancelled":
                    Debug.Log("[UNAI MCP] Client cancelled a request.");
                    break;
                default:
                    Debug.Log($"[UNAI MCP] Received notification: {method}");
                    break;
            }
        }

        private static string HandleInitialize(JToken id, JObject parameters)
        {
            if (parameters["protocolVersion"]?.Type != JTokenType.String)
                return MakeError(id, -32602, "initialize requires a protocolVersion string.");
            var result = new JObject
            {
                ["protocolVersion"] = SelectVersion(parameters["protocolVersion"].ToString()),
                ["capabilities"] = new JObject
                {
                    ["tools"] = new JObject
                    {
                        ["listChanged"] = false
                    }
                },
                ["serverInfo"] = new JObject
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion
                }
            };

            Debug.Log("[UNAI MCP] Client connected — initialize handshake complete.");
            return MakeResult(id, result);
        }

        private static string HandleToolsList(JToken id, UnaiToolRegistry tools, bool allowMutatingTools)
        {
            var toolsArray = new JArray();

            foreach (var def in tools.GetAllDefinitions())
            {
                if (!allowMutatingTools && !def.IsReadOnly) continue;
                var inputSchema = def.ParametersSchema != null
                    ? def.ParametersSchema.DeepClone() as JObject
                    : new JObject { ["type"] = "object" };

                toolsArray.Add(new JObject
                {
                    ["name"] = def.Name,
                    ["description"] = def.Description ?? "",
                    ["inputSchema"] = inputSchema,
                    ["annotations"] = new JObject { ["readOnlyHint"] = def.IsReadOnly }
                });
            }

            var result = new JObject { ["tools"] = toolsArray };
            Debug.Log($"[UNAI MCP] Listed {toolsArray.Count} tools.");
            return MakeResult(id, result);
        }

        private static async Task<string> HandleToolsCall(JToken id, JObject parameters,
            UnaiToolRegistry tools, CancellationToken ct, bool allowMutatingTools)
        {
            string toolName = parameters["name"]?.ToString();
            if (parameters["name"]?.Type != JTokenType.String || string.IsNullOrEmpty(toolName))
                return MakeError(id, -32602, "Missing required parameter: 'name'");
            if (parameters["arguments"] != null && parameters["arguments"] is not JObject)
                return MakeError(id, -32602, "Tool arguments must be an object.");

            var tool = tools.Get(toolName);
            if (tool == null)
                return MakeError(id, -32602, $"Unknown tool: '{toolName}'. Use tools/list to see available tools.");
            if (!allowMutatingTools && !tool.Definition.IsReadOnly)
                return MakeError(id, -32602, "Mutating tools are disabled. Enable them explicitly in Unity before calling this tool.");

            var arguments = parameters["arguments"] as JObject ?? new JObject();

            var call = new UnaiToolCall
            {
                Id = $"mcp_{Guid.NewGuid():N}".Substring(0, 24),
                ToolName = tool.Definition.Name,
                ArgumentsJson = arguments.ToString(Formatting.None)
            };

            Debug.Log($"[UNAI MCP] Calling tool '{toolName}'.");

            var toolResult = await tool.ExecuteAsync(call, ct);

            var contentArray = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = toolResult.Content ?? ""
                }
            };

            var result = new JObject
            {
                ["content"] = contentArray,
                ["isError"] = toolResult.IsError
            };

            if (toolResult.IsError)
                Debug.LogWarning($"[UNAI MCP] Tool '{toolName}' returned error: {toolResult.Content}");
            else
                Debug.Log($"[UNAI MCP] Tool '{toolName}' completed successfully.");

            return MakeResult(id, result);
        }

        private static string MakeResult(JToken id, JObject result)
        {
            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = result
            };
            return response.ToString(Formatting.None);
        }

        internal static string MakeError(JToken id, int code, string message)
        {
            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            };
            return response.ToString(Formatting.None);
        }
    }
}
