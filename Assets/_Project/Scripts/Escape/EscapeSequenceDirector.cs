using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Runs the escape sequence from the third core to the gate. It is the conductor and nothing else:
/// it decides WHEN each piece starts, and every piece does its own work (<see cref="EscapeFogCycle"/>,
/// <see cref="EscapeAlarmLights"/>, <see cref="NemesisCinematicActor"/>,
/// <see cref="NemesisEscapePursuit"/>, <see cref="EscapeShotCamera"/>,
/// <see cref="EscapeChaseRestart"/>, <see cref="EscapeGateSlam"/>, <see cref="EscapeAudio"/>,
/// <see cref="EscapeCorridorLock"/>).
///
///   Armed      the hub puzzle (the three cores) completes. Nothing cuts: the player keeps playing.
///              The hub's other exits shut and lock, so the centre door is the only way out. The
///              cores are left as they are (green): nothing on the sockets changes.
///   Slam       a few steps out into the corridor (the Stage's reveal trigger, and clear of the
///              centre door's swing): hard cut to the wide shot, and every open door slams shut on
///              the same frame — the centre door behind the player too, there is no going back —
///              and every door locks. The dense fog rolls in. The Nemesis is put at the far end of
///              the corridor with its eyes shut. A fixed hold of silence.
///   Reveal     the same shot pans round to the far end of the corridor; cut to the detail of the
///              Nemesis's eyes opening in the dark (the fog eats the body); cut to the wide shot
///              over the player's shoulder as it charges, and once it has covered a few metres,
///              cut to the player's own view, looking at it: control comes back and the chase is on.
///   Escape     gameplay (Pasos 4-7): the corridor's lamps turn into sirens, red and amber taking
///              turns, the alarm sounds, the fog expands and contracts, the Nemesis chases the whole
///              way. A capture is a game over, or, per the config, starts the chase over from
///              Player_Spot.
///   Ending     crossing the level's WinTrigger, past the gate, plays the last shots before the
///              win: the Nemesis runs at the gate and it slams down in its face in a burst of dust,
///              in a fog that shows nothing past the gate; then the Nemesis, stuck on the corridor
///              side, through the dust. The player is not in the shots. The director is the
///              <see cref="IWinPresenter"/> for as long as the chase lasts.
///
/// Every cinematic can be cut with the config's skip key, and a skip lands in the state the
/// cinematic ends in: the slam and the reveal hand over with the doors locked, the Nemesis where
/// its charge would have taken it (never nearer) and the player looking at it; the ending shuts
/// the gate.
///
/// The test key (<see cref="StartForTest"/>) puts the player inside the hub, in front of the
/// centre door, and arms the escape as if the last core had just gone in.
///
/// What is NOT covered: resuming the escape from a saved game (if the trigger puzzle is already
/// complete when the scene loads, nothing plays), and the floor / wall fluid marks of the path
/// (art).
///
/// SETUP: everything hangs off the EscapeSequence object in Zona1 (its Stage, Cameras and Route
/// children, GateDust). The hub's other exits are found at runtime around the trigger puzzle's
/// sockets rather than listed: a list can go stale.
/// </summary>
public class EscapeSequenceDirector : MonoBehaviour, IWinPresenter
{
    /// <summary>The scene objects the escape acts on. The config says WHAT happens and when; these
    /// say WHERE.</summary>
    [Serializable]
    public class Stage
    {
        [Header("Player")]
        [Tooltip("Donde vuelve a arrancar la persecución si el Nemesis te agarra (con Restart " +
                 "Chase), mirando hacia el portón. Parado en la línea del medio del pasillo: " +
                 "también marca dónde está esa línea.")]
        public Transform playerSpot;

        [Tooltip("Donde queda el jugador en el plano del portazo (ver 'Snap Player To Mark' en el " +
                 "config): en el pasillo, pasando la puerta del centro y fuera del barrido de su " +
                 "hoja. Vacío = queda donde estaba.")]
        public Transform playerCorridorMark;

        [Tooltip("El checkpoint del reinicio (con Restart Chase): un Checkpoint en Player_Spot que " +
                 "no se activa solo (modo Puzzle Completed, sin puzzles). El director lo activa al " +
                 "arrancar la persecución.")]
        public Checkpoint chaseCheckpoint;

        [Tooltip("La puerta del centro: la única salida del hub después del último núcleo. Se " +
                 "cierra de golpe y se traba atrás del jugador cuando sale al pasillo (no hay " +
                 "vuelta a la zona segura). NO va en la lista de EscapeCorridorLock.")]
        public DoorInteractable safeDoor;

        [Tooltip("La zona del pasillo, pasando la puerta del centro, que dispara la cinemática. " +
                 "Arranca apenas el jugador, ya adentro, está fuera del barrido de la hoja de la " +
                 "puerta (que se cierra de golpe atrás suyo).")]
        public EscapeRevealTrigger revealTrigger;

        [Tooltip("El portón del final del pasillo. Se abre durante la persecución y termina justo " +
                 "cuando llegarías corriendo (ver 'End Gate Slack Seconds' en el config), y cae de " +
                 "golpe en el plano final. Necesita 'Opens On Puzzle Completed' apagado, o su puzzle " +
                 "lo abre antes.")]
        public PuzzleGate endGate;

        [Header("Nemesis")]
        [Tooltip("Al fondo del pasillo, en la oscuridad, mirando hacia el jugador: acá lo pone el " +
                 "portazo, y de acá arranca la carga.")]
        public Transform nemesisCorridorEnd;

        [Tooltip("Donde vuelve a arrancar si te agarró (con Restart Chase): detrás de Player_Spot, " +
                 "a la distancia a la que empieza la persecución.")]
        public Transform nemesisApproachStart;

        [Header("Nemesis (final)")]
        [Tooltip("Desde dónde corre hacia el portón en el plano final, del lado del pasillo.")]
        public Transform nemesisGateStart;

        [Tooltip("Donde queda trabado: pegado al portón, del lado del pasillo.")]
        public Transform nemesisGateStop;
    }

    [SerializeField] private SO_EscapeSequenceConfig config;

    [Header("Cinematic cameras")]
    [Tooltip("El plano general del portazo, en el pasillo: el jugador y la puerta del centro " +
             "cerrándose detrás. Después gira (paneo) hacia el fondo del pasillo. Se ubica a mano " +
             "en la escena; el sentido del giro está en el config.")]
    [SerializeField] private EscapeShotCamera slamShot;

    [Tooltip("El detalle de los ojos del Nemesis: teleobjetivo (FOV chico) a unos metros, para que " +
             "la niebla se coma el cuerpo y queden sólo los ojos. Vacío = se saltea.")]
    [SerializeField] private EscapeShotCamera eyesShot;

    [Tooltip("El plano general de la carga: sobre el hombro del jugador, mirando al fondo del " +
             "pasillo. Vacío = la carga sigue en el plano que esté al aire.")]
    [SerializeField] private EscapeShotCamera chargeShot;

