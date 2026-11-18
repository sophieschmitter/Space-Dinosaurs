using UnityEngine;

/// <summary>
/// The ice cream stand heals the dino. Walk into its Serving Area (a trigger around it) while hurt and the dino
/// eats the ice cream: it gets Heal Amount HP back and the stand shows its Empty state (no ice cream) for
/// Refill Time seconds, then the ice cream comes back. Nothing happens while it's being placed, and it
/// doesn't serve a dino that's already at full health (so the ice cream isn't wasted).
/// </summary>
[RequireComponent(typeof(Building))]
public class IceCreamStand : MonoBehaviour
{
    [Min(1)] public int healAmount = 20;
    [Tooltip("Seconds after the ice cream is eaten before a new one appears.")]
    [Min(0f)] public float refillTime = 20f;
    public string fullState = "Normal", emptyState = "Empty";

    Building building;
    float refillTimer;

    public bool HasIceCream => refillTimer <= 0f;

    void Awake() => building = GetComponent<Building>();

    void Update()
    {
        if (refillTimer <= 0f) return;
        refillTimer -= Time.deltaTime;
        if (refillTimer <= 0f) Show(fullState);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!HasIceCream || building.Selected) return;
        var dino = other.GetComponentInParent<DinoController>();
        if (dino == null || !dino.TryGetComponent(out Health health)) return;
        if (health.IsDead || health.Current >= health.maxHealth) return;
        Serve(health);
    }

    /// <summary>Heals this Health and empties the stand until it refills.</summary>
    public void Serve(Health health)
    {
        health.Heal(healAmount);
        refillTimer = refillTime;
        Show(emptyState);
    }

    void Show(string state)
    {
        building.normalState = state;
        building.Refresh();
    }
}
