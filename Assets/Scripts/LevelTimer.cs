using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Survival timer: a bar fills over Level Length seconds, and when it's full the level is complete.
/// The game then pauses and shows the complete message; R (or Enter) plays the level again.
/// The timer stops while the dino is dead; dying restarts the level, which starts the timer over.
/// Hook On Level Complete up to load the next level later.
/// </summary>
public class LevelTimer : MonoBehaviour
{
    [Tooltip("Seconds to survive.")]
    [Min(1f)] public float levelLength = 30f;

    [Tooltip("Image with Image Type = Filled; fills up as time passes.")]
    public Image fill;

    [Tooltip("Shown when the level is complete (hidden until then).")]
    public GameObject completePanel;

    [Tooltip("Pause the game when the level is complete.")]
    public bool pauseOnComplete = true;

    public UnityEvent onLevelComplete;

    public float Elapsed { get; private set; }
    public float Progress => Mathf.Clamp01(Elapsed / levelLength);
    public bool IsComplete { get; private set; }

    DinoController dino;

    void Start()
    {
        Time.timeScale = 1f; // in case the last run ended paused
        dino = FindFirstObjectByType<DinoController>();
        if (completePanel != null) completePanel.SetActive(false);
        Apply();
    }

    void Update()
    {
        if (IsComplete)
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.rKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) Restart();
            return;
        }

        if (dino != null && dino.IsDead) return; // frozen until the restart

        Elapsed += Time.deltaTime;
        Apply();
        if (Elapsed >= levelLength) Complete();
    }

    void Apply()
    {
        if (fill != null) fill.fillAmount = Progress;
    }

    void Complete()
    {
        IsComplete = true;
        Elapsed = levelLength;
        Apply();
        if (completePanel != null) completePanel.SetActive(true);
        if (pauseOnComplete) Time.timeScale = 0f;
        onLevelComplete?.Invoke();
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
