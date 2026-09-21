using System;
using UnityEngine;

/// <summary>
/// The light / fog loop of the escape (Pasos 4-5). Its one job is WHEN: when the corridor's white
/// lights come on and the fog opens, when they die and the fog closes again, and which door of the
/// route the player has reached.
///
/// The loop, on one clock for the whole corridor:
///   1. Opening — the white lights come on and the fog opens (open seconds, lerped).
///   2. Holding — lights on, fog open (hold seconds): the level is visible, and so is the Nemesis.
///   3. Closing — the lights die and the fog closes back to its normal density (close seconds).
///   4. Dark    — fog closed (dark gap seconds). Only the amber lights of the path show through it.
/// Nothing scales with progress: every number is the same from the first door to the gate.
///
/// The amber lights (<see cref="EscapeGuideDoor"/>) are fixed and stay on for the whole escape:
/// they are the path, and they are what pierces the closed fog. They used to be the thing that
/// cycled, one door at a time, with a beam aimed at the player — which read as a light following
/// the player and never as a way to go (WIR-039).
///
/// The white lights themselves are <see cref="EscapeCorridorFlicker"/>'s: this only sets how much
/// power they get (<see cref="Power"/>), and they keep failing on top of it.
///
/// The fog presets (<see cref="SO_EscapeSequenceConfig.ClosedFog"/> and
/// <see cref="SO_EscapeSequenceConfig.OpenFog"/>) are pushed onto the <see cref="VisionRangeController"/>'s
/// stack as runtime copies, with the open / close seconds written into their transition time. That
/// is what makes the seconds in the config the ONLY place the lerp durations live.
///
/// Reaching the last door of the route raises <see cref="RouteCompleted"/>; what happens then is
/// not this component's business.
/// </summary>
public class EscapeFogCycle : MonoBehaviour
{
    [Tooltip("The doors of the route, IN ORDER: the last one is the gate. Each one carries an " +
             "EscapeGuideDoor.")]
    [SerializeField] private EscapeGuideDoor[] route = Array.Empty<EscapeGuideDoor>();

    /// <summary>The player reached the door at this index of the route.</summary>
    public event Action<int> DoorReached;

    /// <summary>The player reached the last door of the route.</summary>
    public event Action RouteCompleted;

    private enum Phase { Idle, Opening, Holding, Closing, Dark }

    private SO_EscapeSequenceConfig config;
    private EscapeCorridorFlicker whiteLights;
    private VisionRangeController fog;
    private SO_VisionFogConfig closedRuntime;
    private SO_VisionFogConfig openRuntime;
    private bool openPushed;
    private bool closedPushed;

    private Phase phase = Phase.Idle;
    private int index;
    private float timer;
    private float power;

    public bool IsRunning { get; private set; }
    public int CurrentIndex => index;
    public int RouteLength => route.Length;

    /// <summary>How much power the white lights have right now, 0..1. 1 = on, fog open.</summary>
    public float Power => power;

    /// <summary>
    /// Starts the loop with the lights on and the fog open, which is how the corridor looks when
    /// the escape starts: the cinematic hands over under lit lamps, and the first close is the
    /// first thing the player sees the fog do.
    /// </summary>
    /// <param name="corridorLamps">The white lights the cycle powers. Optional.</param>
    public void Begin(SO_EscapeSequenceConfig escapeConfig, EscapeCorridorFlicker corridorLamps)
    {
        // A cycle that stopped at the gate still has its presets on the stack. Rebuilding them without
        // popping first would destroy assets the controller is still reading, and freeze the fog.
        ReleaseFog();

        config = escapeConfig;
        whiteLights = corridorLamps;

        fog = FindAnyObjectByType<VisionRangeController>();
        CreateRuntimeFog();

        // The dense fog rules the whole escape; the open preset goes on top of it while lit.
        if (fog != null && closedRuntime != null && !closedPushed)
        {
            fog.PushConfig(closedRuntime);
            closedPushed = true;
        }

        if (route.Length == 0)
            Debug.LogWarning($"[{nameof(EscapeFogCycle)}] The route is empty: no amber path and no " +
                             "gate arrival.", this);

        IsRunning = true;
        Restart();
    }

    /// <summary>Back to the first door, lights on. Also what a respawn does.</summary>
    public void Restart()
    {
        if (!IsRunning) return;

        index = 0;
        LightPath();
        EnterHolding();
    }

