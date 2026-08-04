using UnityEngine;

[CreateAssetMenu(fileName = "SO_SoundData", menuName = "Scriptable Objects/Audio/SO_SoundData")]
public class SO_SoundData : ScriptableObject
{
    /// <summary>
    /// Categorias alineadas con los AudioMixerGroups del Audio System Spec.
    /// El orden de los primeros dos valores (SFX=0, Music=1) se mantiene para no
    /// invalidar SOs ya serializados en el proyecto.
    /// </summary>
    public enum SoundCategory
    {
        SFX      = 0,
        Music    = 1,
        Player   = 2,
        Nemesis  = 3,
        UI       = 4,
        Voice    = 5,
        Ambience = 6
    }

    [SerializeField] private string id;
    [SerializeField] private SoundCategory category = SoundCategory.SFX;
    [SerializeField] private AudioClip clip;
    [SerializeField] private bool loop = false;

    [Tooltip("Si esta activo, el sonido sigue sonando aunque el juego este pausado (Time.timeScale = 0 + AudioListener.pause). " +
             "Usar en clicks de UI, tick de timers y todo lo que deba escucharse en pausa.")]
    [SerializeField] private bool ignoreListenerPause = false;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public SoundCategory Category => category;
    public AudioClip Clip => clip;
    public bool Loop => loop;
    public bool IgnoreListenerPause => ignoreListenerPause;
}
