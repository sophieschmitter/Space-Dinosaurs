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
    [Tooltip("Optional: an animated portrait (e.g. Assets/Animations/Portraits/Bad Guy Portrait or TV Face " +
             "Portrait). Plays while this line is up, instead of the still Portrait.")]
    public RuntimeAnimatorController portraitAnimation;
    [Tooltip("Which animation in Portrait Animation to play (e.g. Talk, or Neutral / Happy / Look / Squint). " +
             "Empty = its first one.")]
    public string portraitState = "";
    [Tooltip("Optional: loops while this line is being typed out (e.g. the General's voice).")]
    public AudioClip voice;
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

    /// <summary>Each time a line comes up (e.g. the training level switches the TV to whoever is talking).</summary>
    public static event Action<DialogueLine> LineShown;
    /// <summary>When the box closes after its last line.</summary>
    public static event Action Closed;

    [Tooltip("Everything that shows while talking (hidden otherwise).")]
    public GameObject root;
    public Text speakerLabel, textLabel;
    [Tooltip("Blinks when the line has finished typing.")]
    public GameObject nextIndicator;
    public Image portrait;
    [Tooltip("Shown instead of the portrait when a line has none.")]
    public Text portraitLetter;

    [Range(0f, 1f)] public float voiceVolume = 0.45f;

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
    /// <summary>Showing a message that stays up (an end screen): presses don't move it on; whoever opened it closes it.</summary>
    public bool IsHeld { get; private set; }

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
        foreach (var l in lines) if (l != null) foreach (var page in Pages(l)) queue.Enqueue(page);
        if (done != null) onDone += done;
        if (current == null) Open();
    }

    /// <summary>Shows one message that stays on screen (typed out, but presses don't close it), e.g. a level end
    /// screen. Doesn't change the game's speed. Call Hide() to take it away.</summary>
    public void Hold(DialogueLine line)
    {
        if (line == null) return;
        queue.Clear();
        onDone = null;
        IsHeld = true;
        if (root != null) root.SetActive(true);
        var pages = Pages(line);
        // An end screen can't be paged (presses don't move it on), so it shows the whole text.
        queue.Enqueue(pages.Count == 1 ? pages[0] : line);
        Next();
    }

    public void Hide()
    {
        StopVoice();
        IsHeld = false;
        current = null;
        queue.Clear();
        if (root != null) root.SetActive(false);
    }

    // ---------- fitting long lines ----------

    readonly TextGenerator measure = new TextGenerator();

    /// <summary>Splits a line that's too long for the text box into pages that fit, each a copy of the line
    /// (same speaker, portrait, voice) with part of the text. Pages break between sentences where possible, and
    /// only mid-sentence (at a space) when one sentence alone doesn't fit.</summary>
    List<DialogueLine> Pages(DialogueLine line)
    {
        var pages = new List<DialogueLine>();
        if (textLabel == null || string.IsNullOrEmpty(line.text) || Fits(line.text)) { pages.Add(line); return pages; }
        string page = "";
        void Flush() { if (page.Length > 0) pages.Add(Copy(line, page)); page = ""; }
        foreach (var sentence in Sentences(line.text))
        {
            string joined = page.Length == 0 ? sentence : page + " " + sentence;
            if (Fits(joined)) { page = joined; continue; }
            Flush();
            if (Fits(sentence)) { page = sentence; continue; }
            foreach (var w in sentence.Split(' ')) // a sentence too long on its own: by words
            {
                string tryText = page.Length == 0 ? w : page + " " + w;
                if (page.Length > 0 && !Fits(tryText)) { Flush(); page = w; }
                else page = tryText;
            }
        }
        Flush();
        return pages;
    }

    static IEnumerable<string> Sentences(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool end = (c == '.' || c == '!' || c == '?' || c == '\u2026') && (i + 1 == text.Length || text[i + 1] == ' ');
            if (!end) continue;
            yield return text.Substring(start, i + 1 - start).Trim();
            start = i + 1;
        }
        if (start < text.Length && text.Substring(start).Trim().Length > 0) yield return text.Substring(start).Trim();
    }

    bool Fits(string text)
    {
        var rect = textLabel.rectTransform.rect;
        var settings = textLabel.GetGenerationSettings(new Vector2(rect.width, rect.height));
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        float height = measure.GetPreferredHeight(text, settings) / textLabel.pixelsPerUnit;
        return height <= rect.height + 0.5f;
    }

    static DialogueLine Copy(DialogueLine l, string text) => new DialogueLine
    {
        speaker = l.speaker, portrait = l.portrait, portraitAnimation = l.portraitAnimation,
        portraitState = l.portraitState, voice = l.voice, text = text,
    };

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
        bool animated = current.portraitAnimation != null && portrait != null;
        bool hasPortrait = current.portrait != null || animated;
        if (portrait != null) { if (current.portrait != null) portrait.sprite = current.portrait; portrait.enabled = hasPortrait; }
        PlayPortrait(animated ? current.portraitAnimation : null, current.portraitState);
        StartVoice(current.voice);
        LineShown?.Invoke(current);
        if (portraitLetter != null)
        {
            portraitLetter.enabled = !hasPortrait;
            portraitLetter.text = string.IsNullOrEmpty(current.speaker) ? (IsHeld ? "" : "?") : current.speaker.Substring(0, 1).ToUpper();
        }
        Show();
    }

    AudioSource voiceSource;
    float voiceFade = -1f;
    bool voicing; // our own flag: AudioSource.isPlaying reads false while the editor is paused

    // The line's voice loops while the text types out, then fades.
    void StartVoice(AudioClip clip)
    {
        if (voiceSource == null)
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.loop = true;
            voiceSource.spatialBlend = 0f;
            voiceSource.ignoreListenerPause = true;
        }
        voiceFade = -1f;
        if (clip == null) { voiceSource.Stop(); voicing = false; return; }
        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume;
        voiceSource.time = UnityEngine.Random.Range(0f, clip.length * 0.9f); // start somewhere different each line
        voiceSource.Play();
        voicing = true;
    }

    void UpdateVoice(bool typing)
    {
        if (voiceSource == null || !voicing) return;
        if (typing) return;
        if (voiceFade < 0f) voiceFade = 0f;
        voiceFade += Time.unscaledDeltaTime;
        voiceSource.volume = voiceVolume * Mathf.Clamp01(1f - voiceFade / 0.15f);
        if (voiceFade >= 0.15f) { voiceSource.Stop(); voicing = false; }
    }

    void StopVoice() { if (voiceSource != null) voiceSource.Stop(); voicing = false; }

    Animator portraitAnimator;

    // Animated portraits: an Animator on the portrait Image, running in real time (the game is paused meanwhile).
    void PlayPortrait(RuntimeAnimatorController controller, string state)
    {
        if (portrait == null) return;
        if (portraitAnimator == null) portraitAnimator = portrait.GetComponent<Animator>();
        if (controller == null)
        {
            if (portraitAnimator != null) portraitAnimator.runtimeAnimatorController = null;
            return;
        }
        if (portraitAnimator == null) portraitAnimator = portrait.gameObject.AddComponent<Animator>();
        portraitAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        portraitAnimator.runtimeAnimatorController = controller;
        if (!string.IsNullOrEmpty(state)) portraitAnimator.Play(state, 0, 0f);
        portraitAnimator.Update(0f); // show the first frame straight away
    }

    void Close()
    {
        current = null;
        StopVoice();
        if (root != null) root.SetActive(false);
        if (pauseGame) Time.timeScale = savedTimeScale;
        lastClosedFrame = Time.frameCount;
        var done = onDone;
        onDone = null;
        Closed?.Invoke();
        done?.Invoke();
    }

    void Update()
    {
        if (current == null) return;
        if (typed < current.text.Length) typed += lettersPerSecond * Time.unscaledDeltaTime;
        UpdateVoice(typed < current.text.Length);
        Show();
        if (IsHeld) return; // an end screen: stays until hidden
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
        if (nextIndicator != null) nextIndicator.SetActive(done && !IsHeld && (Time.unscaledTime * 2f) % 2f < 1.4f); // end screens don't move on
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
