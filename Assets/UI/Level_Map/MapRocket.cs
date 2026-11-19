using System.Collections;
using UnityEngine;

/// <summary>
/// The rocket ship on the level map. The Level Map tells it where to go: the first time, it flies in from off
/// the left of the screen and lands on the asteroid you picked; after that it flies from asteroid to asteroid,
/// in a gentle arc, and lands on top of each one. It faces the way it's flying (the art points right).
/// Coming back from a level, it's already sitting on that level's asteroid.
/// </summary>
public class MapRocket : MonoBehaviour
{
    [Tooltip("Flying speed in world units per second.")]
    public float speed = 9f;
    [Tooltip("Shortest and longest a flight may take (seconds).")]
    public Vector2 flightTime = new Vector2(0.7f, 2f);
    [Tooltip("How high the flight arcs above a straight line, per unit flown (0.15 = 15%).")]
    public float arc = 0.15f;
    [Tooltip("Gap between the bottom of the ship and the top of the asteroid when landed (can be negative to sink in).")]
    public float landingGap = -0.15f;
    [Tooltip("Distance below the ship's middle to the bottom of its body, before scaling (the art has empty space under it).")]
    public float bodyBottom = 2.2f;

    public bool IsMoving { get; private set; }
    /// <summary>Which stage it's on (-1 = not on the map yet).</summary>
    public int AtStage { get; private set; } = -1;

    SpriteRenderer sr;
    Coroutine flight;

    void Awake()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        var anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.updateMode = AnimatorUpdateMode.UnscaledTime;
    }

    /// <summary>Where it sits when landed on this stage.</summary>
    public Vector3 LandingPoint(MapStage stage)
    {
        var top = stage.GetComponent<SpriteRenderer>().bounds.max.y;
        float lift = bodyBottom * transform.lossyScale.y;
        return new Vector3(stage.transform.position.x, top + landingGap + lift, transform.position.z);
    }

    /// <summary>Not on the map yet: hidden until it flies in.</summary>
    public void Hide()
    {
        Stop();
        AtStage = -1;
        if (sr != null) sr.enabled = false;
    }

    /// <summary>Sit on this stage straight away.</summary>
    public void PlaceOn(MapStage stage, int index)
    {
        Stop();
        if (sr != null) sr.enabled = true;
        transform.position = LandingPoint(stage);
        AtStage = index;
    }

    /// <summary>Fly to this stage (from off the left of the screen if it isn't on the map yet).</summary>
    public void FlyTo(MapStage stage, int index, Camera cam)
    {
        Stop();
        Vector3 to = LandingPoint(stage);
        Vector3 from = transform.position;
        if (AtStage < 0 || sr == null || !sr.enabled)
        {
            float left = cam != null ? cam.transform.position.x - cam.orthographicSize * cam.aspect : to.x - 12f;
            from = new Vector3(left - 3f, to.y + 2.5f, to.z);
        }
        if (sr != null) sr.enabled = true;
        AtStage = index;
        flight = StartCoroutine(Fly(from, to));
    }

    void Stop()
    {
        if (flight != null) StopCoroutine(flight);
        flight = null;
        IsMoving = false;
    }

    IEnumerator Fly(Vector3 from, Vector3 to)
    {
        IsMoving = true;
        float distance = Vector3.Distance(from, to);
        float duration = Mathf.Clamp(distance / Mathf.Max(0.1f, speed), flightTime.x, flightTime.y);
        Vector3 control = (from + to) * 0.5f + Vector3.up * distance * arc; // the top of the arc
        Face(to.x - from.x);
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / duration); // eases off for the landing
            Vector3 a = Vector3.Lerp(from, control, u), b = Vector3.Lerp(control, to, u);
            transform.position = Snap(Vector3.Lerp(a, b, u));
            yield return null;
        }
        transform.position = to;
        IsMoving = false;
        flight = null;
    }

    void Face(float dx)
    {
        if (sr != null && Mathf.Abs(dx) > 0.01f) sr.flipX = dx < 0f;
    }

    // Moves in whole art pixels, so the pixel art doesn't shimmer as it flies.
    Vector3 Snap(Vector3 p)
    {
        float ppu = sr != null && sr.sprite != null ? sr.sprite.pixelsPerUnit / 10f * Mathf.Max(0.01f, 1f / transform.lossyScale.x) : 16f;
        if (ppu <= 0f) return p;
        return new Vector3(Mathf.Round(p.x * ppu) / ppu, Mathf.Round(p.y * ppu) / ppu, p.z);
    }
}
