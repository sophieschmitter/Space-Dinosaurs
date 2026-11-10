using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Slices the dino sprite sheets in Assets/Sprites (one column = one animation, frames run top to bottom),
/// builds an AnimationClip per column, and puts every clip as a state in one Animator Controller (Dino.controller).
/// Run from: Tools > Space Dinosaurs > Build Dino Animations. Safe to re-run: sprite IDs, the controller asset
/// and the Dino prefab (with its movement script and collider) are kept; only the clips and states are refreshed.
///
/// All sheets are drawn at 10 texture px per art pixel. Every frame's pivot sits between the dino's feet,
/// so switching between clips from different sheets never makes the dino jump.
/// </summary>
public static class DinoSpriteSheetBuilder
{
    const string OutFolder = "Assets/Animations/Dino";
    const string ControllerPath = OutFolder + "/Dino.controller";
    const string PrefabPath = "Assets/Prefabs/Dino.prefab";
    const int PixelsPerUnit = 160;     // 10 px per art pixel * 16 art px per unit (matches PixelArtImporter)
    const float FramesPerSecond = 8f;
    const int RowPitch = 480;          // rows are 48 art px apart
    const int FirstFeetY = 480;        // the feet of row 0 sit 480 px from the top; row r at 480 * (r + 1)
    const string DefaultState = "RIdle";

    /// <summary>One animation: a column of frames.</summary>
    class Clip
    {
        public string name;            // clip and state name
        public string spriteName;      // sprite name prefix (kept stable so existing references survive renames)
        public string SpritePrefix => spriteName ?? name;
        public bool loop = true;
        public int x, width = 480;     // column position in texture px
        public int above = 430;        // frame rect reaches this far above the feet...
        public int height = 480;       // ...and is this tall
        public float pivotX = 0.5f;    // feet position across the frame (0-1)
        public int[] rows = { 0, 1, 2, 3, 4 };
        public float PivotY => (height - above) / (float)height;
    }

    class Sheet
    {
        public string path;
        public List<Clip> clips;
        public int minPixels = 2000;   // cells with fewer opaque pixels are treated as empty (stray dust)
        public bool spritesOnly;       // slice only; no clips or Animator states (used for effects)
    }

    // Columns of the two 12 x 5 walk sheets, left to right: clip name, sprite name, loops.
    // Rename the first column freely and re-run the menu item; leave the sprite names alone.
    static readonly (string name, string sprite, bool loop)[] WalkColumns =
    {
        ("RIdle",       "Side_Idle",  true),   // col 0  facing right
        ("RWalk",       "Side_Walk",  true),   // col 1
        ("LIdle",       "Side_Idle2", true),   // col 2  facing left
        ("LWalk",       "Side_Walk2", true),   // col 3
        ("Front_Idle",  "Front_Idle", true),   // col 4
        ("Front_Walk",  "Front_Walk", true),   // col 5
        ("Back_Idle",   "Back_Idle",  true),   // col 6
        ("Back_Walk",   "Back_Walk",  true),   // col 7 (no frame in the top row)
        ("RDamage",     "Huff",       true),   // col 8
        ("RDeath",      "Death",      false),  // col 9
        ("LDamage",     "Huff2",      true),   // col 10
        ("LDeath",      "Death2",     false),  // col 11
    };

    static List<Clip> WalkSheet(string prefix) =>
        WalkColumns.Select((c, i) => new Clip { name = prefix + c.name, spriteName = prefix + c.sprite, loop = c.loop, x = i * 480 }).ToList();

    // Earlier clip names -> current ones. Old clip assets are renamed (keeping their IDs) or, if the new one
    // already exists, removed as duplicates.
    static readonly (string from, string to)[] Renames =
    {
        ("Side_Idle", "RIdle"), ("Side_Walk", "RWalk"), ("Side_Idle2", "LIdle"), ("Side_Walk2", "LWalk"),
        ("Huff", "RDamage"), ("Death", "RDeath"), ("Huff2", "LDamage"), ("Death2", "LDeath"),
        ("Drill_Side_Idle", "Drill_RIdle"), ("Drill_Side_Walk", "Drill_RWalk"), ("Drill_Side_Idle2", "Drill_LIdle"),
        ("Drill_Side_Walk2", "Drill_LWalk"), ("Drill_Huff", "Drill_RDamage"), ("Drill_Death", "Drill_RDeath"),
        ("Drill_Huff2", "Drill_LDamage"), ("Drill_Death2", "Drill_LDeath"),
        ("Drill_Attack_Side", "Drill_Attack_R"), ("Drill_Attack_SideLeft", "Drill_Attack_L"),
        ("Drill_Grey_Side", "Drill_Grey_R"), ("Drill_Grey_SideLeft", "Drill_Grey_L"),
    };

