using UnityEngine;

/// <summary>
/// Marca una zona esférica del mundo donde la luz "atraviesa" la niebla del
/// <see cref="VisionRangeController"/> — pensado para farolas, hogueras,
/// bengalas, señales narrativas de "acá hay algo importante", etc.
///
/// A diferencia de <see cref="FogLightSource"/> (que sigue al player), estas
/// zonas están ancladas al mundo. El controller lee todas las bypass zones
/// activas cada frame y las pushea al shader como un array (máx.
/// <see cref="VisionRangeController.MaxBypassZones"/>).
///
/// El shader las combina con MAX (no se acumulan cuadraticamente) — dos
/// bypasses solapados no explotan la escena en blanco, se tratan como una sola.
/// </summary>
[DisallowMultipleComponent]
public class FogLightBypass : MonoBehaviour
{
    [Tooltip("Radio (metros) donde la niebla se disuelve. Con 0 el componente no hace nada.")]
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
