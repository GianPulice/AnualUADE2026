using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps each module's LED light off the OTHER modules, using URP Rendering Layers.
///
/// Every module (its LED button and the base model around it) carries a Point Light that blinks
/// with the countdown and is meant to glow on the floor and the body. The modules sit 0.3–0.9 m
/// apart, about as close as the floor is, so the light of one also lit the button and base of the
/// others: a faint orange flash on every module each time any of them blinked. No range can keep
/// the floor glow and drop the neighbours, so the neighbours are filtered out by layer instead:
///
///  - Each module's renderers go on a layer of their own (Light Layer 1 = Legs, 2 = Chest,
///    3 = Head) instead of Default.
///  - Its LED light keeps Default (floor, body, world) plus its own module's layer.
///  - Every other light in the world gets the three module layers added, so it keeps lighting the
///    modules exactly as before. Only lights that already reach Default are touched.
///
/// Runtime only: the editor keeps the authored look, and nothing here is saved into scenes or
/// prefabs. Lights instantiated mid-game must call <see cref="LetLightReachModules"/> themselves
/// (ExplosionVFX does); lights in any loaded scene, active or not, are handled automatically.
/// </summary>
public static class ModuleLightLayers
{
    private const uint WorldLayer = 1u << 0;   // "Default"
    private const uint AllModuleLayers = (1u << 1) | (1u << 2) | (1u << 3);

    /// <summary>The layer a module's renderers are moved to: Light Layer 1, 2 or 3.</summary>
    public static uint LayerFor(PenaltyType penalty) => 1u << (1 + (int)penalty);

    /// <summary>
    /// Puts every renderer under <paramref name="moduleRoot"/> on the module's own layer and makes
    /// <paramref name="ownLight"/> reach Default plus that layer only.
    /// </summary>
    public static void Isolate(Transform moduleRoot, Light ownLight, PenaltyType penalty)
    {
        uint own = LayerFor(penalty);

        foreach (Renderer r in moduleRoot.GetComponentsInChildren<Renderer>(true))
            r.renderingLayerMask = own;

        if (ownLight != null)
            ownLight.GetUniversalAdditionalLightData().renderingLayers = WorldLayer | own;

        LetWorldLightsReachModules();
    }

    /// <summary>Adds the module layers to every non-module light in the loaded scenes.</summary>
    public static void LetWorldLightsReachModules()
    {
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            LetLightReachModules(light);
    }

    /// <summary>
    /// Lets a world light keep lighting the modules after they left the Default layer. Skips the
    /// modules' own LED lights, and lights that never reached Default in the first place.
    /// </summary>
    public static void LetLightReachModules(Light light)
    {
        if (light == null || light.GetComponent<ModuleLED>() != null) return;

        UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
        uint mask = data.renderingLayers;
        if ((mask & WorldLayer) == 0) return;
        if ((mask & AllModuleLayers) == AllModuleLayers) return;

        data.renderingLayers = mask | AllModuleLayers;
    }

    // Additive scenes load after the player: their lights need the module layers too.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => LetWorldLightsReachModules();
}