    [Tooltip("El plano final: el portón desde el lado del pasillo, cerca, así la niebla del plano " +
             "(config) no deja ver nada del otro lado. El jugador no se ve (queda oculto).")]
    [SerializeField] private EscapeShotCamera gateShot;

    [Tooltip("El plano medio del Nemesis frente al portón cerrado, entre el polvo. Vacío = queda " +
             "el plano del portón.")]
    [SerializeField] private EscapeShotCamera dustShot;

    [Header("Scene pieces")]
    [SerializeField] private Stage stage = new Stage();
    [SerializeField] private EscapeFogCycle fogCycle;

    [Tooltip("Las sirenas del pasillo: rojo y ámbar alternando desde que vuelve el control hasta " +
             "el final del escape. Se ven a través de la niebla.")]
    [SerializeField] private EscapeAlarmLights alarmLights;
    [SerializeField] private EscapeAudio escapeAudio;
    [SerializeField] private EscapeCorridorLock corridorLock;
    [SerializeField] private NemesisCinematicActor actor;
    [SerializeField] private NemesisEscapePursuit pursuit;

    [Tooltip("Qué pasa si el Nemesis te agarra en la persecución: game over o reinicio desde " +
             "Player_Spot (lo elige el config).")]
    [FormerlySerializedAs("captureGameOver")]
    [SerializeField] private EscapeChaseRestart chaseRestart;

    [Tooltip("El portón cayendo en el plano final, con su polvo.")]
    [SerializeField] private EscapeGateSlam gateSlam;

    [Tooltip("Los tres sockets de los núcleos. Vacío = se buscan solos en la escena. Sólo se usan " +
             "para ubicar el hub (sus otras salidas): no se tocan.")]
    [SerializeField] private SocketInteractable[] sockets = new SocketInteractable[0];

    [Header("Gate trigger")]
    [Tooltip("Se dispara cuando el jugador llega a la última puerta del recorrido (el portón). No " +
             "termina nada: el Nemesis sigue persiguiendo hasta el WinTrigger. Colgale acá lo que " +
             "quieras.")]
    [SerializeField] private UnityEvent onGateReached = new UnityEvent();

    /// <summary>The player reached the last door of the route (the gate).</summary>
    public event Action GateReached;

    private enum Phase { Idle, Armed, Reveal, Escape, Ending, Done }

    private Phase phase = Phase.Idle;
    private bool skipRequested;

    // The player has been in the reveal trigger; the slam waits only for them to be clear of the
    // centre door's swing, wherever they walked on to.
    private bool revealArmed;

    // Clearance kept between the player and the centre door's leaf as it slams: the capsule, and
    // some air.
    private const float SwingClearance = 0.45f;

    // Where the test key puts the player: inside the hub, this far from the centre door.
    private const float TestStartDistance = 1.8f;

    // How far past the centre door's closed leaf (its wall line) the player must be to count as out
    // in the corridor: about a capsule. A player hugging the corridor's near wall still counts.
    private const float CorridorSideMargin = 0.3f;

    // The height the pan aims at the Nemesis: its eyes, not its feet.
    private const float NemesisEyeHeight = 1.7f;

    // The hub's other exits: every door this close to the trigger puzzle's sockets, on their floor,
    // other than the centre door. Sealed when the last core goes in, so the centre door is the
    // only way out.
    private const float HubExitRadius = 12f;
    private const float HubSameFloor = 2.5f;
    private readonly List<DoorInteractable> hubExits = new List<DoorInteractable>();
    private bool hubExitsSealed;

    // The test key pressed while a capture is being resolved: run once it is over (see StartForTest).
    private bool testStartPending;

    private PlayerStateManager lockedPlayer;
    private CancellationTokenSource endGateCts;
    private CancellationTokenSource sequenceCts;

    private CinemachineBrain brain;
    private CinemachineBlendDefinition previousBlend;
    private bool brainOverridden;
    private Coroutine blendRestore;

    private VisionRangeController fogCentre;
    private Transform fogCentreCamera;

    // A shot's own fog (the config's reveal or gate one), while it is on the stack.
    private VisionRangeController shotFogController;
    private SO_VisionFogConfig shotFogPushed;

    // The centre door's doorway, measured on the scene's closed door: the open leaf would drag the
    // bounds out into the corridor.
    private Vector3 safeDoorCentre;

    // And where its leaf pivots and how far it reaches, for the swing check. Both hold for the
    // door open or shut: the leaf turns about the hinge.
    private Vector3 safeDoorHinge;
    private float safeDoorReach;

    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private bool moduleTicksPaused;

    /// <summary>A cinematic of the escape is on screen (from the slam to the handover, or the
    /// ending).</summary>
    public bool IsPlaying => phase == Phase.Reveal || phase == Phase.Ending;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    // Static events: subscribed in Awake and released in OnDestroy, like the rest of the project.
    private void Awake()
    {
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
        GameResultManager.OnGameResult += HandleGameResult;

        if (chaseRestart != null)
        {
            chaseRestart.Respawned += HandleChaseRespawned;
            chaseRestart.NemesisFreed += HandleChaseNemesisFreed;
            chaseRestart.ControlRegained += HandleChaseControlRegained;
        }
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        GameResultManager.OnGameResult -= HandleGameResult;

        if (chaseRestart != null)
        {
            chaseRestart.Respawned -= HandleChaseRespawned;
            chaseRestart.NemesisFreed -= HandleChaseNemesisFreed;
            chaseRestart.ControlRegained -= HandleChaseControlRegained;
        }

        if (fogCycle != null) fogCycle.RouteCompleted -= HandleRouteCompleted;

        CancelEndGate();
        CancelSequence();
        UnregisterWinPresenter();

        // No coroutines here: the object is going away. Whatever the cinematic held is given back now.
        CinematicState.End();
        ReleaseFogCentre();
        PopShotFog();
        ShowPlayer();
        ResumeModuleTicks();
        if (lockedPlayer != null) lockedPlayer.IsDisabled = false;
        if ((brainOverridden || blendRestore != null) && brain != null) brain.DefaultBlend = previousBlend;
    }

    private void Start()
    {
        if (config == null)
        {
            Debug.LogError($"[{nameof(EscapeSequenceDirector)}] No config assigned: the escape " +
                           "sequence will not run.", this);
            enabled = false;
            return;
        }

        if (stage.safeDoor != null)
        {
            safeDoorCentre = RendererCentre(stage.safeDoor.gameObject);
            safeDoorHinge = stage.safeDoor.HingePosition;
            safeDoorReach = stage.safeDoor.SwingReach;
        }

        // Only the sockets of the trigger puzzle: the fuse box and any other socket in the level do
        // not mark where the hub is.
        if (sockets == null || sockets.Length == 0)
            sockets = FindSocketsOfTriggerPuzzle();

        FindHubExits();

        // The sequence already ran (saved game): it does not replay. See the class doc.
        if (PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(config.TriggerPuzzleId))
        {
            phase = Phase.Done;
        }
    }

