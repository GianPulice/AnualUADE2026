using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a footstep every time whatever this component is riding on has travelled one stride.
/// One component for both walkers: the player and the Nemesis differ only in which bank, which
/// mixer bus and which stride they are given.
///
/// WHY DISTANCE AND NOT A TIMER
///
/// The cadence is driven by ground actually covered, not by a clock. That single choice buys three
/// things the spec's <c>footstepInterval = distance / speed</c> asks for and then has to maintain
/// by hand:
///   - The player's walk, sprint and crouch cadences fall out of the movement code for free. There
///     is nothing to keep in step with SO_Movement when a speed is retuned.
///   - The Nemesis speeds up in Chasing and its steps speed up with it, with no reference to the
///     FSM at all. Its four state speeds live in SO_NemesisMovement and this never reads them.
///   - Steps stop when the walker stops, including the cases nothing announces: hitting a wall,
///     being blocked by a NavMeshObstacle, a paused Time.timeScale. A timer keeps ticking through
///     all three and walks on the spot.
///
/// THE TELEPORT GUARD IS LOAD-BEARING. The Nemesis is warped — by NemesisStuckEscape, by the spawn
/// placement, by the elevator link — and a warp is displacement with no walking in it. Without
/// <see cref="teleportThreshold"/> a 30 m warp dumps 30 m into the accumulator and the monster
/// fires thirty footsteps in one frame from wherever it landed.
///
/// WHY THE SHARED POOL AND NOT AN OWNED AudioSource. Each step is handed to
/// <see cref="AudioManager.PlayClip"/>, which plays it from the pool at a fixed world point. That
/// is more correct than a source parented to the walker, not just less code: a footstep is over in
/// a third of a second and belongs where the foot landed, whereas a parented source keeps panning
/// and dopplering as the walker runs past the listener.
///
/// NOISE: this component makes SOUND and nothing else. It deliberately does NOT generate noise for
/// the Nemesis. In this project noise is a sphere (PlayerStateManager.AudioEmitingZone) whose
/// radius the movement states already own per gait; a second writer to it would leave a radius
/// behind and make the player permanently loud. See "Noise is a sphere, not an event" in
/// docs/CLAUDE.md.
///
/// Setup: drop it on the walker root, assign a bank, pick the bus. The player instance also wants
/// its PlayerStateManager in <see cref="player"/> so crouch, hidden and the limp are honoured;
/// leave that field empty on the Nemesis.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Audio/Footstep Emitter")]
public class FootstepEmitter : MonoBehaviour
{
    /// <summary>Which mixer bus the steps come out of. Append only — this is serialized.</summary>
    public enum EBus
    {
        Player  = 0,
        Nemesis = 1,
    }

    /// <summary>What decides when a step happens. Append only — this is serialized.</summary>
    public enum ECadence
    {
        /// <summary>Ground covered. Right when the animation's cadence follows the speed.</summary>
        Distance = 0,

        /// <summary>
        /// An AnimationEvent calling <see cref="Step"/> on the actual footfall frame. Right when it
        /// does not — which is the case for this project's Mixamo clips: Walking and
        /// CrouchedWalking are both 32 frames and play at a fixed rate, so the feet land at the
        /// same cadence whether the character is moving at 2.5 m/s or at 1.25 m/s crouched. Under
        /// Distance those two get half the steps of each other; under AnimationEvent they match,
        /// which is what crouching actually looks like.
        /// </summary>
        AnimationEvent = 1,
    }

    [Header("Content")]
    [Tooltip("The clips, per surface. Without one this component does nothing and says so once.")]
    [SerializeField] private SO_FootstepBank bank;

    [Header("Routing")]
    [SerializeField] private EBus bus = EBus.Player;

    [Header("Cadence")]
    [Tooltip("Distance = a step every strideLength metres. AnimationEvent = a step whenever the " +
             "animation calls Step(), which is the only way to land on the actual footfall when " +
             "the clips play at a fixed rate instead of scaling with speed.")]
    [SerializeField] private ECadence cadenceSource = ECadence.Distance;

