using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Slices "Main Drill Spritework.png" (the drill robot) and builds its animations, controller and prefab art.
/// Sheet layout (frames run top to bottom):
///   column 1 = Green health, column 2 = Yellow, column 3 = Red   (640 x 800 px cells, 8 frames each)
///   column 4 = Death explosion                                   (800 x 960 px cells, 8 frames)
///   bottom-left frame = Wreck, held after the explosion.
/// Every sprite's pivot sits on the same spot of the tower (the middle of its base), so switching between
/// columns never makes it jump. The explosion frames' own shake is kept.
/// Run from: Tools > Space Dinosaurs > Build Robot Animations. Safe to re-run (IDs kept).
/// </summary>
public static class RobotSpriteSheetBuilder
{
    const string SheetPath = "Assets/Sprites/Main Drill Spritework.png";
    const string OutFolder = "Assets/Animations/Robot";
    const string ControllerPath = OutFolder + "/Robot.controller";
    const string PrefabPath = "Assets/Prefabs/Robot.prefab";
    const int PixelsPerUnit = 160;   // 10 px per art pixel, 16 art px per tile
    const float FramesPerSecond = 8f;
    const float BaseHeight = 220f;   // px from the bottom of a cell to the middle of the tower's base (the pivot)

    [MenuItem("Tools/Space Dinosaurs/Build Robot Animations")]
    public static void Build()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError($"Robot animations: no sheet at {SheetPath}"); return; }
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(SheetPath));
        int h = tex.height;
        Object.DestroyImmediate(tex);

        // name, rect (bottom-left origin), pivot (0-1 within the rect)
        var frames = new List<(string name, Rect rect, Vector2 pivot)>();
        string[] health = { "Green", "Yellow", "Red" };
        var healthPivot = new Vector2(320f / 640f, BaseHeight / 800f);
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 8; r++)
                frames.Add(($"Robot_{health[c]}_{r}", new Rect(c * 640, h - (r + 1) * 800, 640, 800), healthPivot));
        var deathPivot = new Vector2(450f / 800f, BaseHeight / 960f); // tower rests 450 px into the 800 px cell
        for (int r = 0; r < 8; r++)
            frames.Add(($"Robot_Death_{r}", new Rect(1920, h - (r + 1) * 960, 800, 960), deathPivot));
        frames.Add(("Robot_Wreck", new Rect(0, h - 7680, 640, 800), healthPivot)); // bottom-left frame

        // Slice.
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 8192; // the sheet is 7840 px tall
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldIds = provider.GetSpriteRects().GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First().spriteID);
        var rects = frames.Select(f => new SpriteRect
        {
            name = f.name,
            spriteID = oldIds.TryGetValue(f.name, out var id) ? id : GUID.Generate(),
            rect = f.rect,
            alignment = SpriteAlignment.Custom,
            pivot = f.pivot,
        }).ToArray();
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);

        // Clips.
        EnsureFolder(OutFolder);
        List<Sprite> Seq(string n) => Enumerable.Range(0, 8).Select(i => sprites[$"Robot_{n}_{i}"]).ToList();
        var green = BuildClip("Green", true, Seq("Green"));
        var yellow = BuildClip("Yellow", true, Seq("Yellow"));
        var red = BuildClip("Red", true, Seq("Red"));
        var death = BuildClip("Death", false, Seq("Death"));
        var wreck = BuildClip("Wreck", true, new List<Sprite> { sprites["Robot_Wreck"] });

        // Controller (refreshed in place so the prefab keeps pointing at it).
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        AnimatorState Add(string n, Motion m, int i) { var s = sm.AddState(n, new Vector3(300 + i * 220, 0)); s.motion = m; return s; }
        var gS = Add("Green", green, 0); Add("Yellow", yellow, 1); Add("Red", red, 2);
        var dS = Add("Death", death, 3); var wS = Add("Wreck", wreck, 4);
        sm.defaultState = gS;
        var toWreck = dS.AddTransition(wS);
        toWreck.hasExitTime = true; toWreck.exitTime = 1f; toWreck.duration = 0f; toWreck.hasFixedDuration = true;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        SetUpPrefab(controller, sprites["Robot_Green_0"]);
        Debug.Log($"Robot: {frames.Count} frames, clips Green/Yellow/Red/Death/Wreck -> {ControllerPath}, prefab updated");
    }

    static void SetUpPrefab(RuntimeAnimatorController controller, Sprite firstFrame)
    {
        if (!File.Exists(PrefabPath)) { Debug.LogWarning($"Robot animations: no prefab at {PrefabPath}"); return; }
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var sr = Get<SpriteRenderer>(root);
            sr.sprite = firstFrame;
            sr.color = Color.white;
            var anim = Get<Animator>(root);
            anim.runtimeAnimatorController = controller;
            var art = Get<DrillRobotArt>(root);
            art.animator = anim;

            // Collide with the tower's base (about 2.3 x 1.4 tiles around the pivot) instead of a 1-tile circle.
            foreach (var c in root.GetComponents<CircleCollider2D>()) Object.DestroyImmediate(c, true);
            var cap = Get<CapsuleCollider2D>(root);
            cap.direction = CapsuleDirection2D.Horizontal;
            cap.size = new Vector2(2.3f, 1.4f);
            cap.offset = Vector2.zero;

            // Enemies count as arrived once they're at the edge of the bigger base.
            var goal = root.GetComponent<EnemyGoal>();
            if (goal != null) goal.arriveDistance = 1.8f;
            // Let the explosion (1 s) play before the lose screen comes up.
            var end = root.GetComponent<EndLevelOnDeath>();
            if (end != null) end.delay = 1.25f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // GetComponent returns a fake null in the Editor, which ?? doesn't catch.
    static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static AnimationClip BuildClip(string name, bool loop, List<Sprite> frames)
    {
        string path = $"{OutFolder}/Robot_{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = FramesPerSecond;
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[frames.Count + 1];
        for (int i = 0; i < frames.Count; i++) keys[i] = new ObjectReferenceKeyframe { time = i / FramesPerSecond, value = frames[i] };
        keys[frames.Count] = new ObjectReferenceKeyframe { time = frames.Count / FramesPerSecond, value = frames[^1] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
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
