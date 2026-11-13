using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnAI.Tools;
using UnityEngine;

namespace UnAI.Samples.NpcDialogue
{
    /// <summary>
    /// Gives an NPC two tools: list_wares (read-only) and sell_item (spends player gold, adds the item).
    /// The model decides when to call them; the C# code enforces the rules.
    /// </summary>
    public class UnaiNpcShop : MonoBehaviour, IUnaiNpcToolProvider
    {
        [Serializable]
        public class Ware
        {
            public string Name;
            public int Price;
            public string Description;
        }

        [SerializeField] private List<Ware> _wares = new()
        {
            new Ware { Name = "Health Potion", Price = 10, Description = "Restores health. Tastes of cherries." },
            new Ware { Name = "Lantern", Price = 8, Description = "Burns all night on one flask of oil." },
            new Ware { Name = "Traveler's Map", Price = 15, Description = "Shows the roads to the north bridge." },
            new Ware { Name = "Iron Sword", Price = 35, Description = "Forged by Brom the blacksmith." }
        };

        public string ToolInstructions =>
            "You run a shop. Use list_wares to check your stock and prices. " +
            "When the player clearly agrees to buy something, call sell_item; never pretend a sale happened " +
            "without calling it. If they cannot afford it, say so in character.";

        public void RegisterTools(UnaiToolRegistry registry)
        {
            registry.Register(new ListWaresTool(this));
            registry.Register(new SellItemTool(this));
        }

        private Ware Find(string name) => _wares.FirstOrDefault(w =>
            string.Equals(w.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

        private class ListWaresTool : IUnaiTool
        {
            private readonly UnaiNpcShop _shop;
            public ListWaresTool(UnaiNpcShop shop) => _shop = shop;

            public UnaiToolDefinition Definition { get; } = new()
            {
                Name = "list_wares",
                Description = "List the items for sale with their prices, plus the player's gold.",
                ParametersSchema = JObject.Parse(@"{ ""type"": ""object"", ""properties"": {} }"),
                IsReadOnly = true
            };

            public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
            {
                string wares = string.Join("\n", _shop._wares.Select(w => $"- {w.Name}: {w.Price} gold. {w.Description}"));
                string player = UnaiPlayerState.Instance != null ? UnaiPlayerState.Instance.Describe() : "unknown";
                return Task.FromResult(new UnaiToolResult { Content = $"{wares}\nPlayer: {player}" });
            }
        }

        private class SellItemTool : IUnaiTool
        {
            private readonly UnaiNpcShop _shop;
            public SellItemTool(UnaiNpcShop shop) => _shop = shop;

            public UnaiToolDefinition Definition { get; } = new()
            {
                Name = "sell_item",
                Description = "Sell one item to the player. Takes their gold and puts the item in their inventory.",
                ParametersSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""item_name"": { ""type"": ""string"", ""description"": ""Exact item name from list_wares"" }
                    },
                    ""required"": [""item_name""]
                }")
            };

            public Task<UnaiToolResult> ExecuteAsync(UnaiToolCall call, CancellationToken ct = default)
            {
                string name = call.GetArguments()["item_name"]?.ToString();
                var ware = _shop.Find(name);
                var player = UnaiPlayerState.Instance;
                string result;
                if (ware == null)
                    result = $"No item called '{name}'. Call list_wares for exact names.";
                else if (player == null || !player.TrySpend(ware.Price))
                    result = $"Sale failed: {ware.Name} costs {ware.Price} gold but the player has {player?.Gold ?? 0}.";
                else
                {
                    player.AddItem(ware.Name);
                    result = $"Sold {ware.Name} for {ware.Price} gold. Player now has {player.Describe()}.";
                }
                return Task.FromResult(new UnaiToolResult { Content = result });
            }
        }
    }
}
