using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds the crafting menu down the left side of the screen from "Build Boxes.PNG": three items (columns:
/// shield generator, turret, ice cream stand) and three pictures each (rows: available, pressed, can't afford).
/// Makes Assets/Prefabs/Crafting Menu.prefab (its own canvas, laid out like the HUD) and puts it in
/// Level One, Level Two, Level Three and Training, adding an EventSystem to scenes that don't have one.
/// Every item costs 3 Nickel and 1 Iridium for now: change it on each button's Craft Button component.
/// Run from: Tools > Space Dinosaurs > Build Crafting Menu. Safe to re-run (sprite IDs and prefab kept).
/// </summary>
public static class CraftMenuBuilder
{
    const string SheetPath = "Assets/Sprites/Build Boxes.PNG";
    const string PrefabPath = "Assets/Prefabs/Crafting Menu.prefab";
    const string HudPath = "Assets/Prefabs/HUD.prefab";
    const int Scale = 10;                 // texture px per art px
    const int CellW = 68, CellH = 45;     // one button, art px
    const int Gap = 4;                    // between buttons, art px
    const float Units = 1f;               // HUD units per art px (the HUD bars use 2: these are half that size)

    static readonly string[] Items = { "Shield Generator", "Turret", "Ice Cream Stand" };
    // Earlier item names -> current ones (renamed in place, so sprites and scene references survive).
    static readonly (string from, string to)[] Renames = { ("Tower", "Turret"), ("Shop", "Ice Cream Stand") };
    static readonly int[] ColumnX = { 160, 960, 1760 };   // texture px, left edge of each column
    static readonly int[] RowTop = { 160, 640, 1120 };    // texture px from the top: available, pressed, unaffordable
    static readonly string[] States = { "Available", "Pressed", "Unaffordable" };

    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level One.unity", "Assets/Scenes/Level Two.unity",
        "Assets/Scenes/Level Three.unity", "Assets/Scenes/Training.unity",
    };

    static string SpriteName(int item, int state) => $"Craft_{Items[item].Replace(" ", "")}_{States[state]}";

    [MenuItem("Tools/Space Dinosaurs/Build Crafting Menu")]
    public static void Build()
    {
        var sprites = Slice();
        if (sprites == null) return;
        BuildPrefab(sprites);
        foreach (var scene in Scenes) AddToScene(scene);
        Debug.Log("Crafting menu: built and added to " + string.Join(", ", Scenes.Select(System.IO.Path.GetFileNameWithoutExtension)));
    }

    static System.Collections.Generic.Dictionary<string, Sprite> Slice()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError($"Crafting menu: no sheet at {SheetPath}"); return null; }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var oldIds = provider.GetSpriteRects().GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First().spriteID);
        foreach (var (from, to) in Renames)
        foreach (var state in States)
        {
            string a = $"Craft_{from.Replace(" ", "")}_{state}", b = $"Craft_{to.Replace(" ", "")}_{state}";
            if (oldIds.TryGetValue(a, out var keep) && !oldIds.ContainsKey(b)) oldIds[b] = keep;
        }
        int texH = 2560;
        var rects = new System.Collections.Generic.List<SpriteRect>();
        for (int i = 0; i < Items.Length; i++)
        for (int s = 0; s < States.Length; s++)
        {
            string name = SpriteName(i, s);
            rects.Add(new SpriteRect
            {
                name = name,
                spriteID = oldIds.TryGetValue(name, out var id) ? id : GUID.Generate(),
                rect = new Rect(ColumnX[i], texH - RowTop[s] - CellH * Scale, CellW * Scale, CellH * Scale),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
            });
        }
        provider.SetSpriteRects(rects.ToArray());
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        if (sprites.Count < rects.Count) // first slice: the new sprites can take one more import to show up
        {
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        }
        if (sprites.Count < rects.Count) { Debug.LogError("Crafting menu: couldn't load the sliced sprites; run this again."); return null; }
        return sprites;
    }

    static void BuildPrefab(System.Collections.Generic.Dictionary<string, Sprite> sprites)
    {
        // Resource icons and the number font come from the HUD's counters, so they match.
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var counters = hud != null ? hud.GetComponentsInChildren<ResourceCounter>(true) : new ResourceCounter[0];
        Image Icon(ResourceType t) // the counter's icon (its sprite and tint)
        {
            var c = counters.FirstOrDefault(x => x.resource == t);
            var icon = c != null ? c.transform.Find("Icon") : null;
            return icon != null ? icon.GetComponent<Image>() : null;
        }
        var anyLabel = counters.Select(c => c.label).FirstOrDefault(l => l != null);
        Font font = anyLabel != null ? anyLabel.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Crafting Menu", typeof(RectTransform));
        try
        {
            var canvas = Get<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvas.pixelPerfect = true;
            var scaler = Get<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(640, 360);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Get<GraphicRaycaster>(root);

            // Column on the left edge, centred top to bottom.
            float h = (Items.Length * CellH + (Items.Length - 1) * Gap) * Units;
            var list = Rect(root.transform, "Buttons", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6 * Units, 0), new Vector2(CellW * Units, h));

            foreach (var (from, to) in Renames)
            {
                var old = list.Find(from);
                if (old != null && list.Find(to) == null) old.name = to;
            }

            for (int i = 0; i < Items.Length; i++)
            {
                var b = Rect(list, Items[i], new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -i * (CellH + Gap) * Units), new Vector2(CellW, CellH) * Units);
                var img = Get<Image>(b.gameObject);
                img.sprite = sprites[SpriteName(i, 0)];
                img.type = Image.Type.Simple;
                img.preserveAspect = false;
                var button = Get<Button>(b.gameObject);
                button.transition = Selectable.Transition.None;
                button.targetGraphic = img;
                var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav; // keep the gamepad on the dino

                var craft = Get<CraftButton>(b.gameObject);
                craft.itemName = Items[i];
                craft.building = AssetDatabase.LoadAssetAtPath<Building>($"Assets/Prefabs/Buildings/{Items[i]}.prefab"); // Build Buildings makes these
                craft.image = img;
                craft.available = sprites[SpriteName(i, 0)];
                craft.pressed = sprites[SpriteName(i, 1)];
                craft.unaffordable = sprites[SpriteName(i, 2)];
                if (!existing) { craft.nickelCost = 3; craft.iridiumCost = 1; }

                // Cost on the empty right half of the box: icon + number, nickel above iridium.
                craft.nickelLabel = CostRow(b, "Nickel Cost", Icon(ResourceType.Nickel), font, 5, craft.nickelCost);
                craft.iridiumLabel = CostRow(b, "Iridium Cost", Icon(ResourceType.Iridium), font, 24, craft.iridiumCost);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    // A 16 x 16 icon at (36, -top) art px with the number to its right.
    static Text CostRow(Transform button, string name, Image icon, Font font, int top, int amount)
    {
        var row = Rect(button, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -top) * Units, new Vector2(28, 16) * Units);
        var iconRect = Rect(row, "Icon", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(16, 16) * Units);
        var iconImg = Get<Image>(iconRect.gameObject);
        iconImg.sprite = icon != null ? icon.sprite : null;
        iconImg.color = icon != null ? icon.color : Color.white;
        iconImg.raycastTarget = false;
        iconImg.enabled = icon != null;
        var textRect = Rect(row, "Count", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0) * Units, new Vector2(12, 16) * Units);
        var text = Get<Text>(textRect.gameObject);
        text.font = font;
        text.fontSize = 12;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = amount.ToString();
        return text;
    }

    static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var t = parent.Find(name);
        if (t == null) { t = new GameObject(name, typeof(RectTransform)).transform; t.SetParent(parent, false); }
        var r = (RectTransform)t;
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = pivot;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        r.localScale = Vector3.one;
        return r;
    }

    // GetComponent returns a fake null in the Editor, so check explicitly rather than with ??.
    static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    static void AddToScene(string path)
    {
        var scene = EditorSceneManager.GetSceneByPath(path);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            opened = true;
        }
        bool wasDirty = scene.isDirty;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var roots = scene.GetRootGameObjects();

        if (!roots.Any(g => PrefabUtility.GetCorrespondingObjectFromSource(g) == prefab))
        {
            var menu = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            menu.name = "Crafting Menu";
        }
        if (!roots.Any(g => g.GetComponentInChildren<EventSystem>(true) != null))
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (module != null) es.AddComponent(module); else es.AddComponent<StandaloneInputModule>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(es, scene);
        }

        if (wasDirty) Debug.LogWarning($"Crafting menu: {path} had unsaved changes, so it was left unsaved. Save it yourself.");
        else EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }
}