    [Tooltip("Metres of ground covered per step, in Distance mode. Smaller = more steps.\n\n" +
             "Work it out from the animation, not by ear: speed divided by the clip's real footfall " +
             "rate. The player's Walking clip is 32 frames at 30 fps with two footfalls, so 1.94 " +
             "steps/s; at moveSpeed 2.5 that is a 1.29 m stride. Guessing low is what made the " +
             "first pass sound like a jog.")]
    [SerializeField, Min(0.1f)] private float strideLength = 1.29f;

    [Tooltip("Below this speed (m/s) nothing is walking, so nothing steps. It filters out the " +
             "centimetre of physics jitter a Rigidbody has while standing still, which would " +
             "otherwise accumulate into a step every few seconds from a character stood in a corner.")]
    [SerializeField, Min(0f)] private float minSpeed = 0.35f;

    [Tooltip("Displacement in ONE frame above which this is a teleport and not walking, so the " +
             "accumulator is dropped instead of spent. Must stay above the fastest single-frame " +
             "step (chase speed at a bad frame rate) and below the shortest warp.")]
    [SerializeField, Min(0.1f)] private float teleportThreshold = 1.5f;

    [Header("3D falloff")]
    [SerializeField, Min(0f)] private float minDistance = 2f;

    [Tooltip("Unity's default is 500, which is audible across the whole level and makes distance " +
             "useless as information. The Nemesis's steps are a tell; 20-25 m is the range that " +
             "makes them one.")]
    [SerializeField, Min(0.1f)] private float maxDistance = 20f;

    [Tooltip("Master scale on top of the per-surface volume. This is the knob for 'the monster is " +
             "heavier than the player', not the per-surface one.")]
    [SerializeField, Range(0f, 2f)] private float volumeScale = 1f;

    [Header("Occlusion")]
    [Tooltip("Attenuate a step when a wall stands between where it landed and the listener.\n\n" +
             "Without this the Nemesis's steps are audible through the whole level at their full " +
             "rolloff volume, which is what makes them read as 'always there' instead of as " +
             "somewhere. NemesisAudio already does this for its breathing loops; one-shots fired " +
             "through the AudioManager pool get no occlusion of their own, so it lives here.")]
    [SerializeField] private bool occlusionEnabled = true;

    [Tooltip("What counts as a wall. Wall only — NOT Ground, or every step taken on a floor above " +
             "or below the listener reads as occluded by the floor between them, which is true but " +
             "makes vertical proximity impossible to hear.")]
    [SerializeField] private LayerMask occluderMask;

    [Tooltip("Volume multiplier through a wall. Attenuation, never a cut: 0 would make the monster " +
             "silent the moment it steps behind a pillar, which is worse information than too loud.")]
    [SerializeField, Range(0f, 1f)] private float occludedVolume = 0.35f;

    [Header("Ground probe")]
    [Tooltip("What counts as a floor worth reading a surface off. Ground + Props + Water + Default " +
             "mirrors the player's own ground mask plus the layers a walkable prop can sit on.")]
    [SerializeField] private LayerMask probeMask = ~0;

    [Tooltip("How far above the emitter the probe starts. It has to clear the floor the walker is " +
             "standing on or the ray starts underneath it and hits nothing.")]
    [SerializeField, Min(0f)] private float probeStart = 0.4f;

    [SerializeField, Min(0.1f)] private float probeLength = 1.6f;

    [Tooltip("Where the sound comes from, relative to this transform. Roughly the feet.")]
    [SerializeField] private Vector3 footOffset = Vector3.zero;

    [Header("Player hooks — leave EMPTY on the Nemesis")]
    [Tooltip("When set, steps are suppressed while hidden, disabled or airborne, crouching gets " +
             "its own volume and stride, and the M1 legs penalty turns on the limp alternation.")]
    [SerializeField] private PlayerStateManager player;

    [SerializeField, Range(0f, 1f)] private float crouchVolumeScale = 0.45f;

    [Tooltip("Multiplies the stride while crouched, in Distance mode only.\n\n" +
             "0.5 and not something above 1, which is the intuitive-but-wrong answer. Crouching " +
             "halves the speed but CrouchedWalking is the same 32 frames as Walking, so the feet " +
             "land at the SAME rate — the character just covers less ground per step. Scaling the " +
             "stride by the same 0.5 the speed is scaled by is what keeps the two cadences equal. " +
             "In AnimationEvent mode this is ignored and the clip settles it.")]
    [SerializeField, Min(0.1f)] private float crouchStrideScale = 0.5f;

