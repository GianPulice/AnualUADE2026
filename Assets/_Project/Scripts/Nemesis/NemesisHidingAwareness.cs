using UnityEngine;

/// <summary>
/// What the Nemesis knows about hiding spots (plan §3.4): the one it is SURE the player is in, and
/// the one it only suspects. It knows; it does not act. NemesisSearchingState walks to the spot,
/// the ladder decides when, NemesisCatchState pulls the player out.
///
/// LEVEL A, "I SAW YOU GET IN". On <see cref="HidingEvents.OnEntered"/> — raised at the END of the
/// climb-in, which is the whole reason the climb-in takes any time — it asks its own eyes what they
/// had at that moment. Seeing the player, or having seen them within SeenEnteringWindow, with the
/// spot inside view range and its door in line of sight: KNOWN. The player in the corner of its eye on the last sweep, with the
/// meter past the threshold: SUSPECTED, worth walking over to, not certain. A meter that is only
/// still DRAINING from a chase that already lost them does not count — that is a memory, not a
/// glimpse, and a player who broke line of sight and then hid has done everything right (§13
/// case 2). Nothing at all: it does not know.
///
/// LEVEL B, "I CAN MAKE YOU OUT". FieldOfView keeps sensing through a locker's slats and under a
/// table at a reduced range, into the suspicion meter only. Past the threshold the spot becomes
/// suspected — which sends Investigating to it rather than off after some old noise, and the meter
/// keeps filling on the way — and once full, <see cref="FieldOfView.HiddenPlayerSpotted"/> makes it
/// known. Standing right next to a spot is the same event from the proximity rule (§3.3) — unless
/// the player is holding their breath (plan §17.6, D21): then only OPENING a spot it already
/// suspects or knows finds them (<see cref="Open"/>).
///
/// NOTHING HERE ASKS WHERE THE PLAYER REALLY IS. The event says which spot was entered; whether the
/// Nemesis gets to know it is decided entirely by what its own sensors had at the time. Leaving the
/// spot unseen is not reported either: it walks up to a locker that is empty by then, finds
/// nothing, and forgets it (<see cref="MarkChecked"/>). The monster can be wrong; it cannot be
/// omniscient.
///
/// Forgotten when checked and found empty, when the player is seen out in the open, on a capture
/// or a respawn (the same reasoning as FieldOfView.ForgetLastKnownPosition), when the spot is
/// burned or switched off, and — a safety net, not a mechanic — when nothing has confirmed it for
/// longer than the search budget. Every confirmation restarts that clock: a certainty the eyes keep
/// renewing does not expire while they still have it.
///
/// SETUP: none. NemesisStateManager adds it next to itself, initializes it and ticks it before the
/// ladder decides; every number it reads lives on SO_NemesisData.
/// </summary>
[DisallowMultipleComponent]
public class NemesisHidingAwareness : MonoBehaviour
{
    private const float FallbackViewRange = 7f;
    private const float FallbackSeenWindow = 0.75f;
    private const float FallbackThreshold = 0.4f;
    private const float FallbackKnownMemory = 15f;
    private const float FallbackSuspectedMemory = 8f;

    /// <summary>Height above the approach point the "could it see the door" ray aims at: where the
    /// body of a player climbing in is, not the floor.</summary>
    private const float DoorProbeHeight = 1f;

    private NemesisStateManager stateManager;
    private FieldOfView eyes;

    private float knownConfirmedAt;
    private float suspectedConfirmedAt;

    /// <summary>The spot it is sure the player is in, or null. "Sure" as in its own sensors said
    /// so — not as in true: the player may have slipped out since.</summary>
    public HidingSpot KnownSpot { get; private set; }

    /// <summary>A spot it caught the player getting into out of the corner of its eye. Worth a
    /// look, not a certainty. Never the same spot as <see cref="KnownSpot"/>.</summary>
    public HidingSpot SuspectedSpot { get; private set; }

    /// <summary>The spot worth walking to next: the known one, otherwise the suspected one.</summary>
    public HidingSpot SpotToCheck => KnownSpot != null ? KnownSpot : SuspectedSpot;

