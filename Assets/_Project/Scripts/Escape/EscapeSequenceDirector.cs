using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Serialization;
using UnityEngine.Timeline;

/// <summary>
/// Runs the escape sequence from the third core to the gate. It is the conductor and nothing else:
/// it decides WHEN each piece starts, and every piece does its own work (<see cref="EscapeFogCycle"/>,
/// <see cref="EscapeCorridorFlicker"/>, <see cref="NemesisCinematicActor"/>,
/// <see cref="NemesisEscapePursuit"/>, <see cref="EscapePlayerTurn"/>, <see cref="EscapeShotCamera"/>,
/// <see cref="EscapeChaseRestart"/>, <see cref="EscapeGateSlam"/>, <see cref="EscapeAudio"/>,
/// <see cref="EscapeCorridorLock"/>).
///
///   Trigger    the hub puzzle (the three cores) completes.
///   Opening    a short Timeline: the macro of the socket while the alarm goes off, every other
///              door of the hub and the corridor shuts and locks one after another, and the centre
///              door (the safe-zone door) opens. Its shot is a Cinemachine clip and its actions are
///              <see cref="EscapeBeatMarker"/>s — retime them in the Timeline. The Nemesis is taken
///              out of play, behind its door.
///   Corridor   the player has control and walks out through the only open door. Nothing chases
///              yet: the lamps fail, the alarm sounds, every other door says LOCKED.
///   Reveal     once the player is out (the Stage's reveal trigger, and clear of the door's swing):
///              the safe-zone door slams and locks behind them — there is no going back to the safe
///              zone, the chase is the only way — the fog rolls in, and the player turns round,
///              slowly, with their own camera, to the Nemesis's eyes glowing in the fog. It charges
///              as the fog expands around it, and control comes back while it is still coming.
///   Escape     gameplay (Pasos 4-7): the lamps flicker and the fog expands and contracts while the
///              amber lights mark the path door to door, with the Nemesis chasing the whole way. A
///              capture starts the chase over from Player_Spot; it does not end the run.
///   Ending     crossing the level's WinTrigger, past the gate, plays one last shot before the
///              win: the Nemesis runs at the gate, the gate slams down in its face in a burst of
///              dust, and it is left stuck on the other side. The player is not in the shot. The
///              director is the <see cref="IWinPresenter"/> for as long as the chase lasts.
///
/// Every cinematic can be cut with the config's skip key, and a skip lands in the state the
/// cinematic ends in: the opening's playhead jumps to the end (its markers are retroactive), the
/// reveal hands over turned to the Nemesis, with it where its run would have taken it, the ending
/// shuts the gate.
///
/// The test key (<see cref="StartForTest"/>) starts from inside the hub, in front of the centre
/// door, wherever the player was: the rest of the sequence assumes that is where it begins.
///
/// What is NOT covered: resuming the escape from a saved game (if the trigger puzzle is already
/// complete when the scene loads, nothing plays), and the floor / wall fluid marks of the path
/// (art).
///
/// SETUP: Tools ▸ Escape Sequence ▸ Setup Chase Cinematics builds and wires the reveal, the
/// restart checkpoint and the ending on the open scene.
/// </summary>
public class EscapeSequenceDirector : MonoBehaviour, IWinPresenter
{
    /// <summary>The scene objects the escape acts on. The Timeline's markers and the config name
    /// WHAT happens; these say WHERE.</summary>
    [Serializable]
    public class Stage
    {
        [Header("Player")]
        [Tooltip("Donde vuelve a arrancar la persecución si el Nemesis te agarra, mirando hacia el " +
                 "portón. Parado en la línea del medio del pasillo.")]
        public Transform playerSpot;

        [Tooltip("El checkpoint del reinicio: un Checkpoint en Player_Spot que no se activa solo " +
                 "(modo Puzzle Completed, sin puzzles). El director lo activa al arrancar la " +
                 "persecución.")]
        public Checkpoint chaseCheckpoint;

