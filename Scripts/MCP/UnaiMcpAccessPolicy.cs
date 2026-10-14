using System;
using System.Security.Cryptography;

namespace UnAI.MCP
{
    public static class UnaiMcpAccessPolicy
    {
        public static string CreateToken()
        {
            var bytes = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public static bool IsAllowedOrigin(string origin, string serverOrigin)
        {
            // Native local clients typically omit Origin; they still need the bearer token.
            if (origin == null) return true;
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && string.IsNullOrEmpty(uri.UserInfo)
                && (uri.Scheme == "http" || uri.Scheme == "https")
                && string.Equals(origin, serverOrigin, StringComparison.Ordinal);
        }

        public static bool IsAuthorized(string authorization, string token)
        {
            if (string.IsNullOrEmpty(token) || authorization == null
                || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
            string supplied = authorization.Substring(7);
            if (supplied.Length != token.Length) return false;
            int difference = 0;
            for (int i = 0; i < token.Length; i++) difference |= supplied[i] ^ token[i];
            return difference == 0;
        }
    }
}
