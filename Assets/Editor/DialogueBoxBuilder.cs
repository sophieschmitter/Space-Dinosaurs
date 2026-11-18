using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the placeholder dialogue box (Assets/Prefabs/Dialogue Box.prefab): a text box along the bottom of the
/// screen and a portrait frame on the bottom right, in the title screen's colours, with the Press Start 2P pixel
/// font. Puts it in the Training scene with two example DialogueTriggers (one at the start, one in the corridor
/// before the meteorite room) holding placeholder lines to replace.
/// Run from: Tools > Space Dinosaurs > Build Dialogue Box. Safe to re-run (keeps the triggers' lines).
/// </summary>
public static class DialogueBoxBuilder
{
    const string PrefabPath = "Assets/Prefabs/Dialogue Box.prefab";
    const string FontPath = "Assets/UI/Fonts/PressStart2P-Regular.ttf";
    const string ScenePath = "Assets/Scenes/Training.unity";

    // HUD units (the canvas is 640 x 360, like the HUD).
    const float Margin = 8f, BoxHeight = 88f, PortraitSize = 88f, Gap = 6f;
    const int FontSize = 8; // Press Start 2P is drawn on an 8 px grid: keep sizes a multiple of 8

    static readonly Color Panel = new Color32(0x1E, 0x1A, 0x33, 0xF0);
    static readonly Color Border = new Color32(0xE0, 0x47, 0x9E, 0xFF);
    static readonly Color TextColor = new Color32(0xEC, 0xE6, 0xFF, 0xFF);
    static readonly Color Gold = new Color32(0xF5, 0xC5, 0x42, 0xFF);

    [MenuItem("Tools/Space Dinosaurs/Build Dialogue Box")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null) { Debug.LogError($"Dialogue box: no font at {FontPath}"); return; }
        BuildPrefab(font);
        AddToTraining();
    }

    static void BuildPrefab(Font font)
    {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Dialogue Box", typeof(RectTransform));
        try
        {
            var canvas = Get<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20; // above the HUD and crafting menu
            canvas.pixelPerfect = true;
            var scaler = Get<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(640, 360);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Get<GraphicRaycaster>(root);

            var talk = Stretch(root.transform, "Talking");
            // Catches clicks anywhere while talking (they move the dialogue on instead of reaching the HUD).
            var blocker = Get<Image>(Stretch(talk, "Click Blocker").gameObject);
            blocker.color = new Color(0, 0, 0, 0);

            var box = Rect(talk, "Box", new Vector2(0, 0), new Vector2(0, 0), new Vector2(Margin, Margin),
                new Vector2(640 - Margin * 2 - PortraitSize - Gap, BoxHeight));
            Panelled(box.gameObject);
            var speaker = Label(Rect(box, "Speaker", new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -9), new Vector2(300, FontSize)), font, Gold, TextAnchor.UpperLeft);
            var textRect = Rect(box, "Text", new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -25), new Vector2(box.sizeDelta.x - 20, BoxHeight - 35));
            var text = Label(textRect, font, TextColor, TextAnchor.UpperLeft);
            text.lineSpacing = 1.6f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            var next = Label(Rect(box, "Next", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 7), new Vector2(FontSize, FontSize)), font, Gold, TextAnchor.LowerRight);
            next.text = ">";

            var frame = Rect(talk, "Portrait Frame", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Margin, Margin), new Vector2(PortraitSize, PortraitSize));
            Panelled(frame.gameObject);
            var portrait = Get<Image>(Inset(frame, "Portrait", 4).gameObject);
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            var letter = Label(Inset(frame, "Placeholder Letter", 4), font, Border, TextAnchor.MiddleCenter);
            letter.fontSize = FontSize * 4;
            letter.text = "?";

            var dialogue = Get<DialogueBox>(root);
            dialogue.root = talk.gameObject;
            dialogue.speakerLabel = speaker;
            dialogue.textLabel = text;
            dialogue.nextIndicator = next.gameObject;
            dialogue.portrait = portrait;
            dialogue.portraitLetter = letter;
            talk.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    static void Panelled(GameObject go)
    {
        var img = Get<Image>(go);
        img.color = Panel;
        img.raycastTarget = false;
        var outline = Get<Outline>(go);
        outline.effectColor = Border;
        outline.effectDistance = new Vector2(2, -2);
        outline.useGraphicAlpha = false;
    }

    static Text Label(RectTransform r, Font font, Color color, TextAnchor align)
    {
        var t = Get<Text>(r.gameObject);
        t.font = font;
        t.fontSize = FontSize;
        t.color = color;
        t.alignment = align;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void AddToTraining()
    {
        var scene = EditorSceneManager.GetSceneByPath(ScenePath);
        bool opened = false;
        if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
        if (scene.isDirty && !opened) { Debug.LogWarning("Dialogue box: Training has unsaved changes; save it and run this again."); return; }
        var roots = scene.GetRootGameObjects();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!roots.Any(g => PrefabUtility.GetCorrespondingObjectFromSource(g) == prefab))
            ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).name = "Dialogue Box";

        // Example triggers with placeholder lines (kept as they are if they already exist, so edits survive).
        Trigger(scene, roots, "Intro Dialogue", null, Vector2.zero, new[]
        {
            Line("Guide", "Welcome to training! This is placeholder dialogue: write the real lines on the Intro Dialogue object."),
            Line("Guide", "Move with WASD or the left stick. Hold Shift (or A) to sprint."),
            Line("Guide", "Space, X on the controller or a click attacks. Q or Y swaps between the drill and the gun."),
        });
        Trigger(scene, roots, "Meteorite Room Dialogue", new Vector2(16f, 1.15f), new Vector2(1f, 2.3f), new[]
        {
            Line("Guide", "Meteorites ahead. Drill through them to get to the next room. (Placeholder: this plays when you walk in here.)"),
        });

        EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
        Debug.Log("Dialogue box: built and added to Training.");
    }

    static void Trigger(UnityEngine.SceneManagement.Scene scene, GameObject[] roots, string name, Vector2? at, Vector2 size, DialogueLine[] lines)
    {
        if (roots.Any(g => g.name == name)) return;
        var go = new GameObject(name);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        var t = go.AddComponent<DialogueTrigger>();
        t.lines = lines;
        t.playOnStart = at == null;
        if (at != null)
        {
            go.transform.position = at.Value;
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = size;
        }
    }

    static DialogueLine Line(string speaker, string text) => new DialogueLine { speaker = speaker, text = text };

    static RectTransform Stretch(Transform parent, string name)
    {
        var r = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return r;
    }

    static RectTransform Inset(RectTransform parent, string name, float by)
    {
        var r = Stretch(parent, name);
        r.offsetMin = new Vector2(by, by);
        r.offsetMax = new Vector2(-by, -by);
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
