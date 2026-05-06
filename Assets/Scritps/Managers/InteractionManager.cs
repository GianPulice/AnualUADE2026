using UnityEngine;
using UnityEngine.SceneManagement;

public class InteractionManager : Singleton<InteractionManager>
{
    [SerializeField] private SO_InteractionManager SO_interactionManager;

    private Camera playerCamera;

    private LayerMask interactionLayer;

    private IInteractable currentInteractable;

    public IInteractable CurrentInteractable => currentInteractable;


    void Awake()
    {
        CreateSingleton(true);
        InitializeInteractionManager();
        SuscribeToOnSceneLoadedEvent();
    }

    void Update()
    {
        DetectInteractable();
        Interact();
    }


    // No necesita desuscripcion porque es singleton
    private void SuscribeToOnSceneLoadedEvent()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }

    private void InitializeInteractionManager()
    {
        interactionLayer = LayerMask.GetMask("Interactable");
    }

    private void DetectInteractable()
    {
        if (playerCamera == null) return;

        currentInteractable = null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, SO_interactionManager.InteractionDistance, interactionLayer))
        {
            if (hit.collider.TryGetComponent(out IInteractable interactable))
            {
                currentInteractable = interactable;
                return;
            }

            currentInteractable = hit.collider.GetComponent<IInteractable>() ?? hit.collider.GetComponentInParent<IInteractable>() ?? hit.collider.GetComponentInChildren<IInteractable>();
        }
    }

    private void Interact()
    {
        if (Input.GetKeyDown(KeyCode.E) && currentInteractable != null)
        {
            if (currentInteractable.CanInteract())
            {
                currentInteractable.Interact();
            }
        }
    }
}
