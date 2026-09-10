#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drops a throwaway, filled-in copy of the Inventory Canvas into the editor, so the redesign can be
/// looked at in the Game view — over the real scene, PS1 on the world and not on the UI, exactly as
/// in play — without entering Play Mode and picking things up.
///
/// The copy lives in its own untitled additive scene, so the open level is never touched or dirtied,
/// and the prefab is never modified. Remove closes that scene without saving. Entering Play Mode
/// removes it too: a stray copy would otherwise play as a second, dead inventory drawn over the game.
///
/// An offscreen render into a texture was tried first and dropped: URP draws nothing for a UI canvas
/// that lives in an editor preview scene, so every shot came out blank.
/// </summary>
[InitializeOnLoad]
public static class InventoryScenePreview
{
    private const string InstanceName = "__InventoryPreview";
    private const int SampleItems = 6;

    private static readonly ItemCategory[] CategoryOrder =
        { ItemCategory.Key, ItemCategory.Component, ItemCategory.Note, ItemCategory.Special };

    static InventoryScenePreview()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Remove();
        };
    }

    // -- Menu -------------------

    [MenuItem("Tools/UI/Inventory/Preview/Spawn In Scene", priority = 30)]
    public static void Spawn()
    {
        Remove();

        GameObject prefab = InventoryRedesign.LoadRequired<GameObject>(InventoryRedesign.CanvasPrefab);
        if (prefab == null) return;

        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject root = Object.Instantiate(prefab);
        root.name = InstanceName;
        SceneManager.MoveGameObjectToScene(root, scene);

        // An additive NewScene becomes the active scene; hand that back, or whatever gets created
        // next — by hand or by another tool — lands in a scene that is about to be thrown away.
        SceneManager.SetActiveScene(previous);

        List<SO_InventoryItem> items = LoadSampleItems();
        Populate(root, items);

        InternalEditorUtility.RepaintAllViews();
        Debug.Log($"[InventoryScenePreview] Spawned with {items.Count} item(s). Look at the Game view.");
    }

    [MenuItem("Tools/UI/Inventory/Preview/Toggle Doc", priority = 31)]
    public static void ToggleDoc()
    {
        GameObject root = FindInstance();
        DocPanelView doc = root != null ? root.GetComponentInChildren<DocPanelView>(true) : null;
        if (doc == null) return;

        if (!doc.gameObject.activeSelf)
        {
            SO_InventoryItem note = LoadSampleItems().FirstOrDefault(i => i.ContentType == ItemContentType.Text);
            if (note != null) doc.SetContent(note.ItemName, note.TextContent);
        }

        doc.gameObject.SetActive(!doc.gameObject.activeSelf);
        InternalEditorUtility.RepaintAllViews();
    }

    [MenuItem("Tools/UI/Inventory/Preview/Toggle Discard", priority = 32)]
    public static void ToggleDiscard()
    {
        GameObject root = FindInstance();
        DiscardDialogView discard = root != null ? root.GetComponentInChildren<DiscardDialogView>(true) : null;
        if (discard == null) return;

        if (discard.gameObject.activeSelf)
        {
            discard.Hide();
            discard.gameObject.SetActive(false);
        }
        else
        {
            SO_InventoryItem item = LoadSampleItems().FirstOrDefault();
            discard.gameObject.SetActive(true);
            if (item != null) discard.Show(item);
        }

        InternalEditorUtility.RepaintAllViews();
    }

    [MenuItem("Tools/UI/Inventory/Preview/Remove", priority = 33)]
    public static void Remove()
    {
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsPreviewScene(scene)) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Writes the preview as it stands to &lt;project&gt;/Temp/InventoryPreview/&lt;state&gt;.png at the
    /// 1920x1080 reference — for when the Game view cannot be looked at directly.
    ///
    /// An Overlay canvas cannot render into a texture, so for this one synchronous call the copy is
    /// switched to Screen Space - Camera on a temporary camera, and the pipeline's renderer features
    /// (the PS1 pass, the fog) are switched off so they do not land on the UI — which in the game they
    /// never do. Everything is put back before the method returns; nothing is marked dirty.
    /// </summary>
    [MenuItem("Tools/UI/Inventory/Preview/Render PNG", priority = 34)]
    public static void RenderPng()
    {
        GameObject root = FindInstance();
        if (root == null) return;

        const int width = 1920, height = 1080;
        Canvas canvas = root.GetComponent<Canvas>();
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();

        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        GameObject cameraGo = new GameObject("__InventoryPreviewCamera");
        SceneManager.MoveGameObjectToScene(cameraGo, root.scene);

        Camera camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.32f, 0.30f, 0.27f); // stand-in for the world behind the Dim
        camera.cullingMask = 1 << root.layer;
        camera.targetTexture = target;
        camera.enabled = false;

        List<(ScriptableRendererFeature feature, bool wasActive)> features = AllRendererFeatures()
            .Select(f => (f, f.isActive)).ToList();

        RenderMode previousMode = canvas.renderMode;
        bool scalerWasEnabled = scaler != null && scaler.enabled;
        string path = Path.GetFullPath($"Temp/InventoryPreview/{CurrentState(root)}.png");

        try
        {
            foreach ((ScriptableRendererFeature feature, bool _) in features) feature.SetActive(false);

            // The texture is already at the reference resolution; the scaler would size from the Game view.
            if (scaler != null) scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.scaleFactor = 1f;

            Canvas.ForceUpdateCanvases();
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels);
        }
        finally
        {
            foreach ((ScriptableRendererFeature feature, bool wasActive) in features) feature.SetActive(wasActive);

            canvas.renderMode = previousMode;
            canvas.worldCamera = null;
            if (scaler != null) scaler.enabled = scalerWasEnabled;

            Object.DestroyImmediate(cameraGo);
            target.Release();
            Object.DestroyImmediate(target);
        }

        Debug.Log($"[InventoryScenePreview] Rendered {path}");
    }

    // -- Helpers -------------------

    private static string CurrentState(GameObject root)
    {
        DocPanelView doc = root.GetComponentInChildren<DocPanelView>(true);
        DiscardDialogView discard = root.GetComponentInChildren<DiscardDialogView>(true);

        if (discard != null && discard.gameObject.activeSelf) return "discard";
        if (doc != null && doc.gameObject.activeSelf) return "doc";
        return "list";
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

    /// <summary>Only untitled scenes holding the preview: a saved scene is never closed from here.</summary>
    private static bool IsPreviewScene(Scene scene) =>
        scene.isLoaded && string.IsNullOrEmpty(scene.path) &&
        scene.GetRootGameObjects().Any(go => go.name == InstanceName);

    private static GameObject FindInstance()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!IsPreviewScene(scene)) continue;
            return scene.GetRootGameObjects().First(go => go.name == InstanceName);
        }

        Debug.LogWarning("[InventoryScenePreview] No preview in the scene. Run Spawn In Scene first.");
        return null;
    }

    private static List<SO_InventoryItem> LoadSampleItems()
    {
        return AssetDatabase.FindAssets("t:SO_InventoryItem")
            .Select(guid => AssetDatabase.LoadAssetAtPath<SO_InventoryItem>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item != null)
            .OrderBy(item => item.name)
            .GroupBy(item => item.Category)
            .SelectMany(group => group.Take(2))   // a spread of categories beats six of the same
            .Take(SampleItems)
            .ToList();
    }

    /// <summary>Mirrors InventoryView.RefreshList without needing an InventoryManager.</summary>
    private static void Populate(GameObject root, List<SO_InventoryItem> items)
    {
        SerializedObject view = new SerializedObject(root.GetComponent<InventoryView>());
        Transform container = view.FindProperty("itemListContainer").objectReferenceValue as Transform;
        ItemSlotView slotPrefab = view.FindProperty("itemSlotPrefab").objectReferenceValue as ItemSlotView;
        GroupLabelView labelPrefab = view.FindProperty("groupLabelPrefab").objectReferenceValue as GroupLabelView;
        TextMeshProUGUI header = view.FindProperty("listHeaderText").objectReferenceValue as TextMeshProUGUI;
        int headerWidth = view.FindProperty("headerCharWidth").intValue;

        if (header != null)
            header.text = InventoryTextFormat.DotLeader("// INVENTORY", items.Count.ToString("00") + " OBJ", headerWidth);

        int row = 1;
        ItemSlotView first = null;

        foreach (ItemCategory category in CategoryOrder)
        {
            List<SO_InventoryItem> group = items.Where(i => i.Category == category).ToList();
            if (group.Count == 0) continue;

            Object.Instantiate(labelPrefab, container).Setup(category);

            foreach (SO_InventoryItem item in group)
            {
                ItemSlotView slot = Object.Instantiate(slotPrefab, container);
                slot.Setup(item, row++, null);
                if (first == null) first = slot;
            }
        }

        // ButtonHoverSweepEffect collapses its bar in Awake, which does not run in edit mode; left
        // alone, every button would preview as a solid red block it never shows in the game.
        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == "SweepBar") node.gameObject.SetActive(false);

        if (first == null) return;

        // The selection fill animates in Update, which does not run in edit mode; show its end state.
        Image fill = new SerializedObject(first).FindProperty("selectionFillImage").objectReferenceValue as Image;
        if (fill != null)
        {
            fill.fillAmount = 1f;
            Color c = fill.color;
            c.a = 1f;
            fill.color = c;
        }

        ItemDetailView detail = root.GetComponentInChildren<ItemDetailView>(true);
        if (detail != null) detail.ShowDetail(first.Item);

        if (container is RectTransform content) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }
}
#endif
