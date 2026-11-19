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
    float artHeight; // how far above the flight line the art is drawn
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
        // artHeight is in world tiles: divide out the bullet's own scale (the laser prefab is scaled up).
        float scaleY = Mathf.Abs(transform.lossyScale.y) > 1e-4f ? transform.lossyScale.y : 1f;
        if (art != null && artHeight.HasValue) art.localPosition = new Vector3(0f, artHeight.Value / scaleY, art.localPosition.z);
        this.artHeight = art != null ? art.position.y - transform.position.y : 0f;
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
        // The dino's shots hit a crab's visible body either where the laser is drawn (Art Height above the
        // flight line), so a shot that looks like it goes through a crab hits it, or along the flight line on the
        // ground, so a crab standing level with the shooter is hit even when it's shorter than the gun is high.
        if (firedByPlayer)
        {
            Vector2 lift = new Vector2(0f, artHeight);
            float along = target != null ? nearest : dist;
            EnemyController enemy = null; float enemyAt = float.MaxValue;
            foreach (var e in EnemyController.All)
            {
                if (e == null || e.IsDead || e.sprite == null) continue;
                var box = BodyBox(e.sprite);
                if (SegmentHitsBox(from + lift, d / dist, along, box, out float t) && t < enemyAt) { enemyAt = t; enemy = e; }
                if (SegmentHitsBox(from, d / dist, along, box, out float g) && g < enemyAt) { enemyAt = g; enemy = e; }
            }
            if (enemy != null)
            {
                transform.position = from + d / dist * enemyAt;
                HitEnemy(enemy);
                return true;
            }
        }
        if (target == null) return false;
        transform.position = point;
        Hit(target);
        return true;
    }

    // The opaque part of the sprite in world space (tight sprite meshes skip the empty padding), shrunk a touch.
    static Bounds BodyBox(SpriteRenderer r)
    {
        var s = r.sprite;
        if (s == null) return r.bounds;
        var v = s.vertices;
        if (v == null || v.Length == 0) return r.bounds;
        Vector2 lo = v[0], hi = v[0];
        foreach (var p in v) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
        var t = r.transform;
        Vector3 a = t.TransformPoint(new Vector3(r.flipX ? -lo.x : lo.x, r.flipY ? -lo.y : lo.y));
        Vector3 b = t.TransformPoint(new Vector3(r.flipX ? -hi.x : hi.x, r.flipY ? -hi.y : hi.y));
        var box = new Bounds((a + b) * 0.5f, new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), 1f));
        box.Expand(new Vector3(-0.1f, -0.1f, 0f));
        return box;
    }

    // Does the shot (a circle of Radius moving from o along unit dir for len) touch the box? t: how far along.
    bool SegmentHitsBox(Vector2 o, Vector2 dir, float len, Bounds box, out float t)
    {
        t = 0f;
        Vector2 lo = (Vector2)box.min - Vector2.one * radius, hi = (Vector2)box.max + Vector2.one * radius;
        float tMin = 0f, tMax = len;
        for (int axis = 0; axis < 2; axis++)
        {
            float oa = o[axis], da = dir[axis];
            if (Mathf.Abs(da) < 1e-6f) { if (oa < lo[axis] || oa > hi[axis]) return false; continue; }
            float t1 = (lo[axis] - oa) / da, t2 = (hi[axis] - oa) / da;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1); tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        t = tMin;
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
        // (Player shots skip enemies here too: CheckPath tests them against their art instead.)
        return firedByPlayer
            ? c.GetComponentInParent<DinoController>() != null || c.GetComponentInParent<EnemyGoal>() != null
              || c.GetComponentInParent<EnemyController>() != null
            : c.GetComponentInParent<EnemyController>() != null;
    }

    void Hit(Collider2D c)
    {
        var from = (Vector2)transform.position - dir; // knock the target along the shot
        if (firedByPlayer)
        {
            var enemy = c.GetComponentInParent<EnemyController>();
            if (enemy != null) { HitEnemy(enemy); return; }
            Explode();
            return;
        }
        var dino = c.GetComponentInParent<DinoController>();
        var goal = dino == null ? c.GetComponentInParent<EnemyGoal>() : null;
        if (dino != null && dino.TryGetComponent(out Health h)) h.TakeDamage(damage, from, killMessage);
        else if (goal != null)
        {
            if (owner != null) goal.ReportAttack(owner.GetComponent<EnemyController>()); // a tall crab shooting the robot
            if (goal.TryGetComponent(out Health gh)) gh.TakeDamage(damage, from, goal.destroyedMessage);
        }
        // Walls (Tilemap) and spawners just stop it.
        Explode();
    }

    void HitEnemy(EnemyController enemy)
    {
        if (enemy.TryGetComponent(out Health eh)) eh.TakeDamage(damage, (Vector2)transform.position - dir);
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
