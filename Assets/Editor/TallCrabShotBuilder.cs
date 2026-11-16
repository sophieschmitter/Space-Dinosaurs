using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Builds the tall crab's shooting from "Tall Crab Projectile Spritework.png" (25 px per art pixel, 16 px cells):
///   - the glowing crab (right side)        -> "Shoot" state on TallCrab.controller (shown while winding up)
///   - the bullet next to the crab          -> Launch (first frame of the shot)
///   - left column, 3 bullets               -> Fly (loops)
///   - middle column: bullet, ring, burst, dot -> Explode
/// and the TallCrabBullet prefab (CrabBullet + art), then gives the TallCrab prefab a CrabShooter using it.
/// The bullet art is drawn flying left; CrabBullet turns it to face the way it flies.
/// Run from: Tools > Space Dinosaurs > Build Tall Crab Shot. Safe to re-run (IDs kept).
/// Build Crab Animations re-adds the Shoot state, so rebuilding the crabs doesn't lose it.
/// </summary>
public static class TallCrabShotBuilder
{
    const string SheetPath = "Assets/Sprites/Tall Crab Projectile Spritework.png";
    const string BulletFolder = "Assets/Animations/Tall Crab Bullet";
    const string BulletController = BulletFolder + "/TallCrabBullet.controller";
    const string CrabFolder = "Assets/Animations/Tall Crab";
    const string CrabController = CrabFolder + "/TallCrab.controller";
    const string BulletPrefab = "Assets/Prefabs/TallCrabBullet.prefab";
    const string CrabPrefab = "Assets/Prefabs/TallCrab.prefab";
    const int Scale = 25;                    // texture px per art pixel on this sheet
    const int PixelsPerUnit = Scale * 16;    // 400: same on-screen pixel size as the other sprites
    const float MuzzleAboveFeet = 19.5f / 16f; // the bullet leaves the crab this high (tiles)

    // Native-pixel rects (x, y from the top, w, h) and pivots (x, y from the top) on the sheet.
    static readonly (string name, RectInt r, Vector2 pivotFromTop)[] Frames =
    {
        ("TallCrabShot_Launch",    new RectInt(32, 16, 16, 16), new Vector2(8.5f, 11.5f)), // bullet beside the crab
        ("TallCrabShot_Fly_0",     new RectInt(0, 0, 16, 16),   new Vector2(2.5f, 7.5f)),  // nose of the bullet
        ("TallCrabShot_Fly_1",     new RectInt(0, 16, 16, 16),  new Vector2(2.5f, 7.5f)),
        ("TallCrabShot_Fly_2",     new RectInt(0, 32, 16, 16),  new Vector2(2.5f, 7.5f)),
        ("TallCrabShot_Explode_0", new RectInt(16, 0, 16, 16),  new Vector2(6.5f, 8.5f)),  // all at the impact point
        ("TallCrabShot_Explode_1", new RectInt(16, 16, 16, 16), new Vector2(6.5f, 8.5f)),
        ("TallCrabShot_Explode_2", new RectInt(16, 32, 16, 16), new Vector2(6.5f, 8.5f)),
        ("TallCrabShot_Explode_3", new RectInt(16, 48, 16, 16), new Vector2(6.5f, 8.5f)),
        ("TallCrab_Glow",          new RectInt(48, 0, 32, 64),  new Vector2(14f, 47f)),   // feet line up with the tall crab
    };

    [MenuItem("Tools/Space Dinosaurs/Build Tall Crab Shot")]
    public static void Build()
    {
        var sprites = Slice();
        if (sprites == null) return;
        EnsureFolder(BulletFolder);

        var launch = Clip(BulletFolder + "/TallCrabBullet_Launch.anim", 12f, false, sprites["TallCrabShot_Launch"]);
        var fly = Clip(BulletFolder + "/TallCrabBullet_Fly.anim", 10f, true,
            sprites["TallCrabShot_Fly_0"], sprites["TallCrabShot_Fly_1"], sprites["TallCrabShot_Fly_2"]);
        var explode = Clip(BulletFolder + "/TallCrabBullet_Explode.anim", 12f, false,
            Enumerable.Range(0, 4).Select(i => sprites[$"TallCrabShot_Explode_{i}"]).ToArray());

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BulletController);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(BulletController);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        var lS = sm.AddState("Launch", new Vector3(300, 0)); lS.motion = launch;
        var fS = sm.AddState("Fly", new Vector3(520, 0)); fS.motion = fly;
        var eS = sm.AddState("Explode", new Vector3(740, 0)); eS.motion = explode;
        sm.defaultState = lS;
        var t = lS.AddTransition(fS); t.hasExitTime = true; t.exitTime = 1f; t.duration = 0f; t.hasFixedDuration = true;
        EditorUtility.SetDirty(controller);

