using UnityEngine;

/// <summary>
/// The shield generator's force field: a square Radius tiles out from the generator in every direction, drawn
/// from the corner and edge pieces of "Shield Generator Effect.PNG" (animated together) and walled in by an
/// edge collider. Enemies can't walk or jump through it and enemy shots stop at it; the dino walks through
/// freely and its shots pass (see CrabBullet, CrabJumper). While the generator is being placed the field is
/// shown as a preview but doesn't block anything.
/// Set up by Tools > Space Dinosaurs > Build Buildings.
/// </summary>
[RequireComponent(typeof(Building))]
public class ShieldGenerator : MonoBehaviour
{
    [Tooltip("Tiles from the generator to the shield's outer edge.")]
    [Min(1)] public int radius = 2;
    [Tooltip("Middle of the shield, from the generator's ground point (the middle of its base).")]
    public Vector2 center = new Vector2(0f, 0.3125f);
    [Tooltip("Animation frames, in order: the corner piece (drawn as the bottom-right corner) and the edge piece (drawn as the right side).")]
    public Sprite[] cornerFrames, edgeFrames;
    public float framesPerSecond = 8f;
    [Tooltip("Draw order of the shield (above the characters, so it reads as a field over them).")]
    public int sortingOrder = 6;

    Building building;
    Transform field;
    EdgeCollider2D wall;
    SpriteRenderer[] corners, edges;
    float clock;
    bool ignoredDino;

    void Awake() => building = GetComponent<Building>();

    void Start() => Build();

    void OnDestroy() { if (field != null) Destroy(field.gameObject); }

    void OnDisable() { if (field != null) field.gameObject.SetActive(false); }
    void OnEnable() { if (field != null) field.gameObject.SetActive(true); }

    // The field is its own object (not a child): the generator's Sorting Group would otherwise sort it as part
    // of the building.
    void Build()
    {
        if (field != null) return;
        field = new GameObject(name + " Shield").transform;
        field.gameObject.AddComponent<ShieldField>().generator = this;

        float r = radius;
        int perSide = radius * 2 - 2; // edge pieces between the two corners
        corners = new SpriteRenderer[4];
        edges = new SpriteRenderer[4 * perSide];
        // Corners: the art is the bottom-right one with its pivot on the outer corner; turn it for the others.
        Vector2[] cornerAt = { new(r, -r), new(r, r), new(-r, r), new(-r, -r) };
        for (int i = 0; i < 4; i++) corners[i] = Piece("Corner", cornerAt[i], i * 90f);
        // Edges: the art is a right-side piece (pivot bottom-right), one tile long.
        int e = 0;
        for (int i = 0; i < perSide; i++)
        {
            edges[e++] = Piece("Right", new Vector2(r, -r + 1 + i), 0f);
            edges[e++] = Piece("Top", new Vector2(r - 1 - i, r), 90f);
            edges[e++] = Piece("Left", new Vector2(-r, r - 1 - i), 180f);
            edges[e++] = Piece("Bottom", new Vector2(-r + 1 + i, -r), 270f);
        }

        wall = field.gameObject.AddComponent<EdgeCollider2D>();
        wall.points = new[] { new Vector2(-r, -r), new Vector2(r, -r), new Vector2(r, r), new Vector2(-r, r), new Vector2(-r, -r) };
        wall.edgeRadius = 0.05f;
        LateUpdate();
    }

    SpriteRenderer Piece(string side, Vector2 at, float angle)
    {
        var go = new GameObject(side);
        go.transform.SetParent(field, false);
        go.transform.localPosition = at;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    void LateUpdate()
    {
        if (field == null) return;
        field.position = (Vector2)transform.position + center;

        bool placed = !building.Selected;
        wall.enabled = placed;
        if (placed && !ignoredDino) IgnoreDino();

        clock += Time.deltaTime;
        int f = framesPerSecond > 0f ? (int)(clock * framesPerSecond) : 0;
        if (cornerFrames != null && cornerFrames.Length > 0)
            foreach (var c in corners) c.sprite = cornerFrames[f % cornerFrames.Length];
        if (edgeFrames != null && edgeFrames.Length > 0)
            foreach (var s in edges) s.sprite = edgeFrames[f % edgeFrames.Length];
    }

    // The dino walks through its own shield.
    void IgnoreDino()
    {
        var dino = FindFirstObjectByType<DinoController>();
        if (dino == null) return;
        foreach (var c in dino.GetComponentsInChildren<Collider2D>(true)) Physics2D.IgnoreCollision(wall, c);
        ignoredDino = true;
    }

    /// <summary>True if the straight line from a to b crosses this shield's edge.</summary>
    public bool Crosses(Vector2 a, Vector2 b)
    {
        if (field == null || wall == null || !wall.enabled) return false;
        Vector2 c = field.position;
        float r = radius;
        bool InA(Vector2 p) => Mathf.Abs(p.x - c.x) < r && Mathf.Abs(p.y - c.y) < r;
        if (InA(a) != InA(b)) return true;
        // Both outside: it can still cut across the square.
        foreach (var hit in Physics2D.LinecastAll(a, b)) if (hit.collider == wall) return true;
        return false;
    }
}
