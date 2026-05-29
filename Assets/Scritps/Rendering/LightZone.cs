using UnityEngine;

/// <summary>
/// Trigger volume que activa un <see cref="SO_VisionFogConfig"/> mientras el player
/// está adentro. Cuando sale, el controller vuelve al config anterior (o al default
/// del controller si no había anidamiento).
///
/// Setup:
///   1. GameObject vacío en la escena de gameplay.
///   2. Agregar BoxCollider (u otro Collider) con <c>Is Trigger = true</c>.
///   3. Agregar este componente.
///   4. Asignar el <see cref="config"/> con el preset deseado para esta zona.
///
/// Anidamiento: si una LightZone B está dentro de una LightZone A, el player entra a A,
/// después entra a B. El controller mantiene un stack — mientras está en B, aplica B.
/// Cuando sale de B, vuelve a A. Cuando sale de A, vuelve al default.
///
/// El controller se busca por <see cref="UnityEngine.Object.FindFirstObjectByType"/> al
/// primer trigger. Si no existe en la escena, se loguea error y el trigger no hace nada.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LightZone : MonoBehaviour
{
    [Header("Config aplicada al entrar")]
    [Tooltip("Preset del fog para esta zona. Por ejemplo: SafeRoom (visión amplia, " +
             "color cálido), o BossArena (visión corta, atmósfera tensa).")]
    [SerializeField] private SO_VisionFogConfig config;

    [Header("Detección")]
    [Tooltip("Tag del GameObject que dispara el trigger. Default: Player.")]
    [SerializeField] private string playerTag = "Player";

    private VisionRangeController _controller;
    private bool _isPlayerInside;

    private void Reset()
    {
        // Cuando se agrega el componente al GameObject, asegurar que el Collider sea trigger.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnEnable()
    {
        AcquireController();
    }

    private void OnDisable()
    {
        // Si el player estaba adentro y la zona se desactiva, pop manual para no dejar el config colgado.
        if (_isPlayerInside && _controller != null)
        {
            _controller.PopConfig(config);
            _isPlayerInside = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (_isPlayerInside) return; // protección contra triggers duplicados

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
            Debug.LogWarning($"[{nameof(LightZone)}] No se encontró VisionRangeController en escena. " +
                             $"La zona '{name}' no va a hacer nada.");
        }
    }

#if UNITY_EDITOR
    // Visualización del trigger en Scene view, color según el fog color del config.
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
