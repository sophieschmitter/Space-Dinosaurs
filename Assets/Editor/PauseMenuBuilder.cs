using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds the placeholder in-game menu (Assets/Prefabs/Pause Menu.prefab): a MENU block in the bottom-right
/// corner that pauses the game and opens a panel with Resume, Controls and Level Select, in the title screen's
/// colours and the Press Start 2P font. Puts it in every scene except the title screen (adding an EventSystem
/// where there isn't one) and adds Level Select to the build's scene list so the button can load it.
/// Run from: Tools > Space Dinosaurs > Build Pause Menu. Safe to re-run.
/// </summary>
public static class PauseMenuBuilder
{
    const string PrefabPath = "Assets/Prefabs/Pause Menu.prefab";
    const string FontPath = "Assets/UI/Fonts/PressStart2P-Regular.ttf";
    const string LevelSelectPath = "Assets/Scenes/Level Select.unity";
    static readonly string[] Scenes =
    {
        "Assets/Scenes/Level One.unity", "Assets/Scenes/Level Two.unity", "Assets/Scenes/Level Three.unity",
        "Assets/Scenes/Training.unity", LevelSelectPath,
    };
    const int FontSize = 8;

    static readonly Color Panel = new Color32(0x1E, 0x1A, 0x33, 0xF0);
    static readonly Color Border = new Color32(0xE0, 0x47, 0x9E, 0xFF);
    static readonly Color TextColor = new Color32(0xEC, 0xE6, 0xFF, 0xFF);
    static readonly Color Gold = new Color32(0xF5, 0xC5, 0x42, 0xFF);

    static readonly (string action, string keys)[] Controls =
    {
        ("MOVE", "WASD / LEFT STICK"),
        ("SPRINT", "SHIFT / A"),
        ("ATTACK", "SPACE OR CLICK / X"),
        ("SWAP WEAPON", "Q / Y"),
        ("BUILD", "CLICK A CRAFT BUTTON"),
        ("MENU", "ESC / START"),
    };

