using UnityEngine;

/// <summary>
/// A shot (the tall crab's, and for now the dino's gun too). Flies in a straight line at floor level (so it
/// hits walls and feet colliders where it looks like it should) and explodes on the first thing it touches,
/// or when its range runs out.
///   Enemy shot (Fired By Player off): flies past crabs; hurts the dino or the robot; stopped by walls/spawners.
///   Player shot (Fired By Player on): flies past the dino and the robot; hurts crabs; stopped by walls/spawners.
///   Shields from a shield generator stop enemy shots; player shots pass through them.
///
/// Art lives on the Art child, raised to the height it leaves the crab and turned to face the way it flies
/// (the art is drawn flying left). Animator states: Launch -> Fly (loops) while flying, Explode when it hits.
/// </summary>
public class CrabBullet : MonoBehaviour
{
    [Tooltip("Tiles per second.")]
    public float speed = 6f;
    [Min(0)] public int damage = 1;
    [Tooltip("Explodes by itself after flying this many tiles.")]
    public float range = 8f;
    [Tooltip("Size of the bullet for hitting things (tiles).")]
    public float radius = 0.15f;
    [Tooltip("Seconds the Explode animation plays before the bullet is removed.")]
    public float explodeTime = 0.34f;
    [Tooltip("Shown on the lose screen if this shot kills the dino.")]
    [TextArea] public string killMessage = "A tall crab shot you.";
    [Tooltip("On for the dino's gun: hits enemies instead of the dino and the robot.")]
    public bool firedByPlayer = false;

    [Header("Art")]
    [Tooltip("The Art child (sprite + animator). It's rotated to face the flight direction.")]
    public Transform art;
    public Animator animator;
    [Tooltip("The art is drawn pointing this way.")]
    public Vector2 artFaces = Vector2.left;

    Vector2 dir;
    float travelled, explodeTimer = -1f;
    GameObject owner;
    readonly RaycastHit2D[] hits = new RaycastHit2D[8];

    /// <summary>Send it off. The owner's own colliders are ignored. If it starts away from the shooter,
    /// pass the shooter's position as castFrom so it still hits anything in the gap in between.
    /// artHeight (optional) raises the art this far above the bullet's flight line, e.g. to the gun's height.</summary>
    public void Launch(Vector2 direction, GameObject from, Vector2? castFrom = null, float? artHeight = null)
    {
        dir = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.left;
        owner = from;
        if (art == null && transform.childCount > 0) art = transform.GetChild(0);
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (art != null) art.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(artFaces, dir));
        if (art != null && artHeight.HasValue) art.localPosition = new Vector3(0f, artHeight.Value, art.localPosition.z);
        Play("Launch");
        if (castFrom.HasValue) CheckPath(castFrom.Value, (Vector2)transform.position);
    }

    // Moves along from -> to (this frame's flight, or the gap to the muzzle) and hits the first thing on the way.
    bool CheckPath(Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        float dist = d.magnitude;
        if (dist <= 0f) return false;
        int n = Physics2D.CircleCastNonAlloc(from, radius, d / dist, hits, dist);
        float nearest = float.MaxValue; Collider2D target = null; Vector2 point = default;
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c.isTrigger || IsOwner(c) || PassesThrough(c)) continue;
            if (hits[i].distance < nearest) { nearest = hits[i].distance; target = c; point = hits[i].centroid; }
        }
        if (target == null) return false;
        transform.position = point;
        Hit(target);
        return true;
    }

    void Update()
    {
        if (explodeTimer >= 0f)
        {
            explodeTimer += Time.deltaTime;
            if (explodeTimer >= explodeTime) Destroy(gameObject);
            return;
        }
        if (dir == Vector2.zero) return; // not launched

        float step = speed * Time.deltaTime;
        if (CheckPath(transform.position, (Vector2)transform.position + dir * step)) return;
        transform.position += (Vector3)(dir * step);
        travelled += step;
        if (travelled >= range) Explode();
    }

    bool IsOwner(Collider2D c) => owner != null && c.transform.IsChildOf(owner.transform);

    // Enemy shots fly past other enemies; player shots fly past the dino and the robot.
    // Shields (ShieldField) stop enemy shots and let the dino's through.
    bool PassesThrough(Collider2D c)
    {
        if (c.GetComponent<ShieldField>() != null) return firedByPlayer;
        return firedByPlayer
            ? c.GetComponentInParent<DinoController>() != null || c.GetComponentInParent<EnemyGoal>() != null
            : c.GetComponentInParent<EnemyController>() != null;
    }

    void Hit(Collider2D c)
    {
        var from = (Vector2)transform.position - dir; // knock the target along the shot
        if (firedByPlayer)
        {
            var enemy = c.GetComponentInParent<EnemyController>();
            if (enemy != null && enemy.TryGetComponent(out Health eh)) eh.TakeDamage(damage, from);
            Explode();
            return;
        }
        var dino = c.GetComponentInParent<DinoController>();
        var goal = dino == null ? c.GetComponentInParent<EnemyGoal>() : null;
        if (dino != null && dino.TryGetComponent(out Health h)) h.TakeDamage(damage, from, killMessage);
        else if (goal != null && goal.TryGetComponent(out Health gh)) gh.TakeDamage(damage, from, goal.destroyedMessage);
        // Walls (Tilemap) and spawners just stop it.
        Explode();
    }

    void Explode()
    {
        explodeTimer = 0f;
        Play("Explode");
    }

    void Play(string state)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        int hash = Animator.StringToHash(state);
        if (animator.HasState(0, hash)) animator.Play(hash, 0, 0f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
