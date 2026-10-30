using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vision sensor of the Nemesis.
///
/// THREE ZONES, from most to least trusted (<see cref="VisionZones"/>): the FOCUS cone sees the
/// player at once; the PERIPHERY ("creo que vi algo por acá") fills the suspicion meter and becomes a
/// sighting when it is full; BEHIND it, close and with nothing in between ("siento que hay alguien
/// atrás"), fills the same meter far more slowly and never becomes a sighting on its own. And they add
/// up with what it already believes: a glimpse where it sensed the player moments ago IS a sighting
/// (SO_NemesisData.GlimpseCorroborationWindow). Hard proximity sits under all three.
///
/// HOW FAR is not one number either (<see cref="AdaptiveViewRange"/>, 04/10): ViewRange is the
/// distance it NOTICES a player at. One it is already seeing stays seen further out (hold), and one
/// it lost and is still after can be seen again from further the longer it goes without seeing them
/// (hunt). <see cref="EffectiveViewRange"/> is the range a sweep really used. What it makes out
/// through a hiding spot, the rear sense and the hard proximity stay on ViewRange.
///
/// The tuneable values (range, cone angle) live in <see cref="SO_NemesisData"/> so a designer
/// edits them in one asset and Tier 3.3 can scale them by handing this component a runtime copy
/// of the SO through <see cref="SetData"/>. The LayerMasks stay here: those are scene wiring,
/// not design values.
/// </summary>
public class FieldOfView : MonoBehaviour
{
    [Tooltip("Seconds between vision sweeps. Kept on the component: it is a performance knob, " +
             "not a design value.")]
    [SerializeField] private float viewDelay = 0.1f;

    [Tooltip("Inside this distance the cone is ignored, but the occlusion raycast still applies. " +
             "Models 'it is right next to me'. Not the same as the hard proximity detection in " +
             "SO_NemesisData, which ignores the raycast too.")]
    [SerializeField] private float minDistance = 1f;

    [SerializeField] private Transform viewTransform;
    [SerializeField] private LayerMask targetMask;
    [SerializeField] private LayerMask obstacleMask;

    /// <summary>What blocks its sight. Read by NemesisPossibilityGraphBuilder to tell the Hub's doorway
    /// from its walls with the same geometry the eyes use.</summary>
    public LayerMask ObstacleMask => obstacleMask;

    [Header("Data")]
    [Tooltip("Optional. If empty it is taken from the NemesisStateManager in the parents.")]
    [SerializeField] private SO_NemesisData nemesisData;

    private List<GameObject> visibleTargets;
    private GameObject lastKnownTarget;
    private float currentTimer = 0;
    private bool hasVisualTarget = false;
    private Vector3 lastKnownPosition;
    private bool hasLastKnownPosition;

    private Vector3 lastKnownVelocity;
    private float lastSightingTime;

    // -- Peripheral vision --------------------------------------------------
    //
    // What the last sweep found in the OUTER band of the cone: inside viewAngle but outside
    // focusAngle. Detection there is not instant; it accumulates. Held as state between sweeps
    // because the sweep runs on viewDelay (0.1 s) while the ramp below is integrated every frame,
    // which is what keeps the build-up frame-rate independent instead of "however many sweeps
    // happened to land".

    private bool peripheralContact;
    private GameObject peripheralTarget;
    private Vector3 peripheralPoint;

    /// <summary>How close the peripheral contact is, 1 at the eye and 0 at the edge of the vision
    /// range. Scales the build-up: something at arm's length in the corner of the eye registers
    /// far faster than the same thing at the far end of a corridor.</summary>
    private float peripheralCloseness;

    // -- Behind it (VisionZones.EZone.Rear) ------------------------------------
    //
    // "Siento que hay alguien atrás mío": the player outside the cone, within RearSenseRange, with
    // nothing in between. Not sight — there are no eyes back there — so it never becomes a sighting:
    // it adds to the suspicion meter at RearSenseStrength of the peripheral rate (TickAwareness).

    private bool rearContact;
    private Vector3 rearPoint;

    /// <summary>1 at the body, 0 at the edge of RearSenseRange (shortened when crouching).</summary>
    private float rearCloseness;

    /// <summary>The presence behind it is where it already believes the player is: it weighs as much
    /// as a glimpse (still never a sighting).</summary>
    private bool rearCorroborated;

    /// <summary>The Nemesis this sensor belongs to: where the belief comes from for corroboration.
    /// </summary>
    private NemesisStateManager owner;

    /// <summary>The last sweep's sighting came from the periphery, made a sighting by a fresh belief
    /// (VisionZones.Corroborates) rather than by the focus cone. For the debug HUD.</summary>
    public bool LastSightCorroborated { get; private set; }

    // -- A view range that adapts (AdaptiveViewRange) ------------------------------
    //
    // ViewRange used to be the one distance for everything: noticing a player it had never seen and
    // keeping hold of one it was staring at mid-chase. Stepped every frame in TickViewRange, before
    // the sweep that uses it. The rule and its state live in WIRED.Nemesis.Logic; what stays here is
    // the scene half: which state the Nemesis is in, and whether the last sweep had the player.

    private readonly AdaptiveViewRange rangeAdaptation = new AdaptiveViewRange();

    /// <summary>
    /// How far the eyes reach right now against a STANDING player out in the open, in metres:
    /// ViewRange (as lent by the Director and the escalation at this moment) times
    /// <see cref="ViewRangeScale"/>. What the vision sweep uses, before the crouch multiplier.
    ///
    /// Public for the three things that have to agree with the sweep: NemesisGizmos draws it next to
    /// the base cone, the debug HUD says why it is what it is, and NemesisHidingAwareness's "it saw
    /// you get in" rule asks whether a spot was inside the range it was really seeing with — a
    /// player it holds in sight at twelve metres was SEEN climbing into that locker.
    ///
    /// Not for the possibility map: its "not here" stays on SO_NemesisData.SearchMapClearRange, off
    /// the base range, on purpose (a map that clears too far wins by elimination).
    /// </summary>
    public float EffectiveViewRange =>
        nemesisData != null ? AdaptiveViewRange.Range(nemesisData.ViewRange, rangeAdaptation.Scale) : 0f;

