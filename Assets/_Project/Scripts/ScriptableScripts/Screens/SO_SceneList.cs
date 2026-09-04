using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "Lists of Scenes to use in game", menuName = "Scriptable Objects/ Scene List")]
public class SO_SceneList : ScriptableObject
{
#if UNITY_EDITOR
    [Tooltip("Scenes that are never unloaded (managers, inputs, data, etc.)")]
    public List<SceneAsset> persistentSceneAssets = new List<SceneAsset>();
#endif
    [HideInInspector] public List<string> persistentSceneNames = new List<string>();

    [Tooltip("List of scene groups to use. Make sure they are added to the Build Settings.")]
    public List<SceneGroupEntry> sceneGroups = new List<SceneGroupEntry>();

    /// <summary>
    /// Returns the group matching the given label. Null if it does not exist.
    /// </summary>
    public SceneGroupEntry GetGroup(string label)
    {
        return sceneGroups.Find(g => g.label == label);
    }

    /// <summary>
    /// Returns true if a group with that label exists.
    /// </summary>
    public bool ContainsGroup(string label)
    {
        return sceneGroups.Exists(g => g.label == label);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        foreach (var group in sceneGroups)
        {
            group.sceneNames.Clear();
            group.scenePaths.Clear();

            foreach (var asset in group.sceneAssets)
            {
                if (asset != null)
                {
                    group.sceneNames.Add(asset.name);
                    group.scenePaths.Add(AssetDatabase.GetAssetPath(asset));
                }
            }

            // If the label is empty and there is at least one scene, autocomplete it
            if (string.IsNullOrEmpty(group.label) && group.sceneNames.Count > 0)
                group.label = group.sceneNames[0] + "_Group";
        }
        persistentSceneNames.Clear();
        foreach (var asset in persistentSceneAssets)
        {
            if(asset != null)
            {
                persistentSceneNames.Add(asset.name);
            }
        }
    }
#endif
}

[System.Serializable]
public class SceneGroupEntry
{
    [Tooltip("Identifier name of the group. Used by the ScreenManager to load it.")]
    public string label;

#if UNITY_EDITOR
    [Tooltip("Drag here every scene that makes up this group (Art, Logic, UI, etc.)")]
    public List<SceneAsset> sceneAssets = new List<SceneAsset>();
#endif

    [HideInInspector] public List<string> scenePaths = new List<string>();
    [HideInInspector] public List<string> sceneNames = new List<string>();
}
