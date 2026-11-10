using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Hit points for anything that can be hurt (the dino, enemies). Other scripts call TakeDamage;
/// movement/AI scripts listen to Damaged and Died to play hurt/death behaviour.
/// After a hit it ignores further damage for Invulnerable Time, so touching an enemy doesn't drain it instantly.
/// </summary>
public class Health : MonoBehaviour
{
    [Min(1)] public int maxHealth = 3;

    [Tooltip("Seconds after a hit during which further hits are ignored.")]
    [Min(0f)] public float invulnerableTime = 0;

    [Tooltip("Called with the damage amount and the position the hit came from.")]
    public UnityEvent<int, Vector2> onDamaged;
    public UnityEvent onDied;

    /// <summary>For scripts: (amount, hit source position).</summary>
    public event Action<int, Vector2> Damaged;
    public event Action Died;
    /// <summary>Called whenever Current changes (for a health bar).</summary>
    public event Action<int, int> Changed;

    public int Current { get; private set; }
    public bool IsDead => Current <= 0;
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    float invulnerableUntil;

    void Awake() => Current = maxHealth;

    /// <summary>What dealt the last hit that landed, e.g. "A hunter tracked you down." (null if not given).
    /// The dino's lose screen shows this when it dies.</summary>
    public string LastHitCause { get; private set; }

    /// <summary>Returns true if the hit landed (not dead, not invulnerable).</summary>
    public bool TakeDamage(int amount, Vector2 from) => TakeDamage(amount, from, null);

    /// <summary>Same, also saying what caused the hit (shown on the lose screen if this hit kills the dino).</summary>
    public bool TakeDamage(int amount, Vector2 from, string cause)
    {
        if (IsDead || IsInvulnerable || amount <= 0) return false;
        LastHitCause = cause;

        Current = Mathf.Max(0, Current - amount);
        invulnerableUntil = Time.time + invulnerableTime;
        Changed?.Invoke(Current, maxHealth);
        Damaged?.Invoke(amount, from);
        onDamaged?.Invoke(amount, from);

        if (IsDead)
        {
            Died?.Invoke();
            onDied?.Invoke();
        }
        return true;
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        Changed?.Invoke(Current, maxHealth);
    }

    public void ResetHealth()
    {
        Current = maxHealth;
        invulnerableUntil = 0f;
        Changed?.Invoke(Current, maxHealth);
    }
}
