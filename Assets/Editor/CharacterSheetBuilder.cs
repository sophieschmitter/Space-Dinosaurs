using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Slices the TV, portrait and victory sheets on their drawing grids (so frames stay lined up: no jitter) and
/// builds their animations. Run from Tools > Space Dinosaurs > Build TV, Portrait and Victory Animations.
///
/// TV Spritework.PNG (32 x 32 art px cells; the screen bobs up 1 px when the mouth opens):
///   Idle (two neutral frames), Talk (neutral, mouth open/closed), Look, Happy, Squint, Bad Guy (two frames),
///   and Off: its own prefab (TV (Off)), not an animation. Prefab: TV (SpriteRenderer + Animator).
/// TV Portraits.PNG (the TV's face for the dialogue box): rows are Neutral, Happy, Look, Squint; each row is
///   mouth open then closed, one talking loop per emotion. Animates a UI Image (the dialogue portrait).
/// Portraits.PNG (the dino bad guy): all eight frames are one animation, Talk. Also a UI Image.
/// Victory Jumps.PNG: the dino with the gun (left column) and the drill (right column) cheering, 4 frames each,
///   facing left, lined up with the dino's own sprites (same pivot at the feet). Added to the Dino controller as
///   Gun_Victory and Drill_Victory.
/// </summary>
public static class CharacterSheetBuilder
{
    const int Px = 10; // texture pixels per art pixel

