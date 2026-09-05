using System;
using UnityEngine;

/// <summary>
/// Content bank for footsteps: which clips a walker puts down on each kind of ground.
///
/// This asset holds CONTENT only. The cadence (how far apart the steps are, how fast the walker
/// has to be moving before it makes any) lives on <see cref="FootstepEmitter"/>, because stride is
/// a property of the body doing the walking, not of the floor it is walking on. One bank can
/// therefore be shared by several walkers, and one walker can be re-skinned by swapping its bank.
///
/// Entries are a nested [Serializable] class rather than separate SO_SoundData assets, for the
/// same reason SO_AmbienceEventBank gives: nothing ever plays a footstep by string id, so twenty
/// SO_SoundData assets would be twenty dead files plus twenty inspector drags into
/// AudioManager.sounds. NemesisAudio.StateLoop is the other precedent — one asset, one place to
/// tune.
///
/// SURFACE RESOLUTION, in order. The emitter probes down, gets a collider, and asks this bank:
///   1. A <see cref="FootstepSurface"/> marker on the collider or any of its parents. Authoritative,
///      and the intended way to author a level: the project has no surface tags and exactly one
///      PhysicMaterial, so there is nothing else to read.
///   2. The first entry whose <c>layers</c> mask contains the collider's layer. This is what makes
///      the Water layer work with no authoring at all; leave the mask empty on every other entry
///      or a broad layer like Ground will swallow the lot.
///   3. <see cref="FallbackSurface"/>, then the first entry that has any clip. A bank with only
///      concrete in it is a legitimate work-in-progress state, not an error.
///
/// Designer setup:
///   1. Create > Scriptable Objects > Audio > SO_FootstepBank.
///   2. One entry per surface, each with two or more clips — the emitter shuffles them, and a
///      one-clip surface is audibly a one-clip surface.
///   3. Assign the bank to the FootstepEmitter on the walker.
/// </summary>
[CreateAssetMenu(fileName = "SO_FootstepBank",
                 menuName = "Scriptable Objects/Audio/SO_FootstepBank")]
public class SO_FootstepBank : ScriptableObject
{
    /// <summary>
    /// The surfaces a level can be authored in. Explicit values, and APPEND ONLY: Unity stores an
    /// enum field as its integer, so inserting a member above the end silently rewrites every
    /// FootstepSurface marker already placed in a scene into a different surface. Same rule as
    /// SO_SoundData.SoundCategory and ENemesisPredicate.
    /// </summary>
    public enum ESurface
    {
        Concrete = 0,
        Metal    = 1,
        Wood     = 2,
        Gravel   = 3,
        Water    = 4,
        Oil      = 5,
        Dirt     = 6,
    }

    [Serializable]
    public class SurfaceEntry
    {
        public ESurface surface = ESurface.Concrete;

        [Tooltip("Two or more, ideally. The emitter walks a shuffled order and never opens a new " +
                 "shuffle on the clip the previous one closed with, so with three clips a step " +
                 "can never repeat back to back and the pattern never settles.")]
        public AudioClip[] clips = Array.Empty<AudioClip>();

        [Tooltip("Base volume for this surface, before the emitter's own scaling. Metal grating " +
                 "is louder than dirt; that difference belongs here and not in the clip.")]
        [Range(0f, 1f)] public float volume = 1f;

        [Tooltip("A random pitch is drawn inside this range per step. It is what stops two " +
                 "consecutive steps on the same clip reading as a copy-paste. Keep it narrow — " +
                 "past about +/- 10% a footstep starts sounding like a different shoe.")]
        public Vector2 pitchRange = new Vector2(0.94f, 1.06f);

        [Tooltip("Fallback resolution: a floor on one of these layers counts as this surface when " +
                 "it carries no FootstepSurface marker.\n\n" +
                 "LEAVE THIS EMPTY on every entry except the ones where a whole Unity layer really " +
                 "does mean one material — Water is the only such layer in this project. An entry " +
                 "that claims Ground or Default takes every floor in the level and no marker will " +
                 "ever be reached, because the layer pass runs before the fallback.")]
        public LayerMask layers;
    }

    [Header("Surfaces")]
    [SerializeField] private SurfaceEntry[] surfaces = Array.Empty<SurfaceEntry>();

    [Tooltip("Used when the probe hits nothing, hits an unmarked collider on an unclaimed layer, " +
             "or when the resolved surface has no clips.")]
    [SerializeField] private ESurface fallbackSurface = ESurface.Concrete;

    [Header("Limp — M1 penalty (player banks only)")]
    [Tooltip("The drag half of the limp. With the legs penalty active the emitter alternates a " +
             "normal step with one of these, which is the paso-arrastre-paso-arrastre pattern from " +
             "the audio spec §2. Leave empty on the Nemesis bank — it never limps.")]
    [SerializeField] private AudioClip[] limpDragClips = Array.Empty<AudioClip>();

    [Tooltip("A drag is quieter than a step. That difference is most of what sells the limp.")]
    [SerializeField, Range(0f, 1f)] private float limpDragVolume = 0.5f;

