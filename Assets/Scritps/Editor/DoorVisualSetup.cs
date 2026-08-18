using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility: turns the Door prefab (which is a single cube) into a double door with two
/// sliding panels (LeftPanel, RightPanel). Assigns the refs on the DoorInteractable.
/// Idempotent — re-running it replaces the existing panels.
/// Menu: Tools / Door / Setup Door Visual
/// </summary>
public static class DoorVisualSetup
{
    private const string DoorPrefabPath = "Assets/Prefabs/DoorFather/Door.prefab";

    [MenuItem("Tools/Door/Setup Door Visual")]
    public static void Build()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(DoorPrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[DoorVisualSetup] Could not load the prefab at {DoorPrefabPath}");
            return;
        }

        // Capture the original cube's material (to reuse it on the panels)
        Material doorMaterial = null;
        MeshRenderer rootMR = prefabRoot.GetComponent<MeshRenderer>();
        if (rootMR != null)
        {
            doorMaterial = rootMR.sharedMaterial;
            rootMR.enabled = false; // hide the original cube
        }

        // Clean up previous panels if they already exist
        DestroyChildIfExists(prefabRoot, "LeftPanel");
        DestroyChildIfExists(prefabRoot, "RightPanel");

        // Create the panels: each covers half the width of the original door.
        // Local position relative to the centre of the parent cube (standard 1x1x1 size).
        GameObject leftPanel = CreatePanel(
            prefabRoot.transform, "LeftPanel",
            localPos: new Vector3(-0.25f, 0f, 0f),
            localScale: new Vector3(0.5f, 1f, 1f),
            mat: doorMaterial);

        GameObject rightPanel = CreatePanel(
            prefabRoot.transform, "RightPanel",
            localPos: new Vector3(0.25f, 0f, 0f),
            localScale: new Vector3(0.5f, 1f, 1f),
            mat: doorMaterial);

        // Assign the references on the DoorInteractable component
        DoorInteractable door = prefabRoot.GetComponent<DoorInteractable>();
        if (door != null)
        {
            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DoorInteractable).GetField("leftPanel", bf)?.SetValue(door, leftPanel.transform);
            typeof(DoorInteractable).GetField("rightPanel", bf)?.SetValue(door, rightPanel.transform);
        }
        else
        {
            Debug.LogWarning("[DoorVisualSetup] The Door prefab does not have a DoorInteractable.");
        }

        // Save and unload the prefab
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, DoorPrefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[DoorVisualSetup] Door reconfigured: two sliding panels + refs assigned.");
    }

    private static GameObject CreatePanel(Transform parent, string panelName,
        Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = panelName;
        panel.transform.SetParent(parent, worldPositionStays: false);
        panel.transform.localPosition = localPos;
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = localScale;

        // The cube primitive comes with a BoxCollider; we remove it because the door's blocking
        // is already handled by the root's BoxCollider.
        Collider col = panel.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        if (mat != null)
        {
            MeshRenderer mr = panel.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        return panel;
    }

    private static void DestroyChildIfExists(GameObject parent, string childName)
    {
        Transform existing = parent.transform.Find(childName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
    }
}
