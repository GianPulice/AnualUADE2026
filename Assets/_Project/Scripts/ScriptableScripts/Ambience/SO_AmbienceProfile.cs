using UnityEngine;

/// <summary>
/// The ambient "feel" of one area of the factory: which bed loops hum underneath, how loud the
/// subconscious layers sit, and which one-shot bank the building draws from.
///
/// One profile is meant to be reused across many AmbienceZone triggers — six profiles cover the
/// seventeen named areas of WIRED_Zona1_Blockout. Zones push and pop profiles on a stack in
/// AmbienceController, so the innermost zone wins and nesting works without any extra wiring.
/// This mirrors SO_VisionFogConfig + LightZone + VisionRangeController, which is the same
/// push/pop-with-fade shape already proven in this project for the vision fog.
///
/// Designer setup:
///   1. Create > Scriptable Objects > Audio > SO_AmbienceProfile.
///   2. Assign one or two bed loops (see bedTracks — two is strongly preferred).
///   3. Assign an event bank and, if this area has a distinct character, adjust the tier weights.
///   4. Assign the profile to the AmbienceZone triggers covering that area, or to the
///      AmbienceController's defaultProfile if it is the fallback for the whole level.
/// </summary>
[CreateAssetMenu(fileName = "SO_AmbienceProfile",
                 menuName = "Scriptable Objects/Audio/SO_AmbienceProfile")]
public class SO_AmbienceProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Shown in the ambience debug logs. Falls back to the asset name if left empty.")]
    [SerializeField] private string displayName = "";

    [Tooltip("Colour used to tint the AmbienceZone trigger gizmos that carry this profile. " +
             "Unlike SO_VisionFogConfig, an ambience profile has no natural colour, so pick " +
             "something that distinguishes this area in the Scene view.")]
    [SerializeField] private Color gizmoColor = new Color(0.3f, 0.8f, 0.9f, 1f);

    // ── Layer 1 — factory bed ────────────────────────────────────────────────

    [Header("Layer 1 — Factory bed")]
    [Tooltip("Looping room tone. USE TWO CLIPS OF COPRIME LENGTH (for example 37 s and 53 s): " +
             "played together their composite period is the least common multiple — about 33 " +
             "minutes for 37/53 — so the loop is effectively undetectable. A single loop is " +
             "recognised by its contour long before its transients, so stripping the transients " +
             "out is not enough on its own.\n\n" +
             "Each clip gets its own crossfade slot, so the array length must not exceed the " +
             "bed layer's bedSlots.")]
    [SerializeField] private AudioClip[] bedTracks = new AudioClip[0];

    [Tooltip("Level of the first bed track. The other tracks are scaled relative to it by " +
             "bedTrackBalance. Keep this fairly low — around 0.30-0.35 — or a one-shot event " +
             "spawned 25 m away will be buried under the bed.")]
    [SerializeField, Range(0f, 1f)] private float bedVolume = 0.32f;

    [Tooltip("Volume of each additional bed track as a fraction of bedVolume. Two beds at full " +
             "level sum to roughly +3 dB, so the second one usually wants to sit lower.")]
    [SerializeField, Range(0f, 1f)] private float bedTrackBalance = 0.7f;

    [Tooltip("Seconds to crossfade from the previous profile to this one. 0 is an instant change " +
             "and will click. Audio wants slower transitions than the vision fog does — a couple " +
             "of seconds reads as walking into a different room, half a second reads as a cut.")]
    [SerializeField, Min(0f)] private float transitionDuration = 2.5f;

    // ── Layers 3 and 4 — subconscious ────────────────────────────────────────

    [Header("Layers 3-4 — Subconscious (scales, not levels)")]
    [Tooltip("Multiplier over the pink-noise track's own level on AmbienceDriftLayer. The " +
             "absolute level lives there and on the Ambience/Texture mixer fader; this only says " +
             "how much of it this area gets.")]
    [SerializeField, Range(0f, 2f)] private float textureScale = 1f;

    [Tooltip("Multiplier over the low-frequency tracks. Set to 0 for exterior areas — a 17 Hz " +
             "room-pressure drone in an open loading yard is a contradiction.")]
    [SerializeField, Range(0f, 2f)] private float subScale = 1f;

    // ── Layer 2 — random one-shots ───────────────────────────────────────────

    [Header("Layer 2 — Random one-shots")]
    [Tooltip("Content bank this area draws from. Leave empty to have no random events at all " +
             "(useful for a safe room, where total silence is the point).")]
    [SerializeField] private SO_AmbienceEventBank eventBank;

    [Tooltip("Relative tier weights. They do not need to sum to 1 — they are normalised at " +
             "roll time — but keeping them as 0.6 / 0.3 / 0.1 makes the intent readable.\n\n" +
             "At the scheduler's default rhythm these give roughly one common sound a minute, " +
             "one uncommon every two minutes, and one rare every six.")]
    [SerializeField, Range(0f, 1f)] private float commonWeight   = 0.6f;
    [SerializeField, Range(0f, 1f)] private float uncommonWeight = 0.3f;
    [SerializeField, Range(0f, 1f)] private float rareWeight     = 0.1f;

    [Tooltip("Multiplies how often events fire in this area. Below 1 makes it busier (the wait " +
             "is divided by this), above 1 makes it emptier. 1 uses the scheduler's own rhythm.")]
    [SerializeField, Range(0.25f, 4f)] private float eventIntervalScale = 1f;

    // ── Public API ───────────────────────────────────────────────────────────

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Color GizmoColor   => gizmoColor;

    public AudioClip[] BedTracks       => bedTracks;
    public float BedVolume             => bedVolume;
    public float BedTrackBalance       => bedTrackBalance;
    public float TransitionDuration    => transitionDuration;

    public float TextureScale => textureScale;
    public float SubScale     => subScale;

    public SO_AmbienceEventBank EventBank => eventBank;
    public float CommonWeight             => commonWeight;
    public float UncommonWeight           => uncommonWeight;
    public float RareWeight               => rareWeight;
    public float EventIntervalScale       => eventIntervalScale;

    /// <summary>
    /// Volume for bed slot <paramref name="slotIndex"/>: slot 0 sits at bedVolume, every
    /// additional slot is scaled by bedTrackBalance so stacking two loops does not add ~3 dB.
    /// </summary>
    public float BedVolumeForSlot(int slotIndex) =>
        slotIndex <= 0 ? bedVolume : bedVolume * bedTrackBalance;

    /// <summary>The clip for a bed slot, or null if this profile has fewer tracks than slots.</summary>
    public AudioClip BedTrackForSlot(int slotIndex)
    {
        if (bedTracks == null || slotIndex < 0 || slotIndex >= bedTracks.Length) return null;
        return bedTracks[slotIndex];
    }
}

/// <summary>
/// The Ambience sub-buses. Each maps to a child AudioMixerGroup of Ambience in MasterMixer.mixer,
/// created by Tools/Audio/Create or Update Master Mixer.
///
/// Declared here rather than in its own file following the precedent of the enums that live at the
/// bottom of their owning SO (see SO_VisionFogConfig).
/// </summary>
public enum EAmbienceBus
{
    Bed     = 0,
    Events  = 1,
    Texture = 2,
    Sub     = 3
}
