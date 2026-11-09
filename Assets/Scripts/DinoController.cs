using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-down movement for the dino. Moves only while a direction key is held:
///   W / Up arrow    = up (shows the dino's back)
///   S / Down arrow  = down (shows the dino's front)
///   A / Left arrow  = left (side view, flipped)
///   D / Right arrow = right (side view)
///   Space (held)    = sprint
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

    [Header("Animator state names")]
    public string sideIdle = "Side_Idle", sideWalk = "Side_Walk";
    public string frontIdle = "Front_Idle", frontWalk = "Front_Walk";
    public string backIdle = "Back_Idle", backWalk = "Back_Walk";

    /// <summary>The way the dino last moved (world space). Use this to aim a pickaxe, e.g. walls.HitInFront(pos, Facing).</summary>
    public Vector2 Facing { get; private set; } = Vector2.right;
    public bool IsMoving { get; private set; }
    public bool IsSprinting { get; private set; }

    enum View { Side, Front, Back }

    InputAction move, sprint;
    Rigidbody2D body;
    Animator animator;
    SpriteRenderer sprite;
    Vector2 input;
    string currentState;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        sprite = GetComponent<SpriteRenderer>();

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
    }

    void OnEnable() { move.Enable(); sprint.Enable(); }
    void OnDisable() { move.Disable(); sprint.Disable(); body.linearVelocity = Vector2.zero; }
    void OnDestroy() { move.Dispose(); sprint.Dispose(); }

    void Update()
    {
        input = move.ReadValue<Vector2>();
        if (input.sqrMagnitude > 1f) input.Normalize(); // diagonals aren't faster

        IsMoving = input.sqrMagnitude > 0.01f;
        IsSprinting = IsMoving && sprint.IsPressed();
        if (IsMoving) Facing = input;

        UpdateAnimation();
    }

    void FixedUpdate()
    {
        float speed = IsSprinting ? sprintSpeed : walkSpeed;
        body.linearVelocity = IsMoving ? input * speed : Vector2.zero; // stops as soon as keys are released
    }

    void UpdateAnimation()
    {
        // Pick the view from the stronger axis; ties (diagonals) keep the side view.
        View view = Mathf.Abs(Facing.y) > Mathf.Abs(Facing.x) ? (Facing.y > 0 ? View.Back : View.Front) : View.Side;
        sprite.flipX = view == View.Side && Facing.x < 0; // side art faces right; front/back never flipped

        string state = view switch
        {
            View.Back => IsMoving ? backWalk : backIdle,
            View.Front => IsMoving ? frontWalk : frontIdle,
            _ => IsMoving ? sideWalk : sideIdle,
        };
        animator.speed = IsSprinting ? sprintAnimationSpeed : 1f;
        if (state != currentState)
        {
            animator.Play(state);
            currentState = state;
        }
    }
}
