using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Top-down movement and drilling for the dino. Moves only while a direction key is held:
///   W / Up arrow    = up (shows the dino's back)
///   S / Down arrow  = down (shows the dino's front)
///   A / Left arrow  = left (left-facing art)
///   D / Right arrow = right (right-facing art)
///   Space (held)    = sprint (sweat drops fly off now and then)
///   X               = drill the wall tile in front (hold to keep drilling)
/// Two directions at once move diagonally while the dino keeps facing the one held first.
/// Starting and stopping ease in/out over Acceleration Time / Deceleration Time.
/// Walls stop it through physics, so it needs the Rigidbody2D + collider on the same object.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Animator), typeof(SpriteRenderer))]
public class DinoController : MonoBehaviour
{
    [Tooltip("Walking speed in tiles per second.")]
    public float walkSpeed = 4f;

    [Tooltip("Sprinting speed in tiles per second (while Space is held).")]
    public float sprintSpeed = 7f;

    [Tooltip("How much faster the walk animation plays while sprinting.")]
    public float sprintAnimationSpeed = 1.6f;

    [Tooltip("Seconds to get up to full speed after pressing a direction.")]
    [Min(0f)] public float accelerationTime = 0.08f;

    [Tooltip("Seconds to slide to a stop after letting go.")]
    [Min(0f)] public float decelerationTime = 0.1f;

    [Header("Drill")]
    [Tooltip("Show the drill-holding animations (needed for the drill attack to look right).")]
    public bool hasDrill = true;

    [Tooltip("The breakable wall layer. Found automatically if left empty.")]
    public DestructibleTilemap walls;

    [Tooltip("Seconds into the attack animation when the drill hits the tile.")]
    public float hitTime = 0.15f; // frame 2, when the drill first reaches out

    [Tooltip("Enemies are hit any time from Hit Time until this many seconds before the attack ends " +
             "(each enemy at most once per attack). 0 = right up to the end.")]
    [Min(0f)] public float hitWindowEndEarly = 0f;

    [Tooltip("A press of X during an attack or while stunned is remembered this long and fires as soon as possible.")]
    [Min(0f)] public float pressBuffer = 0.2f;

    [Tooltip("Stand still while drilling.")]
    public bool stopWhileDrilling = true;

    [Tooltip("Damage the drill does to enemies (or anything with Health) in front of it.")]
    [Min(0)] public int drillDamage = 1;

    [Tooltip("Where the drill is in the attack art, in tiles from the feet (x right, y up), facing right. " +
             "Anything touching it is hit. Facing left uses this mirrored. Shown in the Scene view when the dino is selected.")]
    public Rect drillBoxRight = new Rect(0.25f, 0.06f, 1.7f, 1.0f);
    [Tooltip("Drill area facing down (front view), in tiles from the feet.")]
    public Rect drillBoxFront = new Rect(-0.69f, -0.44f, 1.0f, 1.31f);
    [Tooltip("Drill area facing up (back view, drill raised overhead), in tiles from the feet.")]
    public Rect drillBoxBack = new Rect(-0.12f, 0.5f, 1.0f, 2.5f);
    [Tooltip("Also hit anything within this radius just in front of the feet (catches enemies at ground level).")]
    public float drillGroundRadius = 0.45f;

    [Header("Hurt and death (needs a Health component)")]
    [Tooltip("Speed the dino is knocked back at when hit, in tiles per second.")]
    public float knockbackSpeed = 6f;
    [Tooltip("Off: a hit only shoves the dino (you keep control and the drill swing isn't interrupted). " +
             "On: the dino is stunned for Hurt Time (no control, hurt animation, swing cancelled).")]
    public bool stunOnHit = false;
    [Tooltip("Seconds the dino is stunned (no control, hurt animation) after a hit. Only used with Stun On Hit.")]
    public float hurtTime = 0.35f;
    [Tooltip("Seconds after dying before the lose screen appears.")]
    public float restartDelay = 2f;
    [Tooltip("Lose-screen message when whatever killed the dino didn't give its own Kill Message.")]
    [TextArea] public string deathMessage = "You died.";

    [Header("Shadow and sweat (set up by Tools > Space Dinosaurs > Build Dino Animations)")]
    [Tooltip("Shown under the dino while it stands or walks/runs facing left or right.")]
    public SpriteRenderer shadow;
    public SpriteRenderer sweat;
    public Sprite[] sweatRight, sweatLeft;
    [Tooltip("Frames per second of one burst of sweat drops.")]
    public float sweatFrameRate = 8f;
    [Tooltip("Seconds of no sweat between bursts while sprinting.")]
    public float sweatGap = 0.4f;

