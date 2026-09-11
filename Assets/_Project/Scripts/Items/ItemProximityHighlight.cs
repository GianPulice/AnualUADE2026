using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lifts the tint and emission of an interactable while the crosshair is on it, following the
/// "Color &amp; Visual Language" spec (section 2.1) — on every renderer that belongs to it.
///
/// Put it on the interactable's ROOT, normally in the Father prefab, so every variant inherits it
/// with nothing to wire. It drives every Renderer below it that answers to the same
/// <see cref="IInteractable"/>: a variant that swaps the Father's placeholder cube for a stack of
/// FBX parts is covered automatically, and so is a part that only switches on later (a socket's
/// inserted item). Renderers under a DIFFERENT interactable nested below are left to that one.
///
/// What it writes, per material slot, through a <see cref="MaterialPropertyBlock"/> (no material
/// instancing, so shared materials stay shared):
///   • Highlight shaders — anything that declares <c>_EmissionColor</c> and
///     <c>_EmissionIntensity</c> (ItemPSX_Outline, PSXIndustrial, the wood box Shader Graph):
///     emission colour and intensity, plus <c>_TintColor</c>/<c>_TintIntensity</c> when the shader
///     declares them too.
///   • URP/Lit-style materials — <c>_EmissionColor</c> only, ADDED to the material's own emission so
///     a lit panel stays lit. Needs the material's Emission switched on (Tools ▸ Interactables ▸
///     Set Up Highlights does it); with it off, URP compiles the emission out.
///   • Anything else is skipped, and Tools ▸ Items ▸ Validate Interactable Highlights lists it.
/// Written slot by slot, reading the slot's block back first: a per-slot block REPLACES the
/// renderer-wide one for that slot, so a renderer-wide write would be silently ignored wherever
/// another script (ElevatorCallPanel, FuseIndicatorLight) already owns a slot.
///
/// Values and colours come from an <see cref="SO_HighlightProfile"/>, never from the component, so
/// a family of interactables cannot drift apart one prefab or one scene override at a time.
///
/// Hook-up is automatic: it listens to <see cref="InteractionEvents.OnTargetChanged"/> and goes
/// near while the InteractionManager's target is this interactable.
/// </summary>
[DisallowMultipleComponent]
public class ItemProximityHighlight : MonoBehaviour
{
    /// <summary>What a material slot can do with the highlight.</summary>
    public enum SlotSupport
    {
        /// <summary>No property the highlight can write. The part stays dark.</summary>
        None,

        /// <summary>Declares _EmissionColor and _EmissionIntensity (and maybe the tint pair).</summary>
        HighlightShader,

        /// <summary>URP/Lit-style: _EmissionColor alone, with the emission keyword on.</summary>
        EmissionOnly,

        /// <summary>URP/Lit-style, but its Emission is switched off, so nothing would show.</summary>
        EmissionKeywordOff,
    }

    private struct Slot
    {
        public Renderer    Renderer;
        public int         Index;
        public SlotSupport Support;
        public bool        HasTint;
        public Color       BaseEmission;
    }

    [Tooltip("Far/near values and colours. SO_Highlight_Items on pickups, SO_Highlight_Interactables " +
             "on puzzle props and devices. Assigned in the Father prefab.")]
    [SerializeField] private SO_HighlightProfile profile;

    private static readonly int TintId      = Shader.PropertyToID("_TintIntensity");
    private static readonly int EmissionId  = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int EmitColorId = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    private readonly List<Slot> _slots = new List<Slot>();
    private MaterialPropertyBlock _block;
    private IInteractable _owner;
    private Color _tintColor;
    private Color _emissionColor;
    private float _tint;
    private float _emission;
    private Coroutine _lerp;

    // Current state. The target-changed event is global, so without it every highlight in the
    // scene would restart its lerp each time the crosshair moves between any two objects.
    private bool _isNear;

    public SO_HighlightProfile Profile => profile;

    private void Awake()
    {
        _owner = GetComponentInParent<IInteractable>(true);
        _block = new MaterialPropertyBlock();

        if (profile == null)
        {
            Debug.LogWarning($"[{nameof(ItemProximityHighlight)}] '{name}' has no " +
                             $"{nameof(SO_HighlightProfile)}, so it never lights up. Assign one on the " +
                             "Father prefab (Tools > Interactables > Set Up Highlights).", this);
            enabled = false;
            return;
        }

        profile.ResolveColors(_owner, out _tintColor, out _emissionColor);
        CollectSlots();

        _tint     = profile.FarTint;
        _emission = profile.FarEmission;
        Apply();
    }

    private void OnEnable()  => InteractionEvents.OnTargetChanged += HandleTargetChanged;
    private void OnDisable() => InteractionEvents.OnTargetChanged -= HandleTargetChanged;

