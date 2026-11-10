using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// An ore node painted on the Ore tilemap, on top of a wall tile. Breaking the wall underneath mines it.
/// Swap the Sprite for real art later (or set an animated sprite list on a custom tile); the colour tint is
/// only there for the placeholder.
/// </summary>
[CreateAssetMenu(menuName = "Space Dinosaurs/Ore Tile")]
public class OreTile : Tile
{
    public ResourceType resource = ResourceType.Nickel;

    [Tooltip("How much of the resource mining one node gives.")]
    [Min(1)] public int amount = 3;
}
