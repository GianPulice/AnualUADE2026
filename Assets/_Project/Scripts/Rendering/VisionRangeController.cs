using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Driver of the vision fog shader (<c>Hidden/Custom/VisionFogHLSL</c>). Sets the globals the
/// shader reads and handles smooth transitions between fog presets.
///
/// Configuration model:
///   - <see cref="defaultConfig"/>: the preset the fog uses when the player is not inside
///     any LightZone (default = dark corridors).
///   - <see cref="PushConfig"/> / <see cref="PopConfig"/>: public API for LightZone
///     triggers. Keeps a stack — the innermost zone wins.
///
/// The player lives in another scene (additive gameplay), so it comes from
/// <see cref="PlayerRegistry"/> rather than being searched for by tag.
/// While there is no player, it sets <c>_VisionEnd = 0</c> → the shader early-outs → no fog.
///
/// Every scalar and colour global goes out through <see cref="VisionFogState.PushToShader"/>
/// rather than from here directly. That is not tidiness: <c>Shader.SetGlobalColor</c> performs no
/// sRGB→linear conversion, so a colour written from anywhere else lands in the shader 2-3x too
/// bright in this Linear project. Keeping the writes in one method is what stops that regressing.
/// The bypass-zone arrays are the exception — only this class knows which zones are registered —
/// and they convert through <see cref="VisionFogState.ToLinear"/> on the way out.
/// </summary>
[DefaultExecutionOrder(100)]
public class VisionRangeController : MonoBehaviour
{
    /// <summary>Must match VISION_FOG_MAX_BYPASS in VisionFog_HLSL.shader.</summary>
    public const int MaxBypassZones = 16;

    /// <summary>
    /// Must match VISION_FOG_MAX_BEACONS in VisionFog_HLSL.shader.
    ///
    /// Sized for everything that can be lit at once, with room to spare: the Nemesis's two eyes,
    /// the escape's three guide lamps and the six SP2 ceiling lamps make eleven. Slots go out in
    /// registration order and the eyes re-register every time the Nemesis wakes, so a full array
    /// drops the eyes first. Changing this needs an editor restart: a global array keeps the
    /// length of its first upload for the whole session.
    /// </summary>
    public const int MaxBeacons = 16;

    /// <summary>Must match VISION_FOG_MAX_VOLUMES in VisionFog_HLSL.shader.</summary>
    public const int MaxLightVolumes = 16;

    [Header("Default config")]
    [Tooltip("Preset applied when the player is not inside any LightZone. " +
             "Usually a 'dark' / oppressive area — LightZones modulate upwards from it.")]
    [SerializeField] private SO_VisionFogConfig defaultConfig;

    [Header("Beacons")]
    [Tooltip("Tolerancia en metros de la prueba de oclusión de un beacon contra la geometría.\n\n" +
             "Los ojos del Nemesis están a centímetros de su propia cara: sin esto la malla de la " +
             "cabeza tapa el punto y no se ve nunca. Subilo sólo si algún beacon parpadea contra " +
             "su propio modelo; bajarlo lo hace más estricto y evita que se vea a través de una " +
             "pared muy delgada.")]
    [SerializeField, Min(0f)] private float beaconDepthBias = 0.25f;

    [Tooltip("Techo del radio en pantalla de un beacon, en píxeles. Es lo que evita que de cerca " +
             "los ojos sean una mancha que tapa media pantalla.")]
    [SerializeField, Min(1f)] private float beaconMaxPixels = 26f;

    [Tooltip("Dureza de la caída del beacon. Más alto = núcleo más chico y borde más marcado; " +
             "más bajo = un halo blando y grande.")]
    [SerializeField, Min(0.01f)] private float beaconFalloff = 4f;

    [Header("Light volumes (FogLightVolume)")]
    [Tooltip("How much the fog dims the beams, relative to how much it dims a surface at the same " +
             "distance.\n\n" +
             "0 = a beam looks the same near and far. 1 = the fog swallows it like the wall behind " +
             "it, so it is not seen from afar.")]
    [SerializeField, Range(0f, 1f)] private float volumeFogExtinction = 0.25f;

    [Tooltip("Distance to the player (m) below which the beam is not drawn. It keeps the stretch of " +
             "beam between the camera and the character from laying a haze over them.")]
    [SerializeField, Min(0f)] private float volumeNearFadeStart = 1f;

