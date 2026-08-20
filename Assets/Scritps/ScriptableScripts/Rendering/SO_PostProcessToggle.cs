using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Master switch for the game's two fullscreen post processes: the PSX filter
/// (<c>PS1Effect.mat</c>, property <c>_EnableEffect</c>) and the vision fog
/// (<c>VisionFog.mat</c> / <c>VisionFog_SilentHill.mat</c>, property <c>_EnableVisionFog</c>),
/// together with the renderer features that draw them on <c>Settings/PC_Renderer.asset</c>.
///
/// Why it exists: turning both off by hand is four clicks spread over two materials, and someone
/// has to remember to turn them back on. With this it is one button, and the button says which
/// state it is in — which matters, because a PSX filter left off and forgotten reads as "the game
/// lost its look" rather than "somebody switched it off to work".
///
/// The two layers it writes to are not the same thing:
/// - The MATERIAL properties are 0/1 floats in their shaders (that is how the HLSL shader's
///   <c>[Toggle]</c> and the Shader Graph Boolean are serialised), and both shaders read them
///   straight off the material. Off means the shader passes the colour through — but the
///   fullscreen blit still runs every frame.
/// - The RENDERER FEATURES on PC_Renderer are what queue that blit in the first place.
///   <see cref="ScriptableRenderer"/> skips any feature whose <c>isActive</c> is false, so off
///   here means the pass is never added and the frame does not pay for it. This is the layer to
///   switch when profiling, or when the effect has to be gone rather than neutral.
///
/// The master button moves both layers so that "no post process" means the same thing wherever it
/// is pressed; the individual buttons are there for when only one layer is wanted.
///
/// NOTE: every write lands in an ASSET — the materials' floats and PC_Renderer's feature flags —
/// so the change persists and shows up in git, which is exactly what is wanted from an authoring
/// tool like this, but worth knowing before committing. It is the same behaviour
/// <see cref="PS1EffectApplier"/> already documents.
///
/// Create: Project > right click > Create > Rendering > Post Process Toggle.
/// </summary>
[CreateAssetMenu(fileName = "SO_PostProcessToggle", menuName = "Rendering/Post Process Toggle")]
public class SO_PostProcessToggle : ScriptableObject
{
    [Header("PSX filter")]
    [Tooltip("Assets/Materials/Post Process/PS1Effect.mat")]
    [SerializeField] private Material ps1Material;

    [Tooltip("Master property of the PSX shader. Do not touch unless the property is renamed in " +
             "PS1_PostProcess_HLSL.shader.")]
    [SerializeField] private string ps1EnableProperty = "_EnableEffect";

    [Header("Vision fog")]
    [Tooltip("Every fog material that has to be switched off together. In this project there are " +
             "two: VisionFog.mat and VisionFog_SilentHill.mat. With only one assigned the other " +
             "stays on, and switching off looks broken as soon as the preset changes.")]
    [SerializeField] private List<Material> visionFogMaterials = new List<Material>();

    [Tooltip("Master property of the fog Shader Graph.")]
    [SerializeField] private string visionFogEnableProperty = "_EnableVisionFog";

    [Header("Renderer features (PC_Renderer)")]
    [Tooltip("The fullscreen passes on Assets/Settings/PC_Renderer.asset that draw the effects " +
             "above: PSXEffect and Vision Fog. Switching these off takes the passes out of the " +
             "frame instead of leaving them running with a pass-through shader. Leave " +
             "ScreenSpaceAmbientOcclusion out of this list unless the intention really is to " +
             "change the lighting as well.")]
    [SerializeField] private List<ScriptableRendererFeature> rendererFeatures = new List<ScriptableRendererFeature>();

    public Material Ps1Material => ps1Material;
    public IReadOnlyList<Material> VisionFogMaterials => visionFogMaterials;
    public IReadOnlyList<ScriptableRendererFeature> RendererFeatures => rendererFeatures;

    /// <summary>Whether the PSX filter is on. Also false when no material is assigned: what does
    /// not exist is not affecting the screen.</summary>
    public bool IsPs1Enabled => IsEnabled(ps1Material, ps1EnableProperty);

