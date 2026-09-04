using System;
using UnityEngine;

/// <summary>
/// Content bank for the random 3D ambient one-shots (ambience Layer 2): the distant clangs,
/// creaking metal, dripping water and chains that make the factory feel like a dead building
/// that occasionally seems to move on its own.
///
/// This asset holds CONTENT only. The rhythm of the system (how long to wait, how often to stay
/// silent) lives on AmbienceEventScheduler, because the pulse of the building is a global feel
/// tuned once, not a per-room value. The tier WEIGHTS live on SO_AmbienceProfile, so a boiler
/// room can bias toward pipes and motors while sharing this same bank.
///
/// Entries are a nested [Serializable] class rather than separate SO_SoundData assets. Nothing
/// ever plays an ambient one-shot by string id, so 22 SO_SoundData assets would be 22 dead files
/// plus 22 inspector drags into AudioManager.sounds. NemesisAudio.StateLoop is the in-repo
/// precedent for this pattern: one asset, one place to tune.
///
/// Designer setup:
///   1. Create > Scriptable Objects > Audio > SO_AmbienceEventBank.
///   2. Fill the three tier arrays. Most entries only need a clip and its tags — every other
///      field has a working default.
///   3. Assign the bank to one or more SO_AmbienceProfile assets.
///
/// Tier guidance, from the audio design spec:
///   COMMON    — small sounds: drips, wind, creaking metal, pipes, small debris, grates, cables.
///   UNCOMMON  — more noticeable: metal doors, chains, glass, rolling objects, failing
///               fluorescents, distant impacts, strong pipe vibration.
///   RARE      — sounds that grab attention: a gate slamming hard, a chain falling, a motor
///               trying to start, a heavy impact on another floor, a huge structure creaking, or
///               an ambiguous metallic sound that could be something moving. These must stay rare
///               or they lose their impact.
/// </summary>
[CreateAssetMenu(fileName = "SO_AmbienceEventBank",
                 menuName = "Scriptable Objects/Audio/SO_AmbienceEventBank")]
public class SO_AmbienceEventBank : ScriptableObject
{
    /// <summary>
    /// Rarity tiers. Explicit values are kept so already-serialized assets are not invalidated
    /// if the list is ever reordered (same reasoning as SO_SoundData.SoundCategory).
    /// </summary>
    public enum ETier
    {
        Common   = 0,
        Uncommon = 1,
        Rare     = 2
    }

    /// <summary>
    /// What kind of thing made the sound. Used to match an event against the AmbienceEmitter
    /// anchors an LD placed in the level, so a chain sound can come from an actual chain.
    /// [Flags] because a rattling grate is both Metal and Structure.
    /// </summary>
    [Flags]
    public enum EEventTag
    {
        None       = 0,
        Metal      = 1 << 0,
        Water      = 1 << 1,
        Air        = 1 << 2,
        Electrical = 1 << 3,
        Structure  = 1 << 4,
        Debris     = 1 << 5,
        Door       = 1 << 6,
        Distant    = 1 << 7
    }

    [Serializable]
    public class Entry
    {
        [Tooltip("Editor readability only. Never shown to the player.")]
        public string label = "";

        public AudioClip clip;

        [Tooltip("Matched against the accepted tags of the AmbienceEmitter anchors in the level. " +
                 "An anchor with no accepted tags takes anything.")]
        public EEventTag tags = EEventTag.Metal;

        [Header("Playback")]
        [Tooltip("Base volume before the per-playback jitter and any occlusion attenuation.")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Tooltip("Stereo spread in degrees. 0 is a point source. Use 20-40 for something huge " +
                 "and diffuse like an entire structure creaking — a point source at 25 m " +
                 "hard-pans to one ear and reads as synthetic.")]
        [Range(0f, 360f)] public float spread = 0f;

        [Header("3D falloff")]
        [Tooltip("Linear rolloff distances. These are NOT the spawn distances — they are where " +
                 "the volume starts and stops falling off. Keep maxDistance comfortably beyond " +
                 "the top of distanceRange or far events will be silent.")]
        [Min(0.1f)] public float minDistance = 6f;
        [Min(0.1f)] public float maxDistance = 45f;

        [Header("Placement")]
        [Tooltip("How far from the listener the sound may spawn, in metres.")]
        public Vector2 distanceRange = new Vector2(8f, 30f);

        [Tooltip("Height offset relative to the player's feet. Negative values put the sound a " +
                 "floor below; positive ones put it in the ceiling or on the floor above.")]
        public Vector2 verticalRange = new Vector2(-1f, 3f);

        [Tooltip("When the chosen point is behind a wall, move the emitter to the wall itself so " +
                 "the surface becomes the source. Turn off for sounds that must never read as " +
                 "coming through a surface.")]
        public bool allowOccluderSnap = true;

        [Tooltip("Reject spawn points that are not near walkable NavMesh. This is the main guard " +
                 "against a chain sounding from outside the building in a blockout. Turn OFF for " +
                 "Structure and Air events, which legitimately live in ceilings, ducts and voids.")]
        public bool requireNavMeshNearby = true;

