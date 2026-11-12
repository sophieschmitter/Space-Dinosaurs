using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Twinkling pixel stars that drift slowly sideways. Put it on a full-screen UI object; it fills that area
/// with square stars when the game starts (bigger stars drift faster, for a bit of depth).
/// Stars are placed by percentage of the area, so they fit any screen size.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class Starfield : MonoBehaviour
{
    public int starCount = 140;
    [Tooltip("Star sizes in UI pixels; one is picked at random per star (repeat a size to make it more common).")]
    public float[] sizes = { 4f, 4f, 4f, 4f, 8f, 8f, 12f };
    public Color[] colors =
    {
        new Color32(0xFF, 0xFF, 0xFF, 0xFF),
        new Color32(0xB9, 0xA6, 0xFF, 0xFF),
        new Color32(0x7F, 0xE3, 0xF0, 0xFF),
        new Color32(0xF5, 0xC5, 0x42, 0xFF),
    };
    [Tooltip("Screen widths per second that the biggest stars drift left. 0 = still.")]
    public float driftSpeed = 0.01f;
    [Tooltip("Twinkles per second, random between these.")]
    public Vector2 twinkleSpeed = new Vector2(0.3f, 1.5f);
    [Range(0f, 1f)] public float minBrightness = 0.25f;

    struct Star
    {
        public RectTransform rect;
        public Image image;
        public Vector2 pos;
        public float speed, phase, twinkle;
        public Color color;
    }

    Star[] stars;

    void Start()
    {
        float biggest = 1f;
        foreach (float s in sizes) biggest = Mathf.Max(biggest, s);

        stars = new Star[starCount];
        for (int i = 0; i < starCount; i++)
        {
            var go = new GameObject("Star", typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            float size = sizes.Length > 0 ? sizes[Random.Range(0, sizes.Length)] : 4f;
            rect.sizeDelta = new Vector2(size, size);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;

            stars[i] = new Star
            {
                rect = rect,
                image = image,
                pos = new Vector2(Random.value, Random.value),
                speed = driftSpeed * size / biggest,
                phase = Random.value * Mathf.PI * 2f,
                twinkle = Random.Range(twinkleSpeed.x, twinkleSpeed.y),
                color = colors.Length > 0 ? colors[Random.Range(0, colors.Length)] : Color.white,
            };
            Place(ref stars[i]);
        }
    }

    void Update()
    {
        if (stars == null) return;
        float t = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < stars.Length; i++)
        {
            ref Star s = ref stars[i];
            s.pos.x -= s.speed * dt;
            if (s.pos.x < -0.02f) { s.pos.x += 1.04f; s.pos.y = Random.value; }
            Place(ref s);

            // Step the brightness in a few levels so it flickers like pixel art instead of fading smoothly.
            float wave = 0.5f + 0.5f * Mathf.Sin(t * s.twinkle * Mathf.PI * 2f + s.phase);
            float a = Mathf.Lerp(minBrightness, 1f, Mathf.Round(wave * 3f) / 3f);
            var c = s.color;
            c.a *= a;
            s.image.color = c;
        }
    }

    static void Place(ref Star s)
    {
        s.rect.anchorMin = s.rect.anchorMax = s.pos;
        s.rect.anchoredPosition = Vector2.zero;
    }
}
