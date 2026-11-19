using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Marks a tilemap of walls that can't be broken (the metal walls). It collides like the meteorite walls, but
/// the drill does nothing to it. Enemies path around its tiles (EnemyController asks AllCells).
/// Metal meteorite-style blocks (MeteoriteCluster with Metal ticked) stamp themselves into it at the start.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class SolidTilemap : MonoBehaviour
{
    /// <summary>Every solid tile in the scene, as cells of the given tilemap's grid.</summary>
    public static void AddCells(Tilemap onGridOf, System.Collections.Generic.HashSet<Vector3Int> into)
    {
        foreach (var s in FindObjectsByType<SolidTilemap>(FindObjectsSortMode.None))
        {
            var map = s.GetComponent<Tilemap>();
            foreach (var c in map.cellBounds.allPositionsWithin)
                if (map.HasTile(c)) into.Add(onGridOf.WorldToCell(map.GetCellCenterWorld(c)));
        }
    }
}
