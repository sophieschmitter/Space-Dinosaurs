using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A crafting menu button. Shows one of three pictures:
///   Available   - the player has enough Nickel and Iridium (ResourceInventory) to craft it;
///   Pressed     - while it's held down, while its building is being placed, and for a moment after a click;
///   Unaffordable - not enough resources: clicking does nothing.
/// Clicking it while affordable picks up its Building to place (BuildingPlacer); the cost is paid when it's put
/// down, then On Crafted fires. Clicking it again while placing puts the building back. With no Building set,
/// a click pays straight away.
/// Built by Tools > Space Dinosaurs > Build Crafting Menu.
/// </summary>
[RequireComponent(typeof(Button))]
public class CraftButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public string itemName = "Item";
    [Tooltip("Placed with the mouse after clicking. Empty: clicking just pays.")]
    public Building building;

    [Header("Cost")]
    [Min(0)] public int nickelCost = 3;
    [Min(0)] public int iridiumCost = 1;

    [Header("Art")]
    public Image image;
    public Sprite available, pressed, unaffordable;
    [Tooltip("Optional: shows the cost, e.g. \"3\" next to the nickel icon.")]
    public Text nickelLabel, iridiumLabel;
    [Tooltip("Seconds the pressed picture stays after a click (so keyboard and controller clicks show too).")]
    public float pressedFlash = 0.12f;

    [Tooltip("Called after the cost is paid.")]
    public UnityEvent onCrafted;

    /// <summary>(item name) after the cost is paid.</summary>
    public static event System.Action<string> Crafted;

    bool held;
    float flashTimer;

    public bool CanAfford
    {
        get
        {
            var inv = ResourceInventory.Instance;
            if (inv == null) return nickelCost <= 0 && iridiumCost <= 0;
            return inv.Get(ResourceType.Nickel) >= nickelCost && inv.Get(ResourceType.Iridium) >= iridiumCost;
        }
    }

    void Awake()
    {
        if (image == null) image = GetComponent<Image>();
        var button = GetComponent<Button>();
        button.transition = Selectable.Transition.None; // the pictures are swapped here instead
        button.onClick.AddListener(Craft);
    }

    void OnValidate()
    {
        if (nickelLabel != null) nickelLabel.text = nickelCost.ToString();
        if (iridiumLabel != null) iridiumLabel.text = iridiumCost.ToString();
    }

    public void Craft()
    {
        // Don't leave the button selected: Space/Enter or the controller's A would "click" it again later.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (BuildingPlacer.Placing == this) { BuildingPlacer.Cancel(); return; } // clicked again: put it back
        if (!CanAfford) return;
        if (building != null) { BuildingPlacer.Begin(this); return; } // paid when it's put down
        Pay(null);
    }

    /// <summary>Spends the cost and reports the item as crafted (BuildingPlacer calls this on placing).</summary>
    public void Pay(Building placed)
    {
        var inv = ResourceInventory.Instance;
        if (inv != null)
        {
            inv.TrySpend(ResourceType.Nickel, nickelCost);
            inv.TrySpend(ResourceType.Iridium, iridiumCost);
        }
        flashTimer = pressedFlash;
        Debug.Log($"Crafted {itemName}");
        onCrafted?.Invoke();
        Crafted?.Invoke(itemName);
    }

    public void OnPointerDown(PointerEventData e) => held = true;
    public void OnPointerUp(PointerEventData e) => held = false;
    public void OnPointerExit(PointerEventData e) => held = false;

    void Update()
    {
        if (flashTimer > 0f) flashTimer -= Time.unscaledDeltaTime;
        if (image == null) return;
        bool afford = CanAfford;
        bool placing = BuildingPlacer.Placing == this;
        var sprite = placing ? pressed : !afford ? unaffordable : (held || flashTimer > 0f) ? pressed : available;
        if (sprite != null && image.sprite != sprite) image.sprite = sprite;
        if (nickelLabel != null) nickelLabel.text = nickelCost.ToString();
        if (iridiumLabel != null) iridiumLabel.text = iridiumCost.ToString();
    }
}
