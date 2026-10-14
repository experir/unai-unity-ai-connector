// Copy into Assets of a disposable Core validation project. No cloud providers or keys.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnAI.Config;
using UnAI.Models;
using UnAI.Providers.Ollama;
using UnityEngine;

public sealed class UnaiBrowserValidation : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void UNAI_ValidationReport(string report);
#endif
    private readonly JArray _checks = new();
    private bool _running;
    private string _status = "ready";

    private void Start() => Publish();

    private void Publish()
    {
        string json = new JObject { ["unity"] = Application.unityVersion,
            ["package"] = UnAI.UnaiVersion.Current, ["platform"] = Application.platform.ToString(),
            ["status"] = _status, ["checks"] = _checks }.ToString();
#if UNITY_WEBGL && !UNITY_EDITOR
        UNAI_ValidationReport(json);
#else
        Debug.Log(json);
#endif
    }

    public async void Run(string unused)
    {
        if (_running) return;
        _running = true;
        _checks.Clear();
        _status = "running";
        Publish();
        await Check("ollama_chat_cors", async result =>
        {
            var response = await Provider("http://localhost:11434").ChatAsync(Request("Reply with UNAI_BROWSER_OK. /no_think"));
            Require(!string.IsNullOrWhiteSpace(response.Content), "Empty chat reply");
            Require(response.Usage != null, "Missing chat usage");
            result["totalTokens"] = response.Usage.TotalTokens;
        });
        await Check("ollama_stream_cors", async result =>
        {
            var content = new StringBuilder();
            UnaiChatResponse response = null;
            UnaiErrorInfo error = null;
            await Provider("http://localhost:11434").ChatStreamAsync(Request("Reply with UNAI_BROWSER_STREAM_OK. /no_think"),
                delta => content.Append(delta.Content), reply => response = reply, failure => error = failure);
            Require(error == null && response != null && content.Length > 0 && response.Content == content.ToString(), "Stream content/settlement failed");
            Require(response.Usage != null, "Missing streamed usage");
            result["totalTokens"] = response.Usage.TotalTokens;
        });
        await Check("slow_stream_delivery", async result =>
        {
            double start = Time.realtimeSinceStartupAsDouble;
            double first = -1;
            var content = new StringBuilder();
            UnaiChatResponse response = null;
            UnaiErrorInfo error = null;
            await Provider("http://localhost:8766").ChatStreamAsync(Request("fixture:slow"), delta =>
            {
                if (!string.IsNullOrEmpty(delta.Content) && first < 0) first = Time.realtimeSinceStartupAsDouble - start;
                content.Append(delta.Content);
            }, reply => response = reply, failure => error = failure);
            Require(error == null && response != null && content.ToString() == "café 😀 end", "Unicode/stream content mismatch");
            Require(response.Usage?.TotalTokens == 5 && response.FinishReason == "stop", "Final metadata missing");
            result["firstDeltaSeconds"] = first;
            result["totalSeconds"] = Time.realtimeSinceStartupAsDouble - start;
            result["delivery"] = first < 2 ? "incremental" : "buffered";
        });
        await Check("cancel_during_network", async result =>
        {
            using var cancel = new CancellationTokenSource();
            StartCoroutine(CancelAfter(cancel, 0.3f));
            bool completed = false;
            bool cancelled = false;
            double start = Time.realtimeSinceStartupAsDouble;
            try { await Provider("http://localhost:8766").ChatStreamAsync(Request("fixture:slow"), _ => { },
                _ => completed = true, cancellationToken: cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && !completed && Time.realtimeSinceStartupAsDouble - start < 2, "Cancellation did not stop the pending request");
            result["settledSeconds"] = Time.realtimeSinceStartupAsDouble - start;
        });
        await Check("cancel_from_delta", async result =>
        {
            using var cancel = new CancellationTokenSource();
            int deltas = 0;
            bool completed = false;
            bool cancelled = false;
            try { await Provider("http://localhost:8766").ChatStreamAsync(Request("fixture:slow"), delta =>
            {
                if (!string.IsNullOrEmpty(delta.Content)) { deltas++; cancel.Cancel(); }
            }, _ => completed = true, cancellationToken: cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && !completed && deltas == 1, "Buffered callback cancellation failed");
            result["deltas"] = deltas;
        });
        await Check("stream_timeout", async result =>
        {
            double start = Time.realtimeSinceStartupAsDouble;
            bool completed = false;
            UnaiErrorInfo error = null;
            await Provider("http://localhost:8766", 1).ChatStreamAsync(Request("fixture:stall"), _ => { },
                _ => completed = true, failure => error = failure);
            result["settledSeconds"] = Time.realtimeSinceStartupAsDouble - start;
            Require(error != null && !completed && Time.realtimeSinceStartupAsDouble - start < 3, "Streaming deadline was not enforced");
        });
        await Check("malformed_stream", async result =>
        {
            UnaiErrorInfo error = null;
            bool completed = false;
            await Provider("http://localhost:8766").ChatStreamAsync(Request("fixture:malformed"), _ => { },
                _ => completed = true, failure => error = failure);
            Require(error?.ErrorType == UnaiErrorType.StreamingError && !completed, "Malformed stream did not fail");
        });
        await Check("cors_denial", async result =>
        {
            bool rejected = false;
            try
            {
                var reply = await Provider("http://localhost:8766/no-cors").ChatAsync(Request("fixture:chat"));
                result["unexpectedContentLength"] = reply.Content?.Length ?? 0;
            }
            catch (UnaiRequestException ex)
            {
                result["requestErrorType"] = ex.ErrorInfo.ErrorType.ToString();
                result["httpStatusCode"] = ex.ErrorInfo.HttpStatusCode;
                rejected = ex.ErrorInfo.ErrorType == UnaiErrorType.Network;
            }
            Require(rejected, "Browser accepted the endpoint without CORS authorization");
        });
        bool failed = false;
        foreach (var check in _checks) if (check["result"]?.ToString() != "passed") failed = true;
        _status = failed ? "failed" : "passed";
        _running = false;
        Publish();
    }

    private async Task Check(string name, Func<JObject, Task> execute)
    {
        var result = new JObject { ["name"] = name };
        _checks.Add(result);
        Publish();
        try { await execute(result); result["result"] = "passed"; }
        catch (Exception ex) { result["result"] = "failed"; result["errorType"] = ex.GetType().Name; }
        Publish();
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static OllamaProvider Provider(string url, int timeout = 30)
    {
        var provider = new OllamaProvider();
        provider.Initialize(new UnaiProviderConfig { ProviderId = "ollama", BaseUrl = url,
            DefaultModel = "qwen3:0.6b", MaxRetries = 0, TimeoutSeconds = timeout });
        return provider;
    }
    private static UnaiChatRequest Request(string prompt) => new()
    {
        Messages = new List<UnaiChatMessage> { UnaiChatMessage.User(prompt) },
        Options = new UnaiRequestOptions { Temperature = 0, MaxTokens = 256 }
    };
    private static IEnumerator CancelAfter(CancellationTokenSource source, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        try { source.Cancel(); } catch (ObjectDisposedException) { }
    }
}
