using System;

namespace UnAI.Config
{
    public static class UnaiEnvironment
    {
        public static string GetApiKey(string envVarName, string fallback = "")
        {
            if (!string.IsNullOrEmpty(envVarName))
            {
                string envValue = Environment.GetEnvironmentVariable(envVarName);
                if (!string.IsNullOrEmpty(envValue))
                    return envValue;
            }
            return fallback ?? "";
        }
    }
}
