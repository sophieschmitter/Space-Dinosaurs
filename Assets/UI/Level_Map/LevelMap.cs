using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The level map (Battle Cats style). Stages are joined by a dotted path that lights up as you beat them;
/// the next stage to play flashes, locked ones are dark, and the dino walks along the path to whichever
/// stage you pick. When you come back after beating a level, the new piece of path lights up dot by dot
/// and the dino walks to the newly unlocked stage.
///
/// Controls: click a stage (click it again, or press Play, to start it), drag or scroll to look around,
/// Left/Right or A/D or the D-pad to step between stages, Enter/Space/A to play, Esc/B for the title screen.
///
/// Put this on a "Map" object and make the stages (sprites with Map Stage) its children, in play order
/// from top to bottom of the Hierarchy. Progress is saved by LevelProgress.
/// </summary>
public class LevelMap : MonoBehaviour
{
    [Tooltip("Stages in play order. Leave empty to use this object's children that have Map Stage, top to bottom.")]
    public List<MapStage> stages = new List<MapStage>();
    [Tooltip("Found automatically if left empty.")]
    public MapCamera mapCamera;
    [Tooltip("Found automatically if left empty.")]
    public MapDino dino;

    [Header("Path")]
    [Tooltip("Optional dot image. Empty = a plain square dot, Dot Size wide.")]
    public Sprite dotSprite;
    [Tooltip("Size of the plain square dot, in world units (0.25 = 4 art pixels).")]
    public float dotSize = 0.25f;
    [Tooltip("World units between dots.")]
    public float dotSpacing = 0.6f;
    [Tooltip("0 = straight lines between stages, 1 = smooth curves.")]
    [Range(0f, 1f)] public float curve = 0.7f;
    [Tooltip("No dots this close to a stage's middle (as a share of the stage's size), so they don't cover it.")]
    public float stageGap = 0.8f;
    public Color reachedDot = new Color32(0xF5, 0xC5, 0x42, 0xFF);
    public Color unreachedDot = new Color32(0xB9, 0xA6, 0xFF, 0x59);
    [Tooltip("Order in Layer for the dots: under the stages, over the background.")]
    public int pathSortingOrder = -10;
    [Tooltip("Seconds between dots lighting up when a new stage unlocks.")]
    public float dotRevealDelay = 0.05f;

    [Header("Bottom bar (all optional)")]
    public Text stageNumberText;
    public Text stageNameText;
    [Tooltip("Short messages like 'CLEAR STAGE 2 FIRST'.")]
    public Text hintText;
    public Button playButton;
    [Tooltip("Only needed if the Play button has no Menu Button Style.")]
    public Text playLabel;

    [Header("Scenes")]
    public string titleScene = "TitleScreen";

    [Header("Fade (optional)")]
    public Image fade;
    public float fadeTime = 0.4f;

    [Header("Testing")]
    [Tooltip("Unlock every stage, without saving anything. Remember to switch it off.")]
    public bool unlockAllForTesting;

    readonly List<List<Vector3>> segmentPoints = new List<List<Vector3>>();       // segment i joins stage i and i+1
    readonly List<List<SpriteRenderer>> segmentDots = new List<List<SpriteRenderer>>();
    int unlockedCount = 1;   // stages 0 .. unlockedCount-1 are playable
    int selected;
    int dinoStage;           // the stage the dino is at or walking to
    bool busy;               // loading another scene
    bool animating;          // showing a newly unlocked stage
    float stickCooldown;
    Coroutine hintRoutine;
    static Sprite plainDot;

    void Awake()
    {
        Time.timeScale = 1f; // a level may have ended paused
        if (stages.Count == 0) stages.AddRange(GetComponentsInChildren<MapStage>());
        stages.RemoveAll(s => s == null);
        LevelProgress.MapScene = gameObject.scene.name;
        if (mapCamera == null) mapCamera = FindFirstObjectByType<MapCamera>();
        if (dino == null) dino = FindFirstObjectByType<MapDino>();
    }

