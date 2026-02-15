using System;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Models;

namespace UnAI.Utilities
{
    public static class UnaiRetryPolicy
    {
        public static async Task<(string body, int statusCode, UnaiErrorInfo error)> ExecuteAsync(
            Func<Task<(string body, int statusCode, UnaiErrorInfo error)>> action,
            int maxRetries,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await action();

                if (result.error == null || !result.error.IsRetryable || attempt >= maxRetries)
                    return result;

                attempt++;
                float delaySeconds;
                if (result.error.RetryAfterSeconds.HasValue)
                {
                    // Respect server-specified Retry-After with small jitter
                    delaySeconds = result.error.RetryAfterSeconds.Value + UnityEngine.Random.Range(0.1f, 1f);
                }
                else if (result.error.ErrorType == UnaiErrorType.RateLimit)
                {
                    // Rate limits need longer waits (5s, 10s, 20s base)
                    delaySeconds = 5f * MathF.Pow(2, attempt - 1) + UnityEngine.Random.Range(0f, 2f);
                }
                else
                {
                    delaySeconds = MathF.Pow(2, attempt - 1) + UnityEngine.Random.Range(0f, 0.5f);
                }

                UnaiLogger.Log($"[UNAI] Retrying request (attempt {attempt}/{maxRetries}) after {delaySeconds:F1}s: {result.error.Message}");

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }
    }
}
