#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared plumbing for the inventory redesign tools — <see cref="InventoryCloseButtonSetup"/>,
/// <see cref="InventoryThemeSetup"/>, <see cref="InventoryFrameSetup"/>,
/// <see cref="InventoryTextOutlineSetup"/>, <see cref="InventorySurfaceSetup"/>,
/// <see cref="InventorySelectionTransitionSetup"/> and <see cref="InventoryCRTSetup"/>: where the
/// assets live, and how a prefab gets opened, edited and saved without a scene.
///
/// The tools edit the prefab ASSETS directly, so nothing has to be selected and they can be run
/// from the menu or over MCP. The price is that there is no Ctrl+Z — git is the undo. They refuse
/// to touch a prefab that is open in Prefab Mode, because the open stage and the asset would drift.
///
/// Every tool adds and configures, and finds its own earlier work before adding more, so any of them
/// can be run twice. The one removal is InventoryCRTSetup taking out the PSX overlay an earlier
/// version of these tools added.
/// </summary>
public static class InventoryRedesign
{
    public const string ThemePath = "Assets/_Project/ScriptableObjects/UI/UITheme.asset";
    public const string FontPath  = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular SDF.asset";

    public const string CanvasPrefab     = "Assets/_Project/Prefabs/UI/Inventory/Inventory Canvas.prefab";
    public const string SlotPrefab       = "Assets/_Project/Prefabs/UI/Inventory/InventoryItem.prefab";
    public const string GroupLabelPrefab = "Assets/_Project/Prefabs/UI/Inventory/Group Label.prefab";
    public const string ParameterPrefab  = "Assets/_Project/Prefabs/UI/Inventory/Item Parameter 1.prefab";
    public const string ModuleRowPrefab  = "Assets/_Project/Prefabs/UI/Inventory/Module Group.prefab";

    [MenuItem("Tools/UI/Inventory/Apply Full Redesign", priority = 0)]
    private static void ApplyAll()
    {
        // The close button goes first only because the theme and frame tables expect it to exist.
        InventoryCloseButtonSetup.Apply();
        InventoryThemeSetup.Apply();
        InventoryFrameSetup.Apply();
        InventoryTextOutlineSetup.Apply();
        InventorySurfaceSetup.Apply();
        // After the frames: it re-seats the detail panel's BevelFrame on top of its own overlays.
        InventorySelectionTransitionSetup.Apply();
        InventoryCRTSetup.Apply();
    }

    public static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) Debug.LogError($"[InventoryRedesign] No {typeof(T).Name} at {path}.");
        return asset;
    }

    /// <summary>Loads the prefab in isolation, hands its root to <paramref name="edit"/>, saves.</summary>
    public static bool EditPrefab(string path, Action<GameObject> edit)
    {
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == path)
        {
            Debug.LogError($"[InventoryRedesign] '{path}' is open in Prefab Mode. Close it and run again.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// "" is the root itself. A missing node is reported, not thrown: the tables are written against
    /// the prefab as authored, and a renamed node should cost one line in the log, not the whole run.
    /// </summary>
    public static Transform Find(GameObject root, string path, StringBuilder report)
    {
        Transform node = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
        if (node == null) report.AppendLine($"  MISSING '{path}'");
        return node;
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
}
#endif
