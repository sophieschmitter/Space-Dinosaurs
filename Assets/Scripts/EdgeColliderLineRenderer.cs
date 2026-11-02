using UnityEngine;

/// <summary>
/// Copies the points of every EdgeCollider2D on this object and its children
/// into LineRenderers so the walls become visible.
///
/// Usage:
/// 1. Add this script to your "Walls" object (the parent of your edge colliders).
/// 2. Assign a wall material (Sprite-Default works) and set color / width.
/// 3. Right-click the component header and choose "Build Lines"
///    (or tick Build On Start and it runs in Play mode).
///
/// Each EdgeCollider2D gets its own child object named "<name>_Line"
/// holding a LineRenderer, so re-running the build replaces old lines.
/// </summary>
[ExecuteAlways]
public class EdgeColliderLineRenderer : MonoBehaviour
{
    [Header("Appearance")]
    public Material lineMaterial;
    public Color lineColor = new Color(0.15f, 0.15f, 0.2f, 1f);
    public float lineWidth = 0.3f;

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 0;

    [Header("Options")]
    public bool buildOnStart = false;
    [Tooltip("Rounds the corners where segments meet.")]
    public int cornerVertices = 4;
    [Tooltip("Rounds the ends of each line.")]
    public int endCapVertices = 4;

    const string Suffix = "_Line";

    void Start()
    {
        if (Application.isPlaying && buildOnStart)
            BuildLines();
    }

    [ContextMenu("Build Lines")]
    public void BuildLines()
    {
        ClearLines();

        var colliders = GetComponentsInChildren<EdgeCollider2D>();
        foreach (var edge in colliders)
        {
            var points = edge.points;
            if (points == null || points.Length < 2) continue;

            // Child object that holds the LineRenderer, parented to the collider's object
            var go = new GameObject(edge.gameObject.name + Suffix);
            go.transform.SetParent(edge.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; // points are in the collider's local space
            lr.positionCount = points.Length;

            // Account for the collider's offset
            Vector2 offset = edge.offset;
            for (int i = 0; i < points.Length; i++)
                lr.SetPosition(i, new Vector3(points[i].x + offset.x, points[i].y + offset.y, 0f));

            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.numCornerVertices = cornerVertices;
            lr.numCapVertices = endCapVertices;
            lr.startColor = lineColor;
            lr.endColor = lineColor;
            lr.sortingLayerName = sortingLayerName;
            lr.sortingOrder = sortingOrder;
            lr.alignment = LineAlignment.TransformZ; // flat in 2D
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            lr.sharedMaterial = lineMaterial != null
                ? lineMaterial
                : new Material(Shader.Find("Sprites/Default"));
        }
    }

    [ContextMenu("Clear Lines")]
    public void ClearLines()
    {
        // Collect first, then destroy, so we don't modify while iterating
        var toRemove = new System.Collections.Generic.List<GameObject>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t != transform && t.name.EndsWith(Suffix) && t.GetComponent<LineRenderer>() != null)
                toRemove.Add(t.gameObject);
        }

        foreach (var go in toRemove)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}