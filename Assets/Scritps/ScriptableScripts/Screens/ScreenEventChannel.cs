using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "New Screen Event Channel", menuName = "Scriptable Objects/Events/Screen Event Channel")]
public class ScreenEventChannel : ScriptableObject
{
    public event UnityAction<string> OnPushScreenRequested;
    public event UnityAction OnPopScreenRequested;
    public event UnityAction OnClearAllScreensRequested;

    public void RaisePushScreen(string screenName)
    {
        if (OnPushScreenRequested != null)
        {
            OnPushScreenRequested.Invoke(screenName);
        }
        else
        {
            Debug.LogWarning($"[ScreenEventChannel] Push requested for {screenName}, but the UIManager isn't listening!");
        }
    }

    public void RaisePopScreen()
    {
        OnPopScreenRequested?.Invoke();
    }

    public void RaiseClearAll()
    {
        OnClearAllScreensRequested?.Invoke();
    }
}
