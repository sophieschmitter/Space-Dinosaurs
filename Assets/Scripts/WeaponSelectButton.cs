using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put on a HUD button: clicking it hands the dino this weapon (calls DinoController.EquipDrill / EquipGun).
/// Q on the keyboard swaps weapons too. The button for the weapon in hand shows at full colour and a little
/// bigger; the other is dimmed. The HUD and the dino are separate prefabs, so the dino is found at runtime.
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
        if (image != null)
        {
            var c = selected ? baseColor : baseColor * unselectedBrightness;
            c.a = baseColor.a;
            image.color = c;
        }
    }
}
