using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// One meteorite (or, with Unbreakable ticked, one metal block or unbreakable meteorite) you can move around the scene on its own: it holds its wall tiles (and any ore on them) in
/// its own child tilemaps ("Meteorite Walls" and "Meteorite Ore"). Drag it in the Scene view; it snaps to whole tiles.
/// When the level starts it stamps its tiles into the scene's main Walls tilemap (DestructibleTilemap) and Ore
/// tilemap (OreLayer) and removes itself, so drilling, ore and enemy pathfinding treat every meteorite as one
/// connected wall, exactly as before. Ore vein blocks stay as they are, each its own piece over the rock (OreVeinLayer).
/// Made by Tools > Space Dinosaurs > Split Walls Into Meteorites, or by dropping a meteorite sprite into a scene.
/// </summary>
[DefaultExecutionOrder(-500)] // before anything reads the walls
[SelectionBase] // clicking its tiles in the Scene view selects the meteorite, not its tilemap
public class MeteoriteCluster : MonoBehaviour
{
    public Tilemap walls, ore;
    [Tooltip("Can't be drilled (metal, or unbreakable meteorite): stamps into the scene's solid tilemap (SolidTilemap) instead of the breakable Walls.")]
    [UnityEngine.Serialization.FormerlySerializedAs("metal")] public bool unbreakable;

    void Awake()
    {
        if (!Application.isPlaying) return;
        if (unbreakable)
        {
            var solid = FindFirstObjectByType<SolidTilemap>();
            Stamp(walls, solid != null ? solid.GetComponent<Tilemap>() : null);
        }
        else
        {
            // Ore veins stay as their own piece over the rock (they don't join up with other ore, and the rock
            // under them stays whole): just make them solid and drillable.
            if (IsOreVein())
            {
                OreVeinLayer.Register(walls);
                var mainOreLayer = FindFirstObjectByType<OreLayer>();
                Stamp(ore, mainOreLayer != null ? mainOreLayer.GetComponent<Tilemap>() : null);
                return; // the block stays in the level
            }
            var mainWalls = FindFirstObjectByType<DestructibleTilemap>();
            Stamp(walls, mainWalls != null ? mainWalls.GetComponent<Tilemap>() : null);
        }
        var mainOre = FindFirstObjectByType<OreLayer>();
        Stamp(ore, mainOre != null ? mainOre.GetComponent<Tilemap>() : null);
        Destroy(gameObject);
    }

    bool IsOreVein()
    {
        if (walls == null) return false;
        foreach (var c in walls.cellBounds.allPositionsWithin) { var t = walls.GetTile(c); if (t != null) return OreVeins.For(t) != null; }
        return false;
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