    private SocketInteractable[] FindSocketsOfTriggerPuzzle()
    {
        List<SocketInteractable> found = new List<SocketInteractable>();

        foreach (SocketInteractable socket in FindObjectsByType<SocketInteractable>(FindObjectsInactive.Exclude))
        {
            if (socket.LinkedPuzzleId == config.TriggerPuzzleId) found.Add(socket);
        }

        return found.ToArray();
    }

    /// <summary>
    /// Every door around the hub other than the centre one: on the sockets' floor, within
    /// <see cref="HubExitRadius"/> of them, and not already in the corridor lock's list. They are
    /// sealed when the last core goes in (<see cref="SealHubExits"/>), so the centre door is the
    /// only way out. Found here rather than listed by hand because a list can go stale, and one
    /// forgotten exit was enough for the player to leave the hub another way and never meet the
    /// Nemesis.
    /// </summary>
    private void FindHubExits()
    {
        hubExits.Clear();
        if (sockets == null || sockets.Length == 0) return;

        Vector3 hub = Vector3.zero;
        int count = 0;
        foreach (SocketInteractable socket in sockets)
        {
            if (socket == null) continue;
            hub += socket.transform.position;
            count++;
        }
        if (count == 0) return;
        hub /= count;

        foreach (DoorInteractable door in FindObjectsByType<DoorInteractable>(FindObjectsInactive.Include))
        {
            if (door == stage.safeDoor) continue;
            if (corridorLock != null && corridorLock.Lists(door)) continue;

            Vector3 offset = door.transform.position - hub;
            if (Mathf.Abs(offset.y) > HubSameFloor) continue;
            offset.y = 0f;
            if (offset.magnitude > HubExitRadius) continue;

            hubExits.Add(door);
        }

        if (hubExits.Count > 0)
            Debug.Log($"[{nameof(EscapeSequenceDirector)}] The last core also seals the hub's other " +
                      $"exits: {string.Join(", ", hubExits.ConvertAll(d => d.name))}.", this);
    }

    /// <summary>Seals the hub's other exits, once per run, shutting any that is open (a normal
    /// close: the player is still playing, and the slam is kept for the corridor).</summary>
    private void SealHubExits(bool quiet)
    {
        if (hubExitsSealed || corridorLock == null) return;
        hubExitsSealed = true;

        foreach (DoorInteractable door in hubExits) corridorLock.LockDoor(door, config, quiet);
    }

    /// <summary>
    /// Arms the sequence from the top as if the third core had just gone in, without solving the
    /// puzzle. For <see cref="EscapeSequenceTestKey"/>: it also works mid-chase or after the escape
    /// ended without a result screen (a result screen blocks the key; use its own buttons),
    /// tearing down whatever the previous one left (the escape's systems, the locked doors, the
    /// gate). The player is put inside the hub, in front of the centre door, wherever they were —
    /// the sequence assumes it starts there. Ignored while a cinematic is playing.
    ///
    /// Pressed while a capture is being resolved (the grab, the black screen, the stand-up), it
    /// waits for the capture to finish: taking the Nemesis in the middle of it froze its Catch state,
    /// and the screen never came back from black.
    /// </summary>
    public void StartForTest()
    {
        if (config == null || IsPlaying) return;

        if (CaptureInProgress())
        {
            testStartPending = true;
            return;
        }
        testStartPending = false;

        ResetForReplay();
        PlacePlayerForTest();
        ArmEscape(quiet: true);
    }

