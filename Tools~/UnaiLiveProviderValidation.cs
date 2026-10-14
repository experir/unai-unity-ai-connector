// Explicit live checks; copy into Assets/Editor of an isolated validation project.
// Only providers named in UNAI_LIVE_PROVIDERS are contacted. No keys are written to reports.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnAI.Tools;
using UnityEditor;
using UnityEngine;

public static class UnaiLiveProviderValidation
{
    public static async void Run()
    {
        try { await RunAsync(); }
        catch (Exception ex)
        {
            Debug.LogError($"[UNAI live] Runner failed ({ex.GetType().Name}). Check report path and configuration.");
            EditorApplication.Exit(1);
        }
    }

    private static async Task RunAsync()
    {
        string path = Environment.GetEnvironmentVariable("UNAI_LIVE_REPORT");
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogError("[UNAI live] Set UNAI_LIVE_REPORT before running live checks.");
            EditorApplication.Exit(1);
            return;
        }
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var results = new JArray();
        int failures = 0;
        foreach (string id in (Environment.GetEnvironmentVariable("UNAI_LIVE_PROVIDERS") ?? "").Split(','))
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var checks = new JObject { ["provider"] = id.Trim() };
            results.Add(checks);
            try { await Check(id.Trim(), checks); checks["result"] = "passed"; }
            catch (Exception ex)
            {
                failures++;
                checks["result"] = "failed";
                // Do not persist provider bodies/exception messages that could include credentials.
                checks["errorType"] = ex.GetType().Name;
                Debug.LogError($"[UNAI live] {id}: failed ({ex.GetType().Name}).");
            }
        }
        File.WriteAllText(path, new JObject { ["unity"] = Application.unityVersion, ["checks"] = results }.ToString());
        Debug.Log($"[UNAI live] Completed {results.Count} provider(s), {failures} failure(s).");
        EditorApplication.Exit(failures == 0 && results.Count > 0 ? 0 : 1);
    }

    private static UnaiChatRequest Request(string prompt) => new()
    {
        Messages = new() { UnaiChatMessage.User(prompt) },
        Options = new UnaiRequestOptions { MaxTokens = TokenLimit() }
    };

    private static int TokenLimit()
    {
        string value = Environment.GetEnvironmentVariable("UNAI_LIVE_MAX_TOKENS");
        int limit = string.IsNullOrEmpty(value) ? 1024 : int.Parse(value);
        if (limit < 128 || limit > 4096) throw new ArgumentOutOfRangeException("UNAI_LIVE_MAX_TOKENS", "Use 128 to 4096 tokens.");
        return limit;
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException(check);
    }

    private static async Task Check(string id, JObject checks)
    {
        string key = id switch { "openai" => "OPENAI_API_KEY", "anthropic" => "ANTHROPIC_API_KEY", "gemini" => "GEMINI_API_KEY", "ollama" => null,
            _ => throw new ArgumentException("Supported live providers: openai, anthropic, gemini, ollama.") };
        if (key != null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            throw new InvalidOperationException("Required key is not configured.");
        string model = Environment.GetEnvironmentVariable("UNAI_LIVE_" + id.ToUpperInvariant() + "_MODEL");
        Require(!string.IsNullOrEmpty(model), "Explicit live model is required.");
        string url = id switch { "openai" => "https://api.openai.com", "anthropic" => "https://api.anthropic.com",
            "gemini" => "https://generativelanguage.googleapis.com", _ => "http://localhost:11434" };
        var type = typeof(UnaiProviderBase).Assembly.GetTypes().First(t => !t.IsAbstract &&
            typeof(UnaiProviderBase).IsAssignableFrom(t) && ((UnaiProviderBase)Activator.CreateInstance(t)).ProviderId == id);
        var provider = (UnaiProviderBase)Activator.CreateInstance(type);
        provider.Initialize(new UnaiProviderConfig { ProviderId = id, BaseUrl = url, DefaultModel = model,
            ApiKeyEnvironmentVariable = key, MaxRetries = 0, TimeoutSeconds = 90 });
        checks["model"] = model;
        UnaiChatRequest MakeRequest(string prompt)
        {
            var request = Request(prompt);
            if (id == "ollama") request.Options.Temperature = 0;
            return request;
        }
        checks["stage"] = "discovery";
        var models = await ((IUnaiModelProvider)provider).ListModelsAsync();
        Require(models.Count > 0, "Model discovery returned no models.");
        checks["discovery"] = "passed";

        checks["stage"] = "chat";
        var reply = await provider.ChatAsync(MakeRequest("Reply with exactly UNAI_OK. /no_think"));
        Require(!string.IsNullOrWhiteSpace(reply.Content), "Chat returned no visible text.");
        checks["chat"] = "passed";
        checks["chatUsagePresent"] = reply.Usage != null;

        UnaiChatResponse streamed = null;
        UnaiErrorInfo error = null;
        var content = new StringBuilder();
        checks["stage"] = "stream";
        await provider.ChatStreamAsync(MakeRequest("Reply with exactly UNAI_STREAM_OK. /no_think"),
            delta => content.Append(delta.Content), response => streamed = response, info => error = info);
        Require(error == null && streamed != null && content.Length > 0 && content.ToString() == streamed.Content,
            "Stream did not settle with matching content.");
        checks["stream"] = "passed";
        checks["streamUsagePresent"] = streamed.Usage != null;

        checks["stage"] = "nativeTools";
        var toolRequest = MakeRequest("Use unai_echo once with value UNAI_TOOL_OK. After the tool returns, answer with its result. /no_think");
        toolRequest.Tools = new List<UnaiToolDefinition> { new() {
            Name = "unai_echo", Description = "Echo a required test string.", IsReadOnly = true,
            ParametersSchema = JObject.Parse("{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[\"value\"]}")
        }};
        var tools = await provider.ChatAsync(toolRequest);
        checks["toolFinishReason"] = tools.FinishReason;
        Require(tools.HasToolCalls && tools.ToolCalls[0].ToolName == "unai_echo", "Native tool call missing.");
        JObject.Parse(tools.ToolCalls[0].ArgumentsJson);
        checks["stage"] = "toolHistory";
        var history = MakeRequest("unused");
        history.Messages = new(toolRequest.Messages)
        {
            UnaiChatMessage.AssistantWithToolCalls(tools.ToolCalls, tools.Content),
            UnaiChatMessage.ToolResult(tools.ToolCalls[0].Id, "unai_echo", "UNAI_TOOL_OK")
        };
        history.Tools = toolRequest.Tools;
        var followup = await provider.ChatAsync(history);
        Require(followup.HasToolCalls || !string.IsNullOrWhiteSpace(followup.Content), "Tool history produced no response.");
        checks["toolHistoryFollowup"] = followup.HasToolCalls ? "toolCalls" : "text";
        checks["toolRoundTrip"] = "passed";

        UnaiChatResponse streamedTools = null;
        error = null;
        checks["stage"] = "streamingTools";
        await provider.ChatStreamAsync(toolRequest, _ => { }, response => streamedTools = response, info => error = info);
        Require(error == null && streamedTools?.HasToolCalls == true, "Streaming API did not return tool calls.");
        checks["streamingTools"] = "passed";
        checks["toolResponseWasStreamed"] = streamedTools.WasStreamed;

        using var cancel = new CancellationTokenSource();
        int deltas = 0;
        bool cancelled = false;
        // Even a fully buffered short reply must honor cancellation from its first visible delta.
        checks["stage"] = "cancellation";
        var longRequest = MakeRequest("Reply with exactly UNAI_CANCEL_OK. /no_think");
        try
        {
            await provider.ChatStreamAsync(longRequest, delta => { if (!string.IsNullOrEmpty(delta.Content)) { deltas++; cancel.Cancel(); } },
                null, info => { if (info.ErrorType == UnaiErrorType.Cancelled) cancelled = true; }, cancel.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        checks["cancellationDeltas"] = deltas;
        checks["cancellationObserved"] = cancelled;
        Require(deltas > 0 && cancelled, "Cancellation did not settle after a visible delta.");
        checks["cancellation"] = "passed";
        checks["stage"] = "complete";
    }
}