    [Header("Animator state names (\"Drill_\" is added in front when Has Drill is on)")]
    public string rightIdle = "RIdle", rightWalk = "RWalk";
    public string leftIdle = "LIdle", leftWalk = "LWalk";
    public string frontIdle = "Front_Idle", frontWalk = "Front_Walk";
    public string backIdle = "Back_Idle", backWalk = "Back_Walk";
    public string rightAttack = "Drill_Attack_R", leftAttack = "Drill_Attack_L";
    public string frontAttack = "Drill_Attack_Front", backAttack = "Drill_Attack_Back";
    public string rightDamage = "RDamage", leftDamage = "LDamage";
    public string rightDeath = "RDeath", leftDeath = "LDeath";

    /// <summary>The way the dino faces: always straight up, down, left or right. It's the first direction
    /// you held, so adding a second direction moves diagonally without turning the dino.</summary>
    public Vector2 Facing { get; private set; } = Vector2.right;
    public bool IsMoving { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsDrilling => attackTimer > 0f;
    public bool IsHurt => hurtTimer > 0f;
    public bool IsDead { get; private set; }

    enum View { Right, Left, Front, Back }

    InputAction move, sprint, attack;
    Rigidbody2D body;
    Animator animator;
    SpriteRenderer sprite;
    Collider2D feet;
    Vector2 input;
    string currentState;
    float attackTimer;   // time left in the current attack
    bool hitDone;        // this attack already hit its tile
    float bufferedPress; // time left on a remembered X press
    readonly System.Collections.Generic.HashSet<Health> hitThisAttack = new();
    Vector2 attackDir;   // cardinal direction of the current attack
    float sweatClock;    // time since the current sweat burst started
    float cruiseSpeed;   // speed the dino was last asked to move at (sets the stopping rate)
    Health health;
    float hurtTimer;     // time left in the current hurt/knockback
    Vector2 knockback;   // knockback velocity at the start of the hit
    bool facedLeft;      // last sideways facing, for the hurt/death art (which only has left/right)
    Vector2 spawnPoint;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        sprite = GetComponent<SpriteRenderer>();
        feet = GetComponent<Collider2D>();
        health = GetComponent<Health>();
        spawnPoint = transform.position;

        body.gravityScale = 0f;                                   // top-down: no falling
        body.constraints = RigidbodyConstraints2D.FreezeRotation; // don't spin when bumping walls
        body.interpolation = RigidbodyInterpolation2D.Interpolate; // smooth on the pixel-perfect camera

        move = new InputAction("Move", InputActionType.Value);
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

        sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/space");
        attack = new InputAction("Attack", InputActionType.Button, "<Keyboard>/x");

        if (walls == null) walls = FindFirstObjectByType<DestructibleTilemap>();
    }

    void OnEnable()
    {
        move.Enable(); sprint.Enable(); attack.Enable();
        if (health != null) { health.Damaged += OnDamaged; health.Died += OnDied; }
    }

    void OnDisable()
    {
        move.Disable(); sprint.Disable(); attack.Disable();
        body.linearVelocity = Vector2.zero;
        if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
    }
    void OnDestroy() { move.Dispose(); sprint.Dispose(); attack.Dispose(); }

    void Update()
    {
        input = move.ReadValue<Vector2>();
        if (input.sqrMagnitude > 1f) input.Normalize(); // diagonals aren't faster

        if (hurtTimer > 0f) hurtTimer -= Time.deltaTime;
        // Remember a press even mid-attack or while stunned, so quick taps aren't lost.
        if (attack.WasPressedThisFrame()) bufferedPress = pressBuffer;
        else if (bufferedPress > 0f) bufferedPress -= Time.deltaTime;
        bool locked = IsDead || IsHurt || (IsDrilling && stopWhileDrilling);
        IsMoving = !locked && input.sqrMagnitude > 0.01f;
        IsSprinting = IsMoving && sprint.IsPressed();
        if (IsMoving) UpdateFacing();

        if (!IsDead && !IsHurt) UpdateAttack();
        UpdateAnimation();
    }

    // Keep facing the direction that was held first; only turn when that key is let go (or reversed).
    void UpdateFacing()
    {
        int x = input.x > 0.1f ? 1 : input.x < -0.1f ? -1 : 0;
        int y = input.y > 0.1f ? 1 : input.y < -0.1f ? -1 : 0;
        bool horizontal = Facing.x != 0;
        bool stillHeld = horizontal ? x == (int)Facing.x : y == (int)Facing.y;
        if (stillHeld) return;

        if (horizontal && x != 0) Facing = new Vector2(x, 0);      // reversed left <-> right
        else if (!horizontal && y != 0) Facing = new Vector2(0, y); // reversed up <-> down
        else if (x != 0) Facing = new Vector2(x, 0);                // switched to the other axis
        else Facing = new Vector2(0, y);
        if (Facing.x != 0) facedLeft = Facing.x < 0;
    }

