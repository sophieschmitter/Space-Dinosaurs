using System;
using System.Collections.Generic;
using UnityEngine;

public enum ResourceType { Nickel, Iridium }

/// <summary>
/// How much of each resource the player has collected this level. One per scene; anything can call
/// ResourceInventory.Add, and the HUD counters listen to Changed. Counts start at 0 each time the level loads.
/// </summary>
public class ResourceInventory : MonoBehaviour
{
    public static ResourceInventory Instance { get; private set; }

    /// <summary>(resource, new total)</summary>
    public event Action<ResourceType, int> Changed;

    readonly Dictionary<ResourceType, int> counts = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public int Get(ResourceType type) => counts.TryGetValue(type, out int n) ? n : 0;

    public void Add(ResourceType type, int amount)
    {
        if (amount == 0) return;
        counts[type] = Mathf.Max(0, Get(type) + amount);
        Changed?.Invoke(type, counts[type]);
    }

    /// <summary>Takes the amount if there's enough (for spending later). Returns false and takes nothing otherwise.</summary>
    public bool TrySpend(ResourceType type, int amount)
    {
        if (Get(type) < amount) return false;
        Add(type, -amount);
        return true;
    }

    /// <summary>Adds to the scene's inventory, creating one if the scene doesn't have it yet.</summary>
    public static void Give(ResourceType type, int amount)
    {
        if (Instance == null) new GameObject("Resource Inventory").AddComponent<ResourceInventory>();
        Instance.Add(type, amount);
    }
}
