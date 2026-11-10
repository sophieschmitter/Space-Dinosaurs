using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Finds a route between two points on the tile grid, going around wall tiles and other solid blocks
/// (enemy spawners). Moves only up/down/left/right, so routes never cut wall corners.
/// The map is open beyond the walls, so the search reaches a few tiles past the painted area.
/// </summary>
public static class TilePathfinder
{
    const int MaxNodes = 6000;  // safety limit per search
    const int Margin = 8;       // tiles searched beyond the walls' painted area

    static readonly Vector3Int[] Steps = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };

    /// <summary>
    /// Returns the cell centres to walk through from start to goal (not including the start cell),
    /// or null if there's no route. Cells holding a wall tile, or listed in extraBlocked, are avoided.
    /// </summary>
    public static List<Vector2> FindPath(Tilemap walls, Vector2 start, Vector2 goal, ICollection<Vector3Int> extraBlocked = null)
    {
        if (walls == null) return null;
        Vector3Int from = walls.WorldToCell(start), to = walls.WorldToCell(goal);
        if (from == to) return new List<Vector2> { goal };

        var b = walls.cellBounds;
        int minX = Mathf.Min(b.xMin, from.x, to.x) - Margin, maxX = Mathf.Max(b.xMax, from.x, to.x) + Margin;
        int minY = Mathf.Min(b.yMin, from.y, to.y) - Margin, maxY = Mathf.Max(b.yMax, from.y, to.y) + Margin;

        bool Blocked(Vector3Int c) =>
            c.x < minX || c.x > maxX || c.y < minY || c.y > maxY ||
            (c != to && (walls.HasTile(c) || (extraBlocked != null && extraBlocked.Contains(c))));

        var open = new SortedSet<(int f, int order, Vector3Int cell)>(Comparer<(int, int, Vector3Int)>.Create(
            (p, q) => p.Item1 != q.Item1 ? p.Item1.CompareTo(q.Item1) : p.Item2.CompareTo(q.Item2)));
        var cost = new Dictionary<Vector3Int, int> { [from] = 0 };
        var cameFrom = new Dictionary<Vector3Int, Vector3Int>();
        int order = 0;
        open.Add((Heuristic(from, to), order++, from));

        while (open.Count > 0 && cost.Count < MaxNodes)
        {
            var current = open.Min.cell;
            open.Remove(open.Min);
            if (current == to) return Build(walls, cameFrom, from, to, goal);

            foreach (var step in Steps)
            {
                var next = current + step;
                if (Blocked(next)) continue;
                int g = cost[current] + 1;
                if (cost.TryGetValue(next, out int old) && old <= g) continue;
                cost[next] = g;
                cameFrom[next] = current;
                open.Add((g + Heuristic(next, to), order++, next));
            }
        }
        return null;
    }

    static int Heuristic(Vector3Int a, Vector3Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    static List<Vector2> Build(Tilemap walls, Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int from, Vector3Int to, Vector2 goal)
    {
        var cells = new List<Vector3Int>();
        for (var c = to; c != from; c = cameFrom[c]) cells.Add(c);
        cells.Reverse();
        var points = new List<Vector2>(cells.Count);
        foreach (var c in cells) points.Add(walls.GetCellCenterWorld(c));
        points[^1] = goal; // finish on the target itself, not just its cell centre
        return points;
    }
}
