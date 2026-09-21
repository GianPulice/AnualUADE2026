using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Runs the escape sequence from the third core to the gate. It is the conductor and nothing else:
/// it decides WHEN each piece starts, and every piece does its own work (<see cref="EscapeFogCycle"/>,
/// <see cref="NemesisCinematicActor"/>, <see cref="NemesisEscapePursuit"/>, <see cref="EscapeAudio"/>,
/// <see cref="EscapeCorridorLock"/>).
///
///   Trigger    the hub puzzle (the three cores) completes.
///   Opening    a Timeline (Pasos 1-3): the macro of the socket, the Nemesis leaving the side door,
///              the corridor locking, the player sprinting into place while his own camera orbits to
///              the nape. Its shots are Cinemachine clips and its actions are
///              <see cref="EscapeBeatMarker"/>s — retime or swap them in the Timeline. F skips it.
///   Handoff    on the frame the timeline ends: the player gets control, the Nemesis starts its
///              permanent trot (<see cref="NemesisEscapePursuit"/>) and the fog cycle starts (Paso 6).
///   Escape     gameplay (Pasos 4-7): the fog cycle walks the player door to door, with the Nemesis
///              chasing the whole way.
///   Gate       the last door of the route only raises <see cref="GateReached"/> / onGateReached. There
///              is no ending here: the Nemesis keeps chasing, and the level's own WinTrigger ends
///              the run when the player crosses the gate.
///
/// SKIP: the playhead jumps to the end and every marker it passes still fires (they are
/// retroactive), then anything a deleted marker would have done is finished here. The result of a
/// skip is the same state the cinematic ends in.
///
/// What is NOT covered: resuming the escape from a saved game (if the trigger puzzle is already
/// complete when the scene loads, nothing plays), and the floor / wall fluid marks of the path
/// (art).
///
/// SETUP: Tools ▸ Escape Sequence ▸ Create Scene Objects builds all of this.
/// </summary>
public class EscapeSequenceDirector : MonoBehaviour
{
    /// <summary>The scene objects the beats act on. Markers in the Timeline name WHAT happens;
    /// these say WHERE.</summary>
    [Serializable]
    public class Stage
    {
        [Header("Player (2B)")]
        [Tooltip("Dónde recupera el control el jugador (posición y hacia dónde mira). Mirando " +
                 "hacia donde viene el Nemesis: la cámara termina en su nuca mirando eso.")]
        public Transform playerSpot;

        [Tooltip("De dónde arranca a correr el jugador hasta el punto de arriba, mirando hacia él. " +
                 "Corre a su velocidad de sprint: la distancia a 4.5 m/s son los segundos de la carrera.")]
        public Transform playerRunStart;

        [Tooltip("La puerta de la zona segura: la que se abre en 2B y NO se traba.")]
        public DoorInteractable safeDoor;

        [Tooltip("El portón del final del pasillo. Se abre de una al arrancar la cinemática: el " +
                 "escape muestra la salida, y un portón cerrado se lee como callejón sin salida. " +
                 "Aparte sigue abriéndose solo cuando se completa su puzzle.")]
        public PuzzleGate endGate;

        [Header("Nemesis (2A)")]
        [Tooltip("Detrás de la puerta lateral, fuera de cuadro: de acá sale.")]
        public Transform nemesisHidden;

        [Tooltip("La puerta lateral del pasillo, frente al montacargas.")]
        public DoorInteractable nemesisSideDoor;

        [Tooltip("En el umbral de la puerta lateral: hasta acá camina al salir.")]
        public Transform nemesisDoorway;

        [Tooltip("El Nemesis gira hacia este marcador para 'mirar a la izquierda'.")]
        public Transform nemesisLookLeft;

        [Tooltip("El Nemesis gira hacia este marcador para 'mirar a la derecha'.")]
        public Transform nemesisLookRight;

