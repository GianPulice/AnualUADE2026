using System;
using UnityEngine;

/// <summary>
/// Tension meter and pacing state (plan §6: BuildUp → SustainPeak → PeakFade → Relax). It only measures;
/// <see cref="NemesisDirector"/> turns the state into levers, and nothing here touches the FSM.
/// </summary>
public class NemesisTension : MonoBehaviour
{
    // Not serialized: the order is free, but keep appending for the HUD's sake.
    public enum EPacingState
    {
        BuildUp,
        SustainPeak,
        PeakFade,
        Relax,
    }

    private const float SenseInterval = 0.2f;
    private const float PlayerEyeHeight = 1.5f;
    private const float NemesisChestHeight = 1.3f;

    public event Action<EPacingState> PacingStateChanged;

    /// <summary>
    /// The Nemesis became off-limits (asleep, in a cinematic, in the escape) or stopped being so —
    /// and once when the tension starts running, suspended or not, so the Director can settle what it
    /// lends against the state it wakes up in.
    ///
    /// Raised on the frame it happens, and on activation from inside NemesisEvents.OnActivated. That
    /// is the point of it: the escape wakes the Nemesis straight into a cinematic, and a puzzle's
    /// senses boost used to ride along for the rest of its duration — a suspension only cleared the
    /// pacing's own pressure, and only on the Director's three-second tick (plan §14.1, §18.5 C4).
    /// </summary>
    public event Action SuspensionChanged;

    public float Tension { get; private set; }
    public EPacingState State { get; private set; } = EPacingState.BuildUp;

    /// <summary>Seconds left in SustainPeak or Relax; 0 in the states that end on a condition.</summary>
    public float StateTimeRemaining => Mathf.Max(0f, stateEndsAt - Time.time);

    /// <summary>Seconds of BuildUp with no ENCOUNTER (see <see cref="IsEncounter"/>). Paused while the
    /// player is in the Hub.</summary>
    public float QuietTime { get; private set; }

    public bool IsQuiet => pacing != null && State == EPacingState.BuildUp && QuietTime >= pacing.QuietTimeout;

    public bool IsRunning => pacing != null && hasActivated;

    /// <summary>
    /// The Nemesis is off-limits: asleep, in a cinematic or in the escape. Measured from the moment it
    /// first wakes up, with or without an SO_DirectorPacing — it is a fact about the Nemesis, not about
    /// the rhythm, and the Director's guard for the escape (clear every pressure) must hold in a level
    /// that has no pacing too. Before the first activation it is false: nothing has started yet.
    /// </summary>
    public bool IsSuspended => SuspendReason != null;
    public string SuspendReason { get; private set; }

    public bool IsPlayerInSafeZone { get; private set; }
    public bool PlayerSeesNemesis { get; private set; }
    public bool HiddenNearSearch { get; private set; }
    public bool IsChasing => isChasing;
    public float Proximity => proximity;

    public SO_DirectorPacing Pacing => pacing;

    private SO_DirectorPacing pacing;
    private NemesisStateManager nemesis;

    private bool hasActivated;
    private bool isChasing;
    private float proximity;

    private float lastStimulusAt = float.NegativeInfinity;
    private float stateEndsAt;
    private float nextSenseAt;

    public void Configure(SO_DirectorPacing pacingConfig, NemesisStateManager nemesisManager)
    {
        pacing = pacingConfig;
        nemesis = nemesisManager;

        // The activation event may have fired before we subscribed.
        if (nemesis != null && nemesis.IsActive)
        {
            hasActivated = true;
            RefreshSuspension(announce: true);
        }
    }

    private void Awake()
    {
        NemesisEvents.OnActivated += HandleActivated;
        NemesisEvents.OnProximityChanged += HandleProximity;
        NemesisEvents.OnChaseStarted += HandleChaseStarted;
        NemesisEvents.OnChaseEnded += HandleChaseEnded;
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
    }

    private void OnDestroy()
    {
        NemesisEvents.OnActivated -= HandleActivated;
        NemesisEvents.OnProximityChanged -= HandleProximity;
        NemesisEvents.OnChaseStarted -= HandleChaseStarted;
        NemesisEvents.OnChaseEnded -= HandleChaseEnded;
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
    }

    /// <summary>
    /// Starts the tension, and measures the suspension right here rather than on the next Update:
    /// the escape cinematic begins BEFORE it wakes the Nemesis (EscapeSequenceDirector.RunRevealAsync:
    /// BeginCinematic, then NemesisCinematicActor.TryTakeControl → ActivateInPlace), so the Nemesis
    /// is already suspended on the frame it wakes, and the Director has to hear it on that frame.
    /// </summary>
    private void HandleActivated()
    {
        hasActivated = true;
        RefreshSuspension(announce: true);
    }

