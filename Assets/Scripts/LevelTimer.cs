using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Survival timer and end screens. A bar fills over Level Length seconds; when it's full the level is complete.
/// Fail(message) ends the level as a loss and shows the lose screen with that message (the dino dying,
/// a robot being destroyed...). On either screen the game pauses: R (or Enter) plays again, E exits.
/// Hook On Level Complete / On Level Failed up to anything else that should happen.
/// </summary>
public class LevelTimer : MonoBehaviour
{
    [Tooltip("Seconds to survive.")]
    [Min(1f)] public float levelLength = 30f;

    [Tooltip("Image with Image Type = Filled; fills up as time passes.")]
    public Image fill;

    [Tooltip("Shown when the level is complete (hidden until then).")]
    public GameObject completePanel;

    [Tooltip("Shown when the level is lost (hidden until then).")]
    public GameObject losePanel;
    [Tooltip("The text on the lose screen; Fail(message) writes the message here.")]
    public Text loseText;
    [Tooltip("Added under every lose message.")]
    public string loseHint = "R to restart, E to exit";

    [Tooltip("Scene to load when E is pressed on an end screen. Empty = quit the game (stops Play mode in the Editor).")]
    public string exitScene = "";

    [Tooltip("Pause the game when the level is complete.")]
    public bool pauseOnComplete = true;

    public UnityEvent onLevelComplete;
    public UnityEvent<string> onLevelFailed;

    public float Elapsed { get; private set; }
    public float Progress => Mathf.Clamp01(Elapsed / levelLength);
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public bool IsOver => IsComplete || IsFailed;

    DinoController dino;

    void Start()
    {
        Time.timeScale = 1f; // in case the last run ended paused
        dino = FindFirstObjectByType<DinoController>();
        if (completePanel != null) completePanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
        Apply();
    }

    void Update()
    {
        if (IsOver)
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.rKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) Restart();
            else if (kb != null && kb.eKey.wasPressedThisFrame) Exit();
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

    /// <summary>Ends the level as a win: fills the bar, shows the complete panel, pauses. Other scripts can call this too.</summary>
    public void Complete()
    {
        if (IsOver) return;
        IsComplete = true;
        Elapsed = levelLength;
        Apply();
        if (completePanel != null) completePanel.SetActive(true);
        if (pauseOnComplete) Time.timeScale = 0f;
        onLevelComplete?.Invoke();
    }

    /// <summary>Ends the level as a loss and shows the lose screen with this message. Other scripts call this.</summary>
    public void Fail(string message)
    {
        if (IsOver) return;
        IsFailed = true;
        if (loseText != null)
            loseText.text = string.IsNullOrEmpty(loseHint) ? message : $"{message}\n<size=16>{loseHint}</size>";
        if (losePanel != null) losePanel.SetActive(true);
        Time.timeScale = 0f;
        foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) enemy.Celebrate(); // victory dance
        onLevelFailed?.Invoke(message);
    }

    public void Exit()
    {
        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(exitScene)) { SceneManager.LoadScene(exitScene); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