    /// <summary>The multiplier on ViewRange for the current sweep: 1 at the base, up to the hold or
    /// the hunt scale. See <see cref="AdaptiveViewRange"/>.</summary>
    public float ViewRangeScale => rangeAdaptation.Scale;

    /// <summary>Why the range is what it is: base, holding a player it sees, hunting one it lost, or
    /// settling back. For the debug HUD and the gizmos.</summary>
    public AdaptiveViewRange.EReason ViewRangeReason => rangeAdaptation.Reason;

    // -- Why it is not seeing the player (SightMiss) -------------------------------
    //
    // Diagnostics for NemesisTraceRecorder and nothing else: no rung, state or other sense reads any
    // of it. Filled in from what the sweep already worked out, so it costs no ray of its own.

    private SightMiss.EReason lastMiss = SightMiss.EReason.NoTarget;
    private bool wasSeeing;

    /// <summary>Why it is not seeing the player right now, as of the last sweep. None while it is.
    /// </summary>
    public SightMiss.EReason MissReason => hasVisualTarget ? SightMiss.EReason.None : lastMiss;

    /// <summary>Why it LOST the player the last time it did: <see cref="MissReason"/> on the frame
    /// the sighting dropped. None until it loses one.</summary>
    public SightMiss.EReason LostSightReason { get; private set; }

    /// <summary>When that was, on the Time.time clock. Negative infinity until it loses one.</summary>
    public float LostSightTime { get; private set; } = float.NegativeInfinity;

    /// <summary>The range the last sweep really measured the player against, in metres:
    /// <see cref="EffectiveViewRange"/> with the crouch multiplier in. 0 when the sweep had nobody
    /// out in the open to measure (the player was hiding).</summary>
    public float SweepViewRange { get; private set; }

    private float awareness;

    /// <summary>Until when a soft noise of the player's keeps feeding the suspicion meter. See
    /// <see cref="NoteSoftNoise"/>.</summary>
    private float softNoiseUntil = float.NegativeInfinity;

    /// <summary>How long one soft-noise report counts: a little over a listening sweep (0.1 s), so
    /// a steady stream of steps reads as continuous and a single one does not linger.</summary>
    private const float SoftNoiseContactWindow = 0.15f;

    /// <summary>
    /// The player made a soft noise the ears caught (FieldOfListening.HeardSoftPlayerNoise): it feeds
    /// the suspicion meter like a glimpse does (plan §17.3, shared suspicion). Called by
    /// NemesisStateManager after sampling the sensors.
    /// </summary>
    public void NoteSoftNoise() => softNoiseUntil = Time.time + SoftNoiseContactWindow;

    /// <summary>The hiding spot the current peripheral contact is being made THROUGH, or null when
    /// the player is out in the open. Decides what a full meter means — see TickAwareness.</summary>
    private HidingSpot peripheralSpot;

    /// <summary>The Nemesis's body: where its feet are. The hard-detection disc is measured from
    /// here, not from the eye — see IsStandingOnMe.</summary>
    private Transform body;

    /// <summary>Height above the player's pivot (their feet) that the proximity test aims its
    /// occlusion ray at: the body, not the floor it stands on. Same height the grab's own ray uses
    /// (NemesisStateManager.CatchProbeHeight); a ray ending at the feet ends ON the floor and can
    /// report the floor itself as the obstruction.</summary>
    private const float BodyProbeHeight = 1f;

    private Vector3 lookDirection;

    /// <summary>
    /// The Nemesis has worked out that the player is inside <c>spot</c> without having SEEN them:
    /// the suspicion meter filled while looking through a locker's slats or under a table (plan
    /// §3.4, level B), or — <c>atArmsLength</c> — it is standing right next to the spot and the
    /// proximity rule reached them through its shell (§3.3). Either way no chase starts: the spot
    /// becomes known (NemesisHidingAwareness), the Nemesis walks to its door and pulls them out.
    ///
    /// Raised EVERY FRAME the detection lasts, not once: the listener dedupes, and a certainty the
    /// eyes keep renewing must not expire on the listener's clock while the eyes still have it. An
    /// instance event and not a static one: these are this Nemesis's eyes, and a second Nemesis in
    /// the scene must not learn from them.
    /// </summary>
    public event System.Action<HidingSpot, bool> HiddenPlayerSpotted;

    /// <summary>Whether the last sweep caught the player in the corner of its eye. Live, unlike
    /// <see cref="Awareness"/>, which takes seconds to drain after a contact is gone — the
    /// difference between "I glimpsed you climbing in" and "I was chasing you a moment ago".</summary>
    public bool HasPeripheralContact => peripheralContact;

    /// <summary>Where the corner of its eye last caught something. A glimpse is evidence too (plan
    /// §17): the belief keeps where it was, so a suspicion has somewhere to be walked to.</summary>
    public Vector3 PeripheralPoint => peripheralPoint;

    /// <summary>Whether the last sweep felt the player BEHIND it (outside the cone, within
    /// RearSenseRange, nothing in between). Live, like <see cref="HasPeripheralContact"/>.</summary>
    public bool HasRearContact => rearContact;

    /// <summary>Where it felt someone behind it. The belief keeps it as a glimpse, so "siento que hay
    /// alguien atrás" has somewhere to turn round and look at.</summary>
    public Vector3 RearPoint => rearPoint;

    /// <summary>Which zone is feeding the suspicion meter right now: Peripheral, Rear, or None
    /// (nothing, a soft noise only, or a sighting). For the debug HUD.</summary>
    public VisionZones.EZone ContactZone =>
        peripheralContact ? VisionZones.EZone.Peripheral
        : rearContact ? VisionZones.EZone.Rear
        : VisionZones.EZone.None;

    public bool HasVisualTarget { get => hasVisualTarget; }
    public Vector3 LastKnownPosition { get => lastKnownPosition; }

    /// <summary>The hiding spot the meter is filling through right now (level B), or null. For the
    /// debug HUD: "suspicion rising" reads very differently when the player is in a locker.</summary>
    public HidingSpot SensedThroughSpot => peripheralContact ? peripheralSpot : null;

