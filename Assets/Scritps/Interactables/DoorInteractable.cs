using System.Collections;
using UnityEngine;

public class DoorInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_DoorData doorData;

    [Header("Animacion de apertura (doble puerta)")]
    [Tooltip("Panel izquierdo. Se desliza hacia -X (su izquierda local).")]
    [SerializeField] private Transform leftPanel;
    [Tooltip("Panel derecho. Se desliza hacia +X (su derecha local).")]
    [SerializeField] private Transform rightPanel;
    [Tooltip("Distancia que se desliza cada panel al abrirse, en unidades locales.")]
    [SerializeField, Min(0f)] private float slideDistance = 1f;
    [Tooltip("Duracion total del deslizamiento, en segundos.")]
    [SerializeField, Min(0.01f)] private float slideDuration = 0.6f;

    private Vector3 leftClosedLocalPos;
    private Vector3 rightClosedLocalPos;
    private bool isOpen;
    private bool isAnimating;

    protected override void Awake()
    {
        base.Awake();

        if (doorData == null)
        {
            Debug.LogError($"DoorInteractable sin SO_DoorData en {gameObject.name}");
            return;
        }

        CacheClosedPositions();

        isOpen = PuzzleStateManager.Instance != null &&
                 PuzzleStateManager.Instance.IsDoorOpened(doorData.DoorId);

        if (isOpen) ApplyOpenStateImmediate();
    }

    public override string GetInteractText()
    {
        if (doorData == null) return "Puerta sin configurar";

        if (isOpen) return string.Empty;

        if (doorData.RequiredKey != null)
            return $"Abrir con {doorData.RequiredKey.ItemName}";

        return doorData.OpenPrompt;
    }

    public override string GetInfoText()
    {
        if (doorData == null || isOpen) return string.Empty;

        if (doorData.RequiredKey != null &&
            !InventoryManager.Instance.HasItem(doorData.RequiredKey))
            return $"Necesitas {doorData.RequiredKey.ItemName}";

        if (!string.IsNullOrWhiteSpace(doorData.RequiredCompletedPuzzleId) &&
            !PuzzleStateManager.Instance.IsPuzzleCompleted(doorData.RequiredCompletedPuzzleId))
            return doorData.LockedPrompt;

        return string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (doorData == null) return false;
        if (isOpen || isAnimating) return false;

        if (doorData.RequiredKey != null &&
            !InventoryManager.Instance.HasItem(doorData.RequiredKey))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(doorData.RequiredCompletedPuzzleId) &&
            !PuzzleStateManager.Instance.IsPuzzleCompleted(doorData.RequiredCompletedPuzzleId))
        {
            return false;
        }

        return true;
    }

    protected override void OnInteract()
    {
        OpenDoor();
    }

    public void OpenDoor()
    {
        if (doorData == null) return;
        if (isOpen || isAnimating) return;

        if (doorData.ConsumeKey && doorData.RequiredKey != null)
            InventoryManager.Instance.ConsumeItem(doorData.RequiredKey);

        PuzzleStateManager.Instance.SetDoorOpened(doorData.DoorId);

        StartCoroutine(AnimateOpen());

        Debug.Log($"Puerta abierta: {doorData.DoorId}");
    }

    private IEnumerator AnimateOpen()
    {
        isAnimating = true;

        Vector3 leftTarget = leftClosedLocalPos + Vector3.left * slideDistance;
        Vector3 rightTarget = rightClosedLocalPos + Vector3.right * slideDistance;

        float t = 0f;
        while (t < slideDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / slideDuration));

            if (leftPanel != null)
                leftPanel.localPosition = Vector3.Lerp(leftClosedLocalPos, leftTarget, k);
            if (rightPanel != null)
                rightPanel.localPosition = Vector3.Lerp(rightClosedLocalPos, rightTarget, k);

            yield return null;
        }

        if (leftPanel != null) leftPanel.localPosition = leftTarget;
        if (rightPanel != null) rightPanel.localPosition = rightTarget;

        //DisableBlockingCollider();
        isOpen = true;
        isAnimating = false;
    }

    private void ApplyOpenStateImmediate()
    {
        if (leftPanel != null)
            leftPanel.localPosition = leftClosedLocalPos + Vector3.left * slideDistance;
        if (rightPanel != null)
            rightPanel.localPosition = rightClosedLocalPos + Vector3.right * slideDistance;

        //DisableBlockingCollider();
    }

    private void CacheClosedPositions()
    {
        if (leftPanel != null) leftClosedLocalPos = leftPanel.localPosition;
        if (rightPanel != null) rightClosedLocalPos = rightPanel.localPosition;
    }

    /*private void DisableBlockingCollider()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }*/

    public override bool IsRepeatable()
    {
        return false;
    }
}