    /// <summary>
    /// Whether the fog is on. One material being on is enough: while any of them is still on the
    /// post process is still visible, and the master button has to offer turning it off, not on.
    /// </summary>
    public bool IsVisionFogEnabled
    {
        get
        {
            for (int i = 0; i < visionFogMaterials.Count; i++)
            {
                if (IsEnabled(visionFogMaterials[i], visionFogEnableProperty)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Whether any of the passes is still being queued on the renderer. Same rule as the fog: one
    /// active feature is enough, because that pass is still in the frame.
    /// </summary>
    public bool AreRendererFeaturesEnabled
    {
        get
        {
            for (int i = 0; i < rendererFeatures.Count; i++)
            {
                ScriptableRendererFeature feature = rendererFeatures[i];
                if (feature != null && feature.isActive) return true;
            }
            return false;
        }
    }

    /// <summary>What decides the master button's label: while anything is still on, it offers to
    /// switch off.</summary>
    public bool IsAnyEnabled => IsPs1Enabled || IsVisionFogEnabled || AreRendererFeaturesEnabled;

    // ── Actions ─────────────────────────────────────────────────────────────

    public void SetPs1Enabled(bool enabled) => SetEnabled(ps1Material, ps1EnableProperty, enabled);

    public void SetVisionFogEnabled(bool enabled)
    {
        for (int i = 0; i < visionFogMaterials.Count; i++)
        {
            SetEnabled(visionFogMaterials[i], visionFogEnableProperty, enabled);
        }
    }

    /// <summary>
    /// Adds or removes the passes on PC_Renderer. This affects EVERY camera rendering with that
    /// renderer, not only the one being looked at — which is the point, and also the reason it is
    /// a separate button from the material properties.
    /// </summary>
    public void SetRendererFeaturesEnabled(bool enabled)
    {
        for (int i = 0; i < rendererFeatures.Count; i++)
        {
            ScriptableRendererFeature feature = rendererFeatures[i];
            if (feature != null) feature.SetActive(enabled);
        }
    }

    public void SetAllEnabled(bool enabled)
    {
        SetPs1Enabled(enabled);
        SetVisionFogEnabled(enabled);
        SetRendererFeaturesEnabled(enabled);
    }

    /// <summary>
    /// Flips the global state. With one on and the other off it switches everything off: "disable
    /// post process" has to leave the screen clean whatever the starting point, not swap which one
    /// is on.
    /// </summary>
    public void ToggleAll() => SetAllEnabled(!IsAnyEnabled);

    public void TogglePs1() => SetPs1Enabled(!IsPs1Enabled);

    public void ToggleVisionFog() => SetVisionFogEnabled(!IsVisionFogEnabled);

    public void ToggleRendererFeatures() => SetRendererFeaturesEnabled(!AreRendererFeaturesEnabled);

    /// <summary>Every asset this toggle writes into — the materials and the renderer features — so
    /// the editor can register them for Undo and mark them dirty in a single pass.</summary>
    public void CollectTargets(List<Object> results)
    {
        results.Clear();
        AddTarget(results, ps1Material);

        for (int i = 0; i < visionFogMaterials.Count; i++) AddTarget(results, visionFogMaterials[i]);
        for (int i = 0; i < rendererFeatures.Count; i++) AddTarget(results, rendererFeatures[i]);
    }

    private static void AddTarget(List<Object> results, Object target)
    {
        if (target != null && !results.Contains(target)) results.Add(target);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Fills in the materials and the renderer features on creation by looking them up by path.
    ///
    /// Unity calls Reset() when the asset is created from the Create menu, so in practice this
    /// comes out ready without anyone dragging anything. If a path does not exist (the material
    /// was moved) the field is left empty to be assigned by hand — it is not worth logging from a
    /// method that runs on asset creation.
    /// </summary>
    private void Reset()
    {
        const string root = "Assets/Materials/Post Process/";

        ps1Material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(root + "PS1Effect.mat");

        visionFogMaterials = new List<Material>();
        AddIfFound(root + "VisionFog.mat");
        AddIfFound(root + "VisionFog_SilentHill.mat");

        rendererFeatures = new List<ScriptableRendererFeature>();
        AddRendererFeaturesFrom("Assets/Settings/PC_Renderer.asset");
    }

    private void AddIfFound(string path)
    {
        Material material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) visionFogMaterials.Add(material);
    }

    /// <summary>
    /// Picks, out of the renderer's own sub-assets, the fullscreen passes that blit one of the
    /// materials above — hence LoadAllAssetsAtPath, since renderer features are stored inside
    /// PC_Renderer.asset rather than as files of their own.
    ///
    /// Matching on the material rather than on the feature's name is what keeps SSAO — and
    /// anything else added to the renderer later — out of a toggle that is only about these two
    /// effects.
    /// </summary>
    private void AddRendererFeaturesFrom(string rendererPath)
    {
        Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(rendererPath);

        for (int i = 0; i < assets.Length; i++)
        {
            if (!(assets[i] is FullScreenPassRendererFeature feature)) continue;
            if (!IsToggledMaterial(feature.passMaterial)) continue;

            rendererFeatures.Add(feature);
        }
    }

    private bool IsToggledMaterial(Material material)
    {
        if (material == null) return false;

        return material == ps1Material || visionFogMaterials.Contains(material);
    }
#endif

    // ── Property access ─────────────────────────────────────────────────────

    private static bool IsEnabled(Material material, string property)
    {
        if (material == null || string.IsNullOrEmpty(property)) return false;
        if (!material.HasProperty(property)) return false;

        return material.GetFloat(property) >= 0.5f;
    }

    /// <summary>
    /// Warns rather than failing silently when the property does not exist: that is what happens
    /// if someone swaps the material's shader or renames the property, and the symptom would be a
    /// button that gets pressed and does nothing.
    /// </summary>
    private static void SetEnabled(Material material, string property, bool enabled)
    {
        if (material == null) return;

        if (string.IsNullOrEmpty(property) || !material.HasProperty(property))
        {
            Debug.LogWarning($"[SO_PostProcessToggle] Material '{material.name}' has no property " +
                             $"'{property}'. Check that it still uses the right shader, or fix the " +
                             "property name on this asset.", material);
            return;
        }

        material.SetFloat(property, enabled ? 1f : 0f);
    }
}
