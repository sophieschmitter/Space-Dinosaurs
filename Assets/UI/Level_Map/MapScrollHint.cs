using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A temporary hint on the level map: "DRAG TO SEE MORE PLANETS" with a blinking arrow, shown while there's more
/// map off to the right and until the player first drags or scrolls (MapCamera.HasScrolled).
/// </summary>
public class MapScrollHint : MonoBehaviour
{
    public MapCamera mapCamera;
    public Text label;
    [Tooltip("Blinks per second for the arrow.")]
    public float blinkRate = 1.5f;
    public string message = "DRAG TO SEE MORE PLANETS";

    void Start()
    {
        if (mapCamera == null) mapCamera = FindFirstObjectByType<MapCamera>();
        if (label == null) label = GetComponentInChildren<Text>(true);
    }

    void Update()
    {
        if (label == null) return;
        bool show = mapCamera == null || (!mapCamera.HasScrolled && mapCamera.MoreToTheRight);
        label.enabled = show;
        if (!show) return;
        bool arrowOn = Mathf.Repeat(Time.unscaledTime * blinkRate, 1f) < 0.6f;
        label.text = message + (arrowOn ? "  >" : "   ");
    }
}