    [MenuItem("Tools/Space Dinosaurs/Build TV, Portrait and Victory Animations")]
    public static void Build()
    {
        // ---------- TV ----------
        var tv = Slice("Assets/Sprites/TV Spritework.PNG", Cells(new[]
        {
            ("TV_Idle_0", 0, 0), ("TV_Talk_Open", 1, 0), ("TV_Look_Open", 2, 0), ("TV_Happy_Open", 3, 0), ("TV_Squint_Open", 4, 0), ("TV_Off", 5, 0),
            ("TV_Idle_1", 0, 1), ("TV_Talk_Closed", 1, 1), ("TV_Happy_Closed", 2, 1), ("TV_Look_Closed", 3, 1), ("TV_Squint_Closed", 4, 1),
            ("TV_BadGuy_0", 0, 2), ("TV_BadGuy_1", 0, 3),
        }, x0: 12, y0: 16, stepX: 32, stepY: 32, w: 32, h: 32, pivotX: 16, pivotY: 0));
        string tvFolder = Folder("Assets/Animations", "TV");
        var tvStates = new (string state, string[] frames, float fps)[]
        {
            ("Idle", new[] { "TV_Idle_0", "TV_Idle_1" }, 2f),
            ("Talk", new[] { "TV_Talk_Open", "TV_Talk_Closed" }, 6f),
            ("Look", new[] { "TV_Look_Open", "TV_Look_Closed" }, 6f),
            ("Happy", new[] { "TV_Happy_Open", "TV_Happy_Closed" }, 6f),
            ("Squint", new[] { "TV_Squint_Open", "TV_Squint_Closed" }, 6f),
            ("Bad Guy", new[] { "TV_BadGuy_0", "TV_BadGuy_1" }, 4f),
        };
        var tvController = Controller($"{tvFolder}/TV.controller",
            tvStates.Select(s => (s.state, Clip($"{tvFolder}/TV_{s.state.Replace(" ", "")}.anim", s.frames.Select(f => tv[f]).ToArray(), s.fps, typeof(SpriteRenderer)))).ToArray());
        TvPrefab("Assets/Prefabs/TV.prefab", "TV", tv["TV_Idle_0"], tvController);
        TvPrefab("Assets/Prefabs/TV (Off).prefab", "TV (Off)", tv["TV_Off"], null);

        // ---------- TV face portraits (dialogue box) ----------
        var faces = Slice("Assets/Sprites/TV Portraits.PNG", Cells(new[]
        {
            ("TVFace_Neutral_Open", 0, 0), ("TVFace_Neutral_Closed", 1, 0),
            ("TVFace_Happy_Open", 0, 1), ("TVFace_Happy_Closed", 1, 1),
            ("TVFace_Look_Open", 0, 2), ("TVFace_Look_Closed", 1, 2),
            ("TVFace_Squint_Open", 0, 3), ("TVFace_Squint_Closed", 1, 3),
        }, x0: 16, y0: 16, stepX: 80, stepY: 80, w: 64, h: 64, pivotX: 32, pivotY: 32));
        string portraitFolder = Folder("Assets/Animations", "Portraits");
        Controller($"{portraitFolder}/TV Face Portrait.controller", new[] { "Neutral", "Happy", "Look", "Squint" }
            .Select(e => (e, Clip($"{portraitFolder}/TVFace_{e}.anim", new[] { faces[$"TVFace_{e}_Open"], faces[$"TVFace_{e}_Closed"] }, 6f, typeof(Image)))).ToArray());

        // ---------- Dino bad guy portrait ----------
        var bad = Slice("Assets/Sprites/Portraits.PNG", Cells(Enumerable.Range(0, 8).Select(i => ($"BadGuyPortrait_{i}", i % 2, i / 2)).ToArray(),
            x0: 16, y0: 16, stepX: 80, stepY: 80, w: 64, h: 64, pivotX: 32, pivotY: 32));
        Controller($"{portraitFolder}/Bad Guy Portrait.controller", new[]
        {
            ("Talk", Clip($"{portraitFolder}/BadGuy_Talk.anim", Enumerable.Range(0, 8).Select(i => bad[$"BadGuyPortrait_{i}"]).ToArray(), 6f, typeof(Image))),
        });

        // ---------- Dino victory ----------
        var win = Slice("Assets/Sprites/Victory Jumps.PNG", Cells(Enumerable.Range(0, 4).SelectMany(i => new[] { ($"Gun_Victory_{i}", 0, i), ($"Drill_Victory_{i}", 1, i) }).ToArray(),
            x0: 14, y0: 1, stepX: 80, stepY: 47, w: 48, h: 47, pivotX: 25, pivotY: 0));
        string dinoFolder = "Assets/Animations/Dino";
        var gunWin = Clip($"{dinoFolder}/Dino_Gun_Victory.anim", Enumerable.Range(0, 4).Select(i => win[$"Gun_Victory_{i}"]).ToArray(), 8f, typeof(SpriteRenderer));
        var drillWin = Clip($"{dinoFolder}/Drill_Victory.anim", Enumerable.Range(0, 4).Select(i => win[$"Drill_Victory_{i}"]).ToArray(), 8f, typeof(SpriteRenderer));
        var dino = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{dinoFolder}/Dino.controller");
        if (dino != null)
        {
            AddOrReplaceState(dino, "Gun_Victory", gunWin);
            AddOrReplaceState(dino, "Drill_Victory", drillWin);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Built TV, portrait and victory animations.");
    }

    // ---------- spawner and rocket ship ----------

    /// <summary>Spawnerd.PNG: the enemy spawner pod, 4 frames (32 x 24 art px cells, bottoms lined up). Idle loops
    /// them; Spawn plays them quickly once when an enemy comes out, then goes back to Idle. Put on all three
    /// spawner prefabs (Crab Spawner, Tall Crab Spawner, JumpySpawner), so every spawner in every level uses it.
    /// Spaceship.PNG: the rocket ship, 2 frames (engine flame flicker). Prefab: Assets/Prefabs/Rocket Ship.</summary>
    [MenuItem("Tools/Space Dinosaurs/Build Spawner and Rocket Animations")]
    public static void BuildSpawnerAndRocket()
    {
        // pod frames are 17 px wide starting 5 px in: pivot at their middle (13.5), on the cell bottom
        var pod = Slice("Assets/Sprites/Spawnerd.PNG", Cells(Enumerable.Range(0, 4).Select(i => ($"Spawner_{i}", 0, i)).ToArray(),
            x0: 0, y0: 0, stepX: 32, stepY: 24, w: 32, h: 24, pivotX: 13.5f, pivotY: 0));
        string folder = Folder("Assets/Animations", "Spawner");
        var frames = Enumerable.Range(0, 4).Select(i => pod[$"Spawner_{i}"]).ToArray();
        var idle = Clip($"{folder}/Spawner_Idle.anim", frames, 6f, typeof(SpriteRenderer));
        var spawn = Clip($"{folder}/Spawner_Spawn.anim", frames, 14f, typeof(SpriteRenderer));
        var spawnSettings = AnimationUtility.GetAnimationClipSettings(spawn);
        spawnSettings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(spawn, spawnSettings);
        var controller = Controller($"{folder}/Spawner.controller", new[] { ("Idle", idle), ("Spawn", spawn) });
        var sm = controller.layers[0].stateMachine;
        var spawnState = sm.states.First(st => st.state.name == "Spawn").state;
        var idleState = sm.states.First(st => st.state.name == "Idle").state;
        var back = spawnState.AddTransition(idleState);
        back.hasExitTime = true; back.exitTime = 1f; back.duration = 0f;

        foreach (var name in new[] { "Crab Spawner", "Tall Crab Spawner", "JumpySpawner" })
            SpawnerArt($"Assets/Prefabs/{name}.prefab", pod["Spawner_0"], controller);

        // ---------- rocket ship ----------
        var ship = Slice("Assets/Sprites/Spaceship.PNG", Cells(new[] { ("Rocket_0", 0, 0), ("Rocket_1", 0, 1) },
            x0: 0, y0: 0, stepX: 198, stepY: 99, w: 198, h: 99, pivotX: 99, pivotY: 49.5f));
        string rocketFolder = Folder("Assets/Animations", "Rocket Ship");
        var fly = Clip($"{rocketFolder}/Rocket_Flame.anim", new[] { ship["Rocket_0"], ship["Rocket_1"] }, 8f, typeof(SpriteRenderer));
        var rocketController = Controller($"{rocketFolder}/Rocket Ship.controller", new[] { ("Flame", fly) });
        RocketPrefab("Assets/Prefabs/Rocket Ship.prefab", ship["Rocket_0"], rocketController);

        AssetDatabase.SaveAssets();
        Debug.Log("Built the spawner and rocket ship animations.");
    }

    // The pod art, on the spawner's "Art" child.
    static void SpawnerArt(string path, Sprite first, RuntimeAnimatorController controller)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var spawner = root.GetComponent<EnemySpawner>();
            var artT = root.transform.Find("Art");
            if (artT == null)
            {
                // (the jumpy spawner drew a square on itself) give it an Art child like the crab spawners
                var oldSr = root.GetComponent<SpriteRenderer>();
                if (oldSr != null) Object.DestroyImmediate(oldSr);
                artT = new GameObject("Art").transform;
                artT.SetParent(root.transform, false);
            }
            var art = artT.gameObject;
            var sr = Get<SpriteRenderer>(art);
            sr.sprite = first;
            sr.color = Color.white;
            sr.drawMode = SpriteDrawMode.Simple;
            sr.sortingOrder = 2;
            art.transform.localPosition = new Vector3(0f, -0.5f, 0f); // stands on its tile
            art.transform.localScale = Vector3.one;
            var anim = Get<Animator>(art);
            anim.runtimeAnimatorController = controller;
            if (spawner != null) spawner.animator = anim;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void RocketPrefab(string path, Sprite first, RuntimeAnimatorController controller)
    {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject("Rocket Ship");
        try
        {
            var sr = Get<SpriteRenderer>(root);
            sr.sprite = first;
            if (sr.sortingOrder < 3) sr.sortingOrder = 3;
            var group = Get<SortingGroup>(root);
            group.sortingOrder = 5;
            Get<Animator>(root).runtimeAnimatorController = controller;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root);
        }
    }