    private void HandleProximity(float value) => proximity = Mathf.Clamp01(value);
    private void HandleChaseStarted() => isChasing = true;
    private void HandleChaseEnded() => isChasing = false;

    private NemesisStateManager.ENemesisState? NemesisState => nemesis != null ? nemesis.CurrentStateKey : null;

    private void HandlePlayerCaptured(PlayerStateManager player)
    {
        if (!IsRunning) return;

        Tension = 1f;
        lastStimulusAt = Time.time;

        // A capture is the encounter by definition.
        QuietTime = 0f;
    }

    private void Update()
    {
        if (!hasActivated) return;

        RefreshSuspension(announce: false);
        if (!IsRunning || IsSuspended) return;

        float deltaTime = Time.deltaTime;

        if (Time.time >= nextSenseAt)
        {
            nextSenseAt = Time.time + SenseInterval;
            SenseWorld();
        }

        // The meter is fed exactly as before the silence rule changed: proximity still counts as
        // contact here, it still raises the tension and still holds the decay off. Only what resets
        // QuietTime changed — see IsEncounter.
        float gain = proximity * pacing.ProximityGain
                   + (isChasing ? pacing.ChaseGain : 0f)
                   + (PlayerSeesNemesis ? pacing.PlayerSeesNemesisGain : 0f)
                   + (HiddenNearSearch ? pacing.HiddenNearSearchGain : 0f);

        bool contact = gain > 0f;

        if (contact)
        {
            Tension = Mathf.Clamp01(Tension + gain * deltaTime);
            lastStimulusAt = Time.time;
        }
        else if (!IsHunting() && Time.time - lastStimulusAt >= pacing.DecayDelay)
        {
            Tension = Mathf.Max(0f, Tension - pacing.DecayPerSecond * deltaTime);
        }

        if (IsEncounter()) QuietTime = 0f;
        else if (State == EPacingState.BuildUp && !IsPlayerInSafeZone) QuietTime += deltaTime;

        TickPacing();
    }

    /// <summary>Recomputes <see cref="SuspendReason"/> and raises <see cref="SuspensionChanged"/> when
    /// being suspended flips — or always, with <paramref name="announce"/> (the tension just started).
    /// The reason changing on its own ("cinemática" → "escape") is not news.</summary>
    private void RefreshSuspension(bool announce)
    {
        bool wasSuspended = IsSuspended;
        SuspendReason = FindSuspendReason();

        if (announce || IsSuspended != wasSuspended) SuspensionChanged?.Invoke();
    }

    private string FindSuspendReason()
    {
        if (nemesis == null || !nemesis.IsActive) return "dormido";
        if (CinematicState.IsPlaying) return "cinemática";
        if (nemesis.Decision != null && nemesis.Decision.ChaseFloor) return "escape";
        return null;
    }

    /// <summary>
    /// Whether this frame is an ENCOUNTER: what resets the silence that rising sensitivity waits for
    /// (plan §18.5 C3). A chase (Chasing, or Catch unresolved); the player seeing the Nemesis; the
    /// player hidden with it searching nearby; the Nemesis within <see
    /// cref="SO_DirectorPacing.QuietProximityThreshold"/> (≈3 m); or it searching or investigating on
    /// evidence of the PLAYER younger than <see cref="SO_DirectorPacing.EncounterBeliefFreshness"/> —
    /// the belief's age, which leads (decoys, Director noise) do not refresh. A capture resets it too,
    /// from its own event.
    ///
    /// WHY PROXIMITY ALONE STOPPED COUNTING. It used to be "any gain at all", and proximity gains from
    /// the edge of the Nemesis's 12 m proximity radius, measured by NavMesh and whether it senses the
    /// player or not. Measured in Unity on the testbed (plan §18.3): 94 % of its NavMesh is within 12 m
    /// of some route, so every patrol pass anywhere near the player reset the silence. The simulated
    /// patrol spends 14–19 % of its time that close to a player standing still, and those passes are
    /// spread over the whole run, so the 90 s of silence arrived late and rarely: rising sensitivity
    /// started in only 40–85 % of simulated ten-minute runs (73–95 % counting proximity only above
    /// 0.5; the 0.75 here is stricter still). A monster walking past behind a wall without noticing
    /// anyone is not an encounter. It still adds to the meter, which is about how the player feels,
    /// not about whether the Nemesis found them.
    /// </summary>
    private bool IsEncounter()
    {
        if (isChasing || PlayerSeesNemesis || HiddenNearSearch) return true;
        if (proximity > pacing.QuietProximityThreshold) return true;

        NemesisStateManager.ENemesisState? state = NemesisState;
        bool hunting = state == NemesisStateManager.ENemesisState.Searching ||
                       state == NemesisStateManager.ENemesisState.Investigating;

        return hunting && nemesis.BeliefAge < pacing.EncounterBeliefFreshness;
    }

