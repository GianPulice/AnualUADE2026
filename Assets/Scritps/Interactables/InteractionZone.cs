using UnityEngine;

[RequireComponent(typeof(Collider))]
public class InteractionZone : MonoBehaviour
{
    [SerializeField] private MonoBehaviour interactableBehaviour;

    private IInteractable interactable;
    private Collider zoneCollider;


    private void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        zoneCollider.isTrigger = true;

        if (interactableBehaviour == null)
        {
            interactableBehaviour = GetComponentInParent<MonoBehaviour>();
        }

        interactable = interactableBehaviour as IInteractable;

        if (interactable == null)
        {
            interactable = GetComponentInParent<IInteractable>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (interactable == null) return;

        InteractionManager.Instance.RegisterInteractable(interactable);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (interactable == null) return;

        InteractionManager.Instance.UnregisterInteractable(interactable);
    }

    private void OnDisable()
    {
        if (InteractionManager.Instance != null && interactable != null)
        {
            InteractionManager.Instance.UnregisterInteractable(interactable);
        }
    }
}
