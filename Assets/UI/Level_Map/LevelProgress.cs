using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Remembers which levels the player has beaten, saved on the player's computer between play sessions.
/// It hooks itself up automatically: whenever a scene with a LevelTimer loads (usually inside the HUD),
/// beating that level (the timer's On Level Complete) marks the scene as cleared, and if the level was
/// started from the map, the level's "E to exit" goes back to the map. No changes to the level scenes needed.
/// A level without a LevelTimer can call LevelProgress.CompleteCurrentLevel() from its own script instead.
/// To start over while testing: Tools > Space Dinosaurs > Reset Map Progress.
/// </summary>
public static class LevelProgress
{
    const string Prefix = "DeusRexMachina.";
    const string ClearedPrefix = Prefix + "cleared.";
    const string SeenKey = Prefix + "map.seenUnlocked";
    const string LastStageKey = Prefix + "map.lastStage";

    /// <summary>The map scene's name; the map sets this when it opens.</summary>
    public static string MapScene = "Level Select";

    /// <summary>True once the player has launched a level from the map (so exiting a level returns there).</summary>
    public static bool CameFromMap;

    public static bool IsCleared(string sceneName) =>
        !string.IsNullOrEmpty(sceneName) && PlayerPrefs.GetInt(ClearedPrefix + sceneName, 0) == 1;

    public static void MarkCleared(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        PlayerPrefs.SetInt(ClearedPrefix + sceneName, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Marks the scene that's open right now as cleared. For levels that end some other way than a LevelTimer.</summary>
    public static void CompleteCurrentLevel() => MarkCleared(SceneManager.GetActiveScene().name);

    /// <summary>How many stages the map has already shown as unlocked (so it can animate newly unlocked ones).</summary>
    public static int SeenUnlocked
    {
        get => PlayerPrefs.GetInt(SeenKey, 1);
        set { PlayerPrefs.SetInt(SeenKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>The stage the player last launched from the map (-1 = none yet).</summary>
    public static int LastStage
    {
        get => PlayerPrefs.GetInt(LastStageKey, -1);
        set { PlayerPrefs.SetInt(LastStageKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>Forgets all progress for these scenes, plus the map's own memory.</summary>
    public static void ResetAll(IEnumerable<string> sceneNames)
    {
        foreach (var name in sceneNames)
            if (!string.IsNullOrEmpty(name)) PlayerPrefs.DeleteKey(ClearedPrefix + name);
        PlayerPrefs.DeleteKey(SeenKey);
        PlayerPrefs.DeleteKey(LastStageKey);
        PlayerPrefs.Save();
    }

    // ---------- automatic hookup ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        HookLevel(SceneManager.GetActiveScene()); // the first scene was already loaded before Init ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HookLevel(scene);

    static void HookLevel(Scene scene)
    {
        string sceneName = scene.name;
        foreach (var timer in Object.FindObjectsByType<LevelTimer>(FindObjectsSortMode.None))
        {
            if (timer.gameObject.scene != scene) continue;
            if (timer.onLevelComplete == null) timer.onLevelComplete = new UnityEngine.Events.UnityEvent();
            timer.onLevelComplete.AddListener(() => MarkCleared(sceneName));
            if (CameFromMap && string.IsNullOrEmpty(timer.exitScene)) timer.exitScene = MapScene;
        }
    }
}
