using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put on a HUD button: clicking it hands the dino this weapon (calls DinoController.EquipDrill / EquipGun).
/// Q on the keyboard swaps weapons too. Styled like the dialogue box: a dark panel with an outline. The button
/// for the weapon in hand gets the yellow outline and is a little bigger; the other has the pink outline and a
/// dimmed icon. The HUD and the dino are separate prefabs, so the dino is found at runtime.
/// </summary>
[RequireComponent(typeof(Button))]
public class WeaponSelectButton : MonoBehaviour
{
    public enum Weapon { Drill, Gun }
    public Weapon weapon;

    [Tooltip("Size of the selected button compared to normal.")]
    public float selectedScale = 1.15f;
    [Tooltip("Brightness of the button that isn't selected (0-1).")]
    [Range(0f, 1f)] public float unselectedBrightness = 0.45f;

    [Header("Look (optional: without these it tints the button's own image)")]
    [Tooltip("The weapon picture inside the button.")]
    public Image icon;
    [Tooltip("The panel's outline (like the dialogue box's).")]
    public Outline outline;
    public Color outlineColor = new Color32(0xE0, 0x47, 0x9E, 0xFF);      // the dialogue box's pink
    public Color selectedOutlineColor = new Color32(0xF5, 0xC5, 0x42, 0xFF); // the HUD's yellow

    Image image;
    Color baseColor;
    DinoController dino;

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Select);
        image = GetComponent<Image>();
        if (image != null) baseColor = image.color;
    }

    public void Select()
    {
        // Don't leave the button selected: Space/Enter or the controller's A would "click" it again later.
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        if (!FindDino()) return;
        if (!dino.canSwapWeapons) return;
        if (weapon == Weapon.Gun) dino.EquipGun(); else dino.EquipDrill();
    }

    bool FindDino()
    {
        if (dino == null) dino = FindFirstObjectByType<DinoController>();
        return dino != null;
    }

    void LateUpdate()
    {
        if (!FindDino()) return;
        bool selected = weapon == Weapon.Gun ? dino.hasGun && !dino.hasDrill : dino.hasDrill;
        transform.localScale = Vector3.one * (selected ? selectedScale : 1f);
        if (icon != null || outline != null)
        {
            if (outline != null) outline.effectColor = selected ? selectedOutlineColor : outlineColor;
            if (icon != null)
            {
                var ic = selected ? Color.white : Color.white * unselectedBrightness;
                ic.a = 1f;
                icon.color = ic;
            }
            return;
        }
        if (image != null)
        {
            var c = selected ? baseColor : baseColor * unselectedBrightness;
            c.a = baseColor.a;
            image.color = c;
        }
    }
}
