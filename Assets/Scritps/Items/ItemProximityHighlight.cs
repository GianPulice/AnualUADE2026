using System.Collections;
using UnityEngine;

/// <summary>
/// Lerpea los parámetros <c>_TintIntensity</c> y <c>_EmissionIntensity</c> del shader
/// ItemPSX cuando el player entra/sale del radio de interacción, según el spec
/// "Color &amp; Visual Language" (sección 2.1).
///
/// Estado lejano (default): tinte 0.15, emisión 0.0.
/// Estado próximo (player en radio): tinte 0.4, emisión 0.2.
/// Transición: lerp de 0.3 segundos.
///
/// Usa MaterialPropertyBlock — no instancia material, mantiene SRP Batcher.
///
/// Setup:
///   1. El Renderer del item debe usar un material con shader <c>Shader Graphs/ItemPSX</c>
///      (o cualquier shader que exponga las dos properties <c>_TintIntensity</c> y
///      <c>_EmissionIntensity</c>).
///   2. Pegar este componente al GameObject del item (Renderer en el mismo objeto, o
///      asignar a mano en <c>targetRenderer</c>).
///   3. El enganche es automático: el componente escucha <see cref="InteractionEvents.OnTargetChanged"/>
///      y cuando el raycast del <c>InteractionManager</c> apunta a este interactuable pasa a
///      estado próximo (near), y al dejar de apuntarlo vuelve a lejano (far). No hay que
///      cablear triggers a mano.
///
/// Para puzzles e interactuables sin tinte de categoría (sec 6 del spec):
/// setear <c>farTint = 0</c> y <c>nearTint = 0</c> — solo brilla la emisión al acercarse.
///
/// Categoría/color: si el GameObject tiene un <see cref="PickupInteractable"/>, la categoría
/// (y por lo tanto el tinte/emisión) se resuelve sola desde su <c>SO_InventoryItem</c> —
/// no hace falta volver a elegirla acá. Usar el dropdown manual (tildando
/// <c>overrideCategory</c>) solo en interactuables sin ítem de inventario.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ItemProximityHighlight : MonoBehaviour
{
    [Header("Estado lejano (default)")]
    [Tooltip("Tinte de categoría apenas perceptible. Spec: 0.15.")]
    [SerializeField, Range(0f, 1f)] private float farTint      = 0.15f;

    [Tooltip("Emisión apagada en estado lejano. Spec: 0.0.")]
    [SerializeField, Range(0f, 1f)] private float farEmission  = 0.0f;

    [Header("Estado próximo (player en radio)")]
    [Tooltip("Tinte intensificado al acercarse. Spec: 0.4.")]
    [SerializeField, Range(0f, 1f)] private float nearTint     = 0.4f;

    [Tooltip("Emisión sutil al acercarse. Spec: 0.2.")]
    [SerializeField, Range(0f, 1f)] private float nearEmission = 0.2f;

    [Header("Transición")]
    [Tooltip("Duración del lerp en segundos. Spec: 0.3s.")]
    [SerializeField, Min(0.01f)] private float lerpDuration = 0.3f;

    [Header("Tinte de categoría (ItemPSX §4.4)")]
    [Tooltip("Si hay un PickupInteractable en este mismo GameObject, la categoría se toma " +
             "solo de su SO_InventoryItem — no hace falta duplicarla acá. Tildar esto para " +
             "forzar la categoría manual de abajo (ej: puzzles/decorativos sin ítem de inventario).")]
    [SerializeField] private bool overrideCategory = false;
    [Tooltip("Categoría manual — solo se usa si 'Override Category' está tildado, o si no hay " +
             "PickupInteractable con ítem asignado en este GameObject.")]
    [SerializeField] private ItemCategory category;
    [Tooltip("Config global de categorías. Asignar el asset SO_ItemCategoryConfig del proyecto.")]
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    [Header("Renderer (opcional — autodetecta el del GameObject)")]
    [SerializeField] private Renderer targetRenderer;

    private static readonly int TintId       = Shader.PropertyToID("_TintIntensity");
    private static readonly int EmissionId   = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int TintColorId  = Shader.PropertyToID("_TintColor");
    private static readonly int EmitColorId  = Shader.PropertyToID("_EmissionColor");

    private Color _tintColor;
    private Color _emissionColor;

    // Si no hay categoryConfig, el color language vive en el propio material
    // (mat_item_keys, mat_item_clues, etc.): en ese caso NO pisamos _TintColor /
    // _EmissionColor, solo animamos sus intensidades. Sin esto, el item se veía
    // gris al Play porque el else de Awake forzaba grey/black sobre el material.
    private bool _overrideColors;

    private MaterialPropertyBlock _propBlock;
    private Coroutine _activeLerp;
    private float _currentTint;
    private float _currentEmission;

    // Interactuable de este item (PickupInteractable). Se compara contra el target
    // del InteractionManager para saber si el player lo está mirando (estado near).
    private IInteractable _selfInteractable;

    // Estado actual de proximidad. Evita relanzar el lerp en todos los items del scene
    // cada vez que el InteractionManager cambia de target (el evento es global).
    private bool _isNear;

    private void Awake()
    {
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        _selfInteractable = GetComponent<IInteractable>() ?? GetComponentInParent<IInteractable>();
        _propBlock = new MaterialPropertyBlock();
        _currentTint = farTint;
        _currentEmission = farEmission;

        if (categoryConfig != null)
        {
            CategoryVisuals visuals = categoryConfig.Get(ResolveCategory());
            _tintColor    = visuals.shaderTintColor;
            _emissionColor = visuals.shaderEmissionColor;
            _overrideColors = true;
        }
        else
        {
            // Sin config: respetamos los colores que trae el material.
            _overrideColors = false;
        }

        ApplyProps();
    }

    /// <summary>
    /// Categoría efectiva del item. Por default la toma del <see cref="SO_InventoryItem"/>
    /// asignado en el <see cref="PickupInteractable"/> del mismo GameObject, así el diseñador
    /// solo la setea una vez (en el ítem de inventario) y no tiene que repetirla acá.
    /// Cae al dropdown manual si está overrideada o si no hay pickup/ítem asignado
    /// (ej: puzzles y decorativos interactuables sin SO_InventoryItem).
    /// </summary>
    private ItemCategory ResolveCategory()
    {
        if (overrideCategory) return category;

        PickupInteractable pickup = GetComponent<PickupInteractable>();
        if (pickup != null && pickup.Item != null) return pickup.Item.Category;

        return category;
    }

    private void OnEnable()  => InteractionEvents.OnTargetChanged += HandleTargetChanged;
    private void OnDisable() => InteractionEvents.OnTargetChanged -= HandleTargetChanged;

    /// <summary>
    /// Reacciona al cambio de target del <c>InteractionManager</c>: si el raycast del player
    /// pasa a apuntar a este item, lerpea a estado próximo (near); si deja de apuntarlo, a lejano.
    /// </summary>
    private void HandleTargetChanged(IInteractable target)
    {
        bool isTargeted = _selfInteractable != null && ReferenceEquals(target, _selfInteractable);
        if (isTargeted == _isNear) return;

        _isNear = isTargeted;
        if (isTargeted) OnPlayerEnteredRange();
        else            OnPlayerExitedRange();
    }

    /// <summary>Llamar cuando el player entra al radio de interacción del item.</summary>
    public void OnPlayerEnteredRange() => TransitionTo(nearTint, nearEmission);

    /// <summary>Llamar cuando el player sale del radio de interacción del item.</summary>
    public void OnPlayerExitedRange() => TransitionTo(farTint, farEmission);

    /// <summary>Forzar estado lejano sin animación (ej: al ocultar el item).</summary>
    public void SnapToFar()
    {
        if (_activeLerp != null) StopCoroutine(_activeLerp);
        _activeLerp = null;
        _isNear = false;
        _currentTint = farTint;
        _currentEmission = farEmission;
        ApplyProps();
    }

    private void TransitionTo(float targetTint, float targetEmission)
    {
        if (!isActiveAndEnabled) return;
        if (_activeLerp != null) StopCoroutine(_activeLerp);
        _activeLerp = StartCoroutine(LerpRoutine(targetTint, targetEmission));
    }

    private IEnumerator LerpRoutine(float targetTint, float targetEmission)
    {
        float startTint     = _currentTint;
        float startEmission = _currentEmission;
        float elapsed = 0f;

        while (elapsed < lerpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lerpDuration);
            // SmoothStep para que el "respirar" no se sienta lineal/mecánico.
            float eased = t * t * (3f - 2f * t);
            _currentTint     = Mathf.Lerp(startTint,     targetTint,     eased);
            _currentEmission = Mathf.Lerp(startEmission, targetEmission, eased);
            ApplyProps();
            yield return null;
        }

        _currentTint = targetTint;
        _currentEmission = targetEmission;
        ApplyProps();
        _activeLerp = null;
    }

    private void ApplyProps()
    {
        if (targetRenderer == null) return;
        targetRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetFloat(TintId,      _currentTint);
        _propBlock.SetFloat(EmissionId,  _currentEmission);
        if (_overrideColors)
        {
            _propBlock.SetColor(TintColorId, _tintColor);
            _propBlock.SetColor(EmitColorId, _emissionColor);
        }
        targetRenderer.SetPropertyBlock(_propBlock);
    }
}
