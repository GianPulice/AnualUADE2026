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
    /// El parametro decide si se crea un singleton que no se destruye, es decir que vive durante toda la ejecucion o si se crea uno que se destruye al pasar entre escenas
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
