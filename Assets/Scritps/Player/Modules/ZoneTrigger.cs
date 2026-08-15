using UnityEngine;

/// <summary>
/// Volume that activates a module the first time the player enters it. Level designers place one
/// of these at the entrance of each puzzle zone, assign the corresponding <see cref="ModuleData"/>,
/// and the manager takes care of the rest.
///
/// Requires a Collider on the same GameObject with <c>isTrigger = true</c>.
/// Uses the "Player" tag (already used across the project) to detect the player.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ZoneTrigger : MonoBehaviour
{
    [Header("Target module")]
    [Tooltip("Which module this zone activates. Drag the M1/M2/M3 ModuleData asset here.")]
    [SerializeField] private ModuleData moduleToActivate;

    private bool disableAfterActivation = true;

    private bool hasFired;

    private void Reset()
    {
        // Auto-configure the collider as trigger when the component is added, so LDs don't forget.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasFired) return;
        if (!other.CompareTag("Player")) return;
        if (moduleToActivate == null)
        {
            Debug.LogWarning($"[ZoneTrigger] '{name}' has no ModuleData assigned — ignored.", this);
            return;
        }

        // Exists rather than 'Instance == null': the property logs a warning of its own every time
        // it is read while null, so the guard would double up on the message below.
        if (!ModuleManager.Exists)
        {
            Debug.LogWarning($"[ZoneTrigger] '{name}' fired but ModuleManager is not in the scene.", this);
            return;
        }

        hasFired = true;
        ModuleManager.Instance.ActivateModule(moduleToActivate);

        if (disableAfterActivation) gameObject.SetActive(false);
    }
}