    /// <summary>Why the current knowledge exists, for the debug HUD. Null when there is none.</summary>
    public string Reason { get; private set; }

    private void Awake()
    {
        // Static events: subscribed in Awake and released in OnDestroy, per docs/CLAUDE.md. The
        // listeners check stateManager, so an event landing before Initialize is simply ignored.
        HidingEvents.OnEntered += HandleEntered;
        HidingEvents.OnSpotBurned += HandleSpotGone;
        PlayerEvents.OnPlayerCaptured += HandleCaptured;
        CheckpointManager.OnRespawned += HandleRespawned;
    }

    private void OnDestroy()
    {
        HidingEvents.OnEntered -= HandleEntered;
        HidingEvents.OnSpotBurned -= HandleSpotGone;
        PlayerEvents.OnPlayerCaptured -= HandleCaptured;
        CheckpointManager.OnRespawned -= HandleRespawned;

        if (eyes != null) eyes.HiddenPlayerSpotted -= HandleSpottedThroughSpot;
    }

    /// <summary>Called once by NemesisStateManager, after its references are resolved.</summary>
    public void Initialize(NemesisStateManager manager)
    {
        stateManager = manager;

        if (eyes != null) eyes.HiddenPlayerSpotted -= HandleSpottedThroughSpot;
        eyes = manager != null ? manager.FieldOfView : null;
        if (eyes != null) eyes.HiddenPlayerSpotted += HandleSpottedThroughSpot;
    }

    /// <summary>
    /// Drops what it can no longer stand behind. Ticked by NemesisStateManager BEFORE the ladder
    /// decides, so KnowsHidingSpot never reads a spot that stopped being worth knowing this frame.
    /// </summary>
    public void Tick()
    {
        SuspectWhatItIsMakingOut();
        TrackHidingSpotNoises();

        if (KnownSpot == null && SuspectedSpot == null) return;

        // Seen out in the open: whatever it believed about a spot is superseded by the real thing.
        // Out in the open and not merely seen — proximity detects a player INSIDE a spot too, and
        // that is the moment the knowledge pays off, not the moment to throw it away.
        if (stateManager != null && stateManager.HasVisualTarget)
        {
            PlayerStateManager player = PlayerRegistry.Current;
            if (player == null || !player.IsHidden)
            {
                Forget();
                return;
            }
        }

        if (KnownSpot != null && (IsGone(KnownSpot) || Time.time - knownConfirmedAt > KnownMemory))
            KnownSpot = null;

        if (SuspectedSpot != null && (IsGone(SuspectedSpot) || Time.time - suspectedConfirmedAt > SuspectedMemory))
            SuspectedSpot = null;

        if (KnownSpot == null && SuspectedSpot == null) Reason = null;
    }

    /// <summary>
    /// Level B on its way up: the meter is filling through a spot and has passed the threshold.
    /// Without this the ladder read that as a plain "vio algo de reojo", and Investigating went off
    /// to the last NOISE — which could be anywhere — walking away from the locker while the meter
    /// drained. Suspected, the spot is where it goes, and the meter keeps filling on the way.
    /// </summary>
    private void SuspectWhatItIsMakingOut()
    {
        if (eyes == null || KnownSpot != null) return;

        HidingSpot through = eyes.SensedThroughSpot;
        if (through == null) return;

        SO_NemesisData data = stateManager.NemesisData;
        float threshold = data != null ? data.AwarenessTriggerThreshold : FallbackThreshold;
        if (eyes.Awareness >= threshold) Suspect(through, "algo se mueve adentro");
    }

    /// <summary>
    /// The Nemesis stood at <paramref name="spot"/> for the whole check and nothing came of it — it
    /// opened it on arrival (<see cref="Open"/>) and nobody was inside. Forget it. Called by the
    /// state that did the checking.
    /// </summary>
    public void MarkChecked(HidingSpot spot)
    {
        if (spot == null) return;

        // Not while the eyes are still making the player out through that very spot. That happens
        // when the door it stood at is too far from the interior for the proximity rule — bad
        // authoring, but forgetting a spot it is looking into only to relearn it a frame later
        // would leave the Nemesis walking away from someone it can see.
        if (eyes != null && ReferenceEquals(eyes.SensedThroughSpot, spot)) return;

        if (ReferenceEquals(KnownSpot, spot)) KnownSpot = null;
        if (ReferenceEquals(SuspectedSpot, spot)) SuspectedSpot = null;
        if (KnownSpot == null && SuspectedSpot == null) Reason = null;

        // Checked and empty: a noise from around it starts from scratch (D22).
        hasFirstHidingNoise = false;
        if (ReferenceEquals(habitSpot, spot)) habitSpot = null;
    }