        [Tooltip("La puerta del centro: la única que queda abierta, se abre sola en la apertura y se " +
                 "cierra y traba atrás del jugador cuando sale (no hay vuelta a la zona segura). NO " +
                 "va en la lista de EscapeCorridorLock.")]
        public DoorInteractable safeDoor;

        [Tooltip("La zona del pasillo, pasando la puerta del centro, que arma la aparición del " +
                 "Nemesis. Arranca apenas el jugador, ya adentro, está fuera del barrido de la hoja " +
                 "de la puerta (que se cierra atrás suyo).")]
        public EscapeRevealTrigger revealTrigger;

        [Tooltip("El portón del final del pasillo. Se abre durante la persecución y termina justo " +
                 "cuando llegarías corriendo (ver 'End Gate Slack Seconds' en el config), y cae de " +
                 "golpe en el plano final. Necesita 'Opens On Puzzle Completed' apagado, o su puzzle " +
                 "lo abre antes.")]
        public PuzzleGate endGate;

        [Header("Nemesis")]
        [Tooltip("Fuera de vista: acá espera desde la alarma hasta que salís al pasillo.")]
        public Transform nemesisHidden;

        [Tooltip("Donde aparece en la niebla, mirando hacia el jugador. Lejos: primero se le ven " +
                 "sólo los ojos.")]
        public Transform nemesisRevealStart;

        [Tooltip("Donde vuelve a arrancar si te agarró: detrás de Player_Spot, a la distancia a la " +
                 "que empieza la persecución.")]
        public Transform nemesisApproachStart;

        [Header("Nemesis (final)")]
        [Tooltip("Desde dónde corre hacia el portón en el plano final, del lado del pasillo.")]
        public Transform nemesisGateStart;

        [Tooltip("Donde queda trabado: pegado al portón, del lado del pasillo.")]
        public Transform nemesisGateStop;
    }

    [SerializeField] private SO_EscapeSequenceConfig config;

    [Header("Timelines")]
    [Tooltip("La apertura: macro del socket, alarma, puertas trabándose, se abre la del centro. Se " +
             "edita en Window ▸ Sequencing ▸ Timeline.")]
    [SerializeField] private PlayableDirector openingTimeline;

    [Header("Cinematic cameras")]
    [Tooltip("La cámara del macro del socket. Se coloca sola frente al ÚLTIMO socket encastrado " +
             "al arrancar.")]
    [SerializeField] private CinemachineCamera socketMacroCamera;

    [SerializeField, Min(0.1f)] private float macroDistance = 0.6f;
    [SerializeField] private float macroHeight = 0.2f;
    [SerializeField] private float macroLookHeight = 0.05f;

    [Tooltip("El plano final: el portón desde el lado del pasillo. Se ubica a mano en la escena; " +
             "el jugador no se ve (queda oculto mientras dura).")]
    [SerializeField] private EscapeShotCamera gateShot;

    [Tooltip("El giro de la aparición: el jugador se da vuelta despacio hacia el Nemesis, con su " +
             "propia cámara (no hay corte).")]
    [SerializeField] private EscapePlayerTurn playerTurn;

    [Header("Scene pieces")]
    [SerializeField] private Stage stage = new Stage();
    [SerializeField] private EscapeFogCycle fogCycle;

    [Tooltip("Las luces blancas del pasillo: titilan desde la alarma hasta el final del escape. La " +
             "niebla no las apaga.")]
    [SerializeField] private EscapeCorridorFlicker corridorFlicker;
    [SerializeField] private EscapeAudio escapeAudio;
    [SerializeField] private EscapeCorridorLock corridorLock;
    [SerializeField] private NemesisCinematicActor actor;
    [SerializeField] private NemesisEscapePursuit pursuit;

