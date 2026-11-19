using System.Collections;
using UnityEngine;

/// <summary>
/// The drill's sounds when it hits breakable tiles (DestructibleTilemap.AnyHit):
///   rock hit or broken, or ore hit but not broken yet - Hit Sound (cut short to Hit Length, faded out);
///   ore broken                                       - Ore Break Sound.
/// Each hit restarts the hit sound instead of stacking another copy, so fast drilling doesn't turn to mush.
/// The ore break sound has its own channel, so the next hit doesn't cut it off.
/// </summary>
public class DrillSounds : MonoBehaviour
{
    [Header("Rock, and ore not broken yet")]
    public AudioClip hitSound;
    [Range(0f, 1f)] public float hitVolume = 0.5f;
    [Tooltip("Seconds of the hit sound to play (the file is a long crumble).")]
    [Min(0.05f)] public float hitLength = 0.45f;
    [Tooltip("Skip this much silence at the start of the file (seconds).")]
    [Min(0f)] public float hitStart = 0.06f;

    [Header("Ore broken")]
    public AudioClip oreBreakSound;
    [Range(0f, 1f)] public float oreBreakVolume = 0.6f;
    [Min(0.05f)] public float oreBreakLength = 1.1f;
    [Min(0f)] public float oreBreakStart = 0.04f;

    [Tooltip("Fade at the end of each cut, so it doesn't click (seconds).")]
    [Min(0f)] public float fadeOut = 0.12f;
    [Tooltip("Random pitch change per hit, so repeats don't sound identical.")]
    [Range(0f, 0.2f)] public float pitchJitter = 0.06f;

    AudioSource hitSource, breakSource;
    Coroutine hitFade, breakFade;

    void Awake()
    {
        hitSource = NewSource();
        breakSource = NewSource();
    }

    AudioSource NewSource()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = false;
        s.spatialBlend = 0f; // 2D: the camera follows the dino
        return s;
    }

    void OnEnable() => DestructibleTilemap.AnyHit += OnHit;
    void OnDisable() => DestructibleTilemap.AnyHit -= OnHit;

    void OnHit(Vector3 where, bool ore, bool broke)
    {
        if (ore && broke) Play(breakSource, oreBreakSound, oreBreakVolume, oreBreakStart, oreBreakLength, ref breakFade);
        else Play(hitSource, hitSound, hitVolume, hitStart, hitLength, ref hitFade);
    }

    void Play(AudioSource source, AudioClip clip, float volume, float start, float length, ref Coroutine fade)
    {
        if (clip == null) return;
        if (fade != null) StopCoroutine(fade);
        source.Stop();
        source.clip = clip;
        source.volume = volume;
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.time = Mathf.Min(start, clip.length - 0.01f);
        source.Play();
        fade = StartCoroutine(FadeOut(source, volume, length));
    }

    IEnumerator FadeOut(AudioSource source, float volume, float length)
    {
        float hold = Mathf.Max(0f, length - fadeOut);
        yield return new WaitForSeconds(hold);
        for (float t = 0f; t < fadeOut; t += Time.deltaTime)
        {
            source.volume = volume * (1f - t / fadeOut);
            yield return null;
        }
        source.Stop();
        source.volume = volume;
    }
}
