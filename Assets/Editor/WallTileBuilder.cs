using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Turns the wall sample sheet in Assets/Tiles into a Rule Tile that can draw ANY wall layout.
///
/// A wall tile's look depends on its 8 neighbours. Once diagonals that can't matter are ignored,
/// there are exactly 47 shapes. The tool:
///   1. reads each 16x16 cell of the sheet and works out its shape from its own art
///      (which edges and inner corners have the bright rim), so loose pieces like the cracked squares work too;
///   2. uses the real tiles for every shape the sheet shows (several samples = random variants);
///   3. fills any missing shape by rotating/mirroring a real tile, or, failing that,
///      by stitching together the right quarter-tiles from real tiles;
///   4. writes a 1x atlas (Assets/Tiles/Generated/WallTiles.png), a Rule Tile and a "Walls" Tile Palette.
/// Run from: Tools > Space Dinosaurs > Build Wall Tiles. Safe to re-run after editing the sheet.
/// </summary>
public static class WallTileBuilder
{
    const string SourceFolder = "Assets/Tiles";
    const string OutFolder = "Assets/Tiles/Generated";
    const string AtlasPath = OutFolder + "/WallTiles.png";
    const string RuleTilePath = OutFolder + "/WallRuleTile.asset";
    const string PaletteName = "Walls";
    const int Art = 16;              // art pixels per tile
    const int AtlasColumns = 8;
    const int FillMaxRed = 100;      // wall-body colours (navy/dark purple) have red below this; rim colours above
    const float Coverage = 0.5f;

    // Direction bits, clockwise from north.
    const int N = 1, NE = 2, E = 4, SE = 8, S = 16, SW = 32, W = 64, NW = 128;
    static readonly (int bit, Vector3Int pos)[] Dirs =
    {
        (N, new Vector3Int(0, 1, 0)), (NE, new Vector3Int(1, 1, 0)), (E, new Vector3Int(1, 0, 0)),
        (SE, new Vector3Int(1, -1, 0)), (S, new Vector3Int(0, -1, 0)), (SW, new Vector3Int(-1, -1, 0)),
        (W, new Vector3Int(-1, 0, 0)), (NW, new Vector3Int(-1, 1, 0)),
    };

    // A tile is Art x Art colours, row 0 = TOP.
    class TileArt
    {
        public Color32[] px = new Color32[Art * Art];
        public Color32 this[int x, int y] { get => px[y * Art + x]; set => px[y * Art + x] = value; }
    }

    [MenuItem("Tools/Space Dinosaurs/Build Wall Tiles")]
    public static void Build()
    {
        string sheetPath = FindSheet();
        if (sheetPath == null) { Debug.LogError($"WallTileBuilder: put the wall sheet PNG in {SourceFolder}."); return; }

        var cells = ReadSheet(sheetPath, out string report);

        // Group real tiles by shape.
        var byShape = new Dictionary<int, List<TileArt>>();
        foreach (var (shape, art) in cells)
        {
            if (!byShape.TryGetValue(shape, out var list)) byShape[shape] = list = new List<TileArt>();
            list.Add(art);
        }
        int realShapes = byShape.Count;

        // Every rotation/mirror of every real tile, for filling gaps.
        var transformed = new List<(int shape, TileArt art)>();
        foreach (var (shape, art) in cells)
            for (int t = 0; t < 8; t++) transformed.Add((TransformShape(shape, t), TransformArt(art, t)));

        int rotated = 0, stitched = 0;
        foreach (int shape in AllShapes())
        {
            if (byShape.ContainsKey(shape)) continue;
            var match = transformed.FirstOrDefault(c => c.shape == shape);
            if (match.art != null) { byShape[shape] = new List<TileArt> { match.art }; rotated++; continue; }
            byShape[shape] = new List<TileArt> { Stitch(shape, transformed) };
            stitched++;
        }

        EnsureFolder(OutFolder);
        var spriteNames = WriteAtlas(byShape);
        var sprites = AssetDatabase.LoadAllAssetsAtPath(AtlasPath).OfType<Sprite>().ToDictionary(s => s.name);
        var tile = BuildRuleTile(byShape.Keys.ToList(), spriteNames, sprites);
        BuildPalette(tile);
        AssetDatabase.SaveAssets();

        Debug.Log($"WallTileBuilder: {report}. {realShapes} of 47 shapes from the sheet, {rotated} by rotating/mirroring, " +
                  $"{stitched} stitched from quarter-tiles. Rule Tile: {RuleTilePath}");
    }

