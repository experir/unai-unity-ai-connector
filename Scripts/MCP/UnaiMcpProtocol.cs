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
        public const string ProtocolVersion = "2025-03-26";
        public const string ServerName = "unai-unity";
        public const string ServerVersion = "1.0.0";

        public static async Task<string> HandleRequest(string jsonBody, UnaiToolRegistry tools, CancellationToken ct)
        {
            JObject request;
            try
            {
                request = JObject.Parse(jsonBody);
            }
            catch (Exception ex)
            {
                return MakeError(null, -32700, $"Parse error: {ex.Message}");
            }

            string jsonrpc = request["jsonrpc"]?.ToString();
            if (jsonrpc != "2.0")
                return MakeError(request["id"], -32600, "Invalid Request: jsonrpc must be '2.0'");

            string method = request["method"]?.ToString();
            var id = request["id"];
            var parameters = request["params"] as JObject ?? new JObject();

            // Notifications (no id) — acknowledge silently
            if (id == null || id.Type == JTokenType.Null)
            {
                HandleNotification(method);
                return null; // No response for notifications
            }

            try
            {
                return method switch
                {
                    "initialize" => HandleInitialize(id),
                    "ping" => MakeResult(id, new JObject()),
                    "tools/list" => HandleToolsList(id, tools),
                    "tools/call" => await HandleToolsCall(id, parameters, tools, ct),
                    _ => MakeError(id, -32601, $"Method not found: {method}")
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UNAI MCP] Error handling '{method}': {ex.Message}");
                return MakeError(id, -32603, $"Internal error: {ex.Message}");
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

        private static string HandleInitialize(JToken id)
        {
            var result = new JObject
            {
                ["protocolVersion"] = ProtocolVersion,
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

        private static string HandleToolsList(JToken id, UnaiToolRegistry tools)
        {
            var toolsArray = new JArray();

            foreach (var def in tools.GetAllDefinitions())
            {
                var inputSchema = def.ParametersSchema != null
                    ? def.ParametersSchema.DeepClone() as JObject
                    : new JObject { ["type"] = "object" };

                toolsArray.Add(new JObject
                {
                    ["name"] = def.Name,
                    ["description"] = def.Description ?? "",
                    ["inputSchema"] = inputSchema
                });
            }

            var result = new JObject { ["tools"] = toolsArray };
            Debug.Log($"[UNAI MCP] Listed {toolsArray.Count} tools.");
            return MakeResult(id, result);
        }

        private static async Task<string> HandleToolsCall(JToken id, JObject parameters,
            UnaiToolRegistry tools, CancellationToken ct)
        {
            string toolName = parameters["name"]?.ToString();
            if (string.IsNullOrEmpty(toolName))
                return MakeError(id, -32602, "Missing required parameter: 'name'");

            var tool = tools.Get(toolName) ?? tools.GetFuzzy(toolName);
            if (tool == null)
                return MakeError(id, -32602, $"Unknown tool: '{toolName}'. Use tools/list to see available tools.");

            var arguments = parameters["arguments"] as JObject ?? new JObject();

            var call = new UnaiToolCall
            {
                Id = $"mcp_{Guid.NewGuid():N}".Substring(0, 24),
                ToolName = tool.Definition.Name,
                ArgumentsJson = arguments.ToString(Formatting.None)
            };

            Debug.Log($"[UNAI MCP] Calling tool '{toolName}' with args: {call.ArgumentsJson}");

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

        private static string MakeError(JToken id, int code, string message)
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
