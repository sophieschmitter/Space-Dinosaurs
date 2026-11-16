using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slices "Healthbars.png" (10 px per art pixel) and builds the HUD's pixel-art bars in the HUD prefab:
///   Dino Health (top left): frame + heart, fill block + cap in green / yellow / red / purple (critical).
///   Robot bars (right after it, on the same top row as the resource trackers): robot icon, the robot's health (green / yellow / red) and,
///   in the thin bar underneath, the level timer (teal with a white leading pixel).
/// The old placeholder Health Bar and Timer Bar are switched off (not deleted).
/// Run from: Tools > Space Dinosaurs > Build HUD Bars. Safe to re-run.
/// </summary>
public static class HudBarBuilder
{
    const string SheetPath = "Assets/Sprites/Healthbars.png";
    const string HudPath = "Assets/Prefabs/HUD.prefab";
    const int Scale = 10;      // texture px per art pixel on this sheet
    const float U = 2f;        // HUD units per art pixel (the HUD is 640 x 360, the game view 320 x 180)
    // Frame lengths in art px (the sheet's are 104 and 133). Short enough that the row stays clear of the
    // resource trackers on 16:10 screens as well as 16:9.
    const int DinoLength = 92;
    const int RobotLength = 108;
    // Ends of each frame that are never stretched (left, right), in art px.
    static readonly Dictionary<string, Vector2Int> Borders = new()
    {
        { "DinoBar_Frame", new Vector2Int(15, 4) },  // heart circle + rounded end
        { "RobotBar_Frame", new Vector2Int(6, 6) },  // both bars' ends, incl. the timer bar's
    };

    // name, rect in art pixels (x, y from the top, w, h)
    static readonly (string name, RectInt r)[] Pieces =
    {
        ("DinoBar_Frame", new RectInt(13, 22, 104, 16)),
        ("DinoBar_Heart_Green", new RectInt(13, 42, 15, 16)),
        ("DinoBar_Heart_Yellow", new RectInt(13, 61, 15, 16)),
        ("DinoBar_Heart_Red", new RectInt(13, 80, 15, 16)),
        ("DinoBar_Heart_Purple", new RectInt(13, 99, 15, 16)),
        ("DinoBar_Fill_Green", new RectInt(34, 47, 6, 6)),  ("DinoBar_Cap_Green", new RectInt(44, 47, 2, 6)),
        ("DinoBar_Fill_Yellow", new RectInt(34, 66, 6, 6)), ("DinoBar_Cap_Yellow", new RectInt(44, 66, 2, 6)),
        ("DinoBar_Fill_Red", new RectInt(34, 85, 6, 6)),    ("DinoBar_Cap_Red", new RectInt(44, 85, 2, 6)),
        ("DinoBar_Fill_Purple", new RectInt(34, 104, 6, 6)),("DinoBar_Cap_Purple", new RectInt(44, 104, 2, 6)),
        ("RobotBar_Icon", new RectInt(211, 7, 27, 19)),
        ("RobotBar_Frame", new RectInt(158, 28, 133, 9)),
        ("RobotBar_Fill_Green", new RectInt(179, 44, 6, 5)),  ("RobotBar_Cap_Green", new RectInt(189, 44, 2, 5)),
        ("RobotBar_Fill_Yellow", new RectInt(179, 54, 6, 5)), ("RobotBar_Cap_Yellow", new RectInt(189, 54, 2, 5)),
        ("RobotBar_Fill_Red", new RectInt(179, 64, 6, 5)),    ("RobotBar_Cap_Red", new RectInt(189, 64, 2, 5)),
        ("Timer_Fill", new RectInt(162, 48, 6, 1)), ("Timer_Cap", new RectInt(170, 48, 1, 1)),
    };

