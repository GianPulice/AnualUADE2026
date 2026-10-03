using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Hearing sensor of the Nemesis.
///
/// THE EAR IS NOT A GPS (Plan-Busqueda-Nemesis, Fase 1). A noise of the PLAYER's is reported at a
/// perceived point: the real one plus an offset of up to SO_NemesisData.HearingLocalizationError ×
/// that evidence's radius, drifting smoothly in time and pinned to the NavMesh of the player's own
/// floor (<see cref="HearingLocalization"/>). The belief, the states and the hiding awareness all get
/// only that point; the real transform never leaves this sensor (the editor keeps a copy for the
/// gizmo and F9, and nothing else can read it). Decoys and Director pulses stay exact: they are
/// leads at a known place, and nothing about them is the player.
///
/// The tuneable values (range, wall occlusion) live in <see cref="SO_NemesisData"/> so a
/// designer edits them in one asset and Tier 3.3 can scale them by handing this component a
/// runtime copy of the SO through <see cref="SetData"/>. The LayerMasks stay here: those are
/// scene wiring, not design values.
/// </summary>
public class FieldOfListening : MonoBehaviour
{
    [Tooltip("Seconds between hearing sweeps. Kept on the component: it is a performance knob, " +
             "not a design value.")]
    [SerializeField] private float listenDelay = 0.1f;

    [SerializeField] private LayerMask listenMask;

    /// <summary>
    /// Which layers count as a noise. Exposed so <see cref="NemesisDirector"/> can check on load
    /// that the layer it spawns its synthetic noises on is one this actually listens to — a
    /// mismatch there produces no error and no sound, just a lever that does nothing.
    /// </summary>
    public LayerMask ListenMask => listenMask;

    [Tooltip("Geometric line of sight. Used by IsOccludedByWall, which despite living on the " +
             "hearing sensor answers a VISION question for four other systems: whether the " +
             "capture has a wall in the way, whether a spawn point is in view of the player, " +
             "whether a stuck-escape warp would be seen, and whether the Nemesis's own audio is " +
             "muffled. Floors belong in here. Changing it changes all four.")]
    [SerializeField] private LayerMask obstacleMask;

    [Tooltip("What actually stops SOUND. Walls, not floors.\n\n" +
             "Separate from obstacleMask because the two questions have different answers: the " +
             "Nemesis must never SEE through a slab, but it does HEAR through one — that is the " +
             "only channel it has to the storey above, and without it a player upstairs simply " +
             "does not exist to it.\n\n" +
             "Left empty it is derived from obstacleMask minus floorMask, which reproduces the " +
             "intended behaviour without anyone having to fill it in.")]
    [SerializeField] private LayerMask soundBlockerMask;

    [Tooltip("Floor and ceiling slabs. Attenuate sound by SO_NemesisData.FloorOcclusionMultiplier " +
             "instead of blocking it.\n\n" +
             "Left empty it defaults to the Ground layer.")]
    [SerializeField] private LayerMask floorMask;

    [Header("Data")]
    [Tooltip("Optional. If empty it is taken from the NemesisStateManager in the parents.")]
    [SerializeField] private SO_NemesisData nemesisData;

    private List<GameObject> listenedTargets;
    private float currentTimer = 0;
    private bool hasAudioTarget = false;
    private Vector3 lastKnownPosition;
    private bool hasLastKnownPosition;

    private float lastNoiseTime;

    /// <summary>Seeds this listener's drift of <see cref="HearingLocalization.Offset"/>.</summary>
    private float errorSeed;

    /// <summary>How far from the player's real position the NavMesh is looked for, to pin the
    /// perceived point on their floor. Generous: a player inside a locker is off the mesh.</summary>
    private const float PerceivedSnapRadius = 2f;

    public bool HasAudioTarget { get => hasAudioTarget; }
    public Vector3 LastKnownPosition { get => lastKnownPosition; }

    /// <summary>Whether <see cref="LastKnownPosition"/> means anything yet. Same reason as
    /// <see cref="FieldOfView.HasLastKnownPosition"/>: Vector3.zero is a valid level coordinate
    /// and cannot stand in for "I have not heard anything yet".</summary>
    public bool HasLastKnownPosition { get => hasLastKnownPosition; }

