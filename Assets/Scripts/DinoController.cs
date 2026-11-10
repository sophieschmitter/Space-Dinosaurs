using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

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

    [Tooltip("Stand still while drilling.")]
    public bool stopWhileDrilling = true;

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

    /// <summary>The way the dino faces: always straight up, down, left or right. It's the first direction
    /// you held, so adding a second direction moves diagonally without turning the dino.</summary>
    public Vector2 Facing { get; private set; } = Vector2.right;
    public bool IsMoving { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsDrilling => attackTimer > 0f;

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
    Vector2 attackDir;   // cardinal direction of the current attack
    float sweatClock;    // time since the current sweat burst started
    float cruiseSpeed;   // speed the dino was last asked to move at (sets the stopping rate)

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        sprite = GetComponent<SpriteRenderer>();
        feet = GetComponent<Collider2D>();

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

    void OnEnable() { move.Enable(); sprint.Enable(); attack.Enable(); }
    void OnDisable() { move.Disable(); sprint.Disable(); attack.Disable(); body.linearVelocity = Vector2.zero; }
    void OnDestroy() { move.Dispose(); sprint.Dispose(); attack.Dispose(); }

    void Update()
    {
        input = move.ReadValue<Vector2>();
        if (input.sqrMagnitude > 1f) input.Normalize(); // diagonals aren't faster

        bool locked = IsDrilling && stopWhileDrilling;
        IsMoving = !locked && input.sqrMagnitude > 0.01f;
        IsSprinting = IsMoving && sprint.IsPressed();
        if (IsMoving) UpdateFacing();

        UpdateAttack();
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
    }

    void FixedUpdate()
    {
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
            if (!hitDone && length - attackTimer >= hitTime) { hitDone = true; HitTile(); }
            if (attackTimer > 0f) return;
            currentState = null; // let the idle/walk state come back
        }

        // Start an attack on press, and keep drilling while X stays held.
        if (hasDrill && (attack.WasPressedThisFrame() || attack.IsPressed()))
        {
            attackDir = Cardinal(Facing);
            attackTimer = ClipLength(AttackState());
            hitDone = false;
            currentState = null; // restart the attack animation from its first frame
        }
    }

    void HitTile()
    {
        if (walls == null) return;
        // Aim from the middle of the feet collider into the next tile over.
        Vector2 origin = feet != null ? (Vector2)feet.bounds.center : body.position;
        walls.HitInFront(origin, attackDir);
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

        string state;
        if (IsDrilling) state = AttackState();
        else
        {
            string prefix = hasDrill ? "Drill_" : "";
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
            shadow.enabled = side && !IsDrilling;
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
