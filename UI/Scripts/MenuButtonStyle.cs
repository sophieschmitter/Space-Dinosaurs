using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Pixel-style look for a menu button. The highlighted button (hovered with the mouse or picked with the
/// keyboard / gamepad) gets gold text, a gold border and arrows around its label: "> PLAY <".
/// Hovering also selects the button, so mouse and keyboard always share one highlight.
/// Set the Button's Transition to None; this script does the colours instead.
/// </summary>
[RequireComponent(typeof(Selectable))]
public class MenuButtonStyle : MonoBehaviour,
    ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler
{
    public Image background;
    [Tooltip("Outline effect on the background Image, used as the border.")]
    public Outline border;
    public Text label;

    [Header("Normal")]
    public Color normalBackground = new Color32(0x1E, 0x1A, 0x33, 0xE6);
    public Color normalBorder = new Color32(0xE0, 0x47, 0x9E, 0xFF);
    public Color normalText = new Color32(0xEC, 0xE6, 0xFF, 0xFF);

    [Header("Highlighted")]
    public Color selectedBackground = new Color32(0x2E, 0x26, 0x50, 0xF2);
    public Color selectedBorder = new Color32(0xF5, 0xC5, 0x42, 0xFF);
    public Color selectedText = new Color32(0xF5, 0xC5, 0x42, 0xFF);
    public string selectedPrefix = "> ";
    public string selectedSuffix = " <";

    [Tooltip("Size while the mouse button is held down on it.")]
    public float pressedScale = 0.95f;

    string baseText;

    void Awake()
    {
        if (label != null) baseText = label.text;
        Apply(false);
    }

    void OnDisable()
    {
        transform.localScale = Vector3.one;
        Apply(false);
    }

    public void OnSelect(BaseEventData eventData) => Apply(true);
    public void OnDeselect(BaseEventData eventData) => Apply(false);

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
    }

    public void OnPointerDown(PointerEventData eventData) => transform.localScale = Vector3.one * pressedScale;
    public void OnPointerUp(PointerEventData eventData) => transform.localScale = Vector3.one;

    void Apply(bool selected)
    {
        if (background != null) background.color = selected ? selectedBackground : normalBackground;
        if (border != null) border.effectColor = selected ? selectedBorder : normalBorder;
        if (label != null)
        {
            label.color = selected ? selectedText : normalText;
            if (baseText != null) label.text = selected ? selectedPrefix + baseText + selectedSuffix : baseText;
        }
    }
}
