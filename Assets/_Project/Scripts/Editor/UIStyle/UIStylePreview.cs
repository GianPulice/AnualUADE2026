#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Looks at any styled prefab without entering Play Mode: drops a throwaway copy in the editor, and
/// renders it to &lt;project&gt;/Temp/UIStylePreview/&lt;name&gt;.png — flat, and through the CRT tube as
/// &lt;name&gt;_crt.png when its profile turns the tube on.
///
/// The copy lives in its own untitled additive scene, so the open level is never touched or dirtied,
/// and the prefab is never modified. Remove closes that scene without saving; entering Play Mode
/// removes it too, or it would play as a second, dead copy of the screen.
///
/// Edit mode runs no Awake, so the copy is put in the state the game would show
/// (<see cref="ForceRuntimeState"/>). Screen-specific states — the inventory's items, its doc — are
/// the business of hooks like InventoryScenePreview.
///
/// A render in an editor preview scene was tried first and dropped: URP draws nothing for a UI
/// canvas that lives there, so every shot came out blank.
/// </summary>
[InitializeOnLoad]
public static class UIStylePreview
{
    private const string InstanceName = "__UIStylePreview";
    private const string SpawnedPrefabKey = "UIStylePreview.SpawnedPrefab";
    private const int Width = 1920, Height = 1080;

    // What shows where the world would be: behind a Dim, and past the tube's corners.
    private static readonly Color WorldStandIn = new Color(0.32f, 0.30f, 0.27f, 1f);

    // Far from anything a scene could put in front of the camera, as CanvasCRTPresenter does.
    private static readonly Vector3 CameraPosition = new Vector3(0f, -10000f, 0f);

    public static string OutputFolder => Path.GetFullPath("Temp/UIStylePreview");