    /// <summary>The decoy behind <see cref="LastKnownPosition"/>, or null when the last noise that
    /// won was anything else (the player, a Director pulse). <see cref="NemesisDecoyBreaker"/>
    /// reads it to know which decoy the Nemesis came for — proximity alone would let it smash a
    /// radio it walked past while chasing a footstep.</summary>
    public DecoyNoiseSource LastHeardDecoy { get; private set; }

    /// <summary>
    /// One noise as this sensor heard it (plan §17): where, when, how far along the NavMesh, and what
    /// it had to get through on the way. The sensor reports; NemesisBelief turns it into a radius.
    /// </summary>
    public readonly struct HeardNoise
    {
        public readonly Vector3 Position;
        public readonly float HeardAt;
        public readonly float Distance;
        public readonly bool ThroughWall;
        public readonly bool ThroughFloor;

        /// <summary>The decoy that made it, or null for anything else.</summary>
        public readonly DecoyNoiseSource Decoy;

        /// <summary>
        /// The player's own noise, made from inside a hiding spot (plan D22): breathing, a sigh, the
        /// exhale after holding it. Muffled by the furniture it comes out of, so it says "over
        /// there" rather than "here" — NemesisBelief widens its radius, and a search around it
        /// sweeps the area instead of walking to the door.
        ///
        /// This is how the sound travels, not knowledge the Nemesis did not earn: it says nothing
        /// about WHICH spot, and the answer it produces is less precise, never more.
        /// </summary>
        public readonly bool FromHidingSpot;

        /// <summary>
        /// How far off <see cref="Position"/> may be, in metres: the longest offset the ear could have
        /// put on it (Plan-Busqueda-Nemesis Fase 1). Zero for a lead. The sensor's own uncertainty,
        /// not where the player is: anything comparing two noises ("did it sound from the same
        /// place twice") has to allow for it.
        /// </summary>
        public readonly float LocalizationError;

        public HeardNoise(Vector3 position, float heardAt, float distance, bool throughWall,
                          bool throughFloor, DecoyNoiseSource decoy, bool fromHidingSpot = false,
                          float localizationError = 0f)
        {
            Position = position;
            HeardAt = heardAt;
            Distance = distance;
            ThroughWall = throughWall;
            ThroughFloor = throughFloor;
            Decoy = decoy;
            FromHidingSpot = fromHidingSpot;
            LocalizationError = localizationError;
        }
    }

    /// <summary>
    /// The player's own noise — footsteps, breathing — was heard this sweep.
    ///
    /// <see cref="HasAudioTarget"/> is ANY noise, and it stays that; this is the player only. The
    /// difference is plan §17: a decoy or a Director pulse used to be the player's position simply
    /// by being the loudest thing in range.
    /// </summary>
    public bool HeardPlayer { get; private set; }

    /// <summary>The player's noise heard this sweep was a SOFT one — crouching, under
    /// SO_NemesisData.SoftNoiseLoudness — and not from inside a hiding spot. It feeds the suspicion
    /// meter the glimpses fill (plan §17.3, shared suspicion).</summary>
    public bool HeardSoftPlayerNoise { get; private set; }

    /// <summary>Something that is NOT the player was heard this sweep: a decoy, a Director pulse, any
    /// other trigger on the listen layer. A lead (D18, D19).</summary>
    public bool HeardLead { get; private set; }

    /// <summary>The best-heard player noise of the latest sweep that heard one.</summary>
    public bool TryGetLastPlayerNoise(out HeardNoise noise)
    {
        noise = lastPlayerNoise;
        return hasPlayerNoise;
    }

    /// <summary>The best-heard lead of the latest sweep that heard one.</summary>
    public bool TryGetLastLead(out HeardNoise noise)
    {
        noise = lastLead;
        return hasLeadNoise;
    }

    private bool hasPlayerNoise;
    private HeardNoise lastPlayerNoise;
    private bool hasLeadNoise;
    private HeardNoise lastLead;