    /// <summary>Stops the loop: the path goes dark, the white lights get their power back, and the
    /// fog returns to what it was.</summary>
    public void End()
    {
        IsRunning = false;
        phase = Phase.Idle;

        TurnAllOff();
        SetPower(1f);
        whiteLights = null;
        ReleaseFog();
    }

    private void OnDestroy() => End();

    private void Update()
    {
        if (!IsRunning || config == null) return;

        SyncRuntimeFog();
        TickClock();
        TickArrival();
    }

    private void TickClock()
    {
        float dt = Time.deltaTime;

        switch (phase)
        {
            case Phase.Opening:
                SetPower(Mathf.MoveTowards(power, 1f, dt / config.OpenSeconds));
                if (power >= 1f) EnterHolding();
                break;

            case Phase.Holding:
                timer -= dt;
                if (timer <= 0f)
                {
                    phase = Phase.Closing;
                    SetFogOpen(false);
                }
                break;

            case Phase.Closing:
                SetPower(Mathf.MoveTowards(power, 0f, dt / config.CloseSeconds));
                if (power <= 0f)
                {
                    phase = Phase.Dark;
                    timer = config.DarkGapSeconds;
                }
                break;

            case Phase.Dark:
                timer -= dt;
                if (timer <= 0f)
                {
                    phase = Phase.Opening;
                    SetFogOpen(true);
                }
                break;
        }
    }

    private void EnterHolding()
    {
        phase = Phase.Holding;
        timer = config.HoldSeconds;
        SetPower(1f);
        SetFogOpen(true);
    }

    private void TickArrival()
    {
        if (index >= route.Length) return;

        Transform player = PlayerRegistry.CurrentTransform;
        EscapeGuideDoor door = route[index];
        if (player == null || door == null) return;

        if (HorizontalDistance(player.position, door.Position) > config.ArrivalRadius) return;

        int reached = index;
        index++;
        DoorReached?.Invoke(reached);

        // The gate. The listener decides what the arrival means; the cycle stops here and leaves the
        // path lit and the fog as it is — the Nemesis is still coming.
        if (index >= route.Length)
        {
            IsRunning = false;
            phase = Phase.Idle;
            RouteCompleted?.Invoke();
        }
    }

    private void SetPower(float value)
    {
        power = Mathf.Clamp01(value);
        if (whiteLights != null) whiteLights.Power = power;
    }

    private void LightPath()
    {
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] != null) route[i].Apply(config, 1f);
        }
    }

    private void TurnAllOff()
    {
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] != null) route[i].TurnOff();
        }
    }

    // ── Fog presets ─────────────────────────────────────────────────────────

    /// <summary>Takes both presets off the stack and destroys the runtime copies.</summary>
    private void ReleaseFog()
    {
        SetFogOpen(false);

        if (fog != null && closedPushed) fog.PopConfig(closedRuntime);
        closedPushed = false;

        DestroyRuntimeFog();
    }

    private void CreateRuntimeFog()
    {
        DestroyRuntimeFog();

        if (config.ClosedFog != null) closedRuntime = Instantiate(config.ClosedFog);
        if (config.OpenFog != null) openRuntime = Instantiate(config.OpenFog);
        SyncRuntimeFog();
    }

    private void DestroyRuntimeFog()
    {
        if (closedRuntime != null) Destroy(closedRuntime);
        if (openRuntime != null) Destroy(openRuntime);
        closedRuntime = null;
        openRuntime = null;
        openPushed = false;
    }

    /// <summary>The controller lerps at the speed of the config it is heading to, so the seconds
    /// are written into the presets: the open one carries the open seconds, the closed one the
    /// close seconds.</summary>
    private void SyncRuntimeFog()
    {
        if (openRuntime != null) openRuntime.transitionDuration = config.OpenSeconds;
        if (closedRuntime != null) closedRuntime.transitionDuration = config.CloseSeconds;
    }

    private void SetFogOpen(bool open)
    {
        if (fog == null || openRuntime == null || openPushed == open) return;

        if (open) fog.PushConfig(openRuntime);
        else fog.PopConfig(openRuntime);

        openPushed = open;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (route == null) return;

        Gizmos.color = new Color(1f, 0.72f, 0.38f, 0.9f);
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] == null) continue;

            UnityEditor.Handles.Label(route[i].Position + Vector3.up * 0.4f, $"{i + 1}");
            if (i > 0 && route[i - 1] != null) Gizmos.DrawLine(route[i - 1].Position, route[i].Position);
        }
    }
#endif
}
