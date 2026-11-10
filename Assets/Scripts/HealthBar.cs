using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a Health as a bar that empties as damage is taken. Finds the dino automatically if no target is set.
/// The fill is an Image set to "Filled", so swapping in your own bar art only means changing its sprite.
/// </summary>
public class HealthBar : MonoBehaviour
{
    [Tooltip("Whose health to show. Left empty = the dino.")]
    public Health target;

    [Tooltip("Image with Image Type = Filled; its Fill Amount shows the health.")]
    public Image fill;

    [Tooltip("Fill colour when health is full and when it's nearly empty (blended in between).")]
    public Color fullColor = new Color(0.35f, 0.85f, 0.4f), lowColor = new Color(0.9f, 0.25f, 0.25f);

    [Tooltip("Seconds the bar takes to slide to a new value (0 = instant).")]
    [Min(0f)] public float smoothTime = 0.15f;

    float shown = 1f, velocity;

    void Start()
    {
        if (target == null)
        {
            var dino = FindFirstObjectByType<DinoController>();
            if (dino != null) target = dino.GetComponent<Health>();
        }
        shown = Fraction();
        Apply();
    }

    void Update()
    {
        // Unscaled time so the bar still settles while the game is paused at the end of the level.
        shown = smoothTime > 0f
            ? Mathf.SmoothDamp(shown, Fraction(), ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime)
            : Fraction();
        Apply();
    }

    float Fraction() => target != null && target.maxHealth > 0 ? (float)target.Current / target.maxHealth : 0f;

    void Apply()
    {
        if (fill == null) return;
        fill.fillAmount = shown;
        fill.color = Color.Lerp(lowColor, fullColor, shown);
    }
}
