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

        public bool IsRetryable =>
            ErrorType == UnaiErrorType.RateLimit ||
            ErrorType == UnaiErrorType.ServerError ||
            ErrorType == UnaiErrorType.Network;
    }

    public class UnaiRequestException : Exception
    {
        public UnaiErrorInfo ErrorInfo { get; }

        public UnaiRequestException(UnaiErrorInfo errorInfo)
            : base(errorInfo.Message)
        {
            ErrorInfo = errorInfo;
        }

        public UnaiRequestException(UnaiErrorInfo errorInfo, Exception inner)
            : base(errorInfo.Message, inner)
        {
            ErrorInfo = errorInfo;
        }
    }
}
