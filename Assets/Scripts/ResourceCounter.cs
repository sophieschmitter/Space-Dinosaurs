using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD readout for one resource: keeps a Text showing how many the player has.
/// The icon next to it is a plain Image, so replacing the art only means changing that sprite.
/// </summary>
public class ResourceCounter : MonoBehaviour
{
    public ResourceType resource;
    public Text label;
    [Tooltip("How the number is written; {0} is the amount.")]
    public string format = "{0}";

    ResourceInventory inventory;

    void Start()
    {
        inventory = ResourceInventory.Instance;
        if (inventory != null) inventory.Changed += OnChanged;
        Show(inventory != null ? inventory.Get(resource) : 0);
    }

    void OnDestroy() { if (inventory != null) inventory.Changed -= OnChanged; }

    void OnChanged(ResourceType type, int total) { if (type == resource) Show(total); }

    void Show(int amount) { if (label != null) label.text = string.Format(format, amount); }
}
