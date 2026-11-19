using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Side-scrolling camera for the level map. Drag with the mouse (or a finger) to look around, or use the
/// scroll wheel / trackpad; it glides to whichever stage gets selected and never scrolls past the map's edges.
/// A click that isn't a drag is passed on to the Level Map (Clicked), which works out which stage was clicked.
/// With a Background set, the camera fits that image's height to the screen and uses its edges;
/// otherwise it uses View Height and Min X / Max X.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MapCamera : MonoBehaviour
{
    [Tooltip("Optional: the map's background image. The camera fits its height and stops at its edges.")]
    public SpriteRenderer background;

    [Header("Without a background")]
    [Tooltip("How much of the map shows top to bottom, in world units. 15 = 240 art pixels.")]
    public float viewHeight = 15f;
    [Tooltip("Left edge of the map (world X).")]
    public float minX = -22f;
    [Tooltip("Right edge of the map (world X).")]
    public float maxX = 64f;

    [Header("Feel")]
    [Tooltip("Seconds to glide to a newly selected stage.")]
    public float smoothTime = 0.3f;
    [Tooltip("World units scrolled per mouse-wheel notch.")]
    public float wheelStep = 2f;
    [Tooltip("Screen pixels the pointer must move before a press counts as a drag instead of a click.")]
    public float dragThreshold = 12f;

    /// <summary>Called with the world position of a click (a press and release without dragging).</summary>
    public event Action<Vector2> Clicked;

    Camera cam;
    float targetX, velocity, dragStartCamX;
    bool pressed, pressedOverUI, dragging;
    Vector2 pressPosition;

    float Left => background != null ? background.bounds.min.x : minX;
    float Right => background != null ? background.bounds.max.x : maxX;
    float HalfWidth => cam.orthographicSize * cam.aspect;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        FitHeight();
        targetX = transform.position.x;
    }

    /// <summary>Glide (or jump, with instant) so this world X is in the middle of the screen, as far as the edges allow.</summary>
    public void FocusOn(float x, bool instant = false)
    {
        if (cam == null) Awake();
        targetX = Clamp(x);
        if (instant)
        {
            velocity = 0f;
            SetX(targetX);
        }
    }

    void Update()
    {
        FitHeight();
        HandlePointer();
        HandleWheel();

        targetX = Clamp(targetX);
        if (!dragging)
            SetX(Mathf.SmoothDamp(transform.position.x, targetX, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime));
    }

    void HandlePointer()
    {
        var pointer = Pointer.current;
        if (pointer == null) return;
        bool down = pointer.press.isPressed;
        Vector2 pos = pointer.position.ReadValue();

        if (down && !pressed)
        {
            pressed = true;
            dragging = false;
            pressPosition = pos;
            dragStartCamX = transform.position.x;
            // Presses that start on a button belong to the UI, not the map.
            pressedOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
        else if (down && pressed && !pressedOverUI)
        {
            if (!dragging && (pos - pressPosition).magnitude > dragThreshold) dragging = true;
            if (dragging)
            {
                float unitsPerPixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
                targetX = Clamp(dragStartCamX - (pos.x - pressPosition.x) * unitsPerPixel);
                velocity = 0f;
                SetX(targetX);
            }
        }
        else if (!down && pressed)
        {
            if (!dragging && !pressedOverUI) Clicked?.Invoke(cam.ScreenToWorldPoint(pos));
            pressed = false;
            dragging = false;
        }
    }

    void HandleWheel()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 scroll = mouse.scroll.ReadValue();
        float amount = scroll.x - scroll.y; // wheel down or swipe right = move right
        if (Mathf.Approximately(amount, 0f)) return;
        // Wheels report different sizes per notch on different computers; treat anything big as one notch.
        amount = Mathf.Sign(amount) * Mathf.Min(Mathf.Abs(amount), 1f);
        targetX = Clamp(targetX + amount * wheelStep);
    }

    void FitHeight()
    {
        float height = background != null ? background.bounds.size.y : viewHeight;
        if (height > 0f) cam.orthographicSize = height * 0.5f;
        if (background != null)
        {
            var p = transform.position;
            p.y = background.bounds.center.y;
            transform.position = p;
        }
    }

    float Clamp(float x)
    {
        float lo = Left + HalfWidth, hi = Right - HalfWidth;
        return lo > hi ? (Left + Right) * 0.5f : Mathf.Clamp(x, lo, hi);
    }

    void SetX(float x)
    {
        var p = transform.position;
        p.x = x;
        transform.position = p;
    }

    void OnDrawGizmosSelected()
    {
        if (background != null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(new Vector3((minX + maxX) * 0.5f, transform.position.y, 0f), new Vector3(maxX - minX, viewHeight, 0f));
    }
}
