using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Models;
using UnAI.Utilities;
using UnityEngine.Networking;

namespace UnAI.Http
{
    public static class UnaiHttpClient
    {
        public static Task<(string body, int statusCode, UnaiErrorInfo error)> PostAsync(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = 120)
        {
            var tcs = new TaskCompletionSource<(string, int, UnaiErrorInfo)>();
            UnaiCoroutineRunner.Run(PostCoroutine(url, jsonBody, headers, cancellationToken, timeoutSeconds, tcs));
            return tcs.Task;
        }

        private static IEnumerator PostCoroutine(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            CancellationToken cancellationToken,
            int timeoutSeconds,
            TaskCompletionSource<(string, int, UnaiErrorInfo)> tcs)
        {
            UnaiLogger.LogRawJson("REQUEST", url, jsonBody);

            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.uploadHandler.contentType = "application/json";
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;

            if (headers != null)
            {
                foreach (var kvp in headers)
                    request.SetRequestHeader(kvp.Key, kvp.Value);
            }

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

            int statusCode = (int)request.responseCode;
            string responseBody = request.downloadHandler?.text;

            UnaiLogger.LogRawJson($"RESPONSE [{statusCode}]", url, responseBody);

            if (request.result != UnityWebRequest.Result.Success)
            {
                var error = CreateErrorFromRequest(request);
                tcs.TrySetResult((responseBody, statusCode, error));
            }
            else
            {
                tcs.TrySetResult((responseBody, statusCode, null));
            }
        }

        internal static UnaiErrorInfo CreateErrorFromRequest(UnityWebRequest request)
        {
            int statusCode = (int)request.responseCode;
            var errorType = statusCode switch
            {
                401 or 403 => UnaiErrorType.Authentication,
                429 => UnaiErrorType.RateLimit,
                400 => UnaiErrorType.InvalidRequest,
                >= 500 => UnaiErrorType.ServerError,
                _ => request.result == UnityWebRequest.Result.ConnectionError
                    ? UnaiErrorType.Network
                    : UnaiErrorType.Unknown
            };

            // Parse Retry-After header (seconds or HTTP-date)
            float? retryAfter = null;
            string retryHeader = request.GetResponseHeader("Retry-After");
            if (!string.IsNullOrEmpty(retryHeader))
            {
                if (float.TryParse(retryHeader, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float secs))
                {
                    retryAfter = secs;
                }
                else if (System.DateTime.TryParse(retryHeader,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
                {
                    retryAfter = (float)(date.ToUniversalTime() - System.DateTime.UtcNow).TotalSeconds;
                    if (retryAfter < 0) retryAfter = 0;
                }
            }

            return new UnaiErrorInfo
            {
                ErrorType = errorType,
                Message = request.error,
                HttpStatusCode = statusCode,
                RawResponse = request.downloadHandler?.text,
                RetryAfterSeconds = retryAfter
            };
        }
    }
}
