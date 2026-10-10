using UnityEngine;

/// <summary>
/// Volume that plays an Architect context line the first time the player walks into it
/// (ARC_CTX_01 "Zona 1", ARC_CTX_02 "Zona 2", ARC_CTX_06 the box room). Place one at the entrance
/// of each zone, or over the whole room when it has more than one way in.
///
/// Walking in again does nothing: context lines play once per run, which the controller enforces.
/// Requires a trigger Collider and the "Player" tag, same as <see cref="ZoneTrigger"/>.
/// For a line tied to a puzzle instead of a place, see <see cref="ArchitectPuzzleTrigger"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ArchitectZoneTrigger : MonoBehaviour
{
    [SerializeField] private ArchitectLineID line = ArchitectLineID.ContextZone1;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (ArchitectVoiceController.Instance == null)
        {
            Debug.LogWarning($"[{nameof(ArchitectZoneTrigger)}] '{name}': no ArchitectVoiceController loaded.", this);
            return;
        }

        ArchitectVoiceController.Instance.TriggerContext(line);
    }
}