    [Tooltip("Multiplies the stride while sprinting, in Distance mode only.\n\n" +
             "The same correction as the crouch one and it was the missing half of it: crouch " +
             "keeps the walk clip and changes the speed, sprint changes BOTH — a different clip " +
             "AND a different speed — so the two do not cancel and a single stride cannot serve " +
             "both.\n\n" +
             "Worked out from the clips, not by ear. Neither the Walking nor the Running state " +
             "has Speed Parameter on, so both play at a FIXED rate however fast the player is " +
             "actually moving: Walking is 32 frames at 30 fps = 1.87 footfalls/s, Running is 22 " +
             "frames = 2.73 footfalls/s. At moveSpeed 2.5 that walk rate is the 1.29 m stride " +
             "above; sprinting at 2.5 x 1.8 = 4.5 m/s the right stride is 4.5 / 2.73 = 1.65 m, " +
             "which is 1.28 times the walking one.\n\n" +
             "Left at 1 the audio runs 4.5 / 1.29 = 3.5 steps/s against an animation playing " +
             "2.73 — about 28% too many steps, which is what a sprint that sounds out of time " +
             "with the legs is.\n\n" +
             "Re-derive it if either clip is retimed or the speeds change; in AnimationEvent mode " +
             "it is ignored and the clip settles it.")]
    [SerializeField, Min(0.1f)] private float sprintStrideScale = 1.28f;

    /// <summary>
    /// Speed multiplier above which the player counts as sprinting.
    ///
    /// Read off <see cref="PlayerStateManager.SpeedMultiplier"/> rather than an input check or a
    /// state test, which is the same source <c>CameraSprintEffect</c> uses and for the reason it
    /// documents: PlayerMovingState raises it above 1 only while the sprint button is actually
    /// held, so the states that ignore sprint (crouch, hidden, interacting, disabled) need no
    /// special case here.
    /// </summary>
    private const float SprintSpeedMultiplierThreshold = 1.01f;

    // ── Runtime ─────────────────────────────────────────────────────────────

    private Vector3 lastPosition;
    private float distanceAccumulator;

    /// <summary>Alternates step / drag while the legs penalty is active. Reset when it is not.</summary>
    private bool limpDragNext;

    private bool warned;

    /// <summary>One shuffle order per surface entry, plus one for the limp drags.</summary>
    private readonly Dictionary<SO_FootstepBank.SurfaceEntry, ShuffleBag> bags = new();
    private ShuffleBag limpBag;

    /// <summary>
    /// A shuffled play order that refills when it runs out, rather than an independent random draw
    /// per step.
    ///
    /// The difference is audible. Independent draws over four clips repeat the same clip back to
    /// back about a quarter of the time, and a repeated footstep is the single loudest tell that a
    /// sound is canned. A bag hears every clip once per cycle, and refusing to open a new bag on
    /// the clip the last one closed with removes the only remaining way to get a repeat.
    /// </summary>
    private class ShuffleBag
    {
        private readonly int[] order;
        private int cursor;
        private int lastReturned = -1;

        public ShuffleBag(int count)
        {
            order = new int[Mathf.Max(count, 1)];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            cursor = order.Length; // forces a shuffle on the first Next()
        }

        public int Count => order.Length;

        public int Next()
        {
            if (order.Length == 1) return 0;

            if (cursor >= order.Length)
            {
                Reshuffle();
                cursor = 0;
            }

            lastReturned = order[cursor++];
            return lastReturned;
        }

        private void Reshuffle()
        {
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            // The one case a shuffle cannot rule out: the new bag opening on the clip the old one
            // ended with. Swapping it to the back costs nothing and closes the last repeat path.
            if (order[0] == lastReturned)
                (order[0], order[^1]) = (order[^1], order[0]);
        }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        if (player == null && bus == EBus.Player)
            player = GetComponentInParent<PlayerStateManager>();
    }

    private void OnEnable()
    {
        // Not in Awake: the Nemesis spends the first part of the run disabled and is warped to its
        // spawn on the way in. Seeding here means the first frame after it wakes measures from
        // where it actually is, instead of firing a burst from wherever the prefab sat.
        lastPosition = transform.position;
        distanceAccumulator = 0f;
        limpDragNext = false;
    }

    // LateUpdate, so the displacement read is the one the Rigidbody and the NavMeshAgent have both
    // already finished writing this frame.
    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;