    void FixedUpdate()
    {
        if (IsHurt)
        {
            // Knocked back, slowing to a stop over the hurt time.
            body.linearVelocity = knockback * (hurtTimer / hurtTime);
            return;
        }

        float speed = IsSprinting ? sprintSpeed : walkSpeed;
        Vector2 target = IsMoving ? input * speed : Vector2.zero;
        Vector2 current = body.linearVelocity;

        if (IsMoving) cruiseSpeed = speed;

        // Ease toward the target speed at a steady rate: full speed is reached in accelerationTime,
        // and a stop from full speed takes decelerationTime.
        bool speedingUp = target.sqrMagnitude > current.sqrMagnitude;
        float time = speedingUp ? accelerationTime : decelerationTime;
        float rate = time > 0f ? Mathf.Max(target.magnitude, cruiseSpeed) / time : float.PositiveInfinity;
        body.linearVelocity = Vector2.MoveTowards(current, target, rate * Time.fixedDeltaTime);
    }

    // ---------- drilling ----------

    void UpdateAttack()
    {
        if (IsDrilling)
        {
            float length = ClipLength(AttackState());
            attackTimer -= Time.deltaTime;
            float elapsed = length - attackTimer;
            if (elapsed >= hitTime)
            {
                if (!hitDone) { hitDone = true; BreakTileInFront(); } // one wall tile per attack
                if (attackTimer >= hitWindowEndEarly) HitEnemies();    // enemies: any time in the window
            }
            if (attackTimer > 0f) return;
            currentState = null; // let the idle/walk state come back
        }

        // Start an attack on press, and keep drilling while X stays held.
        if (hasDrill && (bufferedPress > 0f || attack.IsPressed()))
        {
            bufferedPress = 0f;
            attackDir = Cardinal(Facing);
            attackTimer = ClipLength(AttackState());
            hitDone = false;
            hitThisAttack.Clear();
            currentState = null; // restart the attack animation from its first frame
        }
    }

    // Middle of the feet collider: where the drill is aimed from.
    Vector2 DrillOrigin() => feet != null ? (Vector2)feet.bounds.center : body.position;

    // Anything with Health touching the drill, or right in front of the feet, takes drill damage,
    // at most once per attack. Called every frame of the hit window, so enemies that step in late still get hit.
    void HitEnemies()
    {
        Vector2 origin = DrillOrigin();
        Rect box = DrillBox(attackDir);
        foreach (var col in Physics2D.OverlapBoxAll((Vector2)transform.position + box.center, box.size, 0f)) Hit(col);
        foreach (var col in Physics2D.OverlapCircleAll(origin + attackDir * 0.75f, drillGroundRadius)) Hit(col);

        void Hit(Collider2D col)
        {
            var target = col.GetComponentInParent<Health>();
            if (target == null || target == health || hitThisAttack.Contains(target)) return;
            // Only count it once the hit actually lands, so a target that's briefly invulnerable can still be hit later in the swing.
            if (target.TakeDamage(drillDamage, origin)) hitThisAttack.Add(target);
        }
    }

    void BreakTileInFront()
    {
        if (walls != null) walls.HitInFront(DrillOrigin(), attackDir);
    }

    /// <summary>The drill's hit area for a direction, in tiles from the feet.</summary>
    public Rect DrillBox(Vector2 dir) => ViewOf(dir) switch
    {
        View.Front => drillBoxFront,
        View.Back => drillBoxBack,
        View.Left => new Rect(-drillBoxRight.xMax, drillBoxRight.y, drillBoxRight.width, drillBoxRight.height),
        _ => drillBoxRight,
    };