        [Tooltip("Minimum seconds before this specific clip may play again. 0 means only the " +
                 "shared repetition history applies.")]
        [Min(0f)] public float cooldown = 0f;
    }

    [Header("Common — small, background sounds")]
    [SerializeField] private Entry[] commonEvents = Array.Empty<Entry>();

    [Header("Uncommon — more noticeable")]
    [SerializeField] private Entry[] uncommonEvents = Array.Empty<Entry>();

    [Header("Rare — grabs attention, must stay rare")]
    [SerializeField] private Entry[] rareEvents = Array.Empty<Entry>();

    public Entry[] CommonEvents   => commonEvents;
    public Entry[] UncommonEvents => uncommonEvents;
    public Entry[] RareEvents     => rareEvents;

    /// <summary>
    /// The entries for a tier, or an empty array if the tier is unpopulated. Callers are expected
    /// to fall back to a lower tier rather than treating an empty tier as an error — a bank that
    /// only has common sounds is a legitimate work-in-progress state.
    /// </summary>
    public Entry[] GetTier(ETier tier)
    {
        switch (tier)
        {
            case ETier.Rare:     return rareEvents     ?? Array.Empty<Entry>();
            case ETier.Uncommon: return uncommonEvents ?? Array.Empty<Entry>();
            case ETier.Common:
            default:             return commonEvents   ?? Array.Empty<Entry>();
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Repairs entries whose numeric fields are degenerate.
    ///
    /// This exists because of a genuine Unity trap: raising the Size of an array of [Serializable]
    /// classes in the inspector zero-fills the new elements and IGNORES the C# field initializers.
    /// An entry created that way gets distanceRange (0,0) and verticalRange (0,0), which spawns every
    /// candidate exactly at the player's feet — inside the floor collider — so the placement resolver
    /// rejects 100% of them and the event never plays. The symptom (silence plus a placement-failure
    /// warning) points nowhere near the cause.
    ///
    /// Only values that make an entry unplayable are touched. A deliberate choice — a tight
    /// distanceRange, a zero vertical offset paired with a small solidCheckRadius — is left alone.
    /// </summary>
    private void OnValidate()
    {
        RepairTier(commonEvents, nameof(commonEvents));
        RepairTier(uncommonEvents, nameof(uncommonEvents));
        RepairTier(rareEvents, nameof(rareEvents));
    }

    private void RepairTier(Entry[] tier, string tierName)
    {
        if (tier == null) return;

        for (int i = 0; i < tier.Length; i++)
        {
            Entry entry = tier[i];
            if (entry == null) continue;

            string repaired = "";

            if (entry.distanceRange.y <= 0f)
            {
                entry.distanceRange = new Vector2(8f, 30f);
                repaired += " distanceRange";
            }
            else if (entry.distanceRange.x > entry.distanceRange.y)
            {
                entry.distanceRange = new Vector2(entry.distanceRange.y, entry.distanceRange.x);
                repaired += " distanceRange(swapped)";
            }

            // A zero-width vertical range is only fatal when it is zero-width AT the feet, which is
            // the zero-filled case. Any other flat value is a legitimate authoring choice.
            if (Mathf.Approximately(entry.verticalRange.x, 0f) &&
                Mathf.Approximately(entry.verticalRange.y, 0f))
            {
                entry.verticalRange = new Vector2(-1f, 3f);
                repaired += " verticalRange";
            }
            else if (entry.verticalRange.x > entry.verticalRange.y)
            {
                entry.verticalRange = new Vector2(entry.verticalRange.y, entry.verticalRange.x);
                repaired += " verticalRange(swapped)";
            }

            if (entry.maxDistance <= entry.minDistance)
            {
                entry.minDistance = 6f;
                entry.maxDistance = 45f;
                repaired += " min/maxDistance";
            }

            if (entry.volume <= 0f)
            {
                entry.volume = 0.8f;
                repaired += " volume";
            }

            if (repaired.Length == 0) continue;

            string label = string.IsNullOrEmpty(entry.label)
                ? (entry.clip != null ? entry.clip.name : $"element {i}")
                : entry.label;

            // Without this the repair only lives in memory, so a build made without touching the
            // asset again would still ship the zeroed values — and OnValidate never runs in a build.
            UnityEditor.EditorUtility.SetDirty(this);

            Debug.LogWarning($"[{nameof(SO_AmbienceEventBank)}] '{name}' {tierName}[{i}] " +
                             $"('{label}'): repaired{repaired}. Unity zero-fills new array elements " +
                             "and skips the C# defaults, so a freshly added entry needs this. Check " +
                             "the values are what you wanted.", this);
        }
    }
#endif

    /// <summary>True if at least one tier has at least one entry with a clip assigned.</summary>
    public bool HasAnyPlayableEntry()
    {
        return TierHasClip(commonEvents) || TierHasClip(uncommonEvents) || TierHasClip(rareEvents);
    }

    private static bool TierHasClip(Entry[] tier)
    {
        if (tier == null) return false;
        for (int i = 0; i < tier.Length; i++)
            if (tier[i] != null && tier[i].clip != null) return true;
        return false;
    }
}
