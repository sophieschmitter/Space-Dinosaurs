using UnityEngine;

/// <summary>Marks a shield generator's field collider (so shots and crabs know what they hit).</summary>
public class ShieldField : MonoBehaviour
{
    public ShieldGenerator generator;

    /// <summary>True if any shield's edge is between a and b.</summary>
    public static bool AnyBetween(Vector2 a, Vector2 b)
    {
        foreach (var s in FindObjectsByType<ShieldField>(FindObjectsSortMode.None))
            if (s.generator != null && s.generator.Crosses(a, b)) return true;
        return false;
    }
}
