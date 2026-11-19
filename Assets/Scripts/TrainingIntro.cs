using System.Collections;
using UnityEngine;

/// <summary>
/// The training level's opening: the dino (weapons put away, so the plain dino) walks in from off the left
/// edge of the screen, stops Stop Distance tiles from Talk To, stands idle, and the Dialogue plays. When the
/// dialogue closes the player takes over (and, with Rearm After, the dino gets back the weapon it started with).
/// The player can't move, attack or swap weapons until then. With Hold Camera the camera stays put for the
/// walk-in, picks the dino up once it reaches the middle of the screen, and follows it from then on.
/// </summary>
public class TrainingIntro : MonoBehaviour
{
    public DinoController dino;
    [Tooltip("Who the dino walks up to (the temp dino bad guy).")]
    public Transform talkTo;
    [Tooltip("Tiles between the dino and Talk To when it stops.")]
    public float stopDistance = 3f;
    [Tooltip("Where the dino starts (its feet). Leave X at 0 to start just off the left edge of the screen.")]
    public Vector2 start = new Vector2(0f, 0.23f);
    [Tooltip("Plays once the dino has stopped.")]
    public DialogueTrigger dialogue;
    [Tooltip("Give the dino back the weapon it had once the dialogue is over.")]
    public bool rearmAfter = true;
    [Tooltip("Keep the camera where it's placed in the scene while the dino walks on screen, then pick the dino up " +
             "(once it reaches the middle of the screen) and follow it.")]
    public bool holdCamera = true;
    [Tooltip("Seconds the camera takes to reach the dino after the dialogue.")]
    public float cameraCatchUp = 0.6f;

    /// <summary>True once the walk-in (and its dialogue, if any) is over and the player has control.</summary>
    public bool Done { get; private set; }

    PixelCameraFollow cam;
    Vector2 introView;

    void Awake()
    {
        // Read before the camera's Start moves it onto the dino.
        cam = Camera.main != null ? Camera.main.GetComponent<PixelCameraFollow>() : null;
        if (cam != null) introView = cam.transform.position;
    }

    IEnumerator Start()
    {
        if (holdCamera && cam != null) cam.FocusOn(introView, 0.01f);
        if (dino == null) dino = FindFirstObjectByType<DinoController>();
        if (dino == null || talkTo == null) { Done = true; yield break; }
        var body = dino.GetComponent<Rigidbody2D>();
        bool hadDrill = dino.hasDrill, hadGun = dino.hasGun;

        dino.BeginScripted();
        dino.Unarm();
        Vector2 from = start;
        if (Mathf.Approximately(from.x, 0f)) from.x = OffLeftEdge();
        body.position = from;
        dino.transform.position = from;

        // Walk right (along the corridor) until Stop Distance short of them, allowing for the slow-down.
        // The held camera picks the dino up once it reaches the middle of the screen, then follows it.
        float stopX = talkTo.position.x - stopDistance - dino.walkSpeed * dino.decelerationTime * 0.5f;
        bool released = false;
        while (body.position.x < stopX && !dino.IsDead)
        {
            dino.ScriptedMove(Vector2.right);
            if (holdCamera && cam != null && !released && body.position.x >= introView.x) { cam.Release(cameraCatchUp); released = true; }
            yield return null;
        }
        dino.ScriptedMove(Vector2.zero);
        while (body.linearVelocity.sqrMagnitude > 0.0001f && !dino.IsDead) yield return null; // idle once stopped

        bool talking = dialogue != null;
        if (talking) dialogue.Play(() => talking = false);
        while (talking) yield return null;

        if (rearmAfter) dino.SetWeapons(hadDrill, hadGun);
        dino.EndScripted();
        Done = true;
        if (holdCamera && cam != null && !released) cam.Release(cameraCatchUp);
    }

    // Just outside the left of the view, so it walks on screen. With a Pixel Perfect Camera the view is its
    // reference size (the same on every screen), not however wide the Game window happens to be.
    float OffLeftEdge()
    {
        var c = Camera.main;
        if (c == null || !c.orthographic) return dino.transform.position.x;
        float halfWidth = c.orthographicSize * c.aspect;
        var ppc = c.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
        if (ppc != null && ppc.assetsPPU > 0) halfWidth = ppc.refResolutionX / (2f * ppc.assetsPPU);
        return introView.x - halfWidth - 1.5f;
    }
}