    /// <summary>Seconds since the last noise was heard, or infinity if none ever was. Same
    /// purpose as <see cref="FieldOfView.TimeSinceLastSighting"/>: how much the memory is still
    /// worth, as opposed to whether it exists.</summary>
    public float TimeSinceLastNoise =>
        hasLastKnownPosition ? Time.time - lastNoiseTime : float.PositiveInfinity;

    private void Awake()
    {
        listenedTargets = new List<GameObject>();

        // Per listener, so the way this ear mishears is its own (HearingLocalization).
        errorSeed = Random.Range(0f, 1000f);

        ResolveAcousticMasks();

        if (nemesisData != null) return;

        NemesisStateManager manager = GetComponentInParent<NemesisStateManager>();
        if (manager != null) nemesisData = manager.NemesisData;

        if (nemesisData == null)
            Debug.LogError($"[{nameof(FieldOfListening)}] No SO_NemesisData assigned and none " +
                           $"found in the parents — the Nemesis will not hear anything.", this);
    }

    /// <summary>
    /// Fills in the two acoustic masks when the scene left them empty.
    ///
    /// Derived rather than reported missing, because these fields were added to a component that
    /// already ships on prefabs, and an empty LayerMask is not a neutral default here — it is the
    /// most dangerous possible value. A soundBlockerMask of Nothing means no geometry ever
    /// attenuates anything, so the Nemesis would hear the player through the entire level. That
    /// regression would land on every existing Nemesis the moment this file compiled, and it
    /// fails silently: nothing errors, the monster simply becomes omniscient.
    ///
    /// The derivation reproduces exactly the behaviour that was there before — obstacleMask
    /// blocked sound, floors included — minus the floors, which is the whole point of the change.
    /// Anything set explicitly in the inspector wins.
    /// </summary>
    private void ResolveAcousticMasks()
    {
        if (floorMask == 0)
        {
            int ground = LayerMask.GetMask("Ground");

            // A project with no Ground layer is not an error here: it means floors are on Default
            // along with everything else, and no mask can tell them apart. Sound then attenuates
            // through them like a wall, which is where this started.
            if (ground != 0) floorMask = ground;
        }

        if (soundBlockerMask == 0) soundBlockerMask = obstacleMask & ~floorMask;
    }

    /// <summary>
    /// Drops the memory of the last noise. See <see cref="FieldOfView.ForgetLastKnownPosition"/>
    /// for why a capture is the only thing entitled to call this.
    /// </summary>
    public void ForgetLastKnownPosition()
    {
        hasAudioTarget = false;
        hasLastKnownPosition = false;
        LastHeardDecoy = null;

        HeardPlayer = false;
        HeardSoftPlayerNoise = false;
        HeardLead = false;
        hasPlayerNoise = false;
        hasLeadNoise = false;
    }

    /// <summary>
    /// Swaps the data asset at runtime. Tier 3.3 uses it to push a scaled copy of the SO without
    /// touching the original asset.
    /// </summary>
    public void SetData(SO_NemesisData data)
    {
        if (data == null) return;
        nemesisData = data;
    }

