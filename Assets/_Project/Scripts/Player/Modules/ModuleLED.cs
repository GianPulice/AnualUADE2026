using UnityEngine;

/// <summary>
/// Drives one LED on the player's rig from the status of its own module, independently of the
/// other LEDs:
///   • Inactive → off (the default until that module's countdown starts)
///   • Active   → the orange-yellow blink (LED_Parpadeo Animator + LED_Naranja)
///   • Resolved → steady green
///   • Exploded → steady red, from the frame the explosion VFX goes off (not the state change)
///
/// The colours are the LED's own emissive materials (LED_Naranja / LED_Verde / LED_Rojo),
/// untextured: the button textures are solid red and would tint any colour back to red. The Light
/// only glows for the steady ones when castLightWhenSteady is on.
///
/// The blink Animator writes _EmissionColor through renderer property blocks and the Light's
/// intensity every frame, so it is disabled outside Active and the blocks are cleared before a
/// material swap — otherwise the last animated orange would bleed over the off/green/red materials.
///
/// Polls the status each frame instead of only listening to <see cref="ModuleEvents"/>: a session
/// reset or a loaded save rebuilds the runtimes without raising OnStateChanged.
/// </summary>
public class ModuleLED : MonoBehaviour
{
    [Tooltip("The module this LED reports. Each LED only reacts to its own module.")]
    [SerializeField] private ModuleData module;

    [Header("Parts (auto-filled from this object if left empty)")]
    [SerializeField] private Light ledLight;
    [Tooltip("The LED_Parpadeo Animator. Only runs while the module is Active.")]
    [SerializeField] private Animator blinkAnimator;
    [SerializeField] private Renderer[] renderers;

    [Header("Materials")]
    [Tooltip("Blinking orange-yellow, used while Active. Empty = the material the renderers start with.")]
    [SerializeField] private Material activeMaterial;
    [SerializeField] private Material offMaterial;
    [SerializeField] private Material resolvedMaterial;

    [Header("Resolved light")]
    [SerializeField] private Color resolvedLightColor = new Color(0.1f, 1f, 0.25f, 1f);
    [SerializeField] private float resolvedLightIntensity = 1.5f;

    [Header("Exploded light")]
    [Tooltip("Material shown once this module has exploded. Empty = the off material.")]
    [SerializeField] private Material explodedMaterial;
    [SerializeField] private Color explodedLightColor = new Color(1f, 0.06f, 0.06f, 1f);   // red
    [SerializeField] private float explodedLightIntensity = 1.5f;

    [Tooltip("Off = the green and yellow states only light up the LED itself (its emissive " +
             "material), with no glow cast around the player. On = the Light is also tinted and on.")]
    [SerializeField] private bool castLightWhenSteady = false;

    private Color activeLightColor;
    private float activeLightIntensity;
    private ModuleStatus? shownStatus;

    /// <summary>
    /// The module this LED reports. Read by <see cref="ModuleExplosionSequence"/> so a blast goes
    /// off on the LED of the module that actually exploded, and moves with it.
    /// </summary>
    public ModuleData Module => module;

    /// <summary>
    /// Where that blast is spawned: the LED's own Light, so the explosion sits exactly on the glow
    /// the player has been watching count down. Falls back to this object when there is no Light.
    /// </summary>
    public Transform ExplosionAnchor => ledLight != null ? ledLight.transform : transform;

    // The Active → Exploded switch waits for the blast to be SEEN (ModuleEvents.OnExplosionShown):
    // with a cinematic the camera first travels to the body part, and the LED going red before the
    // VFX reads as a spoiler. Bounded so a presentation that never plays the VFX cannot freeze it.
    private const float MaxWaitForExplosionVfx = 10f;
    private bool explosionShown;
    private float explodedAt = -1f;

    private void Awake()
    {
        if (ledLight == null) ledLight = GetComponent<Light>();
        if (blinkAnimator == null) blinkAnimator = GetComponent<Animator>();
        if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>(true);
        if (activeMaterial == null && renderers.Length > 0) activeMaterial = renderers[0].sharedMaterial;

        if (ledLight != null)
        {
            activeLightColor = ledLight.color;
            activeLightIntensity = ledLight.intensity;
        }

        if (module == null)
            Debug.LogWarning($"[ModuleLED] '{name}' has no ModuleData assigned — it will stay off.", this);
    }

    private void OnEnable()
    {
        ModuleEvents.OnExplosionShown += HandleExplosionShown;
        shownStatus = null;
        Refresh();
    }

    private void OnDisable()
    {
        ModuleEvents.OnExplosionShown -= HandleExplosionShown;
    }

    private void Update() => Refresh();

    private void HandleExplosionShown(ModuleRuntime runtime)
    {
        if (module == null || runtime == null || runtime.ModuleID != module.ModuleID) return;
        explosionShown = true;
        Refresh();   // Same frame as the VFX.
    }

    private void Refresh()
    {
        ModuleStatus status = CurrentStatus();
        if (shownStatus == status) return;

        // Keep blinking until the explosion is on screen. Only from Active: an LED enabled on an
        // already exploded module (loaded save, session reset) shows red straight away. With no
        // presentation in the scene nobody raises OnExplosionShown, so it switches at once.
        if (status == ModuleStatus.Exploded && shownStatus == ModuleStatus.Active &&
            ModuleEvents.PenaltyPresenterActive && !explosionShown)
        {
            if (explodedAt < 0f) explodedAt = Time.unscaledTime;
            if (Time.unscaledTime - explodedAt < MaxWaitForExplosionVfx) return;
        }

        explodedAt = -1f;
        if (status != ModuleStatus.Exploded) explosionShown = false;

        shownStatus = status;
        Apply(status);
    }

    private ModuleStatus CurrentStatus()
    {
        if (module == null || !ModuleManager.Exists) return ModuleStatus.Inactive;
        ModuleRuntime runtime = ModuleManager.Instance.GetRuntime(module.ModuleID);
        return runtime != null ? runtime.Status : ModuleStatus.Inactive;
    }

    private void Apply(ModuleStatus status)
    {
        switch (status)
        {
            case ModuleStatus.Active:
                SetMaterial(activeMaterial);
                if (ledLight != null)
                {
                    ledLight.color = activeLightColor;
                    ledLight.intensity = activeLightIntensity;
                    ledLight.enabled = true;
                }
                if (blinkAnimator != null)
                {
                    blinkAnimator.enabled = true;
                    blinkAnimator.Rebind();   // Start the blink from its first frame.
                }
                break;

            case ModuleStatus.Resolved:
                StopBlink();
                SetMaterial(resolvedMaterial);
                if (ledLight != null)
                {
                    ledLight.color = resolvedLightColor;
                    ledLight.intensity = resolvedLightIntensity;
                    ledLight.enabled = castLightWhenSteady;
                }
                break;

            case ModuleStatus.Exploded:
                StopBlink();
                SetMaterial(explodedMaterial != null ? explodedMaterial : offMaterial);
                if (ledLight != null)
                {
                    ledLight.color = explodedLightColor;
                    ledLight.intensity = explodedLightIntensity;
                    ledLight.enabled = castLightWhenSteady;
                }
                break;

            default: // Inactive
                StopBlink();
                SetMaterial(offMaterial);
                if (ledLight != null) ledLight.enabled = false;
                break;
        }
    }

    private void StopBlink()
    {
        if (blinkAnimator != null) blinkAnimator.enabled = false;
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(null);
    }

    private void SetMaterial(Material material)
    {
        if (material == null) return;
        foreach (Renderer r in renderers)
            if (r != null) r.sharedMaterial = material;
    }
}
