using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Goes on the Ore tilemap (drawn over the Walls tilemap). When the wall tile under an ore node is broken,
/// the node is removed and its resource goes to the ResourceInventory.
///
/// Placing ore: paint the ore tiles from the "Ore" Tile Palette onto wall tiles, or right-click this
/// component's header > Scatter Ore On Walls to place it randomly (Common/Rare chances below). Undo works.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class OreLayer : MonoBehaviour
{
    [Tooltip("The breakable wall layer the ore sits on. Found automatically if left empty.")]
    public DestructibleTilemap walls;

    [Header("Scatter Ore On Walls")]
    public OreTile nickel;
    [Range(0f, 1f)] public float nickelChance = 0.12f;
    public OreTile iridium;
    [Range(0f, 1f)] public float iridiumChance = 0.04f;

    [Tooltip("Called with the ore tile and the world position when a node is mined (for effects/sound).")]
    public UnityEngine.Events.UnityEvent<OreTile, Vector3> onMined;

    Tilemap ore;

    void Awake()
    {
        ore = GetComponent<Tilemap>();
        if (walls == null) walls = FindFirstObjectByType<DestructibleTilemap>();
    }

    void OnEnable() { if (walls != null) walls.TileBroken += OnWallBroken; }
    void OnDisable() { if (walls != null) walls.TileBroken -= OnWallBroken; }

    void OnWallBroken(Vector3Int wallCell, Vector3 worldCentre)
    {
        // The two tilemaps share the Grid, so the same cell coordinates line up.
        var cell = ore.WorldToCell(worldCentre);
        if (ore.GetTile(cell) is not OreTile node) return;
        ore.SetTile(cell, null);
        ResourceInventory.Give(node.resource, node.amount);
        onMined?.Invoke(node, worldCentre);
    }

#if UNITY_EDITOR
    [ContextMenu("Scatter Ore On Walls")]
    void Scatter()
    {
        var map = GetComponent<Tilemap>();
        var w = walls != null ? walls : FindFirstObjectByType<DestructibleTilemap>();
        if (w == null) { Debug.LogWarning("OreLayer: no Walls tilemap found."); return; }
        var wallMap = w.GetComponent<Tilemap>();
        UnityEditor.Undo.RegisterCompleteObjectUndo(map, "Scatter ore");

        int n = 0, ir = 0;
        foreach (var cell in wallMap.cellBounds.allPositionsWithin)
        {
            if (!wallMap.HasTile(cell)) continue;
            var oreCell = map.WorldToCell(wallMap.GetCellCenterWorld(cell));
            if (map.HasTile(oreCell)) continue; // keep ore that's already there
            float roll = Random.value;
            if (iridium != null && roll < iridiumChance) { map.SetTile(oreCell, iridium); ir++; }
            else if (nickel != null && roll < iridiumChance + nickelChance) { map.SetTile(oreCell, nickel); n++; }
        }
        Debug.Log($"OreLayer: scattered {n} nickel and {ir} iridium nodes.");
    }

    [ContextMenu("Clear All Ore")]
    void ClearAll()
    {
        var map = GetComponent<Tilemap>();
        UnityEditor.Undo.RegisterCompleteObjectUndo(map, "Clear ore");
        map.ClearAllTiles();
    }
#endif
}
