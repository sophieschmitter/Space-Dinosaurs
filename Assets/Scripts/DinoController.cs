using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-down movement and drilling for the dino. Moves only while a direction key is held:
///   W / Up arrow    = up (shows the dino's back)
///   S / Down arrow  = down (shows the dino's front)
///   A / Left arrow  = left (side view, flipped)
///   D / Right arrow = right (side view)
///   Space (held)    = sprint
///   X               = drill the wall tile in front (hold to keep drilling)
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

    [Header("Drill")]
    [Tooltip("Show the drill-holding animations (needed for the drill attack to look right).")]
    public bool hasDrill = true;

    [Tooltip("The breakable wall layer. Found automatically if left empty.")]
    public DestructibleTilemap walls;

    [Tooltip("Seconds into the attack animation when the drill hits the tile.")]
    public float hitTime = 0.15f; // frame 2, when the drill first reaches out

    [Tooltip("Stand still while drilling.")]
    public bool stopWhileDrilling = true;

    [Header("Animator state names (\"Drill_\" is added in front when Has Drill is on)")]
    public string sideIdle = "Side_Idle", sideWalk = "Side_Walk";
    public string frontIdle = "Front_Idle", frontWalk = "Front_Walk";
    public string backIdle = "Back_Idle", backWalk = "Back_Walk";
    public string sideAttack = "Drill_Attack_Side", frontAttack = "Drill_Attack_Front", backAttack = "Drill_Attack_Back";

    /// <summary>The way the dino last moved (world space).</summary>
    public Vector2 Facing { get; private set; } = Vector2.right;
    public bool IsMoving { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsDrilling => attackTimer > 0f;

    enum View { Side, Front, Back }

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
        if (IsMoving) Facing = input;

        UpdateAttack();
        UpdateAnimation();
    }

    void FixedUpdate()
    {
        float speed = IsSprinting ? sprintSpeed : walkSpeed;
        body.linearVelocity = IsMoving ? input * speed : Vector2.zero; // stops as soon as keys are released
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
        _ => sideAttack,
    };

    float ClipLength(string state)
    {
        var ctrl = animator.runtimeAnimatorController;
        var clip = ctrl != null ? ctrl.animationClips.FirstOrDefault(c => c.name == "Dino_" + state || c.name == state) : null;
        return clip != null ? clip.length : 0.5f;
    }

    // ---------- animation ----------

    static View ViewOf(Vector2 dir) =>
        Mathf.Abs(dir.y) > Mathf.Abs(dir.x) ? (dir.y > 0 ? View.Back : View.Front) : View.Side;

    void UpdateAnimation()
    {
        Vector2 dir = IsDrilling ? attackDir : Facing;
        View view = ViewOf(dir); // stronger axis wins; diagonals keep the side view
        sprite.flipX = view == View.Side && dir.x < 0; // side art faces right; front/back never flipped

        string state;
        if (IsDrilling) state = AttackState();
        else
        {
            string prefix = hasDrill ? "Drill_" : "";
            state = prefix + view switch
            {
                View.Back => IsMoving ? backWalk : backIdle,
                View.Front => IsMoving ? frontWalk : frontIdle,
                _ => IsMoving ? sideWalk : sideIdle,
            };
        }

        animator.speed = IsSprinting ? sprintAnimationSpeed : 1f;
        if (state != currentState)
        {
            animator.Play(state, 0, 0f);
            currentState = state;
        }
    }
}
