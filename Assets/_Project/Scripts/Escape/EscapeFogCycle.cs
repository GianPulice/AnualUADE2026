using System;
using UnityEngine;

/// <summary>
/// The fog / guide-light loop of the escape (Paso 4). Its one job is WHEN: which door of the route
/// is the objective, and when its light opens the fog, holds, closes, and waits.
///
/// Per leg, on the current objective door:
///   1. The door lights — its beam opens the fog from the door towards the player (open seconds).
///   2. The light holds (hold seconds): the window to see and decide.
///   3. The light goes out and the fog closes (close seconds).
///   4. A dark gap, then the same door lights again — until the player reaches it (arrival radius).
///      Then the next door of the route repeats the cycle.
/// Nothing scales with progress: every number is the same from the first door to the gate.
///
/// The optional fog presets (<see cref="SO_EscapeSequenceConfig.ClosedFog"/> and
/// <see cref="SO_EscapeSequenceConfig.OpenFog"/>) are pushed onto the <see cref="VisionRangeController"/>'s
/// stack as runtime copies, with the open / close seconds written into their transition time. That
/// is what makes the seconds in the config the ONLY place the lerp durations live.
///
/// Reaching the last door of the route raises <see cref="RouteCompleted"/>; what happens then is
/// not this component's business.
/// </summary>
public class EscapeFogCycle : MonoBehaviour
{
    [Tooltip("Las puertas del recorrido, EN ORDEN: la primera en encenderse primero, la última es " +
             "el portón. Cada una lleva un EscapeGuideDoor.")]
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
    private int index;
    private float timer;

    // The door that was the objective before the player crossed it, still fading out.
    private EscapeGuideDoor fading;

    public bool IsRunning { get; private set; }
    public int CurrentIndex => index;
    public int RouteLength => route.Length;

    /// <summary>Starts the loop at the first door.</summary>
    public void Begin(SO_EscapeSequenceConfig escapeConfig)
    {
        config = escapeConfig;
        if (route.Length == 0)
        {
            Debug.LogWarning($"[{nameof(EscapeFogCycle)}] The route is empty: no guide lights.", this);
            return;
        }

        fog = FindAnyObjectByType<VisionRangeController>();
        CreateRuntimeFog();

        // The dense fog rules the whole escape, between light pulses.
        if (fog != null && closedRuntime != null && !closedPushed)
        {
            fog.PushConfig(closedRuntime);
            closedPushed = true;
        }

        IsRunning = true;
        Restart();
    }

    /// <summary>Back to the first door, everything closed. Also what a respawn does.</summary>
    public void Restart()
    {
        if (!IsRunning) return;

        TurnAllOff();
        SetFogOpen(false);

        index = 0;
        fading = null;
        phase = Phase.Dark;
        timer = config.DarkGapSeconds;
    }

    /// <summary>Stops the loop: every light out, the fog back to what it was.</summary>
    public void End()
    {
        IsRunning = false;
        phase = Phase.Idle;

        TurnAllOff();
        SetFogOpen(false);

        if (fog != null && closedPushed) fog.PopConfig(closedRuntime);
        closedPushed = false;

        DestroyRuntimeFog();
    }

    private void OnDestroy() => End();

    private void Update()
    {
        if (!IsRunning || config == null) return;

        SyncRuntimeFog();
        TickFading();

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return;

        EscapeGuideDoor door = route[index];
        Vector3 playerPos = player.position;

        if (HorizontalDistance(playerPos, door.Position) <= config.ArrivalRadius)
        {
            HandleArrival(door);
            return;
        }

        switch (phase)
        {
            case Phase.Opening:
                door.Apply(config, Mathf.MoveTowards(door.Lit, 1f, Time.deltaTime / config.OpenSeconds), playerPos);
                if (door.Lit >= 1f)
                {
                    phase = Phase.Holding;
                    timer = config.HoldSeconds;
                }
                break;

            case Phase.Holding:
                door.Apply(config, 1f, playerPos);
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    phase = Phase.Closing;
                    SetFogOpen(false);
                }
                break;

            case Phase.Closing:
                door.Apply(config, Mathf.MoveTowards(door.Lit, 0f, Time.deltaTime / config.CloseSeconds), playerPos);
                if (door.Lit <= 0f)
                {
                    phase = Phase.Dark;
                    timer = config.DarkGapSeconds;
                }
                break;

            case Phase.Dark:
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    phase = Phase.Opening;
                    SetFogOpen(true);
                }
                break;
        }
    }

    private void HandleArrival(EscapeGuideDoor door)
    {
        int reached = index;
        DoorReached?.Invoke(reached);

        if (reached >= route.Length - 1)
        {
            // The gate. Everything goes dark; the listener decides what the arrival means.
            IsRunning = false;
            phase = Phase.Idle;
            TurnAllOff();
            SetFogOpen(false);
            RouteCompleted?.Invoke();
            return;
        }

        // Crossed a door: its light fades out on its own while the next door waits its dark gap.
        fading = door;
        SetFogOpen(false);

        index = reached + 1;
        phase = Phase.Dark;
        timer = config.DarkGapSeconds;
    }

    private void TickFading()
    {
        if (fading == null) return;

        Transform player = PlayerRegistry.CurrentTransform;
        Vector3 towards = player != null ? player.position : fading.Position;

        fading.Apply(config, Mathf.MoveTowards(fading.Lit, 0f, Time.deltaTime / config.CloseSeconds), towards);
        if (fading.Lit <= 0f) fading = null;
    }

    private void TurnAllOff()
    {
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] != null) route[i].TurnOff();
        }
    }

    // ── Fog presets ─────────────────────────────────────────────────────────

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
