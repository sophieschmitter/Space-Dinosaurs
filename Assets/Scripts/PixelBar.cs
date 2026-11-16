using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A pixel-art bar for the HUD: a fill block stretched to the current length with a cap on its end, inside a frame.
/// The length is rounded to whole art pixels so it never looks blurry. The fill, cap and icon art can change with
/// the value (e.g. green when full, purple when critical): see States.
/// Something else sets Value (0-1): HealthBar for health, LevelTimer for the timer.
/// Built by Tools > Space Dinosaurs > Build HUD Bars.
/// </summary>
public class PixelBar : MonoBehaviour
{
    [System.Serializable]
    public class State
    {
        [Tooltip("Used while the value is at or above this (0-1). Put states highest first.")]
        [Range(0f, 1f)] public float atLeast;
        public Sprite fill, cap, icon;
    }

    [Range(0f, 1f)] public float value = 1f;

    [Tooltip("The fill block (stretched sideways). Its RectTransform is anchored on the left of the bar's opening.")]
    public Image fill;
    [Tooltip("The end piece placed right after the fill.")]
    public Image cap;
    [Tooltip("Optional icon whose art follows the state (the heart).")]
    public Image icon;

    [Tooltip("Length of the opening when full, in art pixels (fill + cap).")]
    public int maxLength = 86;
    [Tooltip("Width of the cap, in art pixels.")]
    public int capWidth = 2;
    [Tooltip("HUD units per art pixel (2 matches the game's pixel size on the 640 x 360 HUD).")]
    public float unitsPerArtPixel = 2f;

    [Tooltip("Highest first. The first one the value reaches is used.")]
    public State[] states;

    public float Value { get => value; set => this.value = Mathf.Clamp01(value); }

    void LateUpdate() => Apply();
    void OnValidate() => Apply();

    public void Apply()
    {
        if (fill == null) return;
        var state = StateFor(value);
        if (state != null)
        {
            if (state.fill != null) fill.sprite = state.fill;
            if (cap != null && state.cap != null) cap.sprite = state.cap;
            if (icon != null && state.icon != null) icon.sprite = state.icon;
        }

        int length = Mathf.RoundToInt(value * maxLength);
        if (value > 0f) length = Mathf.Max(length, capWidth); // anything left shows at least the cap
        int cw = cap != null ? Mathf.Min(capWidth, length) : 0;
        int body = length - cw;

        fill.enabled = body > 0;
        var f = fill.rectTransform;
        f.sizeDelta = new Vector2(body * unitsPerArtPixel, f.sizeDelta.y);
        if (cap != null)
        {
            cap.enabled = cw > 0;
            var c = cap.rectTransform;
            c.anchoredPosition = new Vector2(f.anchoredPosition.x + body * unitsPerArtPixel, f.anchoredPosition.y);
            c.sizeDelta = new Vector2(cw * unitsPerArtPixel, c.sizeDelta.y);
        }
    }

    State StateFor(float v)
    {
        if (states == null || states.Length == 0) return null;
        foreach (var s in states) if (v >= s.atLeast) return s;
        return states[states.Length - 1];
    }
}
