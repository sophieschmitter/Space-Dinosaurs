using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Erase single tiles from meteorites (and metal blocks, unbreakable meteorite and ore pieces) in the Scene view,
/// even after they've merged into one big meteorite.
///
/// Turn it on with Tools > Space Dinosaurs > Erase Meteor Tiles (Shift+E). Then click a tile to remove it, or
/// click and drag to remove a line of them. The tile under the mouse is outlined red. Where an ore piece sits on
/// rock, the first click takes the ore and the next one the rock. Esc (or Shift+E again) turns it off.
///
/// When you let go, a meteorite that's been cut into separate pieces becomes one meteorite per piece, so each
/// can be dragged on its own; one with no tiles left is removed. Each click or drag is one Cmd+Z.
/// </summary>
[InitializeOnLoad]
public static class MeteoriteEraser
{
    const string MenuPath = "Tools/Space Dinosaurs/Erase Meteor Tiles #e";
    const string MenuName = "Tools/Space Dinosaurs/Erase Meteor Tiles";

    static bool active;
    static bool stroking;
    static int undoGroup;
    static readonly HashSet<Vector3Int> erasedThisStroke = new();
    static readonly HashSet<MeteoriteCluster> touched = new();
    static readonly HashSet<Tilemap> recorded = new();

    static MeteoriteEraser() => SceneView.duringSceneGui += OnScene;

