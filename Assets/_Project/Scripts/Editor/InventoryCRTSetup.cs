#if UNITY_EDITOR
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Puts the inventory behind the CRT tube:
///   - UI_Renderer.asset: a copy of PC_Renderer with its features stripped, appended to every
///     pipeline asset below, so the UI camera renders like the world does minus PS1 and the fog.
///   - <see cref="CanvasCRTPresenter"/> on the canvas root, pointed at LAYOUT and InventoryPSX.mat.
///   - <see cref="CRTWarpedRaycaster"/> in place of the root's GraphicRaycaster.
///
/// It also deletes LAYOUT/PSXOverlay, the multiply overlay the previous pass added: the tube does
/// everything it did and more, and both at once would stripe the UI twice. That node is the only
/// thing any of the redesign tools removes, and only because a redesign tool put it there.
///
/// The renderer index has to exist on whichever pipeline asset the quality level picks, which is why
/// it goes on all of them, and why a mismatch between them is reported as an error.
///
/// USAGE: Tools / UI / Inventory / Add CRT Screen. See <see cref="InventoryRedesign"/>.
/// </summary>
public static class InventoryCRTSetup
{
    public const string MaterialPath = "Assets/_Project/Art/Materials/UI/InventoryPSX.mat";

    private const string SourceRendererPath = "Assets/_Project/Settings/PC_Renderer.asset";
    private const string UIRendererPath = "Assets/_Project/Settings/UI_Renderer.asset";

    private static readonly string[] PipelinePaths =
    {
        "Assets/_Project/Settings/PC_RPAsset.asset",
        "Assets/_Project/Settings/Mobile_RPAsset.asset",
    };

    private const string ContentPath = "LAYOUT";
    private const string OldOverlayPath = "LAYOUT/PSXOverlay";

    [MenuItem("Tools/UI/Inventory/Add CRT Screen", priority = 17)]
    public static void Apply()
    {
        Material material = InventoryRedesign.LoadRequired<Material>(MaterialPath);
        if (material == null) return;

        StringBuilder report = new StringBuilder();
        int rendererIndex = EnsureUIRenderer(report);
        if (rendererIndex < 0)
        {
            Debug.LogError($"[InventoryCRTSetup] No UI renderer, nothing changed.\n{report}");
            return;
        }

        InventoryRedesign.EditPrefab(InventoryRedesign.CanvasPrefab, root =>
        {
            RemoveOldOverlay(root, report);

            Transform content = InventoryRedesign.Find(root, ContentPath, report);
            if (content == null) return;

            ConfigurePresenter(root, content.gameObject, material, rendererIndex);
            SwapRaycaster(root, report);
        });

        Debug.Log($"[InventoryCRTSetup] Done — UI camera renderer index {rendererIndex}.\n{report}");
    }

    // -- Renderer -------------------

    private static int EnsureUIRenderer(StringBuilder report)
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
            report.AppendLine($"  {path}: UI renderer at index {at}");

            if (index < 0) index = at;
            else if (at != index)
                Debug.LogError($"[InventoryCRTSetup] The UI renderer is at index {index} in one pipeline " +
                               $"asset and {at} in {path}; the presenter holds a single index. Line them up by hand.");
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

    private static void RemoveOldOverlay(GameObject root, StringBuilder report)
    {
        Transform old = root.transform.Find(OldOverlayPath);
        if (old == null) return;

        Object.DestroyImmediate(old.gameObject);
        report.AppendLine($"  removed {OldOverlayPath} (the tube replaces it)");
    }

    private static void ConfigurePresenter(GameObject root, GameObject content, Material material, int rendererIndex)
    {
        CanvasCRTPresenter presenter = root.GetComponent<CanvasCRTPresenter>();
        if (presenter == null) presenter = root.AddComponent<CanvasCRTPresenter>();

        SerializedObject serialized = new SerializedObject(presenter);
        serialized.FindProperty("content").objectReferenceValue = content;
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
}
#endif
