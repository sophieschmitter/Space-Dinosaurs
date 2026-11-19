using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Survival timer and end screens. A bar fills over Level Length seconds; when it's full the level is complete.
/// While Heavy Attackers or more enemies are attacking the robot at once, the timer runs slower (Heavy Attack
/// Speed) and its fill is tinted; it eases back to normal once fewer are attacking.
/// Fail(message) ends the level as a loss and shows the lose screen with that message (the dino dying,
/// a robot being destroyed...). Both show in the dialogue box along the bottom (with the portrait space) and pause
/// the game. Won: R plays again, Enter goes to Next Scene (Level Select). Lost: R (or Enter) plays again, E exits.
/// Hook On Level Complete / On Level Failed up to anything else that should happen.
/// </summary>
public class LevelTimer : MonoBehaviour
{
    [Tooltip("Seconds to survive.")]
    [Min(1f)] public float levelLength = 30f;

    [Header("Robot under heavy attack")]
    [Tooltip("This many enemies attacking the robot at once slows the timer down.")]
    [Min(1)] public int heavyAttackers = 5;
    [Tooltip("Timer speed while the robot is under heavy attack (1 = normal).")]
    [Range(0.05f, 1f)] public float heavyAttackSpeed = 0.5f;
    [Tooltip("Seconds to ease between normal speed and the slowed speed.")]
    [Min(0f)] public float speedChangeTime = 1f;
    [Tooltip("The timer fill is tinted this colour while it's slowed.")]
    public Color slowedTint = new Color(1f, 0.55f, 0.75f, 1f);

    [Tooltip("Image with Image Type = Filled; fills up as time passes.")]
    public Image fill;
    [Tooltip("Pixel-art timer bar (under the robot's health bar). Used as well as / instead of Fill.")]
    public PixelBar timerBar;

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

    [Header("End screens in the dialogue box")]
    [Tooltip("Shows the win and lose messages in the dialogue box along the bottom (with the portrait space on the " +
             "bottom right) instead of the panels. Uses the scene's Dialogue Box, or makes one from this prefab.")]
    public bool useDialogueBox = true;
    public DialogueBox dialogueBoxPrefab;
    [Tooltip("Name shown above the end screen text (empty = none).")]
    public string endSpeaker = "";
    [Tooltip("Portrait on the end screens (empty = an empty portrait frame).")]
    public Sprite endPortrait;
    [TextArea(2, 4)] public string winText = "Congratulations! You've protected the drill and acquired the necessary resources.";
    [TextArea(1, 3)] public string winHint = "[Press R to restart or Enter to proceed to the next level.]";
    [Tooltip("Scene Enter goes to after winning.")]
    public string nextScene = "Level Select";
    [Tooltip("Added under every lose message in the dialogue box.")]
    [TextArea(1, 3)] public string loseBoxHint = "[Press R to restart or E to exit.]";

    [Tooltip("Pause the game when the level is complete.")]
    public bool pauseOnComplete = true;

    public UnityEvent onLevelComplete;
    public UnityEvent<string> onLevelFailed;

    public float Elapsed { get; private set; }
    /// <summary>How fast the timer runs right now (1 = normal; lower while the robot is under heavy attack).</summary>
    public float Speed { get; private set; } = 1f;
    /// <summary>True while enough enemies are attacking the robot to slow the timer.</summary>
    public bool UnderHeavyAttack { get; private set; }
    public float Progress => Mathf.Clamp01(Elapsed / levelLength);
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public bool IsOver => IsComplete || IsFailed;

    DinoController dino;
    EnemyGoal goal;
    Color fillColor = Color.white, barColor = Color.white;

    void Start()
    {
        Time.timeScale = 1f; // in case the last run ended paused
        dino = FindFirstObjectByType<DinoController>();
        goal = FindFirstObjectByType<EnemyGoal>();
        if (fill != null) fillColor = fill.color;
        if (timerBar != null && timerBar.fill != null) barColor = timerBar.fill.color;
        if (completePanel != null) completePanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
        Apply();
    }

