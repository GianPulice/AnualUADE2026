using UnityEngine;

[CreateAssetMenu(fileName = "SO_DoorData", menuName = "Scriptable Objects/Interactables/Door Data")]
public class SO_DoorData : ScriptableObject
{
    /// <summary>
    /// Played by every door that has no <see cref="openSoundId"/> of its own.
    ///
    /// A constant rather than a per-door requirement because the point of the door sound is that
    /// the player can hear the NEMESIS use one, and that only works if it is the same sound
    /// everywhere. A door that needs its own (a heavy shutter, a hatch) overrides it.
    /// </summary>
    public const string DefaultOpenSoundId = "sfx_interaction_puerta_abrir";

    [SerializeField] private string doorId;
    [SerializeField] private SO_InventoryItem requiredKey;
    [SerializeField] private bool consumeKey = true;
    [PuzzleId]
    [SerializeField] private string requiredCompletedPuzzleId;
    [SerializeField] private string openPrompt = "Open door";
    [SerializeField] private string lockedPrompt = "Locked door";

    [Header("Audio")]
    [Tooltip("Played at the door's position whenever it swings open — by the player OR by the " +
             "Nemesis. Hearing the monster open a door two rooms away is the whole point, so give " +
             "the SO_SoundData a real Max Distance (~25 m); at Unity's default of 500 it is " +
             "audible across the level and tells you nothing about where it is.\n\n" +
             "Leave empty to use the shared default.")]
    [SoundId]
    [SerializeField] private string openSoundId = string.Empty;

    [Tooltip("Played when it swings shut. Leave empty for silence — nothing closes doors today " +
             "except a script calling CloseDoor, so this is here for when something does.")]
    [SoundId]
    [SerializeField] private string closeSoundId = string.Empty;

    public string DoorId => doorId;
    public SO_InventoryItem RequiredKey => requiredKey;
    public bool ConsumeKey => consumeKey;
    public string RequiredCompletedPuzzleId => requiredCompletedPuzzleId;
    public string OpenPrompt => openPrompt;
    public string LockedPrompt => lockedPrompt;

    /// <summary>The door's own open sound, or the shared default when it has none.</summary>
    public string OpenSoundId =>
        string.IsNullOrWhiteSpace(openSoundId) ? DefaultOpenSoundId : openSoundId;

    /// <summary>No fallback: an unset close sound means silence, not the default open sound.</summary>
    public string CloseSoundId => closeSoundId;
}
