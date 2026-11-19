using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// When a sprite from the meteorite sheet (or the generated wall tiles) is dropped into a scene, it snaps to the
/// tile grid. By default it is then turned into a MeteoriteCluster: Wall Rule Tiles in its own little tilemap,
/// cell for cell, so it can be dragged around on its own and, in play, collides and breaks tile by tile like the
/// rest of the walls. Toggle with Tools > Space Dinosaurs > Meteorite Sprites Become Wall Tiles
/// (off = the sprite object only snaps).
/// Tools > Space Dinosaurs > Split Walls Into Meteorites turns the tiles already painted on the scene's Walls
/// tilemap into one MeteoriteCluster per connected meteorite (taking the ore on them along).
/// Metal sprites ("Assets/Sprites/Metal Tilemap.png") and unbreakable meteorite sprites ("Assets/Sprites/Meteorite
/// Unbreakable Tilemap.png") work the same way, as blocks on the scene's solid tilemap (SolidTilemap: can't be
/// drilled). Each kind only merges with its own kind.
/// Meteorites snap to whole tiles when moved, and a meteorite dropped touching another one (side by side or
/// corner to corner) merges with it into one meteorite, so the edges join up like one piece of rock.
/// Everything is undoable with Cmd+Z.
/// </summary>
[InitializeOnLoad]
public static class MeteoritePlacement
{
    const string TilesFolder = "Assets/Tiles/";
    const string RuleTilePath = "Assets/Tiles/Generated/WallRuleTile.asset";
    const string MetalSheet = "Assets/Sprites/Metal Tilemap.png";
    const string MetalRuleTilePath = "Assets/Tiles/Generated/Metal/MetalRuleTile.asset";
    const string UnbreakableSheet = "Assets/Sprites/Meteorite Unbreakable Tilemap.png";
    const string UnbreakableRuleTilePath = "Assets/Tiles/Generated/Unbreakable/UnbreakableRuleTile.asset";
    const string ConvertPref = "SpaceDinosaurs.MeteoriteToTiles";
    const string MenuPath = "Tools/Space Dinosaurs/Meteorite Sprites Become Wall Tiles";

    static bool Convert
    {
        get => EditorPrefs.GetBool(ConvertPref, true);
        set => EditorPrefs.SetBool(ConvertPref, value);
    }

    [MenuItem(MenuPath)] static void ToggleConvert() => Convert = !Convert;
    [MenuItem(MenuPath, true)] static bool ToggleConvertValidate() { Menu.SetChecked(MenuPath, Convert); return true; }

    [MenuItem("Tools/Space Dinosaurs/Snap Selected Meteorite Sprites")]
    static void ProcessSelection()
    {
        foreach (var go in Selection.gameObjects) Process(go);
    }

    static MeteoritePlacement()
    {
        ObjectChangeEvents.changesPublished += OnChanges;
        EditorApplication.update += TickMerges;
    }

