using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;

/// <summary>
/// Lets tiles on this tilemap be broken one at a time (e.g. by a pickaxe).
/// Removing a tile makes the Rule Tile redraw its neighbours' edges and the collider update automatically.
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

    /// <summary>Hit a specific cell. Returns true if a tile was there.</summary>
    public bool HitCell(Vector3Int cell)
    {
        if (!tilemap.HasTile(cell)) return false;

        int hits = damage.TryGetValue(cell, out int h) ? h + 1 : 1;
        Vector3 centre = tilemap.GetCellCenterWorld(cell);
        if (hits < hitsToBreak)
        {
            damage[cell] = hits;
            onTileHit?.Invoke(cell, centre);
            return true;
        }

        BreakCell(cell);
        return true;
    }

    /// <summary>Remove a tile immediately, ignoring hit points.</summary>
    public void BreakCell(Vector3Int cell)
    {
        if (!tilemap.HasTile(cell)) return;
        Vector3 centre = tilemap.GetCellCenterWorld(cell);
        damage.Remove(cell);
        tilemap.SetTile(cell, null); // Rule Tile refreshes the 8 neighbours; TilemapCollider2D updates itself
        onTileBroken?.Invoke(cell, centre);
    }
}
