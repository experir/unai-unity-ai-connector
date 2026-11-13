using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnAI.Samples.NpcDialogue
{
    /// <summary>
    /// Minimal game state that NPC tools can read and change (gold and inventory).
    /// </summary>
    public class UnaiPlayerState : MonoBehaviour
    {
        [SerializeField] private int _gold = 50;
        [SerializeField] private List<string> _inventory = new();

        public static UnaiPlayerState Instance { get; private set; }
        public int Gold => _gold;
        public IReadOnlyList<string> Inventory => _inventory;
        public event Action Changed;

        private void Awake() => Instance = this;

        public bool TrySpend(int amount)
        {
            if (amount < 0 || amount > _gold) return false;
            _gold -= amount;
            Changed?.Invoke();
            return true;
        }

        public void AddItem(string item)
        {
            _inventory.Add(item);
            Changed?.Invoke();
        }

        public string Describe() =>
            $"{_gold} gold; inventory: {(_inventory.Count == 0 ? "empty" : string.Join(", ", _inventory))}";
    }
}
