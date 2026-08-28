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
        IInteractable detected = RaycastForInteractable();

        if (detected != lastInteractable)
        {
            lastInteractable = detected;
            currentInteractable = detected;

            InteractionEvents.TargetChanged(currentInteractable);
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
        if (!currentInteractable.CanInteract()) return;

        lastInteractTime = Time.unscaledTime;

        IInteractable interactableToUse = currentInteractable;
        bool wasRepeatable = interactableToUse.IsRepeatable();

        interactableToUse.Interact();

        if (!wasRepeatable)
        {
            currentInteractable = null;
            lastInteractable = null;
            InteractionEvents.TargetChanged(null);
        }
    }
}