    [Tooltip("Distance to the player (m) from which the beam is at full brightness. Between this and " +
             "the start it fades in.")]
    [SerializeField, Min(0f)] private float volumeNearFadeEnd = 3.5f;

    [Tooltip("Size (m) of the dust patches drifting inside the beam.")]
    [SerializeField, Min(0.05f)] private float volumeDustSize = 1.2f;

    [Tooltip("How visible the dust is. 0 = a smooth beam; 1 = strong patches.")]
    [SerializeField, Range(0f, 1f)] private float volumeDustAmount = 0.5f;

    [Tooltip("Speed (m/s) at which the dust sinks through the beam.")]
    [SerializeField, Min(0f)] private float volumeDustSpeed = 0.15f;

    [Header("Player")]
    [Tooltip("Optional manual assignment, mainly for Timeline preview. If empty, the player " +
             "comes from PlayerRegistry.")]
    [SerializeField] private Transform playerOverride;

    // ── State ───────────────────────────────────────────────────────────────
    private Transform _player;

    // Where the fog is measured from when it is not the player: a cinematic shot framed far from
    // them. See SetCentreOverride.
    private Transform _centreOverride;

    // Stack of active configs: the last one pushed wins. The bottom is always defaultConfig.
    private readonly List<SO_VisionFogConfig> _configStack = new List<SO_VisionFogConfig>();

    // Player light source (optional) and bypass zones registered by their components.
    private FogLightSource _playerLight;
    private static readonly List<FogLightBypass> s_bypassZones = new List<FogLightBypass>(MaxBypassZones);
    private static readonly List<FogBeacon> s_beacons = new List<FogBeacon>(MaxBeacons);
    private static readonly List<FogLightVolume> s_volumes = new List<FogLightVolume>(MaxLightVolumes);

    // Reusable buffers. Unity locks a global array's size on first upload, so these are allocated
    // at full length once and the unused tail is zeroed rather than the array being resized.
    private readonly Vector4[] _bypassData  = new Vector4[MaxBypassZones];
    private readonly Vector4[] _bypassColor = new Vector4[MaxBypassZones];
    private readonly Vector4[] _bypassAxis  = new Vector4[MaxBypassZones]; // xyz = cone axis, w = cos(half); w >= 2 => sphere

    private readonly Vector4[] _beaconData  = new Vector4[MaxBeacons]; // xyz = world pos, w = radius in metres
    private readonly Vector4[] _beaconColor = new Vector4[MaxBeacons]; // rgb = linear colour * intensity, a = min pixel radius

    private readonly Vector4[] _volumeData  = new Vector4[MaxLightVolumes]; // xyz = apex, w = range in metres
    private readonly Vector4[] _volumeAxis  = new Vector4[MaxLightVolumes]; // xyz = axis, w = cos(outer half-angle); < -1.5 => sphere
    private readonly Vector4[] _volumeColor = new Vector4[MaxLightVolumes]; // rgb = linear colour * strength, a = cos(inner half-angle)

    // Current values (interpolated frame by frame) and where they are heading.
    private VisionFogState _current;
    private VisionFogState _target;

    // Current transition speed (in units per second, derived from transitionDuration).
    private float _lerpRate = 4f;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void OnEnable()
    {
        // Catch-up subscription: this controller usually wakes up before the additive gameplay
        // scene brings the player in, but not always. SubscribeAndCatchUp covers both orders.
        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered += HandlePlayerUnregistered;

        // Same catch-up idea for the module light, in the other direction. FogLightSource pushes
        // itself from its own OnEnable, which covers "controller first". The opposite order —
        // the light already enabled in the gameplay scene before this controller's scene loads —
        // left it pushing into nothing, with no retry, and the fog never opened around the player.
        if (_playerLight == null) _playerLight = FindAnyObjectByType<FogLightSource>();
    }

    private void OnDisable()
    {
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered -= HandlePlayerUnregistered;
    }

    private void HandlePlayerRegistered(PlayerStateManager player)
    {
        // playerOverride wins: it is the manual hook used for Timeline scrub preview.
        if (playerOverride != null) return;
        _player = player.transform;
    }

    private void HandlePlayerUnregistered(PlayerStateManager player)
    {
        if (playerOverride != null) return;
        _player = null;
    }

