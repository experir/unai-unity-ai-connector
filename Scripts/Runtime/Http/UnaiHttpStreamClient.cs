using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Utilities;
using UnityEngine.Networking;

namespace UnAI.Http
{
    public static class UnaiHttpStreamClient
    {
        public static Task StreamPostAsync(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            ISseLineParser lineParser,
            Action<UnaiStreamDelta> onDelta,
            Action onComplete,
            Action<UnaiErrorInfo> onError,
            CancellationToken cancellationToken,
            int timeoutSeconds = 120)
        {
            var tcs = new TaskCompletionSource<bool>();

            UnaiCoroutineRunner.Run(UnaiCoroutineRunner.Guard(StreamCoroutine(
                url, jsonBody, headers, lineParser,
                onDelta, onComplete, onError,
                cancellationToken, timeoutSeconds, tcs),
                ex => tcs.TrySetException(ex), () => tcs.TrySetCanceled()));

            return tcs.Task;
        }

        private static IEnumerator StreamCoroutine(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            ISseLineParser lineParser,
            Action<UnaiStreamDelta> onDelta,
            Action onComplete,
            Action<UnaiErrorInfo> onError,
            CancellationToken cancellationToken,
            int timeoutSeconds,
            TaskCompletionSource<bool> tcs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled();
                yield break;
            }
            UnaiLogger.LogRawJson("STREAM_REQUEST", url, jsonBody);

            string lastAccumulatedContent = null;

            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.uploadHandler.contentType = "application/json";
            request.timeout = timeoutSeconds;

            var downloadHandler = new UnaiStreamingDownloadHandler(
                onLineReceived: line =>
                {
                    var delta = lineParser.ProcessLine(line);
                    if (delta != null)
                    {
                        if (delta.AccumulatedContent != null)
                            lastAccumulatedContent = delta.AccumulatedContent;
                        onDelta?.Invoke(delta);
                    }
                },
                onComplete: () =>
                {
                    if (lineParser is IUnaiStreamCompletionParser completionParser)
                    {
                        var delta = completionParser.Complete();
                        if (delta != null)
                        {
                            lastAccumulatedContent = delta.AccumulatedContent;
                            onDelta?.Invoke(delta);
                        }
                    }
                }
            );
            request.downloadHandler = downloadHandler;

            if (headers != null)
            {
                foreach (var kvp in headers)
                    request.SetRequestHeader(kvp.Key, kvp.Value);
            }

            request.SetRequestHeader("Accept", "text/event-stream");

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    tcs.TrySetCanceled();
                    yield break;
                }
                yield return null;
            }

            if (downloadHandler.ProcessingError != null)
            {
                onError?.Invoke(new UnaiErrorInfo
                {
                    ErrorType = UnaiErrorType.StreamingError,
                    Message = "Could not process streaming response: " + downloadHandler.ProcessingError.Message,
                    InnerException = downloadHandler.ProcessingError
                });
                tcs.TrySetResult(false);
            }
            else if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                var errorInfo = UnaiHttpClient.CreateErrorFromRequest(request);
                UnaiLogger.LogRawJson($"STREAM_ERROR [{(int)request.responseCode}]", url, errorInfo.RawResponse);
                onError?.Invoke(errorInfo);
                tcs.TrySetResult(false);
            }
            else
            {
                UnaiLogger.LogRawJson("STREAM_COMPLETE", url, lastAccumulatedContent);
                onComplete?.Invoke();
                tcs.TrySetResult(true);
            }
        }
    }
}
