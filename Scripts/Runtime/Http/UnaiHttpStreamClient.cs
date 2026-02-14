using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Models;
using UnAI.Streaming;
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
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            UnaiCoroutineRunner.Run(StreamCoroutine(
                url, jsonBody, headers, lineParser,
                onDelta, onComplete, onError,
                cancellationToken, tcs));

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
            TaskCompletionSource<bool> tcs)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.uploadHandler.contentType = "application/json";

            var downloadHandler = new UnaiStreamingDownloadHandler(
                onLineReceived: line =>
                {
                    var delta = lineParser.ProcessLine(line);
                    if (delta != null)
                        onDelta?.Invoke(delta);
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

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                var errorInfo = UnaiHttpClient.CreateErrorFromRequest(request);
                onError?.Invoke(errorInfo);
                tcs.TrySetResult(false);
            }
            else
            {
                onComplete?.Invoke();
                tcs.TrySetResult(true);
            }
        }
    }
}
