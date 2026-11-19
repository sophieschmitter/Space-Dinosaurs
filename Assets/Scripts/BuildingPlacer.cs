using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Puts down buildings from the crafting menu. Clicking a craft button you can afford picks the building up:
/// it follows the mouse showing its Selected animation (tinted red where it doesn't fit). Left-click places
/// it and pays its cost; right-click or Escape puts it back for free. Clicking another craft button swaps
/// to that building. Clicks used for placing never make the dino attack.
/// Created by the first craft button that's used, so scenes don't need one.
/// </summary>
public class BuildingPlacer : MonoBehaviour
{
    static BuildingPlacer instance;
    public static BuildingPlacer Instance => instance;
    static int lastPlacedFrame = -1;

    [Tooltip("Tint while it can't go where the mouse is.")]
    public Color blockedTint = new Color(1f, 0.45f, 0.45f, 0.85f);
    [Tooltip("Snaps to this many positions per tile (16 = whole art pixels, so the art stays crisp).")]
    public int snapPerTile = 16;

    CraftButton source;
    Building held;
    SpriteRenderer[] heldSprites;
    Color[] heldColors;
    BoxCollider2D footprint;
    readonly Collider2D[] overlaps = new Collider2D[8];

    /// <summary>True while a building is following the mouse, and on the frame one is put down: the dino
    /// ignores the mouse button then.</summary>
    public static bool ClickTaken => (instance != null && instance.held != null) || Time.frameCount == lastPlacedFrame;

    /// <summary>The craft button whose building is being placed (it shows its pressed picture meanwhile).</summary>
    public static CraftButton Placing => instance != null && instance.held != null ? instance.source : null;

    public static void Begin(CraftButton button)
    {
        if (button == null || button.building == null) return;
        if (instance == null) instance = new GameObject("Building Placer").AddComponent<BuildingPlacer>();
        instance.Pick(button);
    }

    public static void Cancel() { if (instance != null) instance.Drop(); }

    void OnDestroy() { if (instance == this) instance = null; }

    void Pick(CraftButton button)
    {
        Drop();
        source = button;
        held = Instantiate(button.building, MouseWorld(), Quaternion.identity);
        held.name = button.building.name;
        held.Selected = true;
        footprint = held.GetComponent<BoxCollider2D>();
        heldSprites = held.GetComponentsInChildren<SpriteRenderer>();
        heldColors = new Color[heldSprites.Length];
        for (int i = 0; i < heldSprites.Length; i++) heldColors[i] = heldSprites[i].color;
        MoveTo(MouseWorld());
    }

    void Drop()
    {
        if (held != null) Destroy(held.gameObject);
        held = null;
        source = null;
    }

    void Update()
    {
        if (held == null) return;
        if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return; // paused: menu clicks aren't placing
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if ((mouse != null && mouse.rightButton.wasPressedThisFrame) || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame))
        {
            Drop();
            return;
        }

        bool fits = MoveTo(MouseWorld());
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || OverUI() || !fits) return;
        Place();
    }

    /// <summary>Puts the held building down where it is and pays for it. False if it doesn't fit there.</summary>
    public bool Place()
    {
        if (held == null || !Fits(held.transform.position)) return false;
        if (!source.CanAfford) { Drop(); return false; } // spent the resources on something else meanwhile
        var placed = held;
        var paidBy = source;
        held = null;
        source = null;
        Tint(null);
        placed.Selected = false;
        lastPlacedFrame = Time.frameCount;
        paidBy.Pay(placed);
        return true;
    }

    /// <summary>Moves the held building here (snapped to the art pixel grid) and shows whether it fits.</summary>
    public bool MoveTo(Vector2 p)
    {
        if (held == null) return false;
        float snap = Mathf.Max(1, snapPerTile);
        p = new Vector2(Mathf.Round(p.x * snap) / snap, Mathf.Round(p.y * snap) / snap);
        held.transform.position = p;
        bool fits = Fits(p);
        Tint(fits ? (Color?)null : blockedTint);
        return fits;
    }

    // Nothing solid (walls, spawners, the robot, crabs, the dino, other buildings) under its base.
    bool Fits(Vector2 at)
    {
        if (footprint == null) return true;
        Vector2 center = at + footprint.offset;
        int n = Physics2D.OverlapBoxNonAlloc(center, footprint.size, 0f, overlaps);
        for (int i = 0; i < n; i++)
            if (!overlaps[i].isTrigger && !overlaps[i].transform.IsChildOf(held.transform)) return false;
        return true;
    }

    void Tint(Color? tint)
    {
        if (heldSprites == null) return;
        for (int i = 0; i < heldSprites.Length; i++)
            if (heldSprites[i] != null) heldSprites[i].color = tint.HasValue ? heldColors[i] * tint.Value : heldColors[i];
    }

    // Over a HUD button (not just any UI: the floor is a UI image too).
    static bool OverUI() => DinoController.PointerOverButton();

    static Vector2 MouseWorld()
    {
        var cam = Camera.main;
        if (cam == null || Mouse.current == null) return Vector2.zero;
        Vector3 s = Mouse.current.position.ReadValue();
        s.z = -cam.transform.position.z;
        return cam.ScreenToWorldPoint(s);
    }
}
