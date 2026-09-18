using UnityEngine;

/// <summary>
/// Drives one LED on the player's rig from the status of its own module, independently of the
/// other LEDs:
///   • Inactive → off (the default until that module's countdown starts)
///   • Active   → the red blink (LED_Parpadeo Animator + its original material)
///   • Resolved → steady green
///   • Exploded → off
///
/// The blink Animator writes _EmissionColor through renderer property blocks and the Light's
/// intensity every frame, so it is disabled outside Active and the blocks are cleared before a
/// material swap — otherwise the last animated red would bleed over the off/green materials.
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
    [Tooltip("Blinking red, used while Active. Empty = the material the renderers start with.")]
    [SerializeField] private Material activeMaterial;
    [SerializeField] private Material offMaterial;
    [SerializeField] private Material resolvedMaterial;

    [Header("Resolved light")]
    [SerializeField] private Color resolvedLightColor = new Color(0.1f, 1f, 0.25f, 1f);
    [SerializeField] private float resolvedLightIntensity = 1.5f;

    private Color activeLightColor;
    private float activeLightIntensity;
    private ModuleStatus? shownStatus;

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
        shownStatus = null;
        Refresh();
    }

    private void Update() => Refresh();

    private void Refresh()
    {
        ModuleStatus status = CurrentStatus();
        if (shownStatus == status) return;
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
                    ledLight.enabled = true;
                }
                break;

            default: // Inactive, Exploded
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
