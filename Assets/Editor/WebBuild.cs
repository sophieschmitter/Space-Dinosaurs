using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the web (WebGL) version for itch.io into Builds/WebGL, using the scenes in the build list
/// (TitleScreen first). Tools > Space Dinosaurs > Build Web Version. The result is also written to
/// Builds/WebGL-build-result.txt. Zip the *contents* of Builds/WebGL (index.html at the top) to upload.
/// </summary>
public static class WebBuild
{
    public const string Folder = "Builds/WebGL";

    [MenuItem("Tools/Space Dinosaurs/Build Web Version")]
    public static void Build()
    {
        Directory.CreateDirectory("Builds");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var options = new BuildPlayerOptions { scenes = scenes, locationPathName = Folder, target = BuildTarget.WebGL, options = BuildOptions.None };
        var report = BuildPipeline.BuildPlayer(options);
        var s = report.summary;
        string text = $"{s.result} | {s.totalErrors} errors, {s.totalWarnings} warnings | {s.totalSize / (1024f * 1024f):F1} MB | {s.totalTime.TotalSeconds:F0} s | scenes: {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}";
        foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception) text += "\nERROR: " + m.content;
        File.WriteAllText("Builds/WebGL-build-result.txt", text);
        Debug.Log("Web build: " + text);
    }
}
