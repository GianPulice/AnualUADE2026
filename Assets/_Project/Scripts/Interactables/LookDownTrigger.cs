using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// A spot where the player can stop and "look down": a trigger volume with its own camera. While the
/// player stands in the volume the interaction prompt reads "[E] Look down"; pressing E blends to the
/// associated camera, holds it for a few seconds and blends back. The player is frozen for the whole
/// shot, look input included, so control comes back facing the way it was left.
///
/// E again ("Go back") cuts the shot short, even half way through the blend in. It cannot be started
/// in the middle of a chase (<see cref="NemesisEvents.IsChasing"/>: the prompt hides and comes back
/// when the chase ends), and a chase that begins during the shot ends it at once, giving the body
/// back without waiting for the camera.
///
/// WHY NOT A PLAIN INTERACTABLE. Every other interactable is found by the crosshair, and a view of
/// the drop is something you offer to a place, not to a prop: the player can be facing the railing,
/// the stairs or the wall and still be at the spot. So the volume registers itself with the
/// <see cref="InteractionManager"/> as the ZONE interactable — the prompt it shows when the crosshair
/// is on nothing else. A door or a lever under the crosshair still wins, and so does anything pinned
/// with <see cref="InteractionManager.SetForcedInteractable"/> (a push box in hand, a locker).
///
/// THE CAMERA. A <see cref="CinemachineCamera"/> with no Position or Rotation behaviours, so its own
/// transform is the shot: put it in the Scene view where the view should be and use Align With View
/// (Ctrl+Shift+F). It is switched off on Awake, raised above the player rig for the shot and switched
/// off again after; the Cinemachine brain blends both ways with its Default Blend — do NOT hand-lerp
/// the main camera, <see cref="PlayerCameraController"/> and <see cref="SO_CameraConfig"/> own the
/// normal rig. The hold starts when the blend IN has landed and the player gets control back when the
/// blend OUT has, so changing the brain's blend never cuts a shot short.
///
/// SETUP:
///   Trigger root   ← this component + a Collider (Is Trigger is forced on when the component is added).
///   \-- Look Camera ← a CinemachineCamera, aimed down. GameObject > WIRED > Look Down Trigger builds both.
/// One trigger per spot: the shot is authored per place.
///
/// Every way the shot can end that is not its timer — a capture, a respawn, a cinematic, the trigger
/// going away — hands the body and the camera back at once.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Interaction/Look Down Trigger")]
public class LookDownTrigger : BaseRangeInteractable
{
    [Header("Camera")]
    [Tooltip("The shot. A CinemachineCamera with no Position/Rotation behaviours: its own transform " +
             "is the framing. Switched off on Awake and raised only while the player looks down.")]
    [SerializeField] private CinemachineCamera lookCamera;

    [Tooltip("Priority given to the camera while the shot is on. It has to beat the player rig and " +
             "stay under the module-explosion defeat camera (1000), which is allowed to cut over everything.")]
    [SerializeField] private int shotPriority = 100;

    [Header("Timing")]
    [Tooltip("Seconds the shot is held once the blend in has landed, unless the player presses E " +
             "first. The blends themselves come from the Cinemachine brain's Default Blend.")]
    [SerializeField, Min(0.1f)] private float holdSeconds = 3f;

    [Header("Prompt")]
    [SerializeField] private string prompt = "Look down";
    [Tooltip("Shown while the shot is on: E cuts it short.")]
    [SerializeField] private string leavePrompt = "Go back";

    [Header("Detection")]
    [Tooltip("Tag of the GameObject that fires the trigger. Default: Player.")]
    [SerializeField] private string playerTag = "Player";

    // ── Zone bookkeeping ────────────────────────────────────────────────────

    // The triggers the player is in, in the order they were entered: the last one is the one that
    // offers its prompt. Same shape as CameraAreaZone.
    private static readonly List<LookDownTrigger> occupied = new List<LookDownTrigger>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => occupied.Clear();

    private bool isPlayerInside;

    // ── Shot ────────────────────────────────────────────────────────────────

    // Longest the shot waits for a blend to land. The brain's own blend is a couple of seconds at
    // most; this is only the net under a brain that never reports it has finished.
    private const float MaxBlendWait = 8f;

    private PlayerStateManager player;
    private CancellationTokenSource shotCts;
    private bool shotRunning;