        [Tooltip("Sale de cuadro corriendo hacia acá (a la derecha).")]
        public Transform nemesisRunExit;

        [Header("Nemesis (2B)")]
        [Tooltip("Donde aparece por el pasillo cuando la cámara panea. Lejos del jugador.")]
        public Transform nemesisApproachStart;

        [Tooltip("Hacia dónde corre desde ahí. Suele ser el jugador: se le pasa por delante y el " +
                 "control vuelve con el Nemesis ya en carrera.")]
        public Transform nemesisApproachEnd;

    }

    [SerializeField] private SO_EscapeSequenceConfig config;

    [Header("Timelines")]
    [Tooltip("Pasos 1-3. Se puede editar en Window ▸ Sequencing ▸ Timeline.")]
    [SerializeField] private PlayableDirector openingTimeline;

    [Header("Cinematic camera")]
    [Tooltip("La cámara del macro del socket. Se coloca sola frente al ÚLTIMO socket encastrado " +
             "al arrancar; los otros planos son cámaras fijas que ubicás vos en la escena.")]
    [SerializeField] private CinemachineCamera socketMacroCamera;

    [SerializeField, Min(0.1f)] private float macroDistance = 0.6f;
    [SerializeField] private float macroHeight = 0.2f;
    [SerializeField] private float macroLookHeight = 0.05f;

    [Header("Scene pieces")]
    [SerializeField] private Stage stage = new Stage();
    [SerializeField] private EscapeFogCycle fogCycle;
    [SerializeField] private EscapeAudio escapeAudio;
    [SerializeField] private EscapeCorridorLock corridorLock;
    [SerializeField] private NemesisCinematicActor actor;
    [SerializeField] private NemesisEscapePursuit pursuit;

    [Tooltip("Una captura durante el escape es game over: sin checkpoint, sin respawn.")]
    [SerializeField] private EscapeCaptureGameOver captureGameOver;
    [SerializeField] private PlayerCinematicRun playerRun;
    [SerializeField] private EscapeCameraPan cameraPan;

    [Tooltip("Los tres sockets de los núcleos. Vacío = se buscan solos en la escena.")]
    [SerializeField] private SocketInteractable[] sockets = new SocketInteractable[0];

    [Header("Gate trigger")]
    [Tooltip("Se dispara cuando el jugador llega a la última puerta del recorrido (el portón). No " +
             "termina nada: el Nemesis sigue persiguiendo. Colgale acá lo que quieras.")]
    [SerializeField] private UnityEvent onGateReached = new UnityEvent();

    /// <summary>The player reached the last door of the route (the gate).</summary>
    public event Action GateReached;

    private enum Phase { Idle, Opening, Escape, Done }

    private Phase phase = Phase.Idle;
    private PlayableDirector activeTimeline;
    private bool skipping;

    private readonly HashSet<EscapeBeat> firedBeats = new HashSet<EscapeBeat>();

    private SocketInteractable lastInsertedSocket;
    private PlayerStateManager lockedPlayer;

    private CinemachineBrain brain;
    private CinemachineBlendDefinition previousBlend;
    private bool brainOverridden;

