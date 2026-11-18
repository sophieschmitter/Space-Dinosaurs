using UnityEngine;

/// <summary>
/// Starts some dialogue in the DialogueBox: when the level starts (Play On Start), or when the dino walks into
/// this object's trigger collider (add a Collider2D with Is Trigger on). Plays once unless Once is off.
/// Write the lines in the Inspector.
/// </summary>
public class DialogueTrigger : MonoBehaviour
{
    public DialogueLine[] lines;
    [Tooltip("Play as soon as the level starts instead of waiting for the dino to walk in.")]
    public bool playOnStart;
    [Tooltip("Only ever play it once.")]
    public bool once = true;

    bool played;

    void Start()
    {
        if (playOnStart) Play();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!playOnStart && other.GetComponentInParent<DinoController>() != null) Play();
    }

    public void Play() => Play(null);

    /// <summary>Plays the lines; done runs after the last one closes (or straight away if it can't play).</summary>
    public void Play(System.Action done)
    {
        if (once && played) { done?.Invoke(); return; }
        var box = DialogueBox.Instance != null ? DialogueBox.Instance : FindFirstObjectByType<DialogueBox>();
        if (box == null) { Debug.LogWarning($"{name}: no Dialogue Box in the scene."); done?.Invoke(); return; }
        played = true;
        box.Play(lines, done);
    }
}
