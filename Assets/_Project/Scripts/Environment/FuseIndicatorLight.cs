using UnityEngine;

/// <summary>
/// Drives the emission of the fuse box indicator lamps (the three red domes on
/// <c>Caja de Fusible.fbx</c>) so a puzzle can turn them on and off individually.
///
/// Fully self-contained: it does not inherit from, reference, or subscribe to anything
/// else in the project. Drop it on any GameObject, wire the targets, and call the API.
///
/// It writes <c>_EmissionColor</c> and <c>_EmissionIntensity</c> through a
/// <see cref="MaterialPropertyBlock"/>, so it never touches <c>Renderer.material</c> and
/// therefore never instantiates a material copy at runtime.
///
/// Setup:
///   1. Add one entry to <c>fuses</c> per lamp.
///   2. Each entry points at a Renderer plus the material slot index of that lamp.
///      The fuses come out of the FBX as three separate GameObjects
///      (<c>Fusible.001</c>, <c>Fusible 002</c>, <c>Fusible 003</c>), so the usual setup
///      is three entries with <c>materialIndex = 0</c>. If you instead merge them into a
///      single Renderer, use one entry per submesh index — both layouts work.
///   3. The target material must expose <c>_EmissionColor</c> / <c>_EmissionIntensity</c>
///      (<c>Custom/PSXIndustrial</c> does).
///
/// Usage: <c>SetFuse(0, true)</c>, <c>SetAll(false)</c>, <c>IsLit(2)</c>.
/// </summary>
public class FuseIndicatorLight : MonoBehaviour
{
    /// <summary>One indicator lamp: a Renderer plus the material slot it occupies.</summary>
    [System.Serializable]
    public struct FuseTarget
    {
        [Tooltip("Renderer that draws this lamp.")]
        public Renderer meshRenderer;

        [Tooltip("Material slot index within that Renderer. 0 if the lamp is its own object.")]
        public int materialIndex;
    }

    [Header("Lamps")]
    [Tooltip("One entry per indicator lamp, in the order the puzzle addresses them.")]
    [SerializeField] private FuseTarget[] fuses = new FuseTarget[0];

    [Header("Emission")]
    [Tooltip("Emission color of a lit lamp. HDR — pick it well above 1 to feed the bloom.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color litColor = new Color(1f, 0.12f, 0.06f, 1f);

    [Tooltip("_EmissionIntensity while the lamp is on.")]
    [SerializeField, Min(0f)] private float litIntensity = 2.5f;

    [Tooltip("_EmissionIntensity while the lamp is off. Leave at 0 for a fully dead lamp.")]
    [SerializeField, Min(0f)] private float offIntensity = 0f;

    [Header("Transition")]
    [Tooltip("Seconds to fade between off and on. 0 snaps instantly.")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.15f;

    [Header("Initial state")]
    [Tooltip("Whether every lamp starts lit.")]
    [SerializeField] private bool startLit = false;

    private static readonly int EmissionColorId     = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");

    private MaterialPropertyBlock _propBlock;
    private bool[] _lit;
    private float[] _current;

    // Skips the whole Update body once every lamp has settled on its target value.
    private bool _animating;

    /// <summary>Number of configured lamps. Valid indices are 0..FuseCount-1.</summary>
    public int FuseCount => fuses != null ? fuses.Length : 0;

    private void Awake()
    {
        _propBlock = new MaterialPropertyBlock();
        _lit = new bool[FuseCount];
        _current = new float[FuseCount];

        for (int i = 0; i < FuseCount; i++)
        {
            _lit[i] = startLit;
            _current[i] = startLit ? litIntensity : offIntensity;
            Apply(i);
        }

        _animating = false;
    }

    private void Update()
    {
        if (!_animating) return;

        // Speed is derived from the full off->on range so the fade always takes
        // fadeDuration regardless of where it starts from.
        float range = Mathf.Abs(litIntensity - offIntensity);
        float step = (fadeDuration <= 0f || range <= 0f)
            ? float.MaxValue
            : range / fadeDuration * Time.deltaTime;

        bool stillMoving = false;

        for (int i = 0; i < _current.Length; i++)
        {
            float target = _lit[i] ? litIntensity : offIntensity;
            if (_current[i] == target) continue;

            _current[i] = Mathf.MoveTowards(_current[i], target, step);
            Apply(i);

            if (_current[i] != target) stillMoving = true;
        }

        _animating = stillMoving;
    }

    /// <summary>Turns a single lamp on or off, fading over <c>fadeDuration</c>.</summary>
    public void SetFuse(int index, bool on)
    {
        if (!IsValidIndex(index)) return;
        if (_lit[index] == on) return;

        _lit[index] = on;
        _animating = true;
    }

    /// <summary>Turns every lamp on or off.</summary>
    public void SetAll(bool on)
    {
        for (int i = 0; i < FuseCount; i++) SetFuse(i, on);
    }

    /// <summary>Flips a single lamp.</summary>
    public void ToggleFuse(int index)
    {
        if (!IsValidIndex(index)) return;
        SetFuse(index, !_lit[index]);
    }

    /// <summary>Whether a lamp is currently switched on (its target state, not the fade progress).</summary>
    public bool IsLit(int index) => IsValidIndex(index) && _lit[index];

    /// <summary>Sets a lamp without fading. Useful when restoring saved puzzle state.</summary>
    public void SnapFuse(int index, bool on)
    {
        if (!IsValidIndex(index)) return;

        _lit[index] = on;
        _current[index] = on ? litIntensity : offIntensity;
        Apply(index);
    }

    /// <summary>Sets every lamp without fading.</summary>
    public void SnapAll(bool on)
    {
        for (int i = 0; i < FuseCount; i++) SnapFuse(i, on);
    }

    private bool IsValidIndex(int index)
    {
        return _lit != null && index >= 0 && index < _lit.Length;
    }

    private void Apply(int index)
    {
        FuseTarget fuse = fuses[index];
        if (fuse.meshRenderer == null) return;

        // Guard against a slot index that no longer exists on the mesh — otherwise
        // SetPropertyBlock would silently write to nothing (or throw on some versions).
        int slotCount = fuse.meshRenderer.sharedMaterials.Length;
        if (fuse.materialIndex < 0 || fuse.materialIndex >= slotCount) return;

        fuse.meshRenderer.GetPropertyBlock(_propBlock, fuse.materialIndex);
        _propBlock.SetColor(EmissionColorId, litColor);
        _propBlock.SetFloat(EmissionIntensityId, _current[index]);
        fuse.meshRenderer.SetPropertyBlock(_propBlock, fuse.materialIndex);
    }
}