    void Update()
    {
        if (IsOver)
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool r = kb != null && kb.rKey.wasPressedThisFrame;
            bool enter = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                         || (pad != null && (pad.startButton.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame));
            bool e = (kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (IsComplete)
            {
                // Won: R plays again, Enter (controller: Start or A) goes on to the next level (Level Select).
                if (r || (pad != null && pad.buttonWest.wasPressedThisFrame)) Restart();
                else if (enter) Proceed();
            }
            else
            {
                // Lost: R or Enter (controller: Start or A) restarts, E (B) exits.
                if (r || enter) Restart();
                else if (e) Exit();
            }
            return;
        }

        if (dino != null && dino.IsDead) return; // frozen until the restart

        UnderHeavyAttack = goal != null && goal.Attackers >= heavyAttackers;
        float target = UnderHeavyAttack ? heavyAttackSpeed : 1f;
        Speed = speedChangeTime > 0f ? Mathf.MoveTowards(Speed, target, (1f - heavyAttackSpeed) / speedChangeTime * Time.deltaTime) : target;
        Elapsed += Time.deltaTime * Speed;
        Apply();
        if (Elapsed >= levelLength) Complete();
    }

    void Apply()
    {
        // 0 at normal speed, 1 when fully slowed: tints the fill so the slowdown shows.
        float slowed = heavyAttackSpeed < 1f ? Mathf.InverseLerp(1f, heavyAttackSpeed, Speed) : 0f;
        if (fill != null) { fill.fillAmount = Progress; fill.color = Color.Lerp(fillColor, fillColor * slowedTint, slowed); }
        if (timerBar != null)
        {
            timerBar.Value = Progress;
            if (timerBar.fill != null) timerBar.fill.color = Color.Lerp(barColor, barColor * slowedTint, slowed);
            if (timerBar.cap != null) timerBar.cap.color = Color.Lerp(barColor, barColor * slowedTint, slowed);
        }
    }

    /// <summary>Ends the level as a win: fills the bar, shows the complete panel, pauses. Other scripts can call this too.</summary>
    public void Complete()
    {
        if (IsOver) return;
        IsComplete = true;
        Elapsed = levelLength;
        Apply();
        if (dino != null) dino.Celebrate(); // the victory dance, behind the end screen
        if (!ShowInBox($"{winText}\n{winHint}") && completePanel != null) completePanel.SetActive(true);
        if (pauseOnComplete) Time.timeScale = 0f;
        onLevelComplete?.Invoke();
    }

    /// <summary>Ends the level as a loss and shows the lose screen with this message. Other scripts call this.</summary>
    public void Fail(string message)
    {
        if (IsOver) return;
        IsFailed = true;
        if (!ShowInBox(string.IsNullOrEmpty(loseBoxHint) ? message : $"{message}\n{loseBoxHint}"))
        {
            if (loseText != null)
                loseText.text = string.IsNullOrEmpty(loseHint) ? message : $"{message}\n<size=16>{loseHint}</size>";
            if (losePanel != null) losePanel.SetActive(true);
        }
        Time.timeScale = 0f;
        foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) enemy.Celebrate(); // victory dance
        onLevelFailed?.Invoke(message);
    }

    // The end screen in the dialogue box. False if there's no dialogue box to use (the panels show instead).
    bool ShowInBox(string text)
    {
        if (!useDialogueBox) return false;
        var box = DialogueBox.Instance;
        if (box == null && dialogueBoxPrefab != null) box = Instantiate(dialogueBoxPrefab);
        if (box == null) return false;
        box.Hold(new DialogueLine { speaker = endSpeaker, portrait = endPortrait, text = text });
        return true;
    }

    /// <summary>After winning: on to the next level (Next Scene, the level select).</summary>
    public void Proceed()
    {
        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(nextScene)) SceneManager.LoadScene(nextScene);
        else Restart();
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
