using UnityEngine;

/// <summary>
/// The dino's footsteps: a looping clip that plays softly while it walks, a bit faster (and a touch higher)
/// while it sprints, and pauses when it stops, so the steps carry on from where they left off. Quiet while the
/// game is paused (menus, dialogue) and once the dino is dead. Fades in and out over a few hundredths of a
/// second so starting and stopping doesn't click.
/// </summary>
[RequireComponent(typeof(DinoController))]
public class DinoFootsteps : MonoBehaviour
{
    [Tooltip("Loops while walking (Assets/SFX/Footsteps Loop.wav: the footsteps with the silence cut off).")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.35f;
    [Tooltip("Playback speed while sprinting (1 = normal). Also raises the pitch slightly.")]
    [Range(1f, 2f)] public float sprintSpeed = 1.25f;
    [Tooltip("Seconds to fade in or out.")]
    public float fadeTime = 0.06f;

    DinoController dino;
    AudioSource source;
    float level;  // 0-1 fade
    bool sounding; // started (or resumed) and not paused since: kept here rather than asking isPlaying,
                   // which also reads false while the editor or the audio is paused

    void Awake()
    {
        dino = GetComponent<DinoController>();
        source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // plain 2D sound: the camera follows the dino anyway
        source.volume = 0f;
    }

    void Update()
    {
        if (clip == null) return;
        bool paused = Time.timeScale == 0f;
        bool walking = !paused && !dino.IsDead && dino.IsMoving;

        float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
        level = Mathf.MoveTowards(level, walking ? 1f : 0f, step);
        source.volume = level * volume;
        source.pitch = dino.IsSprinting ? sprintSpeed : 1f;

        if (level > 0f && !sounding)
        {
            if (source.time > 0f) source.UnPause(); else source.Play(); // carry on the steps where they stopped
            sounding = true;
        }
        else if (level <= 0f && sounding)
        {
            source.Pause();
            sounding = false;
        }
    }

    void OnDisable()
    {
        if (source != null) source.Pause();
        sounding = false;
        level = 0f;
    }
}
