using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Visual hit feedback that works with any art (block placeholders or real sprites). Two styles:
///   Tint  - multiplies the sprites by Flash Color for a moment (can only darken/recolour).
///   White - shows the sprite as a solid white silhouette for a moment (needs White Material).
/// Optionally blinks the sprites while the Health is invulnerable afterwards.
/// </summary>
[RequireComponent(typeof(Health))]
public class DamageFlash : MonoBehaviour
{
    public enum Style { Tint, White }

    public Style style = Style.Tint;
    [Tooltip("Tint style: colour multiplied onto the sprites.")]
    public Color flashColor = new Color(1f, 0.35f, 0.35f);
    [Tooltip("White style: material that draws a sprite as a solid colour (Assets/Placeholders/WhiteFlash.mat).")]
    public Material whiteMaterial;
    [Min(0f)] public float flashTime = 0.1f;
    [Tooltip("Blink on/off while invulnerable after a hit.")]
    public bool blinkWhileInvulnerable = true;
    [Min(0.01f)] public float blinkInterval = 0.08f;

    [Tooltip("Sprites to flash. Left empty = every SpriteRenderer on this object and its children except ones named Shadow.")]
    public SpriteRenderer[] renderers;

    Health health;
    Color[] baseColors;
    SpriteRenderer[] overlays; // white silhouettes drawn on top of each sprite during a White flash

    void Awake()
    {
        health = GetComponent<Health>();
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name != "Shadow").ToArray();
        baseColors = renderers.Select(r => r.color).ToArray();

        if (style == Style.White && whiteMaterial != null)
        {
            overlays = new SpriteRenderer[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var go = new GameObject("White Flash");
                go.transform.SetParent(renderers[i].transform, false);
                var o = go.AddComponent<SpriteRenderer>();
                o.sharedMaterial = whiteMaterial;
                o.color = Color.white;
                o.sortingLayerID = renderers[i].sortingLayerID;
                o.sortingOrder = renderers[i].sortingOrder + 1;
                o.enabled = false;
                overlays[i] = o;
            }
        }
    }

    void OnEnable() { if (health != null) health.Damaged += OnDamaged; }
    void OnDisable() { if (health != null) health.Damaged -= OnDamaged; Restore(); }

    void OnDamaged(int amount, Vector2 from)
    {
        StopAllCoroutines();
        Restore();
        StartCoroutine(Flash());
    }

    // Keep the silhouettes matching the (animated, possibly flipped) sprites while they're showing.
    void LateUpdate()
    {
        if (overlays == null) return;
        for (int i = 0; i < overlays.Length; i++)
        {
            if (!overlays[i].enabled) continue;
            overlays[i].sprite = renderers[i].sprite;
            overlays[i].flipX = renderers[i].flipX;
            overlays[i].flipY = renderers[i].flipY;
        }
    }

    IEnumerator Flash()
    {
        bool white = style == Style.White && overlays != null;
        if (white) { SetOverlays(true); LateUpdate(); }
        else for (int i = 0; i < renderers.Length; i++) renderers[i].color = baseColors[i] * flashColor;

        yield return new WaitForSeconds(flashTime);
        Restore();

        if (!blinkWhileInvulnerable || health.IsDead) yield break;
        bool hidden = false;
        while (health.IsInvulnerable)
        {
            hidden = !hidden;
            foreach (var r in renderers) r.color = new Color(r.color.r, r.color.g, r.color.b, hidden ? 0.25f : 1f);
            yield return new WaitForSeconds(blinkInterval);
        }
        Restore();
    }

    void SetOverlays(bool on)
    {
        if (overlays == null) return;
        foreach (var o in overlays) if (o != null) o.enabled = on;
    }

    void Restore()
    {
        SetOverlays(false);
        if (baseColors == null) return;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].color = baseColors[i];
    }
}
