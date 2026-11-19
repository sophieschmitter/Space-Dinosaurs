using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The ore veins in play. Each ore block (an ore vein MeteoriteCluster) stays its own piece over the rock instead of
/// being stamped into a shared tilemap, so ore tiles never join up with each other and look exactly as placed in
/// the editor; the rock under them stays whole. Each block's tilemap is made solid when the level starts and
/// registered here, so the Walls' DestructibleTilemap can drill it (Ore Veins: hits and resource) and enemies
/// path around it.
/// </summary>
public static class OreVeinLayer
{
    static readonly List<Tilemap> maps = new();

    /// <summary>Make this ore block's tilemap solid and drillable (called by MeteoriteCluster as the level starts).</summary>
    public static void Register(Tilemap map)
    {
        if (map == null || maps.Contains(map)) return;
        maps.RemoveAll(m => m == null); // blocks from a previous level
        if (map.GetComponent<Rigidbody2D>() == null) map.gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        if (map.GetComponent<TilemapCollider2D>() == null) map.gameObject.AddComponent<TilemapCollider2D>();
        maps.Add(map);
    }

    /// <summary>The ore vein tile at this world point, and the tilemap and cell it's in (null if none).</summary>
    public static TileBase At(Vector3 world, out Tilemap map, out Vector3Int cell)
    {
        foreach (var m in maps)
        {
            if (m == null) continue;
            var c = m.WorldToCell(world);
            var t = m.GetTile(c);
            if (t != null) { map = m; cell = c; return t; }
        }
        map = null; cell = default;
        return null;
    }

    /// <summary>Every ore vein cell, as cells of the given tilemap's grid (for enemy pathfinding).</summary>
    public static void AddCells(Tilemap onGridOf, HashSet<Vector3Int> into)
    {
        foreach (var m in maps)
        {
            if (m == null) continue;
            foreach (var c in m.cellBounds.allPositionsWithin)
                if (m.HasTile(c)) into.Add(onGridOf.WorldToCell(m.GetCellCenterWorld(c)));
        }
    }
}
