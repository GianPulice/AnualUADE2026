#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Applies <see cref="SO_UIStyleProfile"/>s: opens each profile's prefab, checks every path the
/// profile names, runs the steps and saves.
///
///   nested overrides → theme → fills → frames → texts → surfaces → transition → CRT
///
/// Nested overrides go first so the theme can write the ones the profile wants back on top; the
/// transition goes after the frames because it re-seats a panel's BevelFrame on top of its own
/// overlays. Each step lives in its own class (UIStyleNestedOverrides, UIStyleTheme, UIStyleFills,
/// UIStyleFrames, UIStyleText, UIStyleSurfaces, UIStyleTransition, UIStyleCRT) and finds its own
/// earlier work before adding more, so applying twice changes nothing.
///
/// The prefab ASSETS are edited directly, so nothing has to be selected in a scene and it runs from
/// the menu or over MCP. There is no Ctrl+Z — git is the undo. A prefab open in Prefab Mode is
/// refused: the open stage and the asset would drift apart. Nothing here removes an asset, and the
/// only nodes removed are a step's own earlier work that a profile moved (the transition's overlays).
/// </summary>
public static class UIStyleTools
{
    public const string ThemePath = "Assets/_Project/ScriptableObjects/UI/UITheme.asset";
    public const string ProfileFolder = "Assets/_Project/ScriptableObjects/UI/Style";
    public const string CRTMaterialPath = "Assets/_Project/Art/Materials/UI/UI_CRT.mat";
    public const string SurfaceMaterialPath = "Assets/_Project/Art/Materials/UI/InventorySurface.mat";

    // Children the steps create. The drafter skips them, so a styled prefab drafts like a plain one.
    public const string FrameName = "BevelFrame";
    public const string StaticName = "SignalStatic";
    public const string ScanBarName = "SignalScanBar";

    // -- Menu -------------------

    [MenuItem("Tools/UI/Style/Apply Selected Profile(s)", priority = 0)]
    private static void ApplySelected() => Apply(Selection.GetFiltered<SO_UIStyleProfile>(SelectionMode.Assets));

    [MenuItem("Tools/UI/Style/Apply Selected Profile(s)", true)]
    private static bool HasSelectedProfiles() => Selection.GetFiltered<SO_UIStyleProfile>(SelectionMode.Assets).Length > 0;

    [MenuItem("Tools/UI/Style/Apply All Profiles", priority = 1)]
    public static void ApplyAll() => Apply(FindAllProfiles());

    // -- Apply -------------------

    public static void Apply(IEnumerable<SO_UIStyleProfile> profiles)
    {
        SO_UIThemeConfig theme = LoadRequired<SO_UIThemeConfig>(ThemePath);
        if (theme == null) return;

        foreach (SO_UIStyleProfile profile in profiles) ApplyOne(profile, theme);
        AssetDatabase.SaveAssets();
    }

    private static void ApplyOne(SO_UIStyleProfile profile, SO_UIThemeConfig theme)
    {
        string path = profile.prefab != null ? AssetDatabase.GetAssetPath(profile.prefab) : null;
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError($"[UIStyle] {profile.name}: no prefab assigned.", profile);
            return;
        }

        StringBuilder report = new StringBuilder();

        // Before opening the prefab: it may create the renderer asset and edit the pipeline assets.
        int rendererIndex = -1;
        if (profile.crt.enabled)
        {
            rendererIndex = UIStyleCRT.EnsureUIRenderer(report);
            if (rendererIndex < 0)
            {
                Debug.LogError($"[UIStyle] {profile.name}: no UI renderer, nothing changed.\n{report}", profile);
                return;
            }
        }

        bool saved = EditPrefab(path, report, root =>
        {
            if (!Validate(profile, root, report)) return false;

            UIStyleContext ctx = new UIStyleContext(profile, root, theme, report);
            UIStyleNestedOverrides.Apply(ctx);
            UIStyleTheme.Apply(ctx);
            UIStyleFills.Apply(ctx);
            UIStyleFrames.Apply(ctx);
            UIStyleText.Apply(ctx);
            UIStyleSurfaces.Apply(ctx);
            UIStyleTransition.Apply(ctx);
            UIStyleCRT.Apply(ctx, rendererIndex);
            ReportCanvas(ctx);
            return true;
        });

