using System;

namespace UnAI.Models
{
    public enum UnaiErrorType
    {
        Network,
        Authentication,
        RateLimit,
        InvalidRequest,
        ServerError,
        Cancelled,
        StreamingError,
        Unknown
    }

    [System.Serializable]
    public class UnaiErrorInfo
    {
        public UnaiErrorType ErrorType { get; set; }
        public string Message { get; set; }
        public int HttpStatusCode { get; set; }
        public string RawResponse { get; set; }
        public string ProviderId { get; set; }
        public Exception InnerException { get; set; }

        /// <summary>
        /// Seconds to wait before retrying, parsed from the Retry-After response header.
        /// Null when the header is absent.
        /// </summary>
        public float? RetryAfterSeconds { get; set; }

        public bool IsRetryable =>
            ErrorType == UnaiErrorType.RateLimit ||
            ErrorType == UnaiErrorType.ServerError ||
            ErrorType == UnaiErrorType.Network;

        /// <summary>
        /// Returns a human-readable explanation of the error suitable for display to
        /// developers using UnAI, including guidance on how to resolve the issue.
        /// </summary>
        public string UserFriendlyMessage
        {
            get
            {
                string provider = string.IsNullOrEmpty(ProviderId) ? "your AI provider" : ProviderId;
                return ErrorType switch
                {
                    UnaiErrorType.RateLimit =>
                        $"Rate limit exceeded ({provider}). This usually means your API plan has " +
                        $"insufficient quota or you're sending too many requests. Check your plan's " +
                        $"rate limits and billing at your provider's dashboard. If you're on a free tier, " +
                        $"consider upgrading or adding credits.",
                    UnaiErrorType.Authentication =>
                        $"Authentication failed ({provider}). Your API key may be invalid, expired, " +
                        $"or missing. Check your key in the UnAI configuration (Window > UnAI > Settings).",
                    UnaiErrorType.InvalidRequest =>
                        $"Invalid request sent to {provider} (HTTP {HttpStatusCode}). " +
                        $"The model name may be incorrect, or the request format is unsupported. " +
                        $"Details: {Message}",
                    UnaiErrorType.ServerError =>
                        $"{provider} server error (HTTP {HttpStatusCode}). The provider is experiencing " +
                        $"issues. This is usually temporary — try again in a moment.",
                    UnaiErrorType.Network =>
                        $"Network error: could not reach {provider}. Check your internet connection " +
                        $"and ensure the API endpoint URL is correct in UnAI settings.",
                    _ => Message ?? "An unknown error occurred."
                };
            }
        }
    }

    public class UnaiRequestException : Exception
    {
        public UnaiErrorInfo ErrorInfo { get; }

        public UnaiRequestException(UnaiErrorInfo errorInfo)
            : base(errorInfo.UserFriendlyMessage)
        {
            ErrorInfo = errorInfo;
        }

        public UnaiRequestException(UnaiErrorInfo errorInfo, Exception inner)
            : base(errorInfo.UserFriendlyMessage, inner)
        {
            ErrorInfo = errorInfo;
        }
    }
}
