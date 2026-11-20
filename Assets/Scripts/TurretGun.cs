using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The turret building's gun. Once it's placed, it shoots the nearest enemy within Range tiles (in any
/// direction), one bullet at a time: it waits for its bullet to land, then Fire Interval, before the next shot.
/// Its bullets are player shots (Fired By Player): they hurt enemies, fly past the dino and the robot, and go
/// through shields. It doesn't shoot at enemies behind walls.
/// Nothing happens while the turret is still being placed (Building.Selected).
/// </summary>
[RequireComponent(typeof(Building))]
public class TurretGun : MonoBehaviour
{
    [Tooltip("Shoots enemies this many tiles away or closer.")]
    [Min(0f)] public float range = 4f;
    [Tooltip("The bullet it fires (the dino's gun bullet).")]
    public CrabBullet bullet;
    [Min(0)] public int damage = 1;
    [Tooltip("Seconds between its bullet landing and the next shot.")]
    [Min(0f)] public float fireInterval = 0.5f;
    [Tooltip("How high the gun is above the turret's base (tiles): the bullet is drawn at this height.")]
    public float muzzleHeight = 2.5f;
    [Tooltip("How far out from the middle of the turret the bullet appears (tiles).")]
    public float muzzleOut = 0.6f;
    [Tooltip("Height of the flight line above the base (the middle of the footprint).")]
    public float baseHeight = 0.3f;
    [Tooltip("Played each time it fires (quieter than the dino's gun).")]
    public AudioClip fireSound;
    [Range(0f, 1f)] public float fireVolume = 0.25f;
    [Tooltip("Pitch of the fire sound (below 1 = lower and longer), so the turret doesn't sound like the dino's gun.")]
    [Range(0.3f, 2f)] public float firePitch = 0.7f;

    Building building;
    CrabBullet current;
    AudioSource audioSource;
    float timer;

    void Awake() => building = GetComponent<Building>();

    void Update()
    {
        if (building.Selected || bullet == null) { timer = fireInterval; return; }
        if (current != null) return;                 // one bullet at a time
        if (timer > 0f) { timer -= Time.deltaTime; return; }
        var target = Nearest(out Vector2 aimAt);
        if (target == null) return;
        Fire(aimAt);
        timer = fireInterval;
    }

    Vector2 Base => (Vector2)transform.position + new Vector2(0f, baseHeight);

    // The closest living enemy in range with no wall in the way. aimAt: the point the flight line aims for, so
    // the bullet's art (drawn Muzzle Height above it) crosses the middle of the enemy's body.
    EnemyController Nearest(out Vector2 aimAt)
    {
        aimAt = default;
        EnemyController best = null;
        float bestDist = range * range;
        Vector2 from = Base;
        foreach (var e in EnemyController.All)
        {
            if (e == null || e.IsDead) continue;
            float d = ((Vector2)e.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d > bestDist) continue;
            Vector2 body = e.sprite != null ? (Vector2)e.sprite.bounds.center : (Vector2)e.transform.position;
            Vector2 aim = body - new Vector2(0f, muzzleHeight);
            if (!ClearPath(from, aim)) continue;
            best = e; bestDist = d; aimAt = aim;
        }
        return best;
    }

    static bool ClearPath(Vector2 a, Vector2 b)
    {
        // (Shields don't count: player shots go through them.)
        foreach (var hit in Physics2D.LinecastAll(a, b))
            if (!hit.collider.isTrigger && hit.collider.GetComponentInParent<Tilemap>() != null) return false;
        return true;
    }

    void Fire(Vector2 aimAt)
    {
        Vector2 from = Base;
        Vector2 dir = aimAt - from;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        dir.Normalize();
        Vector2 start = from + dir * muzzleOut;
        current = Instantiate(bullet, start, Quaternion.identity);
        current.firedByPlayer = true;
        current.damage = damage;
        current.range = range + 1.5f;
        current.Launch(dir, gameObject, from, muzzleHeight);
        if (fireSound != null)
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.pitch = firePitch * (1f + Random.Range(-0.04f, 0.04f));
            audioSource.PlayOneShot(fireSound, fireVolume);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
