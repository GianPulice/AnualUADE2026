using UnityEngine;

[CreateAssetMenu(fileName = "SO_SoundData", menuName = "Scriptable Objects/Audio/SO_SoundData")]
public class SO_SoundData : ScriptableObject
{
    /// <summary>
    /// Categories aligned with the AudioMixerGroups of the Audio System Spec.
    /// The order of the first two values (SFX=0, Music=1) is kept so already-serialized
    /// SOs in the project are not invalidated.
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

    [Tooltip("If enabled, the sound keeps playing even while the game is paused (Time.timeScale = 0 + AudioListener.pause). " +
             "Use on UI clicks, timer ticks and anything that must be audible during pause.")]
    [SerializeField] private bool ignoreListenerPause = false;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public SoundCategory Category => category;
    public AudioClip Clip => clip;
    public bool Loop => loop;
    public bool IgnoreListenerPause => ignoreListenerPause;
}
