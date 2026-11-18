using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Slices "UntitledArtwork4.PNG" into the buildings' animations. Each row is one animation, frames left to
/// right in 320 px columns. Top three rows: shield generator, ice cream stand, turret placed down (Normal).
/// Bottom three rows: the same, outlined, for while the player is placing it (Selected). Every frame of a
/// building shares one ground line, so switching between Normal and Selected doesn't make it jump.
/// Makes an Animator Controller per building (Assets/Animations/Buildings) and a prefab
/// (Assets/Prefabs/Buildings) with a Building component that switches between the two. The ice cream stand's
/// first frame (no ice cream) is left out of its loops and becomes an Empty state, shown while it refills.
/// Run from: Tools > Space Dinosaurs > Build Buildings. Safe to re-run (sprite IDs and prefabs kept).
/// </summary>
public static class BuildingSpriteSheetBuilder
{
    const string SheetPath = "Assets/Sprites/UntitledArtwork4.PNG";
    const string AnimFolder = "Assets/Animations/Buildings";
    const string PrefabFolder = "Assets/Prefabs/Buildings";
    const int PixelsPerUnit = 160;      // 10 px per art px, 16 art px per tile (like the characters)
    const int CellW = 320;
    const int SelectedOffset = 1600;    // the Selected rows are this far below the Normal ones
    const int Below = 20;               // room under the ground line for the Selected outline
    const float FramesPerSecond = 6f;

    class Def
    {
        public string name, item;
        public int firstCol, frames;
        public int ground;              // texture px from the top: where the base sits in the Normal row
        public int above;               // how much art there is above the ground line
        public Vector2 footprint;       // collider at the base, tiles
        public int emptyFrame = -1;     // a frame left out of the loops and given its own Empty state (the stand with no ice cream)
    }

    static readonly Def[] Defs =
    {
        new Def { name = "ShieldGenerator", item = "Shield Generator", firstCol = 0, frames = 4, ground = 480,  above = 370, footprint = new Vector2(1.5f, 0.6f) },
        new Def { name = "IceCreamStand",   item = "Ice Cream Stand",  firstCol = 1, frames = 5, ground = 1050, above = 420, footprint = new Vector2(1.5f, 0.6f), emptyFrame = 0 },
        new Def { name = "Turret",          item = "Turret",           firstCol = 1, frames = 5, ground = 1600, above = 510, footprint = new Vector2(1.5f, 0.6f) },
    };