    private void SenseWorld()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null)
        {
            IsPlayerInSafeZone = false;
            PlayerSeesNemesis = false;
            HiddenNearSearch = false;
            return;
        }

        IsPlayerInSafeZone = NemesisSafeZones.Contains(player.transform.position);
        PlayerSeesNemesis = CheckPlayerSeesNemesis(player);

        NemesisStateManager.ENemesisState? state = NemesisState;
        bool hunting = state == NemesisStateManager.ENemesisState.Searching ||
                       state == NemesisStateManager.ENemesisState.Investigating;
        HiddenNearSearch = player.IsHidden && hunting && proximity > 0f;
    }

    private bool CheckPlayerSeesNemesis(PlayerStateManager player)
    {
        FieldOfListening senses = nemesis.FieldOfListening;
        if (senses == null) return false;

        Vector3 eye = player.transform.position + Vector3.up * PlayerEyeHeight;
        Vector3 chest = nemesis.transform.position + Vector3.up * NemesisChestHeight;

        float range = pacing.PlayerSightRange;
        if ((chest - eye).sqrMagnitude > range * range) return false;

        // The player's own locker is not a wall: they are looking out through its slats.
        return !senses.IsOccludedByWall(eye, chest, player.CurrentHidingSpot);
    }

    /// <summary>Chasing or an unresolved Catch: the meter never decays and PeakFade keeps waiting.</summary>
    private bool IsHunting() =>
        isChasing ||
        NemesisState == NemesisStateManager.ENemesisState.Chasing ||
        NemesisState == NemesisStateManager.ENemesisState.Catch;

    private void TickPacing()
    {
        switch (State)
        {
            // Only from BuildUp: Relax starts with the meter still high, and peaking from there would loop.
            case EPacingState.BuildUp:
                if (Tension >= pacing.PeakThreshold) Enter(EPacingState.SustainPeak, pacing.RollSustainPeak());
                break;

            case EPacingState.SustainPeak:
                if (Time.time >= stateEndsAt) Enter(EPacingState.PeakFade, 0f);
                break;

            case EPacingState.PeakFade:
                if (IsEncounterOver()) Enter(EPacingState.Relax, pacing.RollRelax());
                break;

            case EPacingState.Relax:
                if (Time.time >= stateEndsAt) Enter(EPacingState.BuildUp, 0f);
                break;
        }
    }

    /// <summary>
    /// No chase, no grab, nothing crossing floors after the player, and no search on a fresh sighting.
    ///
    /// The sighting is the one the BELIEF keeps (NemesisBelief.TryGetLastSeen), not FieldOfView read
    /// from behind (plan §10: everything downstream of the senses reads the belief). Same answer
    /// today — the belief forwards the eyes' last sighting — but one definition of "where it last saw
    /// the player" for the whole project, so this cannot drift from what Searching acts on.
    /// </summary>
    private bool IsEncounterOver()
    {
        if (IsHunting()) return false;

        NemesisStateManager.ENemesisState? state = NemesisState;
        if (state == NemesisStateManager.ENemesisState.Traversing) return false;

        if (state == NemesisStateManager.ENemesisState.Searching)
        {
            NemesisBelief belief = nemesis.Belief;
            if (belief != null && belief.TryGetLastSeen(out _, out float sightingAge) &&
                sightingAge < pacing.FreshSightSeconds)
            {
                return false;
            }
        }

        return true;
    }

    private void Enter(EPacingState next, float duration)
    {
        State = next;
        stateEndsAt = Time.time + duration;

        if (next == EPacingState.BuildUp) QuietTime = 0f;

        PacingStateChanged?.Invoke(next);
    }

    // ── Test console only ───────────────────────────────────────────────────

    /// <summary>Fills the meter, as a long chase would. Changes an input, not a state.</summary>
    public void DebugSpike()
    {
        Tension = 1f;
        lastStimulusAt = Time.time;
    }

    /// <summary>Skips the quiet wait so rising sensitivity can be watched without waiting 90 s.</summary>
    public void DebugSkipQuiet()
    {
        if (pacing != null) QuietTime = Mathf.Max(QuietTime, pacing.QuietTimeout);
    }
}