    void OnEnable()
    {
        if (mapCamera != null) mapCamera.Clicked += OnMapClicked;
    }

    void OnDisable()
    {
        if (mapCamera != null) mapCamera.Clicked -= OnMapClicked;
    }

    void Start()
    {
        if (fade != null) StartCoroutine(Fade(1f, 0f));
        if (stages.Count == 0)
        {
            Debug.LogWarning("Level Map: no stages. Make sprites with Map Stage children of the Map object.", this);
            return;
        }

        BuildPath();
        unlockedCount = CountUnlocked();

        // Stages unlocked since the map was last open get revealed with an animation.
        int shown = unlockAllForTesting ? unlockedCount : Mathf.Clamp(LevelProgress.SeenUnlocked, 1, unlockedCount);
        bool reveal = shown < unlockedCount;
        ShowStates(shown);

        if (reveal) selected = shown - 1;
        else if (LevelProgress.LastStage >= 0) selected = Mathf.Clamp(LevelProgress.LastStage, 0, unlockedCount - 1);
        else selected = FirstUnbeatenStage();

        dinoStage = selected;
        if (dino != null) dino.PlaceAt(stages[selected].StandPoint);
        if (mapCamera != null) mapCamera.FocusOn(stages[selected].StandPoint.x, true);
        UpdateBar();

        if (reveal) StartCoroutine(RevealNewStages(shown));
        if (!unlockAllForTesting) LevelProgress.SeenUnlocked = unlockedCount;
    }

