using UnityEngine;
using UnityEngine.SceneManagement;

// Interaction system based on a raycast from the centre of the camera.
// The camera casts a ray forward with the distance configured in SO_InteractionManager.
// If the ray hits a Collider hanging off an IInteractable and that one can be interacted with,
// it becomes the "current" one and the UI shows the prompt. The E key runs the interaction.
public class InteractionManager : Singleton<InteractionManager>
{
    [Header("Config")]
    [Tooltip("Distance and layers for the raycast.")]
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
        if (playerCamera == null) return null;
        if (config == null) return null;

        float distance = config.InteractionDistance;
        LayerMask combinedMask = config.InteractableLayers | config.BlockingLayers;

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = playerCamera.transform.forward;

        // SphereCast: a "thick" ray with a small radius. Makes aiming at small items
        // (pickups on the floor, valves) more forgiving without losing directionality.
        const float sphereRadius = 0.1f;

        if (!Physics.SphereCast(origin, sphereRadius, direction, out RaycastHit hit, distance, combinedMask, QueryTriggerInteraction.Ignore))
            return null;

        // We look for an IInteractable on the collider itself or on its parents.
        // If we find one, it is valid (a child of the prefab is not on the Interactable layer
        // but its root is, and it must still be activatable).
        // If we do not find one, the first hit is a wall / blocking object.
        IInteractable interactable =
            hit.collider.GetComponent<IInteractable>() ??
            hit.collider.GetComponentInParent<IInteractable>();

        return interactable;
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
