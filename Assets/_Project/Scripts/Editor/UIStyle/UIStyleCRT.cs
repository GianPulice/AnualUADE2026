#if UNITY_EDITOR
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// CRT step: puts the canvas behind the tube —
///   - UI_Renderer.asset: a copy of PC_Renderer with its features stripped, appended to every
///     pipeline asset below, so the UI camera renders like the world does minus PS1 and the fog;
///   - <see cref="CanvasCRTPresenter"/> on the canvas root, pointed at the profile's content,
///     visibility group and material;
///   - <see cref="CRTWarpedRaycaster"/> in place of the root's GraphicRaycaster, so clicks land where
///     things are seen on the curved screen.
///
/// The renderer index has to exist on whichever pipeline asset the quality level picks, which is why
/// it goes on all of them, and why a mismatch between them is reported as an error.
///
/// Turning the tube off in a profile does not remove a presenter already on the prefab; it is reported.
/// </summary>
public static class UIStyleCRT
{
    private const string SourceRendererPath = "Assets/_Project/Settings/PC_Renderer.asset";
    private const string UIRendererPath = "Assets/_Project/Settings/UI_Renderer.asset";

    private static readonly string[] PipelinePaths =
    {
        "Assets/_Project/Settings/PC_RPAsset.asset",
        "Assets/_Project/Settings/Mobile_RPAsset.asset",
    };

    public static void Apply(UIStyleContext ctx, int rendererIndex)
    {
        SO_UIStyleProfile.CRTSettings crt = ctx.Profile.crt;
        if (!crt.enabled)
        {
            if (ctx.Root.GetComponent<CanvasCRTPresenter>() != null)
                ctx.Report.AppendLine("  crt: off in the profile, but the prefab has a CanvasCRTPresenter — left as is.");
            return;
        }

        if (crt.material == null)
        {
            ctx.Report.AppendLine("  SKIPPED crt — the profile has no material.");
            return;
        }

        GameObject content = string.IsNullOrEmpty(crt.contentPath) ? null : ctx.Find(crt.contentPath).gameObject;

        CanvasGroup visibility = null;
        if (crt.useVisibility)
        {
            visibility = ctx.Find(crt.visibilityPath).GetComponent<CanvasGroup>();
            if (visibility == null)
                ctx.Report.AppendLine($"  NO VISIBILITY — '{crt.visibilityPath}' has no CanvasGroup; the tube will render whenever the content is active.");
        }

        ConfigurePresenter(ctx.Root, content, visibility, crt.material, rendererIndex);
        SwapRaycaster(ctx.Root, ctx.Report);
        WarpDropdownLists(ctx);

        ctx.Report.AppendLine($"  crt: content '{(content != null ? crt.contentPath : "(always)")}', " +
                              $"visibility '{(visibility != null ? crt.visibilityPath : "(none)")}', " +
                              $"material {crt.material.name}, renderer {rendererIndex}");
    }

    // -- Renderer -------------------

    public static int EnsureUIRenderer(StringBuilder report)
    {
        UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UIRendererPath);

        if (renderer == null)
        {
            if (!AssetDatabase.CopyAsset(SourceRendererPath, UIRendererPath))
            {
                report.AppendLine($"  could not copy {SourceRendererPath}");
                return -1;
            }

            renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UIRendererPath);
            StripFeatures(renderer);
            report.AppendLine($"  created {UIRendererPath} (PC_Renderer without features)");
        }

        int index = -1;
        foreach (string path in PipelinePaths)
        {
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null)
            {
                report.AppendLine($"  MISSING pipeline asset {path}");
                continue;
            }

            int at = AddRenderer(pipeline, renderer);
            if (index < 0) index = at;
            else if (at != index)
                Debug.LogError($"[UIStyle] The UI renderer is at index {index} in one pipeline asset and {at} " +
                               $"in {path}; the presenter holds a single index. Line them up by hand.");
        }

        return index;
    }

    private static void StripFeatures(UniversalRendererData renderer)
    {
        foreach (ScriptableRendererFeature feature in renderer.rendererFeatures.ToList())
        {
            if (feature == null) continue;
            AssetDatabase.RemoveObjectFromAsset(feature);
            Object.DestroyImmediate(feature, true);
        }
        renderer.rendererFeatures.Clear();

        SerializedObject serialized = new SerializedObject(renderer);
        serialized.FindProperty("m_RendererFeatureMap").ClearArray();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Appends the renderer to the pipeline's list unless it is already there; returns its index.</summary>
    private static int AddRenderer(UniversalRenderPipelineAsset pipeline, ScriptableRendererData renderer)
    {
        SerializedObject serialized = new SerializedObject(pipeline);
        SerializedProperty list = serialized.FindProperty("m_RendererDataList");

        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) return i;

        int at = list.arraySize;
        list.arraySize++;
        list.GetArrayElementAtIndex(at).objectReferenceValue = renderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();
        return at;
    }

    // -- Prefab -------------------

    private static void ConfigurePresenter(GameObject root, GameObject content, CanvasGroup visibility, Material material, int rendererIndex)
    {
        CanvasCRTPresenter presenter = UIStyleTools.GetOrAdd<CanvasCRTPresenter>(root);

        SerializedObject serialized = new SerializedObject(presenter);
        serialized.FindProperty("content").objectReferenceValue = content;
        serialized.FindProperty("visibility").objectReferenceValue = visibility;
        serialized.FindProperty("screenMaterial").objectReferenceValue = material;
        serialized.FindProperty("rendererIndex").intValue = rendererIndex;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SwapRaycaster(GameObject root, StringBuilder report)
    {
        if (root.GetComponent<CRTWarpedRaycaster>() != null) return;

        bool ignoreReversed = true;
        GraphicRaycaster.BlockingObjects blocking = GraphicRaycaster.BlockingObjects.None;

        GraphicRaycaster old = root.GetComponent<GraphicRaycaster>();
        if (old != null)
        {
            ignoreReversed = old.ignoreReversedGraphics;
            blocking = old.blockingObjects;
            Object.DestroyImmediate(old);
        }

        CRTWarpedRaycaster raycaster = root.AddComponent<CRTWarpedRaycaster>();
        raycaster.ignoreReversedGraphics = ignoreReversed;
        raycaster.blockingObjects = blocking;

        report.AppendLine("  GraphicRaycaster replaced by CRTWarpedRaycaster");
    }

    /// <summary>
    /// A dropdown's open list is a canvas of its own, built at runtime from the template with a
    /// GraphicRaycaster that knows nothing of the tube: items near the edges would answer where they
    /// sit, not where they are seen. TMP_Dropdown keeps whatever Canvas and GraphicRaycaster the
    /// template already has, so a CRTWarpedRaycaster on the template carries over to every list.
    /// </summary>
    private static void WarpDropdownLists(UIStyleContext ctx)
    {
        int warped = 0;
        foreach (TMP_Dropdown dropdown in ctx.Root.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dropdown.template == null) continue;

            GameObject template = dropdown.template.gameObject;
            if (template.GetComponent<CRTWarpedRaycaster>() != null) continue;

            UIStyleTools.GetOrAdd<Canvas>(template);   // TMP sets its sorting when the list opens
            GraphicRaycaster plain = template.GetComponent<GraphicRaycaster>();
            if (plain != null) Object.DestroyImmediate(plain);
            template.AddComponent<CRTWarpedRaycaster>();
            warped++;
        }

        if (warped > 0) ctx.Report.AppendLine($"  crt: {warped} dropdown list(s) given a CRTWarpedRaycaster");
    }
}
#endif