    /// <summary>
    /// How close the Nemesis is to noticing something in the corner of its eye: 0 nothing, 1
    /// detected.
    ///
    /// This is the whole of the change from binary vision. Before it, the cone was all-or-nothing:
    /// a player at the extreme edge of a 120 degree cone, seven metres away, tripped exactly the
    /// same instant detection as one standing dead ahead at two metres - and since "sees the
    /// player" is an INTERRUPT rung on the priority ladder, peeking round a corner started a full
    /// chase in the same frame, with no beat in between for the player to react to.
    ///
    /// Reaching 1 promotes the contact to a real sighting and everything downstream behaves as it
    /// always did. Below 1 it is readable by the decision layer as suspicion, which is what sends
    /// the Nemesis to walk over and look rather than to sprint.
    /// </summary>
    public float Awareness { get => awareness; }

    /// <summary>Whether the Nemesis is onto something without having actually seen it yet. False
    /// once it HAS seen them - past that point this is no longer a suspicion, and a rung reading
    /// both would fire twice for one event.</summary>
    public bool IsSuspicious
    {
        get
        {
            if (hasVisualTarget || nemesisData == null) return false;

            return awareness >= nemesisData.AwarenessTriggerThreshold;
        }
    }

    /// <summary>
    /// Where the eye is actually pointed. Defaults to the view transform's forward and can be
    /// driven elsewhere - see <see cref="NemesisLookAround"/>.
    ///
    /// It has to be separate from the body's forward because the body's forward is not the
    /// Nemesis's to spend: the NavMeshAgent rotates it towards whatever it is walking at. With the
    /// cone welded to that, a Nemesis standing still at a patrol waypoint stares down the corridor
    /// it arrived from for the entire wait and cannot look anywhere else, however long it stands
    /// there.
    /// </summary>
    public Vector3 LookDirection
    {
        get => lookDirection.sqrMagnitude > 0.0001f ? lookDirection : ViewTransform.forward;
        set => lookDirection = value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.zero;
    }

    /// <summary>Hands the eye back to the body. Called when whatever was steering the look
    /// direction stops.</summary>
    public void ResetLookDirection() => lookDirection = Vector3.zero;

    /// <summary>
    /// Where the cone is cast from — eye height, not the pivot.
    ///
    /// Public so <see cref="NemesisGizmos"/> can draw the cone from the same origin the sweep
    /// actually uses. Falls back to this component's transform outside Play mode, because the
    /// Awake fallback that normally fills it in has not run yet in the Scene view.
    /// </summary>
    public Transform ViewTransform => viewTransform != null ? viewTransform : transform;

    /// <summary>
    /// How fast and in what direction the target appeared to be moving when it was last seen, in
    /// units per second.
    ///
    /// Derived from consecutive sightings rather than read off the player's own movement code,
    /// which the Nemesis could trivially reach. That is the difference between predicting and
    /// cheating: this only ever knows what the sensor actually observed, so a player who breaks
    /// line of sight and immediately changes direction gets away with it — which is the whole
    /// point of breaking line of sight.
    ///
    /// Zero until two sightings have landed close enough together to measure between.
    /// </summary>
    public Vector3 LastKnownVelocity { get => lastKnownVelocity; }

    /// <summary>
    /// Whether <see cref="LastKnownPosition"/> means anything yet. Starts false and never goes
    /// back to false: it is a memory, not a state.
    ///
    /// It is needed because lastKnownPosition starts at Vector3.zero, which is a perfectly valid
    /// level coordinate. Without this flag, anything reading the last known position before the
    /// first detection believes the player is at the world origin — which is how the patrol bias
    /// would end up sending the Nemesis to the same corner every time.
    /// </summary>
    public bool HasLastKnownPosition { get => hasLastKnownPosition; }

    /// <summary>
    /// Seconds since the target was last seen, or infinity if it never has been.
    ///
    /// <see cref="HasLastKnownPosition"/> says the memory exists; this says how much it is still
    /// worth. They are different questions and only the first one was answerable before — which
    /// is why a sighting from ten minutes ago steered the patrol exactly as hard as one from two
    /// seconds ago.
    /// </summary>
    public float TimeSinceLastSighting =>
        hasLastKnownPosition ? Time.time - lastSightingTime : float.PositiveInfinity;

    /// <summary>When the target was last seen, on the Time.time clock. What NemesisBelief compares
    /// to fold each sighting in exactly once. Meaningless while HasLastKnownPosition is false.
    /// </summary>
    public float LastSightingTime => lastSightingTime;

    private void Awake()
    {
        visibleTargets = new List<GameObject>();

        // Every sweep below reads viewTransform, so an unassigned one is a NullReferenceException
        // per sweep. This component's own transform is a reasonable stand-in — hence a fallback
        // and not a hard failure — but it is worth a warning: the marker is normally placed at eye
        // height, and dropping to the object's pivot silently narrows what the cone can see.
        if (viewTransform == null)
        {
            viewTransform = transform;
            Debug.LogWarning($"[{nameof(FieldOfView)}] No {nameof(viewTransform)} assigned — " +
                             $"falling back to this object's own transform. Vision will be cast " +
                             $"from the pivot instead of from eye height.", this);
        }

        NemesisStateManager manager = GetComponentInParent<NemesisStateManager>();
        body = manager != null ? manager.transform : transform;
        owner = manager;

        if (nemesisData != null) return;

        if (manager != null) nemesisData = manager.NemesisData;

        if (nemesisData == null)
            Debug.LogError($"[{nameof(FieldOfView)}] No SO_NemesisData assigned and none found " +
                           $"in the parents — the Nemesis will not see anything.", this);
    }