    // Update is called once per frame
    void Update()
    {
        // Same guard as NemesisStateManager: this Update is its own, so without it the
        // Nemesis kept hearing (and reacting) with the game paused.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        if (currentTimer < listenDelay) currentTimer += Time.deltaTime;
        else
        {
            currentTimer = 0;
            ListenTargets();
        }
    }
    private void ListenTargets()
    {
        if (nemesisData == null) return;

        float listenRange = nemesisData.ListenRange;

        listenedTargets.Clear();
        sweptEmitters.Clear();
        bool heardAny = false;
        Vector3 loudestPosition = default;
        DecoyNoiseSource loudestDecoy = null;
        float loudestMargin = float.NegativeInfinity;

        BeginSweep();

        Collider[] targetsInListenRadius = Physics.OverlapSphere(transform.position, listenRange, listenMask);
        for (int i = 0; i < targetsInListenRadius.Length; i++)
        {
            Collider emitter = targetsInListenRadius[i];
            GameObject target = emitter.gameObject;
            if (listenedTargets.Contains(target)) continue;

            // A noise is a TRIGGER sized to how loud it is — the player's emitter and the
            // Director's synthetic pulses both are. A SOLID collider on the listen layer is level
            // geometry on the wrong layer, and it is the worst kind of mistake this sensor can
            // meet: it is always "on", it never moves, and its bounds read as a loud noise. Zona1's
            // Stair_Divider was one, and it turned the whole stairwell into a permanent sound the
            // monster kept walking over to investigate (WIR-018 / WIR-020). Ignored, and named once.
            if (!emitter.isTrigger)
            {
                WarnSolidEmitter(emitter);
                continue;
            }

            sweptEmitters.Add(emitter);

            // The OverlapSphere is only a broadphase now — how loud the emitter actually is
            // decides the real range, and that is read off the collider it just returned.
            float loudness = GetEmitterRadius(emitter);
            if (!TryHear(emitter, listenRange, loudness, out float margin, out float distance,
                         out bool throughWall, out bool throughFloor)) continue;

            listenedTargets.Add(target);

            // WHO made it, as well as how loud (plan §17). The player's own emitter is evidence
            // about the player; anything else on the listen layer is a lead. Told apart by what the
            // collider IS, not by where it sits: footsteps and breathing are not a radio.
            bool fromPlayer = IsPlayerEmitter(emitter);
            bool fromHidingSpot = fromPlayer && IsPlayerHidden();

            // The player is heard where the ear places them, not where they are (Fase 1).
            Vector3 truth = target.transform.position;
            Vector3 heardAt = truth;
            float error = 0f;
            if (fromPlayer)
                heardAt = Mislocate(truth, distance, throughWall, throughFloor, fromHidingSpot, out error);

            // The one that is heard BEST, not the first the physics query happened to return:
            // with the Director's synthetic pulse and the player both audible, which one the
            // monster turns to must not depend on collider order.
            if (margin > loudestMargin)
            {
                heardAny = true;
                loudestMargin = margin;
                loudestPosition = heardAt;
                loudestDecoy = null;
            }

            var heard = new HeardNoise(heardAt, Time.time, distance, throughWall, throughFloor, null,
                                       fromHidingSpot, error);
            if (fromPlayer) OfferPlayerNoise(heard, margin, truth);
            else            OfferLead(heard, margin);

            // A SOFT noise of the player's — crouching — also feeds the suspicion meter the glimpses
            // fill (plan §17.3, shared suspicion; case 26). Not from inside a hiding spot: breathing
            // there has its own rules (D21, D22).
            if (fromPlayer && !heard.FromHidingSpot && loudness <= nemesisData.SoftNoiseLoudness)
                sweepHeardSoftPlayer = true;
        }

        ListenDecoys(ref heardAny, ref loudestMargin, ref loudestPosition, ref loudestDecoy);

        DropStalePathCaches();

        if (heardAny)
        {
            hasAudioTarget = true;
            lastKnownPosition = loudestPosition;
            hasLastKnownPosition = true;
            lastNoiseTime = Time.time;
            LastHeardDecoy = loudestDecoy;
        }
        else hasAudioTarget = false;

        CommitSweep();
    }

    /// <summary>
    /// The second input channel: decoys (radio, fire alarm, chains) registered in
    /// <see cref="DecoyNoiseSource.Active"/>.
    ///
    /// A channel of their own and not a trigger sphere on the listen layer, because the design
    /// asks for two things a sphere cannot say. A decoy states how far it is heard in plain metres
    /// ("the radio reaches a medium distance X"), with no NoiseRangeScale in between and no
    /// ListenRange ceiling on top; and the fire alarm is heard from anywhere, which the
    /// OverlapSphere broadphase — bounded by ListenRange — would never even return.
    ///
    /// Walls and floors still muffle a ranged decoy exactly as they muffle the player, through the
    /// same TryHearAt. An audible-everywhere one skips all of it and competes with margin 0: it is
    /// always heard, but a noise that is genuinely heard nearby still wins over it.
    /// </summary>
    private void ListenDecoys(ref bool heardAny, ref float loudestMargin, ref Vector3 loudestPosition,
                              ref DecoyNoiseSource loudestDecoy)
    {
        IReadOnlyList<DecoyNoiseSource> decoys = DecoyNoiseSource.Active;
        for (int i = 0; i < decoys.Count; i++)
        {
            DecoyNoiseSource decoy = decoys[i];
            if (decoy == null || !decoy.IsEmitting) continue;

            float margin = 0f;
            float distance = Vector3.Distance(transform.position, decoy.NoisePosition);
            bool throughWall = false;
            bool throughFloor = false;
            if (!decoy.AudibleEverywhere)
            {
                sweptEmitters.Add(decoy);
                if (!TryHearAt(decoy, decoy.NoisePosition, decoy.HearingDistance, out margin,
                               out distance, out throughWall, out throughFloor)) continue;
            }

            // Always a lead: a decoy is never the player (D18).
            OfferLead(new HeardNoise(decoy.NoisePosition, Time.time, distance, throughWall,
                                     throughFloor, decoy), margin);

            if (margin > loudestMargin)
            {
                heardAny = true;
                loudestMargin = margin;
                loudestPosition = decoy.NoisePosition;
                loudestDecoy = decoy;
            }
        }
    }

