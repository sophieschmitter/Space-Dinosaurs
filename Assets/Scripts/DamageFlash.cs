using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Visual hit feedback that works with any art (block placeholders or real sprites):
/// tints the sprites briefly when hit, then blinks them while the Health is invulnerable.
/// </summary>
[RequireComponent(typeof(Health))]
public class DamageFlash : MonoBehaviour
{
    public Color flashColor = new Color(1f, 0.35f, 0.35f);
    [Min(0f)] public float flashTime = 0.1f;
    [Tooltip("Blink on/off while invulnerable after a hit.")]
    public bool blinkWhileInvulnerable = true;
    [Min(0.01f)] public float blinkInterval = 0.08f;

    [Tooltip("Sprites to flash. Left empty = every SpriteRenderer on this object and its children except ones named Shadow.")]
    public SpriteRenderer[] renderers;

    Health health;
    Color[] baseColors;

    void Awake()
    {
        health = GetComponent<Health>();
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name != "Shadow").ToArray();
        baseColors = renderers.Select(r => r.color).ToArray();
    }

    void OnEnable() { if (health != null) health.Damaged += OnDamaged; }
    void OnDisable() { if (health != null) health.Damaged -= OnDamaged; Restore(); }

    void OnDamaged(int amount, Vector2 from)
    {
        StopAllCoroutines();
        StartCoroutine(Flash());
    }

    IEnumerator Flash()
    {
        for (int i = 0; i < renderers.Length; i++) renderers[i].color = baseColors[i] * flashColor;
        yield return new WaitForSeconds(flashTime);
        for (int i = 0; i < renderers.Length; i++) renderers[i].color = baseColors[i];

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

    void Restore()
    {
        if (baseColors == null) return;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].color = baseColors[i];
    }
}
