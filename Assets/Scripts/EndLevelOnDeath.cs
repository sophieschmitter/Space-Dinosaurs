using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on anything with Health (a boss, a reactor, a special enemy...). When its health reaches 0,
/// the level ends: either as a win (same as the timer filling up) or as a loss (the lose screen, with Lose Message).
/// </summary>
[RequireComponent(typeof(Health))]
public class EndLevelOnDeath : MonoBehaviour
{
    public enum Outcome { Win, Lose }

    [Tooltip("Win = level complete screen. Lose = the lose screen with Lose Message.")]
    public Outcome outcome = Outcome.Win;

    [Tooltip("Seconds to wait after it dies before the level ends (lets its death play out).")]
    [Min(0f)] public float delay = 0.5f;

    [Tooltip("Win: replaces the complete screen's text. Leave empty to keep \"You survived!\".")]
    [TextArea] public string winMessage = "Minerals Acquired";
    [Tooltip("Lose: the message on the lose screen (\"R to restart, E to exit\" is added underneath).")]
    [TextArea] public string loseMessage = "The drill robot was destroyed.";

    [Header("Lose: show it blowing up")]
    [Tooltip("On a loss, freeze the game and pan the camera over to it while its death plays, then show the lose screen (instead of Delay).")]
    public bool panToIt = true;
    [Tooltip("Seconds the camera takes to get there.")]
    [Min(0f)] public float panTime = 0.7f;
    [Tooltip("Seconds to watch once there. 0 = as long as its Death animation.")]
    [Min(0f)] public float watchTime = 0f;
    [Tooltip("Extra seconds on the wreck before the lose screen.")]
    [Min(0f)] public float linger = 0.4f;

    Health health;

    void Awake() => health = GetComponent<Health>();
    void OnEnable() => health.Died += OnDied;
    void OnDisable() => health.Died -= OnDied;

    void OnDied()
    {
        var timer = FindFirstObjectByType<LevelTimer>();
        if (timer != null && outcome == Outcome.Lose && panToIt && !timer.IsOver)
        {
            timer.StartCoroutine(ShowThenLose(timer));
            return;
        }
        // Run the ending on the timer, not on this object, so it still happens if this object is destroyed.
        if (timer != null) timer.StartCoroutine(End(timer, outcome, delay, outcome == Outcome.Win ? winMessage : loseMessage));
        else Debug.LogWarning($"{name}: EndLevelOnDeath needs a LevelTimer (on the HUD) in the scene.");
    }

    // The game is frozen (time scale 0) the moment it dies, so nothing else can happen meanwhile; the camera
    // and its death animation run in real time (DrillRobotArt switches the robot's Animator to unscaled time).
    IEnumerator ShowThenLose(LevelTimer timer)
    {
        Time.timeScale = 0f;
        var cam = Camera.main != null ? Camera.main.GetComponent<PixelCameraFollow>() : null;
        if (cam != null) cam.FocusOn(Middle(), panTime);
        yield return new WaitForSecondsRealtime(panTime + (watchTime > 0f ? watchTime : DeathLength()) + linger);
        if (timer != null) timer.Fail(loseMessage);
    }

    // The middle of its art (its position is at its feet).
    Vector2 Middle()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        return sr != null ? (Vector2)sr.bounds.center : (Vector2)transform.position;
    }

    float DeathLength()
    {
        var anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
            foreach (var clip in anim.runtimeAnimatorController.animationClips)
                if (clip.name.Contains("Death")) return clip.length;
        return 1f;
    }

    static IEnumerator End(LevelTimer timer, Outcome outcome, float delay, string message)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (timer == null) yield break;

        if (outcome == Outcome.Lose) { timer.Fail(message); yield break; }

        if (!string.IsNullOrEmpty(message) && timer.completePanel != null)
        {
            var text = timer.completePanel.GetComponentInChildren<Text>(true);
            if (text != null) text.text = message;
        }
        timer.Complete();
    }
}
