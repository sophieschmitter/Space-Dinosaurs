using UnityEngine;

/// <summary>
/// A craftable building (shield generator, ice cream stand, turret). Two looping animations:
///   Normal   - placed down;
///   Selected - outlined, while the player is still choosing where to put it (following the mouse).
/// Set Selected from the placing code. While selected its colliders are off so it doesn't push things around.
/// Built by Tools > Space Dinosaurs > Build Buildings.
/// </summary>
public class Building : MonoBehaviour
{
    public string itemName = "Building";
    [SerializeField] bool selected;
    public Animator animator;
    public string normalState = "Normal", selectedState = "Selected";

    [Tooltip("Played when it's put down on a building spot.")]
    public AudioClip placedSound;
    [Range(0f, 1f)] public float placedVolume = 0.6f;

    /// <summary>The placed sound (BuildingPlacer calls this when it's put down).</summary>
    public void PlayPlacedSound()
    {
        if (placedSound == null) return;
        var src = gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.playOnAwake = false;
        src.PlayOneShot(placedSound, placedVolume);
        Destroy(src, placedSound.length + 0.1f);
    }

    Collider2D[] colliders;
    string playing;

    public bool Selected
    {
        get => selected;
        set { selected = value; Apply(); }
    }

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        colliders = GetComponentsInChildren<Collider2D>(true);
    }

    void OnEnable() => Apply();
    void OnValidate() { if (Application.isPlaying) Apply(); }

    /// <summary>Replays the right state after Normal State changed (e.g. the ice cream stand emptying).</summary>
    public void Refresh() => Apply();

    void Apply()
    {
        if (colliders != null) foreach (var c in colliders) c.enabled = !selected;
        string state = selected ? selectedState : normalState;
        if (animator == null || animator.runtimeAnimatorController == null || state == playing) return;
        // Keep the frame when switching, so picking it up or putting it down doesn't restart the loop.
        float t = playing != null ? animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
        animator.Play(state, 0, t % 1f);
        playing = state;
    }
}
