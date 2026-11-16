using UnityEngine;

/// <summary>
/// Picks the drill robot's animation from its Health: Green while healthy, Yellow when damaged,
/// Red when nearly destroyed, then Death (the explosion), which ends on the Wreck frame.
///
/// All of these are frames of one sprite sheet driven by one Animator, so switching only changes
/// which already-loaded sprite is drawn: nothing is reloaded and there's no hitch. The three health
/// loops have the same timing, so a switch carries on from the same point in the loop instead of
/// jumping back to frame 1.
/// </summary>
[RequireComponent(typeof(Health))]
public class DrillRobotArt : MonoBehaviour
{
    [Tooltip("Animator with the Green / Yellow / Red / Death / Wreck states (Robot.controller).")]
    public Animator animator;

    [Tooltip("Shows Yellow at or below this fraction of max health.")]
    [Range(0f, 1f)] public float yellowBelow = 0.67f;
    [Tooltip("Shows Red at or below this fraction of max health.")]
    [Range(0f, 1f)] public float redBelow = 0.34f;

    public string greenState = "Green", yellowState = "Yellow", redState = "Red", deathState = "Death";

    Health health;
    string current;

    void Awake()
    {
        health = GetComponent<Health>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void OnEnable() { health.Changed += OnChanged; health.Died += OnDied; }
    void OnDisable() { health.Changed -= OnChanged; health.Died -= OnDied; }

    void Start() => OnChanged(health.Current, health.maxHealth);

    void OnChanged(int now, int max)
    {
        if (health.IsDead) return; // OnDied handles it
        float f = max > 0 ? now / (float)max : 1f;
        string state = f <= redBelow ? redState : f <= yellowBelow ? yellowState : greenState;
        if (state == current || animator == null) return;

        // Keep the place in the loop so the swap is seamless.
        float t = current == null ? 0f : animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
        animator.Play(state, 0, t);
        current = state;
    }

    void OnDied()
    {
        if (animator == null) return;
        // The lose screen pauses the game (timeScale 0); keep the explosion playing behind it.
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.Play(deathState, 0, 0f); // its controller moves on to Wreck when it finishes
        current = deathState;
    }
}
