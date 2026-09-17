using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Interaction system driven by the crosshair.
///
/// The cast is fired through the crosshair's exact viewport point (see
/// <see cref="SO_InteractionManager.CrosshairViewportPoint"/>) so what the reticle covers is
/// literally what gets picked. The REACH, however, is measured from the player and not from the
/// camera: this is a third person rig, the camera orbits ~3.4 m behind and above the character,
/// and a budget spent from there is mostly empty air between the lens and the player's hands.
/// <see cref="InteractionProbe"/> does both by starting the cast at the point of the crosshair ray
/// closest to the player — everything between the camera and the character (their own body, the
/// wall the Deoccluder pinched the camera into) is behind the start and simply cannot interfere.
///
/// Occlusion is resolved with two casts instead of one combined mask so that interaction volumes
/// may be triggers. A door's interaction box has to be a trigger: it is authored on the door root
/// and does not swing with the hinge, so as a solid collider it walls the doorway shut forever.
/// </summary>
public class InteractionManager : Singleton<InteractionManager>
{
    [Header("Config")]
    [Tooltip("Reach, layers, crosshair position and cast radius.")]
    [SerializeField] private SO_InteractionManager config;

    public SO_InteractionManager Config => config;

    private Camera playerCamera;

    private IInteractable currentInteractable;
    private IInteractable lastInteractable;
    public IInteractable CurrentInteractable => currentInteractable;

    // While non-null, this replaces the crosshair raycast as the active target. Used by
    // interactables that latch on once picked up (a push box the player is currently pushing) so
    // E keeps working even if the camera swings and the reticle slides off the mesh. Cleared
    // with ClearForcedInteractable(this) so a stale owner cannot steal the lock.
    private IInteractable forcedInteractable;

    // Cooldown between E presses to avoid double activations.
    private const float InteractCooldown = 0.2f;
    private float lastInteractTime = -999f;

    private void Awake()
    {
        CreateSingleton(true);
        SuscribeToOnSceneLoadedEvent();
        RefreshCamera();
    }

    private void Update()
    {
        RefreshCamera();

        // If a modal UI is open or the game is paused, we do not process interactions.
        if (PauseManager.IsGameplayInputBlocked)
        {
            if (currentInteractable != null)
            {
                currentInteractable = null;
                lastInteractable = null;
                InteractionEvents.TargetChanged(null);
            }
            return;
        }

        UpdateCurrentInteractable();
        Interact();
    }

    private void SuscribeToOnSceneLoadedEvent()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshCamera();
    }

    private void RefreshCamera()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }

    private void UpdateCurrentInteractable()
    {
        IInteractable detected = forcedInteractable != null
            ? forcedInteractable
            : RaycastForInteractable();

        if (detected != lastInteractable)
        {
            lastInteractable = detected;
            currentInteractable = detected;

            InteractionEvents.TargetChanged(currentInteractable);
        }
    }

    /// <summary>
    /// Pins <paramref name="target"/> as the active interactable until <see cref="ClearForcedInteractable"/>
    /// is called with the same reference. Used by interactables that own the interaction until the
    /// player explicitly ends it — a push box being pushed keeps responding to E even if the crosshair
    /// slides off it. Passing null is equivalent to clearing.
    /// </summary>
    /// <summary>
    /// Pins <paramref name="target"/> as the active interactable until <see cref="ClearForcedInteractable"/>
    /// is called with the same reference. Used by interactables that own the interaction until the
    /// player explicitly ends it — a push box being pushed keeps responding to E even if the crosshair
    /// slides off it. Passing null is equivalent to clearing.
    ///
    /// Does NOT touch <c>lastInteractable</c>: the natural diff in the next Update fires
    /// TargetChanged only when the target actually changes. If the pinned target is the same as
    /// what the player was already looking at (typical when grabbing a box you were aiming at),
    /// the caller should follow up with <see cref="InteractionEvents.RequestPromptRefresh"/> to
    /// refresh the prompt text — the target did not change, only its state did.
    /// </summary>
    public void SetForcedInteractable(IInteractable target)
    {
        forcedInteractable = target;
    }

    /// <summary>
    /// Clears the forced target only if it is still <paramref name="owner"/>. Prevents a stale caller
    /// that lost ownership (e.g. the box was locked into its basket in the meantime) from unpinning
    /// a different, currently-forced interactable.
    /// </summary>
    /// <summary>
    /// Clears the forced target only if it is still <paramref name="owner"/>. Prevents a stale caller
    /// that lost ownership (e.g. the box was locked into its basket in the meantime) from unpinning
    /// a different, currently-forced interactable.
    ///
    /// Deliberately leaves <c>lastInteractable</c> alone so the next Update's raycast fires
    /// TargetChanged(null) if the crosshair is no longer on the previously-pinned target, and
    /// stays silent when it still is. In the "still on it" case the caller should also fire
    /// <see cref="InteractionEvents.RequestPromptRefresh"/> to update the prompt text for the
    /// new state of the same interactable (e.g. "push" → "stop pushing").
    /// </summary>
    public void ClearForcedInteractable(IInteractable owner)
    {
        if (owner == null || ReferenceEquals(forcedInteractable, owner))
        {
            forcedInteractable = null;
        }
    }


    private IInteractable RaycastForInteractable()
    {
        // Camera.main is the CinemachineBrain camera, i.e. the one actually rendering the frame
        // the crosshair is drawn over — not the CinemachineCamera rig, whose transform lags the
        // brain by a frame and is not what ScreenPointToRay would agree with.
        return InteractionProbe.Find(playerCamera, PlayerRegistry.Current, config, out _);
    }

    private void Interact()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (Time.unscaledTime - lastInteractTime < InteractCooldown) return;
        if (currentInteractable == null) return;
        if (!currentInteractable.CanInteract())
        {
            // Give the interactable a chance to react to the refused press (e.g. a locked door
            // rattling instead of being silent). Cooldown is still bumped so a held key does not
            // fire the feedback every frame.
            lastInteractTime = Time.unscaledTime;
            if (currentInteractable is BaseRangeInteractable blocked)
                blocked.OnInteractAttemptBlocked();
            return;
        }

        lastInteractTime = Time.unscaledTime;

        IInteractable interactableToUse = currentInteractable;
        bool wasRepeatable = interactableToUse.IsRepeatable();

        interactableToUse.Interact();
        InteractionEvents.Interacted(interactableToUse);

        if (!wasRepeatable)
        {
            currentInteractable = null;
            lastInteractable = null;
            InteractionEvents.TargetChanged(null);
        }
    }
}
