using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName ="New Scene Event Channel", menuName ="Scriptable Objects/Events/Scene Event Channel")]
public class SceneEventChannel : ScriptableObject
{
    public event UnityAction<string> OnSceneLoadRequested;
    public event UnityAction<string> OnSceneUnloadRequested;

    public void RaiseLoadRequest(string sceneName)
    {
        if (OnSceneLoadRequested != null)
        {
            OnSceneLoadRequested.Invoke(sceneName);
        }
        else
        {
            Debug.LogWarning($"[SceneEventChannel] A load request was raised for {sceneName}, but no SceneLoader is listening!");
        }
    }
    public void RaiseUnloadRequest(string sceneName)
    {
        OnSceneUnloadRequested?.Invoke(sceneName);
    }
}
