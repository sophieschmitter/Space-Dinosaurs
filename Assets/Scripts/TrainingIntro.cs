using System.Collections;
using UnityEngine;

/// <summary>
/// The training level's opening: the dino (weapons put away, so the plain dino) walks in from off the left
/// edge of the screen, stops Stop Distance tiles from Talk To, stands idle, and the Dialogue plays. When the
/// dialogue closes the player takes over (and, with Rearm After, the dino gets back the weapon it started with).
/// The player can't move, attack or swap weapons until then.
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

    IEnumerator Start()
    {
        if (dino == null) dino = FindFirstObjectByType<DinoController>();
        if (dino == null || talkTo == null) yield break;
        var body = dino.GetComponent<Rigidbody2D>();
        bool hadDrill = dino.hasDrill, hadGun = dino.hasGun;

        dino.BeginScripted();
        dino.Unarm();
        Vector2 from = start;
        if (Mathf.Approximately(from.x, 0f)) from.x = OffLeftEdge();
        body.position = from;
        dino.transform.position = from;

        // Walk right (along the corridor) until Stop Distance short of them, allowing for the slow-down.
        float stopX = talkTo.position.x - stopDistance - dino.walkSpeed * dino.decelerationTime * 0.5f;
        while (body.position.x < stopX && !dino.IsDead)
        {
            dino.ScriptedMove(Vector2.right);
            yield return null;
        }
        dino.ScriptedMove(Vector2.zero);
        while (body.linearVelocity.sqrMagnitude > 0.0001f && !dino.IsDead) yield return null; // idle once stopped

        bool talking = dialogue != null;
        if (talking) dialogue.Play(() => talking = false);
        while (talking) yield return null;

        if (rearmAfter) dino.SetWeapons(hadDrill, hadGun);
        dino.EndScripted();
    }

    // Just outside the left of the view, so it walks on screen.
    float OffLeftEdge()
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return dino.transform.position.x;
        return cam.transform.position.x - cam.orthographicSize * cam.aspect - 1.5f;
    }
}