    private void HandleTargetChanged(IInteractable target)
    {
        bool isTargeted = _owner != null && ReferenceEquals(target, _owner);
        if (isTargeted == _isNear) return;

        _isNear = isTargeted;
        if (isTargeted) OnPlayerEnteredRange();
        else            OnPlayerExitedRange();
    }

    /// <summary>Lerp to the near state.</summary>
    public void OnPlayerEnteredRange() => TransitionTo(profile.NearTint, profile.NearEmission);

    /// <summary>Lerp to the far state.</summary>
    public void OnPlayerExitedRange() => TransitionTo(profile.FarTint, profile.FarEmission);

    /// <summary>Force the far state without animating (e.g. when hiding the object).</summary>
    public void SnapToFar()
    {
        if (profile == null) return;
        if (_lerp != null) StopCoroutine(_lerp);
        _lerp = null;
        _isNear = false;
        _tint = profile.FarTint;
        _emission = profile.FarEmission;
        Apply();
    }

    /// <summary>
    /// The renderers a highlight on <paramref name="root"/> drives: every renderer under it,
    /// inactive ones included, whose nearest interactable is the same as the root's. Static so the
    /// validator and the setup tool judge exactly the set the component will use at runtime.
    /// </summary>
    public static List<Renderer> GatherRenderers(Transform root)
    {
        IInteractable owner = root.GetComponentInParent<IInteractable>(true);
        var result = new List<Renderer>();

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (ReferenceEquals(renderer.GetComponentInParent<IInteractable>(true), owner))
                result.Add(renderer);
        }

        return result;
    }

    /// <summary>What the highlight can do on <paramref name="material"/>.</summary>
    public static SlotSupport GetSupport(Material material)
    {
        if (material == null || !material.HasProperty(EmitColorId)) return SlotSupport.None;
        if (material.HasProperty(EmissionId)) return SlotSupport.HighlightShader;
        return material.IsKeywordEnabled(EmissionKeyword) ? SlotSupport.EmissionOnly : SlotSupport.EmissionKeywordOff;
    }

    private void CollectSlots()
    {
        _slots.Clear();

        foreach (Renderer renderer in GatherRenderers(transform))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                SlotSupport support = GetSupport(materials[i]);
                if (support != SlotSupport.HighlightShader && support != SlotSupport.EmissionOnly) continue;

                _slots.Add(new Slot
                {
                    Renderer     = renderer,
                    Index        = i,
                    Support      = support,
                    HasTint      = support == SlotSupport.HighlightShader && materials[i].HasProperty(TintId),
                    // Captured once: the material asset's own emission, which the highlight adds to
                    // on URP/Lit instead of replacing.
                    BaseEmission = support == SlotSupport.EmissionOnly ? materials[i].GetColor(EmitColorId) : Color.black,
                });
            }
        }

        if (_slots.Count == 0)
        {
            Debug.LogWarning($"[{nameof(ItemProximityHighlight)}] '{name}': none of its materials can " +
                             "show the highlight, so looking at it changes nothing. Run Tools > Items > " +
                             "Validate Interactable Highlights for the list.", this);
        }
    }

    private void TransitionTo(float targetTint, float targetEmission)
    {
        if (!isActiveAndEnabled || profile == null) return;
        if (_lerp != null) StopCoroutine(_lerp);
        _lerp = StartCoroutine(LerpRoutine(targetTint, targetEmission));
    }

    private IEnumerator LerpRoutine(float targetTint, float targetEmission)
    {
        float startTint     = _tint;
        float startEmission = _emission;
        float duration      = profile.LerpDuration;
        float elapsed       = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // SmoothStep so the "breathing" does not feel linear/mechanical.
            float eased = t * t * (3f - 2f * t);
            _tint     = Mathf.Lerp(startTint,     targetTint,     eased);
            _emission = Mathf.Lerp(startEmission, targetEmission, eased);
            Apply();
            yield return null;
        }

        _tint = targetTint;
        _emission = targetEmission;
        Apply();
        _lerp = null;
    }

    private void Apply()
    {
        foreach (Slot slot in _slots)
        {
            // A part destroyed at runtime (a consumed pickup's child, a swapped visual).
            if (slot.Renderer == null) continue;

            slot.Renderer.GetPropertyBlock(_block, slot.Index);

            if (slot.Support == SlotSupport.HighlightShader)
            {
                if (slot.HasTint)
                {
                    _block.SetFloat(TintId,      _tint);
                    _block.SetColor(TintColorId, _tintColor);
                }
                _block.SetFloat(EmissionId,  _emission);
                _block.SetColor(EmitColorId, _emissionColor);
            }
            else
            {
                _block.SetColor(EmitColorId, slot.BaseEmission + _emissionColor * _emission);
            }

            slot.Renderer.SetPropertyBlock(_block, slot.Index);
        }
    }
}
