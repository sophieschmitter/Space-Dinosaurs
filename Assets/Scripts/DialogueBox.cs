using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>One line of dialogue: who says it, their portrait (optional) and the text.</summary>
[Serializable]
public class DialogueLine
{
    public string speaker = "???";
    [Tooltip("Shown bottom right. Empty: a placeholder square with the speaker's first letter.")]
    public Sprite portrait;
    [TextArea(2, 5)] public string text = "...";
}

/// <summary>
/// The dialogue box: text along the bottom of the screen with the speaker's portrait on the bottom right.
/// Play(lines) shows them one at a time, typed out letter by letter. Space, Enter, E, a click or the
/// controller's A button finishes the current line, then moves to the next; after the last line it closes.
/// The game is paused while it's open (it runs in real time).
/// Placeholder look for now: swap the Box / Portrait Frame Images' sprites and colours for the real art.
/// Built by Tools > Space Dinosaurs > Build Dialogue Box. Start it with a DialogueTrigger.
/// </summary>
public class DialogueBox : MonoBehaviour
{
    public static DialogueBox Instance { get; private set; }

    [Tooltip("Everything that shows while talking (hidden otherwise).")]
    public GameObject root;
    public Text speakerLabel, textLabel;
    [Tooltip("Blinks when the line has finished typing.")]
    public GameObject nextIndicator;
    public Image portrait;
    [Tooltip("Shown instead of the portrait when a line has none.")]
    public Text portraitLetter;

    [Tooltip("Letters per second (0 = the whole line at once).")]
    public float lettersPerSecond = 40f;
    public bool pauseGame = true;

    readonly Queue<DialogueLine> queue = new();
    Action onDone;
    DialogueLine current;
    float typed;
    float savedTimeScale = 1f;
    static int lastClosedFrame = -1;

    public bool IsOpen => current != null;

    /// <summary>True while it's open and on the frame it closes: the dino ignores the mouse button then.</summary>
    public static bool ClickTaken => (Instance != null && Instance.IsOpen) || Time.frameCount == lastClosedFrame;

    void Awake()
    {
        Instance = this;
        if (root != null) root.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Shows these lines in order (added to the end if it's already talking). onDone runs after the last.</summary>
    public void Play(IEnumerable<DialogueLine> lines, Action done = null)
    {
        foreach (var l in lines) if (l != null) queue.Enqueue(l);
        if (done != null) onDone += done;
        if (current == null) Open();
    }

    void Open()
    {
        if (queue.Count == 0) return;
        if (pauseGame) { savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f; Time.timeScale = 0f; }
        if (root != null) root.SetActive(true);
        Next();
    }

    void Next()
    {
        if (queue.Count == 0) { Close(); return; }
        current = queue.Dequeue();
        typed = lettersPerSecond > 0f ? 0f : current.text.Length;
        if (speakerLabel != null) speakerLabel.text = current.speaker;
        bool hasPortrait = current.portrait != null;
        if (portrait != null) { portrait.sprite = current.portrait; portrait.enabled = hasPortrait; }
        if (portraitLetter != null)
        {
            portraitLetter.enabled = !hasPortrait;
            portraitLetter.text = string.IsNullOrEmpty(current.speaker) ? "?" : current.speaker.Substring(0, 1).ToUpper();
        }
        Show();
    }

    void Close()
    {
        current = null;
        if (root != null) root.SetActive(false);
        if (pauseGame) Time.timeScale = savedTimeScale;
        lastClosedFrame = Time.frameCount;
        var done = onDone;
        onDone = null;
        done?.Invoke();
    }

    void Update()
    {
        if (current == null) return;
        if (typed < current.text.Length) typed += lettersPerSecond * Time.unscaledDeltaTime;
        Show();
        if (!PressedNext()) return;
        if (typed < current.text.Length) typed = current.text.Length; // finish the line first
        else Next();
    }

    void Show()
    {
        if (current == null) return;
        int n = Mathf.Clamp(Mathf.FloorToInt(typed), 0, current.text.Length);
        if (textLabel != null) textLabel.text = current.text.Substring(0, n);
        bool done = n >= current.text.Length;
        if (nextIndicator != null) nextIndicator.SetActive(done && (Time.unscaledTime * 2f) % 2f < 1.4f);
    }

    static bool PressedNext()
    {
        var k = Keyboard.current;
        if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.eKey.wasPressedThisFrame)) return true;
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) return true;
        var g = Gamepad.current;
        return g != null && g.buttonSouth.wasPressedThisFrame;
    }
}
