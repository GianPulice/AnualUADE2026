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

    [Tooltip("Per-clip trim applied to the AudioSource before the mixer. Clips come from different " +
             "sources and are mastered at different loudness, so this is where they get evened out " +
             "against each other without re-exporting the file. The category volumes in the options " +
             "menu still apply on top, through the mixer.\n\n1 = the file as-is (the value every " +
             "existing sound had before this field existed).")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

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

    [Tooltip("Falloff: how the volume drops as the listener moves away from the sound, between Min " +
             "Distance (full volume) and Max Distance.\n\n" +
             "• Logarithmic (Unity default, realistic): the volume halves every time the distance " +
             "doubles past Min Distance. It drops fast right after Min Distance and then lingers as " +
             "a quiet tail for a long way, and it does NOT reach silence at Max Distance (it only " +
             "stops attenuating there). Min Distance is the knob that matters most here: raising it " +
             "makes the sound carry further.\n\n" +
             "• Linear: the volume goes down in a straight line from full at Min Distance to silence " +
             "at Max Distance. Less natural, but predictable: past Max Distance it is guaranteed " +
             "inaudible. Use it when the range itself is information (a door the Nemesis opens, a " +
             "box being pushed).\n\n" +
             "• Custom: uses the curve on the AudioSource. Pooled sources are created in code and " +
             "have no authored curve, so avoid it here.")]
    [SerializeField] private AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public SoundCategory Category => category;
    public AudioClip Clip => clip;
    public bool Loop => loop;
    public float Volume => volume;
    public bool IgnoreListenerPause => ignoreListenerPause;

    public float MinDistance => minDistance;
    public float MaxDistance => Mathf.Max(maxDistance, minDistance + 0.1f);
    public AudioRolloffMode Rolloff => rolloff;
}
