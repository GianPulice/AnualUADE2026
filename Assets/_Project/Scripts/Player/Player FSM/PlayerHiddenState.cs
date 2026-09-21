using UnityEngine;

/// <summary>
/// The player is inside a hiding spot.
///
/// It owns exactly one thing: HOW LOUD THE PLAYER IS IN THERE. Getting in and out, the poses, the
/// interior camera and freezing the body all belong to <see cref="HidingSpot"/>, which is already
/// done by the time this state is entered.
///
/// BREATHING IS A SPHERE, NOT AN EVENT. There is no OnNoiseGenerated in this project: the Nemesis
/// hears the player's <c>AudioEmitingZone</c> collider, which <c>FieldOfListening</c> sweeps every
/// 0.1 s. So a breath is the emitter switched ON at the breathing radius for
/// <see cref="SO_HidingData.BreathingPulseDuration"/> and off again — long enough that a sweep
/// cannot miss it. Read "Noise is a sphere, not an event" in docs/CLAUDE.md before touching any of
/// this; in particular, the emitter is SHARED with the movement states, so whatever this state
/// changed is put back exactly as it was found on the way out. Leave a radius behind and the
/// player is permanently loud for the rest of the run, silently.
///
/// The breathing the PLAYER hears is a different thing entirely and lives in
/// <see cref="HiddenBreathing"/> on the player prefab, which makes no noise for the Nemesis at all.
/// </summary>
public class PlayerHiddenState : BaseState<PlayerStateManager.EPlayerState>
{
    private readonly PlayerStateManager playerStateManager;

    // The emitter as the movement states left it, restored verbatim in ExitState.
    private float cachedNoiseRadius;
    private bool cachedEmitterActive;

    // Seconds until the next breath. Counts down only while not holding.
    private float nextBreathIn;

    // Seconds the current pulse (breath or exhale) still has to run; 0 when the emitter is off.
    private float pulseLeft;

    private bool holdingBreath;
    private float heldFor;

    public PlayerHiddenState(PlayerStateManager.EPlayerState key, PlayerStateManager stateManager) : base(key)
    {
        playerStateManager = stateManager;
    }

    public override void EnterState()
    {
        // Without this the first entry inherits NextState = default(EPlayerState) = Idle, so
        // GetNextState() bounces straight back out on the next frame.
        NextState = StateKey;

        SphereCollider emitter = playerStateManager.AudioEmitingZone;
        cachedNoiseRadius = emitter.radius;
        cachedEmitterActive = emitter.gameObject.activeSelf;

        // Silent until the first breath. Every state that can lead here turns the emitter back ON
        // in its ExitState (Idle and Crouch both do), so this is not redundant.
        emitter.gameObject.SetActive(false);

        holdingBreath = false;
        heldFor = 0f;
        pulseLeft = 0f;

        // Not a full interval: the first breath lands soon enough that a player who dived into a
        // locker with the monster on their heels has to decide about holding it straight away.
        nextBreathIn = Interval() * 0.5f;

        playerStateManager.CurrentVelocity = 0f;
    }

    public override void ExitState()
    {
        NextState = StateKey;

        SphereCollider emitter = playerStateManager.AudioEmitingZone;
        emitter.radius = cachedNoiseRadius;
        emitter.gameObject.SetActive(cachedEmitterActive);

        holdingBreath = false;
        pulseLeft = 0f;
    }

    public override void UpdateState()
    {
        // A capture beats everything, including a spot that has not released yet.
        if (playerStateManager.IsImmobilized)
        {
            NextState = PlayerStateManager.EPlayerState.Disabled;
            return;
        }

        if (!playerStateManager.IsHidden)
        {
            NextState = PlayerStateManager.EPlayerState.Idle;
            return;
        }

        // The body is kinematic and parked at the interior pose by HidingSpot, so there is nothing
        // to zero and nothing to drive here. Writing linearVelocity would fight that freeze.
        TickBreathing();
    }

