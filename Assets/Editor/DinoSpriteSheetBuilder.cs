using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Slices "Dinosaur Spritework.PNG" into a grid (one column = one animation, frames run top to bottom)
/// and builds an AnimationClip per column, an AnimatorController holding them all, and a Dino prefab.
/// Run from: Tools > Space Dinosaurs > Build Dino Animations. Safe to re-run (sprite IDs are kept).
/// </summary>
public static class DinoSpriteSheetBuilder
{
    const string SheetPath = "Assets/Sprites/Dinosaur Spritework.PNG";
    const string OutFolder = "Assets/Animations/Dino";
    const string PrefabPath = "Assets/Prefabs/Dino.prefab";

    // Sheet layout (in texture pixels). The art is drawn at 10 texture px per art pixel.
    const int CellW = 480;
    const int CellH = 480;
    const int Columns = 12;
    const int Rows = 5;
    const int TopMargin = 50;          // empty strip above the first row
    const int FeetFromCellBottom = 50; // where the feet sit inside each cell -> pivot
    const int PixelsPerUnit = 160;     // 10 px per art pixel * 16 art px per unit (matches PixelArtImporter's PPU 16)
    const float FramesPerSecond = 8f;

    // One entry per column, left to right. Rename freely; re-run the menu item afterwards.
    static readonly (string name, bool loop)[] ColumnClips =
    {
        ("Side_Idle",   true),   // col 0
        ("Side_Walk",   true),   // col 1
        ("Side_Idle2",  true),   // col 2
        ("Side_Walk2",  true),   // col 3
        ("Front_Idle",  true),   // col 4
        ("Front_Walk",  true),   // col 5
        ("Back_Idle",   true),   // col 6
        ("Back_Walk",   true),   // col 7 (no frame in the top row)
        ("Huff",        true),   // col 8
        ("Death",       false),  // col 9
        ("Huff2",       true),   // col 10
        ("Death2",      false),  // col 11
    };

    [MenuItem("Tools/Space Dinosaurs/Build Dino Animations")]
    public static void Build()
    {
        var importer = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
        if (importer == null) { Debug.LogError($"No texture at {SheetPath}"); return; }

        var cells = FindFilledCells();
        SliceSheet(importer, cells);

        var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>()
            .ToDictionary(s => s.name);

        EnsureFolder(OutFolder);
        var clips = new List<AnimationClip>();
        for (int c = 0; c < Columns; c++)
        {
            var frames = cells.Where(cell => cell.col == c).OrderBy(cell => cell.row)
                .Select(cell => sprites[SpriteName(cell.col, cell.row)]).ToList();
            if (frames.Count == 0) continue;
            clips.Add(BuildClip(ColumnClips[c].name, ColumnClips[c].loop, frames));
        }

        var controller = BuildController(clips);
        BuildPrefab(controller, clips[0]);

        AssetDatabase.SaveAssets();
        Debug.Log($"Dino: sliced {cells.Count} frames into {clips.Count} clips in {OutFolder}, prefab at {PrefabPath}");
    }

    static string SpriteName(int col, int row) => $"{ColumnClips[col].name}_{row}";

    // Reads the PNG straight from disk so the texture doesn't need Read/Write enabled.
    static List<(int col, int row)> FindFilledCells()
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(SheetPath));
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        var filled = new List<(int, int)>();

        for (int c = 0; c < Columns; c++)
        for (int r = 0; r < Rows; r++)
        {
            RectInt rect = CellRect(c, r, h);
            int count = 0;
            for (int y = rect.yMin; y < rect.yMax && count < 2000; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
                if (px[y * w + x].a > 0) count++;
            if (count >= 2000) filled.Add((c, r)); // ignore cells holding only stray dust
        }
        Object.DestroyImmediate(tex);
        return filled;
    }

    // Unity sprite rects use a bottom-left origin.
    static RectInt CellRect(int col, int row, int texHeight) =>
        new RectInt(col * CellW, texHeight - TopMargin - (row + 1) * CellH, CellW, CellH);

    static void SliceSheet(TextureImporter importer, List<(int col, int row)> cells)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 8192; // sheet is 5760 wide; the 2048 default downscales and blurs it

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        // Keep IDs of sprites that already exist with the same name so references survive re-runs.
        var oldIds = provider.GetSpriteRects().GroupBy(s => s.name)
            .ToDictionary(g => g.Key, g => g.First().spriteID);

        int texHeight = 2560;
        importer.GetSourceTextureWidthAndHeight(out _, out texHeight);

        var pivot = new Vector2(0.5f, FeetFromCellBottom / (float)CellH);
        var rects = cells.Select(cell =>
        {
            string name = SpriteName(cell.col, cell.row);
            var r = CellRect(cell.col, cell.row, texHeight);
            return new SpriteRect
            {
                name = name,
                spriteID = oldIds.TryGetValue(name, out var id) ? id : GUID.Generate(),
                rect = new Rect(r.x, r.y, r.width, r.height),
                alignment = SpriteAlignment.Custom,
                pivot = pivot,
            };
        }).ToArray();

        provider.SetSpriteRects(rects);
        var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
    }

    static AnimationClip BuildClip(string name, bool loop, List<Sprite> frames)
    {
        string path = $"{OutFolder}/Dino_{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = FramesPerSecond;

        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[frames.Count + 1];
        for (int i = 0; i < frames.Count; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / FramesPerSecond, value = frames[i] };
        // Extra key so the last frame is shown for a full frame before looping/ending.
        keys[frames.Count] = new ObjectReferenceKeyframe { time = frames.Count / FramesPerSecond, value = frames[^1] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorController BuildController(List<AnimationClip> clips)
    {
        string path = $"{OutFolder}/Dino.controller";
        AssetDatabase.DeleteAsset(path); // rebuilt from scratch each run
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = controller.layers[0].stateMachine;

        for (int i = 0; i < clips.Count; i++)
        {
            string stateName = clips[i].name.Replace("Dino_", "");
            var state = sm.AddState(stateName, new Vector3(300 + (i % 4) * 220, i / 4 * 80));
            state.motion = clips[i];
            if (i == 0) sm.defaultState = state;
        }
        return controller;
    }

    static void BuildPrefab(AnimatorController controller, AnimationClip firstClip)
    {
        EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
        var go = new GameObject("Dino");
        var sr = go.AddComponent<SpriteRenderer>();
        var firstKey = AnimationUtility.GetObjectReferenceCurve(firstClip,
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"))[0];
        sr.sprite = firstKey.value as Sprite;
        go.AddComponent<Animator>().runtimeAnimatorController = controller;
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
