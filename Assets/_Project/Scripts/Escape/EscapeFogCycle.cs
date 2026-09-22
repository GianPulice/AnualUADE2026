using System;
using UnityEngine;

/// <summary>
/// The fog of the escape (Pasos 4-5), and the amber path through it. Its one job is WHEN: when the
/// fog expands and when it contracts again, and which door of the route the player has reached.
///
/// The loop, on one clock for the whole corridor:
///   1. Opening — the fog expands (open seconds, lerped): the corridor shows ahead.
///   2. Holding — fog open (hold seconds): the level is visible, and so is the Nemesis.
///   3. Closing — the fog contracts back to its dense preset (close seconds).
///   4. Dark    — fog closed (dark gap seconds). Only the amber lights of the path show through it.
/// Nothing scales with progress: every number is the same from the first door to the gate.
///
/// The clock does not start with the fog. <see cref="Begin"/> brings the dense fog in with the clock
/// stopped — the reveal: the fog rolls in and the Nemesis's eyes show through it, and
/// <see cref="Hold"/> opens it as the Nemesis charges out of it — and <see cref="Run"/> starts the
/// breathing when control comes back.
///
/// The corridor's white lamps are NOT this component's: they flicker on their own
/// (<see cref="EscapeCorridorFlicker"/>) whatever the fog does. They used to be powered from here,
/// dying every time the fog closed (WIR-038); the fog is what breathes now.
///
/// The amber lights (<see cref="EscapeGuideDoor"/>) are fixed and stay on for the whole escape
/// when the config's <see cref="SO_EscapeSequenceConfig.GuideLightsEnabled"/> asks for them. Off by
/// default: the corridor's sirens (<see cref="EscapeAlarmLights"/>) are what pierces the closed fog
/// now (WIR-039 was about the path showing through it).
///
/// The fog presets (<see cref="SO_EscapeSequenceConfig.ClosedFog"/> and
/// <see cref="SO_EscapeSequenceConfig.OpenFog"/>) are pushed onto the <see cref="VisionRangeController"/>'s
/// stack as runtime copies, with the open / close seconds written into their transition time. That
/// is what makes the seconds in the config the ONLY place the lerp durations live.
///
/// Reaching the last door of the route raises <see cref="RouteCompleted"/> and stops the clock with
/// the fog as it is; what happens then is not this component's business.
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
    private VisionRangeController fog;
    private SO_VisionFogConfig closedRuntime;
    private SO_VisionFogConfig openRuntime;
    private bool openPushed;
    private bool closedPushed;

    private Phase phase = Phase.Idle;
    private bool clockRunning;
    private int index;
    private float timer;

    /// <summary>The escape's fog is on the stack (from <see cref="Begin"/> to <see cref="End"/>).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>The fog is breathing (<see cref="Run"/> was called and nothing stopped it since).</summary>
    public bool IsCycling => IsRunning && clockRunning;

    public int CurrentIndex => index;
    public int RouteLength => route.Length;

    /// <summary>The open preset is on top right now (expanding or held open).</summary>
    public bool IsFogOpen => openPushed;

    /// <summary>
    /// Brings the escape's dense fog in and lights the amber path, with the clock stopped: the fog
    /// stays closed until <see cref="Run"/>. The reveal calls it as its shot opens, so the fog is
    /// still thickening around the Nemesis while it stands in it.
    /// </summary>
    public void Begin(SO_EscapeSequenceConfig escapeConfig)
    {
        // A cycle that ran before (the test key) still has its presets on the stack. Rebuilding
        // them without popping first would destroy assets the controller is still reading, and
        // freeze the fog.
        ReleaseFog();

        config = escapeConfig;

        fog = FindAnyObjectByType<VisionRangeController>();
        CreateRuntimeFog();

        // The dense fog rules the whole escape; the open preset goes on top of it while open.
        if (fog != null && closedRuntime != null && !closedPushed)
        {
            fog.PushConfig(closedRuntime);
            closedPushed = true;
        }

        if (route.Length == 0)
            Debug.LogWarning($"[{nameof(EscapeFogCycle)}] The route is empty: no amber path and no " +
                             "gate arrival.", this);

        IsRunning = true;
        clockRunning = false;
        index = 0;
        LightPath();
        EnterDark();
    }

    /// <summary>Starts the breathing with the fog expanding (or, if a <see cref="Hold"/> already opened
    /// it, staying open for the open seconds before the hold). Idempotent.</summary>
    public void Run()
    {
        if (!IsRunning || clockRunning) return;

        clockRunning = true;
        EnterOpening();
    }

    /// <summary>Back to the first door with the fog closed, and the clock running: what a chase that
    /// starts over after a capture looks like.</summary>
    public void Restart()
    {
        if (!IsRunning) return;

        index = 0;
        LightPath();
        clockRunning = true;
        EnterDark();
    }

    /// <summary>Stops the breathing and keeps the fog open or closed until the next <see cref="Run"/>
    /// (the reveal opens it as the Nemesis charges; the last shot needs to see the gate). Works after
    /// the route is complete too.</summary>
    public void Hold(bool open)
    {
        if (!IsRunning) return;

        clockRunning = false;
        phase = open ? Phase.Holding : Phase.Dark;
        SetFogOpen(open);
    }

    /// <summary>Stops everything: the path goes dark and the fog returns to what it was.</summary>
    public void End()
    {
        IsRunning = false;
        clockRunning = false;
        phase = Phase.Idle;

        TurnAllOff();
        ReleaseFog();
    }

    private void OnDestroy() => End();

    private void Update()
    {
        if (!IsRunning || config == null) return;

        SyncRuntimeFog();
        if (clockRunning) TickClock();
        TickArrival();
    }

    private void TickClock()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        switch (phase)
        {
            case Phase.Opening: EnterHolding(); break;
            case Phase.Holding: EnterClosing(); break;
            case Phase.Closing: EnterDark(); break;
            case Phase.Dark: EnterOpening(); break;
        }
    }

    // The lerps themselves are the fog controller's (the presets carry the seconds, see
    // SyncRuntimeFog): each phase only pushes or pops the open preset and waits its time out.

    private void EnterOpening()
    {
        phase = Phase.Opening;
        timer = config.OpenSeconds;
        SetFogOpen(true);
    }

    private void EnterHolding()
    {
        phase = Phase.Holding;
        timer = config.HoldSeconds;
    }

    private void EnterClosing()
    {
        phase = Phase.Closing;
        timer = config.CloseSeconds;
        SetFogOpen(false);
    }

    private void EnterDark()
    {
        phase = Phase.Dark;
        timer = config.DarkGapSeconds;
        SetFogOpen(false);
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

        // The gate. The listener decides what the arrival means; the clock stops here and leaves
        // the path lit and the fog as it is — the Nemesis is still coming.
        if (index >= route.Length)
        {
            clockRunning = false;
            RouteCompleted?.Invoke();
        }
    }

    // Off unless the config asks for them: the corridor's sirens mark the way now. The route still
    // counts the doors reached either way; only the lamps stay dark (and use no fog slots).
    private void LightPath()
    {
        float lit = config != null && config.GuideLightsEnabled ? 1f : 0f;
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] != null) route[i].Apply(config, lit);
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
