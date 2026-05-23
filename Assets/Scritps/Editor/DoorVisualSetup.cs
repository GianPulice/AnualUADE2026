using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Utilidad de editor: convierte el prefab Door (que es un cubo unico) en una doble puerta
/// con dos paneles deslizables (LeftPanel, RightPanel). Asigna los refs al DoorInteractable.
/// Idempotente — re-ejecutar reemplaza los paneles existentes.
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
            Debug.LogError($"[DoorVisualSetup] No se pudo cargar el prefab en {DoorPrefabPath}");
            return;
        }

        // Capturar el material del cubo original (para reutilizarlo en los paneles)
        Material doorMaterial = null;
        MeshRenderer rootMR = prefabRoot.GetComponent<MeshRenderer>();
        if (rootMR != null)
        {
            doorMaterial = rootMR.sharedMaterial;
            rootMR.enabled = false; // ocultar el cubo original
        }

        // Limpiar paneles previos si ya existen
        DestroyChildIfExists(prefabRoot, "LeftPanel");
        DestroyChildIfExists(prefabRoot, "RightPanel");

        // Crear paneles: cada uno cubre la mitad del ancho de la puerta original.
        // Posicion local relativa al centro del cubo padre (size 1x1x1 standard).
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

        // Asignar referencias en el componente DoorInteractable
        DoorInteractable door = prefabRoot.GetComponent<DoorInteractable>();
        if (door != null)
        {
            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DoorInteractable).GetField("leftPanel", bf)?.SetValue(door, leftPanel.transform);
            typeof(DoorInteractable).GetField("rightPanel", bf)?.SetValue(door, rightPanel.transform);
        }
        else
        {
            Debug.LogWarning("[DoorVisualSetup] El prefab Door no tiene DoorInteractable.");
        }

        // Guardar y descargar el prefab
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, DoorPrefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[DoorVisualSetup] Door reconfigurada: dos paneles deslizables + refs asignadas.");
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

        // El cube primitive trae un BoxCollider; lo quitamos porque el bloqueo de la puerta
        // ya esta a cargo del BoxCollider del root.
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
