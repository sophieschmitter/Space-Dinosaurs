using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// One meteorite you can move around the scene on its own: it holds its wall tiles (and any ore on them) in
/// its own child tilemaps ("Meteorite Walls" and "Meteorite Ore"). Drag it in the Scene view; it snaps to whole tiles.
/// When the level starts it stamps its tiles into the scene's main Walls tilemap (DestructibleTilemap) and Ore
/// tilemap (OreLayer) and removes itself, so drilling, ore and enemy pathfinding treat every meteorite as one
/// connected wall, exactly as before.
/// Made by Tools > Space Dinosaurs > Split Walls Into Meteorites, or by dropping a meteorite sprite into a scene.
/// </summary>
[DefaultExecutionOrder(-500)] // before anything reads the walls
public class MeteoriteCluster : MonoBehaviour
{
    public Tilemap walls, ore;

    void Awake()
    {
        if (!Application.isPlaying) return;
        var mainWalls = FindFirstObjectByType<DestructibleTilemap>();
        var mainOre = FindFirstObjectByType<OreLayer>();
        Stamp(walls, mainWalls != null ? mainWalls.GetComponent<Tilemap>() : null);
        Stamp(ore, mainOre != null ? mainOre.GetComponent<Tilemap>() : null);
        Destroy(gameObject);
    }

    // Copies every tile to the cell under it in the target tilemap (same Grid, so cells line up).
    static void Stamp(Tilemap from, Tilemap to)
    {
        if (from == null || to == null) return;
        foreach (var cell in from.cellBounds.allPositionsWithin)
        {
            var tile = from.GetTile(cell);
            if (tile == null) continue;
            to.SetTile(to.WorldToCell(from.GetCellCenterWorld(cell)), tile);
        }
    }
}
