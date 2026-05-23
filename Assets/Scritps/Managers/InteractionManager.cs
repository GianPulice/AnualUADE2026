using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InteractionManager : Singleton<InteractionManager>
{
    [Header("Camera")]
    [SerializeField] private bool requireCameraVisibility = true;

    [Header("Vision Check")]
    [SerializeField] private bool requireClearLineOfSight = false;
    [SerializeField] private LayerMask lineOfSightBlockingLayers = ~0;

    private Camera playerCamera;
    private readonly List<IInteractable> nearbyInteractables = new();

    private IInteractable currentInteractable;

    public IInteractable CurrentInteractable => currentInteractable;

    private void Awake()
    {
        CreateSingleton(true);
        SuscribeToOnSceneLoadedEvent();
        RefreshCamera();
    }

    private void Update()
    {
        RefreshCamera();
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

    public void RegisterInteractable(IInteractable interactable)
    {
        if (interactable == null) return;

        if (!nearbyInteractables.Contains(interactable))
        {
            nearbyInteractables.Add(interactable);
        }
    }

    public void UnregisterInteractable(IInteractable interactable)
    {
        if (interactable == null) return;

        nearbyInteractables.Remove(interactable);

        if (currentInteractable == interactable)
        {
            currentInteractable = null;
        }
    }

    private void UpdateCurrentInteractable()
    {
        currentInteractable = null;

        if (nearbyInteractables.Count == 0) return;

        for (int i = nearbyInteractables.Count - 1; i >= 0; i--)
        {
            IInteractable interactable = nearbyInteractables[i];

            if (interactable == null)
            {
                nearbyInteractables.RemoveAt(i);
                continue;
            }

            if (!interactable.CanInteract()) continue;

            MonoBehaviour interactableBehaviour = interactable as MonoBehaviour;

            if (interactableBehaviour == null) continue;

            if (requireCameraVisibility && !IsVisibleByCamera(interactableBehaviour.transform))
                continue;

            if (requireClearLineOfSight && !HasClearLineOfSight(interactableBehaviour.transform))
                continue;

            currentInteractable = interactable;
            return;
        }
    }

    private bool IsVisibleByCamera(Transform target)
    {
        if (playerCamera == null) return false;

        Vector3 viewportPoint = playerCamera.WorldToViewportPoint(target.position);

        bool isInFrontOfCamera = viewportPoint.z > 0f;
        bool isInsideCameraView =
            viewportPoint.x >= 0f &&
            viewportPoint.x <= 1f &&
            viewportPoint.y >= 0f &&
            viewportPoint.y <= 1f;

        return isInFrontOfCamera && isInsideCameraView;
    }

    private bool HasClearLineOfSight(Transform target)
    {
        if (playerCamera == null) return false;

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = target.position - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, lineOfSightBlockingLayers))
        {
            IInteractable hitInteractable =
                hit.collider.GetComponent<IInteractable>() ??
                hit.collider.GetComponentInParent<IInteractable>();

            return hitInteractable == currentInteractable;
        }

        return true;
    }

    private void Interact()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (currentInteractable == null) return;
        if (!currentInteractable.CanInteract()) return;

        IInteractable interactableToUse = currentInteractable;

        bool wasRepeatable = interactableToUse.IsRepeatable();

        interactableToUse.Interact();

        currentInteractable = null;

        if (!wasRepeatable)
        {
            UnregisterInteractable(interactableToUse);
        }
    }
}