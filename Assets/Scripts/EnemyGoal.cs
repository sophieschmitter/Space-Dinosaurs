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

    [Tooltip("Enemies this close to the robot (tiles, edge to edge) count as attacking it.")]
    public float attackReach = 0.35f;
    [Tooltip("An enemy counts as attacking for this long after it last touched or shot the robot (seconds).")]
    public float attackMemory = 1.5f;

    readonly System.Collections.Generic.Dictionary<EnemyController, float> attackedAt = new();
    readonly System.Collections.Generic.List<EnemyController> stale = new();

    /// <summary>Enemies call this while they're touching the robot, or when their shot hits it.</summary>
    public void ReportAttack(EnemyController enemy)
    {
        if (enemy != null) attackedAt[enemy] = Time.time;
    }

    Collider2D[] own;
    readonly System.Collections.Generic.HashSet<EnemyController> counted = new();

    /// <summary>How many enemies are attacking the robot right now: every enemy right up against it (crowding
    /// round it, even between hits), plus any that touched or shot it within the last Attack Memory seconds.</summary>
    public int Attackers
    {
        get
        {
            counted.Clear();
            stale.Clear();
            foreach (var kv in attackedAt)
            {
                if (kv.Key == null || kv.Key.IsDead || Time.time - kv.Value > attackMemory) stale.Add(kv.Key);
                else counted.Add(kv.Key);
            }
            foreach (var e in stale) attackedAt.Remove(e);

            if (own == null || own.Length == 0) own = GetComponentsInChildren<Collider2D>();
            foreach (var e in EnemyController.All)
            {
                if (e == null || e.IsDead || counted.Contains(e)) continue;
                if (Near(e)) counted.Add(e);
            }
            return counted.Count;
        }
    }

    bool Near(EnemyController e)
    {
        foreach (var ec in e.GetComponentsInChildren<Collider2D>())
        {
            if (ec.isTrigger || !ec.enabled) continue;
            foreach (var mine in own)
            {
                if (mine == null || mine.isTrigger || !mine.enabled) continue;
                var d = ec.Distance(mine);
                if (d.isValid && d.distance <= attackReach) return true;
            }
        }
        return false;
    }
}