    [SerializeField] private Vector2 limpDragPitchRange = new Vector2(0.92f, 1.02f);

    public SurfaceEntry[] Surfaces      => surfaces ?? Array.Empty<SurfaceEntry>();
    public ESurface FallbackSurface     => fallbackSurface;
    public AudioClip[] LimpDragClips    => limpDragClips ?? Array.Empty<AudioClip>();
    public float LimpDragVolume         => limpDragVolume;
    public Vector2 LimpDragPitchRange   => limpDragPitchRange;

    /// <summary>True when the limp alternation has something to play.</summary>
    public bool HasLimpDrag => HasClip(limpDragClips);

    /// <summary>True when at least one surface has at least one clip assigned.</summary>
    public bool HasAnyPlayableEntry()
    {
        SurfaceEntry[] all = Surfaces;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && HasClip(all[i].clips)) return true;
        return false;
    }

    /// <summary>
    /// The entry to play for the ground the emitter just probed, or null when the bank is empty.
    /// See the resolution order in the class summary.
    /// </summary>
    public SurfaceEntry Resolve(Collider ground)
    {
        if (ground != null)
        {
            // 1 — an explicit marker anywhere up the hierarchy. GetComponentInParent and not
            // GetComponent because floors are routinely a mesh child under a marked root.
            FootstepSurface marker = ground.GetComponentInParent<FootstepSurface>();
            if (marker != null)
            {
                SurfaceEntry marked = FindPlayable(marker.Surface);
                if (marked != null) return marked;
            }

            // 2 — a layer an entry has claimed.
            int layerBit = 1 << ground.gameObject.layer;
            SurfaceEntry[] all = Surfaces;
            for (int i = 0; i < all.Length; i++)
            {
                SurfaceEntry entry = all[i];
                if (entry == null || entry.layers.value == 0) continue;
                if ((entry.layers.value & layerBit) == 0) continue;
                if (HasClip(entry.clips)) return entry;
            }
        }

        // 3 — the authored fallback, then anything at all.
        return FindPlayable(fallbackSurface) ?? FirstPlayable();
    }

    /// <summary>The entry for a surface, only if it actually has clips. Null otherwise.</summary>
    public SurfaceEntry FindPlayable(ESurface surface)
    {
        SurfaceEntry[] all = Surfaces;
        for (int i = 0; i < all.Length; i++)
        {
            SurfaceEntry entry = all[i];
            if (entry == null || entry.surface != surface) continue;
            if (HasClip(entry.clips)) return entry;
        }
        return null;
    }

    private SurfaceEntry FirstPlayable()
    {
        SurfaceEntry[] all = Surfaces;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && HasClip(all[i].clips)) return all[i];
        return null;
    }

    private static bool HasClip(AudioClip[] clips)
    {
        if (clips == null) return false;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null) return true;
        return false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Repairs the one field combination that makes an entry silently unplayable.
    ///
    /// Raising the Size of an array of [Serializable] classes in the inspector zero-fills the new
    /// elements and IGNORES the C# field initializers — the same Unity trap SO_AmbienceEventBank
    /// documents. A footstep entry created that way gets pitchRange (0,0), and a clip played at
    /// pitch 0 never advances: the AudioSource reports isPlaying forever and no sound comes out.
    /// The symptom is silence with nothing in the console.
    /// </summary>
    private void OnValidate()
    {
        if (surfaces == null) return;

        for (int i = 0; i < surfaces.Length; i++)
        {
            SurfaceEntry entry = surfaces[i];
            if (entry == null) continue;

            bool repaired = false;

            if (entry.pitchRange.x <= 0f || entry.pitchRange.y <= 0f)
            {
                entry.pitchRange = new Vector2(0.94f, 1.06f);
                repaired = true;
            }
            else if (entry.pitchRange.x > entry.pitchRange.y)
            {
                entry.pitchRange = new Vector2(entry.pitchRange.y, entry.pitchRange.x);
                repaired = true;
            }

            if (entry.volume <= 0f)
            {
                entry.volume = 1f;
                repaired = true;
            }

            if (!repaired) continue;

            // Without this the repair only lives in memory, and OnValidate never runs in a build.
            UnityEditor.EditorUtility.SetDirty(this);

            Debug.LogWarning($"[{nameof(SO_FootstepBank)}] '{name}' surfaces[{i}] " +
                             $"({entry.surface}): repaired pitch/volume. Unity zero-fills new " +
                             "array elements and skips the C# defaults, so a freshly added entry " +
                             "needs this. Check the values are what you wanted.", this);
        }

        if (limpDragPitchRange.x <= 0f || limpDragPitchRange.y <= 0f)
            limpDragPitchRange = new Vector2(0.92f, 1.02f);
        else if (limpDragPitchRange.x > limpDragPitchRange.y)
            limpDragPitchRange = new Vector2(limpDragPitchRange.y, limpDragPitchRange.x);
    }
#endif
}
