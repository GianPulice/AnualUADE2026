using UnityEngine;

public class PushBoxTriggerLogic : MonoBehaviour
{
    [SerializeField] private GrabbableBall Owner;
    
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
