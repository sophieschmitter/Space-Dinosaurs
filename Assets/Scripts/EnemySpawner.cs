using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// A stationary, indestructible block that keeps producing enemies.
/// With Path To Goal ticked its enemies first head for the robot, then behave normally.
/// Two prefabs: Crab Spawner (crab1) and Tall Crab Spawner (the stronger tall crab).
/// Every Spawn Interval seconds it puts an enemy on a free tile next to itself, as long as fewer than
/// Max Alive of its enemies are still around. It has no Health, so the drill can't hurt it,
/// and it isn't a wall tile, so it can't be broken either.
///
/// Art is swappable: replace the sprite on the Art child. If the Animator there gets a controller with a
/// "Spawn" state, it plays each time an enemy comes out (and "Idle" otherwise).
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class EnemySpawner : MonoBehaviour
{
    [Tooltip("What to spawn.")]
    public GameObject enemyPrefab;

    [Tooltip("Seconds between spawns.")]
    [Min(0.1f)] public float spawnInterval = 6f;

    [Tooltip("Seconds before the first spawn.")]
    [Min(0f)] public float firstSpawnDelay = 2f;

    [Tooltip("This spawner stops while this many of its enemies are alive.")]
    [Min(1)] public int maxAlive = 2;

    [Tooltip("Only spawn while the dino is within this many tiles (0 = always).")]
    [Min(0f)] public float activationRange = 0f;

    [Tooltip("Don't spawn while the dino is closer than this (tiles), so enemies don't pop out on top of it.")]
    [Min(0f)] public float minPlayerDistance = 2f;

    [Tooltip("Each enemy first follows a path around the walls to the robot (the EnemyGoal), then behaves normally.")]
    public bool pathToGoal = true;

    [Header("Art")]
    public Animator animator;

    readonly List<GameObject> alive = new();
    Tilemap walls;
    Transform player;

    void Awake()
    {
        var body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static; // never moves, blocks the dino and enemies like a wall
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        var destructible = FindFirstObjectByType<DestructibleTilemap>();
        if (destructible != null) walls = destructible.GetComponent<Tilemap>();
        var dino = FindFirstObjectByType<DinoController>();
        if (dino != null) player = dino.transform;
        StartCoroutine(SpawnLoop());
    }

    IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(firstSpawnDelay);
        while (true)
        {
            alive.RemoveAll(e => e == null);
            if (isActiveAndEnabled && enemyPrefab != null && alive.Count < maxAlive && PlayerAllows() && TryFindSpot(out Vector2 spot))
            {
                var enemy = Instantiate(enemyPrefab, spot, Quaternion.identity);
                enemy.name = $"{enemyPrefab.name} ({name})";
                alive.Add(enemy);
                if (pathToGoal && enemy.TryGetComponent(out EnemyController ai)) ai.GoTo(EnemyGoal.Find());
                Play("Spawn");
            }
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    bool PlayerAllows()
    {
        if (player == null) return true;
        var dino = player.GetComponent<DinoController>();
        if (dino != null && dino.IsDead) return false;
        float dist = Vector2.Distance(player.position, transform.position);
        if (activationRange > 0f && dist > activationRange) return false;
        return dist >= minPlayerDistance;
    }

    // A random neighbouring tile with no wall and nothing standing on it.
    bool TryFindSpot(out Vector2 spot)
    {
        var offsets = new List<Vector2Int>();
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
            if (x != 0 || y != 0) offsets.Add(new Vector2Int(x, y));

        for (int i = offsets.Count - 1; i > 0; i--) // shuffle
        {
            int j = Random.Range(0, i + 1);
            (offsets[i], offsets[j]) = (offsets[j], offsets[i]);
        }

        foreach (var o in offsets)
        {
            Vector2 p = (Vector2)transform.position + o;
            if (walls != null && walls.HasTile(walls.WorldToCell(p))) continue;
            if (Physics2D.OverlapCircle(p, 0.4f) != null) continue; // something already there
            spot = p;
            return true;
        }
        spot = default;
        return false;
    }

    void Play(string state)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        int hash = Animator.StringToHash(state);
        if (animator.HasState(0, hash)) animator.Play(hash, 0, 0f);
    }

    // Select the spawner to see where it can put enemies and its activation range.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.3f, 1f, 0.6f);
        Gizmos.DrawWireCube(transform.position, new Vector3(3f, 3f, 0f));
        if (activationRange > 0f)
        {
            Gizmos.color = new Color(0.6f, 0.3f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, activationRange);
        }
    }
}