    static void OnChanges(ref ObjectChangeEventStream stream)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var ids = new List<int>();
        for (int i = 0; i < stream.length; i++)
        {
            switch (stream.GetEventType(i))
            {
                case ObjectChangeKind.CreateGameObjectHierarchy:
                    stream.GetCreateGameObjectHierarchyEvent(i, out var created);
                    ids.Add(created.instanceId);
                    break;
                case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var changed);
                    ids.Add(changed.instanceId);
                    break;
            }
        }
        if (ids.Count == 0) return;
        // Wait a tick so a drag-and-drop or move has fully finished before we touch the object.
        EditorApplication.delayCall += () =>
        {
            foreach (int id in ids)
            {
                var obj = EditorUtility.InstanceIDToObject(id);
                var go = obj as GameObject ?? (obj as Component)?.gameObject;
                if (go != null) Process(go);
            }
        };
    }

    static void Process(GameObject go)
    {
        if (go == null || go.hideFlags != HideFlags.None || !go.scene.IsValid()) return;
        if (EditorUtility.IsPersistent(go) || PrefabUtility.IsPartOfPrefabAsset(go)) return;
        if (go.GetComponent<MeteoriteCluster>() != null) { SnapCluster(go); QueueMerge(go); return; }
        // One of a meteorite's own tilemaps was dragged (picked from the Hierarchy): move the whole meteorite
        // instead, so its tiles stay on the grid and its walls and ore stay together.
        var owner = go.transform.parent != null ? go.transform.parent.GetComponent<MeteoriteCluster>() : null;
        if (owner != null && go.GetComponent<Tilemap>() != null)
        {
            Vector3 offset = go.transform.localPosition;
            if (offset.sqrMagnitude < 1e-8f) return;
            Undo.RecordObject(go.transform, "Move meteorite");
            Undo.RecordObject(owner.transform, "Move meteorite");
            owner.transform.position = go.transform.position; // the tilemap's origin is the meteorite's origin
            go.transform.localPosition = Vector3.zero;
            SnapCluster(owner.gameObject);
            QueueMerge(owner.gameObject);
            Selection.activeGameObject = owner.gameObject;
            return;
        }
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        string texPath = AssetDatabase.GetAssetPath(sr.sprite.texture);
        // Metal and unbreakable meteorite both go on the solid (can't be drilled) tilemap.
        bool unbreakableRock = texPath == UnbreakableSheet;
        bool metal = texPath == MetalSheet || unbreakableRock;
        if (!metal && !texPath.StartsWith(TilesFolder)) return;

        var cells = SpriteCells(sr.sprite, texPath);
        if (cells.Count == 0) return;

        Tilemap walls = metal ? (Convert ? EnsureMetal(go.scene) : FindMetal(go.scene)) : FindWalls(go.scene);
        Grid grid = walls != null ? walls.layoutGrid as Grid : Object.FindFirstObjectByType<Grid>();
        Vector2 cellSize = grid != null ? (Vector2)grid.cellSize : Vector2.one;
        Vector3 gridOrigin = grid != null ? grid.transform.position : Vector3.zero;

        // World centre of the sprite's first wall cell, then the nearest real grid cell to it.
        Vector2 local0 = cells[0].localCentre;
        Vector3 world0 = go.transform.TransformPoint(local0);
        Vector3Int anchor = new Vector3Int(
            Mathf.RoundToInt((world0.x - gridOrigin.x) / cellSize.x - 0.5f),
            Mathf.RoundToInt((world0.y - gridOrigin.y) / cellSize.y - 0.5f), 0);

        var ruleTile = AssetDatabase.LoadAssetAtPath<TileBase>(unbreakableRock ? UnbreakableRuleTilePath : metal ? MetalRuleTilePath : RuleTilePath);
        if (Convert && walls != null && ruleTile != null)
        {
            // A meteorite of its own (movable), not tiles on the shared Walls tilemap.
            var tiles = new Dictionary<Vector3Int, TileBase>();
            foreach (var c in cells) tiles[anchor + c.offset] = ruleTile;
            string kind = unbreakableRock ? "Unbreakable Meteorite" : metal ? "Metal Block" : "Meteorite";
            var cluster = NewCluster(walls, metal ? null : FindOre(go.scene), kind, tiles, null, metal);
            Selection.activeGameObject = cluster;
            QueueMerge(cluster);
            Undo.DestroyObjectImmediate(go);
            return;
        }

        // Snap only: move the object so its first wall cell sits exactly on the grid cell.
        Vector3 target = gridOrigin + new Vector3((anchor.x + 0.5f) * cellSize.x, (anchor.y + 0.5f) * cellSize.y, 0);
        Vector3 delta = target - world0;
        if (delta.sqrMagnitude < 1e-8f) return; // already snapped (also stops the change event looping)
        Undo.RecordObject(go.transform, "Snap meteorite to grid");
        go.transform.position += new Vector3(delta.x, delta.y, 0);
    }

    // ---------- meteorites you can move on their own ----------

    [MenuItem("Tools/Space Dinosaurs/Split Walls Into Meteorites")]
    static void SplitWalls()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var walls = FindWalls(scene);
        if (walls == null) { Debug.LogWarning("Meteorites: no Walls tilemap in the active scene."); return; }
        var ore = FindOre(scene);
        Undo.SetCurrentGroupName("Split walls into meteorites");
        int group = Undo.GetCurrentGroup();
        Undo.RegisterCompleteObjectUndo(walls, "Split walls");
        if (ore != null) Undo.RegisterCompleteObjectUndo(ore, "Split walls");

        // Connected groups of wall tiles (touching sideways or diagonally, like the Rule Tile joins them).
        var left = new HashSet<Vector3Int>();
        foreach (var c in walls.cellBounds.allPositionsWithin) if (walls.HasTile(c)) left.Add(c);
        int made = 0;
        while (left.Count > 0)
        {
            var start = default(Vector3Int);
            foreach (var c in left) { start = c; break; }
            var group1 = new List<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start); left.Remove(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                group1.Add(c);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var n = c + new Vector3Int(dx, dy, 0);
                    if (left.Remove(n)) queue.Enqueue(n);
                }
            }
            var wallTiles = new Dictionary<Vector3Int, TileBase>();
            var oreTiles = new Dictionary<Vector3Int, TileBase>();
            foreach (var c in group1)
            {
                wallTiles[c] = walls.GetTile(c);
                if (ore != null && ore.GetTile(c) is TileBase o) oreTiles[c] = o;
            }
            NewCluster(walls, ore, $"Meteorite {++made}", wallTiles, oreTiles);
            foreach (var c in group1)
            {
                walls.SetTile(c, null);
                if (ore != null && oreTiles.ContainsKey(c)) ore.SetTile(c, null);
            }
        }
        Undo.CollapseUndoOperations(group);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Meteorites: split the walls into {made} meteorites. Drag them anywhere; they join up when the level starts.");
    }

    /// <summary>A movable meteorite under the same Grid as the Walls tilemap, holding these tiles at these cells.</summary>
    static GameObject NewCluster(Tilemap walls, Tilemap ore, string name, Dictionary<Vector3Int, TileBase> wallTiles, Dictionary<Vector3Int, TileBase> oreTiles, bool metal = false)
    {
        // Its origin is the corner of its bottom-left cell, so moving it by whole tiles keeps it on the grid.
        var min = new Vector3Int(int.MaxValue, int.MaxValue, 0);
        foreach (var c in wallTiles.Keys) { min.x = Mathf.Min(min.x, c.x); min.y = Mathf.Min(min.y, c.y); }
        var gridT = walls.layoutGrid != null ? walls.layoutGrid.transform : walls.transform.parent;
        var go = new GameObject(name);
        go.transform.SetParent(gridT, false);
        go.transform.position = walls.CellToWorld(min);
        var cluster = go.AddComponent<MeteoriteCluster>();
        cluster.unbreakable = metal;
        cluster.walls = ChildMap(go.transform, metal ? "Solid Walls" : "Meteorite Walls", walls, wallTiles, min);
        if (ore != null && oreTiles != null && oreTiles.Count > 0) cluster.ore = ChildMap(go.transform, "Meteorite Ore", ore, oreTiles, min);
        // Registered once it's complete, so undo and redo bring back the whole meteorite (tilemaps and tiles),
        // not an empty shell.
        Undo.RegisterCreatedObjectUndo(go, "Make meteorite");
        return go;
    }

    static Tilemap ChildMap(Transform parent, string name, Tilemap like, Dictionary<Vector3Int, TileBase> tiles, Vector3Int min)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var map = go.AddComponent<Tilemap>();
        map.tileAnchor = like.tileAnchor;
        var r = go.AddComponent<TilemapRenderer>();
        var likeR = like.GetComponent<TilemapRenderer>();
        if (likeR != null)
        {
            r.sortingLayerID = likeR.sortingLayerID;
            r.sortingOrder = likeR.sortingOrder;
            r.sharedMaterial = likeR.sharedMaterial;
            r.mode = likeR.mode;
        }
        foreach (var kv in tiles) map.SetTile(kv.Key - min, kv.Value);
        return map;
    }

    // Keeps a meteorite on whole tiles when it's dragged.
    static void SnapCluster(GameObject go)
    {
        var grid = go.GetComponentInParent<Grid>();
        Vector2 size = grid != null ? (Vector2)grid.cellSize : Vector2.one;
        Vector3 origin = grid != null ? grid.transform.position : Vector3.zero;
        Vector3 p = go.transform.position - origin;
        var snapped = origin + new Vector3(Mathf.Round(p.x / size.x) * size.x, Mathf.Round(p.y / size.y) * size.y, go.transform.position.z);
        if ((snapped - go.transform.position).sqrMagnitude < 1e-8f) return; // already on the grid (stops the change event looping)
        Undo.RecordObject(go.transform, "Snap meteorite to grid");
        go.transform.position = snapped;
    }

    // ---------- merging meteorites that touch ----------

    static readonly HashSet<int> pendingMerges = new();

    static void QueueMerge(GameObject go) { if (go != null) pendingMerges.Add(go.GetInstanceID()); }

    // Merge once the mouse is let go, not in the middle of a drag (the dragged object would vanish under it).
    static void TickMerges()
    {
        if (pendingMerges.Count == 0 || GUIUtility.hotControl != 0 || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var ids = new List<int>(pendingMerges);
        pendingMerges.Clear();
        foreach (int id in ids)
            if (EditorUtility.InstanceIDToObject(id) is GameObject go && go != null && go.GetComponent<MeteoriteCluster>() is MeteoriteCluster c)
                MergeTouching(c);
    }

    /// <summary>Folds every meteorite touching this one (including corner to corner) into it.</summary>
    static void MergeTouching(MeteoriteCluster keep)
    {
        var grid = keep.GetComponentInParent<Grid>();
        if (grid == null || keep.walls == null) return;
        bool merged = false;
        for (int guard = 0; guard < 64; guard++)
        {
            var mine = Cells(keep.walls, grid);
            MeteoriteCluster other = null;
            foreach (var root in keep.gameObject.scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<MeteoriteCluster>(true))
            {
                // Only the same kind merges (meteorite, metal, unbreakable meteorite): their tiles wouldn't join up anyway.
                if (c == keep || c.walls == null || c.unbreakable != keep.unbreakable || Kind(c) != Kind(keep) || c.GetComponentInParent<Grid>() != grid) continue;
                if (Touches(mine, Cells(c.walls, grid))) { other = c; break; }
            }
            if (other == null) break;
            Absorb(keep, other, grid);
            merged = true;
        }
        if (merged)
        {
            Selection.activeGameObject = keep.gameObject;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(keep.gameObject.scene);
        }
    }

    // Which wall tile a meteorite is made of (its first one).
    static TileBase Kind(MeteoriteCluster c)
    {
        if (c.walls == null) return null;
        foreach (var p in c.walls.cellBounds.allPositionsWithin) { var t = c.walls.GetTile(p); if (t != null) return t; }
        return null;
    }

    // A meteorite's tiles by Grid cell.
    static Dictionary<Vector3Int, TileBase> Cells(Tilemap map, Grid grid)
    {
        var result = new Dictionary<Vector3Int, TileBase>();
        if (map == null) return result;
        foreach (var c in map.cellBounds.allPositionsWithin)
        {
            var t = map.GetTile(c);
            if (t != null) result[grid.WorldToCell(map.GetCellCenterWorld(c))] = t;
        }
        return result;
    }

    static bool Touches(Dictionary<Vector3Int, TileBase> a, Dictionary<Vector3Int, TileBase> b)
    {
        foreach (var c in a.Keys)
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (b.ContainsKey(c + new Vector3Int(dx, dy, 0))) return true;
        return false;
    }

    // Moves other's walls and ore into keep (keep's tiles win where both have one) and deletes other.
    static void Absorb(MeteoriteCluster keep, MeteoriteCluster other, Grid grid)
    {
        var walls = Cells(keep.walls, grid);
        foreach (var kv in Cells(other.walls, grid)) if (!walls.ContainsKey(kv.Key)) walls[kv.Key] = kv.Value;
        var ore = Cells(keep.ore, grid);
        foreach (var kv in Cells(other.ore, grid)) if (!ore.ContainsKey(kv.Key)) ore[kv.Key] = kv.Value;

        var min = new Vector3Int(int.MaxValue, int.MaxValue, 0);
        foreach (var c in walls.Keys) { min.x = Mathf.Min(min.x, c.x); min.y = Mathf.Min(min.y, c.y); }

        Undo.RecordObject(keep.transform, "Merge meteorites");
        Undo.RegisterCompleteObjectUndo(keep.walls, "Merge meteorites");
        if (keep.ore != null) Undo.RegisterCompleteObjectUndo(keep.ore, "Merge meteorites");
        Undo.RecordObject(keep, "Merge meteorites");
        keep.transform.position = grid.CellToWorld(min);
        keep.walls.ClearAllTiles();
        foreach (var kv in walls) keep.walls.SetTile(kv.Key - min, kv.Value);
        if (ore.Count > 0)
        {
            if (keep.ore == null)
            {
                var likeOre = other.ore != null ? other.ore : FindOre(keep.gameObject.scene);
                if (likeOre != null)
                {
                    var shifted = new Dictionary<Vector3Int, TileBase>();
                    foreach (var kv in ore) shifted[kv.Key] = kv.Value;
                    keep.ore = ChildMap(keep.transform, "Meteorite Ore", likeOre, shifted, min);
                    Undo.RegisterCreatedObjectUndo(keep.ore.gameObject, "Merge meteorites");
                }
            }
            else
            {
                keep.ore.ClearAllTiles();
                foreach (var kv in ore) keep.ore.SetTile(kv.Key - min, kv.Value);
            }
        }
        Undo.DestroyObjectImmediate(other.gameObject);
    }

    // ---------- metal walls ----------

    static Tilemap FindMetal(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var s = root.GetComponentInChildren<SolidTilemap>(true);
            if (s != null) return s.GetComponent<Tilemap>();
        }
        return null;
    }

    /// <summary>The scene's solid tilemap (metal and unbreakable meteorite; can't be drilled), made under its Grid
    /// (or a new one) if it has none.</summary>
    static Tilemap EnsureMetal(UnityEngine.SceneManagement.Scene scene)
    {
        var found = FindMetal(scene);
        if (found != null) return found;
        Grid grid = null;
        foreach (var root in scene.GetRootGameObjects()) { grid = root.GetComponentInChildren<Grid>(true); if (grid != null) break; }
        if (grid == null)
        {
            var g = new GameObject("Grid");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, scene);
            grid = g.AddComponent<Grid>();
            Undo.RegisterCreatedObjectUndo(g, "Make metal tilemap");
        }
        var go = new GameObject("Solid Walls");
        go.transform.SetParent(grid.transform, false);
        var map = go.AddComponent<Tilemap>();
        var r = go.AddComponent<TilemapRenderer>();
        var walls = FindWalls(scene);
        var wr = walls != null ? walls.GetComponent<TilemapRenderer>() : null;
        if (wr != null) { r.sortingLayerID = wr.sortingLayerID; r.sharedMaterial = wr.sharedMaterial; r.mode = wr.mode; }
        r.sortingOrder = 1; // like the meteorite walls: over the floor, under the shadows and characters
        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        var tc = go.AddComponent<TilemapCollider2D>();
        tc.compositeOperation = Collider2D.CompositeOperation.Merge;
        go.AddComponent<CompositeCollider2D>();
        go.AddComponent<SolidTilemap>();
        Undo.RegisterCreatedObjectUndo(go, "Make metal tilemap");
        return map;
    }

    /// <summary>Turns the placeholder walls (plain Square sprites with box colliders) in the active scene into
    /// metal blocks on the tile grid, one block per connected group, and deletes the placeholders.</summary>
    [MenuItem("Tools/Space Dinosaurs/Replace Temporary Walls With Metal")]
    static void ReplaceTemporaryWalls() => ReplaceTemporaryWalls(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

    public static int ReplaceTemporaryWalls(UnityEngine.SceneManagement.Scene scene)
    {
        var tile = AssetDatabase.LoadAssetAtPath<TileBase>(MetalRuleTilePath);
        if (tile == null) { Debug.LogError("Metal walls: build them first (Tools > Space Dinosaurs > Build Metal Wall Tiles)."); return 0; }
        var placeholders = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (sr.sprite != null && sr.sprite.name == "Square" && sr.GetComponent<BoxCollider2D>() != null && sr.GetComponent<MonoBehaviour>() == null)
                placeholders.Add(sr.gameObject);
        if (placeholders.Count == 0) { Debug.Log("Metal walls: no temporary walls in " + scene.name); return 0; }

        Undo.SetCurrentGroupName("Replace temporary walls with metal");
        int group = Undo.GetCurrentGroup();
        var metal = EnsureMetal(scene);
        var grid = metal.layoutGrid as Grid;
        // Every grid cell whose middle is inside a placeholder (left/bottom edges count, right/top don't).
        var cells = new HashSet<Vector3Int>();
        foreach (var p in placeholders)
        {
            var b = p.GetComponent<SpriteRenderer>().bounds;
            var lo = grid.WorldToCell(b.min - new Vector3(0.5f, 0.5f, 0)); var hi = grid.WorldToCell(b.max + new Vector3(0.5f, 0.5f, 0));
            for (int x = lo.x; x <= hi.x; x++)
            for (int y = lo.y; y <= hi.y; y++)
            {
                var c = new Vector3Int(x, y, 0);
                Vector3 mid = grid.GetCellCenterWorld(c);
                if (mid.x >= b.min.x - 0.02f && mid.x < b.max.x - 0.02f && mid.y >= b.min.y - 0.02f && mid.y < b.max.y - 0.02f) cells.Add(c);
            }
        }
        // One movable block per connected group.
        int blocks = 0;
        var left = new HashSet<Vector3Int>(cells);
        while (left.Count > 0)
        {
            var start = default(Vector3Int);
            foreach (var c in left) { start = c; break; }
            var group1 = new Dictionary<Vector3Int, TileBase>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start); left.Remove(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                group1[c] = tile;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var n = c + new Vector3Int(dx, dy, 0);
                    if (left.Remove(n)) queue.Enqueue(n);
                }
            }
            NewCluster(metal, null, $"Metal Block {++blocks}", group1, null, true);
        }
        // Remove the placeholders, then any group objects they leave empty.
        var parents = new HashSet<Transform>();
        foreach (var p in placeholders) { if (p.transform.parent != null) parents.Add(p.transform.parent); Undo.DestroyObjectImmediate(p); }
        foreach (var t in parents) if (t != null && t.childCount == 0 && t.GetComponents<Component>().Length == 1) Undo.DestroyObjectImmediate(t.gameObject);
        foreach (var t in parents) if (t != null && t.parent != null && t.parent.childCount == 0) Undo.DestroyObjectImmediate(t.parent.gameObject);
        Undo.CollapseUndoOperations(group);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Metal walls: replaced {placeholders.Count} temporary walls in {scene.name} with {cells.Count} metal tiles in {blocks} blocks.");
        return placeholders.Count;
    }

    static Tilemap FindOre(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var o = root.GetComponentInChildren<OreLayer>(true);
            if (o != null) return o.GetComponent<Tilemap>();
        }
        return null;
    }

    static Tilemap FindWalls(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var d = root.GetComponentInChildren<DestructibleTilemap>(true);
            if (d != null) return d.GetComponent<Tilemap>();
            foreach (var tm in root.GetComponentsInChildren<Tilemap>(true))
                if (tm.name == "Walls") return tm;
        }
        return null;
    }

    // ---------- which grid cells a sprite covers ----------

    struct Cell { public Vector3Int offset; public Vector2 localCentre; }

    static readonly Dictionary<string, (Color32[] px, int w, int h, long stamp)> PixelCache = new();

    static List<Cell> SpriteCells(Sprite sprite, string texPath)
    {
        var result = new List<Cell>();
        var (px, w, h) = Pixels(texPath);
        if (px == null) return result;

        float ppu = sprite.pixelsPerUnit;          // 1 world unit = 1 tile, so a tile is ppu texture pixels
        int tilePx = Mathf.RoundToInt(ppu);
        Rect r = sprite.rect;
        Vector2 pivotPx = r.position + sprite.pivot; // pivot in texture pixels

        // Sheet cells that overlap the sprite rect by more than half a tile.
        int cx0 = Mathf.RoundToInt(r.xMin / tilePx), cx1 = Mathf.RoundToInt(r.xMax / tilePx);
        int cy0 = Mathf.RoundToInt(r.yMin / tilePx), cy1 = Mathf.RoundToInt(r.yMax / tilePx);
        if (cx1 <= cx0) cx1 = cx0 + 1;
        if (cy1 <= cy0) cy1 = cy0 + 1;

        for (int cy = cy0; cy < cy1; cy++)
        for (int cx = cx0; cx < cx1; cx++)
        {
            if (!Filled(px, w, h, cx * tilePx, cy * tilePx, tilePx)) continue;
            Vector2 centrePx = new Vector2((cx + 0.5f) * tilePx, (cy + 0.5f) * tilePx);
            result.Add(new Cell
            {
                offset = new Vector3Int(cx - cx0, cy - cy0, 0),
                localCentre = (centrePx - pivotPx) / ppu,
            });
        }
        // Offsets relative to the first cell found.
        if (result.Count > 0)
        {
            var o = result[0].offset;
            for (int i = 0; i < result.Count; i++) { var c = result[i]; c.offset -= o; result[i] = c; }
        }
        return result;
    }

    static (Color32[] px, int w, int h) Pixels(string texPath)
    {
        long stamp = File.Exists(texPath) ? File.GetLastWriteTimeUtc(texPath).Ticks : 0;
        if (PixelCache.TryGetValue(texPath, out var c) && c.stamp == stamp) return (c.px, c.w, c.h);
        if (stamp == 0) return (null, 0, 0);
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(texPath));
        var entry = (tex.GetPixels32(), tex.width, tex.height, stamp);
        Object.DestroyImmediate(tex);
        PixelCache[texPath] = entry;
        return (entry.Item1, entry.Item2, entry.Item3);
    }

    // Texture rows run bottom-up, same as sprite rects.
    static bool Filled(Color32[] px, int w, int h, int x0, int y0, int size)
    {
        int solid = 0, total = 0, step = Mathf.Max(1, size / 16);
        for (int y = y0; y < y0 + size; y += step)
        for (int x = x0; x < x0 + size; x += step)
        {
            total++;
            if (x >= 0 && y >= 0 && x < w && y < h && px[y * w + x].a > 128) solid++;
        }
        return total > 0 && solid >= total / 2;
    }
}
