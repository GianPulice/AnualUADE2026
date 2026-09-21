using UnityEngine;

/// <summary>
/// The Nemesis during the escape (Paso 6): it trots after the player for good, with active pathing
/// to the player's real position rather than a fixed route.
///
/// It leans on the Nemesis's own machine and adds exactly three things while it is on:
///   1. <see cref="NemesisDecision.ChaseFloor"/>: the decision ladder can never drop it below
///      Chasing (Catch and the lift still win, so it can still grab the player).
///   2. A fresh belief: with <see cref="SO_EscapeSequenceConfig.PerfectTracking"/> it tells the
///      Nemesis where the player is every refresh (<see cref="FieldOfView.InjectSighting"/>), so
///      dense fog does not make it lose them.
///   3. The pace: its speed, measured against what the player's sprint is worth RIGHT NOW rather
///      than a fixed m/s. A module penalty that slows the player down (M1 legs, M2 chest) slows
///      the Nemesis with them, so the chase keeps the same shape instead of turning into a
///      guaranteed catch. Distance shades it: it eases off when it is on top of the player and
///      presses when the player pulls away.
///
/// Not a second FSM and not a state: it never writes a state, a destination or a route. The
/// pursuit steers exactly as any chase does (NemesisPursuit).
///
/// Sits on the escape sequence object, not on the Nemesis.
/// </summary>
public class NemesisEscapePursuit : MonoBehaviour
{
    private SO_EscapeSequenceConfig config;
    private NemesisStateManager nemesis;
    private bool active;
    private float refreshTimer;

    public bool IsActive => active;

    /// <summary>Starts the escape pursuit. Call it on the same frame the player gets control back.</summary>
    public void Begin(SO_EscapeSequenceConfig escapeConfig)
    {
        config = escapeConfig;
        if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>();

        if (nemesis == null || nemesis.Decision == null)
        {
            Debug.LogWarning($"[{nameof(NemesisEscapePursuit)}] No working Nemesis found: the " +
                             "escape runs without a pursuer.", this);
            return;
        }

        active = true;
        refreshTimer = 0f;
        nemesis.Decision.ChaseFloor = true;
    }

    /// <summary>Gives the Nemesis back its normal behaviour.</summary>
    public void End()
    {
        active = false;
        if (nemesis != null && nemesis.Decision != null) nemesis.Decision.ChaseFloor = false;
    }

    private void OnDestroy() => End();

    private void Update()
    {
        if (!active || config == null || !config.PerfectTracking) return;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = config.TrackingRefreshSeconds;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player != null && nemesis.FieldOfView != null)
            nemesis.FieldOfView.InjectSighting(player.gameObject);
    }

    // LateUpdate: the Chasing state writes the agent's speed during the FSM's Update, so this
    // one lands after it and is what the agent actually runs at.
    private void LateUpdate()
    {
        if (!active || config == null || !nemesis.IsAgentReady) return;
        if (nemesis.CurrentStateKey != NemesisStateManager.ENemesisState.Chasing) return;

        // Standing (arrived, waiting on the capture cooldown): the state set speed 0 on purpose.
        if (nemesis.CurrentGait != NemesisStateManager.EGait.Running) return;

        nemesis.NavAgent.speed = ComputeSpeed();
    }

    private float ComputeSpeed()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null) return nemesis.NemesisMovement.ChaseSpeed;

        Vector3 gap = player.transform.position - nemesis.transform.position;
        gap.y = 0f;

        float t = Mathf.InverseLerp(config.PaceNearDistance, config.PaceFarDistance, gap.magnitude);
        float pace = Mathf.Lerp(config.PaceNear, config.PaceFar, t);
        return Mathf.Max(SprintSpeedOf(player) * pace, config.MinChaseSpeed);
    }

    /// <summary>What the player's sprint is worth this frame, module penalties included. This is
    /// the yardstick the pace is measured against, which is what makes the chase self-adjust.</summary>
    private static float SprintSpeedOf(PlayerStateManager player)
    {
        SO_Movement movement = player.Movement;
        if (movement == null) return 4.5f;

        // EffectiveMoveSpeed already carries the legs penalty; SprintPenaltyFactor is the chest one.
        return player.EffectiveMoveSpeed * movement.SprintSpeedMultiplier * player.SprintPenaltyFactor;
    }
}