    /// <summary>A capture is under way: the player grabbed, on the black screen or getting up, or the
    /// Nemesis in its own Catch state (a pull-out from a hiding spot starts before the grab).</summary>
    private bool CaptureInProgress()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player != null && player.IsRecoveringFromCapture) return true;
        return actor != null && actor.IsNemesisCatching;
    }

    private void ResetForReplay()
    {
        CancelSequence();
        CancelEndGate();
        EndEscapeSystems();
        UnregisterWinPresenter();
        revealArmed = false;
        hubExitsSealed = false;

        // UnlockAll gives the safe door and the hub's exits back too: they were sealed through the
        // same lock.
        if (corridorLock != null) corridorLock.UnlockAll();
        if (stage.endGate != null) stage.endGate.CloseImmediate();
        if (gateSlam != null) gateSlam.ResetDust();
        ReleaseShots();
        PopShotFog();

        ShowPlayer();
        ResumeModuleTicks();
    }

    /// <summary>Inside the hub, <see cref="TestStartDistance"/> from the centre door and facing it:
    /// where a real run is when the last core goes in.</summary>
    private void PlacePlayerForTest()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null || stage.safeDoor == null || stage.playerSpot == null) return;

        Vector3 door = safeDoorCentre;
        Vector3 toCorridor = CorridorSideOf(door);
        if (toCorridor.sqrMagnitude < 0.0001f) return;

        Vector3 position = door - toCorridor * TestStartDistance;
        position.y = stage.playerSpot.position.y;

        player.TeleportTo(position, Quaternion.LookRotation(toCorridor));

        PlayerCameraController look = FindAnyObjectByType<PlayerCameraController>();
        if (look != null) look.FaceYaw(Quaternion.LookRotation(toCorridor).eulerAngles.y);
    }

    /// <summary>From a point by the corridor's side wall (the centre door), the flat direction
    /// straight across into the corridor: towards its centre line, which Player_Spot stands on.</summary>
    private Vector3 CorridorSideOf(Vector3 point)
    {
        if (stage.playerSpot == null) return Vector3.zero;

        Vector3 axis = stage.endGate != null
            ? stage.endGate.transform.position - stage.playerSpot.position
            : stage.playerSpot.forward;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.0001f) return Vector3.zero;
        axis.Normalize();

        Vector3 across = stage.playerSpot.position - point;
        across.y = 0f;
        across -= Vector3.Project(across, axis);
        return across.sqrMagnitude > 0.0001f ? across.normalized : Vector3.zero;
    }

    private static Vector3 RendererCentre(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return go.transform.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    private void Update()
    {
        // A test start held back by a capture, now that it is over.
        if (testStartPending && !IsPlaying && !CaptureInProgress()) StartForTest();

        if (phase == Phase.Armed)
        {
            if (ReadyForReveal()) StartReveal();
            return;
        }

        if (IsPlaying && SkipPressed()) skipRequested = true;
    }

    // Legacy input, like the wake-up cinematic: the project runs both input backends.
    private bool SkipPressed()
    {
        if (!config.Skippable) return false;
        if (PauseManager.IsGameplayInputBlocked) return false;
        if (LoadingScreen.IsLoading) return false;

        return Input.GetKeyDown(config.SkipKey);
    }

    // ── Trigger ─────────────────────────────────────────────────────────────

    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (phase != Phase.Idle || config == null || puzzleId != config.TriggerPuzzleId) return;

        ArmEscape(quiet: false);
    }

    /// <summary>
    /// The last core is in. Nothing cuts and nothing on the sockets changes, but the room answers:
    /// every door of the hub shuts and locks, the centre one included, and a moment later that one
    /// alone opens again. The player keeps playing, and the only way on is the corridor behind it,
    /// where the cinematic waits (<see cref="ReadyForReveal"/>).
    /// </summary>
    private void ArmEscape(bool quiet)
    {
        phase = Phase.Armed;
        revealArmed = false;

        SealHubExits(quiet);

        // The centre door goes with them, and comes back on its own (OpenSafeDoorAfterAsync).
        if (stage.safeDoor != null && corridorLock != null)
            corridorLock.LockDoor(stage.safeDoor, config, quiet);

        // Asleep until the slam in a normal run. Awake (the test key, after a chase), it must not
        // hunt the player on the way out of the hub: it waits where the slam would put it, eyes shut.
        if (actor != null && actor.IsNemesisAwake && actor.TryTakeControl())
        {
            actor.WarpTo(stage.nemesisCorridorEnd);
            actor.SetEyesVisible(false);
        }

        if (stage.revealTrigger == null)
            Debug.LogWarning($"[{nameof(EscapeSequenceDirector)}] No reveal trigger on the Stage: the " +
                             "cinematic starts as soon as the player is out of the safe room. Add an " +
                             "EscapeRevealTrigger just past the centre door.", this);

        OpenSafeDoorAfterAsync(config.SafeDoorOpensAfter, NewSequenceToken()).Forget();
    }

    /// <summary>
    /// The lock-down holds for a moment and then the centre door opens by itself: the way out,
    /// named. It waits out the door's own closing swing first — a door mid-swing refuses to open —
    /// and unseals it, since the lock-down had just sealed it with the rest.
    /// </summary>
    private async UniTaskVoid OpenSafeDoorAfterAsync(float seconds, CancellationToken token)
    {
        if (seconds > 0f) await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: token);

        DoorInteractable door = stage.safeDoor;
        if (phase != Phase.Armed || door == null) return;

        float guard = 0f;
        while (door.IsAnimating && guard < 3f)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            guard += Time.deltaTime;
        }

        if (phase != Phase.Armed) return;

        door.SetSequenceLocked(false);
        if (!door.IsOpen) door.OpenDoor();
    }

    // ── The slam and the reveal ─────────────────────────────────────────────

    /// <summary>
    /// Whether the cinematic can start this frame. The player has been in the reveal trigger (that
    /// arms it, once), and now: is out in the corridor, and is clear of the centre door's swing —
    /// the door slams behind them as it starts, and must not sweep its leaf through them. Armed and
    /// not ready, the player walks on with control until they are; wherever that is.
    /// </summary>
    private bool ReadyForReveal()
    {
        // Not under a menu: the cinematic would play frozen behind it.
        if (PauseManager.IsGameplayInputBlocked) return false;

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return false;

        if (!revealArmed)
            revealArmed = stage.revealTrigger != null
                ? stage.revealTrigger.Contains(player.position)
                : IsOutOfSafeRoom(player.position);

        // Out in the corridor NOW, not only when it was armed: a player who stepped out and back into
        // the hub would otherwise be sealed in the safe zone by the slam meant to shut them out.
        return revealArmed && OnCorridorSide(player.position) && ClearOfSafeDoorSwing(player.position);
    }

    /// <summary>Past the centre door's wall line, on the corridor side, by
    /// <see cref="CorridorSideMargin"/>. Measured from the hinge, which stands in that line whether
    /// the door is open or shut.</summary>
    private bool OnCorridorSide(Vector3 point)
    {
        if (stage.safeDoor == null) return true;

        Vector3 toCorridor = CorridorSideOf(safeDoorCentre);
        if (toCorridor.sqrMagnitude < 0.0001f) return true;

        Vector3 offset = point - safeDoorHinge;
        offset.y = 0f;
        return Vector3.Dot(offset, toCorridor) > CorridorSideMargin;
    }

    /// <summary>On the corridor side of the centre door, a step past it. Only the net for a Stage
    /// with no reveal trigger.</summary>
    private bool IsOutOfSafeRoom(Vector3 point)
    {
        Vector3 toCorridor = CorridorSideOf(safeDoorCentre);
        if (toCorridor.sqrMagnitude < 0.0001f) return true;

        Vector3 offset = point - safeDoorCentre;
        offset.y = 0f;
        return Vector3.Dot(offset, toCorridor) > 1f;
    }

    /// <summary>The centre door can shut without its leaf going through a player standing here.</summary>
    private bool ClearOfSafeDoorSwing(Vector3 point)
    {
        DoorInteractable door = stage.safeDoor;
        if (door == null || (!door.IsOpen && !door.IsAnimating)) return true;

        Vector3 gap = point - safeDoorHinge;
        gap.y = 0f;
        return gap.magnitude > safeDoorReach + SwingClearance;
    }

    private void StartReveal()
    {
        phase = Phase.Reveal;
        skipRequested = false;
        RunRevealAsync(NewSequenceToken()).Forget();
    }

    /// <summary>
    /// From the slam to the handover. Cut to the wide shot of the corridor: every open door slams
    /// shut at once, the centre door behind the player among them, and everything locks; the dense
    /// fog rolls in, and the Nemesis stands at the far end, a shape in the dark with its eyes shut.
    /// A hold of silence. The shot pans round to the far end; cut to the eyes opening; cut to the
    /// wide shot over the player's shoulder as it charges; once it has covered the config's
    /// distance, cut to the player's own view, turned its way on the frame of the cut (the turn is
    /// never seen): control comes back and the chase is on. At the time cap whatever happens.
    /// </summary>
    private async UniTaskVoid RunRevealAsync(CancellationToken token)
    {
        PlayerStateManager player = PlayerRegistry.Current;

        BeginCinematic();
        CutTo(slamShot);

        // A short step at most (the trigger is just past the door), hidden by the cut: the wide
        // shot frames the player the same every time. Their facing is kept.
        // Facing the way they came out of the door, camera included, whatever the mouse was doing
        // as they crossed it. The cut hides it, and it is what makes the turn that follows always
        // the same quarter turn to the far end instead of however far round they happened to be.
        if (player != null) FacePlayerOutOfDoor(player);

        if (player != null && config.SnapPlayerToMark && stage.playerCorridorMark != null)
        {
            Vector3 mark = stage.playerCorridorMark.position;
            mark.y = player.transform.position.y;

            Vector3 facing = player.PlayerBody != null ? Flat(player.PlayerBody.forward) : Vector3.zero;
            player.TeleportTo(mark, facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing)
                : player.transform.rotation);
        }

        SlamEveryDoor();

        // The dense fog comes in, clock stopped: the shots are in it. A fog of the cinematic's own,
        // if the config gives it one, goes on top until the cut back.
        BeginFogCycle();
        PushShotFog(config.RevealShotFog);

        bool hasNemesis = actor != null && stage.nemesisCorridorEnd != null && actor.TryTakeControl();
        if (hasNemesis)
        {
            actor.WarpTo(stage.nemesisCorridorEnd);
            actor.SetEyesVisible(false);
        }

        Vector3 farEnd = hasNemesis ? actor.Body.position
                       : stage.nemesisCorridorEnd != null ? stage.nemesisCorridorEnd.position
                       : transform.position;

        // The hold: nothing but the fog closing in.
        await WaitOrSkip(config.TensionSeconds, token);

        // The turn round to the far end of the corridor. From the player's own view by default:
        // they turn where they stand, see the corridor the way they will see it with control back,
        // and nothing has to be re-aimed at the handover.
        Vector3 farEndEyes = farEnd + Vector3.up * NemesisEyeHeight;
        if (!skipRequested)
        {
            if (config.PanOnPlayerView && player != null)
            {
                // The cut back to the player's camera: the shot steps down and the turn starts there.
                ReleaseShots();
                await TurnPlayerViewTo(player, farEndEyes, config.PanSeconds, token);
            }
            else if (slamShot != null && slamShot.IsLive)
            {
                slamShot.BeginPan(farEndEyes, config.PanSeconds, config.PanCurve, config.PanTurnSign);

                while (slamShot.IsPanning && !skipRequested)
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }

        // The eyes open. Whatever was skipped, they are open from here on.
        if (hasNemesis) actor.SetEyesVisible(true);
        if (!skipRequested && eyesShot != null)
        {
            CutTo(eyesShot);
            await WaitOrSkip(config.EyesHoldSeconds, token);
        }

        // The charge: it runs at the player, and control comes back once it has covered the
        // distance — mid-stride, the chase takes it from there.
        Vector3 chargeFrom = hasNemesis ? actor.Body.position : farEnd;
        if (!skipRequested && hasNemesis && player != null)
        {
            // Back to the player's view for the charge, so control comes back with no cut at all.
            if (config.ChargeOnPlayerView) ReleaseShots();
            else CutTo(chargeShot);

            actor.RunTo(player.transform);
            PlaySound(config.RevealSoundId, actor.Body.position);

            float elapsed = 0f;
            while (!skipRequested && elapsed < config.ChargeMaxSeconds && actor.IsMoving &&
                   HorizontalGap(actor.Body.position, chargeFrom) < config.ChargeDistance)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                elapsed += Time.deltaTime;
            }
        }

        // However it ended, it ends where the charge would have got it: cut short, it is put there
        // now. Never nearer than that.
        if (hasNemesis && player != null &&
            HorizontalGap(actor.Body.position, chargeFrom) < config.ChargeDistance)
        {
            Vector3 toPlayer = Flat(player.transform.position - chargeFrom);
            float run = Mathf.Min(config.ChargeDistance, Mathf.Max(0f, toPlayer.magnitude - 1f));
            if (toPlayer.sqrMagnitude > 0.0001f)
                actor.WarpTo(chargeFrom + toPlayer.normalized * run, Quaternion.LookRotation(toPlayer).eulerAngles.y);
        }

        // Only when the player was not the one who turned: on the frame of the cut, so the turn is
        // never seen. After a turn of their own they are already looking at it, and re-aiming here
        // would jerk the view they just settled into.
        if (player != null && (skipRequested || !config.PanOnPlayerView))
            AimPlayerViewAt(player, hasNemesis ? actor.Body.position : farEnd);

        ReleaseShots();
        FinishReveal();
    }

    /// <summary>
    /// Every open door around the player slams shut on this frame, and every one of them locks:
    /// the corridor's, the hub's other exits (already sealed when the last core went in) and the
    /// centre door behind the player — there is no going back to the safe zone.
    /// </summary>
    private void SlamEveryDoor()
    {
        if (corridorLock != null)
        {
            corridorLock.SlamAllNow(config);
            foreach (DoorInteractable door in hubExits) corridorLock.SlamDoor(door, config);
            if (stage.safeDoor != null) corridorLock.SlamDoor(stage.safeDoor, config);
        }
        else if (stage.safeDoor != null)
        {
            stage.safeDoor.SetSequenceLocked(true);
            stage.safeDoor.Slam(config.DoorSlamSeconds, config.DoorSlamSoundId);
        }

        hubExitsSealed = true;
    }

    /// <summary>
    /// Points the player, body and camera, straight out of the centre door into the corridor: the
    /// way they were walking when they crossed it, whatever the mouse had been doing. Done on the
    /// frame of the cut, so it is never seen — and it is what the turn to the far end starts from.
    /// </summary>
    private void FacePlayerOutOfDoor(PlayerStateManager player)
    {
        Vector3 toCorridor = CorridorSideOf(safeDoorCentre);
        if (toCorridor.sqrMagnitude < 0.0001f) return;

        if (player.PlayerBody != null) player.PlayerBody.forward = toCorridor;
        player.NextDirection = toCorridor;

        PlayerCameraController look = FindAnyObjectByType<PlayerCameraController>();
        if (look != null) look.FaceYaw(Quaternion.LookRotation(toCorridor).eulerAngles.y);
    }

    /// <summary>
    /// The turn to the far end of the corridor, done by the player where they stand and seen
    /// from their own camera: the body turns with the view, so what they are looking at when it
    /// ends is what they get when control comes back — nothing is re-aimed behind a cut, and there
    /// is nothing to be disoriented by.
    ///
    /// Which way round is the config's turn sign: half a turn has no short way to speak of, and one
    /// side sweeps the corridor while the other sweeps a wall.
    /// </summary>
    private async UniTask TurnPlayerViewTo(PlayerStateManager player, Vector3 point, float seconds,
                                           CancellationToken token)
    {
        PlayerCameraController look = FindAnyObjectByType<PlayerCameraController>();
        if (player == null || look == null) return;

        Vector3 to = Flat(point - player.transform.position);
        if (to.sqrMagnitude < 0.0001f) return;
        to.Normalize();

        CinemachineOrbitalFollow orbital = look.GetComponent<CinemachineOrbitalFollow>();
        float fromYaw = orbital != null ? orbital.HorizontalAxis.Value : player.transform.eulerAngles.y;
        float fromVertical = orbital != null ? orbital.VerticalAxis.Value : 0f;
        float fromBodyYaw = player.PlayerBody != null ? player.PlayerBody.eulerAngles.y : fromYaw;

        // Same shoulder rule as the cut version: the camera sits off the line so the player's own
        // head does not cover what they are looking at.
        float side = look.Config != null && look.Config.ShoulderOffset.x < 0f ? -1f : 1f;
        float bodyYaw = Quaternion.LookRotation(to).eulerAngles.y;
        float toYaw = bodyYaw - side * config.RevealCameraSideAngle;

        // The short way round, always: the player turns their head the way a person would. Coming
        // out of the centre door they are facing into the corridor, so the far end is a quarter
        // turn to the left — anything longer reads as being spun around. (The config's turn sign
        // is for the camera pan, which starts from a framing that has no natural short way.)
        float delta = Mathf.DeltaAngle(fromYaw, toYaw);

        AnimationCurve curve = config.PanCurve;
        float elapsed = 0f;
        while (elapsed < seconds && !skipRequested)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, seconds));
            float k = curve != null && curve.length > 0 ? curve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);

            look.FaceYaw(fromYaw + delta * k);
            if (orbital != null)
                orbital.VerticalAxis.Value =
                    orbital.VerticalAxis.ClampValue(Mathf.Lerp(fromVertical, config.RevealCameraVertical, k));

            // The body turns with the view: it is the player turning round, not just the camera.
            Vector3 facing = Quaternion.Euler(0f, Mathf.LerpAngle(fromBodyYaw, bodyYaw, k), 0f) * Vector3.forward;
            if (player.PlayerBody != null) player.PlayerBody.forward = facing;
            player.NextDirection = facing;
        }
    }

    /// <summary>
    /// Turns the player to a point, their camera behind them looking at it, at once: done on the
    /// frame of a cut, so the turn is never seen. The camera sits the config's side angle off the
    /// line, towards the side the rig's shoulder offset leaves open — straight behind, the player's
    /// own head covered the eyes. And at the config's height: the rig's default ring looks at the
    /// floor.
    /// </summary>
    private void AimPlayerViewAt(PlayerStateManager player, Vector3 point)
    {
        Vector3 to = point - player.transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;
        to.Normalize();

        // The body and the way it moves off, not the root: the camera's pivot hangs from the root.
        if (player.PlayerBody != null) player.PlayerBody.forward = to;
        player.NextDirection = to;

        PlayerCameraController look = FindAnyObjectByType<PlayerCameraController>();
        if (look == null) return;

        // Shoulder to the right (the rig's default): the player stands left of centre, so the camera
        // turns left of the line and what it looks at lands right of them.
        float side = look.Config != null && look.Config.ShoulderOffset.x < 0f ? -1f : 1f;
        look.FaceYaw(Quaternion.LookRotation(to).eulerAngles.y - side * config.RevealCameraSideAngle);

        CinemachineOrbitalFollow orbital = look.GetComponent<CinemachineOrbitalFollow>();
        if (orbital != null) orbital.VerticalAxis.Value = orbital.VerticalAxis.ClampValue(config.RevealCameraVertical);

        // A cut, not a swing: the rig forgets where it was, damping and all.
        CinemachineCamera cam = look.GetComponent<CinemachineCamera>();
        if (cam != null) cam.PreviousStateIsValid = false;
    }

    /// <summary>
    /// The handoff: control, the Nemesis's chase, the sirens and the fog's breathing, on the same
    /// frame. The player looks down the corridor at the Nemesis coming; the way out is behind them.
    /// </summary>
    private void FinishReveal()
    {
        EndCinematic();
        BeginChase();
    }

    private void BeginChase()
    {
        phase = Phase.Escape;

        // Mid-stride if it is still running: the chase state sets its own gait on the same frame.
        if (actor != null)
        {
            actor.SetEyesVisible(true);
            actor.Release();
        }
        if (pursuit != null) pursuit.Begin(config);
        if (chaseRestart != null) chaseRestart.Begin(stage.chaseCheckpoint, config.CaptureOutcome);

        // Normally under way since the slam; this is the net if it never got there.
        BeginFogCycle();

        // The breathing starts closed, with the clock running: the first thing the player sees is
        // the eyes coming, and only then does the fog open on the rest of it.
        if (fogCycle != null) fogCycle.Restart();

        // The alarm goes off with the chase: sirens and sound on the frame control comes back.
        if (alarmLights != null) alarmLights.Begin(config);
        if (escapeAudio != null) escapeAudio.Begin(config);

        RegisterWinPresenter();
        ScheduleEndGate(PlayerPosition());
    }

    /// <summary>
    /// Brings the escape's fog in, once. The guard is not optional: a second Begin on a running
    /// cycle rebuilds its runtime presets and starts its clock over.
    /// </summary>
    private void BeginFogCycle()
    {
        if (fogCycle == null || fogCycle.IsRunning) return;

        fogCycle.RouteCompleted -= HandleRouteCompleted;
        fogCycle.RouteCompleted += HandleRouteCompleted;
        fogCycle.Begin(config);
    }

    // ── Capture, with Restart Chase: the chase starts over ──────────────────

    /// <summary>
    /// The player is back at Player_Spot, the screen still black: the chase is reset to how it
    /// started — the fog closed, the path from its first door, the gate shut again. The gate opens
    /// on its own schedule once the player is up. (Only raised with Restart Chase: a game over
    /// ends the run instead, see <see cref="EscapeChaseRestart"/>.)
    /// </summary>
    private void HandleChaseRespawned()
    {
        if (phase != Phase.Escape) return;

        CancelSequence();

        // Closed and still while the screen is black and the player gets up: the breathing starts
        // over when they have control (HandleChaseControlRegained), not behind the black screen.
        if (fogCycle != null)
        {
            fogCycle.Restart();
            fogCycle.Hold(open: false);
        }

        CancelEndGate();
        if (stage.endGate != null) stage.endGate.CloseImmediate();

        // Every seal again, quietly: nothing should have opened one, but a way back into the safe
        // zone would end the chase for good. Player_Spot is well clear of every leaf.
        if (corridorLock != null)
        {
            corridorLock.LockAllNow();
            if (stage.safeDoor != null) corridorLock.LockDoor(stage.safeDoor, config, quiet: true);
            foreach (DoorInteractable door in hubExits) corridorLock.LockDoor(door, config, quiet: true);
        }
    }

    /// <summary>The Nemesis has let go: it waits on the chase's start mark, behind the player, until
    /// they are up.</summary>
    private void HandleChaseNemesisFreed()
    {
        if (phase != Phase.Escape || actor == null) return;

        if (actor.TryTakeControl()) actor.WarpTo(stage.nemesisApproachStart);
    }

    private void HandleChaseControlRegained()
    {
        if (phase != Phase.Escape) return;

        // The fog breathes again from closed, the path from its first door.
        if (fogCycle != null) fogCycle.Restart();

        ScheduleEndGate(PlayerPosition());
        ReleaseNemesisAfterAsync(config.RestartNemesisDelay, NewSequenceToken()).Forget();
    }

    private async UniTaskVoid ReleaseNemesisAfterAsync(float seconds, CancellationToken token)
    {
        if (seconds > 0f) await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: token);
        if (phase == Phase.Escape && actor != null) actor.Release();
    }

    // ── Gate ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The gate opens during the chase, not before it, and finishes as a player who sprinted for it
    /// gets there. Its opening has a fixed length (the sound is cut to it), so what is timed here is
    /// the START: the run from <paramref name="from"/> takes R seconds, the opening takes D, so it
    /// starts R - D from now — at once when D is the longer one.
    /// </summary>
    private void ScheduleEndGate(Vector3 from)
    {
        if (stage.endGate == null) return;

        CancelEndGate();
        endGateCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        float startIn = Mathf.Max(0f, RunToEndGateSeconds(from) - stage.endGate.OpenDuration);
        OpenEndGateAfterAsync(startIn, endGateCts.Token).Forget();
    }

    // Scaled time, like the chase it is counted against. A cancel (a restart, the ending, the
    // director going away) throws out of the delay and the gate is left alone.
    private async UniTaskVoid OpenEndGateAfterAsync(float seconds, CancellationToken token)
    {
        if (seconds > 0f) await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: token);
        stage.endGate.Open();
    }

    private void CancelEndGate()
    {
        endGateCts?.Cancel();
        endGateCts?.Dispose();
        endGateCts = null;
    }

    /// <summary>The sprint to the gate in a straight line, with the player's sprint of this moment
    /// (so a module penalty moves the gate too), plus the slack.</summary>
    private float RunToEndGateSeconds(Vector3 from)
    {
        Vector3 to = stage.endGate.transform.position;
        from.y = to.y = 0f;

        return Vector3.Distance(from, to) / SprintSpeedOf(PlayerRegistry.Current) + config.EndGateSlackSeconds;
    }

    // Same product the moving state uses (and NemesisEscapePursuit): EffectiveMoveSpeed carries the
    // legs penalty and SprintPenaltyFactor the chest one.
    private static float SprintSpeedOf(PlayerStateManager player)
    {
        if (player == null || player.Movement == null) return 4.5f;
        return Mathf.Max(0.1f, player.EffectiveMoveSpeed * player.Movement.SprintSpeedMultiplier *
                               player.SprintPenaltyFactor);
    }

    /// <summary>
    /// The player got to the last door of the route. This only reports it: nothing ends and nothing
    /// is cut, so the Nemesis keeps chasing until the level's WinTrigger, past the gate, takes over.
    /// </summary>
    private void HandleRouteCompleted()
    {
        if (phase != Phase.Escape) return;

        GateReached?.Invoke();
        onGateReached.Invoke();
    }

    // ── Ending ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The player crossed the WinTrigger. During the chase this plays the last shots before the win
    /// screen; outside it the win goes through at once.
    /// </summary>
    public void PresentWin(Action commit)
    {
        if (commit == null) return;
        if (phase != Phase.Escape)
        {
            commit();
            return;
        }

        phase = Phase.Ending;
        skipRequested = false;
        RunEndingAsync(commit, NewSequenceToken()).Forget();
    }

    /// <summary>
    /// The gate, from the corridor side, without the player, in a fog that shuts just past the gate
    /// so nothing of the other side shows: the Nemesis runs at it and it slams down in its face with
    /// a burst of dust. Cut to the Nemesis left standing at it through the dust, stuck on the wrong
    /// side. Then the win. The shot stays up behind the win screen (which freezes the game).
    /// </summary>
    private async UniTaskVoid RunEndingAsync(Action commit, CancellationToken token)
    {
        PlayerStateManager player = PlayerRegistry.Current;

        BeginCinematic();
        HidePlayer(player);

        // Won from here: no module may run out behind the shot.
        PauseModuleTicks();

        // The chase is over. Nothing hunts any more; the Nemesis is an actor again.
        if (pursuit != null) pursuit.End();
        if (chaseRestart != null) chaseRestart.End();
        CancelEndGate();

        // Closed, and the gate shot's own fog on top: it ends just past the gate.
        if (fogCycle != null) fogCycle.Hold(open: false);
        PushShotFog(config.GateShotFog);

        bool hasNemesis = actor != null && actor.TryTakeControl();
        if (hasNemesis)
        {
            actor.WarpTo(stage.nemesisGateStart);
            actor.RunTo(stage.nemesisGateStop);

            // Once there it turns to the gate and stays.
            if (stage.endGate != null) actor.FaceTowards(stage.endGate.transform);
        }

        CutTo(gateShot, hasNemesis ? actor.Body : null);

        await WaitOrSkip(config.GateDropDelay, token);

        if (skipRequested)
        {
            if (hasNemesis) actor.WarpTo(stage.nemesisGateStop);
            if (gateSlam != null) gateSlam.SlamNow(config.GateSlamSoundId);
            else if (stage.endGate != null) stage.endGate.CloseImmediate();
        }
        else if (gateSlam != null)
        {
            await gateSlam.SlamAsync(config.GateSlamSeconds, config.GateSlamSoundId, token);
        }
        else if (stage.endGate != null)
        {
            await stage.endGate.SlamShutAsync(config.GateSlamSeconds, token);
        }

        if (gateShot != null) gateShot.Shake(config.GateShakeAmplitude, config.GateShakeSeconds);

        // Through the dust: the Nemesis, left on the corridor side of the gate.
        if (dustShot != null)
        {
            await WaitOrSkip(config.DustShotDelay, token);
            CutTo(dustShot, hasNemesis ? actor.Body : null);
        }

        await WaitOrSkip(config.EndingHoldSeconds, token);

        // Only the skip prompt goes: the shot stays behind the win screen.
        CinematicState.End();
        commit();
    }

    private async UniTask WaitOrSkip(float seconds, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < seconds && !skipRequested)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Time.deltaTime;
        }
    }

    private void RegisterWinPresenter() => GameResultManager.WinPresenter = this;

    private void UnregisterWinPresenter()
    {
        if (ReferenceEquals(GameResultManager.WinPresenter, this)) GameResultManager.WinPresenter = null;
    }

    // ── End of the run ──────────────────────────────────────────────────────

    /// <summary>
    /// The run is over — the win after the last shot, or a defeat (the capture's game over, a
    /// module running out) at any point after the last core. What the escape still has running is
    /// stopped here: without this the sirens kept going, the fog kept cycling and the alarm kept
    /// sounding behind the result screen.
    /// </summary>
    private void HandleGameResult(GameResultModel result)
    {
        if (phase == Phase.Idle || phase == Phase.Done) return;

        if (phase == Phase.Ending)
        {
            // The last shot stays up, frozen with the game: only what would still be heard goes.
            StopChaseSystems();
        }
        else
        {
            // A defeat in the middle of the cinematic drops its shots; the player stays as the
            // defeat left them.
            CancelSequence();
            ReleaseShots();
            if (phase == Phase.Reveal) TearDownCinematic(releasePlayer: false);
            EndEscapeSystems();
        }

        UnregisterWinPresenter();
        phase = Phase.Done;
    }

    private void EndEscapeSystems()
    {
        if (fogCycle != null) fogCycle.End();
        if (alarmLights != null) alarmLights.End();
        StopChaseSystems();
    }

    private void StopChaseSystems()
    {
        if (pursuit != null) pursuit.End();
        if (chaseRestart != null) chaseRestart.End();
        if (escapeAudio != null) escapeAudio.Stop();
    }

    // ── Retired opening ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="EscapeBeatReceiver"/> for the markers of the old opening Timeline. There
    /// is no opening cinematic any more (the last core arms the escape without cutting), so they do
    /// nothing. Goes with the Timeline, its receiver and <see cref="EscapeBeat"/> once the scene no
    /// longer carries them.
    /// </summary>
    public void OnBeat(EscapeBeat beat) { }

    // ── Shots ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts <paramref name="shot"/> on the air and takes every other shot of the escape off, on the
    /// same frame: a hard cut, with nothing of the player's own view in between. With no shot, the
    /// one on the air stays.
    /// </summary>
    private void CutTo(EscapeShotCamera shot, Transform lookTarget = null)
    {
        if (shot == null) return;

        shot.GoLive(lookTarget);
        foreach (EscapeShotCamera other in Shots())
        {
            if (other != null && other != shot) other.Release();
        }
    }

    private void ReleaseShots()
    {
        foreach (EscapeShotCamera shot in Shots())
        {
            if (shot != null) shot.Release();
        }
    }

    private IEnumerable<EscapeShotCamera> Shots()
    {
        yield return slamShot;
        yield return eyesShot;
        yield return chargeShot;
        yield return gateShot;
        yield return dustShot;
    }

    /// <summary>A shot's own fog, on top of the escape's while the shot is up. Popped with the rest
    /// of the cinematic (<see cref="TearDownCinematic"/>) — except the ending's, which stays behind
    /// the win screen with its shot.</summary>
    private void PushShotFog(SO_VisionFogConfig fog)
    {
        PopShotFog();
        if (fog == null) return;

        shotFogController = FindAnyObjectByType<VisionRangeController>();
        if (shotFogController == null) return;

        shotFogPushed = fog;
        shotFogController.PushConfig(shotFogPushed);
    }

    private void PopShotFog()
    {
        if (shotFogController != null && shotFogPushed != null) shotFogController.PopConfig(shotFogPushed);
        shotFogController = null;
        shotFogPushed = null;
    }

    // ── Player ──────────────────────────────────────────────────────────────

    private Vector3 PlayerPosition()
    {
        Transform player = PlayerRegistry.CurrentTransform;
        if (player != null) return player.position;
        return stage.playerSpot != null ? stage.playerSpot.position : transform.position;
    }

    /// <summary>Takes the player out of the picture (the last shots must not show them past the
    /// gate). Only what is visible is hidden, so <see cref="ShowPlayer"/> gives back exactly that.</summary>
    private void HidePlayer(PlayerStateManager player)
    {
        if (player == null || hiddenRenderers.Count > 0) return;

        foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            r.enabled = false;
            hiddenRenderers.Add(r);
        }
    }

    private void ShowPlayer()
    {
        foreach (Renderer r in hiddenRenderers)
        {
            if (r != null) r.enabled = true;
        }
        hiddenRenderers.Clear();
    }

    private void PauseModuleTicks()
    {
        if (moduleTicksPaused || !ModuleManager.Exists) return;

        ModuleManager.Instance.PauseTicking();
        moduleTicksPaused = true;
    }

    // A new session resets the pause count on its own; this only balances the one taken above.
    private void ResumeModuleTicks()
    {
        if (!moduleTicksPaused) return;
        moduleTicksPaused = false;

        if (ModuleManager.Exists) ModuleManager.Instance.ResumeTicking();
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private static float HorizontalGap(Vector3 a, Vector3 b) => Flat(a - b).magnitude;

    private static void PlaySound(string soundId, Vector3 position)
    {
        if (!string.IsNullOrWhiteSpace(soundId) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(soundId, position);
    }

    // ── Sequences ───────────────────────────────────────────────────────────

    // The reveal, the ending and the Nemesis's release after a restart never overlap, so they share
    // one token: starting one cancels whatever was left of the last.
    private CancellationToken NewSequenceToken()
    {
        CancelSequence();
        sequenceCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        return sequenceCts.Token;
    }

    private void CancelSequence()
    {
        sequenceCts?.Cancel();
        sequenceCts?.Dispose();
        sequenceCts = null;
    }

    // ── Cinematic state ─────────────────────────────────────────────────────

    /// <summary>Freezes the player, hides nothing else, and makes every camera change a hard cut.</summary>
    private void BeginCinematic()
    {
        PlayerStateManager player = PlayerRegistry.Current;

        // Only taken when free, and only then given back: a player already disabled by something
        // else (a capture, the wake-up) must not be released by this cinematic.
        if (player != null && !player.IsDisabled)
        {
            player.IsDisabled = true;
            lockedPlayer = player;
        }

        CinematicState.Begin(config.Skippable, config.SkipPromptText);

        // The fog is measured from the shot, not from the player standing somewhere off camera, so
        // the cinematic carries the same fog as the gameplay it cuts to (WIR-040).
        fogCentre = FindAnyObjectByType<VisionRangeController>();
        fogCentreCamera = Camera.main != null ? Camera.main.transform : null;
        if (fogCentre != null && fogCentreCamera != null) fogCentre.SetCentreOverride(fogCentreCamera);

        // A cinematic that starts right after another (the test key) finds the last one's blend
        // still on its way back: that one is cancelled, and the blend saved before it — the
        // gameplay one — is the one kept.
        if (blendRestore != null)
        {
            StopCoroutine(blendRestore);
            blendRestore = null;
            brainOverridden = brain != null;
        }

        // Every cut in the script is a cut, including the ones into and out of each shot.
        if (!brainOverridden) brain = CinemachineBrain.ActiveBrainCount > 0 ? CinemachineBrain.GetActiveBrain(0) : null;
        if (brain != null && !brainOverridden)
        {
            previousBlend = brain.DefaultBlend;
            brainOverridden = true;
        }
        if (brain != null) brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
    }

    private void EndCinematic() => TearDownCinematic(releasePlayer: true);

    private void TearDownCinematic(bool releasePlayer)
    {
        CinematicState.End();
        ReleaseFogCentre();
        PopShotFog();

        if (releasePlayer && lockedPlayer != null) lockedPlayer.IsDisabled = false;
        lockedPlayer = null;

        // Put back two frames later, when the brain has already made the cut back to gameplay.
        if (brainOverridden && brain != null) blendRestore = StartCoroutine(RestoreBlendLater(brain, previousBlend));
        brainOverridden = false;
    }

    private void ReleaseFogCentre()
    {
        if (fogCentre != null) fogCentre.ClearCentreOverride(fogCentreCamera);
        fogCentre = null;
        fogCentreCamera = null;
    }

    private IEnumerator RestoreBlendLater(CinemachineBrain target, CinemachineBlendDefinition blend)
    {
        yield return null;
        yield return null;
        if (target != null) target.DefaultBlend = blend;
        blendRestore = null;
    }
}