    /// <summary>
    /// Drops the memory of where the target was, as though it had never been seen.
    ///
    /// The one legitimate caller is the end of a capture. Everywhere else "a belief is a memory,
    /// not a state" holds and this must not be used — but a checkpoint respawn physically moves
    /// the player somewhere else, so the remembered position stops being a stale fact and becomes
    /// an actively false one. Keeping it sends the Nemesis straight back to where it just caught
    /// you, which is the one place the player provably is not.
    /// </summary>
    public void ForgetLastKnownPosition()
    {
        // For the trace: this sighting was not lost, it was thrown away. Stamped here and not left
        // to TrackSightEdge, where the next sweep's own reason would overwrite it.
        if (hasVisualTarget || wasSeeing)
        {
            LostSightReason = SightMiss.EReason.Forgotten;
            LostSightTime = Time.time;
        }

        lastMiss = SightMiss.EReason.Forgotten;
        wasSeeing = false;

        hasVisualTarget = false;
        hasLastKnownPosition = false;
        lastKnownVelocity = Vector3.zero;

        // Cleared too, or GetCurrentTarget keeps handing back the player it was holding and the
        // capture check can fire again on a target the Nemesis is no longer entitled to know about.
        lastKnownTarget = null;

        // Same reasoning one level down: a suspicion meter left full is a memory of the player
        // too, and the whole point of this call is that the respawn has made every such memory
        // actively false. Left standing it would re-promote to a sighting on the next frame the
        // Nemesis happened to have anything in its periphery.
        awareness = 0f;
        peripheralContact = false;
        peripheralTarget = null;
        peripheralSpot = null;
        rearContact = false;

        // And one level further: the longer reach of a hold or a hunt was earned by having seen the
        // player, and that sighting is exactly what is being thrown away. It would settle back on its
        // own within a couple of seconds; after a respawn it starts from the base, like the rest.
        rangeAdaptation.Reset();
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

    private void Update()
    {
        // Same guard as NemesisStateManager: this Update is its own, so without it the
        // Nemesis kept seeing (and reacting) with the game paused.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        // The range the sweep below will use, off what the LAST one left: "is it seeing them" has to
        // be the answer the range was earned with, not the one this frame is about to produce. Every
        // frame and not once per sweep, for the reason TickAwareness gives: growing over seconds is a
        // rate, and a rate integrated on the sweep's cadence depends on how the timer lines up.
        TickViewRange(Time.deltaTime);

        // Extreme proximity is checked every frame and before everything else, deliberately:
        // it does not wait for the viewDelay cadence and it is the only thing that defeats
        // Hidden. Skipping the normal sweep when it hits also stops FindVisibleTargets from
        // immediately clearing the flag it just set.
        if (CheckExtremeProximity())
        {
            TrackSightEdge();
            return;
        }

        if (currentTimer < viewDelay) currentTimer += Time.deltaTime;
        else
        {
            currentTimer = 0;
            FindVisibleTargets();
        }

        // Every frame, not once per sweep. The sweep runs on viewDelay and only decides WHAT is
        // in the periphery; how fast that turns into a detection is a rate, and integrating a rate
        // on a 0.1 s cadence would make the whole feature depend on how the timer happened to line
        // up with the frames.
        TickAwareness(Time.deltaTime);

        TrackSightEdge();
    }

    /// <summary>
    /// Notes the frame a sighting drops, and why (<see cref="LostSightReason"/>). Last thing in
    /// Update, after everything that can raise or drop the flag this frame. Diagnostics only.
    /// </summary>
    private void TrackSightEdge()
    {
        if (wasSeeing && !hasVisualTarget)
        {
            LostSightReason = lastMiss;
            LostSightTime = Time.time;
        }

        wasSeeing = hasVisualTarget;
    }

    /// <summary>
    /// Steps how far the eyes reach (<see cref="AdaptiveViewRange"/>): held further out while it sees
    /// the player, growing while it hunts one it lost, the base range the rest of the time.
    ///
    /// WHAT COUNTS AS HUNTING is read off the state, and a state is not enough on its own: the
    /// rule also wants a sighting behind the hunt, which it keeps track of itself. So Searching that
    /// grew out of a footstep (plan D26) and a lift ride towards a noise are hunting STATES with
    /// nothing earned, and they run on the base range. Investigating is deliberately not on the list
    /// at all: walking to a noise or a glimpse is "something is there", not "I am after him", and
    /// sneaking past a Nemesis that only heard you has to stay as hard as it was. Traversing is: the
    /// lift is how a chase follows someone it saw go up a floor.
    ///
    /// AT REST — back on patrol, or dormant — is when what a sighting earned is dropped. Not on any
    /// state in between: a decoy that takes its attention for a moment in the middle of a search does
    /// not make it forget it was hunting someone it had seen.
    /// </summary>
    private void TickViewRange(float deltaTime)
    {
        if (nemesisData == null) return;

        // NOT DURING THE ESCAPE, and reset rather than left to settle. NemesisEscapePursuit raises the
        // chase floor and feeds this sensor the player's position by hand (InjectSighting) while it
        // paces the Nemesis against their sprint. Seeing them from twice as far would switch the
        // pursuit from running at that position to leading it, in a sequence that was tuned without
        // any of this. Same call NemesisChaseProgress makes, and for the same reason: the escape
        // chase stays byte for byte what it was.
        NemesisDecision decision = owner != null ? owner.Decision : null;
        if (decision != null && decision.ChaseFloor)
        {
            rangeAdaptation.Reset();
            return;
        }

        NemesisStateManager.ENemesisState? state = owner != null ? owner.CurrentStateKey : null;

        bool hunting = state == NemesisStateManager.ENemesisState.Chasing ||
                       state == NemesisStateManager.ENemesisState.Searching ||
                       state == NemesisStateManager.ENemesisState.Traversing;

        bool atRest = !state.HasValue || state == NemesisStateManager.ENemesisState.Patrolling;

        rangeAdaptation.Step(deltaTime, hasVisualTarget, hunting, atRest,
                             nemesisData.ViewHoldScale, nemesisData.ViewHuntScale,
                             nemesisData.ViewHuntGrowTime, nemesisData.ViewHuntSettleTime);
    }

    /// <summary>
    /// Moves the suspicion meter, and promotes it to a real sighting when it fills.
    ///
    /// The build-up is scaled by closeness so the ramp means something across the whole range:
    /// with a flat rate, a player at the far edge of the cone and one two metres away are noticed
    /// after the same number of seconds, which is the binary sensor again wearing a timer.
    ///
    /// The decay is deliberately slower than the build-up at its default tuning, and that is what
    /// makes leaning out twice in a row worse than leaning out once: the second peek starts from
    /// wherever the first one left off.
    /// </summary>
    private void TickAwareness(float deltaTime)
    {
        if (nemesisData == null) return;

        // Already seen: the meter is full by definition and nothing needs integrating. Leaving it
        // full also means that losing sight decays from the top rather than snapping to zero.
        if (hasVisualTarget)
        {
            awareness = 1f;
            return;
        }

        bool noiseContact = Time.time < softNoiseUntil;
        float buildTime = nemesisData.AwarenessBuildTime;

        // Closeness scales the RATE, floored so a contact at the very edge of the range still
        // eventually registers instead of stalling at a value it can never climb past.
        float eyeRate = peripheralContact ? VisionZones.BuildRate(peripheralCloseness, buildTime) : 0f;

        // What is not the eyes adds to the same meter, and never makes it a sighting
        // (VisionZones.StepMeter): a soft noise of the player's (plan §17.3, shared suspicion — a soft
        // step and a glimpse together cross the threshold sooner than either alone, case 26), and a
        // presence felt BEHIND it, at a fraction of the peripheral rate. Either can still pass the
        // suspicion threshold: "vio algo de reojo" turns round and walks over.
        float senseRate = 0f;
        if (noiseContact) senseRate += nemesisData.SoftNoiseSuspicionRate / Mathf.Max(0.05f, buildTime);
        if (rearContact)
        {
            float strength = rearCorroborated ? 1f : nemesisData.RearSenseStrength;
            senseRate += VisionZones.BuildRate(rearCloseness, buildTime) * strength;
        }

        awareness = VisionZones.StepMeter(awareness, deltaTime, peripheralContact, eyeRate,
                                          noiseContact || rearContact, senseRate,
                                          nemesisData.AwarenessDecayRate, nemesisData.NoiseOnlySuspicionCap);

        if (!peripheralContact || awareness < 1f) return;

        // Filled while the player is HIDING: it has worked out where they are, it has not seen them
        // (plan §3.4, level B). A shape behind slats does not start a chase — the spot becomes
        // known and the search walks up to it and opens it. The belief and the target are still
        // refreshed, every frame the contact lasts: it does know where the player is now, and the
        // grab needs a target to reach for once it is standing at the door.
        if (peripheralSpot != null)
        {
            lastKnownTarget = peripheralTarget;
            RecordSighting(peripheralPoint);
            HiddenPlayerSpotted?.Invoke(peripheralSpot, false);
            return;
        }

        // Filled: this stops being a suspicion and becomes a sighting, on exactly the same terms
        // as one caught by the focus cone. RecordSighting is what keeps LastKnownVelocity honest,
        // so it has to run here too and not only on the instant path.
        hasVisualTarget = true;
        lastKnownTarget = peripheralTarget;
        RecordSighting(peripheralPoint);
    }

    /// <summary>
    /// Hard detection: inside <c>proximityDetectionRange</c> the Nemesis notices the player no
    /// matter what — no cone, no hiding. Out in the open, forcing <see cref="HasVisualTarget"/> is
    /// enough to route the FSM into Chasing, since every state already transitions on that flag.
    /// Inside a hiding spot it reports the spot instead (<see cref="HiddenPlayerSpotted"/>): the
    /// Nemesis knows where they are and goes to open the door — see the end of the method.
    ///
    /// With <c>proximityDetectionRespectsWalls</c> on, the only thing it still requires is that
    /// there be no geometry in between. Without that check the radius punches through the thin
    /// blockout walls: standing on the other side of a partition is enough to be detected, chased
    /// and grabbed — which is the reported "it can grab you through walls". The cone and Hidden
    /// are still defeated, which is what this detection exists for.
    ///
    /// The wall test looks THROUGH the shell of the spot the player is hiding in, and only that
    /// one (plan §3.3). A locker on Default is otherwise a wall like any other, and this ray
    /// stopping at its door made hiding total immunity: the one thing the spec says breaks Hidden
    /// could never reach anyone inside anything.
    /// </summary>
    /// <returns>true if the player was detected by proximity this frame.</returns>
    private bool CheckExtremeProximity()
    {
        if (nemesisData == null) return false;

        float range = nemesisData.ProximityDetectionRange;
        if (range <= 0f) return false;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null) return false;

        Vector3 playerPosition = player.transform.position;
        if (!IsStandingOnMe(playerPosition, range)) return false;

        HidingSpot spot = player.CurrentHidingSpot;

        // HOLDING YOUR BREATH INSIDE A SPOT TAKES YOU OUT OF ARM'S REACH (plan §17.6, D21). The
        // classic beat: it walks up to where it heard something, looks left and right, and leaves
        // because it did not see anyone. Breathing, a player at the door is found here exactly as
        // before; holding, only OPENING the spot finds them, and the Nemesis only opens a spot it
        // already suspects or knows (NemesisHidingAwareness.Open). What keeps this from being
        // immunity is SO_HidingData.MaxHoldSeconds and the exhale at the end of it, which is loud
        // enough to bring the Nemesis straight back.
        if (spot != null && player.IsHoldingBreath) return false;

        if (nemesisData.ProximityDetectionRespectsWalls &&
            IsOccluded(playerPosition + Vector3.up * BodyProbeHeight, spot)) return false;

        lastKnownTarget = player.gameObject;
        RecordSighting(playerPosition);

        // Straight to full. Hard proximity is the one detection that answers no questions about
        // cones or suspicion - it is "you are standing on me" - so ramping it would be absurd, and
        // leaving the meter low here would let it decay while the player is still in contact.
        awareness = 1f;
        peripheralContact = false;
        rearContact = false;

        // Inside a spot: standing next to it is KNOWING it, not seeing them. As a sighting it won
        // "lo está viendo", and Chasing ran at a point inside the prop — which the agent can only
        // approach to the edge of the NavMesh, stopping short of the grab and staring at the door
        // for as long as the player stayed in. Known, the search walks to the door and Catch pulls
        // them out. The F10 console's spot-less Hide has no door to walk to: that one is a sighting.
        if (spot != null)
        {
            hasVisualTarget = false;
            lastMiss = SightMiss.EReason.Hidden;
            HiddenPlayerSpotted?.Invoke(spot, true);
            return true;
        }

        hasVisualTarget = true;
        return true;
    }

