using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Config;
using UnAI.Http;
using UnAI.Models;
using UnAI.Utilities;
using UnityEngine;

namespace UnAI.Core
{
    /// <summary>
    /// Wakes up local model servers (Ollama, LM Studio, llama.cpp). Before a request, providers call
    /// <see cref="EnsureRunningAsync"/>: if the server on this machine doesn't answer and
    /// <see cref="UnaiProviderConfig.AutoStartServer"/> is on, it runs the start command and waits until
    /// the server answers. It never downloads models or software.
    /// </summary>
    public static class UnaiLocalServer
    {
        /// <summary>How long to wait for a started server to answer.</summary>
        public static float StartTimeoutSeconds = 30f;

        private static readonly Dictionary<string, string> DisplayNames = new()
        {
            ["ollama"] = "Ollama", ["lmstudio"] = "LM Studio", ["llamacpp"] = "llama.cpp"
        };
        private static readonly Dictionary<string, SemaphoreSlim> Gates = new();
        private static readonly Dictionary<string, float> LastSeenRunning = new();
        private const float RecheckAfterSeconds = 10f;

        public static bool IsLocalProvider(string providerId) =>
            providerId != null && DisplayNames.ContainsKey(providerId);

        /// <summary>True when the base URL points at this machine, the only place a server can be started.</summary>
        public static bool IsOnThisMachine(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
            return uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Starting processes is possible in the Editor and in desktop players.</summary>
        public static bool CanStartProcesses =>
            Application.isEditor || Application.platform is RuntimePlatform.WindowsPlayer
                or RuntimePlatform.OSXPlayer or RuntimePlatform.LinuxPlayer;

        /// <summary>True when something answers at the provider's base URL (any HTTP status).</summary>
        public static async Task<bool> IsRunningAsync(UnaiProviderConfig config, CancellationToken ct = default)
        {
            var (_, statusCode, error) = await UnaiHttpClient.GetAsync(config.BaseUrl, null, ct, timeoutSeconds: 3);
            return statusCode > 0 || error == null || error.ErrorType != UnaiErrorType.Network;
        }

        /// <summary>
        /// Makes sure a local provider's server is running, starting it if needed and allowed.
        /// Returns null when the request can go ahead; otherwise an error explaining what to do.
        /// </summary>
        public static async Task<UnaiErrorInfo> EnsureRunningAsync(UnaiProviderConfig config, CancellationToken ct = default)
        {
            string id = config?.ProviderId;
            if (!IsLocalProvider(id) || !config.AutoStartServer || !IsOnThisMachine(config.BaseUrl)) return null;
            if (LastSeenRunning.TryGetValue(id, out float seen) && Time.realtimeSinceStartup - seen < RecheckAfterSeconds)
                return null;

            if (!Gates.TryGetValue(id, out var gate)) Gates[id] = gate = new SemaphoreSlim(1, 1);
            await gate.WaitAsync(ct);
            try
            {
                if (await IsRunningAsync(config, ct)) return MarkRunning(id);
                if (!CanStartProcesses)
                    return Error(id, $"{DisplayNames[id]} isn't running at {config.BaseUrl}, and this platform can't start it. Start it on this device first.");

                var command = ResolveCommand(config);
                if (command == null)
                    return Error(id, id == "llamacpp"
                        ? $"llama.cpp isn't running at {config.BaseUrl}. Set Server Start Command in the UnAI config (for example: llama-server -m path/to/model.gguf --port 8080), or start it yourself."
                        : $"{DisplayNames[id]} isn't running at {config.BaseUrl}, and UnAI couldn't find it to start it. Start it yourself, or set Server Start Command in the UnAI config.");

                UnaiLogger.Log($"[UNAI] {DisplayNames[id]} isn't running. Starting it: {command.Value.file} {command.Value.args}");
                try
                {
                    Process.Start(new ProcessStartInfo(command.Value.file, command.Value.args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    return Error(id, $"Couldn't start {DisplayNames[id]} ({command.Value.file}): {ex.Message}");
                }

                float deadline = Time.realtimeSinceStartup + StartTimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    await Task.Delay(500, ct);
                    if (await IsRunningAsync(config, ct))
                    {
                        UnaiLogger.Log($"[UNAI] {DisplayNames[id]} is running.");
                        return MarkRunning(id);
                    }
                }
                return Error(id, $"Started {DisplayNames[id]}, but it didn't answer at {config.BaseUrl} within {StartTimeoutSeconds:0} seconds.");
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>The executable and arguments that start the server, or null when none is known or found.</summary>
        public static (string file, string args)? ResolveCommand(UnaiProviderConfig config)
        {
            string command = config.ServerStartCommand?.Trim();
            if (string.IsNullOrEmpty(command))
            {
                command = config.ProviderId switch
                {
                    "ollama" => "ollama serve",
                    "lmstudio" => "lms server start",
                    _ => null
                };
                if (command == null) return null;
            }

            var (file, args) = SplitCommand(command);
            string resolved = FindExecutable(file);
            return resolved == null ? null : (resolved, args);
        }

        internal static (string file, string args) SplitCommand(string command)
        {
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int end = command.IndexOf('"', 1);
                if (end > 0) return (command.Substring(1, end - 1), command.Substring(end + 1).Trim());
            }
            int space = command.IndexOf(' ');
            return space < 0 ? (command, "") : (command.Substring(0, space), command.Substring(space + 1).Trim());
        }

        private static string FindExecutable(string file)
        {
            if (Path.IsPathRooted(file)) return File.Exists(file) ? file : null;

            bool windows = Application.platform is RuntimePlatform.WindowsEditor or RuntimePlatform.WindowsPlayer;
            var names = windows && !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new[] { file + ".exe", file } : new[] { file };

            var folders = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // Default install locations, for when the Editor was started without them on PATH.
            folders.AddRange(new[]
            {
                Path.Combine(localAppData, "Programs", "Ollama"),
                "/usr/local/bin", "/opt/homebrew/bin", "/usr/bin",
                "/Applications/Ollama.app/Contents/Resources",
                Path.Combine(home, ".lmstudio", "bin"),
                Path.Combine(home, ".cache", "lm-studio", "bin")
            });

            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                foreach (string name in names)
                {
                    try
                    {
                        string candidate = Path.Combine(folder.Trim('"'), name);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }
            return null;
        }

        private static UnaiErrorInfo MarkRunning(string id)
        {
            LastSeenRunning[id] = Time.realtimeSinceStartup;
            return null;
        }

        private static UnaiErrorInfo Error(string id, string message) => new()
        {
            ErrorType = UnaiErrorType.LocalServerUnavailable,
            Message = message,
            ProviderId = id
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => LastSeenRunning.Clear();
    }
}