    /// <summary>
    /// The Nemesis opens the spot it walked up to check — the locker door, a look under the table —
    /// and sees whether anyone is in it (plan §17.6: suspecting a spot makes the player prey).
    /// Returns true, and makes the spot known, when the player is inside.
    ///
    /// This used to be the proximity rule's job: standing at the door found whoever was in there.
    /// Holding your breath now takes a player out of that rule (D21), so without an explicit open a
    /// checked spot would come back "empty" with the player in it. Opening is a sense like any
    /// other — it looks inside the one spot it is standing at — and the states only call it at a
    /// spot the Nemesis already suspects or knows, never at one it merely walked past.
    /// </summary>
    public bool Open(HidingSpot spot)
    {
        if (spot == null) return false;

        // The first opening by habit happens HERE, in front of the player, not when the spot was
        // picked: a suspicion dropped on the way (seen elsewhere, a capture) would otherwise spend the
        // lesson where nobody saw it (R3, review 28/09).
        if (ReferenceEquals(habitSpot, spot))
        {
            habitSpot = null;
            if (PlayerHabitTracker.Exists) PlayerHabitTracker.Instance.MarkRun(ECounterplay.CheckHidingSpots);
        }

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null || !ReferenceEquals(player.CurrentHidingSpot, spot)) return false;

