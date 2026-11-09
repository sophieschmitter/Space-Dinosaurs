using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// When a sprite from the meteorite sheet (or the generated wall tiles) is dropped into a scene, it snaps to the
/// tile grid. By default it is then turned into Wall Rule Tiles on the scene's "Walls" tilemap, cell for cell,
/// so it collides and can be broken tile by tile. Toggle with
/// Tools > Space Dinosaurs > Meteorite Sprites Become Wall Tiles (off = the sprite object only snaps).
/// Everything is undoable with Cmd+Z.
/// </summary>
[InitializeOnLoad]
public static class MeteoritePlacement
{
    const string TilesFolder = "Assets/Tiles/";
    const string RuleTilePath = "Assets/Tiles/Generated/WallRuleTile.asset";
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
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        string texPath = AssetDatabase.GetAssetPath(sr.sprite.texture);
        if (!texPath.StartsWith(TilesFolder)) return;

        var cells = SpriteCells(sr.sprite, texPath);
        if (cells.Count == 0) return;

        Tilemap walls = FindWalls(go.scene);
        Grid grid = walls != null ? walls.layoutGrid as Grid : Object.FindFirstObjectByType<Grid>();
        Vector2 cellSize = grid != null ? (Vector2)grid.cellSize : Vector2.one;
        Vector3 gridOrigin = grid != null ? grid.transform.position : Vector3.zero;

        // World centre of the sprite's first wall cell, then the nearest real grid cell to it.
        Vector2 local0 = cells[0].localCentre;
        Vector3 world0 = go.transform.TransformPoint(local0);
        Vector3Int anchor = new Vector3Int(
            Mathf.RoundToInt((world0.x - gridOrigin.x) / cellSize.x - 0.5f),
            Mathf.RoundToInt((world0.y - gridOrigin.y) / cellSize.y - 0.5f), 0);

        var ruleTile = AssetDatabase.LoadAssetAtPath<TileBase>(RuleTilePath);
        if (Convert && walls != null && ruleTile != null)
        {
            Undo.RegisterCompleteObjectUndo(walls, "Place meteorite tiles");
            foreach (var c in cells) walls.SetTile(anchor + c.offset, ruleTile);
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