    // ---------- slicing ----------

    struct Cell { public string name; public RectInt art; public Vector2 pivotArt; }

    // Cells laid out on a grid (x0, y0 = top-left of the first cell, in art px, counted from the top of the sheet).
    static Cell[] Cells((string name, int col, int row)[] frames, int x0, int y0, int stepX, int stepY, int w, int h, float pivotX, float pivotY)
        => frames.Select(f => new Cell
        {
            name = f.name,
            art = new RectInt(x0 + f.col * stepX, y0 + f.row * stepY, w, h),
            pivotArt = new Vector2(pivotX, pivotY),
        }).ToArray();

    static Dictionary<string, Sprite> Slice(string path, Cell[] cells)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        int texH = tex.height;
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldIds = provider.GetSpriteRects().GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First().spriteID);
        var rects = cells.Select(c => new SpriteRect
        {
            name = c.name,
            spriteID = oldIds.TryGetValue(c.name, out var id) ? id : GUID.Generate(),
            // sheet rows are counted from the top; Unity rects from the bottom
            rect = new Rect(c.art.x * Px, texH - (c.art.y + c.art.height) * Px, c.art.width * Px, c.art.height * Px),
            alignment = SpriteAlignment.Custom,
            pivot = new Vector2(c.pivotArt.x / c.art.width, c.pivotArt.y / c.art.height),
        }).ToArray();
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
    }

    // ---------- animation ----------

    // One key per frame; the clip lasts exactly frames / fps, so every frame shows for the same time.
    static AnimationClip Clip(string path, Sprite[] frames, float fps, System.Type target)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = fps;
        var keys = frames.Select((s, i) => new ObjectReferenceKeyframe { time = i / fps, value = s }).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", target, "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorController Controller(string path, (string state, AnimationClip clip)[] states)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        AnimatorState first = null;
        for (int i = 0; i < states.Length; i++)
        {
            var st = sm.AddState(states[i].state, new Vector3(300, i * 70));
            st.motion = states[i].clip;
            first ??= st;
        }
        sm.defaultState = first;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void AddOrReplaceState(AnimatorController controller, string name, AnimationClip clip)
    {
        var sm = controller.layers[0].stateMachine;
        var existing = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        if (existing == null) existing = sm.AddState(name, new Vector3(700, 40 * sm.states.Length));
        existing.motion = clip;
        EditorUtility.SetDirty(controller);
    }

    // ---------- prefabs ----------

    static void TvPrefab(string path, string name, Sprite sprite, RuntimeAnimatorController controller)
    {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject(name);
        try
        {
            var sr = Get<SpriteRenderer>(root);
            sr.sprite = sprite;
            if (sr.sortingOrder < 3) sr.sortingOrder = 3;
            var group = Get<SortingGroup>(root);
            group.sortingOrder = 5; // Y-sorted with the dino, crabs and buildings
            var anim = root.GetComponent<Animator>();
            if (controller != null) { if (anim == null) anim = root.AddComponent<Animator>(); anim.runtimeAnimatorController = controller; }
            else if (anim != null) Object.DestroyImmediate(anim);
            // its foot: you can't walk through the stand
            var box = Get<BoxCollider2D>(root);
            box.size = new Vector2(0.4f, 0.2f);
            box.offset = new Vector2(0f, 0.1f);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root);
        }
    }

    // (Unity's missing-component "null" doesn't work with ??, so check properly.)
    static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static string Folder(string parent, string name)
    {
        string path = $"{parent}/{name}";
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        return path;
    }
}