    // The shot has stopped being held: the timer ran out or the player pressed E, and the camera is
    // on its way back. The prompt is gone from here until control returns — there is nothing left
    // to cut short.
    private bool ending;

    private bool cameraRaised;
    private int restPriority;

    /// <summary>True from pressing E until control is back, blend out included.</summary>
    public bool IsShotRunning => shotRunning;

    protected override void Awake()
    {
        base.Awake();

        // Off until the shot. A live CinemachineCamera is a candidate for the brain whatever its
        // priority, and on a tie the most recently enabled one wins — a camera left enabled in the
        // scene could take the view off the player rig the moment the level loads. The component and
        // not the GameObject, so it stays safe even if the camera sits on the trigger's own root.
        if (lookCamera != null)
        {
            restPriority = lookCamera.Priority;
            lookCamera.enabled = false;

            // It is still the player looking: the camera frame (area name, recording dot, module
            // readout) stays on over this shot instead of dropping and coming back with static.
            PlayerCameraFeed.RegisterPlayerView(lookCamera);
        }
        else
        {
            Debug.LogError($"[{nameof(LookDownTrigger)}] '{name}' has no Look Camera, so there is " +
                           "nothing to look through. The trigger has been disabled.", this);
            enabled = false;
        }
    }

    private void Reset()
    {
        // Is Trigger as soon as the component is added, so it cannot be forgotten.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnEnable()
    {
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        CheckpointManager.OnRespawned += HandleRespawned;
        NemesisEvents.OnChaseStarted += HandleChaseChanged;
        NemesisEvents.OnChaseEnded += HandleChaseChanged;
    }

    private void OnDisable()
    {
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        CheckpointManager.OnRespawned -= HandleRespawned;
        NemesisEvents.OnChaseStarted -= HandleChaseChanged;
        NemesisEvents.OnChaseEnded -= HandleChaseChanged;

        // Switched off (or unloaded) with the player inside, or in the middle of the shot. Nothing
        // else is going to give the body, the look input and the camera back.
        if (shotRunning) EndShotNow();
        LeaveZone();
    }

    private void OnDestroy()
    {
        // Not in OnDisable: a shot cut short by switching the trigger off is still blending home,
        // and the frame must not flicker off for the second half of it.
        PlayerCameraFeed.UnregisterPlayerView(lookCamera);

        shotCts?.Dispose();
        shotCts = null;
    }

    private void Update()
    {
        // A cinematic began under the shot. Its cameras outrank this one anyway, but the player
        // would come out of it frozen: the cinematic wins and the shot lets go.
        if (shotRunning && CinematicState.IsPlaying) EndShotNow();
    }

    private void HandleChaseChanged()
    {
        // The Nemesis started hunting during the shot: the player gets the body back at once —
        // no waiting for the blend out, the camera finds its own way home — and the prompt hides.
        if (shotRunning && NemesisEvents.IsChasing) EndShotNow();

        // Nothing re-evaluates the prompt when a chase starts or ends, and CanInteract just changed
        // its answer: hide it for the chase, bring it back after.
        if (isPlayerInside) InteractionEvents.RequestPromptRefresh();
    }

    // ── Zone ────────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (isPlayerInside) return;   // Several colliders on the player.

        isPlayerInside = true;
        occupied.Remove(this);
        occupied.Add(this);
        PublishZonePrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        LeaveZone();
    }

    private void LeaveZone()
    {
        if (!isPlayerInside) return;

        isPlayerInside = false;
        occupied.Remove(this);

        // Hand the prompt back to the trigger the player is still in (volumes that overlap), or
        // clear it. Only if it is still ours: another zone may have claimed the slot in the meantime.
        if (!InteractionManager.Exists) return;
        if (occupied.Count > 0) PublishZonePrompt();
        else InteractionManager.Instance.ClearZoneInteractable(this);
    }

    private static void PublishZonePrompt()
    {
        if (!InteractionManager.Exists || occupied.Count == 0) return;
        InteractionManager.Instance.SetZoneInteractable(occupied[occupied.Count - 1]);
    }

    // ── Interactable ────────────────────────────────────────────────────────

    public override string GetPromptText() => shotRunning ? leavePrompt : prompt;

    public override bool IsRepeatable() => true;

