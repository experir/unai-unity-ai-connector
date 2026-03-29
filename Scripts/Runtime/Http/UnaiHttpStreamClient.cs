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
            int timeoutSeconds = 30)
        {
            var tcs = new TaskCompletionSource<bool>();

            UnaiCoroutineRunner.Run(StreamCoroutine(
                url, jsonBody, headers, lineParser,
                onDelta, onComplete, onError,
                cancellationToken, tcs, timeoutSeconds));

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
            TaskCompletionSource<bool> tcs,
            int timeoutSeconds)
        {
            UnaiLogger.LogRawJson("STREAM_REQUEST", url, jsonBody);

            string lastAccumulatedContent = null;
            bool hasError = false;

            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.uploadHandler.contentType = "application/json";
            //request.uploadHandler.contentType = "application/json; charset=utf-8";

            if (timeoutSeconds > 0)
                request.timeout = timeoutSeconds;

            var downloadHandler = new UnaiStreamingDownloadHandler(
                onLineReceived: line =>
                {
                    // 显式用 UTF-8 解码响应行，避免中文响应乱码
                    if (line != null)
                        line = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(line));

                    
                    // 不做任何额外转码，line 已经是正确的 UTF-8 字符串
                    var delta = lineParser.ProcessLine(line);
                    if (delta != null)
                    {
                        if (delta.AccumulatedContent != null)
                            lastAccumulatedContent = delta.AccumulatedContent;
                        onDelta?.Invoke(delta);
                    }
                },
                onComplete: () => { }
            );
            request.downloadHandler = downloadHandler;

            if (headers != null)
            {
                foreach (var kvp in headers)
                    request.SetRequestHeader(kvp.Key, kvp.Value);
            }

            request.SetRequestHeader("Accept", "text/event-stream");
            // 明确告知服务端客户端接受 UTF-8 编码的响应
            // request.SetRequestHeader("Accept-Charset", "utf-8");

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    var cancelError = new UnaiErrorInfo
                    {
                        ErrorType = UnaiErrorType.Cancelled,
                        Message = "Stream request was cancelled.",
                    };
                    onError?.Invoke(cancelError);
                    tcs.TrySetCanceled();
                    yield break;
                }
                yield return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                var cancelError = new UnaiErrorInfo
                {
                    ErrorType = UnaiErrorType.Cancelled,
                    Message = "Stream request was cancelled after completion.",
                };
                onError?.Invoke(cancelError);
                tcs.TrySetCanceled();
                yield break;
            }

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                hasError = true;
                var errorInfo = UnaiHttpClient.CreateErrorFromRequest(request);
                UnaiLogger.LogRawJson($"STREAM_ERROR [{(int)request.responseCode}]", url, errorInfo.RawResponse);
                onError?.Invoke(errorInfo);
                tcs.TrySetException(new UnaiRequestException(errorInfo));
            }

            if (!hasError)
            {
                UnaiLogger.LogRawJson("STREAM_COMPLETE", url, lastAccumulatedContent);
                onComplete?.Invoke();
                tcs.TrySetResult(true);
            }
        }
    }
}