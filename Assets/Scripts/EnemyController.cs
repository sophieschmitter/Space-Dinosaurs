using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Simple enemy: wanders around, chases the dino when it can see it, and hurts it on contact.
/// Takes damage through its Health (the dino's drill), gets knocked back, and dies.
///
/// Art is swappable: it works with just a placeholder sprite. To animate it later, give the Animator a
/// controller with any of these states and they're played automatically: Idle, Walk, Hurt, Death.
/// The sprite flips to face the way it moves (turn off Flip To Face if your art has separate left/right).
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Health))]
public class EnemyController : MonoBehaviour
{
    [Header("Movement")]
    public float wanderSpeed = 1.5f;
    public float chaseSpeed = 3f;
    [Tooltip("Seconds to reach full speed / stop.")]
    public float accelerationTime = 0.15f;
    [Tooltip("Random seconds spent walking one way while wandering (min, max).")]
    public Vector2 wanderWalkTime = new Vector2(0.8f, 2f);
    [Tooltip("Random seconds spent standing still between wanders (min, max).")]
    public Vector2 wanderPauseTime = new Vector2(0.5f, 1.5f);

    [Header("Chasing")]
    [Tooltip("Starts chasing when the dino is this close (tiles) and not behind a wall.")]
    public float sightRange = 5f;
    [Tooltip("Gives up when the dino gets this far away.")]
    public float loseRange = 8f;

    [Header("Attack")]
    [Min(0)] public int contactDamage = 1;
    [Tooltip("Pause after hitting the dino before it can move again.")]
    public float hitPause = 0.5f;

    [Header("Hurt and death")]
    public float knockbackSpeed = 7f;
    public float hurtTime = 0.25f;
    [Tooltip("Seconds the death plays (Death state, or a shrink on the placeholder) before it's removed.")]
    public float deathTime = 0.5f;

    [Header("Art")]
    public SpriteRenderer sprite;
    public Animator animator;
    public bool flipToFace = true;

    Rigidbody2D body;
    Health health;
    Transform player;
    Collider2D[] colliders;
    Vector2 wanderDir;
    float stateTimer;      // time left walking or pausing while wandering
    bool wanderWalking;
    bool chasing;
    float hurtTimer, pauseTimer;
    Vector2 knockback;
    bool dead;
    string currentState;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        colliders = GetComponentsInChildren<Collider2D>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void Start()
    {
        var dino = FindFirstObjectByType<DinoController>();
        if (dino != null) player = dino.transform;
        stateTimer = Random.Range(wanderPauseTime.x, wanderPauseTime.y);
    }

    void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    void OnDisable() { health.Damaged -= OnDamaged; health.Died -= OnDied; }

    void Update()
    {
        if (dead) return;
        if (hurtTimer > 0f) hurtTimer -= Time.deltaTime;
        if (pauseTimer > 0f) pauseTimer -= Time.deltaTime;

        UpdateChaseState();
        if (!chasing) UpdateWander();
        UpdateArt();
    }

    void FixedUpdate()
    {
        if (dead) { body.linearVelocity = Vector2.zero; return; }
        if (hurtTimer > 0f) { body.linearVelocity = knockback * (hurtTimer / hurtTime); return; }

        Vector2 target = Vector2.zero;
        if (pauseTimer <= 0f)
        {
            if (chasing && player != null) target = ((Vector2)player.position - body.position).normalized * chaseSpeed;
            else if (wanderWalking) target = wanderDir * wanderSpeed;
        }
        float rate = Mathf.Max(chaseSpeed, wanderSpeed) / Mathf.Max(0.01f, accelerationTime);
        body.linearVelocity = Vector2.MoveTowards(body.linearVelocity, target, rate * Time.fixedDeltaTime);
    }

    // ---------- behaviour ----------

    void UpdateChaseState()
    {
        if (player == null) { chasing = false; return; }
        var dino = player.GetComponent<DinoController>();
        if (dino != null && dino.IsDead) { chasing = false; return; }

        float dist = Vector2.Distance(body.position, player.position);
        if (chasing) chasing = dist <= loseRange;
        else chasing = dist <= sightRange && CanSee(player.position);
    }

    // True when no wall tilemap is between the enemy and the point.
    bool CanSee(Vector2 point)
    {
        foreach (var hit in Physics2D.LinecastAll(body.position, point))
            if (hit.collider.GetComponentInParent<Tilemap>() != null) return false;
        return true;
    }

    void UpdateWander()
    {
        stateTimer -= Time.deltaTime;
        // Bumped into something while walking: give up on this direction early.
        if (wanderWalking && stateTimer < wanderWalkTime.x * 0.5f && body.linearVelocity.sqrMagnitude < 0.01f) stateTimer = 0f;
        if (stateTimer > 0f) return;

        wanderWalking = !wanderWalking;
        if (wanderWalking)
        {
            wanderDir = Random.insideUnitCircle.normalized;
            stateTimer = Random.Range(wanderWalkTime.x, wanderWalkTime.y);
        }
        else stateTimer = Random.Range(wanderPauseTime.x, wanderPauseTime.y);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (dead || contactDamage <= 0) return;
        var target = collision.collider.GetComponentInParent<DinoController>();
        if (target == null) return;
        var targetHealth = target.GetComponent<Health>();
        if (targetHealth != null && targetHealth.TakeDamage(contactDamage, body.position))
            pauseTimer = hitPause; // back off for a moment after landing a hit
    }

    void OnDamaged(int amount, Vector2 from)
    {
        Vector2 away = body.position - from;
        knockback = (away.sqrMagnitude > 0.0001f ? away.normalized : Random.insideUnitCircle.normalized) * knockbackSpeed;
        hurtTimer = hurtTime;
        chasing = player != null; // getting hit makes it come after the dino
    }

    void OnDied()
    {
        dead = true;
        foreach (var c in colliders) c.enabled = false;
        body.linearVelocity = Vector2.zero;
        StartCoroutine(Die());
    }

    IEnumerator Die()
    {
        bool hasDeathAnim = Play("Death");
        Vector3 startScale = transform.localScale;
        for (float t = 0; t < deathTime; t += Time.deltaTime)
        {
            if (!hasDeathAnim) transform.localScale = startScale * (1f - t / deathTime); // placeholder: shrink away
            yield return null;
        }
        Destroy(gameObject);
    }

    // ---------- art ----------

    void UpdateArt()
    {
        Vector2 v = body.linearVelocity;
        if (flipToFace && sprite != null && Mathf.Abs(v.x) > 0.1f) sprite.flipX = v.x < 0;

        if (hurtTimer > 0f) Play("Hurt");
        else Play(v.sqrMagnitude > 0.05f ? "Walk" : "Idle");
    }

    // Plays an Animator state if the art has one; returns false (and does nothing) otherwise.
    bool Play(string state)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        if (state == currentState) return true;
        int hash = Animator.StringToHash(state);
        if (!animator.HasState(0, hash)) return false;
        animator.Play(hash, 0, 0f);
        currentState = state;
        return true;
    }
}
