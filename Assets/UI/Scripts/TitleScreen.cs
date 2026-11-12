using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The title screen menu: Play loads the first level, Controls shows the how-to-play panel, Quit exits.
/// Works with mouse, keyboard (arrows / WASD + Enter) and gamepad. Esc closes the controls panel.
/// The screen fades in from black on start and fades out before loading the level.
/// Built by Tools > Space Dinosaurs > Build Title Screen, but every reference here can be re-wired by hand.
/// </summary>
public class TitleScreen : MonoBehaviour
{
    [Tooltip("Scene loaded by Play. It must be listed in File > Build Profiles (Scene List).")]
    public string firstLevelScene = "Level One";

    [Header("Menus")]
    [Tooltip("The Play / Controls / Quit buttons. Hidden while the controls panel is open.")]
    public GameObject mainMenu;
    public GameObject controlsPanel;
    [Tooltip("Button highlighted when the title screen opens (Play).")]
    public GameObject firstSelected;
    [Tooltip("Button highlighted when the controls panel opens (Back).")]
    public GameObject controlsFirstSelected;
    [Tooltip("Hidden in web builds, where a game can't quit.")]
    public GameObject quitButton;

    [Header("Fade")]
    [Tooltip("Full-screen black Image used to fade in and out. Optional.")]
    public Image fade;
    public float fadeTime = 0.4f;

    [Header("Title bob")]
    public RectTransform title;
    [Tooltip("How far the title floats up and down, in UI pixels.")]
    public float titleBobHeight = 12f;
    public float titleBobSpeed = 1.6f;
    [Tooltip("Snap the bob to steps this size so it moves like pixel art. 0 = smooth.")]
    public float titleBobStep = 4f;

    bool busy;
    Vector2 titleStart;

    bool ControlsOpen => controlsPanel != null && controlsPanel.activeSelf;

    void Awake()
    {
        Time.timeScale = 1f; // a level may have ended paused
    }

    void Start()
    {
        if (controlsPanel != null) controlsPanel.SetActive(false);
        if (mainMenu != null) mainMenu.SetActive(true);
        if (title != null) titleStart = title.anchoredPosition;
#if UNITY_WEBGL
        if (quitButton != null) quitButton.SetActive(false);
#endif
        Select(firstSelected);
        if (fade != null) StartCoroutine(Fade(1f, 0f));
    }

    void Update()
    {
        if (title != null)
        {
            float y = Mathf.Sin(Time.unscaledTime * titleBobSpeed) * titleBobHeight;
            if (titleBobStep > 0f) y = Mathf.Round(y / titleBobStep) * titleBobStep;
            title.anchoredPosition = titleStart + new Vector2(0f, y);
        }

        if (busy) return;

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) ||
                    (pad != null && pad.buttonEast.wasPressedThisFrame);
        if (back && ControlsOpen) HideControls();

        // Clicking empty space clears the selection; put it back so the keyboard keeps working.
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject == null)
            Select(ControlsOpen ? controlsFirstSelected : firstSelected);
    }

    // ---------- buttons ----------

    public void Play()
    {
        if (busy) return;
        busy = true;
        StopAllCoroutines();
        StartCoroutine(PlayRoutine());
    }

    public void ShowControls()
    {
        if (busy || controlsPanel == null) return;
        controlsPanel.SetActive(true);
        if (mainMenu != null) mainMenu.SetActive(false);
        Select(controlsFirstSelected);
    }

    public void HideControls()
    {
        if (controlsPanel == null) return;
        controlsPanel.SetActive(false);
        if (mainMenu != null) mainMenu.SetActive(true);
        Select(firstSelected);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- helpers ----------

    IEnumerator PlayRoutine()
    {
        if (fade != null) yield return Fade(fade.color.a, 1f);
        SceneManager.LoadScene(firstLevelScene);
    }

    IEnumerator Fade(float from, float to)
    {
        fade.gameObject.SetActive(true);
        fade.raycastTarget = true; // block clicks while fading
        for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            SetFadeAlpha(Mathf.Lerp(from, to, t / fadeTime));
            yield return null;
        }
        SetFadeAlpha(to);
        if (to <= 0f) fade.raycastTarget = false;
    }

    void SetFadeAlpha(float a)
    {
        var c = fade.color;
        c.a = a;
        fade.color = c;
    }

    static void Select(GameObject go)
    {
        var es = EventSystem.current;
        if (es == null || go == null) return;
        es.SetSelectedGameObject(null); // force OnSelect even if it was already selected
        es.SetSelectedGameObject(go);
    }
}