    private void Start()
    {
        if (defaultConfig != null)
        {
            ApplyTargetsFromConfig(defaultConfig);
            // Initialise the current values to the target to avoid lerping from 0 at startup.
            _current = _target;
        }

        if (playerOverride != null) _player = playerOverride;
    }

    private void LateUpdate()
    {
        if (_player == null)
        {
            Shader.SetGlobalFloat(VisionFogState.Ids.VisionEnd, 0f); // shader early-out
            Shader.SetGlobalFloat(VisionFogState.Ids.PlayerLightRange, 0f);
            Shader.SetGlobalInt(VisionFogState.Ids.BypassCount, 0);
            Shader.SetGlobalInt(VisionFogState.Ids.BeaconCount, 0);
            Shader.SetGlobalInt(VisionFogState.Ids.VolumeCount, 0);
            return;
        }

        // Re-read the active config every frame — that way tweaking the SO's sliders in the
        // Inspector while in Play is visible immediately, without needing a new Push/Pop.
        SO_VisionFogConfig activeConfig = _configStack.Count > 0
            ? _configStack[_configStack.Count - 1]
            : defaultConfig;
        if (activeConfig != null) ApplyTargetsFromConfig(activeConfig);

        _current = VisionFogState.Lerp(_current, _target, Time.deltaTime * _lerpRate);

        // The module light — if there is a FogLightSource assigned we take its transform in
        // real time; otherwise we fall back to the player's position.
        Vector3 lightPos = _playerLight != null ? _playerLight.transform.position : _player.position;

        // If the FogLightSource is in "read from the Light component" mode (default), its
        // range/colour/clear win over those of the SO — that way the future module degradation
        // (§2.5.1) lowers the fog clear radius automatically. Applied to a copy so the override
        // never leaks into the interpolated state and stick there once the light goes away.
        VisionFogState frame = _current;
        if (_playerLight != null && _playerLight.HasLightOverride)
        {
            frame.playerLightRange = _playerLight.OverrideRange;
            frame.playerLightClear = _playerLight.OverrideClear;
            frame.playerLightColor = _playerLight.OverrideColor;
        }

        Vector3 centre = _centreOverride != null ? _centreOverride.position : _player.position;
        frame.PushToShader(centre, lightPos);
        PushBypassZones(frame);
        PushBeacons(centre);
        PushLightVolumes();
    }

    // ── Public API for LightZones ───────────────────────────────────────────

    /// <summary>
    /// Pushes a config onto the stack. Called by LightZones when the player enters.
    /// Handles nesting — the innermost zone (last pushed) is the one applied.
    /// </summary>
    public void PushConfig(SO_VisionFogConfig config)
    {
        if (config == null) return;
        _configStack.Add(config);
        ApplyTargetsFromConfig(config);
    }

    /// <summary>
    /// Pops a specific config from the stack. Called by LightZones on exit.
    /// If the zone leaving was not on top (odd nesting), it is still removed but the
    /// target does not change until the actual top is popped.
    /// </summary>
    public void PopConfig(SO_VisionFogConfig config)
    {
        if (config == null) return;
        int lastIndex = _configStack.LastIndexOf(config);
        if (lastIndex < 0) return;

        bool wasTop = lastIndex == _configStack.Count - 1;
        _configStack.RemoveAt(lastIndex);

        if (wasTop)
        {
            SO_VisionFogConfig newTop = _configStack.Count > 0
                ? _configStack[_configStack.Count - 1]
                : defaultConfig;

            if (newTop != null) ApplyTargetsFromConfig(newTop);
        }
    }

    /// <summary>
    /// Measures the fog from <paramref name="centre"/> instead of the player until cleared. For a
    /// cinematic whose camera is far from the player: centred on the player, a shot 13 m away
    /// renders fully fogged, and the only other way out was turning the fog off for the shot —
    /// which made the cinematic look nothing like the gameplay around it (WIR-040). The module
    /// light stays on the player.
    /// </summary>
    public void SetCentreOverride(Transform centre) => _centreOverride = centre;

    /// <summary>Back to the player — only if <paramref name="centre"/> is still the override, so a
    /// caller never clears someone else's.</summary>
    public void ClearCentreOverride(Transform centre)
    {
        if (_centreOverride == centre) _centreOverride = null;
    }

