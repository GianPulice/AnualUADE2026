using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "Lists of Scenes to use in game", menuName = "Scriptable Objects/ Scene List")]
public class SO_SceneList : ScriptableObject
{
#if UNITY_EDITOR
    [Tooltip("Escenas que nunca se descargan (managers, inputs, data, etc.)")]
    public List<SceneAsset> persistentSceneAssets = new List<SceneAsset>();
#endif
    [HideInInspector] public List<string> persistentSceneNames = new List<string>();

    [Tooltip("Lista de grupos de escenas a usar. Asegurarse de que estén agregadas en Build Settings.")]
    public List<SceneGroupEntry> sceneGroups = new List<SceneGroupEntry>();

    /// <summary>
    /// Devuelve el grupo que coincide con el label dado. Null si no existe.
    /// </summary>
    public SceneGroupEntry GetGroup(string label)
    {
        return sceneGroups.Find(g => g.label == label);
    }

    /// <summary>
    /// Devuelve true si existe un grupo con ese label.
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

            // Si el label está vacío y hay al menos una escena, autocompletamos
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
    [Tooltip("Nombre identificador del grupo. Usado por el ScreenManager para cargarlo.")]
    public string label;

#if UNITY_EDITOR
    [Tooltip("Arrastrá aquí todas las escenas que componen este grupo (Arte, Lógica, UI, etc.)")]
    public List<SceneAsset> sceneAssets = new List<SceneAsset>();
#endif

    [HideInInspector] public List<string> scenePaths = new List<string>();
    [HideInInspector] public List<string> sceneNames = new List<string>();
}