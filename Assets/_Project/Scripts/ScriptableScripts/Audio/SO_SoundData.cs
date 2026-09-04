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

    [Header("3D range (only used when the sound is played at a position)")]
    [Tooltip("Distance below which the sound is at full volume. Unity's default is 1.")]
    [SerializeField, Min(0f)] private float minDistance = 1f;

    [Tooltip("Distance at which the sound has faded out entirely. Unity's default is 500, which is " +
             "effectively 'audible everywhere' — fine for a one-off, wrong for anything the player " +
             "is meant to locate by ear. A door the Nemesis opens wants something like 25.\n\n" +
             "Left at the Unity defaults on purpose so adding this field changed no existing sound; " +
             "set it per clip where the distance is part of the information.")]
    [SerializeField, Min(0.1f)] private float maxDistance = 500f;

    [Tooltip("How the volume falls off between the two distances. Logarithmic is Unity's default " +
             "and the realistic one; Linear is easier to reason about when tuning a specific range.")]
    [SerializeField] private AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public SoundCategory Category => category;
    public AudioClip Clip => clip;
    public bool Loop => loop;
    public bool IgnoreListenerPause => ignoreListenerPause;

    public float MinDistance => minDistance;
    public float MaxDistance => Mathf.Max(maxDistance, minDistance + 0.1f);
    public AudioRolloffMode Rolloff => rolloff;
}
