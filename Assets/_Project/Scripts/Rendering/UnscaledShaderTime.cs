using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Publishes <c>_UnscaledTime</c>: a global shader clock, in seconds, that keeps running while
/// <c>Time.timeScale == 0</c>.
///
/// Unity's own <c>_Time</c> follows the scaled clock, so a shader animated with it freezes the moment
/// UIStateManager pauses the game — exactly when the pause, settings and inventory canvases are on
/// screen. The UI shaders (InventoryCRT, AnimatedSurface) animate with <c>_UnscaledTime</c> instead.
/// World shaders keep <c>_Time</c>: the world is supposed to freeze.
///
/// Pushed from <c>Canvas.willRenderCanvases</c>, raised once per frame before any canvas is drawn,
/// paused or not — no GameObject, no scene setup. In edit mode it follows the editor clock, so the
/// effects still move in the Scene and Game views.
///
/// Shaders declare it as a plain uniform, NOT in their Properties block: a material property of the
/// same name would shadow the global and freeze the animation again.
/// </summary>
public static class UnscaledShaderTime
{
    private static readonly int Id = Shader.PropertyToID("_UnscaledTime");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
        // -= first: with domain reload disabled the static event survives between play sessions.
        Canvas.willRenderCanvases -= Push;
        Canvas.willRenderCanvases += Push;
        Push();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void InstallInEditor() => Install();
#endif

    private static void Push() => Shader.SetGlobalFloat(Id, Now);

    private static float Now
    {
        get
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return (float)EditorApplication.timeSinceStartup;
#endif
            return Time.unscaledTime;
        }
    }
}
