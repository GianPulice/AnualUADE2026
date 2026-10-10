using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GameObject > WIRED > Look Down Trigger: builds a <see cref="LookDownTrigger"/> — the volume, its
/// camera as a child and the wiring between them — so there is one per spot without copying and
/// re-hooking by hand. Not a one-shot builder: it is the way to add the next spot.
///
/// The new trigger lands at the Scene view pivot (or under the selected object), with a camera
/// above and behind it already pitched down. That camera is a placeholder: frame the real shot by
/// selecting it, flying the Scene view to where the view should be and pressing Ctrl+Shift+F
/// (GameObject > Align With View).
/// </summary>
public static class LookDownTriggerMenu
{
    private const string MenuPath = "GameObject/WIRED/Look Down Trigger";

    // Standing player: tall enough to catch the capsule, wide enough not to need pixel-perfect aim.
    private static readonly Vector3 VolumeSize = new Vector3(3f, 2.5f, 3f);

    // Where the placeholder camera starts, relative to the trigger, and how far it already looks down.
    private static readonly Vector3 CameraOffset = new Vector3(0f, 2.2f, -1.5f);
    private const float CameraPitch = 55f;

    [MenuItem(MenuPath, false, 10)]
    private static void Create(MenuCommand command)
    {
        GameObject root = new GameObject("LookDownTrigger");
        Undo.RegisterCreatedObjectUndo(root, "Create Look Down Trigger");

        // A right-click on an object parents the trigger to it; the menu bar creates it at the root.
        GameObject context = command.context as GameObject;
        if (context != null) GameObjectUtility.SetParentAndAlign(root, context);
        else root.transform.position = SceneViewPivot();

        BoxCollider volume = Undo.AddComponent<BoxCollider>(root);
        volume.isTrigger = true;
        volume.center = new Vector3(0f, VolumeSize.y * 0.5f, 0f);
        volume.size = VolumeSize;

        LookDownTrigger trigger = Undo.AddComponent<LookDownTrigger>(root);

        GameObject cameraObject = new GameObject("Look Camera");
        Undo.RegisterCreatedObjectUndo(cameraObject, "Create Look Down Trigger");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = CameraOffset;
        cameraObject.transform.localRotation = Quaternion.Euler(CameraPitch, 0f, 0f);
        Undo.AddComponent<CinemachineCamera>(cameraObject);

        SerializedObject serialized = new SerializedObject(trigger);
        serialized.FindProperty("lookCamera").objectReferenceValue = cameraObject.GetComponent<CinemachineCamera>();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // The camera is what gets framed next, and selecting it draws the volume and the frustum.
        Selection.activeGameObject = cameraObject;
    }

    private static Vector3 SceneViewPivot()
    {
        SceneView view = SceneView.lastActiveSceneView;
        return view != null ? view.pivot : Vector3.zero;
    }
}
