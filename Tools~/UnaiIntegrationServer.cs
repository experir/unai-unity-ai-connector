// Copy into Assets/Editor of an isolated full-module validation project only.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnAI.Editor.Assistant;
using UnAI.MCP;
using UnAI.Tools;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class UnaiIntegrationServer
{
    private const string StateKey = "Unai.Integration.Directory";
    private static UnaiMcpServer _server;
    private static string _directory;

    static UnaiIntegrationServer()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(StateKey, "")))
            EditorApplication.delayCall += Resume;
    }

    public static void Start()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-unaiIntegrationDir");
        if (index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Supply -unaiIntegrationDir with a temporary client directory.");
        _directory = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(_directory);
        SessionState.SetString(StateKey, _directory);
        Resume();
    }

    private static void Resume()
    {
        if (_server?.IsRunning == true) return;
        _directory = SessionState.GetString(StateKey, "");
        if (string.IsNullOrEmpty(_directory)) return;
        int port = SessionState.GetInt(StateKey + ".Port", 0);
        if (port == 0)
        {
            var finder = new TcpListener(IPAddress.Loopback, 0);
            finder.Start(); port = ((IPEndPoint)finder.LocalEndpoint).Port; finder.Stop();
            SessionState.SetInt(StateKey + ".Port", port);
        }
        var tools = UnaiAssistantToolsFactory.CreateEditorToolRegistry();
        tools.Register(new ProbeTool(false));
        tools.Register(new ProbeTool(true));
        _server = new UnaiMcpServer();
        _server.Start(port, tools);
        if (!_server.IsRunning) throw new InvalidOperationException("Integration MCP server did not start.");
        int generation = SessionState.GetInt(StateKey + ".Generation", 0) + 1;
        SessionState.SetInt(StateKey + ".Generation", generation);
        var state = new JObject
        {
            ["url"] = _server.Url, ["token"] = _server.AccessToken,
            ["generation"] = generation, ["unity"] = Application.unityVersion
        };
        File.WriteAllText(Path.Combine(_directory, "server.json"), state.ToString());
        File.WriteAllText(Path.Combine(_directory, ".mcp.json"), new JObject
        {
            ["mcpServers"] = new JObject { ["unity"] = new JObject
            {
                ["type"] = "http", ["url"] = _server.Url,
                ["headers"] = new JObject { ["Authorization"] = "Bearer " + _server.AccessToken }
            }}
        }.ToString());
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Debug.Log($"[UNAI integration] Ready, generation {generation}. Client credentials are in the temporary directory.");
    }

    private static void Poll()
    {
        string control = Path.Combine(_directory, "control.json");
        if (!File.Exists(control)) return;
        var command = JObject.Parse(File.ReadAllText(control));
        File.Delete(control);
        switch (command["action"]?.ToString())
        {
            case "permissions": _server.AllowMutatingTools = command["allowMutating"].Value<bool>(); break;
            case "recompile":
                File.WriteAllText("Assets/Editor/UnaiRecompileTrigger.cs",
                    "internal static class UnaiRecompileTrigger { public const string Revision = \"" + Guid.NewGuid().ToString("N") + "\"; }");
                AssetDatabase.ImportAsset("Assets/Editor/UnaiRecompileTrigger.cs", ImportAssetOptions.ForceUpdate);
                break;
            case "stop":
                SessionState.EraseString(StateKey);
                _server.Stop();
                EditorApplication.Exit(0);
                break;
        }
    }

    private sealed class ProbeTool : IUnaiTool
    {
        private readonly bool _mutating;
        public ProbeTool(bool mutating) { _mutating = mutating; }
        public UnaiToolDefinition Definition => new()
        {
            Name = _mutating ? "integration_mutate" : "integration_wait",
            Description = "Isolated integration probe; not a shipping editor tool.",
            IsReadOnly = !_mutating,
            ParametersSchema = new JObject { ["type"] = "object" }
        };
        public async Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
        {
            if (!_mutating) await Task.Delay(TimeSpan.FromSeconds(20), ct);
            return new UnaiToolResult { Content = _mutating ? "mutation probe permitted" : "completed" };
        }
    }
}