    static UIStylePreview()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Remove();
        };
    }

    // -- Menu -------------------

    [MenuItem("Tools/UI/Style/Preview/Spawn Selected", priority = 20)]
    private static void SpawnSelected()
    {
        GameObject prefab = SelectedPrefab();
        Spawn(prefab);
        Debug.Log($"[UIStylePreview] Spawned {prefab.name}. Look at the Game view, or Render PNG.");
    }

    [MenuItem("Tools/UI/Style/Preview/Spawn Selected", true)]
    private static bool HasSelectedPrefab() => SelectedPrefab() != null;

    [MenuItem("Tools/UI/Style/Preview/Render PNG", priority = 21)]
    private static void RenderSpawned()
    {
        GameObject root = FindInstance();
        if (root == null) return;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SessionState.GetString(SpawnedPrefabKey, ""));
        SO_UIStyleProfile profile = prefab != null ? UIStyleTools.FindProfileFor(prefab) : null;
        RenderPng(root, prefab != null ? prefab.name : "preview", TubeOf(profile));
    }

    [MenuItem("Tools/UI/Style/Preview/Remove", priority = 22)]
    public static void Remove()
    {
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsPreviewScene(scene)) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // -- Spawn -------------------

    public static GameObject Spawn(GameObject prefab)
    {
        Remove();

        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject root = Object.Instantiate(prefab);
        root.name = InstanceName;
        SceneManager.MoveGameObjectToScene(root, scene);

        // An additive NewScene becomes the active scene; hand that back, or whatever gets created
        // next — by hand or by another tool — lands in a scene that is about to be thrown away.
        SceneManager.SetActiveScene(previous);

        SessionState.SetString(SpawnedPrefabKey, AssetDatabase.GetAssetPath(prefab));
        ForceRuntimeState(root);
        InternalEditorUtility.RepaintAllViews();
        return root;
    }

    /// <summary>
    /// What the game shows that edit mode would not:
    ///   - A root saved inactive is shown: its view would activate it.
    ///   - SweepBars collapse in ButtonHoverSweepEffect.Awake; left alone, every button previews as a solid bar.
    ///   - UISignalTransition switches its overlays off in Awake; left alone, the scan line shows.
    ///   - Screens that wait at alpha 0 (Result, Win, the interaction prompt) are shown at 1: a
    ///     preview of an invisible screen shows nothing.
    /// </summary>
    public static void ForceRuntimeState(GameObject root)
    {
        // A screen saved hidden (SaveSlots) is what its view shows with SetActive.
        if (!root.activeSelf) root.SetActive(true);

        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == "SweepBar") node.gameObject.SetActive(false);

        foreach (UISignalTransition signal in root.GetComponentsInChildren<UISignalTransition>(true))
        {
            SerializedObject data = new SerializedObject(signal);
            if (data.FindProperty("staticNoise").objectReferenceValue is Graphic noise) noise.enabled = false;
            if (data.FindProperty("scanBar").objectReferenceValue is Graphic bar) bar.enabled = false;
        }

        foreach (BaseScreenView view in root.GetComponentsInChildren<BaseScreenView>(true))
            if (view.TryGetComponent(out CanvasGroup group)) group.alpha = 1f;
    }

    public static GameObject FindInstance()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsPreviewScene(scene)) return scene.GetRootGameObjects().First(go => go.name == InstanceName);
        }

        Debug.LogWarning("[UIStylePreview] No preview in the scene. Spawn one first.");
        return null;
    }

    public static Material TubeOf(SO_UIStyleProfile profile) =>
        profile != null && profile.crt.enabled ? profile.crt.material : null;

    // -- Render -------------------

    /// <summary>
    /// Writes the copy as it stands to OutputFolder/&lt;stem&gt;.png at 1920x1080, and through
    /// <paramref name="tube"/> to &lt;stem&gt;_crt.png when there is one.
    ///
    /// An Overlay canvas cannot render into a texture, so for this one synchronous call the copy is
    /// switched to Screen Space - Camera on a temporary camera, and the pipeline's renderer features
    /// (the PS1 pass, the fog) are switched off so they do not land on the UI — which in the game they
    /// never do. Everything is put back before the method returns; nothing is marked dirty.
    /// </summary>
    public static string RenderPng(GameObject root, string stem, Material tube)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning($"[UIStylePreview] '{stem}' has no Canvas on its root.");
            return null;
        }

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        RenderTexture target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

        GameObject cameraGo = new GameObject("__UIStylePreviewCamera");
        SceneManager.MoveGameObjectToScene(cameraGo, root.scene);
        cameraGo.transform.position = CameraPosition;

        Camera camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = WorldStandIn;
        // Every layer: as an Overlay the canvas draws children of any layer, and the SweepBars are on Default.
        camera.cullingMask = ~0;
        camera.orthographic = true;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        camera.targetTexture = target;
        camera.enabled = false;

        List<(ScriptableRendererFeature feature, bool wasActive)> features = AllRendererFeatures()
            .Select(f => (f, f.isActive)).ToList();

        RenderMode previousMode = canvas.renderMode;
        float previousPlane = canvas.planeDistance;
        bool scalerWasEnabled = scaler != null && scaler.enabled;
        float scale = ScaleFactor(scaler);

        string file = FileSafe(stem);
        string path = Path.Combine(OutputFolder, file + ".png");
        Directory.CreateDirectory(OutputFolder);

        try
        {
            foreach ((ScriptableRendererFeature feature, bool _) in features) feature.SetActive(false);

            // The scaler would size from the Game view; the texture has a size of its own.
            if (scaler != null) scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.scaleFactor = scale;

            Canvas.ForceUpdateCanvases();
            camera.Render();

            SavePng(target, path);

            // CanvasCRTPresenter never runs in edit mode, so its material is applied here by hand —
            // exactly the blit it does at runtime.
            if (tube != null) SaveThroughTube(target, tube, Path.Combine(OutputFolder, file + "_crt.png"));
        }
        finally
        {
            foreach ((ScriptableRendererFeature feature, bool wasActive) in features) feature.SetActive(wasActive);

            canvas.renderMode = previousMode;
            canvas.worldCamera = null;
            canvas.planeDistance = previousPlane;
            if (scaler != null) scaler.enabled = scalerWasEnabled;

            Object.DestroyImmediate(cameraGo);
            target.Release();
            Object.DestroyImmediate(target);
        }

        Debug.Log($"[UIStylePreview] Rendered {path}" + (tube != null ? " (+ _crt)" : ""));
        return path;
    }

    /// <summary>The scale the canvas's scaler would pick at 1920x1080 — so a screen authored at another reference renders right.</summary>
    private static float ScaleFactor(CanvasScaler scaler)
    {
        if (scaler == null) return 1f;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler.scaleFactor;
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 1f;

        Vector2 reference = scaler.referenceResolution;
        float byWidth = Width / reference.x, byHeight = Height / reference.y;

        switch (scaler.screenMatchMode)
        {
            case CanvasScaler.ScreenMatchMode.Expand: return Mathf.Min(byWidth, byHeight);
            case CanvasScaler.ScreenMatchMode.Shrink: return Mathf.Max(byWidth, byHeight);
            default: return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(byWidth, 2f), Mathf.Log(byHeight, 2f), scaler.matchWidthOrHeight));
        }
    }

    // -- Play Mode -------------------

    /// <summary>
    /// Saves what every tube on screen shows to OutputFolder/&lt;canvas&gt;_play_crt.png, plus the flat
    /// UI texture under it as &lt;canvas&gt;_play_ui.png.
    ///
    /// It re-draws each tube itself — the presenter's UI texture through the presenter's own screen
    /// material — instead of grabbing the Game view: ScreenCapture only writes when the Game view
    /// repaints, which an unfocused editor may not do for a long while. There is no world in a blit,
    /// so the tube goes over a flat stand-in colour.
    /// </summary>
    [MenuItem("Tools/UI/Style/Preview/Play Mode: Capture Tubes", priority = 40)]
    public static void CaptureTubes()
    {
        // Private on purpose: at runtime nobody but the presenter has business with them.
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo targetField = typeof(CanvasCRTPresenter).GetField("target", Private);
        FieldInfo screenField = typeof(CanvasCRTPresenter).GetField("screen", Private);

        CanvasCRTPresenter[] presenters = Object.FindObjectsByType<CanvasCRTPresenter>(FindObjectsInactive.Include);
        Directory.CreateDirectory(OutputFolder);
        List<string> captured = new List<string>();

        foreach (CanvasCRTPresenter presenter in presenters)
        {
            if (!presenter.IsPresenting) continue;
            if (!(targetField?.GetValue(presenter) is RenderTexture ui) || !(screenField?.GetValue(presenter) is RawImage screen)) continue;

            string stem = FileSafe(presenter.name) + "_play";
            SaveThroughTube(ui, screen.material, Path.Combine(OutputFolder, stem + "_crt.png"));
            SavePng(ui, Path.Combine(OutputFolder, stem + "_ui.png"));
            captured.Add(presenter.name);
        }

        if (captured.Count > 0)
            Debug.Log($"[UIStylePreview] Captured {string.Join(", ", captured)} to {OutputFolder}");
        else
            Debug.LogWarning("[UIStylePreview] No tube is presenting. Presenters: " + string.Join("; ",
                presenters.Select(p => $"{p.name} enabled={p.enabled} active={p.gameObject.activeInHierarchy}")));
    }

    [MenuItem("Tools/UI/Style/Preview/Play Mode: Capture Tubes", true)]
    private static bool IsPlaying() => Application.isPlaying;

    // -- Helpers -------------------

    private static void SaveThroughTube(RenderTexture flat, Material tube, string path)
    {
        RenderTexture output = RenderTexture.GetTemporary(flat.width, flat.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;

        RenderTexture.active = output;
        GL.Clear(true, true, WorldStandIn);
        Graphics.Blit(flat, output, tube);   // the material's own blend composites it over the clear

        SavePng(output, path);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(output);
    }

    private static void SavePng(RenderTexture source, string path)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;

        Texture2D pixels = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        pixels.Apply();

        RenderTexture.active = previous;
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        Object.DestroyImmediate(pixels);
    }

    /// <summary>
    /// Every renderer feature of the active pipeline. The renderer list is private on the URP asset,
    /// hence the reflection; if a URP update renames it, this returns nothing and the render simply
    /// comes out with the effects on.
    /// </summary>
    private static IEnumerable<ScriptableRendererFeature> AllRendererFeatures()
    {
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset))
            return Enumerable.Empty<ScriptableRendererFeature>();

        FieldInfo field = typeof(UniversalRenderPipelineAsset)
            .GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);

        if (!(field?.GetValue(asset) is ScriptableRendererData[] renderers))
            return Enumerable.Empty<ScriptableRendererFeature>();

        return renderers.Where(r => r != null).SelectMany(r => r.rendererFeatures).Where(f => f != null);
    }

    private static GameObject SelectedPrefab()
    {
        if (Selection.activeObject is SO_UIStyleProfile profile) return profile.prefab;
        return Selection.activeObject is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go) ? go : null;
    }

    /// <summary>Only untitled scenes holding the preview: a saved scene is never closed from here.</summary>
    private static bool IsPreviewScene(Scene scene) =>
        scene.isLoaded && string.IsNullOrEmpty(scene.path) &&
        scene.GetRootGameObjects().Any(go => go.name == InstanceName);

    private static string FileSafe(string name) =>
        new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
}
#endif