    /// <summary>
    /// Stores where the target was seen and how fast it seemed to be going.
    ///
    /// Both sighting paths funnel through here — the proximity check above and the cone sweep
    /// below — so the velocity estimate cannot go stale just because the detection that frame
    /// came from the other one.
    ///
    /// The gap between sightings is capped before it is divided by: a target re-acquired after
    /// twenty seconds on the far side of the level is not a target moving slowly, and dividing a
    /// hundred metres by twenty seconds to get "5 m/s in that direction" would be a fabricated
    /// reading, not a measured one. Past the cap the estimate is dropped instead.
    /// </summary>
    private void RecordSighting(Vector3 position)
    {
        const float MaxGapForVelocity = 0.5f;

        float gap = Time.time - lastSightingTime;

        if (hasLastKnownPosition && gap > 0.0001f && gap <= MaxGapForVelocity)
            lastKnownVelocity = (position - lastKnownPosition) / gap;
        else
            lastKnownVelocity = Vector3.zero;

        lastKnownPosition = position;
        hasLastKnownPosition = true;
        lastSightingTime = Time.time;
    }

    /// <summary>
    /// Tells the Nemesis where a target is, as though this sensor had just seen it: refreshes the
    /// last known position, velocity and target, but does NOT set <see cref="HasVisualTarget"/> —
    /// nothing was actually seen, so the suspicion and focus logic are untouched.
    ///
    /// For <see cref="NemesisEscapePursuit"/>, which keeps the belief fresh for the length of the
    /// escape. Nothing else should call it: a belief that the sensor did not earn is exactly what
    /// the rest of this class is careful never to invent.
    /// </summary>
    public void InjectSighting(GameObject target)
    {
        if (target == null) return;

        lastKnownTarget = target;
        RecordSighting(target.transform.position);
    }

