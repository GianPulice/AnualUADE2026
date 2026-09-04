using UnityEngine;

public class PushBoxTriggerLogic : MonoBehaviour
{
    [SerializeField] private GrabbableBall Owner;

    /// <summary>
    /// All three trigger callbacks dereference Owner on the first line, so an unassigned one is a
    /// NullReferenceException on every overlap. Resolved from the parents first — this component
    /// always lives on a child trigger of the ball it belongs to — and only then given up on.
    /// </summary>
    private void Awake()
    {
        if (Owner == null) Owner = GetComponentInParent<GrabbableBall>();

        if (Owner != null) return;

        Debug.LogError($"[{nameof(PushBoxTriggerLogic)}] '{name}' has no {nameof(Owner)} and none " +
                       $"was found in its parents. The trigger has been disabled — assign it in " +
                       $"the inspector, or reparent this object under its GrabbableBall.", this);
        enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(Owner.PlayerTag)) return;
        Owner.Player = other.GetComponent<PlayerStateManager>();
        Owner.PlayerNearby = true;
        Owner.CurrentTriggerTransform = transform;
    }
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(Owner.PlayerTag)) return;
        if (Owner.Player == null) Owner.Player = other.GetComponent<PlayerStateManager>();
    }
    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(Owner.PlayerTag)) return;
        Owner.Player = null;
        Owner.PlayerNearby = false;
        Owner.CurrentTriggerTransform = null;
    }
}