    // ── Who made it (plan §17) ───────────────────────────────────────────────
    //
    // Same rule as the loudest-overall pick above — the one heard BEST, not the first the physics
    // query returned — kept once for the player and once for everything else.

    private bool sweepHeardPlayer;
    private bool sweepHeardSoftPlayer;
    private float sweepPlayerMargin;
    private HeardNoise sweepPlayer;
#if UNITY_EDITOR
    private Vector3 sweepPlayerTruth;
    private Vector3 lastPlayerNoiseTruth;

    /// <summary>
    /// EDITOR ONLY: where the player REALLY was when <see cref="TryGetLastPlayerNoise"/>'s noise was
    /// heard. For NemesisGizmos and F9, to tune HearingLocalizationError against the truth. Behind
    /// UNITY_EDITOR so nothing that ships can read it: the whole point of Fase 1 is that the real
    /// position never leaves this sensor.
    /// </summary>
    public bool TryGetLastPlayerNoiseTruth(out Vector3 truth)
    {
        truth = lastPlayerNoiseTruth;
        return hasPlayerNoise;
    }
#endif
    private bool sweepHeardLead;
    private float sweepLeadMargin;
    private HeardNoise sweepLead;

    private void BeginSweep()
    {
        sweepHeardPlayer = false;
        sweepHeardSoftPlayer = false;
        sweepPlayerMargin = float.NegativeInfinity;
        sweepHeardLead = false;
        sweepLeadMargin = float.NegativeInfinity;
    }

    /// <param name="truth">Where the player really was. Kept for the editor's gizmo and F9 only.
    /// </param>
    private void OfferPlayerNoise(in HeardNoise heard, float margin, Vector3 truth)
    {
        if (margin <= sweepPlayerMargin) return;
        sweepHeardPlayer = true;
        sweepPlayerMargin = margin;
        sweepPlayer = heard;
#if UNITY_EDITOR
        sweepPlayerTruth = truth;
#endif
    }

    private void OfferLead(in HeardNoise heard, float margin)
    {
        if (margin <= sweepLeadMargin) return;
        sweepHeardLead = true;
        sweepLeadMargin = margin;
        sweepLead = heard;
    }

    private void CommitSweep()
    {
        HeardPlayer = sweepHeardPlayer;
        HeardSoftPlayerNoise = sweepHeardSoftPlayer;
        if (sweepHeardPlayer)
        {
            lastPlayerNoise = sweepPlayer;
            hasPlayerNoise = true;
#if UNITY_EDITOR
            lastPlayerNoiseTruth = sweepPlayerTruth;
#endif
        }

        HeardLead = sweepHeardLead;
        if (sweepHeardLead)
        {
            lastLead = sweepLead;
            hasLeadNoise = true;
        }
    }

