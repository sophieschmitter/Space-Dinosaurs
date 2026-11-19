using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Marks on the level timer bar when each enemy wave comes: a small arrow under the bar pointing up at the
/// moment the wave starts (from every EnemySpawner's WaveTimes, against LevelTimer's Level Length).
/// The arrow blinks for the last Warn Time seconds before its wave and goes grey once the wave has come.
/// Sits on the timer bar (PixelBar), in pixel art steps (Units Per Art Pixel) so it stays crisp.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class WaveMarkers : MonoBehaviour
{
    public LevelTimer timer;
    [Tooltip("The timer bar: its fill's left edge and Max Length give where 0% and 100% are.")]
    public PixelBar bar;

    public Color upcoming = new Color32(0xE0, 0x47, 0x9E, 0xFF);   // the game's pink
    public Color passed = new Color32(0x8A, 0x83, 0xA6, 0xFF);
    [Tooltip("Outline around the arrow so it shows over anything behind the HUD.")]
    public Color outline = new Color32(0x1E, 0x1A, 0x33, 0xFF);
    [Tooltip("Seconds before a wave that its arrow starts blinking.")]
    [Min(0f)] public float warnTime = 8f;
    [Tooltip("Blinks per second while warning.")]
    [Min(0.1f)] public float blinkRate = 3f;
    [Tooltip("Gap between the bottom of the bar and the arrow tip, in art pixels.")]
    public int gapBelow = 1;
    [Tooltip("Waves from different spawners this close together (seconds) share one arrow.")]
    public float mergeWithin = 1f;

    class Marker { public float time; public List<Image> parts = new List<Image>(); }
    readonly List<Marker> markers = new List<Marker>();

    void Start()
    {
        if (timer == null) timer = FindFirstObjectByType<LevelTimer>();
        if (bar == null && timer != null) bar = timer.timerBar;
        if (timer == null || bar == null || bar.fill == null) return;

        var times = new List<float>();
        foreach (var s in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
            if (s.isActiveAndEnabled) times.AddRange(s.WaveTimes());
        times.Sort();
        float last = float.NegativeInfinity;
        foreach (float t in times)
        {
            if (t - last < mergeWithin || t >= timer.levelLength) continue;
            last = t;
            markers.Add(Build(t));
        }
    }

    // An arrow pointing up, rows 1, 3 and 5 art px wide, with a 1 px dark outline all round.
    Marker Build(float time)
    {
        float u = bar.unitsPerArtPixel;
        var fillRt = bar.fill.rectTransform;
        float startX = fillRt.anchoredPosition.x;
        float barBottom = fillRt.anchoredPosition.y - fillRt.sizeDelta.y; // fill is anchored top-left
        float fraction = time / timer.levelLength;
        float x = startX + Mathf.Round(fraction * bar.maxLength) * u;  // whole art pixels: the tip's left edge
        float top = barBottom - (gapBelow + 1) * u;                     // below the bar's frame

        var m = new Marker { time = time };
        const int rows = 3;
        // outline first (behind): one px bigger than each row on every side
        Pixel($"Wave {time:0}s outline", x - u, top + u, 3, 1, fillRt, outline);
        for (int i = 0; i < rows; i++)
            Pixel($"Wave {time:0}s outline", x - (i + 1) * u, top - i * u, 3 + 2 * i, 1, fillRt, outline);
        Pixel($"Wave {time:0}s outline", x - rows * u, top - rows * u, 1 + 2 * rows, 1, fillRt, outline);
        for (int i = 0; i < rows; i++)
            m.parts.Add(Pixel($"Wave {time:0}s", x - i * u, top - i * u, 1 + 2 * i, 1, fillRt, upcoming));
        return m;
    }

    Image Pixel(string name, float x, float y, int w, int h, RectTransform like, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(bar.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = like.anchorMin;
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w * bar.unitsPerArtPixel, h * bar.unitsPerArtPixel);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.color = color;
        return img;
    }

    void Update()
    {
        if (timer == null) return;
        float now = timer.Elapsed;
        bool blinkOn = Mathf.Repeat(Time.unscaledTime * blinkRate, 1f) < 0.5f;
        foreach (var m in markers)
        {
            bool done = now >= m.time;
            bool warning = !done && m.time - now <= warnTime;
            var c = done ? passed : upcoming;
            bool visible = !warning || blinkOn || timer.IsOver;
            foreach (var p in m.parts)
            {
                p.color = c;
                p.enabled = visible;
            }
        }
    }
}
