using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the Dev scenes out of the player build without taking them out of Build Settings.
///
/// They have to stay in the list: <c>SceneLoader</c> loads every scene by name
/// (<c>SceneManager.LoadSceneAsync</c>), and that only finds scenes that are in Build Settings —
/// in the editor too. Unticking TestIñaki, TestAlesio, TestFabriScene, TestLevel or NemesisTestbed
/// would break pressing Play inside them, which is the whole point of having them.
///
/// So they are dropped here instead: the Build and Build and Run buttons get the scene list minus
/// everything under <c>Assets/_Project/Scenes/Dev/</c>. Nothing else about the build changes.
///
/// Not covered: a build started from a script (<c>BuildPipeline.BuildPlayer</c> with its own scene
/// list). Run those through <see cref="WithoutDevScenes"/>.
/// </summary>
[InitializeOnLoad]
public static class DevScenesBuildFilter
{
    private const string DevScenesFolder = "Assets/_Project/Scenes/Dev/";

    static DevScenesBuildFilter()
    {
        BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(GetOptions);
    }

    private static BuildPlayerOptions GetOptions(BuildPlayerOptions defaults)
    {
        // The default handler is what asks for the output path and fills everything else in; only
        // the scene list is ours. It throws to cancel the build (an empty path, for one), and that
        // has to travel: swallowing it would start a build the user called off.
        BuildPlayerOptions options = BuildPlayerWindow.DefaultBuildMethods.GetBuildPlayerOptions(defaults);

        string[] kept = WithoutDevScenes(options.scenes);
        int dropped = (options.scenes?.Length ?? 0) - kept.Length;
        if (dropped > 0)
            Debug.Log($"[{nameof(DevScenesBuildFilter)}] Left {dropped} dev scene(s) out of the build.");

        options.scenes = kept;
        return options;
    }

    /// <summary>The same scene paths with everything under the Dev folder removed.</summary>
    public static string[] WithoutDevScenes(IEnumerable<string> scenePaths)
    {
        if (scenePaths == null) return Array.Empty<string>();

        return scenePaths
            .Where(path => !string.IsNullOrEmpty(path) &&
                           !path.Replace('\\', '/').StartsWith(DevScenesFolder, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