    /// <summary>
    /// Whether a point is inside the hard-detection disc: measured FLAT from the body, and only on
    /// the Nemesis's own floor.
    ///
    /// It used to be a sphere round the EYE measured to the player's FEET, and the eye is the head
    /// bone, about 1.8 m up. With the shipped 1.5 m range that sphere never reached the floor the
    /// Nemesis was standing on: extreme proximity could not fire on level ground at any distance,
    /// which left the one rule the spec says breaks Hidden (§5.2) as dead code. NemesisGizmos and
    /// the SO editor have always drawn it as a flat disc; this is now what they draw.
    ///
    /// "Its own floor" is the grab's test (CatchMaxVerticalOffset). Standing on the monster and
    /// being within its reach have to agree on what the same floor is — a player on the catwalk
    /// overhead is neither.
    /// </summary>
    private bool IsStandingOnMe(Vector3 point, float range)
    {
        Vector3 offset = point - body.position;
        if (Mathf.Abs(offset.y) > nemesisData.CatchMaxVerticalOffset) return false;

        offset.y = 0f;
        return offset.sqrMagnitude <= range * range;
    }

    /// <summary>
    /// Whether there is <see cref="obstacleMask"/> geometry between the eye and the point, not
    /// counting the shell of <paramref name="through"/> — the spot the player is hiding in, or
    /// null out in the open.
    ///
    /// Tested against the player's centre and not the three points FindVisibleTargets sweeps: here
    /// the distance is a couple of metres and the question being answered is "is there a wall in
    /// between", not "is a shoulder peeking out".
    /// </summary>
    /// <summary>Whether the eye has a clear line to <paramref name="point"/>, looking through the
    /// shell of <paramref name="through"/> only. For NemesisHidingAwareness's "did it see the spot
    /// being climbed into", which has to use the same obstacles this sensor sees with.</summary>
    public bool HasLineOfSightTo(Vector3 point, HidingSpot through) => !IsOccluded(point, through);

    private bool IsOccluded(Vector3 targetPosition, HidingSpot through) =>
        through != null
            ? through.IsLineBlockedIgnoringSelf(viewTransform.position, targetPosition, obstacleMask)
            : !LineOfSight.CheckView(viewTransform.position, targetPosition, obstacleMask);

