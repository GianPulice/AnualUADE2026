using UnityEngine;

/// <summary>
/// Trigger volume that activates a <see cref="SO_VisionFogConfig"/> while the player is
/// inside. When they leave, the controller falls back to the previous config (or to the
/// controller's default if there was no nesting).
///
/// Setup:
///   1. Empty GameObject in the gameplay scene.
///   2. Add a BoxCollider (or another Collider) with <c>Is Trigger = true</c>.
///   3. Add this component.
///   4. Assign <see cref="config"/> with the preset wanted for this area.
///
/// Nesting: if LightZone B is inside LightZone A, the player enters A, then enters B.
/// The controller keeps a stack — while in B, it applies B. When leaving B, it goes back
/// to A. When leaving A, it goes back to the default.
///
/// The controller is looked up with <see cref="UnityEngine.Object.FindFirstObjectByType"/>
/// on the first trigger. If it does not exist in the scene, an error is logged and the
/// trigger does nothing.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LightZone : MonoBehaviour
{
    [Header("Config applied on enter")]
    [Tooltip("Fog preset for this area. For example: SafeRoom (wide vision, " +
             "warm color), or BossArena (short vision, tense atmosphere).")]
    [SerializeField] private SO_VisionFogConfig config;

    [Header("Detection")]
    [Tooltip("Tag of the GameObject that fires the trigger. Default: Player.")]
    [SerializeField] private string playerTag = "Player";

    private VisionRangeController _controller;
    private bool _isPlayerInside;

    private void Reset()
    {
        // When the component is added to the GameObject, make sure the Collider is a trigger.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnEnable()
    {
        AcquireController();
    }

    private void OnDisable()
    {
        // If the player was inside and the zone is disabled, pop manually so the config is not left hanging.
        if (_isPlayerInside && _controller != null)
        {
            _controller.PopConfig(config);
            _isPlayerInside = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (_isPlayerInside) return; // guard against duplicate triggers

        if (_controller == null) AcquireController();
        if (_controller == null || config == null) return;

        _controller.PushConfig(config);
        _isPlayerInside = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!_isPlayerInside) return;

        if (_controller != null && config != null)
            _controller.PopConfig(config);

        _isPlayerInside = false;
    }

    private void AcquireController()
    {
        if (_controller != null) return;
        _controller = FindFirstObjectByType<VisionRangeController>();
        if (_controller == null)
        {
            Debug.LogWarning($"[{nameof(LightZone)}] No VisionRangeController was found in the scene. " +
                             $"Zone '{name}' will do nothing.");
        }
    }

#if UNITY_EDITOR
    // Trigger visualization in the Scene view, colored from the config's fog color.
    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Color c = config != null
            ? new Color(1f - config.fogColor.r, 1f - config.fogColor.g, 1f - config.fogColor.b, 0.15f)
            : new Color(1f, 1f, 0f, 0.15f);

        Gizmos.color = c;

        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.6f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sph)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawSphere(sph.center, sph.radius);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.6f);
            Gizmos.DrawWireSphere(sph.center, sph.radius);
        }
    }
#endif
}
