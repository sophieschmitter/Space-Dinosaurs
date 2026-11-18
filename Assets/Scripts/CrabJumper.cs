using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Lets an enemy (the jumpy crab) leap at the dino. When the dino is in range and not behind a wall, the crab
/// stops and crouches for Wind Up seconds, then jumps at the spot the dino is standing on at that moment. It
/// doesn't steer in the air, so moving away dodges it. Landing on the dino hurts it.
/// In the air it flies over everything and can't be hit (EnemyController.Leap).
///
/// Plays the Animator's Jump state frame by frame: the first frames while crouching, the raised frame in the
/// air, the last frame on landing. The art is also lifted along an arc while it's in the air.
/// </summary>
[RequireComponent(typeof(EnemyController))]
[DefaultExecutionOrder(100)] // after EnemyController, so the jump frame it sets is the one shown
public class CrabJumper : MonoBehaviour
{
    [Tooltip("Jumps when the dino is within this many tiles.")]
    public float range = 4.5f;
    [Tooltip("Doesn't jump when the dino is closer than this (it just walks into it).")]
    public float minRange = 1.2f;
    [Tooltip("Seconds between jumps.")]
    public float cooldown = 2.5f;
    [Tooltip("Seconds after first seeing the dino before the first jump.")]
    public float firstJumpDelay = 0.8f;
    [Tooltip("Seconds it crouches before leaving the ground (a warning for the player).")]
    public float windUp = 0.4f;
    [Tooltip("Seconds in the air, whatever the distance.")]
    public float airTime = 0.55f;
    [Tooltip("Seconds it stays still after landing.")]
    public float recover = 0.35f;
    [Tooltip("How high the art goes at the top of the jump (tiles).")]
    public float jumpHeight = 0.8f;

    [Header("Landing")]
    [Min(0)] public int landDamage = 1;
    [Tooltip("Hurts the dino if it lands this close to it (tiles).")]
    public float landRadius = 0.6f;
    [TextArea] public string killMessage = "A jumpy crab landed on you.";

    [Header("Art")]
    public string jumpState = "Jump";
    [Tooltip("Frames in the Jump animation, and which ones are crouching / in the air / landing.")]
    public int crouchFrames = 3, airFrame = 3, landFrame = 4, totalFrames = 5;

    EnemyController ai;
    Health health;
    Rigidbody2D body;
    Animator animator;
    Transform art;
    Vector3 artRest;
    Transform player;
    Collider2D playerCollider;
    DinoController dino;
    float cooldownTimer;
    bool jumping, sawPlayer;
    int frame = -1;      // jump frame to show, -1 when not jumping
    float lift;          // art raised this far right now

    public bool IsJumping => jumping;

    void Awake()
    {
        ai = GetComponent<EnemyController>();
        health = GetComponent<Health>();
        body = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();
        if (animator != null) { art = animator.transform; artRest = art.localPosition; }
    }

    void Start()
    {
        dino = FindFirstObjectByType<DinoController>();
        if (dino != null) { player = dino.transform; playerCollider = dino.GetComponent<Collider2D>(); }
    }

    void Update()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        if (!jumping) TryJump();
        ShowArt();
    }

    void TryJump()
    {
        if (player == null || !CanAct()) return;
        Vector2 to = Target() - body.position;
        float dist = to.magnitude;
        if (dist > range || !ClearPath(Target())) { sawPlayer = false; return; }
        if (!sawPlayer) { sawPlayer = true; cooldownTimer = Mathf.Max(cooldownTimer, firstJumpDelay); }
        if (cooldownTimer > 0f || dist < minRange) return;
        StartCoroutine(Jump());
    }

    bool CanAct() => !ai.IsDead && !ai.IsCelebrating && !ai.IsHurt && (health == null || !health.IsDead)
                     && (dino == null || !dino.IsDead);

    Vector2 Target() => playerCollider != null ? (Vector2)playerCollider.bounds.center : (Vector2)player.position;

    // No wall or shield between here and there. It flies over spawners, the robot and other enemies.
    bool ClearPath(Vector2 point)
    {
        if (ShieldField.AnyBetween(body.position, point)) return false;
        foreach (var hit in Physics2D.LinecastAll(body.position, point))
            if (!hit.collider.isTrigger && hit.collider.GetComponentInParent<Tilemap>() != null) return false;
        return true;
    }

    IEnumerator Jump()
    {
        jumping = true;
        ai.Face(Target() - body.position);
        ai.Hold(windUp + airTime + recover, jumpState);

        // Crouch.
        for (float t = 0f; t < windUp; t += Time.deltaTime)
        {
            if (!CanAct()) { Cancel(); yield break; }
            frame = Mathf.Min(crouchFrames - 1, (int)(t / windUp * crouchFrames));
            yield return null;
        }

        // Take off at where the dino is now (no steering after this).
        Vector2 start = body.position, end = Target();
        if (Vector2.Distance(start, end) > range) end = start + (end - start).normalized * range;
        if (!ClearPath(end)) { Cancel(); yield break; }
        ai.Face(end - start);
        ai.Leap((end - start) / airTime);
        frame = airFrame;
        for (float t = 0f; t < airTime; t += Time.deltaTime)
        {
            if (ai.IsCelebrating) break;
            lift = Mathf.Sin(t / airTime * Mathf.PI) * jumpHeight;
            yield return null;
        }
        lift = 0f;
        ai.Land(ai.IsCelebrating ? body.position : end);
        if (ai.IsCelebrating) { frame = -1; jumping = false; yield break; }

        // Land: hurts the dino if it's still there.
        frame = landFrame;
        if (dino != null && landDamage > 0 && dino.TryGetComponent(out Health dh)
            && Vector2.Distance(Target(), end) <= landRadius)
            dh.TakeDamage(landDamage, end, killMessage);

        yield return new WaitForSeconds(recover);
        frame = -1;
        cooldownTimer = cooldown;
        jumping = false;
    }

    void Cancel()
    {
        frame = -1;
        lift = 0f;
        ai.ReleaseHold();
        cooldownTimer = cooldown * 0.5f;
        jumping = false;
    }

    void ShowArt()
    {
        if (art != null) art.localPosition = artRest + Vector3.up * lift;
        if (frame < 0 || animator == null || animator.runtimeAnimatorController == null || ai.IsCelebrating || ai.IsDead) return;
        // Clips hold their last frame one extra key, so the clip is totalFrames + 1 frames long.
        animator.Play(jumpState, 0, (frame + 0.5f) / (totalFrames + 1));
    }

    void OnDisable()
    {
        if (art != null) art.localPosition = artRest;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, range);
        Gizmos.DrawWireSphere(transform.position, minRange);
    }
}
