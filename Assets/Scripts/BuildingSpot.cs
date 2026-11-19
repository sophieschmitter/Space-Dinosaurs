using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spot a building can be put on (the flat round base). Buildings can only be placed on free spots: while one
/// is being placed it snaps onto the nearest free spot near the mouse (BuildingPlacer), and free spots show their
/// outlined picture so you can see where they are. Once a building is on it, the spot hides under it; if the
/// building is ever removed the spot is free again.
/// Drop the Building Spot prefab (Assets/Prefabs/Buildings) wherever buildings should be allowed.
/// </summary>
public class BuildingSpot : MonoBehaviour
{
    /// <summary>Every spot in the scene.</summary>
    public static readonly List<BuildingSpot> All = new List<BuildingSpot>();

    public SpriteRenderer sprite;
    [Tooltip("Shown normally.")]
    public Sprite normal;
    [Tooltip("Shown on free spots while a building is being placed.")]
    public Sprite highlighted;

    /// <summary>The building standing on it, if any.</summary>
    public Building Occupant { get; private set; }
    public bool Free => Occupant == null;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Awake() { if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>(); }

    public void Occupy(Building building) => Occupant = building;

    void LateUpdate()
    {
        if (sprite == null) return;
        sprite.enabled = Free; // hidden under its building (Occupant turns null if the building is destroyed)
        var want = Free && BuildingPlacer.Placing != null && highlighted != null ? highlighted : normal;
        if (want != null && sprite.sprite != want) sprite.sprite = want;
    }

    /// <summary>The free spot closest to this point, no further than maxDistance; null if there's none.</summary>
    public static BuildingSpot NearestFree(Vector2 point, float maxDistance)
    {
        BuildingSpot best = null;
        float bestSq = maxDistance * maxDistance;
        foreach (var s in All)
        {
            if (s == null || !s.Free) continue;
            float d = ((Vector2)s.transform.position - point).sqrMagnitude;
            if (d <= bestSq) { best = s; bestSq = d; }
        }
        return best;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.3f, 0.75f);
    }
}