        AddShootState();
        var bullet = MakeBulletPrefab(controller, sprites["TallCrabShot_Launch"], explode.length);
        AddShooter(bullet);
        AssetDatabase.SaveAssets();
        Debug.Log("Tall crab shot: Shoot state, bullet clips (Launch/Fly/Explode), TallCrabBullet prefab and CrabShooter on TallCrab");
    }

    /// <summary>Adds (or refreshes) the glowing "Shoot" state on the tall crab's controller.</summary>
    public static void AddShootState()
    {
        var glow = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().FirstOrDefault(s => s.name == "TallCrab_Glow");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrabController);
        if (glow == null || controller == null) return;
        var clip = Clip(CrabFolder + "/tallcrab_Shoot.anim", 8f, true, glow);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.Where(s => s.state.name == "Shoot").ToArray()) sm.RemoveState(s.state);
        var state = sm.AddState("Shoot", new Vector3(300, 300));
        state.motion = clip;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static Dictionary<string, Sprite> Slice()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError($"Tall crab shot: no sheet at {SheetPath}"); return null; }
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(SheetPath));
        int h = tex.height;
        Object.DestroyImmediate(tex);

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
        var rects = Frames.Select(f => new SpriteRect
        {
            name = f.name,
            spriteID = oldIds.TryGetValue(f.name, out var id) ? id : GUID.Generate(),
            rect = new Rect(f.r.x * Scale, h - (f.r.y + f.r.height) * Scale, f.r.width * Scale, f.r.height * Scale),
            alignment = SpriteAlignment.Custom,
            pivot = new Vector2(f.pivotFromTop.x / f.r.width, 1f - f.pivotFromTop.y / f.r.height),
        }).ToArray();
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
    }

    static CrabBullet MakeBulletPrefab(RuntimeAnimatorController controller, Sprite first, float explodeLength)
    {
        // How high above the crab's middle the bullet comes out: its feet are where the Art child sits.
        float artY = -0.375f;
        var crab = AssetDatabase.LoadAssetAtPath<GameObject>(CrabPrefab);
        var crabArt = crab != null ? crab.transform.Find("Art") : null;
        if (crabArt != null) artY = crabArt.localPosition.y;

        bool existing = File.Exists(BulletPrefab);
        GameObject root = existing ? PrefabUtility.LoadPrefabContents(BulletPrefab) : new GameObject("TallCrabBullet");
        try
        {
            var art = root.transform.Find("Art");
            if (art == null) { art = new GameObject("Art").transform; art.SetParent(root.transform, false); }
            art.localPosition = new Vector3(0f, artY + MuzzleAboveFeet, 0f);
            var sr = Get<SpriteRenderer>(art.gameObject);
            sr.sprite = first;
            sr.sortingOrder = 3;
            var anim = Get<Animator>(art.gameObject);
            anim.runtimeAnimatorController = controller;
            var b = Get<CrabBullet>(root);
            b.art = art; b.animator = anim; b.explodeTime = explodeLength;
            var group = Get<UnityEngine.Rendering.SortingGroup>(root);
            group.sortingOrder = 5; // sorts with the dino and crabs by height on screen
            var saved = PrefabUtility.SaveAsPrefabAsset(root, BulletPrefab);
            return saved.GetComponent<CrabBullet>();
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    static void AddShooter(CrabBullet bullet)
    {
        if (!File.Exists(CrabPrefab)) return;
        var root = PrefabUtility.LoadPrefabContents(CrabPrefab);
        try
        {
            var shooter = Get<CrabShooter>(root);
            shooter.bulletPrefab = bullet;
            PrefabUtility.SaveAsPrefabAsset(root, CrabPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static AnimationClip Clip(string path, float fps, bool loop, params Sprite[] frames)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = fps;
        var keys = new ObjectReferenceKeyframe[frames.Length + 1];
        for (int i = 0; i < frames.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };
        keys[frames.Length] = new ObjectReferenceKeyframe { time = frames.Length / fps, value = frames[^1] };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        // Never mirrored (the crab's _Right clips flip the sprite; this un-flips it).
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(SpriteRenderer), "m_FlipX"),
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(frames.Length / fps, 0f)));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