    // Effects sheet (Assets/Sprites/Dino Effects.png), same 480 px cells and feet line as the walk sheets:
    // col 0 = shadow (hangs 4 art px below the feet), col 1 = sweat dots flying off the back facing right
    // (3 frames), col 2 = the same mirrored for facing left. Taken from your shadow/sweat reference page.
    const string EffectsPath = "Assets/Sprites/Dino Effects.png";
    static List<Clip> EffectsSheet() => new()
    {
        new Clip { name = "FX_Shadow", x = 0, rows = new[] { 0 } },
        new Clip { name = "FX_SweatR", x = 480, rows = new[] { 0, 1, 2 } },
        new Clip { name = "FX_SweatL", x = 960, rows = new[] { 0, 1, 2 } },
    };

    // Attack sheet: 4 columns, 640 px wide. Row 0 is a grey copy of the drill idle pose; rows 1-4 are the attack.
    // Front frames hang 1-2 art px below the feet and back frames reach 4 px higher, so their rects are shifted.
    static List<Clip> AttackSheet()
    {
        var dirs = new[]
        {
            // name,  sprite name, x,    pivotX,        above (geometry measured from the art)
            ("R",     "Side",      0,    240 / 640f,    430),
            ("Front", "Front",     640,  340 / 640f,    420),
            ("Back",  "Back",      1280, 300 / 640f,    480),
            ("L",     "SideLeft",  1920, 400 / 640f,    430), // exact mirror of R
        };
        var list = new List<Clip>();
        foreach (var (dir, spr, x, px, above) in dirs)
        {
            list.Add(new Clip { name = "Drill_Attack_" + dir, spriteName = "Drill_Attack_" + spr, loop = false, x = x, width = 640, above = above, pivotX = px, rows = new[] { 1, 2, 3, 4 } });
            list.Add(new Clip { name = "Drill_Grey_" + dir, spriteName = "Drill_Grey_" + spr, loop = true, x = x, width = 640, above = above, pivotX = px, rows = new[] { 0 } });
        }
        return list;
    }

    static List<Sheet> Sheets() => new()
    {
        new Sheet { path = "Assets/Sprites/Dinosaur Spritework.PNG", clips = WalkSheet("") },
        new Sheet { path = "Assets/Sprites/Dinosaur Spritework - 2.png", clips = WalkSheet("Drill_") },
        new Sheet { path = "Assets/Sprites/Dinosaur Attacks and Actions.PNG", clips = AttackSheet() },
        new Sheet { path = EffectsPath, clips = EffectsSheet(), minPixels = 1, spritesOnly = true },
    };

    [MenuItem("Tools/Space Dinosaurs/Build Dino Animations")]
    public static void Build()
    {
        EnsureFolder(OutFolder);
        MigrateOldClipNames();
        var clips = new List<AnimationClip>();
        var effects = new Dictionary<string, Sprite[]>();
        var report = new List<string>();

        foreach (var sheet in Sheets())
        {
            if (!File.Exists(sheet.path)) { report.Add($"{Path.GetFileName(sheet.path)}: missing, skipped"); continue; }

            var frames = FindFrames(sheet);
            SliceSheet(sheet, frames);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(sheet.path).OfType<Sprite>().ToDictionary(s => s.name);

            int made = 0;
            foreach (var clip in sheet.clips)
            {
                var seq = frames.Where(f => f.clip == clip).OrderBy(f => f.row).Select(f => sprites[f.name]).ToList();
                if (seq.Count == 0) continue;
                if (sheet.spritesOnly) { effects[clip.name] = seq.ToArray(); continue; }
                clips.Add(BuildClip(clip.name, clip.loop, seq));
                made++;
            }
            report.Add($"{Path.GetFileName(sheet.path)}: {frames.Count} frames, {made} clips");
        }

        var controller = BuildController(clips);
        HookUpPrefab(controller, clips, effects);
        AssetDatabase.SaveAssets();
        Debug.Log("Dino animations: " + string.Join("; ", report) + $". {clips.Count} states in {ControllerPath}");
    }

    // ---------- slicing ----------

    class Frame { public Clip clip; public int row; public string name; public RectInt rect; }

    // Unity sprite rects use a bottom-left origin.
    static RectInt FrameRect(Clip c, int row, int texHeight)
    {
        int top = FirstFeetY + row * RowPitch - c.above;
        return new RectInt(c.x, texHeight - top - c.height, c.width, c.height);
    }

