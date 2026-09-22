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
/// known. Standing right next to a spot is the same event from the proximity rule (§3.3).
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
    /// The Nemesis stood at <paramref name="spot"/> for the whole check and nothing came of it — had
    /// the player been inside, the proximity rule would have found them. Nobody is there: forget it.
    /// Called by the state that did the checking.
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
    }

    /// <summary>Forgets everything. The capture and the respawn use it, and so does being seen out
    /// in the open.</summary>
    public void Forget()
    {
        KnownSpot = null;
        SuspectedSpot = null;
        Reason = null;
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
