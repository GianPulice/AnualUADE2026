#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The inventory's hooks on top of <see cref="UIStylePreview"/>: the preview copy filled with sample
/// items — an empty list shows nothing of the redesign — the doc and discard states, and driving the
/// real inventory in Play Mode.
///
/// Rendering, removing the copy and capturing tubes in Play Mode are the generic ones, under
/// Tools / UI / Style / Preview.
/// </summary>
public static class InventoryScenePreview
{
    private const string CanvasPrefab = "Assets/_Project/Prefabs/UI/Inventory/Inventory Canvas.prefab";
    private const int SampleItems = 6;

    private static readonly ItemCategory[] CategoryOrder =
        { ItemCategory.Key, ItemCategory.Component, ItemCategory.Note, ItemCategory.Special };

    // -- Edit mode -------------------

    [MenuItem("Tools/UI/Inventory/Preview/Spawn In Scene", priority = 30)]
    public static void Spawn()
    {
        GameObject prefab = UIStyleTools.LoadRequired<GameObject>(CanvasPrefab);
        if (prefab == null) return;

        GameObject root = UIStylePreview.Spawn(prefab);

        // ItemDetailView.Awake closes the doc pop-up, which is saved open in the prefab; edit mode
        // runs no Awake, so without this every preview would show a doc nobody opened.
        DocPanelView doc = root.GetComponentInChildren<DocPanelView>(true);
        if (doc != null) doc.gameObject.SetActive(false);

        List<SO_InventoryItem> items = LoadSampleItems();
        Populate(root, items);

        // The rows came in after the spawn, with SweepBars of their own.
        UIStylePreview.ForceRuntimeState(root);
        InternalEditorUtility.RepaintAllViews();
        Debug.Log($"[InventoryScenePreview] Spawned with {items.Count} item(s). Look at the Game view.");
    }

    [MenuItem("Tools/UI/Inventory/Preview/Toggle Doc", priority = 31)]
    public static void ToggleDoc()
    {
        GameObject root = UIStylePreview.FindInstance();
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
        GameObject root = UIStylePreview.FindInstance();
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

    /// <summary>Renders to Temp/UIStylePreview/Inventory_&lt;list|doc|discard&gt;[_crt].png.</summary>
    [MenuItem("Tools/UI/Inventory/Preview/Render PNG", priority = 33)]
    public static void RenderPng()
    {
        GameObject root = UIStylePreview.FindInstance();
        if (root == null) return;

        SO_UIStyleProfile profile = UIStyleTools.FindProfileFor(UIStyleTools.LoadRequired<GameObject>(CanvasPrefab));
        UIStylePreview.RenderPng(root, "Inventory_" + CurrentState(root), UIStylePreview.TubeOf(profile));
    }

    // -- Play Mode -------------------
    // The edit-mode copy never runs its components, so it cannot show anything that only exists at
    // runtime — the tube itself, the selection transition. These two drive the REAL inventory
    // instead; they need Play Mode with Data (InventoryManager) and LevelUI (InventoryManagerUI) loaded.

    [MenuItem("Tools/UI/Inventory/Preview/Play Mode: Open With Sample Items", priority = 50)]
    public static void OpenInPlayMode()
    {
        if (!InventoryManager.Exists || !InventoryManagerUI.Exists)
        {
            Debug.LogWarning("[InventoryScenePreview] Needs Play Mode with the Data and LevelUI scenes loaded.");
            return;
        }

        foreach (SO_InventoryItem item in LoadSampleItems())
            if (!InventoryManager.Instance.HasItem(item)) InventoryManager.Instance.AddItem(item);

        // Run In Background is off in Player Settings, so an unfocused editor stops the player loop:
        // the open tween would never finish and the tube would never render. On for this session only.
        Application.runInBackground = true;

        InventoryManagerUI.Instance.OpenInventory();
    }

    [MenuItem("Tools/UI/Inventory/Preview/Play Mode: Select Next Item", priority = 51)]
    public static void SelectNextInPlayMode()
    {
        if (!InventoryManager.Exists || !InventoryManagerUI.Exists) return;

        IReadOnlyList<SO_InventoryItem> items = InventoryManager.Instance.GetAllItems();
        if (items.Count == 0) return;

        int current = -1;
        for (int i = 0; i < items.Count; i++)
            if (items[i] == InventoryManagerUI.Instance.SelectedItem) current = i;

        InventoryManagerUI.Instance.SelectItem(items[(current + 1) % items.Count]);
    }

    [MenuItem("Tools/UI/Inventory/Preview/Play Mode: Open With Sample Items", true)]
    [MenuItem("Tools/UI/Inventory/Preview/Play Mode: Select Next Item", true)]
    private static bool IsPlaying() => Application.isPlaying;

    // -- Helpers -------------------

    private static string CurrentState(GameObject root)
    {
        DocPanelView doc = root.GetComponentInChildren<DocPanelView>(true);
        DiscardDialogView discard = root.GetComponentInChildren<DiscardDialogView>(true);

        if (discard != null && discard.gameObject.activeSelf) return "discard";
        if (doc != null && doc.gameObject.activeSelf) return "doc";
        return "list";
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
