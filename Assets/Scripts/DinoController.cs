using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
///   WASD or arrow keys to move
///   Shift (held)    = sprint
///   Spacebar = drill/attack/fire gun
///   Q  = swap weapons
/// Game controller: left stick or d-pad = move, X = attack, Y = weapon select, A = sprint.
///   Left click      = the same, aimed at the mouse: the drill turns to the nearest of up/down/left/right,
///                     the gun fires straight at the cursor at any angle (clicks on HUD buttons are ignored)
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
    [Tooltip("Holding the gun (plays the Gun_ animations). Ignored while Has Drill is on. " +
             "The weapon-select buttons call EquipDrill / EquipGun.")]
    public bool hasGun = false;

    [Header("Gun")]
    [Tooltip("What the gun fires (for now the tall crab's bullet art, as the DinoBullet prefab).")]
    public CrabBullet gunBullet;
    [Tooltip("Seconds between shots; holding X keeps firing at this rate.")]
    [Min(0.05f)] public float fireInterval = 0.35f;
    [Min(0)] public int gunDamage = 1;
    [Tooltip("Where the bullet leaves the gun, in tiles from the feet (x right, y up), for each way the dino faces. " +
             "Measured from the gun art. Left is Right mirrored.")]
    public Vector2 muzzleRight = new Vector2(1.43f, 0.78f);
    public Vector2 muzzleFront = new Vector2(-0.59f, 0.13f);
    public Vector2 muzzleBack = new Vector2(0.81f, 1.88f);
    [Tooltip("Each shot swaps the idle/walk art for these versions in turn (gun kicked back 2 px, then 3 px), " +
             "staying in step with the walk. Reorder to change the kick, e.g. Recoil2 then Recoil1.")]
    public string[] recoilFrames = { "Recoil1", "Recoil2" };
    [Tooltip("Seconds each recoil frame shows.")]
    [Min(0.01f)] public float recoilFrameTime = 0.07f;

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
    [Tooltip("Shadow under the side views (taken from the Shadow child if left empty).")]
    public Sprite shadowSide;
    [Tooltip("Shadow and sweat for the drill dino facing front or back.")]
    public Sprite shadowVertical;
    public Sprite[] sweatVertical;
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

    InputAction move, sprint, attack, click, swapWeapon;
    bool aimWithMouse;   // the last attack press was a click: aim at the cursor
    bool clickOnUI;      // the current click started on a HUD button, so it isn't an attack
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
        move.AddBinding("<Gamepad>/leftStick"); // has the controller's dead zone built in
        move.AddBinding("<Gamepad>/dpad");

        sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/shift");
        sprint.AddBinding("<Gamepad>/buttonSouth");   // A (Xbox) / Cross (PlayStation)
        attack = new InputAction("Attack", InputActionType.Button, "<Keyboard>/space");
        attack.AddBinding("<Gamepad>/buttonWest");    // X (Xbox) / Square (PlayStation)
        click = new InputAction("Click Attack", InputActionType.Button, "<Mouse>/leftButton");
        swapWeapon = new InputAction("Swap Weapon", InputActionType.Button, "<Keyboard>/q");
        swapWeapon.AddBinding("<Gamepad>/buttonNorth"); // Y (Xbox) / Triangle (PlayStation)

        if (walls == null) walls = FindFirstObjectByType<DestructibleTilemap>();
    }

    void OnEnable()
    {
        move.Enable(); sprint.Enable(); attack.Enable(); click.Enable(); swapWeapon.Enable();
        if (health != null) { health.Damaged += OnDamaged; health.Died += OnDied; }
    }

    void OnDisable()
    {
        move.Disable(); sprint.Disable(); attack.Disable(); click.Disable(); swapWeapon.Disable();
        body.linearVelocity = Vector2.zero;
        if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
    }
    void OnDestroy() { move.Dispose(); sprint.Dispose(); attack.Dispose(); click.Dispose(); swapWeapon.Dispose(); }

    void Update()
    {
        input = move.ReadValue<Vector2>();
        if (input.sqrMagnitude > 1f) input.Normalize(); // diagonals aren't faster

        if (hurtTimer > 0f) hurtTimer -= Time.deltaTime;
        if (swapWeapon.WasPressedThisFrame() && !IsDead) SwapWeapon();
        // Remember a press even mid-attack or while stunned, so quick taps aren't lost.
        if (click.WasPressedThisFrame())
        {
            clickOnUI = PointerOverButton();
            if (!clickOnUI) { bufferedPress = pressBuffer; aimWithMouse = true; }
        }
        if (attack.WasPressedThisFrame()) { bufferedPress = pressBuffer; aimWithMouse = false; }
        else if (bufferedPress > 0f && !click.WasPressedThisFrame()) bufferedPress -= Time.deltaTime;
        bool locked = IsDead || IsHurt || (IsDrilling && stopWhileDrilling);
        IsMoving = !locked && input.sqrMagnitude > 0.01f;
        IsSprinting = IsMoving && sprint.IsPressed();
        if (IsMoving) UpdateFacing();

        if (gunTimer > 0f) gunTimer -= Time.deltaTime;
        if (recoilTimer > 0f) recoilTimer -= Time.deltaTime;
        if (!IsDead && !IsHurt) { UpdateAttack(); UpdateGun(); }
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
        if (hasDrill && (bufferedPress > 0f || AttackHeld))
        {
            bufferedPress = 0f;
            if (aimWithMouse) FaceTowards(Aim(DrillOrigin())); // turn to the side the cursor is on
            attackDir = Cardinal(Facing);
            attackTimer = ClipLength(AttackState());
            hitDone = false;
            hitThisAttack.Clear();
            currentState = null; // restart the attack animation from its first frame
        }
    }

    // True when the mouse is over a clickable HUD control (a Button etc.). Only those swallow the click:
    // other UI under the cursor, like the floor Image on the world-space Canvas, doesn't block attacking.
    static readonly System.Collections.Generic.List<RaycastResult> uiHits = new();
    static bool PointerOverButton()
    {
        var es = EventSystem.current;
        if (es == null || Mouse.current == null) return false;
        var data = new PointerEventData(es) { position = Mouse.current.position.ReadValue() };
        uiHits.Clear();
        es.RaycastAll(data, uiHits);
        foreach (var hit in uiHits)
            if (hit.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
        return false;
    }

    // X held, or the mouse button held after a click that wasn't on the HUD.
    bool AttackHeld => attack.IsPressed() || (click.IsPressed() && !clickOnUI && aimWithMouse);

    // The mouse in the world (on the dino's plane).
    Vector2 MouseWorld()
    {
        var cam = Camera.main;
        if (cam == null || Mouse.current == null) return (Vector2)transform.position + Facing;
        Vector3 p = Mouse.current.position.ReadValue();
        p.z = -cam.transform.position.z;
        return cam.ScreenToWorldPoint(p);
    }

    // From a point to the mouse (or straight ahead if the cursor is right on it).
    Vector2 Aim(Vector2 from)
    {
        Vector2 v = MouseWorld() - from;
        return v.sqrMagnitude > 0.0001f ? v : Facing;
    }

    // Turn to the nearest of up/down/left/right toward a direction.
    void FaceTowards(Vector2 dir)
    {
        Facing = Cardinal(dir);
        if (Facing.x != 0) facedLeft = Facing.x < 0;
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

    // ---------- gun ----------

    float gunTimer, recoilTimer;

    // X with the gun: fire straight ahead. Click: fire straight at the cursor, at any angle (the art turns to the
    // nearest side). Holding either keeps firing every Fire Interval.
    void UpdateGun()
    {
        if (hasDrill || !hasGun || gunBullet == null || gunTimer > 0f) return;
        if (!(bufferedPress > 0f || AttackHeld)) return;
        bufferedPress = 0f;
        gunTimer = fireInterval;

        if (aimWithMouse) FaceTowards(Aim(DrillOrigin()));
        Vector2 dir = Cardinal(Facing);
        View view = ViewOf(dir);
        Vector2 muzzle = view switch
        {
            View.Front => muzzleFront,
            View.Back => muzzleBack,
            View.Left => new Vector2(-muzzleRight.x, muzzleRight.y),
            _ => muzzleRight,
        };
        Vector2 feetPos = transform.position;
        Vector2 center = DrillOrigin(); // middle of the feet collider: the height shots travel at

        Vector2 start; float artHeight;
        if (view == View.Left || view == View.Right)
        {
            start = new Vector2(feetPos.x + muzzle.x, center.y); // fly along the ground line...
            artHeight = feetPos.y + muzzle.y - center.y;         // ...drawn at the gun's height
        }
        else
        {
            start = feetPos + muzzle; // up/down: the gun's spot is on the flight line itself
            artHeight = 0f;
        }
        // Aimed with the mouse: fly so the bullet's art (drawn Art Height above its path) crosses the cursor.
        if (aimWithMouse)
        {
            Vector2 toCursor = MouseWorld() - new Vector2(0f, artHeight) - start;
            if (toCursor.sqrMagnitude > 0.01f && Vector2.Dot(toCursor, dir) > 0f) dir = toCursor.normalized;
        }
        recoilTimer = recoilFrames.Length * recoilFrameTime;
        var shot = Instantiate(gunBullet, start, Quaternion.identity);
        shot.firedByPlayer = true;
        shot.damage = gunDamage;
        Vector2 castFrom = view == View.Left || view == View.Right ? new Vector2(center.x, start.y) : new Vector2(start.x, center.y);
        shot.Launch(dir, gameObject, castFrom, artHeight);
    }

    // ---------- weapons ----------

    /// <summary>Switch to the drill (X drills).</summary>
    public void EquipDrill() => SetWeapon(true, false);

    /// <summary>Switch to the gun (Gun_ animations; X or click fires).</summary>
    public void EquipGun() => SetWeapon(false, true);

    /// <summary>Switch to whichever weapon isn't in hand (Q).</summary>
    public void SwapWeapon()
    {
        if (hasDrill) EquipGun(); else EquipDrill();
    }

    void SetWeapon(bool drill, bool gun)
    {
        if (hasDrill == drill && hasGun == gun) return;
        hasDrill = drill; hasGun = gun;
        attackTimer = 0f;     // stop a drill swing in progress
        currentState = null;  // switch art straight away
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
        string prefix = hasDrill ? "Drill_" : hasGun ? "Gun_" : "";

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
            // Just fired: show the matching recoil frames (if the art has them).
            if (recoilTimer > 0f && recoilFrames.Length > 0)
            {
                float total = recoilFrames.Length * recoilFrameTime;
                int i = Mathf.Clamp(Mathf.FloorToInt((total - recoilTimer) / recoilFrameTime), 0, recoilFrames.Length - 1);
                string recoil = state + "_" + recoilFrames[i];
                if (animator.HasState(0, Animator.StringToHash(recoil))) state = recoil;
            }
        }

        bool side = view == View.Right || view == View.Left;
        bool vertical = !side && hasDrill; // front/back effects are drawn for the drill dino
        if (shadow != null)
        {
            if (shadowSide == null) shadowSide = shadow.sprite;
            bool hasArt = side ? shadowSide != null : vertical && shadowVertical != null;
            shadow.enabled = hasArt && !IsDrilling && !IsDead;
            shadow.sprite = side ? shadowSide : shadowVertical;
            shadow.flipX = view == View.Left; // the side shadow art leans 1 px toward the right-facing feet
        }
        var sweatFrames = side ? (view == View.Left ? sweatLeft : sweatRight) : vertical ? sweatVertical : null;
        UpdateSweat((side || vertical) && IsSprinting, sweatFrames);

        animator.speed = IsSprinting ? sprintAnimationSpeed : 1f;
        if (state != currentState)
        {
            // Swapping between a walk/idle and its recoil version: carry on from the same frame.
            bool sameMove = currentState != null && BaseState(state) == BaseState(currentState);
            float t = sameMove ? animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
            animator.Play(state, 0, t);
            currentState = state;
        }
    }

    static string BaseState(string state)
    {
        int i = state.IndexOf("_Recoil", System.StringComparison.Ordinal);
        return i >= 0 ? state.Substring(0, i) : state;
    }

    // While sprinting: play one burst of drops, wait sweatGap seconds, repeat.
    void UpdateSweat(bool active, Sprite[] frames)
    {
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