    /// <summary>Change the default config at runtime (e.g. on a level change).</summary>
    public void SetDefaultConfig(SO_VisionFogConfig newDefault)
    {
        defaultConfig = newDefault;
        // If there are no active zones, apply the new default.
        if (_configStack.Count == 0 && newDefault != null)
            ApplyTargetsFromConfig(newDefault);
    }

    // ── API for FogLightSource / FogLightBypass ─────────────────────────────

    /// <summary>Set (or clear with null) the module light that is read every frame.</summary>
    public void SetPlayerLightSource(FogLightSource source) => _playerLight = source;

    /// <summary>The light currently driving the fog opening, or null. Read by
    /// <see cref="FogLightSource"/> so it only clears itself and never another source.</summary>
    public FogLightSource PlayerLightSource => _playerLight;

    public static void RegisterBypass(FogLightBypass zone)
    {
        if (zone == null || s_bypassZones.Contains(zone)) return;
        s_bypassZones.Add(zone);
    }

    public static void UnregisterBypass(FogLightBypass zone)
    {
        if (zone == null) return;
        s_bypassZones.Remove(zone);
    }

    // -- API for FogBeacon --------------------------------------------------

    public static void RegisterBeacon(FogBeacon beacon)
    {
        if (beacon == null || s_beacons.Contains(beacon)) return;
        s_beacons.Add(beacon);
    }

    public static void UnregisterBeacon(FogBeacon beacon)
    {
        if (beacon == null) return;
        s_beacons.Remove(beacon);
    }

    // -- API for FogLightVolume ---------------------------------------------

    public static void RegisterLightVolume(FogLightVolume volume)
    {
        if (volume == null || s_volumes.Contains(volume)) return;
        s_volumes.Add(volume);
    }

    public static void UnregisterLightVolume(FogLightVolume volume)
    {
        if (volume == null) return;
        s_volumes.Remove(volume);
    }

    // ── API for VisionFogTrack (Timeline) and the config inspector ──────────