    // ── Breathing ───────────────────────────────────────────────────────────

    private void TickBreathing()
    {
        // Frozen, not released, while a menu is up. Reading the key as "not held" here would turn
        // opening the pause menu mid-hold into the exhale — the player's own menu giving them away.
        if (PauseManager.IsGameplayInputBlocked) return;

        float dt = Time.deltaTime;
        SO_HidingData data = Data();

        // A running pulse always finishes, even if the player starts holding their breath in the
        // middle of it: a breath that is already out cannot be taken back, and cutting it short
        // could drop it under the Nemesis's sweep interval and make holding a free undo.
        if (pulseLeft > 0f)
        {
            pulseLeft -= dt;
            if (pulseLeft <= 0f) StopPulse();
            return;
        }

        bool wantsToHold = GameInput.HoldBreathHeld && !PauseManager.IsGameplayInputBlocked;

        if (wantsToHold)
        {
            holdingBreath = true;
            heldFor += dt;

            // The lungs give out. The exhale happens whether the player let go or not, which is
            // what stops "hold F forever" from being total immunity.
            float maxHold = data != null ? data.MaxHoldSeconds : 0f;
            if (maxHold > 0f && heldFor >= maxHold) Exhale();
            return;
        }

        if (holdingBreath)
        {
            // Let go: the involuntary exhale, louder than a breath. This is the cost of holding.
            Exhale();
            return;
        }

        nextBreathIn -= dt;
        if (nextBreathIn <= 0f) Breathe();
    }

    private void Breathe()
    {
        StartPulse(RadiusOf(Data() != null ? Data().BreathingNoiseRadius : 0f),
                   Data() != null ? Data().BreathingPulseDuration : 0.4f);
        nextBreathIn = Interval();
    }

    private void Exhale()
    {
        holdingBreath = false;
        heldFor = 0f;

        StartPulse(RadiusOf(Data() != null ? Data().ExhaleNoiseRadius : 0f),
                   Data() != null ? Data().ExhalePulseDuration : 0.6f);

        // A full interval after an exhale, not half: the player has just emptied their lungs and
        // the exhale itself was the noise for this beat.
        nextBreathIn = Interval();
    }

    private void StartPulse(float radius, float duration)
    {
        if (radius <= 0f || duration <= 0f) return;

        SphereCollider emitter = playerStateManager.AudioEmitingZone;
        emitter.radius = radius;
        emitter.gameObject.SetActive(true);

        // Floor at two sweeps of FieldOfListening (0.1 s each). A pulse under one sweep is a coin
        // flip that can be heard by nobody, which reads in play as "the monster ignores breathing".
        pulseLeft = Mathf.Max(duration, 0.25f);
    }

    private void StopPulse()
    {
        pulseLeft = 0f;
        playerStateManager.AudioEmitingZone.gameObject.SetActive(false);
    }

    /// <summary>Scales a spec radius by what this spot type does to it — only the container
    /// muffles. The monster's own wall and floor occlusion is applied on top, by it, later.</summary>
    private float RadiusOf(float baseRadius)
    {
        SO_HidingData data = Data();
        HidingSpot spot = playerStateManager.CurrentHidingSpot;
        if (data == null || spot == null) return baseRadius;
        return baseRadius * data.NoiseRadiusMultiplierFor(spot.Type);
    }

    private float Interval()
    {
        SO_HidingData data = Data();
        return data != null ? data.BreathingInterval : 3f;
    }

    /// <summary>
    /// The shared hiding asset, or null while the player is "hidden" with no spot at all — which is
    /// what the F10 console's Hide toggle does, so that vision can be tested without a level built
    /// around it. In that case there is simply no breathing: nothing here may assume a spot.
    /// </summary>
    private SO_HidingData Data()
    {
        HidingSpot spot = playerStateManager.CurrentHidingSpot;
        return spot != null ? spot.Data : null;
    }
}
