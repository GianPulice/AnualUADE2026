using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    protected static T instance;

    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                Debug.LogWarning($"Singleton {typeof(T).Name} instance is null! Make sure the singleton is properly initialized.");
            }
            return instance;
        }
    }
    public static bool Exists => instance != null;
    /// <summary>
    /// The parameter decides whether to create a singleton that is never destroyed — i.e. one that
    /// lives for the whole run — or one that is destroyed when moving between scenes.
    /// </summary>
    protected virtual void CreateSingleton(bool dontDestroyOnLoad)
    {
        if (instance == null)
        {
            instance = this as T;
        }

        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (dontDestroyOnLoad)
        {
            // DontDestroyOnLoad only accepts ROOT objects. Called on a child, Unity logs
            // "DontDestroyOnLoad only works for root GameObjects" and does nothing — so the
            // singleton is destroyed with its scene anyway, which is the exact opposite of what
            // the caller asked for, and the failure only shows up later as a null Instance after
            // a scene change.
            //
            // Detaching is the fix rather than the warning: a manager that must outlive the scene
            // cannot stay parented to something that will not. worldPositionStays: true so a
            // manager that happens to care about its transform does not jump.
            if (transform.parent != null) transform.SetParent(null, true);

            DontDestroyOnLoad(gameObject);
        }
    }
}