    public void FindVisibleTargets()
    {
        if (nemesisData == null) return;

        PlayerStateManager player = PlayerRegistry.Current;

        // Re-sensed every sweep, like the periphery: a presence it no longer feels lets the meter drain.
        rearContact = false;
        rearCorroborated = false;
        LastSightCorroborated = false;

        // Hidden means inside a locker, under a table or in a container: NORMAL vision cannot reach
        // the player at all. Extreme proximity was already checked in Update before this ran — so
        // getting here with IsHidden means it did not trigger — and what is left is what leaks
        // through the spot itself, into the suspicion meter only (SenseThroughSpot).
        if (player != null && player.IsHidden)
        {
            hasVisualTarget = false;
            lastMiss = SightMiss.EReason.Hidden;
            SweepViewRange = 0f;

            // Cleared before the spot gets its say, or the meter keeps climbing off the last sweep
            // that saw them in the open — which would have the Nemesis work out that someone is in
            // the locker purely by having been looking that way when they got in. Whether it SAW
            // them get in is a separate rule with its own window (NemesisHidingAwareness).
            peripheralContact = false;
            peripheralSpot = null;

            SenseThroughSpot(player);
            return;
        }

        peripheralSpot = null;

        // The range as it stands right now (TickViewRange stepped it this frame, before this sweep):
        // ViewRange for a Nemesis that has not seen anyone, further for one that is holding the
        // player in sight or hunting one it lost. Everything below — the overlap sphere, both zones,
        // how close a glimpse counts as — measures against this one number, as it did against
        // ViewRange.
        float viewRange = EffectiveViewRange;
        float viewAngle = nemesisData.ViewAngle;

        // Crouching shortens the range it can be spotted at rather than breaking line of sight:
        // a lower silhouette is harder to pick out, not invisible. It halves whatever the range is,
        // held and hunting included: a crouch is worth the same fraction against an alert Nemesis.
        if (player != null && player.IsCrouch) viewRange *= nemesisData.CrouchVisionMultiplier;

        SweepViewRange = viewRange;

        Vector3 eye = viewTransform.position;
        Vector3 front = LookDirection;
        float focusAngle = nemesisData.FocusAngle;

        visibleTargets.Clear();
        peripheralContact = false;

        GameObject focusHit = null;
        GameObject peripheralHit = null;
        float peripheralDistance = float.PositiveInfinity;

        // For SightMiss only: whether anything that failed the test below was at least inside the
        // cone, which is what tells "behind a wall" from "not looking that way".
        bool anyInsideCone = false;

        Collider[] targetsInViewRadius = Physics.OverlapSphere(eye, viewRange, targetMask);
        for (int i = 0; i < targetsInViewRadius.Length; i++)
        {
            Collider candidate = targetsInViewRadius[i];

            // Both cones, sampled at feet/centre/head. One call where this used to be a hand-rolled
            // double loop; see LineOfSight.CheckConeSampled for why the three samples and the
            // together-per-sample angle+occlusion test both matter.
            //
            // The focus cone is asked of EVERY sample, inside that call. It used to be asked here,
            // of the one sample that got through first — the feet, which from an eye two metres up
            // are outside the focus cone for anyone closer than 2.3 m, however squarely their head
            // is in the middle of it. minDistance still overrides the angle: something touching the
            // Nemesis is not "in the corner of its eye" no matter which way it happens to be facing.
            if (!LineOfSight.CheckConeSampled(eye, front, candidate, viewAngle, focusAngle, minDistance,
                                              obstacleMask, out Vector3 seenPoint, out bool inFocus,
                                              out bool insideCone))
            {
                anyInsideCone |= insideCone;
                continue;
            }

            GameObject target = candidate.gameObject;
            float distance = Vector3.Distance(eye, seenPoint);

            // Inside the focus cone this is a sighting, exactly as it always was.
            if (inFocus)
            {
                if (!visibleTargets.Contains(target))
                {
                    visibleTargets.Add(target);
                    focusHit = target;
                }
                continue;
            }

            // Outer band. Not a detection yet - it feeds the suspicion ramp in TickAwareness, and
            // only becomes one if the exposure lasts. Nearest wins, so a second target further out
            // cannot slow down the ramp for the one actually closing in.
            if (distance >= peripheralDistance) continue;

            peripheralDistance = distance;
            peripheralHit = target;
            peripheralPoint = target.transform.position;
        }

        // THE SENSES ADD UP. Out of the corner of its eye, right where it heard or saw the player a
        // moment ago: that is not "something", it is the player (VisionZones.Corroborates). It used to
        // start the meter from nothing, so a search that heard you, then glimpsed you, went Searching
        // -> "vio algo de reojo" (Investigating) -> Chasing instead of straight at you.
        if (visibleTargets.Count == 0 && peripheralHit != null && IsCorroborated(peripheralPoint))
        {
            visibleTargets.Add(peripheralHit);
            focusHit = peripheralHit;
            LastSightCorroborated = true;
        }

        if (visibleTargets.Count > 0)
        {
            hasVisualTarget = true;
            lastKnownTarget = focusHit != null ? focusHit : visibleTargets[0];
            RecordSighting(visibleTargets[0].transform.position);
            return;
        }

        hasVisualTarget = false;

        bool anyInRange = targetsInViewRadius.Length > 0;
        lastMiss = player == null && !anyInRange
            ? SightMiss.EReason.NoTarget
            : SightMiss.Classify(anyInRange, anyInsideCone, peripheralHit != null);

        if (peripheralHit == null)
        {
            // Nothing for the eyes: is there someone behind it?
            SenseBehind(player, eye, front);
            return;
        }

        peripheralContact = true;
        peripheralTarget = peripheralHit;
        peripheralCloseness = VisionZones.Closeness(peripheralDistance, viewRange);
    }

    /// <summary>
    /// "Siento que hay alguien atrás mío" (VisionZones.EZone.Rear): the player outside the vision
    /// cone, within RearSenseRange measured flat from the body (shortened when crouching, the same
    /// way the view is), on its own floor, with nothing in between. It feeds the suspicion meter at a
    /// fraction of the peripheral rate and never becomes a sighting — what it can do is make the
    /// Nemesis turn round ("vio algo de reojo" → Investigating, which turns to face it first), and
    /// then its eyes decide. Corroborated by a fresh belief, it weighs as much as a glimpse.
    ///
    /// Inside ProximityDetectionRange it never gets here: the hard detection has already fired.
    /// </summary>
    private void SenseBehind(PlayerStateManager player, Vector3 eye, Vector3 front)
    {
        float range = nemesisData.RearSenseRange;
        if (player == null || range <= 0f || nemesisData.RearSenseStrength <= 0f) return;
        if (player.IsCrouch) range *= nemesisData.CrouchVisionMultiplier;

        Vector3 feet = player.transform.position;
        Vector3 flat = feet - body.position;
        if (Mathf.Abs(flat.y) > nemesisData.CatchMaxVerticalOffset) return;

        flat.y = 0f;
        float distance = flat.magnitude;

        Vector3 chest = feet + Vector3.up * BodyProbeHeight;
        float angle = Vector3.Angle(front, chest - eye);
        VisionZones.EZone zone = VisionZones.Classify(angle, distance, nemesisData.ViewAngle,
                                                      nemesisData.FocusAngle, nemesisData.ViewRange, range);
        if (zone != VisionZones.EZone.Rear) return;
        if (IsOccluded(chest, null)) return;

        rearContact = true;
        rearPoint = feet;
        rearCloseness = VisionZones.Closeness(distance, range);
        rearCorroborated = IsCorroborated(feet);
    }

