using System.Collections;
using UnityEngine;

/// <summary>
/// Lerps the <c>_TintIntensity</c> and <c>_EmissionIntensity</c> parameters of the ItemPSX
/// shader when the player enters/leaves the interaction radius, following the
/// "Color &amp; Visual Language" spec (section 2.1).
///
/// Far state (default): tint 0.15, emission 0.0.
/// Near state (player in range): tint 0.4, emission 0.2.
/// Transition: 0.3 second lerp.
///
/// Uses MaterialPropertyBlock — does not instance the material, keeps the SRP Batcher.
///
/// Setup:
///   1. The item's Renderer must use a material with the <c>Shader Graphs/ItemPSX</c> shader
///      (or any shader exposing the two <c>_TintIntensity</c> and <c>_EmissionIntensity</c>
///      properties).
///   2. Attach this component to the item's GameObject (Renderer on the same object, or
///      assign it manually in <c>targetRenderer</c>).
///   3. Hook-up is automatic: the component listens to <see cref="InteractionEvents.OnTargetChanged"/>
///      and when the <c>InteractionManager</c> raycast points at this interactable it moves to
///      the near state, and back to far when it stops pointing at it. No triggers need to be
///      wired by hand.
///
/// For puzzles and interactables without a category tint (spec section 6):
/// set <c>farTint = 0</c> and <c>nearTint = 0</c> — only the emission glows on approach.
///
/// Category/color: if the GameObject has a <see cref="PickupInteractable"/>, the category
/// (and therefore the tint/emission) resolves itself from its <c>SO_InventoryItem</c> —
/// there is no need to pick it again here. Use the manual dropdown (by ticking
/// <c>overrideCategory</c>) only on interactables without an inventory item.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ItemProximityHighlight : MonoBehaviour
{
    [Header("Far state (default)")]
    [Tooltip("Barely perceptible category tint. Spec: 0.15.")]
    [SerializeField, Range(0f, 1f)] private float farTint      = 0.15f;

    [Tooltip("Emission off in the far state. Spec: 0.0.")]
    [SerializeField, Range(0f, 1f)] private float farEmission  = 0.0f;

    [Header("Near state (player in range)")]
    [Tooltip("Intensified tint on approach. Spec: 0.4.")]
    [SerializeField, Range(0f, 1f)] private float nearTint     = 0.4f;

    [Tooltip("Subtle emission on approach. Spec: 0.2.")]
    [SerializeField, Range(0f, 1f)] private float nearEmission = 0.2f;

    [Header("Transition")]
    [Tooltip("Lerp duration in seconds. Spec: 0.3s.")]
    [SerializeField, Min(0.01f)] private float lerpDuration = 0.3f;

    [Header("Category tint (ItemPSX §4.4)")]
    [Tooltip("If there is a PickupInteractable on this same GameObject, the category is taken " +
             "only from its SO_InventoryItem — there is no need to duplicate it here. Tick this to " +
             "force the manual category below (e.g. puzzles/props without an inventory item).")]
    [SerializeField] private bool overrideCategory = false;
    [Tooltip("Manual category — only used if 'Override Category' is ticked, or if there is no " +
             "PickupInteractable with an assigned item on this GameObject.")]
    [SerializeField] private ItemCategory category;
    [Tooltip("Global category config. Assign the project's SO_ItemCategoryConfig asset.")]
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;

    [Header("Renderer (optional — autodetects the GameObject's)")]
    [SerializeField] private Renderer targetRenderer;

    private static readonly int TintId       = Shader.PropertyToID("_TintIntensity");
    private static readonly int EmissionId   = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int TintColorId  = Shader.PropertyToID("_TintColor");
    private static readonly int EmitColorId  = Shader.PropertyToID("_EmissionColor");

    private Color _tintColor;
    private Color _emissionColor;

    // If there is no categoryConfig, the color language lives in the material itself
    // (mat_item_keys, mat_item_clues, etc.): in that case we do NOT overwrite _TintColor /
    // _EmissionColor, we only animate their intensities. Without this, the item looked
    // grey on Play because the else branch in Awake forced grey/black onto the material.
    private bool _overrideColors;

    private MaterialPropertyBlock _propBlock;
    private Coroutine _activeLerp;
    private float _currentTint;
    private float _currentEmission;

    // This item's interactable (PickupInteractable). Compared against the InteractionManager's
    // target to know whether the player is looking at it (near state).
    private IInteractable _selfInteractable;

    // Current proximity state. Avoids restarting the lerp on every item in the scene each
    // time the InteractionManager changes target (the event is global).
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
            // No config: we respect the colors that come with the material.
            _overrideColors = false;
        }

        ApplyProps();
    }

    /// <summary>
    /// Effective category of the item. By default it is taken from the <see cref="SO_InventoryItem"/>
    /// assigned in the <see cref="PickupInteractable"/> on the same GameObject, so the designer
    /// only sets it once (on the inventory item) and does not have to repeat it here.
    /// Falls back to the manual dropdown if overridden or if there is no pickup/item assigned
    /// (e.g. puzzles and interactable props without an SO_InventoryItem).
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
    /// Reacts to the <c>InteractionManager</c> target change: if the player's raycast starts
    /// pointing at this item, lerp to the near state; if it stops pointing at it, to far.
    /// </summary>
    private void HandleTargetChanged(IInteractable target)
    {
        bool isTargeted = _selfInteractable != null && ReferenceEquals(target, _selfInteractable);
        if (isTargeted == _isNear) return;

        _isNear = isTargeted;
        if (isTargeted) OnPlayerEnteredRange();
        else            OnPlayerExitedRange();
    }

    /// <summary>Call when the player enters the item's interaction radius.</summary>
    public void OnPlayerEnteredRange() => TransitionTo(nearTint, nearEmission);

    /// <summary>Call when the player leaves the item's interaction radius.</summary>
    public void OnPlayerExitedRange() => TransitionTo(farTint, farEmission);

    /// <summary>Force the far state without animating (e.g. when hiding the item).</summary>
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
            // SmoothStep so the "breathing" does not feel linear/mechanical.
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
