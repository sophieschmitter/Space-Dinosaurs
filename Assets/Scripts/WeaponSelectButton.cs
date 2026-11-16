using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put on a HUD button: clicking it hands the dino this weapon (calls DinoController.EquipDrill / EquipGun).
/// The HUD and the dino are separate prefabs, so the dino is found when the level starts.
/// </summary>
[RequireComponent(typeof(Button))]
public class WeaponSelectButton : MonoBehaviour
{
    public enum Weapon { Drill, Gun }
    public Weapon weapon;

    void Awake() => GetComponent<Button>().onClick.AddListener(Select);

    public void Select()
    {
        var dino = FindFirstObjectByType<DinoController>();
        if (dino == null) return;
        if (weapon == Weapon.Gun) dino.EquipGun(); else dino.EquipDrill();
    }
}