    /// <summary>Whether a point is where it already believes the player is, recently enough to
    /// count (SO_NemesisData.GlimpseCorroborationWindow). See VisionZones.Corroborates.</summary>
    private bool IsCorroborated(Vector3 point)
    {
        NemesisBelief belief = owner != null ? owner.Belief : null;
        if (belief == null || !belief.HasBelief) return false;

        return VisionZones.Corroborates(point, belief.Position, belief.Radius, belief.Age,
                                        nemesisData.GlimpseCorroborationWindow,
                                        nemesisData.FloorHeightThreshold);
    }

    /// <summary>
    /// What the Nemesis can still make out of a player who is hiding (plan §3.4, level B). It only
    /// ever sets up a peripheral contact.
    ///
    /// HOW FAR depends on the spot: through a locker's slats, a fraction of the view range and only
    /// from in front of the door; under a table, a shortened view from any side; inside a container
    /// — and under the F10 console's spot-less Hide — nothing. <see cref="HiddenViewRange"/> has the
    /// numbers.
    ///
    /// ALWAYS THROUGH THE METER, NEVER A SIGHTING, even dead ahead in the focus cone. Making out a
    /// shape behind slats is exactly the "something is there" the meter models, and an instant
    /// sighting through a locker door is the binary sensor this replaced. What a FULL meter means
    /// here is different too: see TickAwareness.
    ///
    /// The spot's own colliders are looked through — the reduced range IS the slats and the edge of
    /// the table. Anything else in between, a wall or another prop, still blocks exactly as it does
    /// for a player in the open.
    /// </summary>
    private void SenseThroughSpot(PlayerStateManager player)
    {
        HidingSpot spot = player.CurrentHidingSpot;

        // Holding your breath: still and silent behind the slats, it makes nothing out (plan §17.6,
        // D21). A spot it already suspects does not need the slats — it walks up and opens it.
        if (player.IsHoldingBreath) return;

        float range = HiddenViewRange(spot);
        if (range <= 0f) return;

        Vector3 eye = viewTransform.position;

        // Slats are in the DOOR. The back and the sides of a locker are sheet steel.
        if (spot.Type == EHidingSpotType.Locker && !IsInFrontOf(spot, eye)) return;

        Collider bodyCollider = player.CapsuleColl;
        if (bodyCollider == null) return;

        if (!CheckConeThroughSpot(eye, LookDirection, bodyCollider, nemesisData.ViewAngle, range,
                                  spot, out Vector3 seenPoint))
            return;

        peripheralContact = true;
        peripheralTarget = player.gameObject;
        peripheralPoint = player.transform.position;
        peripheralSpot = spot;
        peripheralCloseness = 1f - Mathf.Clamp01(Vector3.Distance(eye, seenPoint) / range);
    }

    /// <summary>
    /// How far the Nemesis can make out a player hidden in <paramref name="spot"/>, in metres, or 0
    /// when it cannot at all. Public so the gizmos can draw it round a spot.
    ///
    /// The locker's fraction lives on SO_HidingData next to the rest of the spot's numbers; the
    /// table's on SO_NemesisData, where the hiding spec put it and where the SO editor and the
    /// gizmos draw it next to the crouched range it resembles.
    /// </summary>
    public float HiddenViewRange(HidingSpot spot)
    {
        if (spot == null || nemesisData == null) return 0f;   // F10's spot-less Hide: blind.

        switch (spot.Type)
        {
            case EHidingSpotType.Locker:
                return spot.Data != null ? nemesisData.ViewRange * spot.Data.LockerVisionExposure : 0f;

            case EHidingSpotType.UnderTable:
                return nemesisData.ViewRange * nemesisData.UnderTableVisionMultiplier;

            default:
                return 0f;   // Container: sealed. The spec's "no vision at all".
        }
    }

    /// <summary>Whether a point is on the side the spot's interior pose faces — the way the player
    /// inside is looking out, which for a locker is the door.</summary>
    private static bool IsInFrontOf(HidingSpot spot, Vector3 point)
    {
        Transform pose = spot.InteriorPose;

        Vector3 facing = pose.forward;
        facing.y = 0f;

        Vector3 toPoint = point - pose.position;
        toPoint.y = 0f;

        return Vector3.Dot(facing, toPoint) > 0f;
    }

    /// <summary>
    /// <see cref="LineOfSight.CheckConeSampled"/> with a range of its own and the occupied spot's
    /// shell looked through. Same three samples up the body — feet, centre, head — for the reason
    /// that method gives: a head showing over the edge of a table is not the same as a player
    /// entirely under it.
    /// </summary>
    private bool CheckConeThroughSpot(Vector3 origin, Vector3 front, Collider target, float angle,
                                      float range, HidingSpot spot, out Vector3 seenPoint)
    {
        seenPoint = Vector3.zero;
        Bounds bounds = target.bounds;

        for (int j = -1; j < 2; j++)
        {
            Vector3 point = bounds.center + new Vector3(0f, j * bounds.extents.y * 0.9f, 0f);

            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;
            if (distance > range) continue;

            bool withinCone = distance <= minDistance || Vector3.Angle(front, toPoint) <= angle * 0.5f;
            if (!withinCone) continue;

            if (spot.IsLineBlockedIgnoringSelf(origin, point, obstacleMask)) continue;

            seenPoint = point;
            return true;
        }

        return false;
    }
    /// <summary>
    /// Last target seen, or null if nobody has been seen yet / the object was destroyed.
    /// Returns null instead of throwing: the catch state calls this and cannot assume
    /// there is always a target.
    /// </summary>
    public PlayerStateManager GetCurrentTarget()
    {
        if (lastKnownTarget == null) return null;

        // InParent and not GetComponent: lastKnownTarget is whichever collider FindVisibleTargets
        // happened to land on, and the entire player hierarchy sits on the Player layer — the rig
        // mesh and the AudioEmitingRange trigger match targetMask exactly as much as the root
        // capsule does. Reading the component off the collider's own GameObject returned null
        // whenever the sweep picked one of those, which dropped Catch into its "nobody to
        // capture" fallback at random. CheckExtremeProximity never hit this because it stores the
        // player root directly, which is why it only failed some of the time.
        return lastKnownTarget.GetComponentInParent<PlayerStateManager>();
    }
}
