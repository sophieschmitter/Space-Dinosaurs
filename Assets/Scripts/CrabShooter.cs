using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Lets an enemy (the tall crab) shoot at the dino. When the dino is in range, lined up straight
/// left/right/up/down from it (like every other attack in the game) and not behind a wall, the crab stops,
/// glows (its "Shoot" state) for Wind Up seconds, fires a Bullet Prefab (CrabBullet) along that line,
/// then waits Cooldown seconds before it can shoot again.
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class CrabShooter : MonoBehaviour
{
    public CrabBullet bulletPrefab;

    [Tooltip("Shoots when the dino is within this many tiles.")]
    public float range = 6f;
    [Tooltip("How far off the crab's row or column (tiles) the dino can be and still get shot at.")]
    public float lineUpTolerance = 0.4f;
    [Tooltip("Seconds between shots.")]
    public float cooldown = 2.5f;
    [Tooltip("Seconds after first seeing the dino before the first shot.")]
    public float firstShotDelay = 1f;
    [Tooltip("Seconds it stands glowing before the bullet comes out (a warning for the player).")]
    public float windUp = 0.4f;
    [Tooltip("Seconds it stays still after firing.")]
    public float recover = 0.3f;
    [Tooltip("How far out from the crab's middle the bullet appears (tiles). Anything in that gap still gets hit.")]
    public float muzzleDistance = 1f;
    [Tooltip("Animator state shown while winding up and firing.")]
    public string shootState = "Shoot";

    EnemyController ai;
    Health health;
    Transform player;
    Collider2D playerCollider;
    float cooldownTimer;
    bool shooting, sawPlayer;

    void Awake()
    {
        ai = GetComponent<EnemyController>();
        health = GetComponent<Health>();
    }

    void Start()
    {
        var dino = FindFirstObjectByType<DinoController>();
        if (dino != null) { player = dino.transform; playerCollider = dino.GetComponent<Collider2D>(); }
    }

    void Update()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        if (shooting || bulletPrefab == null || player == null) return;
        if (ai.IsDead || ai.IsCelebrating || ai.IsHurt || (health != null && health.IsDead)) return;
        var dino = player.GetComponent<DinoController>();
        if (dino != null && dino.IsDead) return;

        Vector2 to = Target() - (Vector2)transform.position;
        bool inRange = to.magnitude <= range;
        if (!inRange) { sawPlayer = false; return; }
        if (!sawPlayer) { sawPlayer = true; cooldownTimer = Mathf.Max(cooldownTimer, firstShotDelay); }
        if (cooldownTimer > 0f) return;

        // Lined up on a row or column?
        Vector2 dir;
        if (Mathf.Abs(to.y) <= lineUpTolerance && Mathf.Abs(to.x) > Mathf.Abs(to.y)) dir = new Vector2(Mathf.Sign(to.x), 0);
        else if (Mathf.Abs(to.x) <= lineUpTolerance && Mathf.Abs(to.y) > Mathf.Abs(to.x)) dir = new Vector2(0, Mathf.Sign(to.y));
        else return;
        if (!ClearShot(Target())) return;

        StartCoroutine(Shoot(dir));
    }

    Vector2 Target() => playerCollider != null ? (Vector2)playerCollider.bounds.center : (Vector2)player.position;

    // Nothing solid (walls, spawners, the robot) between the crab and the dino.
    bool ClearShot(Vector2 point)
    {
        foreach (var hit in Physics2D.LinecastAll(transform.position, point))
        {
            var c = hit.collider;
            if (c.isTrigger || c.transform.IsChildOf(transform) || c.GetComponentInParent<EnemyController>() != null) continue;
            if (c.GetComponentInParent<DinoController>() != null) return true;
            if (c.GetComponentInParent<Tilemap>() != null || c.GetComponentInParent<EnemySpawner>() != null || c.GetComponentInParent<EnemyGoal>() != null)
                return false;
        }
        return true;
    }

    IEnumerator Shoot(Vector2 dir)
    {
        shooting = true;
        ai.Face(dir);
        ai.Hold(windUp + recover, shootState);
        yield return new WaitForSeconds(windUp);

        if (!ai.IsDead && !ai.IsCelebrating)
        {
            var bullet = Instantiate(bulletPrefab, (Vector2)transform.position + dir * muzzleDistance, Quaternion.identity);
            bullet.Launch(dir, gameObject, transform.position);
        }
        cooldownTimer = cooldown;
        yield return new WaitForSeconds(recover);
        shooting = false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.35f);
        Vector3 p = transform.position;
        Gizmos.DrawLine(p + Vector3.left * range, p + Vector3.right * range);
        Gizmos.DrawLine(p + Vector3.down * range, p + Vector3.up * range);
    }
}