    [MenuItem("Tools/Space Dinosaurs/Build Pause Menu")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null) { Debug.LogError($"Pause menu: no font at {FontPath}"); return; }
        BuildPrefab(font);
        AddLevelSelectToBuild();
        foreach (var path in Scenes) AddToScene(path);
    }

    static void BuildPrefab(Font font)
    {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Pause Menu", typeof(RectTransform));
        try
        {
            var canvas = Get<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15; // over the HUD and crafting menu, under the dialogue box
            canvas.pixelPerfect = true;
            var scaler = Get<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(640, 360);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Get<GraphicRaycaster>(root);
            var menu = Get<PauseMenu>(root);

            // The menu itself (hidden until opened).
            var open = Stretch(root.transform, "Menu Root");
            var backdrop = Get<Image>(Stretch(open, "Backdrop").gameObject);
            backdrop.color = new Color(0f, 0f, 0f, 0.55f); // also stops clicks reaching the game

            var main = Rect(open, "Menu Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 150));
            Panelled(main.gameObject);
            Title(main, "PAUSED", font);
            var resume = MenuButton(main, "Resume", "RESUME", -44, font);
            var controls = MenuButton(main, "Controls", "CONTROLS", -74, font);
            var levelSelect = MenuButton(main, "Level Select", "LEVEL SELECT", -104, font);

            var help = Rect(open, "Controls Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 190));
            Panelled(help.gameObject);
            Title(help, "CONTROLS", font);
            var names = Label(Rect(help, "Actions", new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -40), new Vector2(110, 110)), font, Gold, TextAnchor.UpperLeft);
            var keys = Label(Rect(help, "Keys", new Vector2(0, 1), new Vector2(0, 1), new Vector2(134, -40), new Vector2(170, 110)), font, TextColor, TextAnchor.UpperLeft);
            names.lineSpacing = keys.lineSpacing = 2f;
            names.text = string.Join("\n", Controls.Select(c => c.action));
            keys.text = string.Join("\n", Controls.Select(c => c.keys));
            var back = MenuButton(help, "Back", "BACK", -156, font);

            // The corner button (last, so it stays clickable over the backdrop and closes the menu again).
            var corner = Rect(root.transform, "Menu Button", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-6, 6), new Vector2(48, 18));
            corner.SetAsLastSibling();
            var cornerImg = Panelled(corner.gameObject);
            cornerImg.raycastTarget = true;
            var cornerButton = Get<Button>(corner.gameObject);
            cornerButton.targetGraphic = cornerImg;
            var colors = cornerButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            cornerButton.colors = colors;
            var nav = cornerButton.navigation; nav.mode = Navigation.Mode.None; cornerButton.navigation = nav; // keep the stick for the dino
            var cornerLabel = Label(Stretch(corner, "Label"), font, TextColor, TextAnchor.MiddleCenter);
            cornerLabel.text = "MENU";

            Hook(resume, menu.Close);
            Hook(controls, menu.ShowControls);
            Hook(levelSelect, menu.GoToLevelSelect);
            Hook(back, menu.ShowMain);
            menu.menuButton = cornerButton;
            menu.menuRoot = open.gameObject;
            menu.mainPanel = main.gameObject;
            menu.controlsPanel = help.gameObject;
            menu.firstMainButton = resume;
            menu.firstControlsButton = back;
            menu.levelSelectButton = levelSelect;
            help.gameObject.SetActive(false);
            open.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    // A placeholder button in the title screen's style (MenuButtonStyle does the highlight).
    static Button MenuButton(RectTransform panel, string name, string text, float y, Font font)
    {
        var r = Rect(panel, name, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(170, 22));
        var img = Panelled(r.gameObject);
        img.raycastTarget = true;
        var button = Get<Button>(r.gameObject);
        button.targetGraphic = img;
        button.transition = Selectable.Transition.None;
        var label = Label(Stretch(r, "Label"), font, TextColor, TextAnchor.MiddleCenter);
        label.text = text;
        var style = Get<MenuButtonStyle>(r.gameObject);
        style.background = img;
        style.border = r.GetComponent<Outline>();
        style.label = label;
        return button;
    }

    static void Hook(Button b, UnityEngine.Events.UnityAction action)
    {
        for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
        UnityEventTools.AddPersistentListener(b.onClick, action);
    }

    static void Title(RectTransform panel, string text, Font font)
    {
        var t = Label(Rect(panel, "Title", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(200, FontSize * 2)), font, Gold, TextAnchor.UpperCenter);
        t.fontSize = FontSize * 2;
        t.text = text;
    }

    static Image Panelled(GameObject go)
    {
        var img = Get<Image>(go);
        img.color = Panel;
        img.raycastTarget = false;
        var outline = Get<Outline>(go);
        outline.effectColor = Border;
        outline.effectDistance = new Vector2(2, -2);
        outline.useGraphicAlpha = false;
        return img;
    }

    static Text Label(RectTransform r, Font font, Color color, TextAnchor align)
    {
        var t = Get<Text>(r.gameObject);
        t.font = font;
        t.fontSize = FontSize;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void AddLevelSelectToBuild()
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (list.Any(s => s.path == LevelSelectPath)) return;
        list.Add(new EditorBuildSettingsScene(LevelSelectPath, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("Pause menu: added Level Select to the build's scene list.");
    }

    static void AddToScene(string path)
    {
        var scene = EditorSceneManager.GetSceneByPath(path);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened = true; }
        bool wasDirty = scene.isDirty;
        var roots = scene.GetRootGameObjects();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = roots.FirstOrDefault(g => PrefabUtility.GetCorrespondingObjectFromSource(g) == prefab);
        if (instance == null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "Pause Menu";
        }
        // Level select already uses Esc / B for "back": there only the button opens the menu.
        var menu = instance.GetComponent<PauseMenu>();
        bool isLevelSelect = path == LevelSelectPath;
        if (menu.keyboardOpens == isLevelSelect) { menu.keyboardOpens = !isLevelSelect; PrefabUtility.RecordPrefabInstancePropertyModifications(menu); }
        if (!roots.Any(g => g.GetComponentInChildren<EventSystem>(true) != null))
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (module != null) es.AddComponent(module); else es.AddComponent<StandaloneInputModule>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(es, scene);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (wasDirty) Debug.LogWarning($"Pause menu: added to {path}, which had unsaved changes, so it was left unsaved. Save it yourself.");
        else EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }

    static RectTransform Stretch(Transform parent, string name)
    {
        var r = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return r;
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
}