    // Select the dino in the Scene view to see the drill areas (yellow) and the ground circle (orange).
    void OnDrawGizmosSelected()
    {
        Vector2 feetPos = transform.position;
        var col = GetComponent<Collider2D>();
        Vector2 origin = col != null ? (Vector2)col.bounds.center : feetPos;
        foreach (var d in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        {
            bool current = Application.isPlaying ? d == Cardinal(Facing) : d == Vector2.right;
            Gizmos.color = current ? new Color(1f, 0.9f, 0f, 0.9f) : new Color(1f, 0.9f, 0f, 0.25f);
            Rect r = DrillBox(d);
            Gizmos.DrawWireCube(feetPos + r.center, r.size);
            Gizmos.color = current ? new Color(1f, 0.5f, 0f, 0.9f) : new Color(1f, 0.5f, 0f, 0.25f);
            Gizmos.DrawWireSphere(origin + d * 0.75f, drillGroundRadius);
        }
    }

    // ---------- hurt and death ----------

    void OnDamaged(int amount, Vector2 from)
    {
        Vector2 away = (Vector2)body.position - from;
        knockback = (away.sqrMagnitude > 0.0001f ? away.normalized : -Facing) * knockbackSpeed;
        if (!stunOnHit)
        {
            // Just a shove: normal movement easing takes over again right away.
            body.linearVelocity = knockback;
            return;
        }
        hurtTimer = hurtTime;
        attackTimer = 0f;     // a hit interrupts drilling
        currentState = null;
    }

    void OnDied()
    {
        IsDead = true;
        attackTimer = 0f;
        currentState = null;
        StartCoroutine(RestartAfterDelay());
    }

    IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(restartDelay);

        // Lose screen with what killed us (e.g. an enemy's Kill Message), or the default message.
        var timer = FindFirstObjectByType<LevelTimer>();
        if (timer != null)
        {
            timer.Fail(string.IsNullOrEmpty(health.LastHitCause) ? deathMessage : health.LastHitCause);
            yield break;
        }

        int index = SceneManager.GetActiveScene().buildIndex;
        if (index >= 0)
        {
            SceneManager.LoadScene(index); // fresh level: walls, enemies and dino all reset
            yield break;
        }
        // Scene isn't in Build Settings: just respawn the dino where it started.
        body.position = spawnPoint;
        body.linearVelocity = Vector2.zero;
        health.ResetHealth();
        IsDead = false;
        currentState = null;
    }

    static Vector2 Cardinal(Vector2 v) =>
        Mathf.Abs(v.y) > Mathf.Abs(v.x) ? new Vector2(0, Mathf.Sign(v.y)) : new Vector2(Mathf.Sign(v.x == 0 ? 1 : v.x), 0);

    string AttackState() => ViewOf(attackDir) switch
    {
        View.Back => backAttack,
        View.Front => frontAttack,
        View.Left => leftAttack,
        _ => rightAttack,
    };

    float ClipLength(string state)
    {
        var ctrl = animator.runtimeAnimatorController;
        var clip = ctrl != null ? ctrl.animationClips.FirstOrDefault(c => c.name == "Dino_" + state || c.name == state) : null;
        return clip != null ? clip.length : 0.5f;
    }

    // ---------- animation ----------

    // Stronger axis wins; diagonals keep the side view.
    static View ViewOf(Vector2 dir) =>
        Mathf.Abs(dir.y) > Mathf.Abs(dir.x) ? (dir.y > 0 ? View.Back : View.Front) : (dir.x < 0 ? View.Left : View.Right);

    void UpdateAnimation()
    {
        Vector2 dir = IsDrilling ? attackDir : Facing;
        View view = ViewOf(dir);
        sprite.flipX = false; // left and right have their own art
        string prefix = hasDrill ? "Drill_" : "";

        string state;
        if (IsDead || IsHurt)
        {
            // Hurt and death art only comes facing left or right.
            view = facedLeft ? View.Left : View.Right;
            state = prefix + (IsDead ? (facedLeft ? leftDeath : rightDeath) : (facedLeft ? leftDamage : rightDamage));
        }
        else if (IsDrilling) state = AttackState();
        else
        {
            state = prefix + view switch
            {
                View.Back => IsMoving ? backWalk : backIdle,
                View.Front => IsMoving ? frontWalk : frontIdle,
                View.Left => IsMoving ? leftWalk : leftIdle,
                _ => IsMoving ? rightWalk : rightIdle,
            };
        }

        bool side = view == View.Right || view == View.Left;
        if (shadow != null)
        {
            shadow.enabled = side && !IsDrilling && !IsDead;
            shadow.flipX = view == View.Left; // the shadow art leans 1 px toward the right-facing feet
        }
        UpdateSweat(side && IsSprinting, view);

        animator.speed = IsSprinting ? sprintAnimationSpeed : 1f;
        if (state != currentState)
        {
            animator.Play(state, 0, 0f);
            currentState = state;
        }
    }

    // While sprinting sideways: play one burst of drops, wait sweatGap seconds, repeat.
    void UpdateSweat(bool active, View view)
    {
        var frames = view == View.Left ? sweatLeft : sweatRight;
        if (sweat == null || !active || frames == null || frames.Length == 0)
        {
            if (sweat != null) sweat.enabled = false;
            sweatClock = 0f; // the next sprint starts with a burst straight away
            return;
        }

        float burst = frames.Length / sweatFrameRate;
        int frame = Mathf.FloorToInt(sweatClock * sweatFrameRate);
        sweat.enabled = frame < frames.Length;
        if (sweat.enabled) sweat.sprite = frames[frame];

        sweatClock += Time.deltaTime;
        if (sweatClock >= burst + sweatGap) sweatClock = 0f;
    }
}
