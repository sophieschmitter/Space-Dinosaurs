using UnityEngine;

/// <summary>
/// Marks what enemies are sent after when they spawn (the drill robot / tower). Spawners with
/// Path To Goal ticked send each new enemy along a path around the walls to this object; once it gets
/// there it goes back to normal behaviour. Enemies also damage it on contact if it has a Health.
/// </summary>
public class EnemyGoal : MonoBehaviour
{
    [Tooltip("Enemies count as having arrived when they're this close (tiles).")]
    public float arriveDistance = 1.2f;

    [Tooltip("Lose-screen message if an enemy's hit destroys this (used when it has End Level On Death).")]
    [TextArea] public string destroyedMessage = "The drill robot was destroyed.";

    public static EnemyGoal Find() => FindFirstObjectByType<EnemyGoal>();
}