    [Tooltip("Una captura durante el escape vuelve a empezar la persecución desde Player_Spot: no " +
             "es game over.")]
    [FormerlySerializedAs("captureGameOver")]
    [SerializeField] private EscapeChaseRestart chaseRestart;

    [Tooltip("El portón cayendo en el plano final, con su polvo.")]
    [SerializeField] private EscapeGateSlam gateSlam;

    [Tooltip("Los tres sockets de los núcleos. Vacío = se buscan solos en la escena.")]
    [SerializeField] private SocketInteractable[] sockets = new SocketInteractable[0];

    [Header("Gate trigger")]
    [Tooltip("Se dispara cuando el jugador llega a la última puerta del recorrido (el portón). No " +
             "termina nada: el Nemesis sigue persiguiendo hasta el WinTrigger. Colgale acá lo que " +
             "quieras.")]
    [SerializeField] private UnityEvent onGateReached = new UnityEvent();

    /// <summary>The player reached the last door of the route (the gate).</summary>
    public event Action GateReached;

    private enum Phase { Idle, Opening, Corridor, Reveal, Escape, Ending, Done }

    private Phase phase = Phase.Idle;
    private PlayableDirector activeTimeline;
    private bool skipping;
    private bool skipRequested;

    // The player has been in the reveal trigger; the reveal waits only for them to be clear of the
    // centre door's swing, wherever they walked on to.
    private bool revealArmed;

    // Clearance kept between the player and the centre door's leaf as it shuts: the capsule, and
    // some air.
    private const float SwingClearance = 0.45f;

    // Where the test key puts the player: inside the hub, this far from the centre door.
    private const float TestStartDistance = 1.8f;

    private readonly HashSet<EscapeBeat> firedBeats = new HashSet<EscapeBeat>();

    private SocketInteractable lastInsertedSocket;
    private PlayerStateManager lockedPlayer;
    private CancellationTokenSource endGateCts;
    private CancellationTokenSource sequenceCts;

    private CinemachineBrain brain;
    private CinemachineBlendDefinition previousBlend;
    private bool brainOverridden;
    private Coroutine blendRestore;

    private VisionRangeController fogCentre;
    private Transform fogCentreCamera;

    // The centre door's doorway, measured on the scene's closed door: the open leaf would drag the
    // bounds out into the corridor.
    private Vector3 safeDoorCentre;

    // And where its leaf pivots and how far it reaches, for the swing check. Both hold for the
    // door open or shut: the leaf turns about the hinge.
    private Vector3 safeDoorHinge;
    private float safeDoorReach;

    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private bool moduleTicksPaused;