    [MenuItem("Tools/Space Dinosaurs/Build HUD Bars")]
    public static void Build()
    {
        var s = Slice();
        if (s == null) return;

        var root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            // One row at the top, level with the resource trackers. Both frames are 3-sliced: their ends stay
            // pixel-exact and only the straight middle is shortened (DinoLength / RobotLength, in art px).
            // ---- dino health, top left ----
            var dino = Rect(root.transform, "Dino Health", new Vector2(0, 1), new Vector2(6, -2), new Vector2(DinoLength, 16));
            var dBar = Get<PixelBar>(dino.gameObject);
            dBar.fill = Img(dino, "Fill", s["DinoBar_Fill_Green"], new Vector2(15, -5), new Vector2(0, 6));
            dBar.cap = Img(dino, "Cap", s["DinoBar_Cap_Green"], new Vector2(15, -5), new Vector2(2, 6));
            Img(dino, "Frame", s["DinoBar_Frame"], Vector2.zero, new Vector2(DinoLength, 16), sliced: true);
            dBar.icon = Img(dino, "Heart", s["DinoBar_Heart_Green"], Vector2.zero, new Vector2(15, 16));
            dBar.maxLength = DinoLength - 18; dBar.capWidth = 2; dBar.unitsPerArtPixel = U; // opening: 15 px in, 3 px short of the end
            dBar.states = new[]
            {
                State(0.75f, s, "DinoBar", "Green", true), State(0.5f, s, "DinoBar", "Yellow", true),
                State(0.25f, s, "DinoBar", "Red", true),   State(0f, s, "DinoBar", "Purple", true),
            };
            var dHealth = Get<HealthBar>(dino.gameObject);
            dHealth.bar = dBar; dHealth.fill = null; dHealth.forRobot = false;

            // ---- robot icon + health + timer, right after the dino bar ----
            // Icon on the left (like the heart), then the frame; the tops of both bars' openings line up.
            const int IconW = 29;                       // icon (27) + 2 px gap
            float robotX = 6 + DinoLength + 4;
            var robot = Rect(root.transform, "Robot Bars", new Vector2(0, 1), new Vector2(robotX, 0), new Vector2(IconW + RobotLength, 19));
            var rHealthT = Rect(robot, "Robot Health", new Vector2(0, 1), Vector2.zero, new Vector2(IconW + RobotLength, 19), anchorTopLeft: true);
            var rBar = Get<PixelBar>(rHealthT.gameObject);
            rBar.fill = Img(rHealthT, "Fill", s["RobotBar_Fill_Green"], new Vector2(IconW + 2, -7), new Vector2(0, 5));
            rBar.cap = Img(rHealthT, "Cap", s["RobotBar_Cap_Green"], new Vector2(IconW + 2, -7), new Vector2(2, 5));
            rBar.maxLength = RobotLength - 4; rBar.capWidth = 2; rBar.unitsPerArtPixel = U;
            rBar.states = new[] { State(0.67f, s, "RobotBar", "Green", false), State(0.34f, s, "RobotBar", "Yellow", false), State(0f, s, "RobotBar", "Red", false) };
            var rHealth = Get<HealthBar>(rHealthT.gameObject);
            rHealth.bar = rBar; rHealth.fill = null; rHealth.forRobot = true;

            var timerT = Rect(robot, "Timer", new Vector2(0, 1), Vector2.zero, new Vector2(IconW + RobotLength, 19), anchorTopLeft: true);
            var tBar = Get<PixelBar>(timerT.gameObject);
            tBar.fill = Img(timerT, "Fill", s["Timer_Fill"], new Vector2(IconW + 6, -13), new Vector2(0, 1));
            tBar.cap = Img(timerT, "Cap", s["Timer_Cap"], new Vector2(IconW + 6, -13), new Vector2(1, 1));
            tBar.maxLength = RobotLength - 12; tBar.capWidth = 1; tBar.unitsPerArtPixel = U; tBar.value = 0f;
            tBar.states = new[] { new PixelBar.State { atLeast = 0f, fill = s["Timer_Fill"], cap = s["Timer_Cap"] } };

            Img(robot, "Frame", s["RobotBar_Frame"], new Vector2(IconW, -6), new Vector2(RobotLength, 9), sliced: true).transform.SetAsLastSibling();
            Img(robot, "Icon", s["RobotBar_Icon"], Vector2.zero, new Vector2(27, 19)).transform.SetAsLastSibling();

            var timer = root.GetComponent<LevelTimer>();
            if (timer != null) timer.timerBar = tBar;

            // Old placeholder bars off.
            foreach (var old in new[] { "Health Bar", "Timer Bar" })
            {
                var t = root.transform.Find(old);
                if (t != null) t.gameObject.SetActive(false);
            }
            dBar.Apply(); rBar.Apply(); tBar.Apply();
            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("HUD bars built: Dino Health, Robot Health, Timer");
    }

    static PixelBar.State State(float atLeast, Dictionary<string, Sprite> s, string prefix, string color, bool heart) => new PixelBar.State
    {
        atLeast = atLeast,
        fill = s[$"{prefix}_Fill_{color}"],
        cap = s[$"{prefix}_Cap_{color}"],
        icon = heart ? s[$"DinoBar_Heart_{color}"] : null,
    };

    // A RectTransform child (made if missing). Position and size in art pixels, top-left based.
    static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 posArt, Vector2 sizeArt, bool anchorTopLeft = false)
    {
        var t = parent.Find(name) as RectTransform;
        if (t == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            t = (RectTransform)go.transform;
            t.SetParent(parent, false);
        }
        t.anchorMin = t.anchorMax = anchorTopLeft ? new Vector2(0, 1) : anchor;
        t.pivot = new Vector2(0, 1);
        t.anchoredPosition = posArt * U;
        t.sizeDelta = sizeArt * U;
        t.localScale = Vector3.one;
        return t;
    }

    static Image Img(Transform parent, string name, Sprite sprite, Vector2 posArt, Vector2 sizeArt, bool sliced = false)
    {
        var t = Rect(parent, name, new Vector2(0, 1), posArt, sizeArt, anchorTopLeft: true);
        var img = Get<Image>(t.gameObject);
        img.sprite = sprite;
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        if (sliced)
        {
            // Make the unstretched ends come out at U HUD units per art pixel, like everything else.
            var canvas = parent.GetComponentInParent<Canvas>();
            float refPpu = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            img.pixelsPerUnitMultiplier = refPpu / (sprite.pixelsPerUnit * U / Scale);
            img.fillCenter = true;
        }
        img.preserveAspect = false;
        img.raycastTarget = false; // never blocks clicks
        img.color = Color.white;
        return img;
    }

    static Dictionary<string, Sprite> Slice()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError($"HUD bars: no sheet at {SheetPath}"); return null; }
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(SheetPath));
        int h = tex.height;
        Object.DestroyImmediate(tex);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldIds = provider.GetSpriteRects().GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);
        var rects = Pieces.Select(p => new SpriteRect
        {
            name = p.name,
            spriteID = oldIds.TryGetValue(p.name, out var id) ? id : GUID.Generate(),
            rect = new Rect(p.r.x * Scale, h - (p.r.y + p.r.height) * Scale, p.r.width * Scale, p.r.height * Scale),
            alignment = SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
            border = Borders.TryGetValue(p.name, out var b) ? new Vector4(b.x * Scale, 0, b.y * Scale, 0) : Vector4.zero,
        }).ToArray();
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(x => x.name);
    }

    static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
