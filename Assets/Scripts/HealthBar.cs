using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a Health as a bar that empties as damage is taken. Finds the dino (or, with For Robot, the drill robot)
/// automatically if no target is set. Drives a PixelBar (the pixel-art bars) if one is set, otherwise a plain
/// Image set to "Filled".
/// </summary>
public class HealthBar : MonoBehaviour
{
    [Tooltip("Whose health to show. Left empty = the dino.")]
    public Health target;

    [Tooltip("With no Target: follow the drill robot (the EnemyGoal) instead of the dino.")]
    public bool forRobot;

    [Tooltip("The pixel-art bar to drive (its states pick the colours). Leave empty to use Fill instead.")]
    public PixelBar bar;

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
            Component owner = forRobot ? FindFirstObjectByType<EnemyGoal>() : FindFirstObjectByType<DinoController>();
            if (owner != null) target = owner.GetComponent<Health>();
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
        if (bar != null) bar.Value = shown;
        if (fill == null) return;
        fill.fillAmount = shown;
        fill.color = Color.Lerp(lowColor, fullColor, shown);
    }
}
