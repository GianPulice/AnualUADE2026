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

    public float Tension { get; private set; }
    public EPacingState State { get; private set; } = EPacingState.BuildUp;

    /// <summary>Seconds left in SustainPeak or Relax; 0 in the states that end on a condition.</summary>
    public float StateTimeRemaining => Mathf.Max(0f, stateEndsAt - Time.time);

    /// <summary>Seconds of BuildUp with no contact. Paused while the player is in the Hub.</summary>
    public float QuietTime { get; private set; }

    public bool IsQuiet => pacing != null && State == EPacingState.BuildUp && QuietTime >= pacing.QuietTimeout;

    public bool IsRunning => pacing != null && hasActivated;
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
        if (nemesis != null && nemesis.IsActive) hasActivated = true;
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

    private void HandleActivated() => hasActivated = true;
    private void HandleProximity(float value) => proximity = Mathf.Clamp01(value);
    private void HandleChaseStarted() => isChasing = true;
    private void HandleChaseEnded() => isChasing = false;

    private NemesisStateManager.ENemesisState? NemesisState => nemesis != null ? nemesis.CurrentStateKey : null;

    private void HandlePlayerCaptured(PlayerStateManager player)
    {
        if (!IsRunning) return;

        Tension = 1f;
        lastStimulusAt = Time.time;
    }

    private void Update()
    {
        if (!IsRunning) return;

        SuspendReason = FindSuspendReason();
        if (IsSuspended) return;

        float deltaTime = Time.deltaTime;

        if (Time.time >= nextSenseAt)
        {
            nextSenseAt = Time.time + SenseInterval;
            SenseWorld();
        }

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

        if (contact) QuietTime = 0f;
        else if (State == EPacingState.BuildUp && !IsPlayerInSafeZone) QuietTime += deltaTime;

        TickPacing();
    }

    private string FindSuspendReason()
    {
        if (nemesis == null || !nemesis.IsActive) return "dormido";
        if (CinematicState.IsPlaying) return "cinemática";
        if (nemesis.Decision != null && nemesis.Decision.ChaseFloor) return "escape";
        return null;
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

    /// <summary>No chase, no grab, nothing crossing floors after the player, and no search on a fresh sighting.</summary>
    private bool IsEncounterOver()
    {
        if (IsHunting()) return false;

        NemesisStateManager.ENemesisState? state = NemesisState;
        if (state == NemesisStateManager.ENemesisState.Traversing) return false;

        if (state == NemesisStateManager.ENemesisState.Searching)
        {
            FieldOfView view = nemesis.FieldOfView;
            if (view != null && view.HasLastKnownPosition &&
                view.TimeSinceLastSighting < pacing.FreshSightSeconds)
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