    static string FindSheet() =>
        AssetDatabase.FindAssets("t:Texture2D", new[] { SourceFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => !p.StartsWith(OutFolder + "/") && Path.GetDirectoryName(p).Replace('\\', '/') == SourceFolder);

    // ---------- reading the sheet ----------

    static List<(int shape, TileArt art)> ReadSheet(string path, out string report)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        int w = tex.width, h = tex.height;
        var src = tex.GetPixels32();
        Object.DestroyImmediate(tex);

        // Texture pixels per art pixel, from the importer's PPU (PixelArtImporter: Assets/Tiles = 15x -> PPU 240).
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        int tilePx = Mathf.RoundToInt(importer.spritePixelsPerUnit);
        float scale = tilePx / (float)Art;
        int cols = Mathf.RoundToInt(w / (float)tilePx), rows = Mathf.RoundToInt(h / (float)tilePx);
        if (w % tilePx != 0 || h % tilePx != 0)
            Debug.LogWarning($"WallTileBuilder: {w}x{h} isn't a whole number of {tilePx}px tiles; check the Assets/Tiles scale in PixelArtImporter.");

        // Sample the centre of each art pixel (texture rows run bottom-up; ours run top-down).
        Color32 Sample(int ax, int ay)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((ax + 0.5f) * scale), 0, w - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((ay + 0.5f) * scale), 0, h - 1);
            return src[(h - 1 - y) * w + x];
        }

        var result = new List<(int, TileArt)>();
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            var art = new TileArt();
            int solid = 0;
            for (int y = 0; y < Art; y++)
            for (int x = 0; x < Art; x++)
            {
                var p = Sample(c * Art + x, r * Art + y);
                art[x, y] = p;
                if (p.a > 128) solid++;
            }
            if (solid < Art * Art * Coverage) continue;
            result.Add((ShapeFromArt(art), art));
        }
        report = $"{Path.GetFileName(path)} {w}x{h} ({cols}x{rows} tiles at {scale}x), {result.Count} wall tiles found";
        return result;
    }

    static bool IsBody(Color32 p) => p.a > 128 && p.r < FillMaxRed;

    // A side is "connected" when the wall body runs right up to it (no rim); same for corners.
    static int ShapeFromArt(TileArt t)
    {
        float Body(int x0, int y0, int x1, int y1)
        {
            int n = 0, b = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { n++; if (IsBody(t[x, y])) b++; }
            return b / (float)n;
        }
        int m = 0;
        if (Body(3, 0, 13, 2) >= 0.5f) m |= N;
        if (Body(3, 14, 13, 16) >= 0.5f) m |= S;
        if (Body(0, 3, 2, 13) >= 0.5f) m |= W;
        if (Body(14, 3, 16, 13) >= 0.5f) m |= E;
        if (Body(0, 0, 2, 2) >= 0.5f) m |= NW;
        if (Body(14, 0, 16, 2) >= 0.5f) m |= NE;
        if (Body(0, 14, 2, 16) >= 0.5f) m |= SW;
        if (Body(14, 14, 16, 16) >= 0.5f) m |= SE;
        return Normalize(m);
    }

    // Drop diagonals whose two neighbouring sides aren't both connected: they can't change the look.
    static int Normalize(int m)
    {
        if ((m & N) == 0 || (m & E) == 0) m &= ~NE;
        if ((m & S) == 0 || (m & E) == 0) m &= ~SE;
        if ((m & S) == 0 || (m & W) == 0) m &= ~SW;
        if ((m & N) == 0 || (m & W) == 0) m &= ~NW;
        return m;
    }

    static IEnumerable<int> AllShapes() => Enumerable.Range(0, 256).Where(m => Normalize(m) == m); // 47

    // ---------- rotations / mirrors (t: 0-3 = rotate 90° clockwise t times, 4-7 = same after mirroring) ----------

    static int RotateShape(int m) => ((m << 2) | (m >> 6)) & 0xFF;                  // N->E, E->S, ...
    static int MirrorShape(int m)                                                     // left-right
    {
        int r = m & (N | S);
        if ((m & E) != 0) r |= W; if ((m & W) != 0) r |= E;
        if ((m & NE) != 0) r |= NW; if ((m & NW) != 0) r |= NE;
        if ((m & SE) != 0) r |= SW; if ((m & SW) != 0) r |= SE;
        return r;
    }
    static int TransformShape(int m, int t)
    {
        if (t >= 4) m = MirrorShape(m);
        for (int i = 0; i < t % 4; i++) m = RotateShape(m);
        return m;
    }
    static TileArt TransformArt(TileArt a, int t)
    {
        var o = a;
        if (t >= 4) { var m = new TileArt(); for (int y = 0; y < Art; y++) for (int x = 0; x < Art; x++) m[Art - 1 - x, y] = o[x, y]; o = m; }
        for (int i = 0; i < t % 4; i++)
        {
            var r = new TileArt(); // clockwise: (x, y) -> (Art-1-y, x)
            for (int y = 0; y < Art; y++) for (int x = 0; x < Art; x++) r[Art - 1 - y, x] = o[x, y];
            o = r;
        }
        return o;
    }

    // ---------- stitching from quarter-tiles ----------

    // Each 8x8 quarter only depends on its two sides and the corner between them.
    static readonly (int side1, int side2, int corner, int qx, int qy)[] Quarters =
    {
        (N, W, NW, 0, 0), (N, E, NE, 1, 0), (S, W, SW, 0, 1), (S, E, SE, 1, 1),
    };
    static int QuarterState(int shape, int q) => shape & (Quarters[q].side1 | Quarters[q].side2 | Quarters[q].corner);

    static TileArt Stitch(int shape, List<(int shape, TileArt art)> pool)
    {
        // Start from the candidate sharing the most quarters, then patch the rest from the best others.
        var best = pool.OrderByDescending(c => Enumerable.Range(0, 4).Count(q => QuarterState(c.shape, q) == QuarterState(shape, q))).First();
        var o = new TileArt();
        System.Array.Copy(best.art.px, o.px, o.px.Length);
        for (int q = 0; q < 4; q++)
        {
            if (QuarterState(best.shape, q) == QuarterState(shape, q)) continue;
            var donor = pool.First(c => QuarterState(c.shape, q) == QuarterState(shape, q));
            int half = Art / 2, x0 = Quarters[q].qx * half, y0 = Quarters[q].qy * half;
            for (int y = y0; y < y0 + half; y++) for (int x = x0; x < x0 + half; x++) o[x, y] = donor.art[x, y];
        }
        return o;
    }

    // ---------- output ----------

    static string ShapeName(int m)
    {
        if (m == 0) return "Single";
        var names = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return string.Concat(Dirs.Select((d, i) => (m & d.bit) != 0 ? names[i] : ""));
    }

    static Dictionary<int, List<string>> WriteAtlas(Dictionary<int, List<TileArt>> byShape)
    {
        var entries = byShape.OrderBy(k => k.Key).SelectMany(k => k.Value.Select((a, i) => (k.Key, i, a))).ToList();
        int rows = Mathf.CeilToInt(entries.Count / (float)AtlasColumns);
        int tw = AtlasColumns * Art, th = rows * Art;
        var px = new Color32[tw * th];
        var names = new Dictionary<int, List<string>>();
        var rects = new List<SpriteRect>();

        for (int i = 0; i < entries.Count; i++)
        {
            var (shape, variant, art) = entries[i];
            int cx = (i % AtlasColumns) * Art, cyTop = (i / AtlasColumns) * Art;
            for (int y = 0; y < Art; y++) for (int x = 0; x < Art; x++)
                px[(th - 1 - (cyTop + y)) * tw + cx + x] = art[x, y];
            string name = $"Wall_{ShapeName(shape)}_{variant}";
            if (!names.TryGetValue(shape, out var l)) names[shape] = l = new List<string>();
            l.Add(name);
            rects.Add(new SpriteRect { name = name, rect = new Rect(cx, th - cyTop - Art, Art, Art), alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f) });
        }

        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        File.WriteAllBytes(AtlasPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Art; // 1x art: 16 px = 1 tile
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldIds = provider.GetSpriteRects().GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);
        foreach (var r in rects) r.spriteID = oldIds.TryGetValue(r.name, out var id) ? id : GUID.Generate();
        provider.SetSpriteRects(rects.ToArray());
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        return names;
    }

    static RuleTile BuildRuleTile(List<int> shapes, Dictionary<int, List<string>> names, Dictionary<string, Sprite> sprites)
    {
        // Fill a fresh tile in memory, then write it in one go (keeps the asset's GUID if it already exists).
        var tile = ScriptableObject.CreateInstance<RuleTile>();
        tile.name = Path.GetFileNameWithoutExtension(RuleTilePath);
        tile.m_TilingRules = new List<RuleTile.TilingRule>();

        int id = 0;
        foreach (int shape in shapes.OrderByDescending(s => System.Convert.ToString(s, 2).Count(ch => ch == '1')))
        {
            var rule = new RuleTile.TilingRule { m_Id = id++, m_ColliderType = Tile.ColliderType.Grid };
            rule.m_Neighbors = new List<int>();
            rule.m_NeighborPositions = new List<Vector3Int>();
            foreach (var (bit, pos) in Dirs)
            {
                bool diagonal = bit == NE || bit == SE || bit == SW || bit == NW;
                if (diagonal && !Matters(shape, bit)) continue; // don't care
                rule.m_NeighborPositions.Add(pos);
                rule.m_Neighbors.Add((shape & bit) != 0 ? RuleTile.TilingRuleOutput.Neighbor.This : RuleTile.TilingRuleOutput.Neighbor.NotThis);
            }
            rule.m_Sprites = names[shape].Select(n => sprites[n]).ToArray();
            rule.m_Output = rule.m_Sprites.Length > 1 ? RuleTile.TilingRuleOutput.OutputSprite.Random : RuleTile.TilingRuleOutput.OutputSprite.Single;
            tile.m_TilingRules.Add(rule);
        }
        tile.m_DefaultSprite = sprites[names[0][0]];
        tile.m_DefaultColliderType = Tile.ColliderType.Grid;

        var existing = AssetDatabase.LoadAssetAtPath<RuleTile>(RuleTilePath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(tile, existing);
            Object.DestroyImmediate(tile);
            tile = existing;
            EditorUtility.SetDirty(tile);
        }
        else AssetDatabase.CreateAsset(tile, RuleTilePath);
        AssetDatabase.SaveAssetIfDirty(tile);
        return tile;
    }

    static bool Matters(int shape, int diag)
    {
        int a = diag == NE || diag == NW ? N : S;
        int b = diag == NE || diag == SE ? E : W;
        return (shape & a) != 0 && (shape & b) != 0;
    }

    static void BuildPalette(RuleTile tile)
    {
        string path = $"{OutFolder}/{PaletteName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            GridPaletteUtility.CreateNewPalette(OutFolder, PaletteName, GridLayout.CellLayout.Rectangle,
                GridPalette.CellSizing.Automatic, Vector3.one, GridLayout.CellSwizzle.XYZ);

        var root = PrefabUtility.LoadPrefabContents(path);
        var map = root.GetComponentInChildren<Tilemap>();
        map.ClearAllTiles();
        map.SetTile(Vector3Int.zero, tile);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
