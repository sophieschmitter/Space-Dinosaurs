using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Which breakable wall tiles are ore veins: what mining one gives and how many drill hits it takes.
/// Lives at Assets/Resources/Ore Veins.asset and is picked up automatically by every breakable Walls tilemap
/// (DestructibleTilemap), so no scene needs setting up. Brown ore = Copper, green ore = Iridium.
/// </summary>
[CreateAssetMenu(menuName = "Space Dinosaurs/Ore Veins", fileName = "Ore Veins")]
public class OreVeins : ScriptableObject
{
    [System.Serializable]
    public class Vein
    {
        public TileBase tile;
        public ResourceType resource = ResourceType.Copper;
        [Min(0)] public int amount = 1;
        [Tooltip("Drill hits to break one tile.")]
        [Min(1)] public int hitsToBreak = 3;
    }

    public Vein[] veins;

    static OreVeins loaded;
    static bool tried;

    /// <summary>The table (Resources/Ore Veins), or null if there isn't one.</summary>
    public static OreVeins Table
    {
        get
        {
            if (!tried) { loaded = Resources.Load<OreVeins>("Ore Veins"); tried = true; }
            return loaded;
        }
    }

    /// <summary>The vein entry for this tile, or null if it's ordinary rock.</summary>
    public static Vein For(TileBase tile)
    {
        var t = Table;
        if (t == null || tile == null || t.veins == null) return null;
        foreach (var v in t.veins) if (v != null && v.tile == tile) return v;
        return null;
    }
}
