#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The player's usual inspector plus one button per <see cref="PlayerSpawnPoint"/> in the open
/// scenes: it moves the Player GameObject there in the scene (undoable), facing the point's blue
/// arrow, so Play starts from it. Edit mode only.
/// </summary>
[CustomEditor(typeof(PlayerStateManager))]
public class PlayerStateManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Move the player to", EditorStyles.boldLabel);

        PlayerSpawnPoint[] points = FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Exclude)
                                    .OrderBy(p => p.name).ToArray();
        if (points.Length == 0)
        {
            EditorGUILayout.HelpBox("No PlayerSpawnPoint in the open scenes. Add the component to an " +
                                    "empty GameObject to get a button here.", MessageType.Info);
            return;
        }

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            foreach (PlayerSpawnPoint point in points)
            {
                if (GUILayout.Button(point.name)) Move(point.transform);
            }
        }

        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Edit mode only.", MessageType.None);
    }

    private void Move(Transform point)
    {
        foreach (Object selected in targets)
        {
            Transform player = ((PlayerStateManager)selected).transform;
            Undo.RecordObject(player, $"Move player to {point.name}");

            // Yaw only: a point tilted by accident must not lay the player over.
            player.SetPositionAndRotation(point.position, Quaternion.Euler(0f, point.eulerAngles.y, 0f));
        }
    }
}
#endif
