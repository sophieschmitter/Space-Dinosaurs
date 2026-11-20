using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD readout of the dino's gun ammo (DinoController.Ammo, 100 shots per level). Turns red when it's running
/// low and blinks when it's empty. Hidden when the gun has unlimited ammo.
/// </summary>
public class AmmoCounter : MonoBehaviour
{
    public DinoController dino;
    public Text label;
    [Tooltip("How the number is written; {0} is the shots left.")]
    public string format = "{0}";
    [Tooltip("Shown in Low Color at or below this many shots.")]
    public int lowAt = 15;
    public Color normalColor = Color.white;
    public Color lowColor = new Color(1f, 0.42f, 0.42f);
    [Tooltip("Everything to hide when the gun has unlimited ammo (the icon and the number).")]
    public GameObject root;

    void Start()
    {
        if (dino == null) dino = FindFirstObjectByType<DinoController>();
        if (dino == null) { gameObject.SetActive(false); return; }
        dino.AmmoChanged += Show;
        if (root != null) root.SetActive(!dino.UnlimitedAmmo);
        Show(dino.Ammo);
    }

    void OnDestroy() { if (dino != null) dino.AmmoChanged -= Show; }

    void Show(int ammo)
    {
        if (label == null) return;
        label.text = string.Format(format, ammo);
        label.color = ammo <= lowAt ? lowColor : normalColor;
    }

    void Update()
    {
        if (label == null || dino == null || dino.Ammo > 0) return;
        label.enabled = Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.4f; // empty: blink
    }
}
