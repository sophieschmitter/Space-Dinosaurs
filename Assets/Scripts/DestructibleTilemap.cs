using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;

/// <summary>
/// Lets tiles on this tilemap be broken one at a time (e.g. by a pickaxe).
/// Removing a tile makes the Rule Tile redraw its neighbours' edges and the collider update automatically.
/// Ore vein tiles (listed in Resources/Ore Veins) take their own number of hits and give their resource when broken.
///
/// From an attack script:
///     walls.HitAt(pickaxeTipWorldPosition);                      // hit whatever tile is there
///     walls.HitInFront(player.position, facingDirection);       // hit the tile next to the player
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class DestructibleTilemap : MonoBehaviour
{
    [Tooltip("Hits needed to break a tile. 1 = breaks on the first hit.")]
    [Min(1)] public int hitsToBreak = 1;

    [Tooltip("How far in front of the player (in tiles) HitInFront reaches.")]
    public float reach = 0.75f;

    [Tooltip("Called with the cell and its world centre every time a tile is hit but not yet broken.")]
    public UnityEvent<Vector3Int, Vector3> onTileHit;

    [Tooltip("Called with the cell and its world centre when a tile breaks (hook up particles/sound here).")]
    public UnityEvent<Vector3Int, Vector3> onTileBroken;

    /// <summary>For scripts (e.g. the ore layer): (cell, world centre) when a tile breaks.</summary>
    public event System.Action<Vector3Int, Vector3> TileBroken;

    /// <summary>Any destructible tilemap: (world centre, was it ore, did it break) each time HitCell lands a hit.
    /// The dino's DrillSounds plays its sounds from this.</summary>
    public static event System.Action<Vector3, bool, bool> AnyHit;

    Tilemap map;
    Tilemap tilemap => map != null ? map : map = GetComponent<Tilemap>();
    readonly Dictionary<Vector3Int, int> damage = new();

    /// <summary>Hit the tile at a world position. Returns true if a tile was there.</summary>
    public bool HitAt(Vector3 worldPosition) => HitCell(tilemap.WorldToCell(worldPosition));

    /// <summary>Hit the tile next to <paramref name="origin"/> in <paramref name="direction"/> (e.g. the way the player faces).</summary>
    public bool HitInFront(Vector3 origin, Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return false;
        return HitAt(origin + (Vector3)(direction.normalized * reach));
    }

    // The ore vein (if any) at this cell: on the ore layer over the rock, or painted straight onto these Walls.
    TileBase OreAt(Vector3Int cell, out Tilemap on)
    {
        var t = OreVeinLayer.At(tilemap.GetCellCenterWorld(cell), out on, out _);
        if (t != null) return t;
        on = null;
        var own = tilemap.GetTile(cell);
        if (own != null && OreVeins.For(own) != null) { on = tilemap; return own; }
        return null;
    }

    /// <summary>Hit a specific cell. Returns true if a tile (rock or ore vein) was there.</summary>
    public bool HitCell(Vector3Int cell)
    {
        var oreTile = OreAt(cell, out _);
        if (!tilemap.HasTile(cell) && oreTile == null) return false;

        int hits = damage.TryGetValue(cell, out int h) ? h + 1 : 1;
        Vector3 centre = tilemap.GetCellCenterWorld(cell);
        var vein = OreVeins.For(oreTile); // ore veins take their own number of hits
        if (hits < (vein != null ? vein.hitsToBreak : hitsToBreak))
        {
            damage[cell] = hits;
            onTileHit?.Invoke(cell, centre);
            AnyHit?.Invoke(centre, vein != null, false);
            return true;
        }

        BreakCell(cell);
        AnyHit?.Invoke(centre, vein != null, true);
        return true;
    }

    /// <summary>Remove a tile immediately, ignoring hit points.</summary>
    public void BreakCell(Vector3Int cell)
    {
        var oreTile = OreAt(cell, out var oreMap);
        if (!tilemap.HasTile(cell) && oreTile == null) return;
        Vector3 centre = tilemap.GetCellCenterWorld(cell);
        var vein = OreVeins.For(oreTile);
        damage.Remove(cell);
        tilemap.SetTile(cell, null); // Rule Tile refreshes the 8 neighbours; TilemapCollider2D updates itself
        if (oreMap != null && oreMap != tilemap) oreMap.SetTile(oreMap.WorldToCell(centre), null); // the ore over it, too
        if (vein != null && vein.amount > 0) ResourceInventory.Give(vein.resource, vein.amount); // mined an ore vein
        onTileBroken?.Invoke(cell, centre);
        TileBroken?.Invoke(cell, centre);
    }
}
