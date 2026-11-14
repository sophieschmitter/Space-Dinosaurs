using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Slices the crab sheets (one column = one animation, frames run top to bottom) into animations and an
/// Animator Controller each: "Rusty Crab Sprites.png" -> crab1 (Assets/Animations/Rusty Crab) and
/// "Tall Crab Spritework.png" -> tallcrab (Assets/Animations/Tall Crab). Both sheets use the same column order.
/// Side animations get both a _Left and a _Right clip (the art faces left; _Right is the same frames mirrored,
/// with the flip saved in the clip). State names match what EnemyController plays:
/// Idle/Walk with _Left, _Right, _Front or _Back, victoryDance (on the lose screen) and Death.
/// Run from: Tools > Space Dinosaurs > Build Crab Animations. Safe to re-run (IDs kept).
/// </summary>
public static class CrabSpriteSheetBuilder
{
    const int PixelsPerUnit = 160;     // 10 px per art pixel * 16 art px per tile (matches the dino)
    const float FramesPerSecond = 8f;
    const string DefaultState = "Idle_Front";

    /// <summary>One crab sheet: where it is, where its clips go, and its cell layout (texture px).</summary>
    class Sheet
    {
        public string path, outFolder, controller, prefix;
        public int cellW, cellH, topMargin, feetFromTop;
    }

    static readonly Sheet[] Sheets =
    {
        // crab1: 320 x 320 cells, feet 210 px down each cell.
        new Sheet { path = "Assets/Sprites/Rusty Crab Sprites.png", outFolder = "Assets/Animations/Rusty Crab",
                    controller = "Crab1.controller", prefix = "crab1", cellW = 320, cellH = 320, topMargin = 0, feetFromTop = 210 },
        // tall crab: 320 x 480 cells starting 50 px down (same rows as the dino sheets), feet 430 px down each cell.
        new Sheet { path = "Assets/Sprites/Tall Crab Spritework.png", outFolder = "Assets/Animations/Tall Crab",
                    controller = "TallCrab.controller", prefix = "tallcrab", cellW = 320, cellH = 480, topMargin = 50, feetFromTop = 430 },
    };

    // Columns left to right: sprite name, loops, and the clips made from it (clip name, mirrored?).
    // Leave the sprite names alone; rename clips freely and re-run.
    static readonly (string sprite, bool loop, (string clip, bool mirror)[] clips)[] Columns =
    {
        ("Idle_Side",  true,  new[] { ("Idle_Left", false), ("Idle_Right", true) }), // col 1 (art faces left)
        ("Walk_Side",  true,  new[] { ("Walk_Left", false), ("Walk_Right", true) }), // col 2
        ("Idle_Front", true,  new[] { ("Idle_Front", false) }),                      // col 3
        ("Walk_Front", true,  new[] { ("Walk_Front", false) }),                      // col 4
        ("Idle_Back",  true,  new[] { ("Idle_Back", false) }),                       // col 5
        ("Walk_Back",  true,  new[] { ("Walk_Back", false) }),                       // col 6
        ("Victory",    true,  new[] { ("victoryDance", false) }),                    // col 7: when the player loses
        ("Death",      false, new[] { ("Death", false) }),                           // col 8
    };

    // Earlier clip names -> current ones (renamed in place so their IDs survive).
    static readonly (string from, string to)[] Renames =
    {
        ("Idle_Side", "Idle_Left"), ("Walk_Side", "Walk_Left"), ("Hurt", "victoryDance"), ("Victory", "victoryDance"),
    };

    [MenuItem("Tools/Space Dinosaurs/Build Crab Animations")]
    public static void Build()
    {
        foreach (var sheet in Sheets) Build(sheet);
    }

    static void Build(Sheet sheet)
    {
        string SheetPath = sheet.path, OutFolder = sheet.outFolder, Prefix = sheet.prefix;
        string ControllerPath = OutFolder + "/" + sheet.controller;
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError($"Crab animations: no sheet at {SheetPath}"); return; }

        // Which cells have a frame in them (read straight from the PNG).
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(SheetPath));
        int w = tex.width, h = tex.height, rows = (h - sheet.topMargin) / sheet.cellH;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);

        var frames = new List<(int col, int row, string name, RectInt rect)>();
        for (int c = 0; c < Columns.Length; c++)
        for (int r = 0; r < rows; r++)
        {
            var rect = new RectInt(c * sheet.cellW, h - sheet.topMargin - (r + 1) * sheet.cellH, sheet.cellW, sheet.cellH); // bottom-left origin
            int count = 0;
            for (int y = rect.yMin; y < rect.yMax && count < 500; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
                if (px[y * w + x].a > 0) count++;
            if (count >= 500) frames.Add((c, r, $"{Prefix}_{Columns[c].sprite}_{r}", rect));
        }

        // Slice.
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096;
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldIds = provider.GetSpriteRects().GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First().spriteID);
        var pivot = new Vector2(0.5f, (sheet.cellH - sheet.feetFromTop) / (float)sheet.cellH);
        var rects = frames.Select(f => new SpriteRect
        {
            name = f.name,
            spriteID = oldIds.TryGetValue(f.name, out var id) ? id : GUID.Generate(),
            rect = new Rect(f.rect.x, f.rect.y, f.rect.width, f.rect.height),
            alignment = SpriteAlignment.Custom,
            pivot = pivot,
        }).ToArray();
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);

        // Clips.
        EnsureFolder(OutFolder);
        foreach (var (from, to) in Renames)
        {
            string oldPath = $"{OutFolder}/{Prefix}_{from}.anim", newPath = $"{OutFolder}/{Prefix}_{to}.anim";
            if (File.Exists(oldPath) && !File.Exists(newPath)) AssetDatabase.MoveAsset(oldPath, newPath);
        }
        var clips = new List<AnimationClip>();
        for (int c = 0; c < Columns.Length; c++)
        {
            var seq = frames.Where(f => f.col == c).OrderBy(f => f.row).Select(f => sprites[f.name]).ToList();
            if (seq.Count == 0) continue;
            foreach (var (clipName, mirror) in Columns[c].clips) clips.Add(BuildClip(OutFolder, Prefix, clipName, Columns[c].loop, mirror, seq));
        }

        // Controller (states refreshed in place so prefabs keep pointing at it).
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        for (int i = 0; i < clips.Count; i++)
        {
            string stateName = clips[i].name.Substring(Prefix.Length + 1);
            var state = sm.AddState(stateName, new Vector3(300 + (i % 4) * 240, i / 4 * 60));
            state.motion = clips[i];
            if (stateName == DefaultState) sm.defaultState = state;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Prefix}: {frames.Count} frames in {clips.Count} clips -> {ControllerPath}");
    }

    static AnimationClip BuildClip(string OutFolder, string Prefix, string name, bool loop, bool mirror, List<Sprite> frames)
    {
        string path = $"{OutFolder}/{Prefix}_{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = FramesPerSecond;
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[frames.Count + 1];
        for (int i = 0; i < frames.Count; i++) keys[i] = new ObjectReferenceKeyframe { time = i / FramesPerSecond, value = frames[i] };
        keys[frames.Count] = new ObjectReferenceKeyframe { time = frames.Count / FramesPerSecond, value = frames[^1] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        // Every clip sets the flip, so going from a _Right clip to any other clip un-mirrors the sprite.
        float flip = mirror ? 1f : 0f, end = frames.Count / FramesPerSecond;
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(SpriteRenderer), "m_FlipX"),
            new AnimationCurve(new Keyframe(0f, flip), new Keyframe(end, flip)));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