        Know(spot, "lo abrió");
        return true;
    }

    /// <summary>Forgets everything. The capture and the respawn use it, and so does being seen out
    /// in the open.</summary>
    public void Forget()
    {
        KnownSpot = null;
        SuspectedSpot = null;
        Reason = null;
        habitSpot = null;

        // A breath heard before the capture, the respawn or seeing the player out in the open is not
        // half of "it sounded twice from there" afterwards.
        hasFirstHidingNoise = false;
    }

    // ── Spots the player has used (plan §17.6, D23; Fase 2D, the Nemesis half) ───────────────────

    /// <summary>How close to a used spot's door the player has to be for its FIRST opening by habit
    /// to count as seen or heard (R3): about the reach of the ear. Director-side knowledge, used only
    /// to stage the lesson, never to find anyone.</summary>
    private const float WitnessDistance = 12f;

    /// <summary>
    /// While it investigates or sweeps an area, the spots the player has USED inside it (the Fase 3
    /// meter, PlayerHabitTracker) are candidates: most used first, each rolled once against its open
    /// chance (meter × 0.25, capped at 0.85). The first that comes up becomes SUSPECTED, which sends the
    /// state to open it (case 39). Never outside the area: a spot across the level does not exist for
    /// this (case 40, R4). Returns true when it picked one.
    ///
    /// R3: until the first opening by habit has happened, only a spot the player is near enough to
    /// see or hear it being opened qualifies — the first time is the lesson, and a lesson nobody
    /// witnessed teaches nothing. Near enough means within WitnessDistance on the same floor. The
    /// lesson counts as given when the spot is OPENED (<see cref="Open"/>), not when it is picked.
    ///
    /// <paramref name="rolled"/> is the caller's memory of what it already rolled this search: a
    /// spot gets one roll per search, not one per re-centre. A spot passed over only because the
    /// player was too far to witness it is not rolled: it may qualify later in the same search.
    /// </summary>
    public bool ConsiderUsedSpots(Vector3 centre, float radius, System.Collections.Generic.HashSet<HidingSpot> rolled)
    {
        if (stateManager == null || KnownSpot != null || SuspectedSpot != null) return false;
        if (!PlayerHabitTracker.Exists) return false;

        PlayerHabitTracker habits = PlayerHabitTracker.Instance;
        if (habits.CollectUsedSpots(centre, radius, usedSpots) == 0) return false;

        bool firstTime = !habits.HasRun(ECounterplay.CheckHidingSpots);
        Transform player = stateManager.PlayerTransform;

        for (int i = 0; i < usedSpots.Count; i++)
        {
            HidingSpot spot = usedSpots[i];
            if (rolled != null && rolled.Contains(spot)) continue;
            if (firstTime && !CouldWitness(player, spot)) continue;

            rolled?.Add(spot);
            if (Random.value >= habits.OpenChance(spot)) continue;

            habitSpot = spot;
            Suspect(spot, $"lo usaste antes ({habits.GetSpotUsage(spot):0.#})");
            return true;
        }

        return false;
    }

    /// <summary>Whether the player is where they could see or hear <paramref name="spot"/> being
    /// opened: close enough, and on the same floor.</summary>
    private bool CouldWitness(Transform player, HidingSpot spot)
    {
        if (player == null) return false;

        Vector3 offset = spot.ApproachPoint.position - player.position;
        SO_NemesisData data = stateManager.NemesisData;
        float floor = data != null ? data.FloorHeightThreshold : 2.5f;
        if (Mathf.Abs(offset.y) > floor) return false;

        offset.y = 0f;
        return offset.sqrMagnitude <= WitnessDistance * WitnessDistance;
    }

    /// <summary>The spot suspected by habit whose opening is still to come: opening it is the first
    /// time of CheckHidingSpots (R3).</summary>
    private HidingSpot habitSpot;

    private readonly System.Collections.Generic.List<HidingSpot> usedSpots = new System.Collections.Generic.List<HidingSpot>();

    // ── A second noise from the same spot (plan §17.6, D22) ──────────────────

    /// <summary>A noise from inside a hiding spot that comes after this much quiet is a new one, not
    /// the same breath heard on the next sweep.</summary>
    private const float HidingNoiseGap = 1f;

    /// <summary>Two noises this close together are "from the same spot".</summary>
    private const float SameSpotNoiseDistance = 1.5f;

    /// <summary>How long the first one is remembered.</summary>
    private const float HidingNoiseMemory = 60f;

    /// <summary>How far from the noise a spot may be and still be where it came from.</summary>
    private const float NoiseToSpotDistance = 2f;

    private bool hasFirstHidingNoise;
    private Vector3 firstHidingNoiseAt;
    private float firstHidingNoiseTime;
    private float lastHidingNoiseHeardAt = float.NegativeInfinity;

    /// <summary>
    /// D22, the second half. The FIRST noise from inside a hiding spot marks the area (the belief's
    /// radius ×2, and the search sweeps it without walking to the door). A SECOND one from the same
    /// place makes the spot SUSPECTED: it was not a coincidence, and now it goes to open it. Which spot
    /// is the one the noise came out of — the nearest to where it sounded — which is what hearing it
    /// twice from the same place tells anyone.
    /// </summary>
    private void TrackHidingSpotNoises()
    {
        if (KnownSpot != null) return;

        FieldOfListening ears = stateManager.FieldOfListening;
        if (ears == null || !ears.TryGetLastPlayerNoise(out FieldOfListening.HeardNoise noise)) return;
        if (!noise.FromHidingSpot || noise.HeardAt <= lastHidingNoiseHeardAt) return;

        bool newEpisode = noise.HeardAt - lastHidingNoiseHeardAt > HidingNoiseGap;
        lastHidingNoiseHeardAt = noise.HeardAt;
        if (!newEpisode) return;

        bool sameAsFirst = hasFirstHidingNoise &&
                           noise.HeardAt - firstHidingNoiseTime < HidingNoiseMemory &&
                           (noise.Position - firstHidingNoiseAt).sqrMagnitude < SameSpotNoiseDistance * SameSpotNoiseDistance;

        if (!sameAsFirst)
        {
            hasFirstHidingNoise = true;
            firstHidingNoiseAt = noise.Position;
            firstHidingNoiseTime = noise.HeardAt;
            return;
        }

        HidingSpot spot = NearestSpot(noise.Position, NoiseToSpotDistance);
        if (spot == null) return;

        hasFirstHidingNoise = false;
        Suspect(spot, "volvió a sonar ahí");
    }

    private static HidingSpot NearestSpot(Vector3 point, float maxDistance)
    {
        HidingSpot best = null;
        float bestSqr = maxDistance * maxDistance;
        System.Collections.Generic.IReadOnlyList<HidingSpot> active = HidingSpot.Active;

        for (int i = 0; i < active.Count; i++)
        {
            HidingSpot spot = active[i];
            if (IsGone(spot)) continue;

            float sqr = (spot.InteriorPose.position - point).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = spot;
        }

        return best;
    }

    // ── Level A ──────────────────────────────────────────────────────────────

    private void HandleEntered(HidingSpot spot)
    {
        if (spot == null || stateManager == null || !stateManager.IsActive || eyes == null) return;

        SO_NemesisData data = stateManager.NemesisData;
        float viewRange = data != null ? data.ViewRange : FallbackViewRange;

        // Too far to have told this spot from the next one, whatever it saw.
        if ((spot.InteriorPose.position - eyes.ViewTransform.position).sqrMagnitude > viewRange * viewRange)
            return;

        // And it has to be able to see the spot's door at all. The window alone would let "seen a
        // moment ago round the corner" count as "saw you get in"; this is what makes it a sighting
        // of the climb. Aimed where the climbing player stands, at body height, looking through the
        // spot's own shell only.
        if (!eyes.HasLineOfSightTo(spot.ApproachPoint.position + Vector3.up * DoorProbeHeight, spot))
            return;

        float window = data != null ? data.SeenEnteringWindow : FallbackSeenWindow;
        if (eyes.HasVisualTarget || eyes.TimeSinceLastSighting < window)
        {
            Know(spot, "lo vio entrar");
            return;
        }

        // A LIVE contact, not just a high meter: see the class summary.
        float threshold = data != null ? data.AwarenessTriggerThreshold : FallbackThreshold;
        if (eyes.HasPeripheralContact && eyes.Awareness >= threshold) Suspect(spot, "lo vio de reojo al entrar");
    }

    // ── Level B ──────────────────────────────────────────────────────────────

    private void HandleSpottedThroughSpot(HidingSpot spot, bool atArmsLength) =>
        Know(spot, atArmsLength ? "lo tiene encima" : "lo distinguió escondido");

    // ── Forgetting ───────────────────────────────────────────────────────────

    private void HandleSpotGone(HidingSpot spot) => MarkChecked(spot);

    private void HandleCaptured(PlayerStateManager player) => Forget();

    private void HandleRespawned(Checkpoint checkpoint) => Forget();

    private void Know(HidingSpot spot, string reason)
    {
        if (spot == null) return;

        knownConfirmedAt = Time.time;
        KnownSpot = spot;
        if (ReferenceEquals(SuspectedSpot, spot)) SuspectedSpot = null;
        Reason = reason;
    }

    private void Suspect(HidingSpot spot, string reason)
    {
        if (spot == null || ReferenceEquals(KnownSpot, spot)) return;

        suspectedConfirmedAt = Time.time;
        SuspectedSpot = spot;
        if (KnownSpot == null) Reason = reason;
    }

    private static bool IsGone(HidingSpot spot) => spot == null || !spot.isActiveAndEnabled || spot.IsBurned;

    /// <summary>How long a certainty may go unconfirmed. The spot was inside view range when it
    /// became known, so the walk there takes seconds; a search budget's worth of them with nothing
    /// renewing it means something is in the way, and holding Searching on a certainty it cannot
    /// act on is the stuck monster every budget in the ladder exists to prevent.</summary>
    private float KnownMemory
    {
        get
        {
            SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
            return data != null ? data.SearchTimeOut : FallbackKnownMemory;
        }
    }

    /// <summary>Same safety net for a suspicion, on the budget of the state that goes to look —
    /// Investigating.</summary>
    private float SuspectedMemory
    {
        get
        {
            SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
            return data != null ? data.InvestigationTimeOut : FallbackSuspectedMemory;
        }
    }
}
