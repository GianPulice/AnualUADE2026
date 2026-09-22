using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vision sensor of the Nemesis.
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

    private float awareness;

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

        // Extreme proximity is checked every frame and before everything else, deliberately:
        // it does not wait for the viewDelay cadence and it is the only thing that defeats
        // Hidden. Skipping the normal sweep when it hits also stops FindVisibleTargets from
        // immediately clearing the flag it just set.
        if (CheckExtremeProximity()) return;

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

        if (!peripheralContact)
        {
            awareness = Mathf.Max(0f, awareness - nemesisData.AwarenessDecayRate * deltaTime);
            return;
        }

        float buildTime = Mathf.Max(0.05f, nemesisData.AwarenessBuildTime);

        // Closeness scales the RATE, floored so a contact at the very edge of the range still
        // eventually registers instead of stalling at a value it can never climb past.
        float rate = Mathf.Lerp(0.35f, 2f, peripheralCloseness) / buildTime;

        awareness = Mathf.Min(1f, awareness + rate * deltaTime);

        if (awareness < 1f) return;

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
        if (nemesisData.ProximityDetectionRespectsWalls &&
            IsOccluded(playerPosition + Vector3.up * BodyProbeHeight, spot)) return false;

        lastKnownTarget = player.gameObject;
        RecordSighting(playerPosition);

        // Straight to full. Hard proximity is the one detection that answers no questions about
        // cones or suspicion - it is "you are standing on me" - so ramping it would be absurd, and
        // leaving the meter low here would let it decay while the player is still in contact.
        awareness = 1f;
        peripheralContact = false;

        // Inside a spot: standing next to it is KNOWING it, not seeing them. As a sighting it won
        // "lo está viendo", and Chasing ran at a point inside the prop — which the agent can only
        // approach to the edge of the NavMesh, stopping short of the grab and staring at the door
        // for as long as the player stayed in. Known, the search walks to the door and Catch pulls
        // them out. The F10 console's spot-less Hide has no door to walk to: that one is a sighting.
        if (spot != null)
        {
            hasVisualTarget = false;
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

        // Hidden means inside a locker, under a table or in a container: NORMAL vision cannot reach
        // the player at all. Extreme proximity was already checked in Update before this ran — so
        // getting here with IsHidden means it did not trigger — and what is left is what leaks
        // through the spot itself, into the suspicion meter only (SenseThroughSpot).
        if (player != null && player.IsHidden)
        {
            hasVisualTarget = false;

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

        float viewRange = nemesisData.ViewRange;
        float viewAngle = nemesisData.ViewAngle;

        // Crouching shortens the range it can be spotted at rather than breaking line of sight:
        // a lower silhouette is harder to pick out, not invisible.
        if (player != null && player.IsCrouch) viewRange *= nemesisData.CrouchVisionMultiplier;

        Vector3 eye = viewTransform.position;
        Vector3 front = LookDirection;
        float focusAngle = nemesisData.FocusAngle;

        visibleTargets.Clear();
        peripheralContact = false;

        GameObject focusHit = null;
        GameObject peripheralHit = null;
        float peripheralDistance = float.PositiveInfinity;

        Collider[] targetsInViewRadius = Physics.OverlapSphere(eye, viewRange, targetMask);
        for (int i = 0; i < targetsInViewRadius.Length; i++)
        {
            Collider candidate = targetsInViewRadius[i];

            // The OUTER cone, sampled at feet/centre/head. One call where this used to be a
            // hand-rolled double loop; see LineOfSight.CheckConeSampled for why the three samples
            // and the together-per-sample angle+occlusion test both matter.
            if (!LineOfSight.CheckConeSampled(eye, front, candidate, viewAngle, minDistance,
                                              obstacleMask, out Vector3 seenPoint))
            {
                continue;
            }

            GameObject target = candidate.gameObject;
            float distance = Vector3.Distance(eye, seenPoint);

            // Inside the focus cone this is a sighting, exactly as it always was. minDistance
            // still overrides the angle: something touching the Nemesis is not "in the corner of
            // its eye" no matter which way it happens to be facing.
            bool inFocus = distance <= minDistance ||
                           LineOfSight.CheckAngle(eye, seenPoint, front, focusAngle);

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

        if (visibleTargets.Count > 0)
        {
            hasVisualTarget = true;
            lastKnownTarget = focusHit != null ? focusHit : visibleTargets[0];
            RecordSighting(visibleTargets[0].transform.position);
            return;
        }

        hasVisualTarget = false;

        if (peripheralHit == null) return;

        peripheralContact = true;
        peripheralTarget = peripheralHit;
        peripheralCloseness = 1f - Mathf.Clamp01(peripheralDistance / Mathf.Max(0.01f, viewRange));
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
