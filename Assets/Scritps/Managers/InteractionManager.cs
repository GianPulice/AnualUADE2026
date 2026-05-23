using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InteractionManager : Singleton<InteractionManager>
{
    [Header("Config")]
    [Tooltip("Radios globales para todos los BaseRangeInteractable de la escena.")]
    [SerializeField] private SO_InteractionManager config;

    [Header("Camera")]
    [SerializeField] private bool requireCameraVisibility = true;

    [Header("Vision Check")]
    [SerializeField] private bool requireClearLineOfSight = false;
    [SerializeField] private LayerMask lineOfSightBlockingLayers = ~0;

    public SO_InteractionManager Config => config;

    private Camera playerCamera;
    private readonly List<IInteractable> nearbyInteractables = new();

    private IInteractable currentInteractable;
    private IInteractable lastInteractable;
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
        IInteractable detectedInteractable = null;

        if (nearbyInteractables.Count > 0)
        {
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

                // Si pasamos todas las validaciones, este es el objeto v�lido
                detectedInteractable = interactable;
                break;
            }
        }

        // Comprobamos si el objeto interactuable es diferente al del frame anterior
        // Esto tambi�n maneja el caso donde dejamos de mirar un objeto (detectedInteractable es null)
        if (detectedInteractable != lastInteractable)
        {
            lastInteractable = detectedInteractable;
            currentInteractable = detectedInteractable;

            if (currentInteractable != null)
                Debug.Log($"[Manager] �Detect� un objeto! Disparando evento para: {currentInteractable.GetInteractText()}");
            else
                Debug.Log($"[Manager] Dej� de mirar el objeto. Disparando evento para ocultar UI.");

            // Disparamos el evento para que la UI reaccione y haga el Fade In/Out
            InteractionEvents.TargetChanged(currentInteractable);
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