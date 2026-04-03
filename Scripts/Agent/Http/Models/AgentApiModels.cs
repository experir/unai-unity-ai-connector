using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnAI.Tools;

namespace UnAI.Agent
{
    [Serializable]
    public class AgentApiRequest
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("mcpTools")]
        public List<McpToolDefinition> McpTools { get; set; }

        [JsonProperty("config")]
        public AgentApiConfig Config { get; set; }

        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }
    }

    [Serializable]
    public class AgentApiConfig
    {
        [JsonProperty("maxSteps")]
        public int MaxSteps { get; set; } = 10;

        [JsonProperty("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 300;

        [JsonProperty("systemPrompt")]
        public string SystemPrompt { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }
    }

    [Serializable]
    public class McpToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JObject InputSchema { get; set; }

        public UnaiToolDefinition ToUnaiToolDefinition()
        {
            return new UnaiToolDefinition
            {
                Name = Name,
                Description = Description,
                ParametersSchema = InputSchema
            };
        }
    }

    [Serializable]
    public class AgentApiResponse
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("stopReason")]
        public string StopReason { get; set; }

        [JsonProperty("steps")]
        public List<AgentStepInfo> Steps { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }

    [Serializable]
    public class AgentStepInfo
    {
        [JsonProperty("stepNumber")]
        public int StepNumber { get; set; }

        [JsonProperty("toolCalls")]
        public List<AgentToolCallInfo> ToolCalls { get; set; }

        [JsonProperty("toolResults")]
        public List<AgentToolResultInfo> ToolResults { get; set; }

        [JsonProperty("durationMs")]
        public float DurationMs { get; set; }

        [JsonProperty("isFinal")]
        public bool IsFinal { get; set; }
    }

    [Serializable]
    public class AgentToolCallInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public string Arguments { get; set; }
    }

    [Serializable]
    public class AgentToolResultInfo
    {
        [JsonProperty("toolCallId")]
        public string ToolCallId { get; set; }

        [JsonProperty("toolName")]
        public string ToolName { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("isError")]
        public bool IsError { get; set; }
    }

    public static class AgentSseEvents
    {
        public const string Thinking = "thinking";
        public const string ToolCall = "tool_call";
        public const string ToolResult = "tool_result";
        public const string Delta = "delta";
        public const string Complete = "complete";
        public const string Error = "error";
    }

    [Serializable]
    public class SseThinkingEvent
    {
        [JsonProperty("step")]
        public int Step { get; set; }

        [JsonProperty("messageCount")]
        public int MessageCount { get; set; }

        [JsonProperty("estimatedTokens")]
        public int EstimatedTokens { get; set; }
    }

    [Serializable]
    public class SseToolCallEvent
    {
        [JsonProperty("step")]
        public int Step { get; set; }

        [JsonProperty("tool")]
        public string Tool { get; set; }

        [JsonProperty("args")]
        public JObject Args { get; set; }
    }

    [Serializable]
    public class SseToolResultEvent
    {
        [JsonProperty("step")]
        public int Step { get; set; }

        [JsonProperty("tool")]
        public string Tool { get; set; }

        [JsonProperty("result")]
        public string Result { get; set; }

        [JsonProperty("isError")]
        public bool IsError { get; set; }
    }

    [Serializable]
    public class SseDeltaEvent
    {
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("step")]
        public int Step { get; set; }
    }

    [Serializable]
    public class SseCompleteEvent
    {
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("stopReason")]
        public string StopReason { get; set; }
    }

    [Serializable]
    public class SseErrorEvent
    {
        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