        if (saved) Debug.Log($"[UIStyle] {profile.name} → {path}\n{report}", profile);
        else Debug.LogError($"[UIStyle] {profile.name} → {path}: NOT SAVED\n{report}", profile);
    }

    /// <summary>
    /// Every path must resolve before anything is touched: a partial pass would leave the prefab half
    /// on the new look with nothing in the log pointing at the half that was missed.
    /// </summary>
    private static bool Validate(SO_UIStyleProfile profile, GameObject root, StringBuilder report)
    {
        bool ok = true;
        HashSet<string> seen = new HashSet<string>();

        foreach ((string path, string usedBy) in profile.AllPaths())
        {
            if (!seen.Add(path + "\n" + usedBy)) continue;

            Transform node = Resolve(root.transform, path, out bool ambiguous);
            if (node == null)
            {
                report.AppendLine($"  MISSING '{path}' ({usedBy})");
                ok = false;
                continue;
            }

            if (ambiguous)
                report.AppendLine($"  AMBIGUOUS '{path}' ({usedBy}) — siblings share a name; the first one is used.");
            if (IsInNestedPrefab(node, root))
                report.AppendLine($"  OVERRIDE '{path}' ({usedBy}) — inside a nested prefab; this becomes an instance override.");
        }

        if (!ok) report.Insert(0, "  Stopped before saving — nothing was changed. Fix these paths:\n");
        return ok;
    }

    /// <summary>
    /// Things worth knowing about the canvas that no step changes: the gamma-space flag the UI
    /// shaders depend on, frames that lost their CanvasRenderer, and — for the tube — nodes off the
    /// canvas's layer, which a layer-masked camera would drop.
    /// </summary>
    private static void ReportCanvas(UIStyleContext ctx)
    {
        Canvas canvas = ctx.Root.GetComponent<Canvas>();
        if (canvas != null)
            ctx.Report.AppendLine($"  canvas: vertex colour always in gamma space = {canvas.vertexColorAlwaysGammaSpace}");

        foreach (UIBevelFrame frame in ctx.Root.GetComponentsInChildren<UIBevelFrame>(true))
            if (frame.GetComponent<CanvasRenderer>() == null)
                ctx.Report.AppendLine($"  FRAME WITHOUT CanvasRenderer '{PathOf(frame.transform, ctx.Root.transform)}'");

        if (!ctx.Profile.crt.enabled) return;

        int layer = ctx.Root.layer;
        string[] offLayer = ctx.Root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.gameObject.layer != layer)
            .GroupBy(t => t.name)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key)
            .ToArray();
        if (offLayer.Length > 0)
            ctx.Report.AppendLine($"  off the {LayerMask.LayerToName(layer)} layer (the tube camera renders every layer): {string.Join(", ", offLayer)}");
    }

    // -- Prefab plumbing -------------------

    /// <summary>
    /// Loads the prefab in isolation and hands its root to <paramref name="edit"/>; saves only when
    /// the edit returns true.
    /// </summary>
    public static bool EditPrefab(string path, StringBuilder report, Func<GameObject, bool> edit)
    {
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == path)
        {
            report.AppendLine($"  '{path}' is open in Prefab Mode. Close it and run again.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!edit(root)) return false;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Transform.Find, one name at a time, noting whether any step had two children of that name —
    /// Find silently takes the first, and a profile written against the other would style the wrong node.
    /// </summary>
    public static Transform Resolve(Transform root, string path, out bool ambiguous)
    {
        ambiguous = false;
        if (string.IsNullOrEmpty(path)) return root;

        Transform node = root;
        foreach (string name in path.Split('/'))
        {
            Transform match = null;
            foreach (Transform child in node)
            {
                if (child.name != name) continue;
                if (match == null) match = child;
                else ambiguous = true;
            }

            if (match == null) return null;
            node = match;
        }
        return node;
    }

    public static string PathOf(Transform node, Transform root)
    {
        if (node == root) return "";

        List<string> names = new List<string>();
        for (Transform t = node; t != null && t != root; t = t.parent) names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>
    /// True for nodes that belong to another prefab nested in this one. Their look comes from that
    /// prefab's own profile; touching them here would write instance overrides.
    /// </summary>
    public static bool IsInNestedPrefab(Transform node, GameObject root)
    {
        GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(node.gameObject);
        return instanceRoot != null && instanceRoot != root;
    }

    /// <summary>
    /// A new UI GameObject parented under <paramref name="parent"/>. It is moved into the parent's
    /// scene first: new GameObjects are born in the active scene, not in the isolated one that
    /// LoadPrefabContents uses.
    /// </summary>
    public static RectTransform CreateUIChild(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    public static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    public static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) Debug.LogError($"[UIStyle] No {typeof(T).Name} at {path}.");
        return asset;
    }

    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    public static SO_UIStyleProfile[] FindAllProfiles() =>
        AssetDatabase.FindAssets("t:" + nameof(SO_UIStyleProfile))
            .Select(guid => AssetDatabase.LoadAssetAtPath<SO_UIStyleProfile>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(profile => profile != null)
            .OrderBy(profile => profile.name)
            .ToArray();

    public static SO_UIStyleProfile FindProfileFor(GameObject prefab) =>
        FindAllProfiles().FirstOrDefault(profile => profile.prefab == prefab);
}

/// <summary>What every step gets: the profile, the loaded prefab root, the theme and the report.</summary>
public sealed class UIStyleContext
{
    public readonly SO_UIStyleProfile Profile;
    public readonly GameObject Root;
    public readonly SO_UIThemeConfig Theme;
    public readonly StringBuilder Report;

    public UIStyleContext(SO_UIStyleProfile profile, GameObject root, SO_UIThemeConfig theme, StringBuilder report)
    {
        Profile = profile;
        Root = root;
        Theme = theme;
        Report = report;
    }

    /// <summary>Paths were validated before any step ran, so this only returns null for "" misuse.</summary>
    public Transform Find(string path) => UIStyleTools.Resolve(Root.transform, path, out _);
}
#endif
