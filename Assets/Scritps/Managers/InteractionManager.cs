using UnityEngine;
using UnityEngine.SceneManagement;

// Sistema de interaccion por raycast desde el centro de la camara.
// La camara emite un rayo hacia adelante con la distancia configurada en SO_InteractionManager.
// Si el rayo pega contra un Collider que cuelga de un IInteractable y este puede interactuarse,
// se vuelve el "current" y la UI muestra el prompt. Tecla E ejecuta la interaccion.
public class InteractionManager : Singleton<InteractionManager>
{
    [Header("Config")]
    [Tooltip("Distancia y layers para el raycast.")]
    [SerializeField] private SO_InteractionManager config;

    public SO_InteractionManager Config => config;

    private Camera playerCamera;

    private IInteractable currentInteractable;
    private IInteractable lastInteractable;
    public IInteractable CurrentInteractable => currentInteractable;

    // Cooldown entre pulsaciones de E para evitar dobles activaciones.
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

        // Si hay UI modal abierta o el juego esta en pausa, no procesamos interacciones.
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

        // SphereCast: rayo "grueso" con un radio chico. Hace que apuntar a items
        // chicos (pickups en el piso, valvulas) sea mas permisivo sin perder direccionalidad.
        const float sphereRadius = 0.1f;

        if (!Physics.SphereCast(origin, sphereRadius, direction, out RaycastHit hit, distance, combinedMask, QueryTriggerInteraction.Ignore))
            return null;

        // Buscamos IInteractable en el propio collider o en sus padres.
        // Si lo encontramos, es valido (un hijo del prefab no esta en layer Interactable
        // pero su raiz si lo es, igual debe poder activarse).
        // Si no lo encontramos, el primer hit es una pared / objeto bloqueante.
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