    void Update()
    {
        if (stickCooldown > 0f) stickCooldown -= Time.unscaledDeltaTime;
        if (busy || animating || stages.Count == 0) return;

        var kb = Keyboard.current;
        var pad = Gamepad.current;

        int step = 0;
        if (kb != null)
        {
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) step = 1;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) step = -1;
        }
        if (pad != null)
        {
            if (pad.dpad.right.wasPressedThisFrame) step = 1;
            if (pad.dpad.left.wasPressedThisFrame) step = -1;
            float x = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(x) < 0.3f) stickCooldown = 0f;
            else if (Mathf.Abs(x) > 0.6f && stickCooldown <= 0f)
            {
                step = x > 0f ? 1 : -1;
                stickCooldown = 0.3f;
            }
        }
        if (step != 0)
        {
            int target = selected + step;
            if (target >= 0 && target < stages.Count) TrySelect(target);
        }

        bool submit = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) ||
                      (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        if (submit) Play();

        bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
        if (back) BackToTitle();
    }

    // ---------- buttons ----------

    /// <summary>Start the selected stage. Hook the Play button's On Click up to this.</summary>
    public void Play()
    {
        if (busy || animating || stages.Count == 0) return;
        var stage = stages[selected];
        if (!stage.HasScene) { ShowHint("COMING SOON"); return; }
        if (!Application.CanStreamedLevelBeLoaded(stage.sceneName))
        {
            Debug.LogError($"Level Map: can't load scene '{stage.sceneName}'. Check the spelling, and add it in " +
                           "File > Build Profiles > Scene List.", stage);
            ShowHint("SCENE NOT IN SCENE LIST");
            return;
        }
        LevelProgress.CameFromMap = true;
        LevelProgress.LastStage = selected;
        StartCoroutine(Load(stage.sceneName));
    }

    /// <summary>Go back to the title screen. Hook a Back button's On Click up to this.</summary>
    public void BackToTitle()
    {
        if (busy || string.IsNullOrEmpty(titleScene)) return;
        StartCoroutine(Load(titleScene));
    }

    // ---------- testing ----------

    [ContextMenu("Reset Map Progress")]
    void ResetProgress()
    {
        var names = new List<string>();
        foreach (var s in stages) if (s != null) names.Add(s.sceneName);
        LevelProgress.ResetAll(names);
        Debug.Log("Level Map: progress reset.");
    }

    // ---------- selecting ----------

    void OnMapClicked(Vector2 world)
    {
        if (busy || animating) return;
        int hit = StageAt(world);
        if (hit < 0) return;
        if (hit == selected && dino != null && !dino.IsMoving) Play(); // second click on the same stage plays it
        else TrySelect(hit);
    }

    void TrySelect(int index)
    {
        if (index >= unlockedCount)
        {
            stages[index].Shake();
            var before = stages[index - 1];
            ShowHint(before.HasScene ? $"CLEAR STAGE {index} FIRST" : "COMING SOON");
            return;
        }
        selected = index;
        UpdateBar();
        if (mapCamera != null) mapCamera.FocusOn(stages[index].StandPoint.x);
        if (dino != null && index != dinoStage) dino.WalkPath(PathBetween(dinoStage, index));
        dinoStage = index;
    }

    int StageAt(Vector2 world)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < stages.Count; i++)
        {
            if (!stages[i].Contains(world)) continue;
            float d = ((Vector2)stages[i].transform.position - world).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    // ---------- progress & looks ----------

    int CountUnlocked()
    {
        if (unlockAllForTesting) return stages.Count;
        int n = 1;
        while (n < stages.Count && LevelProgress.IsCleared(stages[n - 1].sceneName)) n++;
        return n;
    }

    int FirstUnbeatenStage()
    {
        for (int i = 0; i < unlockedCount; i++)
            if (stages[i].HasScene && !LevelProgress.IsCleared(stages[i].sceneName)) return i;
        return unlockedCount - 1;
    }

    /// <summary>Colours stages and path as if the first 'unlocked' stages were playable.</summary>
    void ShowStates(int unlocked)
    {
        for (int i = 0; i < stages.Count; i++)
        {
            var s = stages[i];
            MapStage.State state;
            if (i >= unlocked) state = MapStage.State.Locked;
            else if (LevelProgress.IsCleared(s.sceneName)) state = MapStage.State.Cleared;
            else state = MapStage.State.Open;
            s.SetState(state, state == MapStage.State.Open && s.HasScene);
        }
        for (int i = 0; i < segmentDots.Count; i++)
        {
            Color c = i + 1 < unlocked ? reachedDot : unreachedDot;
            foreach (var dot in segmentDots[i]) dot.color = c;
        }
    }

    IEnumerator RevealNewStages(int shown)
    {
        animating = true;
        yield return new WaitForSecondsRealtime(0.5f);
        for (int k = shown; k < unlockedCount; k++)
        {
            if (mapCamera != null) mapCamera.FocusOn(stages[k].StandPoint.x);
            foreach (var dot in segmentDots[k - 1])
            {
                dot.color = reachedDot;
                yield return new WaitForSecondsRealtime(dotRevealDelay);
            }
            ShowStates(k + 1);
        }
        animating = false;

        int newest = unlockedCount - 1;
        TrySelect(stages[newest].HasScene ? newest : selected);
    }

    void UpdateBar()
    {
        var s = stages[selected];
        if (stageNumberText != null) stageNumberText.text = $"STAGE {selected + 1}";
        if (stageNameText != null) stageNameText.text = s.displayName.ToUpperInvariant();

        string label;
        if (!s.HasScene) label = "COMING SOON";
        else if (LevelProgress.IsCleared(s.sceneName)) label = "REPLAY";
        else label = "PLAY";
        if (playButton != null)
        {
            playButton.interactable = s.HasScene;
            var style = playButton.GetComponent<MenuButtonStyle>();
            if (style != null) style.SetLabel(label);
            else if (playLabel != null) playLabel.text = label;
        }
        else if (playLabel != null) playLabel.text = label;
        ShowHint("");
    }

    void ShowHint(string message)
    {
        if (hintText == null) return;
        if (hintRoutine != null) StopCoroutine(hintRoutine);
        hintText.text = message;
        if (!string.IsNullOrEmpty(message)) hintRoutine = StartCoroutine(ClearHint());
    }

    IEnumerator ClearHint()
    {
        yield return new WaitForSecondsRealtime(2f);
        hintText.text = "";
        hintRoutine = null;
    }

    // ---------- the dotted path ----------

    void BuildPath()
    {
        var parent = new GameObject("Path (made when the game runs)").transform;
        parent.SetParent(transform, true);

        Sprite sprite = dotSprite != null ? dotSprite : PlainDot();
        float scale = dotSprite != null ? 1f : dotSize;

        for (int i = 0; i < stages.Count - 1; i++)
        {
            Vector3 p0 = Point(i - 1), p1 = Point(i), p2 = Point(i + 1), p3 = Point(i + 2);
            var points = new List<Vector3>();
            const int samples = 32;
            for (int k = 0; k <= samples; k++) points.Add(CurvePoint(p0, p1, p2, p3, k / (float)samples));
            segmentPoints.Add(points);

            var dots = new List<SpriteRenderer>();
            float gapStart = stages[i].Radius * stageGap, gapEnd = stages[i + 1].Radius * stageGap;
            float length = Length(points);
            for (float d = dotSpacing * 0.5f; d < length; d += dotSpacing)
            {
                Vector3 pos = PointAt(points, d);
                if (Vector2.Distance(pos, p1) < gapStart || Vector2.Distance(pos, p2) < gapEnd) continue;
                pos.x = Mathf.Round(pos.x * 16f) / 16f; // line up with the 16-pixels-per-tile art grid
                pos.y = Mathf.Round(pos.y * 16f) / 16f;
                pos.z = 0f;

                var go = new GameObject("Dot");
                go.transform.SetParent(parent, false);
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * scale;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = pathSortingOrder;
                sr.color = unreachedDot;
                dots.Add(sr);
            }
            segmentDots.Add(dots);
        }
    }

    Vector3 Point(int i) => stages[Mathf.Clamp(i, 0, stages.Count - 1)].StandPoint;

    Vector3 CurvePoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        Vector3 smooth = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        return Vector3.Lerp(Vector3.Lerp(p1, p2, t), smooth, curve);
    }

    static float Length(List<Vector3> points)
    {
        float total = 0f;
        for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);
        return total;
    }

    static Vector3 PointAt(List<Vector3> points, float distance)
    {
        for (int i = 1; i < points.Count; i++)
        {
            float segment = Vector3.Distance(points[i - 1], points[i]);
            if (distance <= segment && segment > 0f) return Vector3.Lerp(points[i - 1], points[i], distance / segment);
            distance -= segment;
        }
        return points[points.Count - 1];
    }

    List<Vector3> PathBetween(int from, int to)
    {
        var path = new List<Vector3>();
        int dir = to > from ? 1 : -1;
        for (int i = from; i != to; i += dir)
        {
            var segment = segmentPoints[dir > 0 ? i : i - 1];
            if (dir > 0) path.AddRange(segment);
            else for (int j = segment.Count - 1; j >= 0; j--) path.Add(segment[j]);
        }
        if (path.Count == 0) path.Add(stages[to].StandPoint);
        return path;
    }

    static Sprite PlainDot()
    {
        if (plainDot != null) return plainDot;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        plainDot = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return plainDot;
    }

    // ---------- scene changes ----------

    IEnumerator Load(string sceneName)
    {
        busy = true;
        if (fade != null) yield return Fade(fade.color.a, 1f);
        SceneManager.LoadScene(sceneName);
    }

    IEnumerator Fade(float from, float to)
    {
        fade.gameObject.SetActive(true);
        fade.raycastTarget = true;
        for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            SetFade(Mathf.Lerp(from, to, t / fadeTime));
            yield return null;
        }
        SetFade(to);
        if (to <= 0f) fade.raycastTarget = false;
    }

    void SetFade(float a)
    {
        var c = fade.color;
        c.a = a;
        fade.color = c;
    }
}
