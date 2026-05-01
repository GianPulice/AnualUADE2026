using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif
[CreateAssetMenu(fileName ="Lists of Scenes to use in game",menuName ="Scriptable Objects/ Scene List")]
public class SO_SceneList : ScriptableObject
{
    [Tooltip("Lista de escenas a usar. Asegurarse de que estén agregadas en Build Settings.")]
    public List<SceneEntry> scenes = new List<SceneEntry>();
    public string GetScenePath(int index)
    {
        if (index < 0 || index >= scenes.Count) return null;
        return scenes[index].scenePath;
    }
    public string GetSceneName(int index)
    {
        if (index < 0 || index >= scenes.Count) return null;
        return scenes[index].sceneName;
    }
    public List<string> GetAllScenePaths()
    {
        var paths = new List<string>();
        foreach (var entry in scenes)
            if (!string.IsNullOrEmpty(entry.scenePath))
                paths.Add(entry.scenePath);
        return paths;
    }
    public List<string> GetAllSceneNames()
    {
        var names = new List<string>();
        foreach (var entry in scenes)
            if (!string.IsNullOrEmpty(entry.sceneName))
                names.Add(entry.sceneName);
        return names;
    }
#if UNITY_EDITOR
    // Se ejecuta cada vez que cambias algo en el Inspector
    private void OnValidate()
    {
        foreach (var entry in scenes)
        {
            if (entry.sceneAsset != null)
            {
                entry.sceneName = entry.sceneAsset.name;
                entry.scenePath = AssetDatabase.GetAssetPath(entry.sceneAsset);

                // Si el label está vacío, lo autocompletamos con el nombre de la escena
                if (string.IsNullOrEmpty(entry.label))
                    entry.label = entry.sceneName;
            }
        }
    }
#endif
}

[System.Serializable]
public class SceneEntry
{
#if UNITY_EDITOR
    public SceneAsset sceneAsset;

#endif

    [HideInInspector] public string scenePath;

    [HideInInspector] public string sceneName;

    public string label;
}