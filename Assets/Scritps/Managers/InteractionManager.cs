using UnityEngine;

public class InteractionManager : Singleton<InteractionManager>
{
    [SerializeField] private SO_InteractionManager SO_interactionManager;

    private Camera playerCamera;

    private LayerMask interactionLayer;

    [Header("Debug")]
    [SerializeField] private bool drawRay = true;

    private IInteractable currentInteractable;

    public IInteractable CurrentInteractable => currentInteractable;


    void Awake()
    {
        CreateSingleton(true);
        InitializeInteractionManager();
    }

    void Update()
    {
        DetectInteractable();
        Interact();
    }


    /// <summary>
    /// Analizar esta linea de codigo por si en un futuro agregagan mas camaras
    /// </summary>
    private void InitializeInteractionManager()
    {
        playerCamera = Camera.main;
        interactionLayer = LayerMask.GetMask("Interactable");
    }

    private void DetectInteractable()
    {
        currentInteractable = null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (drawRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * SO_interactionManager.InteractionDistance, Color.green);
        }

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
