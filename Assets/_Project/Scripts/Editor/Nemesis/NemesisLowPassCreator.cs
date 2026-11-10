using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds a low pass (<see cref="NemesisBreakLink"/>) as a blockout: a slab hanging across a passage
/// with its underside at crouch height, the NavMesh cut under it, and the two ends of the Nemesis's
/// crossing. <c>GameObject > WIRED > Nemesis > Low Pass</c> puts one where the Scene view is looking
/// (or on the selected object), facing the way the view faces; swap the slab's mesh for the real
/// shutter afterwards and keep the hierarchy.
/// </summary>
public static class NemesisLowPassCreator
{
    /// <summary>Underside over the floor: the player's crouch is 0.8 m.</summary>
    private const float Underside = 0.95f;

    /// <summary>Top over the floor: over the Nemesis's head, so it reads as a wall to it.</summary>
    private const float Top = 2.5f;

    private const float Thickness = 0.12f;
    private const float SideDistance = 1.2f;

    [MenuItem("GameObject/WIRED/Nemesis/Low Pass", false, 10)]
    private static void CreateFromMenu()
    {
        Vector3 position;
        Quaternion rotation = Quaternion.identity;

        if (Selection.activeTransform != null)
        {
            position = Selection.activeTransform.position;
            rotation = Quaternion.Euler(0f, Selection.activeTransform.eulerAngles.y, 0f);
        }
        else if (SceneView.lastActiveSceneView != null)
        {
            position = SceneView.lastActiveSceneView.pivot;
            rotation = Quaternion.Euler(0f, SceneView.lastActiveSceneView.rotation.eulerAngles.y, 0f);
        }
        else
        {
            position = Vector3.zero;
        }

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) position = hit.position;

        GameObject root = Create(position, rotation, 2f);
        Undo.RegisterCreatedObjectUndo(root, "Create Low Pass");
        Selection.activeGameObject = root;
    }

    /// <summary>A low pass standing on <paramref name="floorPoint"/>, crossed along the rotation's
    /// forward, <paramref name="width"/> metres wide.</summary>
    public static GameObject Create(Vector3 floorPoint, Quaternion rotation, float width)
    {
        // Inactive while it is put together: NemesisBreakLink reads its pieces in Awake.
        var root = new GameObject("LowPass");
        root.SetActive(false);
        root.transform.SetPositionAndRotation(floorPoint, rotation);

        GameObject barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrier.name = "Barrier";
        barrier.layer = LayerMask.NameToLayer("Wall");
        barrier.transform.SetParent(root.transform, false);
        barrier.transform.localPosition = new Vector3(0f, (Underside + Top) * 0.5f, 0f);
        barrier.transform.localScale = new Vector3(width, Top - Underside, Thickness);

        // From the floor up, so the cut is made whatever the carving does about height.
        var navBlock = new GameObject("NavBlock");
        navBlock.transform.SetParent(barrier.transform, false);
        NavMeshObstacle obstacle = navBlock.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        float barrierHeight = Top - Underside;
        obstacle.center = new Vector3(0f, (Top * 0.5f - (Underside + Top) * 0.5f) / barrierHeight, 0f);
        obstacle.size = new Vector3(1f, Top / barrierHeight, 0.5f / Thickness);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;

        Transform sideA = NewSide(root.transform, "SideA", -SideDistance);
        Transform sideB = NewSide(root.transform, "SideB", SideDistance);

        root.AddComponent<NavMeshLink>();
        NemesisBreakLink link = root.AddComponent<NemesisBreakLink>();
        var serialized = new SerializedObject(link);
        serialized.FindProperty("barrier").objectReferenceValue = barrier.transform;
        serialized.FindProperty("sideA").objectReferenceValue = sideA;
        serialized.FindProperty("sideB").objectReferenceValue = sideB;
        serialized.FindProperty("linkWidth").floatValue = Mathf.Max(0.6f, width - 0.5f);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(true);
        return root;
    }

    private static Transform NewSide(Transform parent, string name, float along)
    {
        var side = new GameObject(name);
        side.transform.SetParent(parent, false);
        side.transform.localPosition = new Vector3(0f, 0f, along);
        return side.transform;
    }
}
