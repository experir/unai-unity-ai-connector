using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Agent;
using UnAI.Tools;

namespace UnAI.MCP
{
    // ── JSON-RPC Models ────────────────────────────────────────────────────────

    public class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public JToken Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Params { get; set; }
    }

    public class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public JToken Id { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public JsonRpcErrorInfo Error { get; set; }
    }

    public class JsonRpcErrorInfo
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public static class JsonRpcError
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }

    // ── MCP Protocol Models ────────────────────────────────────────────────────

    public class McpInitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public McpCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public McpServerInfo ServerInfo { get; set; }

        [JsonProperty("instructions", NullValueHandling = NullValueHandling.Ignore)]
        public string Instructions { get; set; }
    }

    public class McpServerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public class McpCapabilities
    {
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public McpToolsCapability Tools { get; set; }

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public McpResourcesCapability Resources { get; set; }

        [JsonProperty("logging", NullValueHandling = NullValueHandling.Ignore)]
        public McpLoggingCapability Logging { get; set; }
    }

    public class McpToolsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class McpResourcesCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }

        [JsonProperty("subscribe")]
        public bool Subscribe { get; set; }
    }

    public class McpLoggingCapability
    {
    }

    public class McpToolsListResult
    {
        [JsonProperty("tools")]
        public List<McpToolDefinition> Tools { get; set; } = new List<McpToolDefinition>();
    }

    public class McpToolCallResult
    {
        [JsonProperty("content")]
        public List<McpContent> Content { get; set; } = new List<McpContent>();

        [JsonProperty("isError", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsError { get; set; }
    }

    public class McpContent
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class McpResourcesListResult
    {
        [JsonProperty("resources")]
        public List<McpResourceDefinition> Resources { get; set; } = new List<McpResourceDefinition>();
    }

    public class McpResourceDefinition
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }
    }

    public class McpResourceReadResult
    {
        [JsonProperty("contents")]
        public List<McpResourceContent> Contents { get; set; } = new List<McpResourceContent>();
    }

    public class McpResourceContent
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class McpToolCallParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Arguments { get; set; }
    }

    public class McpResourceReadParams
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }
    }
}
