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
            DontDestroyOnLoad(gameObject);
        }
    }
}
