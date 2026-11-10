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

    Health health;

    void Awake() => health = GetComponent<Health>();
    void OnEnable() => health.Died += OnDied;
    void OnDisable() => health.Died -= OnDied;

    void OnDied()
    {
        var timer = FindFirstObjectByType<LevelTimer>();
        // Run the ending on the timer, not on this object, so it still happens if this object is destroyed.
        if (timer != null) timer.StartCoroutine(End(timer, outcome, delay, outcome == Outcome.Win ? winMessage : loseMessage));
        else Debug.LogWarning($"{name}: EndLevelOnDeath needs a LevelTimer (on the HUD) in the scene.");
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