    public bool IsPlaying => phase == Phase.Opening;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    // A static event: subscribed in Awake and released in OnDestroy, like the rest of the project.
    private void Awake()
    {
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
        CheckpointManager.OnRespawned += HandleRespawned;
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        CheckpointManager.OnRespawned -= HandleRespawned;

        if (fogCycle != null) fogCycle.RouteCompleted -= HandleRouteCompleted;
        foreach (SocketInteractable socket in sockets)
        {
            if (socket != null) socket.Inserted -= HandleAnySocketInserted(socket);
        }

        // No coroutines here: the object is going away. Whatever the cinematic held is given back now.
        CinematicState.End();
        if (lockedPlayer != null) lockedPlayer.IsDisabled = false;
        if (brainOverridden && brain != null) brain.DefaultBlend = previousBlend;
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
    /// tearing down whatever the previous one left (the escape's systems, the locked doors).
    /// Ignored while a cinematic is playing.
    /// </summary>
    public void StartForTest()
    {
        if (config == null || IsPlaying) return;

        EndEscapeSystems();
        if (corridorLock != null) corridorLock.UnlockAll();

        phase = Phase.Opening;
        StartCoroutine(StartOpeningNextFrame());
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
        if (!IsPlaying || activeTimeline == null || !SkipPressed()) return;
        Skip();
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
        // The way out is open before the camera ever shows it.
        if (stage.endGate != null) stage.endGate.OpenNow();

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

        // The Nemesis waits behind the side door, out of every shot, until its beat.
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
    /// The handoff. Everything below happens on the frame the timeline stops, which is the frame
    /// the camera cuts back to the gameplay one: control, the trot, the fog cycle and the chase UI
    /// all start together (Paso 3 and Paso 6).
    /// </summary>
    private void FinishOpening()
    {
        FinishPendingBeats(openingTimeline);

        // The alarm must be on whatever happened to its marker.
        if (escapeAudio != null) escapeAudio.Begin(config);
        EscapeSocketAlarmLight alarmLight = AlarmLightOfLastSocket();
        if (alarmLight != null) alarmLight.ActivateNow();
        if (corridorLock != null) corridorLock.LockAllNow();
        if (stage.safeDoor != null) stage.safeDoor.OpenDoor();

        // Wherever the run and the pan were (a skip cuts them short), control comes back at the
        // spot, looking where the pan ends.
        if (playerRun != null) playerRun.Finish(stage.playerSpot);
        else PlacePlayerAt(stage.playerSpot);
        if (cameraPan != null) cameraPan.Snap();

        // A skip cut the Nemesis's run short: it goes where the run would have ended.
        if (skipping && actor != null) actor.WarpTo(stage.nemesisApproachEnd);

        EndCinematic();
        phase = Phase.Escape;

        if (actor != null) actor.Release();
        if (pursuit != null) pursuit.Begin(config);
        if (captureGameOver != null) captureGameOver.Begin();

        if (fogCycle != null)
        {
            fogCycle.RouteCompleted -= HandleRouteCompleted;
            fogCycle.RouteCompleted += HandleRouteCompleted;
            fogCycle.Begin(config);
        }
    }

    // ── Gate trigger ────────────────────────────────────────────────────────

    /// <summary>
    /// The player got to the last door of the route. This only reports it: nothing ends and nothing
    /// is cut, so the Nemesis keeps chasing until the level's own WinTrigger, past the gate, takes
    /// over.
    /// </summary>
    private void HandleRouteCompleted()
    {
        if (phase != Phase.Escape) return;

        GateReached?.Invoke();
        onGateReached.Invoke();
    }

    private void EndEscapeSystems()
    {
        if (fogCycle != null) fogCycle.End();
        if (pursuit != null) pursuit.End();
        if (captureGameOver != null) captureGameOver.End();
        if (escapeAudio != null) escapeAudio.Stop();
    }

    // ── Respawn ─────────────────────────────────────────────────────────────

    private void HandleRespawned(Checkpoint checkpoint)
    {
        // A capture during the escape sends the player back; the route starts over from its first
        // door. (What the escape SHOULD do after a capture is not in the script: this is the
        // minimum that leaves nothing half-lit.)
        if (phase != Phase.Escape || fogCycle == null) return;

        if (fogCycle.IsRunning) fogCycle.Restart();
        else fogCycle.Begin(config);
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
    /// Jumps the playhead to the end (retroactive markers fire on the way) and stops the timeline,
    /// which lands in the same code as a cinematic that ended by itself.
    /// </summary>
    private void Skip()
    {
        skipping = true;

        PlayableDirector timeline = activeTimeline;
        timeline.time = timeline.duration;
        timeline.Evaluate();
        timeline.Stop();
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
                EscapeSocketAlarmLight socketLight = AlarmLightOfLastSocket();
                if (socketLight != null) socketLight.Activate();
                break;

            case EscapeBeat.NemesisOpenDoor:
                OpenNemesisDoor();
                break;

            case EscapeBeat.NemesisWalkOut:
                if (actor != null) actor.WalkTo(stage.nemesisDoorway);
                break;

            case EscapeBeat.NemesisLookLeft:
                if (actor != null) actor.FaceTowards(stage.nemesisLookLeft);
                break;

            case EscapeBeat.NemesisLookRight:
                if (actor != null) actor.FaceTowards(stage.nemesisLookRight);
                break;

            case EscapeBeat.NemesisRunAway:
                if (actor != null) actor.RunTo(stage.nemesisRunExit);
                break;

            case EscapeBeat.PlacePlayer:
                PlacePlayerAt(stage.playerRunStart != null ? stage.playerRunStart : stage.playerSpot);
                break;

            case EscapeBeat.PlayerRunToSpot:
                if (playerRun != null) playerRun.RunTo(stage.playerSpot);
                break;

            case EscapeBeat.PlayerCameraPan:
                if (cameraPan == null || activeTimeline == null) break;

                // Until the timeline ends, so the pan lands as control comes back.
                cameraPan.Begin((float)(activeTimeline.duration - activeTimeline.time));
                break;

            case EscapeBeat.PlayerOpensSafeDoor:
                if (stage.safeDoor != null) stage.safeDoor.OpenDoor();
                break;

            case EscapeBeat.LockCorridorDoors:
                if (corridorLock == null) break;
                if (skipping) corridorLock.LockAllNow();
                else corridorLock.LockAll(config);
                break;

            case EscapeBeat.NemesisApproach:
                if (actor == null) break;
                actor.WarpTo(stage.nemesisApproachStart);
                actor.RunTo(stage.nemesisApproachEnd);
                break;

        }
    }

    private EscapeSocketAlarmLight AlarmLightOfLastSocket()
    {
        if (lastInsertedSocket == null) return null;

        EscapeSocketAlarmLight light = lastInsertedSocket.GetComponent<EscapeSocketAlarmLight>();
        return light != null ? light : null;
    }

    private void OpenNemesisDoor()
    {
        DoorInteractable door = stage.nemesisSideDoor;
        if (door == null) return;

        // The Nemesis's own opening swings the leaf without spending the player's key or marking the
        // door as opened for good; the player's OpenDoor is only the fallback for a door it may not force.
        if (!door.TryOpenForNemesis()) door.OpenDoor();
    }

    private void PlacePlayerAt(Transform marker)
    {
        if (marker == null) return;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null) return;

        player.TeleportTo(marker.position, marker.rotation);

        // The look camera behind the player, not wherever it was before the cut.
        PlayerCameraController look = FindAnyObjectByType<PlayerCameraController>();
        if (look != null) look.FaceYaw(marker.eulerAngles.y);
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

        // Every cut in the script is a cut, including the ones into and out of the timeline.
        brain = CinemachineBrain.ActiveBrainCount > 0 ? CinemachineBrain.GetActiveBrain(0) : null;
        if (brain != null && !brainOverridden)
        {
            previousBlend = brain.DefaultBlend;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            brainOverridden = true;
        }
    }

    private void EndCinematic()
    {
        CinematicState.End();

        if (lockedPlayer != null) lockedPlayer.IsDisabled = false;
        lockedPlayer = null;

        // Put back two frames later, when the brain has already made the cut back to gameplay.
        if (brainOverridden && brain != null) StartCoroutine(RestoreBlendLater(brain, previousBlend));
        brainOverridden = false;
    }

    private static IEnumerator RestoreBlendLater(CinemachineBrain target, CinemachineBlendDefinition blend)
    {
        yield return null;
        yield return null;
        if (target != null) target.DefaultBlend = blend;
    }
}