    /// <summary>
    /// Whether a noise emitter is the player's own: the <c>AudioEmitingZone</c> every movement state
    /// and the hiding breath drive, or anything else hanging off the player.
    /// </summary>
    private static bool IsPlayerEmitter(Collider emitter)
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null) return false;

        return ReferenceEquals(player.AudioEmitingZone, emitter) ||
               emitter.transform.IsChildOf(player.transform);
    }

    /// <summary>Whether the player's noise is coming out of a hiding spot right now. See
    /// <see cref="HeardNoise.FromHidingSpot"/> for why this is acoustics, not omniscience.</summary>
    private static bool IsPlayerHidden()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        return player != null && player.IsHidden;
    }

    /// <summary>
    /// Where the ear places a noise of the player's that really came from <paramref name="truth"/>
    /// (Plan-Busqueda-Nemesis Fase 1, D39): off by up to HearingLocalizationError × the radius the
    /// belief will give this noise, so the error grows with distance, walls, floors and the furniture
    /// a breath comes out of, exactly as the belief's doubt does.
    ///
    /// PINNED TO THE NAVMESH OF THE PLAYER'S FLOOR, AND NOT THROUGH A WALL. The offset is walked out
    /// from the player's spot on the mesh with a NavMesh raycast, so it stops at the first edge: a
    /// noise in a corridor is misheard along the corridor, never inside the wall or on the far side of
    /// it, and never on the storey above. Without this, a few metres of error would routinely put the
    /// noise somewhere nobody can stand, and the search would sweep the wrong room for no reason the
    /// player could ever read.
    /// </summary>
    /// <param name="maxError">The longest offset this noise could get, for
    /// <see cref="HeardNoise.LocalizationError"/>.</param>
    private Vector3 Mislocate(Vector3 truth, float distance, bool throughWall, bool throughFloor,
                              bool fromHidingSpot, out float maxError)
    {
        float radius = NemesisBelief.NoiseRadiusFor(nemesisData, distance, throughWall, throughFloor,
                                                    fromHidingSpot);
        maxError = HearingLocalization.MaxError(radius, nemesisData.HearingLocalizationError);
        if (maxError <= 0f) return truth;

        Vector3 offset = HearingLocalization.Offset(errorSeed, Time.time, nemesisData.HearingErrorDriftTime,
                                                    maxError);

        // No mesh near the player (mid-jump over a gap, a level with no bake): the offset alone.
        if (!NavMesh.SamplePosition(truth, out NavMeshHit origin, PerceivedSnapRadius, NemesisNav.AreaMask))
            return truth + offset;

        Vector3 aim = origin.position + offset;
        if (NavMesh.Raycast(origin.position, aim, out NavMeshHit edge, NemesisNav.AreaMask))
            return edge.position;

        return NavMesh.SamplePosition(aim, out NavMeshHit landed, 0.5f, NemesisNav.AreaMask)
            ? landed.position
            : aim;
    }

    private readonly HashSet<Collider> warnedSolidEmitters = new HashSet<Collider>();

    private void WarnSolidEmitter(Collider c)
    {
        if (!warnedSolidEmitters.Add(c)) return;
        Debug.LogWarning($"[{nameof(FieldOfListening)}] '{c.name}' is a SOLID collider on a layer the " +
                         $"Nemesis listens to ({LayerMask.LayerToName(c.gameObject.layer)}). It is ignored: " +
                         "noise sources are triggers. Move it to Wall/Props/Default — as it is, it also " +
                         "stays out of the NavMesh bake and out of the vision obstacle mask.", c);
    }

    /// <summary>
    /// Whether a noise at this point is audible from here.
    ///
    /// Neither a wall nor a floor blocks sound outright — both attenuate it, and the model for
    /// that is a shrunken effective range. What differs is by how much, and that difference is
    /// the point of the whole change: a wall is a detour the Nemesis could walk around, so it
    /// muffles hard; a slab is the one surface vision can never cross, so hearing has to stay
    /// usable through it or the storey above may as well not exist.
    ///
    /// They multiply when both apply. A player crouching upstairs and behind a wall should be
    /// close to inaudible, not merely as muffled as either one alone.
    ///
    /// HOW LOUD THE PLAYER IS DECIDES THE RANGE. This is the part that used to be missing, and
    /// its absence is why moving quietly bought nothing where it mattered. The emitter radius
    /// (crouch 1 / walk 2 / run 6) only ever decided whether the broadphase OverlapSphere caught
    /// the collider; the moment any geometry was in the way the test collapsed to
    /// "listenRange * multiplier", which has no loudness term in it at all. Behind a wall a
    /// sprint and a crouch were audible at exactly the same distance.
    ///
    /// listenRange survives as a CEILING rather than as the range itself, so a designer can cap
    /// how far the Nemesis can ever hear without having to reason about the emitter.
    /// </summary>
    /// <param name="loudness">The noise emitter's own radius, in metres.</param>
    /// <param name="margin">How far inside its effective range the noise is, in metres — how
    /// clearly it is heard. Only meaningful when this returns true.</param>
    private bool TryHear(Collider emitter, float listenRange, float loudness, out float margin,
                         out float distance, out bool throughWall, out bool throughFloor)
    {
        float effectiveRange = Mathf.Min(listenRange, loudness * nemesisData.NoiseRangeScale);
        return TryHearAt(emitter, emitter.transform.position, effectiveRange, out margin,
                         out distance, out throughWall, out throughFloor);
    }

    /// <summary>The occlusion and distance half of <see cref="TryHear"/>, shared with the decoy
    /// channel, which arrives with its range already in metres.</summary>
    /// <param name="key">What the path-distance cache is keyed on: the emitter collider, or the
    /// decoy.</param>
    /// <param name="distance">How far the noise is, as measured (see MeasuredDistanceTo).</param>
    /// <param name="throughWall">A wall stands in the way. Reported so the belief can widen its
    /// radius (plan §17): a muffled noise is a rougher claim about where it came from.</param>
    /// <param name="throughFloor">A floor or ceiling slab stands in the way.</param>
    private bool TryHearAt(Object key, Vector3 source, float effectiveRange, out float margin,
                           out float distance, out bool throughWall, out bool throughFloor)
    {
        throughWall = false;
        throughFloor = false;

        if (nemesisData.WallOcclusionEnabled)
        {
            throughWall = IsBlockedBy(source, soundBlockerMask);
            throughFloor = IsBlockedBy(source, floorMask);

            if (throughWall)  effectiveRange *= nemesisData.WallOcclusionMultiplier;
            if (throughFloor) effectiveRange *= nemesisData.FloorOcclusionMultiplier;
        }

        distance = MeasuredDistanceTo(key, source);
        margin = effectiveRange - distance;
        return margin >= 0f;
    }

    /// <summary>
    /// How loud a noise source is, as the radius of its emitter in world units.
    ///
    /// Read off the collider the OverlapSphere already returned rather than by reaching into the
    /// player: the sensor stays a sensor, and anything that wants to be heard only has to carry a
    /// trigger sized to how loud it is.
    ///
    /// lossyScale matters because the emitter rides a scaled hierarchy — a SphereCollider of
    /// radius 6 under a parent scaled 0.5 is a 3-metre noise, and reading the raw radius would
    /// make every gait louder than the design says.
    ///
    /// The bounds fallback keeps a non-sphere emitter audible instead of silently inaudible,
    /// which is the failure mode worth avoiding: a designer who swaps the collider shape should
    /// get roughly the right behaviour, not a Nemesis that has gone deaf for no visible reason.
    /// </summary>
    private static float GetEmitterRadius(Collider col)
    {
        if (col is SphereCollider sphere)
        {
            Vector3 scale = sphere.transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return sphere.radius * maxScale;
        }

        return col.bounds.extents.magnitude;
    }

    /// <summary>
    /// How far the noise really is: along the NavMesh when it can be measured, in a straight line
    /// otherwise.
    ///
    /// The distinction is the entire reason a player one floor up used to be treated as though
    /// they were beside the Nemesis — five metres of slab is five metres in a straight line and
    /// twelve on foot, and every decision downstream of hearing was reading the first number.
    ///
    /// Throttled on NoiseUpdateCooldown, a field that has existed on SO_NemesisData since it was
    /// written with no reader anywhere in the project. This is what it was for. Cached per emitter,
    /// on the Time.time clock so a paused game freezes it — nothing it measures moves while paused.
    ///
    /// Falls back to the straight line when no path exists. Sound is not navigation — an
    /// unreachable player is still audible — and the states that act on a noise already have
    /// their own timeouts for a destination they cannot get to.
    /// </summary>
    private float MeasuredDistanceTo(Object emitter, Vector3 source)
    {
        float straightLine = Vector3.Distance(transform.position, source);
        if (!nemesisData.HearingUsesPathDistance) return straightLine;

        // One cached figure PER EMITTER. It used to be a single figure on the assumption that the
        // player is the only thing that makes noise — but the Director's synthetic pulses are
        // emitters too, and a solid prop on the listen layer used to be one as well. With two
        // sources in range, whichever was measured first lent its distance to the other for the
        // whole cooldown: a pulse across the level could make the player beside the monster read
        // as far away, or the reverse. That is detection "at random" (WIR-020).
        pathCache.TryGetValue(emitter, out PathCache entry);
        if (Time.time >= entry.NextQueryTime)
        {
            entry.NextQueryTime = Time.time + Mathf.Max(0.05f, nemesisData.NoiseUpdateCooldown);
            entry.Valid = NemesisNav.TryGetPathDistance(transform.position, source, out entry.Distance);
            pathCache[emitter] = entry;
        }

        return entry.Valid ? entry.Distance : straightLine;
    }

    private struct PathCache
    {
        public float NextQueryTime;
        public float Distance;
        public bool Valid;
    }

    // Keyed on Object and not Collider: a decoy is an emitter too, and it has no collider to be
    // keyed on.
    private readonly Dictionary<Object, PathCache> pathCache = new Dictionary<Object, PathCache>();
    private readonly List<Object> sweptEmitters = new List<Object>();
    private readonly List<Object> stalePathCaches = new List<Object>();

    /// <summary>Forgets emitters that were not in range this sweep, so a pulse that is gone does
    /// not keep an entry, and one that comes back is measured fresh.</summary>
    private void DropStalePathCaches()
    {
        if (pathCache.Count == 0) return;

        stalePathCaches.Clear();
        foreach (Object c in pathCache.Keys)
            if (c == null || !sweptEmitters.Contains(c)) stalePathCaches.Add(c);
        for (int i = 0; i < stalePathCaches.Count; i++) pathCache.Remove(stalePathCaches[i]);
    }

    /// <summary>Whether geometry on the given mask stands between this sensor and the point. An
    /// empty mask matches nothing, which is how a project with no Ground layer ends up with no
    /// floor attenuation rather than with an exception.</summary>
    private bool IsBlockedBy(Vector3 targetPosition, LayerMask mask)
    {
        if (mask == 0) return false;

        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f) return false;

        // A trigger volume is not a wall: ambience zones and light zones sit on Default, and with
        // Queries Hit Triggers on they used to muffle every sound that crossed their edge.
        return Physics.Raycast(transform.position, toTarget / distance, distance, mask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// True if a wall stands between this sensor and the given point.
    /// Public so the Nemesis audio (Tier 2.6) can attenuate its loops through the same raycast
    /// instead of reimplementing it.
    /// </summary>
    public bool IsOccludedByWall(Vector3 targetPosition) =>
        IsOccludedByWall(transform.position, targetPosition);

    /// <summary>
    /// Same test from an arbitrary origin. Used by the stuck detection to ask "can the player
    /// see this waypoint?", which reuses the obstacleMask already wired on this component
    /// instead of adding a second one to the state manager.
    /// </summary>
    public bool IsOccludedByWall(Vector3 origin, Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f) return false;

        // Triggers ignored: this answers four "is there a WALL in the way" questions (the grab, a
        // spawn in view, a stuck-escape warp in view, the monster's own audio muffling), and an
        // ambience or light-zone volume on Default is none of them — it used to stop the grab
        // across a zone boundary and let the monster "see" nothing through a doorway.
        return Physics.Raycast(origin, toTarget / distance, distance, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// The same test, blind to ONE hiding spot's own shell: the grab reaching the player inside
    /// the locker they are hiding in (plan §3.3). The locker still counts as a wall for anything
    /// else between the two points. Null behaves exactly like the overload above.
    /// </summary>
    public bool IsOccludedByWall(Vector3 origin, Vector3 targetPosition, HidingSpot through) =>
        through != null ? through.IsLineBlockedIgnoringSelf(origin, targetPosition, obstacleMask)
                        : IsOccludedByWall(origin, targetPosition);
}