    // Reads the PNG straight from disk so the texture doesn't need Read/Write enabled; skips empty cells.
    static List<Frame> FindFrames(Sheet sheet)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(sheet.path));
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        Object.DestroyImmediate(tex);

        var frames = new List<Frame>();
        foreach (var clip in sheet.clips)
        foreach (int row in clip.rows)
        {
            var r = FrameRect(clip, row, h);
            if (r.xMin < 0 || r.yMin < 0 || r.xMax > w || r.yMax > h) continue;
            int count = 0;
            for (int y = r.yMin; y < r.yMax && count < sheet.minPixels; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (px[y * w + x].a > 0) count++;
            if (count >= sheet.minPixels) // ignore cells holding only stray dust
                frames.Add(new Frame { clip = clip, row = row, name = $"{clip.SpritePrefix}_{row}", rect = r });
        }
        return frames;
    }

    static void SliceSheet(Sheet sheet, List<Frame> frames)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(sheet.path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 8192; // 2048 default downscales and blurs big sheets

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        // Keep IDs of sprites that already exist with the same name so references survive re-runs.
        var oldIds = provider.GetSpriteRects().GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First().spriteID);

        var rects = frames.Select(f => new SpriteRect
        {
            name = f.name,
            spriteID = oldIds.TryGetValue(f.name, out var id) ? id : GUID.Generate(),
            rect = new Rect(f.rect.x, f.rect.y, f.rect.width, f.rect.height),
            alignment = SpriteAlignment.Custom,
            pivot = new Vector2(f.clip.pivotX, f.clip.PivotY),
        }).ToArray();

        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
    }

    // ---------- clips, controller, prefab ----------

    static AnimationClip BuildClip(string name, bool loop, List<Sprite> frames)
    {
        string path = ClipPath(name);
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

    static string StateName(AnimationClip clip) => clip.name.StartsWith("Dino_") ? clip.name.Substring(5) : clip.name;

    // A clip lives at "Dino_<name>.anim", unless it was renamed to just "<name>.anim" by hand; either is reused.
    static string ClipPath(string name)
    {
        string plain = $"{OutFolder}/{name}.anim";
        return File.Exists(plain) ? plain : $"{OutFolder}/Dino_{name}.anim";
    }

    static void MigrateOldClipNames()
    {
        foreach (var (from, to) in Renames)
        {
            string oldPath = $"{OutFolder}/Dino_{from}.anim";
            if (!File.Exists(oldPath)) continue;
            string newPath = ClipPath(to);
            if (File.Exists(newPath)) AssetDatabase.DeleteAsset(oldPath);   // duplicate of a clip you already renamed
            else AssetDatabase.MoveAsset(oldPath, newPath);                 // keeps the clip's ID
        }
    }

    // Refreshes the states in place so the controller keeps its asset ID (the prefab keeps pointing at it).
    static AnimatorController BuildController(List<AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);

        for (int i = 0; i < clips.Count; i++)
        {
            var state = sm.AddState(StateName(clips[i]), new Vector3(300 + (i % 4) * 240, i / 4 * 60));
            state.motion = clips[i];
            if (state.name == DefaultState) sm.defaultState = state;
        }
        if (sm.defaultState == null && sm.states.Length > 0) sm.defaultState = sm.states[0].state;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    // Creates the Dino prefab the first time; afterwards only re-points its Animator and effect sprites,
    // leaving everything else (movement script settings, collider, Rigidbody) alone.
    static void HookUpPrefab(AnimatorController controller, List<AnimationClip> clips, Dictionary<string, Sprite[]> effects)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var go = new GameObject("Dino");
            var idle = clips.FirstOrDefault(c => StateName(c) == DefaultState) ?? clips[0];
            go.AddComponent<SpriteRenderer>().sprite = AnimationUtility.GetObjectReferenceCurve(idle,
                EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"))[0].value as Sprite;
            go.AddComponent<Animator>();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        // Shadow (just under the dino) and sweat (just over it) as child sprites sharing the dino's feet pivot.
        var body = root.GetComponent<SpriteRenderer>();
        var shadow = Child(root, "Shadow", body, -1);
        var sweat = Child(root, "Sweat", body, +1);
        if (effects.TryGetValue("FX_Shadow", out var sh)) shadow.sprite = sh[0];
        var dino = root.GetComponent<DinoController>();
        if (dino != null)
        {
            dino.shadow = shadow;
            dino.sweat = sweat;
            if (effects.TryGetValue("FX_SweatR", out var r)) dino.sweatRight = r;
            if (effects.TryGetValue("FX_SweatL", out var l)) dino.sweatLeft = l;
        }
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static SpriteRenderer Child(GameObject root, string name, SpriteRenderer body, int orderOffset)
    {
        var t = root.transform.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
        }
        t.localPosition = Vector3.zero;
        var sr = t.GetComponent<SpriteRenderer>();
        if (sr == null) sr = t.gameObject.AddComponent<SpriteRenderer>();
        // Body sits at order 3 so the shadow (2) stays above the walls/floor (1) and below the dino.
        if (body.sortingOrder < 3) body.sortingOrder = 3;
        sr.sortingLayerID = body.sortingLayerID;
        sr.sortingOrder = body.sortingOrder + orderOffset;
        sr.enabled = false; // the controller decides when each is shown
        return sr;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
