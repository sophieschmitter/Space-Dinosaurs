using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Space Dinosaurs > Reset Map Progress: forgets every cleared level, so the map starts fresh
/// (only stage 1 open). Handy while testing the map.
/// </summary>
public static class MapProgressMenu
{
    [MenuItem("Tools/Space Dinosaurs/Reset Map Progress")]
    static void ResetProgress()
    {
        var names = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:SceneAsset"))
            names.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
        LevelProgress.ResetAll(names);
        Debug.Log("Map progress reset: only stage 1 is unlocked now.");
    }
}
