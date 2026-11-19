using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The in-game menu: a button in the bottom-right corner (or Esc / the controller's Start) pauses the game and
/// opens the menu, with Resume, Controls (how to play) and Level Select. Esc / B / Start close it again
/// (from Controls, they go back to the menu first).
/// Placeholder blocks for now: swap the Images' sprites and colours for the real art. Add more menu entries as
/// buttons under Menu Panel/Buttons and hook their On Click up to anything.
/// Built by Tools > Space Dinosaurs > Build Pause Menu.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }

    public Button menuButton;
    [Tooltip("Shown while the menu is open (the dimmed backdrop and the panel).")]
    public GameObject menuRoot;
    public GameObject mainPanel, controlsPanel;
    [Tooltip("Highlighted when each panel opens (so the keyboard and controller can use it).")]
    public Button firstMainButton, firstControlsButton;
    [Tooltip("Hidden in the level select scene itself.")]
    public Button levelSelectButton;

    [Tooltip("Scene the Level Select button loads. It must be in File > Build Profiles (Scene List).")]
    public string levelSelectScene = "Level Select";
    [Tooltip("Esc (and the controller's Start) open and close the menu. Off where Esc already means something else.")]
    public bool keyboardOpens = true;

    float savedTimeScale = 1f;
    static int lastClosedFrame = -1;

    public bool IsOpen => menuRoot != null && menuRoot.activeSelf;

    /// <summary>True while the menu is open and on the frame it closes: the dino ignores attack presses then.</summary>
    public static bool InputTaken => (Instance != null && Instance.IsOpen) || Time.frameCount == lastClosedFrame;

    void Awake()
    {
        Instance = this;
        if (menuRoot != null) menuRoot.SetActive(false);
        if (menuButton != null) menuButton.onClick.AddListener(Toggle);
        if (levelSelectButton != null && SceneManager.GetActiveScene().name == levelSelectScene)
            levelSelectButton.gameObject.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        var k = Keyboard.current;
        var g = Gamepad.current;
        bool esc = k != null && k.escapeKey.wasPressedThisFrame;
        bool start = g != null && g.startButton.wasPressedThisFrame;
        bool back = g != null && g.buttonEast.wasPressedThisFrame;

        if (IsOpen)
        {
            if (esc || start || back)
            {
                if (controlsPanel != null && controlsPanel.activeSelf) ShowMain();
                else Close();
            }
            return;
        }
        if (!keyboardOpens || !(esc || start)) return;
        if (BuildingPlacer.Placing != null) return;                         // Esc puts the building back instead
        if (DialogueBox.Instance != null && DialogueBox.Instance.IsOpen) return; // finish talking first
        var timer = FindFirstObjectByType<LevelTimer>();
        if (timer != null && timer.IsOver) return;                           // the end screens use Start themselves
        Open();
    }

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        if (IsOpen || menuRoot == null) return;
        BuildingPlacer.Cancel();
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        menuRoot.SetActive(true);
        ShowMain();
    }

    public void Close()
    {
        if (!IsOpen) return;
        menuRoot.SetActive(false);
        Time.timeScale = savedTimeScale;
        lastClosedFrame = Time.frameCount;
        Select(null);
    }

    public void ShowMain()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (controlsPanel != null) controlsPanel.SetActive(false);
        Select(firstMainButton);
    }

    public void ShowControls()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (controlsPanel != null) controlsPanel.SetActive(true);
        Select(firstControlsButton);
    }

    public void GoToLevelSelect()
    {
        if (!Application.CanStreamedLevelBeLoaded(levelSelectScene))
        {
            Debug.LogError($"Pause menu: can't load '{levelSelectScene}'. Add it in File > Build Profiles (Scene List).");
            return;
        }
        Time.timeScale = 1f;
        SceneManager.LoadScene(levelSelectScene);
    }

    static void Select(Selectable s)
    {
        if (EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(s != null && s.gameObject.activeInHierarchy ? s.gameObject : null);
    }
}