        delta.y = 0f; // stride is ground covered; a lift ride is not walking
        float travelled = delta.magnitude;

        if (travelled > teleportThreshold)
        {
            // A warp. Spending it would fire a burst of steps at the destination.
            distanceAccumulator = 0f;
            return;
        }

        if (IsSuppressed())
        {
            distanceAccumulator = 0f;
            return;
        }

        // In AnimationEvent mode everything above still runs — the teleport guard and the
        // suppression checks are wanted either way — but the animation decides when a step lands.
        if (cadenceSource == ECadence.AnimationEvent) return;

        if (travelled / dt < minSpeed) return;

        distanceAccumulator += travelled;

        float stride = CurrentStride();

        // Bounded by construction — teleportThreshold caps travelled and strideLength has a Min of
        // 0.1 — but a bank retuned to something silly should not be able to stall a frame.
        for (int guard = 0; guard < 8 && distanceAccumulator >= stride; guard++)
        {
            distanceAccumulator -= stride;
            PlayStep();
        }
    }

    /// <summary>
    /// True while this walker should be silent. Only the player has cases: hidden (the geometry of
    /// the hiding spot is meant to be the reason you cannot be heard), disabled (cinematics,
    /// capture, loading) and airborne (there is no floor to step on).
    /// </summary>
    private bool IsSuppressed()
    {
        if (player == null) return false;
        return player.IsHidden || player.IsDisabled || !player.IsGrounded;
    }

    /// <summary>
    /// The stride for whatever the player is doing right now.
    ///
    /// Crouch and sprint are exclusive by construction — PlayerMovingState never raises the speed
    /// multiplier above 1 while crouched — so this is an either/or rather than two multipliers
    /// stacking. Written as one so a future state that somehow held both could not produce a
    /// stride neither clip was measured for.
    /// </summary>
    private float CurrentStride()
    {
        float stride = strideLength;

        if (player != null)
        {
            if (player.IsCrouch) stride *= crouchStrideScale;
            else if (player.SpeedMultiplier > SprintSpeedMultiplierThreshold) stride *= sprintStrideScale;
        }

        return Mathf.Max(stride, 0.1f);
    }

    /// <summary>
    /// The master volume scale for this step. Crouching is quieter as well as less frequent — the
    /// stride alone only changes how often you hear it, not how loud it is.
    /// </summary>
    private float CurrentVolumeScale()
    {
        float scale = volumeScale;
        if (player != null && player.IsCrouch) scale *= crouchVolumeScale;
        return scale;
    }

    // ── Playback ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires one step now. This is the AnimationEvent entry point: put an event on the footfall
    /// frame of a locomotion clip calling <c>Step</c>.
    ///
    /// Unity delivers an AnimationEvent to the GameObject that owns the Animator, which on the
    /// player is the rig child and not the root this component sits on — hence
    /// <see cref="FootstepAnimationRelay"/>, which is what the clips actually call.
    ///
    /// Does nothing in Distance mode, so flipping <see cref="cadenceSource"/> back does not leave
    /// the animation firing steps on top of the accumulator.
    /// </summary>
    public void Step()
    {
        if (cadenceSource != ECadence.AnimationEvent) return;
        if (IsSuppressed()) return;
        PlayStep();
    }

    private void PlayStep()
    {
        if (bank == null)
        {
            WarnOnce("has no SO_FootstepBank, so it will never make a sound. Assign one.");
            return;
        }

        Collider ground = ProbeGround();

        // The limp is an alternation, not a replacement: step, drag, step, drag. Only the drag half
        // comes out of the limp clips; the other half is a normal step on whatever is underfoot.
        bool legsPenalty = player != null && player.LegsPenaltyActive;
        if (!legsPenalty) limpDragNext = false;

        if (legsPenalty && limpDragNext && bank.HasLimpDrag)
        {
            limpDragNext = false;
            limpBag = Emit(bank.LimpDragClips, limpBag, bank.LimpDragVolume, bank.LimpDragPitchRange);
            return;
        }

        if (legsPenalty) limpDragNext = true;

        SO_FootstepBank.SurfaceEntry entry = bank.Resolve(ground);
        if (entry == null)
        {
            WarnOnce($"the bank '{bank.name}' has no surface with a clip in it.");
            return;
        }

        bags.TryGetValue(entry, out ShuffleBag bag);
        bags[entry] = Emit(entry.clips, bag, entry.volume, entry.pitchRange);
    }

    /// <summary>
    /// Picks the next clip out of <paramref name="bag"/> and hands it to the AudioManager pool.
    /// Returns the bag to store back, rebuilt when the clip count has changed under it (which is
    /// what happens when a bank is edited in play mode).
    /// </summary>
    private ShuffleBag Emit(AudioClip[] clips, ShuffleBag bag, float volume, Vector2 pitchRange)
    {
        int count = CountClips(clips);
        if (count == 0) return bag;

        if (bag == null || bag.Count != count) bag = new ShuffleBag(count);

        AudioClip clip = NthNonNull(clips, bag.Next());
        if (clip == null) return bag;

        AudioManager manager = AudioManager.Instance;
        if (manager == null)
        {
            // Every audio caller in this project depends on the Data scene being loaded. Say so
            // once instead of throwing every step in a scene opened on its own.
            WarnOnce("there is no AudioManager in the scene. Press Play from Bootstrap.");
            return bag;
        }

        Vector3 at = transform.position + footOffset;
        float vol = Mathf.Clamp01(volume * CurrentVolumeScale() * OcclusionMultiplier(at));
        float pitch = Random.Range(pitchRange.x, pitchRange.y);

        if (bus == EBus.Nemesis) manager.PlayNemesis(clip, at, vol, pitch, minDistance, maxDistance);
        else                     manager.PlayPlayer (clip, at, vol, pitch, minDistance, maxDistance);

        return bag;
    }

    /// <summary>
    /// Cached across every emitter — there is exactly one AudioListener in a scene, and looking it
    /// up per step from two walkers is a scene scan several times a second for a value that never
    /// changes. Cleared when the reference goes null, which is what a scene change looks like.
    /// </summary>
    private static AudioListener cachedListener;

    /// <summary>
    /// 1 with a clear line to the listener, <see cref="occludedVolume"/> through a wall.
    ///
    /// One linecast per step, so a couple per second per walker — cheap enough to do per shot and
    /// far more accurate than a per-frame value would need to be, since a footstep is a point in
    /// time and only the geometry at that instant matters.
    /// </summary>
    private float OcclusionMultiplier(Vector3 from)
    {
        if (!occlusionEnabled || occluderMask.value == 0) return 1f;

        // FindAnyObjectByType and not FindFirstObjectByType: the latter is deprecated because it
        // orders by instance ID, and that ordering is worth nothing here — a scene only ever has
        // one AudioListener, so "any" is "the one".
        if (cachedListener == null) cachedListener = FindAnyObjectByType<AudioListener>();
        if (cachedListener == null) return 1f;

        // Lifted off the floor: a ray starting exactly on the ground plane grazes it and can
        // report the floor itself as the occluder.
        Vector3 origin = from + Vector3.up * 0.5f;

        return Physics.Linecast(origin, cachedListener.transform.position, occluderMask,
                                QueryTriggerInteraction.Ignore)
             ? occludedVolume
             : 1f;
    }

    private Collider ProbeGround()
    {
        Vector3 origin = transform.position + footOffset + Vector3.up * probeStart;

        return Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                               probeStart + probeLength, probeMask,
                               QueryTriggerInteraction.Ignore)
             ? hit.collider
             : null;
    }

    private void WarnOnce(string message)
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning($"[{nameof(FootstepEmitter)}] '{name}': {message}", this);
    }

    private static int CountClips(AudioClip[] clips)
    {
        if (clips == null) return 0;
        int n = 0;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null) n++;
        return n;
    }

    /// <summary>
    /// The nth non-null clip. The shuffle indexes the PLAYABLE clips, not the raw array, so an
    /// empty slot left in the middle of an array in the inspector costs a variation instead of
    /// producing a silent step.
    /// </summary>
    private static AudioClip NthNonNull(AudioClip[] clips, int n)
    {
        if (clips == null) return null;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;
            if (n == 0) return clips[i];
            n--;
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxDistance <= minDistance) maxDistance = minDistance + 0.1f;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + footOffset + Vector3.up * probeStart;
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
        Gizmos.DrawLine(origin, origin + Vector3.down * (probeStart + probeLength));
    }
#endif
}
