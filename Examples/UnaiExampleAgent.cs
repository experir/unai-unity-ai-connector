using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TMPro;
using UnAI.Agent;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;
using UnAI.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace UnAI.Examples
{
    /// <summary>
    /// Example showing the UNAI Agent system with tool calling and multi-step reasoning.
    /// The agent can call tools, observe results, and continue reasoning until it has a final answer.
    /// </summary>
    public class UnaiExampleAgent : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private UnaiGlobalConfig _config;
        [SerializeField] private string _providerId = "openai";
        [SerializeField] private int _maxSteps = 5;

        [Header("UI References")]
        [SerializeField] private TMP_InputField _inputField;
        [SerializeField] private TMP_Text _outputText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Button _sendButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private ScrollRect _scrollRect;

        private UnaiAgent _agent;
        private CancellationTokenSource _cts;
        private bool _busy;

        private void Start()
        {
            SetupManager();
            SetupAgent();
            SetupButtons();

            _inputField.text = "Roll two dice and tell me if the total is even or odd.";
            _statusText.text = "Ready. Agent has tools: roll_dice, get_time, calculate.";
            _outputText.text = "";
        }

        private void SetupManager()
        {
            if (UnaiManager.Instance != null) return;

            var manager = GetComponent<UnaiManager>();
            if (manager == null)
                manager = gameObject.AddComponent<UnaiManager>();

            var config = _config != null ? _config : ScriptableObject.CreateInstance<UnaiGlobalConfig>();
            manager.Initialize(config);
        }

        private void SetupAgent()
        {
            // Create tool registry with example tools
            var tools = new UnaiToolRegistry();
            tools.Register(new RollDiceTool());
            tools.Register(new GetTimeTool());
            tools.Register(new CalculateTool());

            var config = new UnaiAgentConfig
            {
                ProviderId = _providerId,
                MaxSteps = _maxSteps,
                TimeoutSeconds = 60,
                UseStreaming = true,
                SystemPrompt =
                    "You are a helpful game assistant. Use the available tools to answer questions. " +
                    "Always show your reasoning and tool results."
            };

            _agent = new UnaiAgent(config, tools);

            // Wire up events for UI feedback
            _agent.OnThinking += args =>
                AppendOutput($"\n[Step {args.StepNumber}] Thinking... ({args.EstimatedTokens} est. tokens)");

            _agent.OnToolCall += args =>
                AppendOutput($"  > Calling tool: {args.ToolCall.ToolName}({args.ToolCall.ArgumentsJson})");

            _agent.OnToolResult += args =>
                AppendOutput($"  < Result: {args.Result.Content} ({args.ExecutionTimeMs:F0}ms)");

            _agent.OnStreamDelta += args =>
            {
                if (_statusText != null)
                    _statusText.text = $"Step {args.StepNumber}: streaming... {args.Delta.AccumulatedContent?.Length ?? 0} chars";
            };

            _agent.OnStepComplete += args =>
            {
                string reason = args.Step.IsFinal ? $"done ({args.Step.StopReason})" : "continuing...";
                AppendOutput($"[Step {args.Step.StepNumber}] {reason} ({args.Step.DurationMs:F0}ms)");
            };
        }

        private void SetupButtons()
        {
            _sendButton.onClick.AddListener(SendMessage);
            _cancelButton.onClick.AddListener(Cancel);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _sendButton.interactable = !_busy;
            _cancelButton.interactable = _busy;
        }

        private async void SendMessage()
        {
            string input = _inputField.text.Trim();
            if (string.IsNullOrEmpty(input) || _busy) return;

            _busy = true;
            UpdateButtons();
            _outputText.text = "";
            AppendOutput($"You: {input}\n");
            _statusText.text = "Processing...";

            _cts = new CancellationTokenSource();

            try
            {
                var step = await _agent.RunAsync(input, _cts.Token);

                AppendOutput($"\nAssistant: {step.Response.Content}");
                _statusText.text = $"Done. {step.StepNumber} step(s), {step.DurationMs:F0}ms";
            }
            catch (OperationCanceledException)
            {
                AppendOutput("\n(Cancelled)");
                _statusText.text = "Cancelled.";
            }
            catch (Exception ex)
            {
                AppendOutput($"\nError: {ex.Message}");
                _statusText.text = $"Error: {ex.GetType().Name}";
                Debug.LogException(ex);
            }
            finally
            {
                _busy = false;
                UpdateButtons();
            }
        }

        private void Cancel()
        {
            _cts?.Cancel();
        }

        private void AppendOutput(string text)
        {
            _outputText.text += text + "\n";
            if (_scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                _scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }

    // ---------- Example Tools ----------

    /// <summary>Rolls one or more dice with a given number of sides.</summary>
    public class RollDiceTool : IUnaiTool
    {
        public UnaiToolDefinition Definition => new()
        {
            Name = "roll_dice",
            Description = "Roll one or more dice. Returns the individual results and the total.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""count"": { ""type"": ""integer"", ""description"": ""Number of dice to roll (default 1)"" },
                    ""sides"": { ""type"": ""integer"", ""description"": ""Number of sides per die (default 6)"" }
                }
            }")
        };

        public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
        {
            var args = call.GetArguments();
            int count = args["count"]?.Value<int>() ?? 1;
            int sides = args["sides"]?.Value<int>() ?? 6;
            count = Mathf.Clamp(count, 1, 100);
            sides = Mathf.Clamp(sides, 2, 1000);

            var rolls = new int[count];
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                rolls[i] = UnityEngine.Random.Range(1, sides + 1);
                total += rolls[i];
            }

            string result = count == 1
                ? $"Rolled a d{sides}: {rolls[0]}"
                : $"Rolled {count}d{sides}: [{string.Join(", ", rolls)}] = {total}";

            return Task.FromResult(new UnaiToolResult { Content = result });
        }
    }

    /// <summary>Returns the current date and time.</summary>
    public class GetTimeTool : IUnaiTool
    {
        public UnaiToolDefinition Definition => new()
        {
            Name = "get_time",
            Description = "Get the current date and time.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {}
            }")
        };

        public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss (dddd)");
            return Task.FromResult(new UnaiToolResult { Content = now });
        }
    }

    /// <summary>Evaluates a simple math expression.</summary>
    public class CalculateTool : IUnaiTool
    {
        public UnaiToolDefinition Definition => new()
        {
            Name = "calculate",
            Description = "Perform a simple calculation. Supports: add, subtract, multiply, divide.",
            ParametersSchema = JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""operation"": { ""type"": ""string"", ""enum"": [""add"", ""subtract"", ""multiply"", ""divide""] },
                    ""a"": { ""type"": ""number"", ""description"": ""First number"" },
                    ""b"": { ""type"": ""number"", ""description"": ""Second number"" }
                },
                ""required"": [""operation"", ""a"", ""b""]
            }")
        };

        public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
        {
            var args = call.GetArguments();
            string op = args["operation"]?.ToString() ?? "add";
            double a = args["a"]?.Value<double>() ?? 0;
            double b = args["b"]?.Value<double>() ?? 0;

            double result = op switch
            {
                "add" => a + b,
                "subtract" => a - b,
                "multiply" => a * b,
                "divide" => b != 0 ? a / b : double.NaN,
                _ => double.NaN
            };

            string content = double.IsNaN(result)
                ? $"Error: Cannot compute {op}({a}, {b})"
                : $"{a} {OpSymbol(op)} {b} = {result}";

            return Task.FromResult(new UnaiToolResult { Content = content });
        }

        private static string OpSymbol(string op) => op switch
        {
            "add" => "+", "subtract" => "-", "multiply" => "*", "divide" => "/", _ => "?"
        };
    }
}
