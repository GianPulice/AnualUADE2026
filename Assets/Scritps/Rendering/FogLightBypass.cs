using UnityEngine;

/// <summary>
/// Marks a spherical world area where light "cuts through" the fog of the
/// <see cref="VisionRangeController"/> — meant for street lamps, bonfires,
/// flares, narrative "there is something important here" signals, etc.
///
/// Unlike <see cref="FogLightSource"/> (which follows the player), these zones
/// are anchored to the world. The controller reads every active bypass zone
/// each frame and pushes them to the shader as an array (max.
/// <see cref="VisionRangeController.MaxBypassZones"/>).
///
/// The shader combines them with MAX (they do not accumulate quadratically) — two
/// overlapping bypasses do not blow the scene out to white, they are treated as one.
/// </summary>
[DisallowMultipleComponent]
public class FogLightBypass : MonoBehaviour
{
    [Tooltip("Radius (metres) where the fog dissolves. With 0 the component does nothing.")]
    [Min(0f)] public float radius = 3f;

    private void OnEnable()  => VisionRangeController.RegisterBypass(this);
    private void OnDisable() => VisionRangeController.UnregisterBypass(this);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.35f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
