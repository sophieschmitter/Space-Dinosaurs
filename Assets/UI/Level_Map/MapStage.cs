using System.Collections;
using UnityEngine;

/// <summary>
/// One stop on the level map (an asteroid). Put it on a sprite and fill in its name and the scene it loads.
/// The Level Map decides whether it's locked, open or cleared and shows it:
/// locked = dark silhouette, open (the next level to play) = flashing and bobbing, cleared = normal.
/// The yellow circle in the Scene view is where the path joins this stage and where the dino stands.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class MapStage : MonoBehaviour
{
    public enum State { Locked, Open, Cleared }

    [Tooltip("Shown in the bar at the bottom, e.g. 'Purple Rock'.")]
    public string displayName = "Stage";
    [Tooltip("Scene this stage loads, spelled exactly like the scene file. Leave empty for 'coming soon'.")]
    public string sceneName = "";
    [Tooltip("Where the path joins and the dino stands, relative to the middle of the sprite.")]
    public Vector2 standOffset = Vector2.zero;

    [Header("Looks")]
    [Tooltip("Colour of a locked stage (multiplies the sprite).")]
    public Color lockedTint = new Color(0.2f, 0.18f, 0.3f, 1f);
    [Tooltip("How much bigger an open stage gets when it pulses (0.06 = 6%).")]
    public float pulseAmount = 0.06f;
    [Tooltip("Pulses per second.")]
    public float pulseSpeed = 1.5f;

    public State Current { get; private set; }
    public bool HasScene => !string.IsNullOrEmpty(sceneName);
    public Vector3 StandPoint => transform.position + (Vector3)standOffset;
    /// <summary>Roughly how far the sprite reaches from its middle, in world units.</summary>
    public float Radius => sr != null ? Mathf.Min(sr.bounds.extents.x, sr.bounds.extents.y) : 0.5f;

    SpriteRenderer sr;
    Vector3 baseScale;
    Vector3 basePosition;
    Color baseColor;
    bool pulsing;
    Coroutine shaking;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
        basePosition = transform.localPosition;
        baseColor = sr.color;
    }

    public void SetState(State state, bool pulse)
    {
        if (sr == null) Awake();
        Current = state;
        pulsing = pulse;
        sr.color = state == State.Locked ? lockedTint : baseColor;
        if (!pulse) transform.localScale = baseScale;
    }

    /// <summary>A quick side-to-side shake: "you can't go here yet".</summary>
    public void Shake()
    {
        if (shaking != null) StopCoroutine(shaking);
        shaking = StartCoroutine(ShakeRoutine());
    }

    /// <summary>True if this world point is on the sprite (its rectangle).</summary>
    public bool Contains(Vector2 world)
    {
        if (sr == null) return false;
        var b = sr.bounds;
        return world.x >= b.min.x && world.x <= b.max.x && world.y >= b.min.y && world.y <= b.max.y;
    }

    void Update()
    {
        if (!pulsing) return;
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f);
        wave = Mathf.Round(wave * 4f) / 4f; // step it, so it moves like pixel art
        transform.localScale = baseScale * (1f + pulseAmount * wave);
        sr.color = Color.Lerp(baseColor * new Color(0.7f, 0.7f, 0.7f, 1f), baseColor, wave);
    }

    IEnumerator ShakeRoutine()
    {
        float[] offsets = { 0.25f, -0.25f, 0.18f, -0.18f, 0.1f, 0f };
        foreach (float x in offsets)
        {
            transform.localPosition = basePosition + new Vector3(x, 0f, 0f);
            yield return new WaitForSecondsRealtime(0.04f);
        }
        transform.localPosition = basePosition;
        shaking = null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + (Vector3)standOffset, 0.25f);
    }
}