    [MenuItem(MenuPath, false, 30)]
    static void Toggle() => SetActive(!active);

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuName, active); return true; }

    public static bool Active => active;

    static Tool toolBefore = Tool.Move;

    public static void SetActive(bool on)
    {
        if (!on && stroking) EndStroke();
        if (on == active) { Menu.SetChecked(MenuName, on); return; }
        // The Hand (View) tool and the Move tool's handle would grab the clicks first, so no tool while erasing.
        if (on) { toolBefore = Tools.current == Tool.None ? Tool.Move : Tools.current; Tools.current = Tool.None; }
        else if (Tools.current == Tool.None) Tools.current = toolBefore;
        active = on;
        if (on) SceneView.lastActiveSceneView?.Focus();
        Menu.SetChecked(MenuName, on);
        SceneView.RepaintAll();
    }

    static void OnScene(SceneView view)
    {
        if (!active) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { SetActive(false); return; }
        // Picking another tool (Q/W/E/R/T or the toolbar) ends erasing.
        if (Tools.current != Tool.None) { active = false; Menu.SetChecked(MenuName, false); SceneView.RepaintAll(); return; }
        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);

        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id); // clicks erase instead of selecting
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { SetActive(false); e.Use(); return; }

        Vector3 world = HandleUtility.GUIPointToWorldRay(e.mousePosition).origin;
        var hit = TopTileAt(world);

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && !e.alt:
                GUIUtility.hotControl = id;
                BeginStroke();
                EraseAt(hit);
                e.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                EraseAt(hit);
                e.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                EndStroke();
                e.Use();
                break;
            case EventType.MouseMove:
                view.Repaint();
                break;
            case EventType.Repaint:
                if (hit.cluster != null) DrawCell(hit.map, hit.cell);
                DrawHint(view);
                break;
        }
    }

    // ---------- finding the tile under the mouse ----------

    struct Hit { public MeteoriteCluster cluster; public Tilemap map; public Vector3Int cell; }

    // Ore pieces first (they're drawn on top of the rock), then everything else.
    static Hit TopTileAt(Vector3 world)
    {
        Hit best = default;
        int bestRank = int.MinValue;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var c in root.GetComponentsInChildren<MeteoriteCluster>())
        {
            if (c.walls == null) continue;
            // The mouse ray starts at the camera's depth: flatten it onto the tilemap, or WorldToCell gives a cell
            // on another z layer that has no tiles.
            var onMap = new Vector3(world.x, world.y, c.walls.transform.position.z);
            var cell = c.walls.WorldToCell(onMap);
            cell.z = 0;
            if (!c.walls.HasTile(cell)) continue;
            int rank = IsOre(c) ? 2 : 1;
            var r = c.walls.GetComponent<TilemapRenderer>();
            if (r != null) rank = rank * 100000 + r.sortingOrder;
            if (rank > bestRank) { bestRank = rank; best = new Hit { cluster = c, map = c.walls, cell = cell }; }
        }
        return best;
    }

    static bool IsOre(MeteoriteCluster c)
    {
        foreach (var p in c.walls.cellBounds.allPositionsWithin)
        {
            var t = c.walls.GetTile(p);
            if (t != null) return OreVeins.For(t) != null;
        }
        return false;
    }

    // ---------- erasing ----------

    static void BeginStroke()
    {
        stroking = true;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Erase meteor tiles");
        undoGroup = Undo.GetCurrentGroup();
        erasedThisStroke.Clear();
        touched.Clear();
        recorded.Clear();
    }

    static void EraseAt(Hit hit)
    {
        if (!stroking || hit.cluster == null) return;
        var grid = hit.cluster.GetComponentInParent<Grid>();
        Vector3Int gridCell = grid != null ? grid.WorldToCell(hit.map.GetCellCenterWorld(hit.cell)) : hit.cell;
        if (!erasedThisStroke.Add(gridCell)) return; // one layer per cell per drag (ore, then rock on the next click)

        Record(hit.map);
        hit.map.SetTile(hit.cell, null);
        var ore = hit.cluster.ore; // ore painted on this meteorite's rock goes with it
        if (ore != null)
        {
            var oreCell = ore.WorldToCell(hit.map.GetCellCenterWorld(hit.cell));
            if (ore.HasTile(oreCell)) { Record(ore); ore.SetTile(oreCell, null); }
        }
        touched.Add(hit.cluster);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hit.cluster.gameObject.scene);
    }

    static void Record(Tilemap map)
    {
        if (recorded.Add(map)) Undo.RegisterCompleteObjectUndo(map, "Erase meteor tiles");
    }

    // Tidy up once the mouse is let go: remove empty meteorites, split ones that were cut in two.
    static void EndStroke()
    {
        stroking = false;
        foreach (var c in touched)
            if (c != null) Tidy(c);
        touched.Clear();
        recorded.Clear();
        Undo.CollapseUndoOperations(undoGroup);
    }

    static void Tidy(MeteoriteCluster c)
    {
        var grid = c.GetComponentInParent<Grid>();
        var walls = Cells(c.walls, grid);
        if (walls.Count == 0) { Undo.DestroyObjectImmediate(c.gameObject); return; }
        if (grid == null) return;
        var pieces = Pieces(walls);
        if (pieces.Count < 2) return;

        // The biggest piece stays in this meteorite; each other piece becomes a new one.
        pieces.Sort((a, b) => b.Count.CompareTo(a.Count));
        var ore = Cells(c.ore, grid);
        for (int i = 1; i < pieces.Count; i++)
        {
            var wallTiles = new Dictionary<Vector3Int, TileBase>();
            var oreTiles = new Dictionary<Vector3Int, TileBase>();
            foreach (var cell in pieces[i])
            {
                wallTiles[cell] = walls[cell];
                if (ore.TryGetValue(cell, out var o)) oreTiles[cell] = o;
            }
            Record(c.walls);
            foreach (var cell in pieces[i]) c.walls.SetTile(c.walls.WorldToCell(grid.GetCellCenterWorld(cell)), null);
            if (c.ore != null && oreTiles.Count > 0)
            {
                Record(c.ore);
                foreach (var cell in oreTiles.Keys) c.ore.SetTile(c.ore.WorldToCell(grid.GetCellCenterWorld(cell)), null);
            }
            var piece = MeteoritePlacement.NewCluster(c.walls, c.ore, c.name, wallTiles, oreTiles, c.unbreakable);
            piece.transform.SetSiblingIndex(c.transform.GetSiblingIndex() + 1);
            var r = c.walls.GetComponent<TilemapRenderer>();
            var pr = piece.GetComponent<MeteoriteCluster>().walls.GetComponent<TilemapRenderer>();
            if (r != null && pr != null) pr.sortingOrder = r.sortingOrder;
        }
        c.walls.CompressBounds();
    }

    // A meteorite's tiles by Grid cell.
    static Dictionary<Vector3Int, TileBase> Cells(Tilemap map, Grid grid)
    {
        var result = new Dictionary<Vector3Int, TileBase>();
        if (map == null) return result;
        map.CompressBounds();
        foreach (var p in map.cellBounds.allPositionsWithin)
        {
            var t = map.GetTile(p);
            if (t != null) result[grid != null ? grid.WorldToCell(map.GetCellCenterWorld(p)) : p] = t;
        }
        return result;
    }

    // Groups of tiles that touch (side by side or corner to corner, the same as merging uses).
    static List<List<Vector3Int>> Pieces(Dictionary<Vector3Int, TileBase> cells)
    {
        var pieces = new List<List<Vector3Int>>();
        var seen = new HashSet<Vector3Int>();
        foreach (var start in cells.Keys)
        {
            if (!seen.Add(start)) continue;
            var piece = new List<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                piece.Add(c);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var n = c + new Vector3Int(dx, dy, 0);
                    if (cells.ContainsKey(n) && seen.Add(n)) queue.Enqueue(n);
                }
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    // ---------- drawing ----------

    static void DrawCell(Tilemap map, Vector3Int cell)
    {
        Vector3 a = map.CellToWorld(cell), size = map.layoutGrid != null ? map.layoutGrid.cellSize : Vector3.one;
        var s = map.transform.lossyScale;
        size = new Vector3(size.x * s.x, size.y * s.y, 0f);
        var rect = new[] { a, a + new Vector3(size.x, 0), a + new Vector3(size.x, size.y), a + new Vector3(0, size.y) };
        Handles.DrawSolidRectangleWithOutline(rect, new Color(1f, 0.2f, 0.2f, 0.25f), new Color(1f, 0.25f, 0.25f, 1f));
    }

    static void DrawHint(SceneView view)
    {
        Handles.BeginGUI();
        var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, richText = true };
        GUI.Label(new Rect(10, 10, 360, 40),
            "<b>Erasing meteor tiles</b>\nClick or drag over tiles to remove them. Esc, Shift+E or any tool to stop.", style);
        Handles.EndGUI();
    }
}