    [MenuItem("Tools/Space Dinosaurs/Build Buildings")]
    public static void Build()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError($"Buildings: no sheet at {SheetPath}"); return; }
        int texH = 3360;

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
        var rects = new List<SpriteRect>();
        foreach (var d in Defs)
        foreach (bool sel in new[] { false, true })
        for (int f = 0; f < d.frames; f++)
        {
            string n = FrameName(d, sel, f);
            int ground = d.ground + (sel ? SelectedOffset : 0);
            int h = d.above + Below;
            rects.Add(new SpriteRect
            {
                name = n,
                spriteID = oldIds.TryGetValue(n, out var id) ? id : GUID.Generate(),
                rect = new Rect((d.firstCol + f) * CellW, texH - ground - Below, CellW, h), // bottom-left origin
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, Below / (float)h), // on the ground line, middle of the cell
            });
        }
        provider.SetSpriteRects(rects.ToArray());
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        if (sprites.Count < rects.Count)
        {
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        }
        if (sprites.Count < rects.Count) { Debug.LogError("Buildings: couldn't load the sliced sprites; run this again."); return; }

        EnsureFolder(AnimFolder);
        EnsureFolder(PrefabFolder);
        foreach (var d in Defs)
        {
            var loop = Enumerable.Range(0, d.frames).Where(f => f != d.emptyFrame).ToArray();
            var normal = BuildClip(d, "Normal", false, loop, sprites);
            var selected = BuildClip(d, "Selected", true, loop, sprites);
            var empty = d.emptyFrame >= 0 ? BuildClip(d, "Empty", false, new[] { d.emptyFrame }, sprites) : null;
            var controller = BuildController(d, normal, selected, empty);
            BuildPrefab(d, controller, sprites[FrameName(d, false, 0)]);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Buildings: " + string.Join(", ", Defs.Select(d => d.item)) + " -> " + PrefabFolder);
    }

    static string FrameName(Def d, bool selected, int f) => $"{d.name}_{(selected ? "Selected" : "Normal")}_{f}";

    static AnimationClip BuildClip(Def d, string state, bool selected, int[] frames, Dictionary<string, Sprite> sprites)
    {
        string path = $"{AnimFolder}/{d.name}_{state}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = FramesPerSecond;
        var keys = new ObjectReferenceKeyframe[frames.Length + 1];
        for (int i = 0; i < frames.Length; i++) keys[i] = new ObjectReferenceKeyframe { time = i / FramesPerSecond, value = sprites[FrameName(d, selected, frames[i])] };
        keys[frames.Length] = new ObjectReferenceKeyframe { time = frames.Length / FramesPerSecond, value = sprites[FrameName(d, selected, frames[^1])] };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorController BuildController(Def d, AnimationClip normal, AnimationClip selected, AnimationClip empty)
    {
        string path = $"{AnimFolder}/{d.name}.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        var n = sm.AddState("Normal", new Vector3(300, 0));
        n.motion = normal;
        var sel = sm.AddState("Selected", new Vector3(300, 80));
        sel.motion = selected;
        if (empty != null) sm.AddState("Empty", new Vector3(300, 160)).motion = empty;
        sm.defaultState = n;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void BuildPrefab(Def d, RuntimeAnimatorController controller, Sprite first)
    {
        string path = $"{PrefabFolder}/{d.item}.prefab";
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject(d.item);
        try
        {
            var sr = Get<SpriteRenderer>(root);
            sr.sprite = first;
            if (sr.sortingOrder < 3) sr.sortingOrder = 3;
            Get<Animator>(root).runtimeAnimatorController = controller;
            Get<SortingGroup>(root).sortingOrder = 5; // Y-sorted with the dino, crabs and robot
            var box = Get<BoxCollider2D>(root);
            box.size = d.footprint;
            box.offset = new Vector2(0f, d.footprint.y * 0.5f);
            var b = Get<Building>(root);
            b.itemName = d.item;
            b.animator = root.GetComponent<Animator>();
            if (d.name == "ShieldGenerator") SetUpShield(Get<ShieldGenerator>(root));
            if (d.emptyFrame >= 0) // the ice cream stand: walking up to it eats the ice cream and heals
            {
                Get<IceCreamStand>(root);
                // The serving area trigger sits on the stand itself: with no Rigidbody, trigger messages only
                // reach the object the collider is on.
                var oldArea = root.transform.Find("Serving Area");
                if (oldArea != null) Object.DestroyImmediate(oldArea.gameObject);
                var circle = Get<CircleCollider2D>(root);
                circle.isTrigger = true;
                circle.radius = 1.2f;
                circle.offset = new Vector2(0f, 0.3f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    // "Shield Generator Effect.PNG": six frames (rows, 320 px apart). Each has a 16 x 16 art px corner piece
    // (drawn as the shield's bottom-right corner) and a 10 x 16 edge piece (its right side). Both pivot on their
    // bottom-right corner, on the shield's outer line.
    const string ShieldSheet = "Assets/Sprites/Shield Generator Effect.PNG";

    static void SetUpShield(ShieldGenerator shield)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(ShieldSheet);
        if (importer == null) { Debug.LogError($"Buildings: no shield sheet at {ShieldSheet}"); return; }
        int texH = 2560, frames = 6;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldIds = provider.GetSpriteRects().GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First().spriteID);
        var rects = new List<SpriteRect>();
        for (int f = 0; f < frames; f++)
        {
            int top = 160 + f * 320;
            foreach (var (n, x, w) in new[] { ($"ShieldCorner_{f}", 640, 160), ($"ShieldEdge_{f}", 1020, 100) })
                rects.Add(new SpriteRect
                {
                    name = n,
                    spriteID = oldIds.TryGetValue(n, out var id) ? id : GUID.Generate(),
                    rect = new Rect(x, texH - top - 160, w, 160),
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2(1f, 0f),
                });
        }
        provider.SetSpriteRects(rects.ToArray());
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(ShieldSheet).OfType<Sprite>().ToDictionary(s => s.name);
        if (sprites.Count < rects.Count)
        {
            AssetDatabase.ImportAsset(ShieldSheet, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            sprites = AssetDatabase.LoadAllAssetsAtPath(ShieldSheet).OfType<Sprite>().ToDictionary(s => s.name);
        }
        shield.cornerFrames = Enumerable.Range(0, frames).Select(f => sprites.TryGetValue($"ShieldCorner_{f}", out var s) ? s : null).ToArray();
        shield.edgeFrames = Enumerable.Range(0, frames).Select(f => sprites.TryGetValue($"ShieldEdge_{f}", out var s) ? s : null).ToArray();
    }

    // GetComponent returns a fake null in the Editor, so check explicitly rather than with ??.
    static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