    protected override bool CanInteractInCloseRange()
    {
        if (!isActiveAndEnabled || lookCamera == null) return false;

        // During the shot E cuts it short — until the camera is already on its way back, when there
        // is nothing left to cut and the prompt goes away until control returns.
        if (shotRunning) return !ending;

        // Not in the middle of a chase: there is no time to stop and look at the floor, and the
        // player would stand frozen in front of the Nemesis. The prompt hides, and comes back when
        // the chase ends (HandleChaseChanged).
        if (NemesisEvents.IsChasing) return false;

        // A scripted cinematic owns the player and the camera.
        if (CinematicState.IsPlaying) return false;

        PlayerStateManager p = ResolvePlayer();
        if (p == null) return false;

        // Captured, waking up, getting up off the floor.
        if (p.IsImmobilized) return false;

        // Hands full with the box, or inside / climbing into a hiding spot. Looking down from a
        // locker is not a thing, and the spot pins the camera itself.
        if (p.IsInteracting || p.IsHidden || p.IsHidingTransition) return false;

        return true;
    }

    protected override void OnInteract()
    {
        // E during the shot: go back. The shot ends the way it does when its timer runs out — the
        // camera blends back and the player gets control when it has — only sooner.
        if (shotRunning)
        {
            ending = true;
            InteractionEvents.RequestPromptRefresh();
            return;
        }

        shotCts?.Dispose();
        shotCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        ShotAsync(shotCts.Token).Forget();
    }

    // ── The shot ────────────────────────────────────────────────────────────
    //
    // UniTask and not a coroutine, per the project's async convention — and as with HidingSpot it is
    // also the correct choice: a coroutine is stopped dead when its GameObject is switched off, so
    // the cleanup would never run and the player would stand frozen. cancelImmediately on every wait
    // so that the finally runs inside Cancel() itself and not a frame later — a late finally could
    // end a shot that has already been replaced by another one.
    //
    // Scaled time on purpose: the pause menu freezes the shot the same way it freezes the blend.

    private async UniTaskVoid ShotAsync(CancellationToken token)
    {
        PlayerStateManager p = ResolvePlayer();
        if (p == null) return;

        shotRunning = true;
        ending = false;
        p.SetInputFrozen(true);
        RaiseCamera();

        // Pinned as the target for the length of the shot. The crosshair now belongs to the SHOT's
        // camera, which looks at the floor below and would happily find a lever there — and E, the
        // key that cuts the shot short, would pull it. The same mechanism HidingSpot and PushableBox
        // use to keep E on themselves.
        if (InteractionManager.Exists) InteractionManager.Instance.SetForcedInteractable(this);

        // The prompt turns into the way out ("Go back").
        InteractionEvents.RequestPromptRefresh();

        try
        {
            CinemachineBrain brain = CinemachineBrain.ActiveBrainCount > 0
                ? CinemachineBrain.GetActiveBrain(0)
                : null;

            // E is accepted from the first frame, blend in included: the player may change their
            // mind half way down, and the brain reverses the blend from wherever it has got to.
            await WaitForBlendAsync(brain, token, interruptible: true);

            float held = 0f;
            while (held < holdSeconds && !ending)
            {
                await UniTask.NextFrame(token, cancelImmediately: true);
                held += Time.deltaTime;
            }

            // From here there is nothing to cut short: the prompt goes.
            ending = true;
            InteractionEvents.RequestPromptRefresh();

            DropCamera();
            await WaitForBlendAsync(brain, token, interruptible: false);
        }
        finally
        {
            // Whatever ended the wait — the timer, E, or a cancel from a capture, a respawn, a
            // chase, a cinematic or the trigger going away — the shot is over and the player comes
            // back.
            FinishShot(p);
        }
    }

    /// <summary>
    /// Waits for the brain's current blend to land. The brain starts a blend in its own LateUpdate,
    /// so the first look is a frame after the camera was switched. An interruptible wait also gives
    /// up the moment the player asks to go back.
    /// </summary>
    private async UniTask WaitForBlendAsync(CinemachineBrain brain, CancellationToken token, bool interruptible)
    {
        await UniTask.NextFrame(token, cancelImmediately: true);
        if (brain == null) return;

        float waited = 0f;
        while (brain != null && brain.IsBlending && waited < MaxBlendWait)
        {
            if (interruptible && ending) return;

            await UniTask.NextFrame(token, cancelImmediately: true);
            waited += Time.deltaTime;
        }
    }

