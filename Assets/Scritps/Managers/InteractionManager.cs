using UnityEngine;

public class InteractionManager : Singleton<InteractionManager>
{
    private Camera playerCamera;

    private LayerMask interactionLayer = LayerMask.NameToLayer("Interactable");
    private float interactionDistance = 3f;

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


    public string GetCurrentInteractionText()
    {
        if (currentInteractable == null) return string.Empty;

        return currentInteractable.GetInteractText();
    }


    /// <summary>
    /// Analizar esta linea de codigo por si en un futuro agregagan mas camaras
    /// </summary>
    private void InitializeInteractionManager()
    {
        playerCamera = Camera.main;
    }

    private void DetectInteractable()
    {
        currentInteractable = null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (drawRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * interactionDistance, Color.green);
        }

        if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance, interactionLayer))
        {
            if (hit.collider.TryGetComponent(out IInteractable interactable))
            {
                currentInteractable = interactable;
                return;
            }

            currentInteractable = hit.collider.GetComponentInParent<IInteractable>();
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
