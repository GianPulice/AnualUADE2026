using UnityEngine;

/// <summary>
/// Look, timing and texts shared by every <see cref="PuzzleRewardBox"/>. One asset for the whole
/// game, so the boxes of every level open the same way and a tweak reaches all of them.
///
/// The colours are an agreed exception to the colour rules (docs/Materials-System.md): red while the
/// box is locked, green once it is unlocked (the same green as a placed core and a resolved module).
/// </summary>
[CreateAssetMenu(fileName = "SO_RewardBoxConfig", menuName = "Scriptable Objects/Puzzles/Reward Box Config")]
public class SO_RewardBoxConfig : ScriptableObject
{
    [Header("Glow and light")]
    [Tooltip("Base colour of the glow parts (the lock LEDs and the screen) while locked. Also the " +
             "colour of the box's light and fog beacon.")]
    [SerializeField] private Color lockedColor = new Color(0.8f, 0.102f, 0.102f);

    [Tooltip("Emission of the glow parts while locked. HDR values are linear, so the two weak channels " +
             "have to be near zero: 0.4 in each reads as pale pink, not red.")]
    [SerializeField, ColorUsage(false, true)] private Color lockedEmission = new Color(2.4f, 0.025f, 0.025f);

    [Tooltip("Base colour of the glow parts once unlocked, and of the light and beacon.")]
    [SerializeField] private Color unlockedColor = new Color(0.1f, 0.9f, 0.25f);

    [Tooltip("Emission of the glow parts once unlocked. One channel high, the other two near zero " +
             "(linear values): a cyan with two channels above 1, or a green with 0.3 in red and blue, " +
             "reads as white or mint on the shot.")]
    [SerializeField, ColorUsage(false, true)] private Color unlockedEmission = new Color(0.02f, 2f, 0.06f);

    [Tooltip("Emission of a lock's LEDs once it has fallen off: a dead lock.")]
    [SerializeField, ColorUsage(false, true)] private Color droppedLockEmission = Color.black;

    [Tooltip("Intensity of the box's Light while locked. The light hangs half a metre from the lid, so " +
             "it lands on it with a lot of punch; this is what the shot sees before the locks open.")]
    [SerializeField, Min(0f)] private float lockedLightIntensity = 1f;

    [Tooltip("Intensity of the box's Light once unlocked: what the shot sees while the lid opens. " +
             "Lower it if the lid and the item read washed out.")]
    [SerializeField, Min(0f)] private float unlockedLightIntensity = 0.4f;

    [Header("Cinematic")]
    [Tooltip("Off: the box opens in place with no shot and the player keeps control.")]
    [SerializeField] private bool playCinematic = true;

    [Tooltip("Longest wait, in seconds, for the screen to be free (a panel closing, another " +
             "cinematic ending) before the box opens without its shot.")]
    [SerializeField, Min(0f)] private float maxWaitSeconds = 10f;

    [Tooltip("Seconds the locked box is on screen before the first lock opens.")]
    [SerializeField, Min(0f)] private float holdBeforeSeconds = 0.8f;

    [Tooltip("Seconds between the right lock landing and the left one opening.")]
    [SerializeField, Min(0f)] private float gapBetweenLocksSeconds = 0.35f;

    [Tooltip("Seconds between the left lock landing and the lid starting to open. The screen and the " +
             "light turn green at the start of this gap.")]
    [SerializeField, Min(0f)] private float holdBeforeLidSeconds = 0.5f;

    [Tooltip("Seconds the open box stays on screen before control returns.")]
    [SerializeField, Min(0f)] private float holdAfterSeconds = 1.2f;

    [SerializeField] private bool skippable = true;
    [SerializeField] private KeyCode skipKey = KeyCode.F;
    [Tooltip("{0} is the skip key.")]
    [SerializeField] private string skipPromptFormat = "[Press {0} to skip]";

    [Header("Falling locks")]
    [Tooltip("Metres a lock jumps up as it springs open, before it falls.")]
    [SerializeField, Min(0f)] private float lockHopHeight = 0.03f;

    [Tooltip("Seconds of that jump.")]
    [SerializeField, Min(0.01f)] private float lockHopSeconds = 0.12f;

    [Tooltip("Metres a lock travels away from the box's front while it falls.")]
    [SerializeField, Min(0f)] private float lockForwardKick = 0.08f;

    [Tooltip("Degrees a lock tumbles forward on its way down.")]
    [SerializeField] private float lockTumbleDegrees = 100f;

    [Tooltip("Gravity of the fall, m/s².")]
    [SerializeField, Min(0.1f)] private float lockGravity = 9.81f;

    [Header("Prompts")]
    [SerializeField] private string lockedPrompt = "Sealed box";
    [Tooltip("Shown under the prompt while locked. Also what keeps the prompt on screen, since E " +
             "does nothing yet.")]
    [SerializeField] private string lockedInfo = "Locked";
    [Tooltip("{0} is the item's name.")]
    [SerializeField] private string takePromptFormat = "Take {0}";

    [Header("Audio")]
    [Tooltip("A lock springing open.")]
    [SoundId, SerializeField] private string latchSoundId = "sfx_interaction_puerta_desbloqueada";
    [Tooltip("A lock hitting the floor.")]
    [SoundId, SerializeField] private string lockLandSoundId = "sfx_interaction_key";
    [Tooltip("Both locks off: the screen turns green.")]
    [SoundId, SerializeField] private string unlockedSoundId = "sfx_interaction_panel_control_success";
    [Tooltip("The lid opening.")]
    [SoundId, SerializeField] private string lidSoundId = "sfx_interaction_box";
    [Tooltip("E on a locked box.")]
    [SoundId, SerializeField] private string lockedSoundId = "sfx_interaction_puerta_bloqueada";
    [Tooltip("Where the item's pickup sound comes from (per category), like a pickup on the floor.")]
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    public Color LockedColor => lockedColor;
    public Color LockedEmission => lockedEmission;
    public Color UnlockedColor => unlockedColor;
    public Color UnlockedEmission => unlockedEmission;
    public Color DroppedLockEmission => droppedLockEmission;
    public float LockedLightIntensity => lockedLightIntensity;
    public float UnlockedLightIntensity => unlockedLightIntensity;

    public bool PlayCinematic => playCinematic;
    public float MaxWaitSeconds => maxWaitSeconds;
    public float HoldBeforeSeconds => holdBeforeSeconds;
    public float GapBetweenLocksSeconds => gapBetweenLocksSeconds;
    public float HoldBeforeLidSeconds => holdBeforeLidSeconds;
    public float HoldAfterSeconds => holdAfterSeconds;
    public bool Skippable => skippable;
    public KeyCode SkipKey => skipKey;
    public string SkipPromptText => string.Format(skipPromptFormat, skipKey);

    public float LockHopHeight => lockHopHeight;
    public float LockHopSeconds => lockHopSeconds;
    public float LockForwardKick => lockForwardKick;
    public float LockTumbleDegrees => lockTumbleDegrees;
    public float LockGravity => lockGravity;

    public string LockedPrompt => lockedPrompt;
    public string LockedInfo => lockedInfo;
    public string TakePromptFormat => takePromptFormat;

    public string LatchSoundId => latchSoundId;
    public string LockLandSoundId => lockLandSoundId;
    public string UnlockedSoundId => unlockedSoundId;
    public string LidSoundId => lidSoundId;
    public string LockedSoundId => lockedSoundId;
    public SO_ItemCategoryConfig CategoryConfig => categoryConfig;
}
