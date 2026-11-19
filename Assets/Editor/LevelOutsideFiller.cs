using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Fills everything outside a level's walls with unbreakable meteorite, Margin tiles out past the level, as part
/// of the level's unbreakable meteorite (so it joins up with the outer walls). "Outside" is found by flooding in
/// from beyond the level until it meets any wall (unbreakable, breakable, ore, metal); if the flood ever reaches
/// the dino, the robot or a spawner (a gap in the outer wall), nothing is changed.
/// Tools > Space Dinosaurs > Fill Outside With Unbreakable Wall (works on the active scene).
/// </summary>
public static class LevelOutsideFiller
{
    [MenuItem("Tools/Space Dinosaurs/Fill Outside With Unbreakable Wall")]
    static void Menu() => Debug.Log(Fill(SceneManager.GetActiveScene()));

    public static string Fill(Scene scene, int margin = 40)
    {
        var clusters = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeteoriteCluster>(true)).ToList();
        var main = clusters.Where(c => c.unbreakable && c.walls != null).OrderByDescending(c => Count(c.walls)).FirstOrDefault();
        if (main == null) return $"{scene.name}: no unbreakable meteorite to extend";
        var grid = main.GetComponentInParent<Grid>();
        Vector3Int Cell(Vector3 w) => grid.WorldToCell(w);

        // Everything solid, by grid cell.
        var blocked = new HashSet<Vector3Int>();
        foreach (var c in clusters)
            foreach (var map in new[] { c.walls, c.ore })
                if (map != null) foreach (var p in Cells(map)) blocked.Add(Cell(map.GetCellCenterWorld(p)));
        foreach (var root in scene.GetRootGameObjects())
            foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
                if (map.GetComponentInParent<MeteoriteCluster>() == null && map.GetComponent<TilemapCollider2D>() != null)
                    foreach (var p in Cells(map)) blocked.Add(Cell(map.GetCellCenterWorld(p)));
        if (blocked.Count == 0) return $"{scene.name}: no walls";

        // Things that must stay inside.
        var inside = new List<Vector3Int>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var d in root.GetComponentsInChildren<DinoController>(true)) inside.Add(Cell(d.transform.position));
            foreach (var g in root.GetComponentsInChildren<EnemyGoal>(true)) inside.Add(Cell(g.transform.position));
            foreach (var s in root.GetComponentsInChildren<EnemySpawner>(true)) inside.Add(Cell(s.transform.position));
        }

        int x0 = blocked.Min(c => c.x) - margin, x1 = blocked.Max(c => c.x) + margin;
        int y0 = blocked.Min(c => c.y) - margin, y1 = blocked.Max(c => c.y) + margin;

        // Flood in from the edge of the big box.
        var outside = new HashSet<Vector3Int>();
        var queue = new Queue<Vector3Int>();
        void Try(int x, int y)
        {
            var c = new Vector3Int(x, y, 0);
            if (x < x0 || x > x1 || y < y0 || y > y1 || blocked.Contains(c) || !outside.Add(c)) return;
            queue.Enqueue(c);
        }
        for (int x = x0; x <= x1; x++) { Try(x, y0); Try(x, y1); }
        for (int y = y0; y <= y1; y++) { Try(x0, y); Try(x1, y); }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            Try(c.x + 1, c.y); Try(c.x - 1, c.y); Try(c.x, c.y + 1); Try(c.x, c.y - 1);
        }
        var leaked = inside.Where(outside.Contains).ToList();
        if (leaked.Count > 0) return $"{scene.name}: NOT filled: the outside reaches into the level at {string.Join(", ", leaked)} (a gap in the outer wall?)";

        var tile = Cells(main.walls).Select(p => main.walls.GetTile(p)).First(t => t != null);
        Undo.RegisterCompleteObjectUndo(main.walls, "Fill outside with unbreakable wall");
        var cells = outside.Select(c => main.walls.WorldToCell(grid.GetCellCenterWorld(c))).ToArray();
        main.walls.SetTiles(cells, Enumerable.Repeat(tile, cells.Length).ToArray());
        main.walls.CompressBounds();
        EditorSceneManager.MarkSceneDirty(scene);
        return $"{scene.name}: filled {cells.Length} tiles outside ({x1 - x0 + 1} x {y1 - y0 + 1} area, {margin} tiles past the walls)";
    }

    static int Count(Tilemap m) => Cells(m).Count();

    static IEnumerable<Vector3Int> Cells(Tilemap m)
    {
        m.CompressBounds();
        foreach (var p in m.cellBounds.allPositionsWithin) if (m.HasTile(p)) yield return p;
    }
}
