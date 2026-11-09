using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Smoothly follows a target across a map larger than the screen, always landing on whole art pixels
/// so the Pixel Perfect Camera never shimmers. Optionally stops at the edges of a tilemap.
/// Put it on the camera that has the Pixel Perfect Camera component.
/// </summary>
[RequireComponent(typeof(Camera))]
public class PixelCameraFollow : MonoBehaviour
{
    [Tooltip("What the camera follows, usually the player.")]
    public Transform target;

    [Tooltip("Shift from the target, in world units (1 unit = 1 tile).")]
    public Vector2 offset = new Vector2(0f, 1f);

    [Tooltip("Seconds to catch up with the target. 0 = locked to the target.")]
    [Min(0f)] public float smoothTime = 0.15f;

    [Tooltip("Optional: keep the view inside this tilemap's painted area.")]
    public Tilemap mapBounds;

    [Tooltip("Art pixels per world unit. Must match the Pixel Perfect Camera's Assets PPU.")]
    public int pixelsPerUnit = 16;

    Camera cam;
    Vector3 velocity;
    Vector3 smoothPosition; // unrounded, so smoothing doesn't stall on the pixel grid

    void Awake()
    {
        cam = GetComponent<Camera>();
        smoothPosition = transform.position;
    }

    void Start()
    {
        if (target != null) smoothPosition = Desired(); // start already framed on the target
        transform.position = Snap(smoothPosition);
    }

    // LateUpdate so the target has finished moving (and physics interpolation applied) this frame.
    void LateUpdate()
    {
        if (target == null) return;

        smoothPosition = smoothTime > 0f
            ? Vector3.SmoothDamp(smoothPosition, Desired(), ref velocity, smoothTime)
            : Desired();

        transform.position = Snap(Clamp(smoothPosition));
    }

    Vector3 Desired() =>
        new Vector3(target.position.x + offset.x, target.position.y + offset.y, transform.position.z);

    Vector3 Clamp(Vector3 p)
    {
        if (mapBounds == null) return p;

        mapBounds.CompressBounds();
        Bounds b = mapBounds.localBounds;
        b.center += mapBounds.transform.position;
        if (b.size.x <= 0f || b.size.y <= 0f) return p; // nothing painted yet: don't lock the camera

        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        // If the map is smaller than the view on an axis, centre on it instead of clamping.
        p.x = b.size.x <= halfW * 2f ? b.center.x : Mathf.Clamp(p.x, b.min.x + halfW, b.max.x - halfW);
        p.y = b.size.y <= halfH * 2f ? b.center.y : Mathf.Clamp(p.y, b.min.y + halfH, b.max.y - halfH);
        return p;
    }

    Vector3 Snap(Vector3 p) => new Vector3(
        Mathf.Round(p.x * pixelsPerUnit) / pixelsPerUnit,
        Mathf.Round(p.y * pixelsPerUnit) / pixelsPerUnit,
        p.z);
}