    /// <summary>A cinematic of the escape is on screen (the opening, the reveal or the ending).</summary>
    public bool IsPlaying => phase == Phase.Opening || phase == Phase.Reveal || phase == Phase.Ending;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    // Static events: subscribed in Awake and released in OnDestroy, like the rest of the project.
    private void Awake()
    {
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
        GameResultManager.OnGameResult += HandleGameResult;

        // It sits on this same object; the field only makes it visible in the inspector.
        if (playerTurn == null) playerTurn = GetComponent<EscapePlayerTurn>();

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
        foreach (SocketInteractable socket in sockets)
        {
            if (socket != null) socket.Inserted -= HandleAnySocketInserted(socket);
        }

        CancelEndGate();
        CancelSequence();
        UnregisterWinPresenter();

        // No coroutines here: the object is going away. Whatever the cinematic held is given back now.
        CinematicState.End();
        ReleaseFogCentre();
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

        // Only the sockets of the trigger puzzle: the fuse box and any other socket in the level are
        // not "the third core", and must not become the shot of the macro.
        if (sockets == null || sockets.Length == 0)
            sockets = FindSocketsOfTriggerPuzzle();

        // The sequence already ran (saved game): it does not replay. See the class doc.
        if (PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(config.TriggerPuzzleId))
        {
            phase = Phase.Done;
            return;
        }

        for (int i = 0; i < sockets.Length; i++)
        {
            SocketInteractable socket = sockets[i];
            if (socket != null) socket.Inserted += HandleAnySocketInserted(socket);
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
    /// Plays the sequence from the top as if the third core had just gone in, without solving the
    /// puzzle. For <see cref="EscapeSequenceTestKey"/>: it also works after a run has finished,
    /// tearing down whatever the previous one left (the escape's systems, the locked doors, the
    /// gate). The player is put inside the hub, in front of the centre door, wherever they were —
    /// the sequence assumes it starts there. Ignored while a cinematic is playing.
    /// </summary>
    public void StartForTest()
    {
        if (config == null || IsPlaying) return;

        ResetForReplay();
        PlacePlayerForTest();

        phase = Phase.Opening;
        StartCoroutine(StartOpeningNextFrame());
    }

    private void ResetForReplay()
    {
        CancelSequence();
        CancelEndGate();
        EndEscapeSystems();
        UnregisterWinPresenter();
        revealArmed = false;

        // UnlockAll gives the safe door back too: it was sealed through the same lock.
        if (corridorLock != null) corridorLock.UnlockAll();
        if (stage.endGate != null) stage.endGate.CloseImmediate();
        if (gateSlam != null) gateSlam.ResetDust();
        if (gateShot != null) gateShot.Release();
        if (playerTurn != null) playerTurn.Stop();

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

    // The socket event carries no argument, so each subscription remembers which socket it is for.
    private readonly Dictionary<SocketInteractable, Action> socketHandlers = new Dictionary<SocketInteractable, Action>();

    private Action HandleAnySocketInserted(SocketInteractable socket)
    {
        if (!socketHandlers.TryGetValue(socket, out Action handler))
        {
            handler = () => lastInsertedSocket = socket;
            socketHandlers[socket] = handler;
        }
        return handler;
    }

    private void Update()
    {
        if (phase == Phase.Corridor)
        {
            if (ReadyForReveal()) StartReveal();
            return;
        }

        if (IsPlaying && SkipPressed()) Skip();
    }

    // Legacy input, like the wake-up cinematic: the project runs both input backends.
    private bool SkipPressed()
    {
        if (!config.Skippable) return false;
        if (PauseManager.IsGameplayInputBlocked) return false;
        if (LoadingScreen.IsLoading) return false;

        return Input.GetKeyDown(config.SkipKey);
    }

    /// <summary>
    /// Cuts whatever cinematic is on. The opening's playhead jumps to the end (retroactive markers
    /// fire on the way) and it stops, which lands in the same code as an opening that ended by
    /// itself; the reveal and the ending are told to finish on their next frame.
    /// </summary>
    private void Skip()
    {
        switch (phase)
        {
            case Phase.Opening:
                if (activeTimeline == null) return;

                skipping = true;
                PlayableDirector timeline = activeTimeline;
                timeline.time = timeline.duration;
                timeline.Evaluate();
                timeline.Stop();
                break;

            case Phase.Reveal:
            case Phase.Ending:
                skipRequested = true;
                break;
        }
    }

    // ── Trigger ─────────────────────────────────────────────────────────────

    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (phase != Phase.Idle || config == null || puzzleId != config.TriggerPuzzleId) return;

        // One frame later: this fires from inside the socket's own interaction, and the player is
        // still in that interaction's state.
        phase = Phase.Opening;
        StartCoroutine(StartOpeningNextFrame());
    }

    private IEnumerator StartOpeningNextFrame()
    {
        yield return null;
        StartOpening();
    }

    // ── Opening cinematic ───────────────────────────────────────────────────

    private void StartOpening()
    {
        if (openingTimeline == null || openingTimeline.playableAsset == null)
        {
            Debug.LogError($"[{nameof(EscapeSequenceDirector)}] No opening Timeline: the sequence " +
                           "cannot play.", this);
            phase = Phase.Done;
            return;
        }

        firedBeats.Clear();
        skipping = false;

        BeginCinematic();
        PositionMacroCamera();

        // Out of play until the reveal: behind its door with its machine off, so nothing hunts the
        // player on the way out of the safe room.
        if (actor != null && actor.TryTakeControl()) actor.WarpTo(stage.nemesisHidden);

        PlayTimeline(openingTimeline, HandleOpeningStopped);
    }

    private void HandleOpeningStopped(PlayableDirector director)
    {
        director.stopped -= HandleOpeningStopped;
        activeTimeline = null;
        FinishOpening();
    }

    /// <summary>
    /// The lock-down is done and control comes back where the player stood at the socket. What the
    /// opening's beats were meant to leave behind is made sure of here, so a skip or a deleted
    /// marker lands in the same state.
    /// </summary>
    private void FinishOpening()
    {
        FinishPendingBeats(openingTimeline);

        if (escapeAudio != null) escapeAudio.Begin(config);
        if (corridorFlicker != null && !corridorFlicker.IsRunning) corridorFlicker.Begin(config);
        EscapeSocketAlarmLight alarmLight = AlarmLightOfLastSocket();
        if (alarmLight != null) alarmLight.ActivateNow();
        if (corridorLock != null) corridorLock.LockAllNow();
        if (stage.safeDoor != null) stage.safeDoor.OpenDoor();

        EndCinematic();
        phase = Phase.Corridor;
        revealArmed = false;

        if (stage.revealTrigger == null)
            Debug.LogWarning($"[{nameof(EscapeSequenceDirector)}] No reveal trigger on the Stage: the " +
                             "Nemesis appears as soon as the player is out of the safe room. Tools ▸ " +
                             "Escape Sequence ▸ Setup Chase Cinematics places one.", this);
    }

    // ── Reveal ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the reveal can start this frame. Two conditions, kept apart on purpose: the player has
    /// been in the reveal trigger (that arms it, once), and is now clear of the centre door's swing
    /// — the door shuts behind them the moment it starts, and must not sweep its leaf through them.
    /// Armed and not clear, the player walks on with control until they are; wherever that is.
    /// </summary>
    private bool ReadyForReveal()
    {
        // Not under a menu: the reveal would play frozen behind it.
        if (PauseManager.IsGameplayInputBlocked) return false;

        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return false;

        if (!revealArmed)
            revealArmed = stage.revealTrigger != null
                ? stage.revealTrigger.Contains(player.position)
                : IsOutOfSafeRoom(player.position);

        return revealArmed && ClearOfSafeDoorSwing(player.position);
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
    /// The Nemesis shows itself. The safe-zone door slams and locks behind the player — there is no
    /// going back, the chase is the only way on — and the dense fog rolls in. After a beat the
    /// player turns round, slowly, their own camera turning with them, to the Nemesis standing in the
    /// fog down the corridor: only its eyes pierce it. After the stare it charges, and the fog
    /// expands around it as it comes, so the body follows the eyes out of it. Control comes back
    /// while it is still coming, the player facing it: at the handoff distance, or at the time cap
    /// if it never gets there.
    /// </summary>
    private async UniTaskVoid RunRevealAsync(CancellationToken token)
    {
        PlayerStateManager player = PlayerRegistry.Current;
        Transform start = stage.nemesisRevealStart != null ? stage.nemesisRevealStart : stage.nemesisApproachStart;

        BeginCinematic();

        // No way back: the safe zone is shut and sealed behind the player, the lock clacking home
        // once the leaf is in its frame.
        if (stage.safeDoor != null)
        {
            if (corridorLock != null) corridorLock.LockDoor(stage.safeDoor, config);
            else stage.safeDoor.SetSequenceLocked(true);
        }

        // The dense fog comes in, and the amber path with it.
        BeginFogCycle();

        // Down the corridor, behind the player, where the turn will find it.
        bool hasNemesis = actor != null && start != null && actor.TryTakeControl();
        if (hasNemesis) actor.WarpTo(start);

        float turnAt = config.RevealTurnDelay;
        float chargeAt = turnAt + config.RevealTurnSeconds + config.RevealStareSeconds;
        float elapsed = 0f;
        bool turning = false;
        bool charging = false;

        while (!skipRequested)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            elapsed += Time.deltaTime;

            if (!turning && elapsed >= turnAt)
            {
                turning = true;
                if (player != null && start != null) BeginTurn(player, start.position, config.RevealTurnSeconds);
            }

            if (!charging && elapsed >= chargeAt)
            {
                charging = true;
                if (hasNemesis && player != null) actor.RunTo(player.transform);

                // The fog opens as it charges: the body comes out of it behind the eyes.
                if (fogCycle != null) fogCycle.Hold(open: true);

                Vector3 at = hasNemesis && actor.Body != null ? actor.Body.position
                           : start != null ? start.position : transform.position;
                PlaySound(config.RevealSoundId, at);
            }

            if (elapsed >= config.RevealMaxSeconds) break;
            if (!charging) continue;

            // Nothing to watch coming: the stare was the shot.
            if (!hasNemesis || player == null) break;
            if (HorizontalGap(player.transform, actor.Body) <= config.RevealHandoffDistance) break;
        }

        // However it was cut, control comes back turned to the Nemesis.
        if (player != null && start != null)
        {
            if (!turning) BeginTurn(player, start.position, 0f);
            if (playerTurn != null) playerTurn.Finish();
        }

        if (skipRequested && hasNemesis && player != null) PlaceNemesisForHandoff(player);

        FinishReveal();
    }

    private void BeginTurn(PlayerStateManager player, Vector3 lookAt, float seconds)
    {
        if (playerTurn != null) playerTurn.Begin(player, lookAt, seconds);
        else FacePlayer(player, lookAt);
    }

    /// <summary>A skipped reveal: the Nemesis goes where its run would have handed over — at the
    /// handoff distance, between the player and where it appeared — already running.</summary>
    private void PlaceNemesisForHandoff(PlayerStateManager player)
    {
        Transform body = actor.Body;
        if (body == null) return;

        Vector3 from = player.transform.position;
        Vector3 away = body.position - from;
        away.y = 0f;
        if (away.magnitude <= config.RevealHandoffDistance) return;

        Vector3 spot = from + away.normalized * config.RevealHandoffDistance;
        actor.WarpTo(spot, Quaternion.LookRotation(-away).eulerAngles.y);
        actor.RunTo(player.transform);
    }

    /// <summary>
    /// The handoff: control, the Nemesis's chase and the fog's breathing, on the same frame. The
    /// player is facing the Nemesis with their camera behind them: turning to run is theirs.
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
        if (actor != null) actor.Release();
        if (pursuit != null) pursuit.Begin(config);
        if (chaseRestart != null) chaseRestart.Begin(stage.chaseCheckpoint);

        // Normally under way since the reveal's first frame; this is the net if it never got there.
        BeginFogCycle();

        // The breathing starts open (the reveal opened it): the way to the gate is the first thing
        // the player sees, and the first thing the fog does from here is close in.
        if (fogCycle != null) fogCycle.Run();

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

    // ── Capture: the chase starts over ──────────────────────────────────────

    /// <summary>
    /// The player is back at Player_Spot, the screen still black: the chase is reset to how it
    /// started — the fog closed, the path from its first door, the gate shut again. The gate opens
    /// on its own schedule once the player is up.
    /// </summary>
    private void HandleChaseRespawned()
    {
        if (phase != Phase.Escape) return;

        CancelSequence();
        if (fogCycle != null) fogCycle.Restart();
        CancelEndGate();
        if (stage.endGate != null) stage.endGate.CloseImmediate();
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
    /// The player crossed the WinTrigger. During the chase this plays the last shot before the win
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
    /// The gate, from the corridor side, without the player: the Nemesis runs at it, it slams down
    /// in its face with a burst of dust, and the Nemesis is left standing at it, stuck on the wrong
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

        // The fog opens for the shot and stays open: the gate has to read.
        if (fogCycle != null) fogCycle.Hold(open: true);

        bool hasNemesis = actor != null && actor.TryTakeControl();
        if (hasNemesis)
        {
            actor.WarpTo(stage.nemesisGateStart);
            actor.RunTo(stage.nemesisGateStop);

            // Once there it turns to the gate and stays.
            if (stage.endGate != null) actor.FaceTowards(stage.endGate.transform);
        }

        if (gateShot != null) gateShot.GoLive(hasNemesis ? actor.Body : null);

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
    /// The run is over — the win after the last shot, or a defeat (a module running out) at any
    /// point after the opening. What the escape still has running is stopped here: without this
    /// the corridor kept flickering, the fog kept cycling and the alarm kept sounding behind the
    /// result screen.
    /// </summary>
    private void HandleGameResult(GameResultModel result)
    {
        if (phase == Phase.Idle || phase == Phase.Opening || phase == Phase.Done) return;

        if (phase == Phase.Ending)
        {
            // The last shot stays up, frozen with the game: only what would still be heard goes.
            StopChaseSystems();
        }
        else
        {
            // A defeat in the middle of the reveal drops its shot; the player stays as the defeat
            // left them.
            CancelSequence();
            if (playerTurn != null) playerTurn.Stop();
            if (phase == Phase.Reveal) TearDownCinematic(releasePlayer: false);
            EndEscapeSystems();
        }

        UnregisterWinPresenter();
        phase = Phase.Done;
    }

    private void EndEscapeSystems()
    {
        if (fogCycle != null) fogCycle.End();
        if (corridorFlicker != null) corridorFlicker.End();
        StopChaseSystems();
    }

    private void StopChaseSystems()
    {
        if (pursuit != null) pursuit.End();
        if (chaseRestart != null) chaseRestart.End();
        if (escapeAudio != null) escapeAudio.Stop();
    }

    // ── Timeline plumbing ───────────────────────────────────────────────────

    private void PlayTimeline(PlayableDirector timeline, Action<PlayableDirector> onStopped)
    {
        activeTimeline = timeline;

        // None: the director stops on the last frame, which is what raises 'stopped'.
        timeline.extrapolationMode = DirectorWrapMode.None;
        timeline.stopped += onStopped;
        timeline.time = 0d;
        timeline.Play();
    }

    /// <summary>
    /// Runs the beats of the timeline that have not fired yet, in time order. Normally none:
    /// retroactive markers already fired on a skip. It is the net under that guarantee.
    /// </summary>
    private void FinishPendingBeats(PlayableDirector timeline)
    {
        TimelineAsset asset = timeline != null ? timeline.playableAsset as TimelineAsset : null;
        if (asset == null || asset.markerTrack == null) return;

        List<EscapeBeatMarker> markers = new List<EscapeBeatMarker>();
        foreach (IMarker marker in asset.markerTrack.GetMarkers())
        {
            if (marker is EscapeBeatMarker beatMarker) markers.Add(beatMarker);
        }

        markers.Sort((a, b) => a.time.CompareTo(b.time));
        foreach (EscapeBeatMarker marker in markers) OnBeat(marker.Beat);
    }

    // ── Beats ───────────────────────────────────────────────────────────────

    /// <summary>Called by <see cref="EscapeBeatReceiver"/> for each marker the playhead crosses.
    /// Each beat runs once per playthrough.</summary>
    public void OnBeat(EscapeBeat beat)
    {
        if (!firedBeats.Add(beat)) return;

        switch (beat)
        {
            case EscapeBeat.AlarmStart:
                if (escapeAudio != null) escapeAudio.Begin(config);
                // The lamps start failing with the alarm, and keep failing to the end of the escape
                // (EndEscapeSystems).
                if (corridorFlicker != null && !corridorFlicker.IsRunning) corridorFlicker.Begin(config);
                EscapeSocketAlarmLight socketLight = AlarmLightOfLastSocket();
                if (socketLight != null) socketLight.Activate();
                break;

            case EscapeBeat.PlayerOpensSafeDoor:
                if (stage.safeDoor != null) stage.safeDoor.OpenDoor();
                break;

            case EscapeBeat.LockCorridorDoors:
                if (corridorLock == null) break;
                if (skipping) corridorLock.LockAllNow();
                else corridorLock.LockAll(config);
                break;

            default:
                Debug.LogWarning($"[{nameof(EscapeSequenceDirector)}] Beat '{beat}' belonged to the " +
                                 "old opening and does nothing now: delete its marker from the " +
                                 "Timeline.", this);
                break;
        }
    }

    private EscapeSocketAlarmLight AlarmLightOfLastSocket()
    {
        if (lastInsertedSocket == null) return null;

        EscapeSocketAlarmLight light = lastInsertedSocket.GetComponent<EscapeSocketAlarmLight>();
        return light != null ? light : null;
    }

    private SocketInteractable NearestSocket(Transform from)
    {
        SocketInteractable best = null;
        float bestDistance = float.MaxValue;

        foreach (SocketInteractable socket in sockets)
        {
            if (socket == null) continue;

            float distance = from != null ? Vector3.Distance(from.position, socket.transform.position) : 0f;
            if (distance >= bestDistance) continue;

            best = socket;
            bestDistance = distance;
        }

        return best;
    }

    private void PositionMacroCamera()
    {
        Transform playerTransform = PlayerRegistry.CurrentTransform;

        // Started by the test key: no core went in, so frame the socket nearest to the player.
        if (lastInsertedSocket == null) lastInsertedSocket = NearestSocket(playerTransform);
        if (socketMacroCamera == null || lastInsertedSocket == null) return;

        Vector3 socket = lastInsertedSocket.transform.position;

        // In front of the socket, on the side the player stands: that is the side it can be reached from.
        Vector3 toPlayer = playerTransform != null ? playerTransform.position - socket : lastInsertedSocket.transform.forward;
        toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.forward;

        Vector3 position = socket + toPlayer * macroDistance + Vector3.up * macroHeight;
        Vector3 focus = socket + Vector3.up * macroLookHeight;

        socketMacroCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
    }

    // ── Player ──────────────────────────────────────────────────────────────

    private Vector3 PlayerPosition()
    {
        Transform player = PlayerRegistry.CurrentTransform;
        if (player != null) return player.position;
        return stage.playerSpot != null ? stage.playerSpot.position : transform.position;
    }

    /// <summary>Turns the player where they stand to face a point, at once, with the look camera
    /// behind them. Only for a Stage without the turn piece.</summary>
    private static void FacePlayer(PlayerStateManager player, Vector3 point)
    {
        Vector3 to = point - player.transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion facing = Quaternion.LookRotation(to);
        player.TeleportTo(player.transform.position, facing);

        PlayerCameraController look = FindAnyObjectByType<PlayerCameraController>();
        if (look != null) look.FaceYaw(facing.eulerAngles.y);
    }

    /// <summary>Takes the player out of the picture (the last shot must not show them past the
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

    private static float HorizontalGap(Transform a, Transform b)
    {
        if (a == null || b == null) return float.MaxValue;

        Vector3 gap = a.position - b.position;
        gap.y = 0f;
        return gap.magnitude;
    }

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

        // A cinematic that starts right after another (the reveal with no trigger to wait for, the
        // test key) finds the last one's blend still on its way back: that one is cancelled, and the
        // blend saved before it — the gameplay one — is the one kept.
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