    /// <summary>
    /// Ends the shot with no waiting: the camera drops (the brain blends back by itself) and the
    /// player is released. Every way out that is not the timer lands here.
    /// </summary>
    private void EndShotNow()
    {
        // Cancelling runs the finally of ShotAsync right here (cancelImmediately), which is what
        // releases the player.
        if (shotCts != null && !shotCts.IsCancellationRequested) shotCts.Cancel();

        // Belt and braces: a shot that never got as far as its first await has no finally to run.
        FinishShot(ResolvePlayer());
    }

    private void FinishShot(PlayerStateManager p)
    {
        if (!shotRunning) return;
        shotRunning = false;
        ending = false;

        DropCamera();
        if (p != null) p.SetInputFrozen(false);

        // Only if it is still ours: a stale call must not unpin whatever holds the target now.
        if (InteractionManager.Exists) InteractionManager.Instance.ClearForcedInteractable(this);

        // The prompt comes back if the player is still standing in the volume.
        InteractionEvents.RequestPromptRefresh();
    }

    private void HandlePlayerCaptured(PlayerStateManager captured)
    {
        if (shotRunning) EndShotNow();
    }

    private void HandleRespawned(Checkpoint checkpoint)
    {
        // The capture that caused the respawn has already ended the shot. Belt and braces.
        if (shotRunning) EndShotNow();
    }

    // ── Camera ──────────────────────────────────────────────────────────────

    private void RaiseCamera()
    {
        if (lookCamera == null || cameraRaised) return;

        cameraRaised = true;
        restPriority = lookCamera.Priority;
        lookCamera.Priority = shotPriority;
        lookCamera.enabled = true;
    }

    private void DropCamera()
    {
        if (lookCamera == null || !cameraRaised) return;

        cameraRaised = false;
        // Disabled and not just demoted — see Awake. The brain blends back to the rig either way.
        lookCamera.enabled = false;
        lookCamera.Priority = restPriority;
    }

    private PlayerStateManager ResolvePlayer()
    {
        if (player == null) player = PlayerRegistry.Current;
        return player;
    }

#if UNITY_EDITOR
    // The pale blue of CameraAreaZone: both are volumes that belong to the camera, and they sit in
    // the same GizmoManager family ("cameras").
    private static readonly Color AreaFill = new Color(0.55f, 0.75f, 0.95f, 0.1f);
    private static readonly Color AreaWire = new Color(0.55f, 0.75f, 0.95f, 0.6f);
    private static readonly Color ShotColor = new Color(0.55f, 0.75f, 0.95f, 0.9f);

    // The volume is always there as a wire, so a spot can be found in the Scene view without picking
    // it out of the hierarchy (the GizmoManager's cameras box hides it with the rest). Thin lines and
    // no fill: a solid box over the level all the time hides it and catches the clicks meant for what
    // is inside. The fill, the line to the shot and its frustum come with the selection.
    //
    // Selecting the camera is the case that matters — that is what you do to frame the shot, and the
    // volume tells you where you are. Selecting the trigger is the other half of it.
    private void OnDrawGizmos()
    {
        DrawArea(filled: false);

        if (lookCamera != null && IsSelected(lookCamera.transform) && !IsSelected(transform))
        {
            DrawArea(filled: true);
            DrawShot();
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Unity can call this for a selected parent as well: that case is not ours to draw.
        if (!IsSelected(transform)) return;

        DrawArea(filled: true);
        DrawShot();
    }

    private static bool IsSelected(Transform target) =>
        target != null && UnityEditor.Selection.Contains(target.gameObject);

    private void DrawArea(bool filled)
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            if (filled)
            {
                Gizmos.color = AreaFill;
                Gizmos.DrawCube(box.center, box.size);
            }
            Gizmos.color = AreaWire;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else
        {
            Bounds bounds = col.bounds;
            if (filled)
            {
                Gizmos.color = AreaFill;
                Gizmos.DrawCube(bounds.center, bounds.size);
            }
            Gizmos.color = AreaWire;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }

    private void DrawShot()
    {
        if (lookCamera == null) return;

        Transform cam = lookCamera.transform;
        Gizmos.color = ShotColor;
        Gizmos.DrawLine(transform.position, cam.position);

        Gizmos.matrix = Matrix4x4.TRS(cam.position, cam.rotation, Vector3.one);
        Gizmos.DrawFrustum(Vector3.zero, lookCamera.Lens.FieldOfView, 3f, 0.1f, 16f / 9f);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
