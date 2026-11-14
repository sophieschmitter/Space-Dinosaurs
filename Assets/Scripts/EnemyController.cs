using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Base level/rock crab enemy: wanders around, chases the dino when it can see it, and hurts it on contact
/// from x or y axis.
/// Takes damage, gets knocked back, and dies.
/// GoTo(goal) (used by spawners) sends it along a path around the walls to the robot (an EnemyGoal); once it
/// arrives, or the trip times out, it goes back to normal wandering/chasing. It damages the robot on contact.
///
/// Art is swappable: it works with just a placeholder sprite. Give the Animator a controller and these states
/// are played automatically when they exist, picked from the way it's moving:
///   Idle_/Walk_ + Left or Right (sideways), Front (down) or Back (up); then _Side (mirrored to face), then plain
///   Idle/Walk (mirrored to face); plus Death, and the Victory State (default victoryDance) when the player loses.
/// There's no hurt animation: DamageFlash flashes it when hit.
/// Side art is mirrored to face the way it moves (set Side Art Faces Left to match your art).
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

    [Header("Going to the robot (when a spawner sends it)")]
    [Tooltip("Speed while following the path to the robot.")]
    public float huntSpeed = 3f;
    [Tooltip("Gives up on reaching the robot after this many seconds and goes back to normal behaviour.")]
    public float huntTimeout = 15f;
    [Tooltip("How often (seconds) the path is recalculated (walls can be drilled away).")]
    public float repathInterval = 0.5f;

    [Header("Attack")]
    [Min(0)] public int contactDamage = 1;
    [Tooltip("Only hurt the dino when touching it from straight above, below, left or right (like the drill), " +
             "within this many degrees of the axis. Diagonal bumps do no damage.")]
    [Range(0f, 45f)] public float attackAngle = 30f;
    [Tooltip("Shown on the lose screen if this enemy lands the hit that kills the dino.")]
    [TextArea] public string killMessage = "An enemy got you.";
    [Tooltip("While chasing, first line up with the dino on an axis, then come straight at it.")]
    public bool approachAlongAxes = true;
    [Tooltip("How close (tiles) to the dino's line counts as lined up.")]
    public float lineUpTolerance = 0.15f;
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
    [Tooltip("For art with only one side view (_Side or plain states): which way it's drawn facing. " +
             "It's mirrored when moving the other way. Not used when the art has _Left and _Right states.")]
    public bool sideArtFacesLeft = false;
    [Tooltip("Animator state played when the player loses (the victory dance).")]
    public string victoryState = "victoryDance";

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
    bool hunting;
    float huntTimer, repathTimer;
    List<Vector2> path;
    int pathIndex;
    HashSet<Vector3Int> blockedCells;
    Tilemap walls;

    public bool IsHunting => hunting;
    public bool IsCelebrating { get; private set; }
    public bool IsChasing => chasing;

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
        FindPlayer();
        stateTimer = Random.Range(wanderPauseTime.x, wanderPauseTime.y);
    }

    void FindPlayer()
    {
        if (player != null) return;
        var dino = FindFirstObjectByType<DinoController>();
        if (dino != null) player = dino.transform;
    }

    /// <summary>Head for the dino along a path around the walls, then return to normal behaviour.</summary>
    EnemyGoal huntGoal;

    /// <summary>Head for the goal (the robot) along a path around the walls, then return to normal behaviour.</summary>
    public void GoTo(EnemyGoal goal)
    {
        FindPlayer();
        var destructible = FindFirstObjectByType<DestructibleTilemap>();
        walls = destructible != null ? destructible.GetComponent<Tilemap>() : null;
        if (goal == null || walls == null) return;
        huntGoal = goal;

        // Spawners are solid blocks that aren't wall tiles, so tell the pathfinder about them.
        blockedCells = new HashSet<Vector3Int>();
        foreach (var s in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
            blockedCells.Add(walls.WorldToCell(s.transform.position));

        hunting = true;
        huntTimer = huntTimeout;
        repathTimer = 0f;
    }

    void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    void OnDisable() { health.Damaged -= OnDamaged; health.Died -= OnDied; }

    void Update()
    {
        if (dead || IsCelebrating) return;
        if (hurtTimer > 0f) hurtTimer -= Time.deltaTime;
        if (pauseTimer > 0f) pauseTimer -= Time.deltaTime;

        if (hunting) UpdateHunt();
        else
        {
            UpdateChaseState();
            if (!chasing) UpdateWander();
        }
        UpdateArt();
    }

    void FixedUpdate()
    {
        if (dead || IsCelebrating) { body.linearVelocity = Vector2.zero; return; }
        if (hurtTimer > 0f) { body.linearVelocity = knockback * (hurtTimer / hurtTime); return; }

        Vector2 target = Vector2.zero;
        if (pauseTimer <= 0f)
        {
            if (hunting && path != null && pathIndex < path.Count)
                target = (path[pathIndex] - body.position).normalized * huntSpeed;
            else if (chasing && player != null) target = ChaseDirection(PlayerCenter() - body.position) * chaseSpeed;
            else if (wanderWalking) target = wanderDir * wanderSpeed;
        }
        float rate = Mathf.Max(chaseSpeed, wanderSpeed, huntSpeed) / Mathf.Max(0.01f, accelerationTime);
        body.linearVelocity = Vector2.MoveTowards(body.linearVelocity, target, rate * Time.fixedDeltaTime);
    }

    // ---------- behaviour ----------

    void UpdateHunt()
    {
        huntTimer -= Time.deltaTime;
        if (huntGoal == null || huntTimer <= 0f) { StopHunting(false); return; }

        // Arrived at the robot: back to normal wandering/chasing.
        if (Vector2.Distance(body.position, huntGoal.transform.position) <= huntGoal.arriveDistance) { StopHunting(false); return; }

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f || path == null)
        {
            repathTimer = repathInterval;
            path = TilePathfinder.FindPath(walls, body.position, huntGoal.transform.position, blockedCells);
            pathIndex = 0;
            if (path == null) { StopHunting(false); return; } // no way through: just behave normally
        }

        // Next waypoint once this one is reached.
        while (pathIndex < path.Count - 1 && Vector2.Distance(body.position, path[pathIndex]) < 0.15f) pathIndex++;
    }

    void StopHunting(bool chaseNow)
    {
        hunting = false;
        path = null;
        chasing = chaseNow;
    }

    void UpdateChaseState()
    {
        if (player == null) { chasing = false; return; }
        var dino = player.GetComponent<DinoController>();
        if (dino != null && dino.IsDead) { chasing = false; return; }

        float dist = Vector2.Distance(body.position, player.position);
        if (chasing) chasing = dist <= loseRange;
        else chasing = dist <= sightRange && CanSee(player.position);
    }

    // The middle of the dino's feet collider (what enemies touch), or its position.
    Vector2 PlayerCenter()
    {
        var col = player.GetComponent<Collider2D>();
        return col != null ? (Vector2)col.bounds.center : (Vector2)player.position;
    }

    /// <summary>Which way to move toward the dino. With Approach Along Axes, it first closes the smaller gap
    /// (lining up horizontally or vertically), then moves straight along the line.</summary>
    public Vector2 ChaseDirection(Vector2 toPlayer)
    {
        if (!approachAlongAxes) return toPlayer.normalized;
        float ax = Mathf.Abs(toPlayer.x), ay = Mathf.Abs(toPlayer.y);
        if (ax >= ay) // dino is more to the side: line up vertically, then come in sideways
            return ay > lineUpTolerance ? new Vector2(0, Mathf.Sign(toPlayer.y)) : new Vector2(Mathf.Sign(toPlayer.x), 0);
        return ax > lineUpTolerance ? new Vector2(Mathf.Sign(toPlayer.x), 0) : new Vector2(0, Mathf.Sign(toPlayer.y));
    }

    /// <summary>True when the offset to the dino is within Attack Angle of straight up/down/left/right.</summary>
    public bool IsOnAxis(Vector2 toPlayer)
    {
        float ax = Mathf.Abs(toPlayer.x), ay = Mathf.Abs(toPlayer.y);
        return Mathf.Min(ax, ay) <= Mathf.Max(ax, ay) * Mathf.Tan(attackAngle * Mathf.Deg2Rad);
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
        if (dead || IsCelebrating || contactDamage <= 0) return;

        // The robot: hurt it from any side.
        var goal = collision.collider.GetComponentInParent<EnemyGoal>();
        if (goal != null)
        {
            if (goal.TryGetComponent(out Health goalHealth) && goalHealth.TakeDamage(contactDamage, body.position, goal.destroyedMessage))
                pauseTimer = hitPause;
            return;
        }

        var target = collision.collider.GetComponentInParent<DinoController>();
        if (target == null) return;
        if (!IsOnAxis(PlayerCenter() - body.position)) return; // diagonal touch: no damage
        var targetHealth = target.GetComponent<Health>();
        if (targetHealth != null && targetHealth.TakeDamage(contactDamage, body.position, killMessage))
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

    // Select an enemy while it's hunting to see its path.
    void OnDrawGizmosSelected()
    {
        if (path == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Vector2 prev = body != null ? body.position : (Vector2)transform.position;
        for (int i = pathIndex; i < path.Count; i++) { Gizmos.DrawLine(prev, path[i]); prev = path[i]; }
    }

    // ---------- art ----------

    Vector2 facing = Vector2.down; // last direction moved, so idle keeps looking that way

    void UpdateArt()
    {
        Vector2 v = body.linearVelocity;
        bool moving = v.sqrMagnitude > 0.05f;
        if (moving && hurtTimer <= 0f) facing = v;

        // Which view: sideways if moving more across than up/down; otherwise front (down) or back (up).
        bool side = Mathf.Abs(facing.x) >= Mathf.Abs(facing.y);
        string action = moving ? "Walk" : "Idle";

        // 1. Separate left/right (and front/back) animations: the clips handle any mirroring themselves.
        string view = side ? (facing.x < 0 ? "_Left" : "_Right") : facing.y > 0 ? "_Back" : "_Front";
        if (Play(action + view)) return;

        // 2. One side view (_Side) or 3. a plain Idle/Walk: mirror the sprite to face the way it's moving.
        bool mirrorable = side ? Play(action + "_Side") || Play(action) : Play(action);
        if (flipToFace && sprite != null && mirrorable && Mathf.Abs(facing.x) > 0.1f)
            sprite.flipX = sideArtFacesLeft ? facing.x > 0 : facing.x < 0;
    }

    /// <summary>The player lost: stop and do the victory dance. Keeps animating while the game is paused.</summary>
    public void Celebrate()
    {
        if (dead || IsCelebrating) return;
        IsCelebrating = true;
        hunting = false; chasing = false;
        body.linearVelocity = Vector2.zero;
        if (animator != null) animator.updateMode = AnimatorUpdateMode.UnscaledTime; // the lose screen pauses the game
        if (sprite != null) sprite.flipX = false;
        if (!Play(victoryState)) Debug.LogWarning($"{name}: no \"{victoryState}\" state in its Animator for the victory dance.");
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