    /// <summary>
    /// Writes the globals directly, bypassing the stack and the LateUpdate lerp.
    /// Called by <c>VisionFogMixerBehaviour</c> with the already-blended result of the
    /// clips active on the track, and by the config inspector's preview button — both meant for
    /// scrub/preview in the editor without pressing Play.
    /// </summary>
    public void ApplyPreviewBlend(in VisionFogState state)
    {
        Vector3 previewPos = playerOverride != null ? playerOverride.position : transform.position;
        Vector3 lightPos = _playerLight != null ? _playerLight.transform.position : previewPos;

        state.PushToShader(previewPos, lightPos);
        PushBypassZones(state);
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private void ApplyTargetsFromConfig(SO_VisionFogConfig config)
    {
        _target = VisionFogState.FromConfig(config);

        // Convert transitionDuration (seconds) into a lerp rate (1/s).
        // Approximation: to reach 99% in `transitionDuration` seconds, the exponential rate
        // is ~4.6 / duration. We use 4 for a slightly less abrupt curve.
        _lerpRate = config.transitionDuration > 0.01f
            ? 4f / config.transitionDuration
            : 1000f; // effectively instant
    }

    /// <summary>
    /// Compacts the active bypass zones into the two shader arrays. Two arrays rather than one
    /// because a zone carries more than fits in a float4: position + radius in the first,
    /// colour × intensity + clear amount in the second, index-matched.
    /// </summary>
    private void PushBypassZones(in VisionFogState state)
    {
        int count = 0;
        for (int i = 0; i < s_bypassZones.Count && count < MaxBypassZones; i++)
        {
            FogLightBypass zone = s_bypassZones[i];
            if (zone == null || !zone.isActiveAndEnabled || zone.radius <= 0f) continue;

            zone.Resolve(state, out Color color, out float intensity, out float clear);

            Vector3 p = zone.WorldCenter;
            _bypassData[count] = new Vector4(p.x, p.y, p.z, zone.radius);

            // A cone zone (typically fed by a Spot Light) carves the spherical pool by angle;
            // w >= 2 is the sentinel the shader reads as "plain sphere, skip the angular test".
            _bypassAxis[count] = zone.TryGetCone(out Vector3 axis, out float cosHalf)
                ? new Vector4(axis.x, axis.y, axis.z, cosHalf)
                : new Vector4(0f, 0f, 0f, 2f);

            // Pre-multiplied by intensity so the shader does one fewer multiply per zone per
            // pixel, and converted here because SetGlobalVectorArray does no colour conversion.
            Vector4 linear = VisionFogState.ToLinear(color) * intensity;
            _bypassColor[count] = new Vector4(linear.x, linear.y, linear.z, Mathf.Clamp01(clear));

            count++;
        }

        // Clear the rest so no garbage from previous frames is read.
        for (int i = count; i < MaxBypassZones; i++)
        {
            _bypassData[i]  = Vector4.zero;
            _bypassColor[i] = Vector4.zero;
            _bypassAxis[i]  = Vector4.zero;
        }

        Shader.SetGlobalVectorArray(VisionFogState.Ids.BypassData, _bypassData);
        Shader.SetGlobalVectorArray(VisionFogState.Ids.BypassColor, _bypassColor);
        Shader.SetGlobalVectorArray(VisionFogState.Ids.BypassAxis, _bypassAxis);
        Shader.SetGlobalInt(VisionFogState.Ids.BypassCount, count);
    }

    /// <summary>
    /// Compacts the active beacons into the shader arrays.
    ///
    /// A beacon is NOT a bypass zone, and the difference is the whole point: a bypass zone injects
    /// light into <c>sceneColor</c> and is then attenuated by the very fog it is trying to punch
    /// through, so past <c>visionEnd</c> it contributes less than the fog's own in-scattering. A
    /// beacon is composited AFTER the extinction, so the brightness asked for here is the
    /// brightness that reaches the screen, in every preset.
    ///
    /// It lives in this class for the reason <see cref="VisionFogState"/>'s docstring gives: every
    /// fog global leaves from one place so the sRGB-to-linear conversion cannot regress. Colours
    /// go out through <see cref="VisionFogState.ToLinear"/>, same as the bypass arrays.
    /// </summary>
    /// <param name="viewerPosition">Where the player is, for each beacon's own facing and distance
    /// fades. The camera would be marginally more correct, but it moves in LateUpdate under
    /// Cinemachine and the player does not.</param>
    private void PushBeacons(Vector3 viewerPosition)
    {
        int count = 0;
        for (int i = 0; i < s_beacons.Count && count < MaxBeacons; i++)
        {
            FogBeacon beacon = s_beacons[i];
            if (beacon == null || !beacon.isActiveAndEnabled) continue;
            if (!beacon.Resolve(viewerPosition, out Color color, out float intensity, out float minPixels)) continue;

            Vector3 p = beacon.WorldCentre;
            _beaconData[count] = new Vector4(p.x, p.y, p.z, beacon.WorldRadius);

            Vector4 linear = VisionFogState.ToLinear(color) * intensity;
            _beaconColor[count] = new Vector4(linear.x, linear.y, linear.z, minPixels);

            count++;
        }

        for (int i = count; i < MaxBeacons; i++)
        {
            _beaconData[i]  = Vector4.zero;
            _beaconColor[i] = Vector4.zero;
        }

        Shader.SetGlobalVectorArray(VisionFogState.Ids.BeaconData, _beaconData);
        Shader.SetGlobalVectorArray(VisionFogState.Ids.BeaconColor, _beaconColor);
        Shader.SetGlobalInt(VisionFogState.Ids.BeaconCount, count);

        Shader.SetGlobalFloat(VisionFogState.Ids.BeaconDepthBias, beaconDepthBias);
        Shader.SetGlobalFloat(VisionFogState.Ids.BeaconMaxPixels, beaconMaxPixels);
        Shader.SetGlobalFloat(VisionFogState.Ids.BeaconFalloff, beaconFalloff);
    }

    /// <summary>
    /// Compacts the lit light volumes into the shader arrays. Same pattern as the beacons: only the
    /// ones that resolve go out, colours convert through <see cref="VisionFogState.ToLinear"/>, and
    /// the unused tail is zeroed. The shader integrates each one along the view ray — see
    /// <see cref="FogLightVolume"/> for why that is not a bypass zone.
    /// </summary>
    private void PushLightVolumes()
    {
        int count = 0;
        for (int i = 0; i < s_volumes.Count && count < MaxLightVolumes; i++)
        {
            FogLightVolume volume = s_volumes[i];
            if (volume == null || !volume.isActiveAndEnabled) continue;
            if (!volume.Resolve(out Vector3 apex, out float range, out Vector3 axis, out float cosOuter,
                                out float cosInner, out Color color, out float strength)) continue;

            _volumeData[count] = new Vector4(apex.x, apex.y, apex.z, range);
            _volumeAxis[count] = new Vector4(axis.x, axis.y, axis.z, cosOuter);

            Vector4 linear = VisionFogState.ToLinear(color) * strength;
            _volumeColor[count] = new Vector4(linear.x, linear.y, linear.z, cosInner);

            count++;
        }

        for (int i = count; i < MaxLightVolumes; i++)
        {
            _volumeData[i]  = Vector4.zero;
            _volumeAxis[i]  = Vector4.zero;
            _volumeColor[i] = Vector4.zero;
        }

        Shader.SetGlobalVectorArray(VisionFogState.Ids.VolumeData, _volumeData);
        Shader.SetGlobalVectorArray(VisionFogState.Ids.VolumeAxis, _volumeAxis);
        Shader.SetGlobalVectorArray(VisionFogState.Ids.VolumeColor, _volumeColor);
        Shader.SetGlobalInt(VisionFogState.Ids.VolumeCount, count);

        Shader.SetGlobalFloat(VisionFogState.Ids.VolumeFogExtinction, volumeFogExtinction);
        Shader.SetGlobalFloat(VisionFogState.Ids.VolumeNearStart, volumeNearFadeStart);
        // Kept apart from the start: the shader fades with a smoothstep between the two.
        Shader.SetGlobalFloat(VisionFogState.Ids.VolumeNearEnd, Mathf.Max(volumeNearFadeEnd, volumeNearFadeStart + 0.01f));
        Shader.SetGlobalFloat(VisionFogState.Ids.VolumeNoiseScale, 1f / Mathf.Max(volumeDustSize, 0.05f));
        Shader.SetGlobalFloat(VisionFogState.Ids.VolumeNoiseAmount, volumeDustAmount);
        Shader.SetGlobalFloat(VisionFogState.Ids.VolumeNoiseSpeed, volumeDustSpeed);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Draws the active preset's radii in the scene, in metres, so the designer can size a room
    /// against the fog instead of guessing and then playing to check.
    ///
    /// Rings rather than spheres for the vision band: the shader measures HORIZONTAL distance
    /// (worldPos.xz), so a sphere would claim coverage above and below the player that the fog
    /// does not actually have. The module light is a sphere, because that mask does use 3D
    /// distance — the shapes differing here is the truth, not an inconsistency.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        SO_VisionFogConfig config = _configStack.Count > 0
            ? _configStack[_configStack.Count - 1]
            : defaultConfig;
        if (config == null) return;

        // In Edit Mode there is no registered player, so fall back to the preview override and
        // then to this object — the radii are what matter, not exactly where they are centred.
        Transform anchor = _player != null ? _player
                         : playerOverride != null ? playerOverride
                         : transform;
        Vector3 centre = anchor.position;

        UnityEditor.Handles.color = new Color(0.5f, 0.8f, 1f, 0.8f);
        UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, config.visionStart);
        UnityEditor.Handles.Label(centre + Vector3.right * config.visionStart,
                                  $"visionStart · {config.visionStart:0.#} m");

        UnityEditor.Handles.color = new Color(0.25f, 0.45f, 0.75f, 0.9f);
        UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, config.visionEnd);
        UnityEditor.Handles.Label(centre + Vector3.right * config.visionEnd,
                                  $"visionEnd · {config.visionEnd:0.#} m");

        if (config.playerLightRange <= 0.001f) return;

        Vector3 lightPos = _playerLight != null ? _playerLight.transform.position : centre;
        Color c = config.playerLightColor;
        UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.9f);
        UnityEditor.Handles.DrawWireDisc(lightPos, Vector3.up,    config.playerLightRange);
        UnityEditor.Handles.DrawWireDisc(lightPos, Vector3.right, config.playerLightRange);
        UnityEditor.Handles.Label(lightPos + Vector3.up * config.playerLightRange,
                                  $"luz módulo · {config.playerLightRange:0.#} m");
    }
#endif
}
