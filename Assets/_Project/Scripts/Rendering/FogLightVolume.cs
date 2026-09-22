using UnityEngine;

/// <summary>
/// Makes a Light's volume visible in the vision fog: the beam of a spot, or the ball of air around a
/// point light, as if the air were thick with dust. The look of Inside's searchlights, kept to the
/// lamps that carry this component rather than spread over the whole fog.
///
/// ── WHY A FOURTH PIECE ──────────────────────────────────────────────────────
///
/// The other three each do one job and none of them is this one:
///   <see cref="FogLightBypass"/> works on SURFACES: it clears or tints the fog where there is
///   geometry inside its zone. The air between a lamp and the floor has no pixel of its own, so a
///   bypass can never draw a beam.
///   <see cref="FogBeacon"/> is a dot at one point. It says "there is a lamp", not "it lights this".
///   The real <see cref="Light"/> lights surfaces and nothing else.
///
/// This one integrates the light's cone along every pixel's view ray in the fog shader
/// (<c>vfLightVolumes</c> in VisionFog_HLSL.shader) and adds it AFTER the extinction, with a weaker
/// attenuation of its own. That is what lets a beam read from across a dark room without pushing any
/// intensity up, which is what washed the player out when it was tried with the bypass.
///
/// Shape, reach, colour and direction come from the Light, so the beam is always the lamp's own: a
/// Spot gives its cone (Spot Angle, softened towards Inner Spot Angle), a Point gives a sphere, and
/// the Range is where the beam ends. It also goes with the Light: a disabled Light or object shows no
/// beam, so a switch that turns the lamp off takes the beam with it, and a Light dimmed below its
/// first lit intensity (a flicker) dims the beam in proportion.
///
/// It lights nothing, and it is not drawn right around the player (the near fade on
/// <see cref="VisionRangeController"/>): a beam between the camera and the character would leave a
/// haze over them.
/// </summary>
[DisallowMultipleComponent]
public class FogLightVolume : MonoBehaviour
{
    [Tooltip("The Light whose volume is shown. Empty = the Light on this same object.\n\n" +
             "Shape, reach, colour and direction all come from it: a Spot gives its cone, a Point a " +
             "sphere.")]
    [SerializeField] private Light lightComponent;

    [Tooltip("Brightness of the beam in the fog. Absolute: it does not follow the Light's intensity, " +
             "so the lamp can be tuned for the floor without touching the beam.\n\n" +
             "0.3 = subtle. 1 = exaggerated, Inside-style.")]
    [Min(0f)] public float intensity = 0.8f;

    [Tooltip("Multiplies the Light's Range for the beam's length. 1 = the beam ends where the light " +
             "stops lighting.")]
    [Range(0.25f, 2f)] public float rangeScale = 1f;

    // The Light's intensity the first time it is seen lit: the reference the beam dims against. Taken
    // lazily because a lamp can start at 0 and be driven up later.
    private float _referenceIntensity;

    private void OnEnable()
    {
        if (lightComponent == null) lightComponent = GetComponent<Light>();
        VisionRangeController.RegisterLightVolume(this);
    }

    private void OnDisable() => VisionRangeController.UnregisterLightVolume(this);

    /// <summary>
    /// Resolves this frame's beam. Called by <see cref="VisionRangeController"/> once per volume per
    /// frame. Returns <c>false</c> when there is none (no Light, Light off or at zero), so the
    /// controller leaves the slot out instead of uploading an empty one.
    /// </summary>
    /// <param name="apex">Where the beam starts: the Light's position.</param>
    /// <param name="range">Where it ends, in metres from the apex.</param>
    /// <param name="axis">The Light's forward.</param>
    /// <param name="cosOuter">Cosine of the cone's outer half-angle; below -1.5 for a sphere.</param>
    /// <param name="cosInner">Cosine of the inner half-angle, where the edge softening ends.</param>
    /// <param name="color">Colour, still in sRGB — the controller converts.</param>
    /// <param name="strength">Brightness after following the Light.</param>
    public bool Resolve(out Vector3 apex, out float range, out Vector3 axis,
                        out float cosOuter, out float cosInner, out Color color, out float strength)
    {
        apex = transform.position;
        axis = transform.forward;
        range = 0f;
        cosOuter = -2f;
        cosInner = -1f;
        color = Color.black;
        strength = 0f;

        Light l = lightComponent;
        if (l == null || !l.isActiveAndEnabled || l.intensity <= 0.0001f) return false;

        if (_referenceIntensity <= 0f) _referenceIntensity = l.intensity;
        strength = intensity * Mathf.Clamp01(l.intensity / _referenceIntensity);
        range = l.range * rangeScale;
        if (strength <= 0.0001f || range <= 0.001f) return false;

        apex = l.transform.position;
        axis = l.transform.forward;
        color = l.color;

        if (l.type == LightType.Spot)
        {
            // At least a degree between inner and outer: the shader softens the edge with a
            // smoothstep between the two cosines, which divides by zero when they are equal.
            float outer = Mathf.Clamp(l.spotAngle, 2f, 179f);
            float inner = Mathf.Clamp(l.innerSpotAngle, 0f, outer - 1f);
            cosOuter = Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad);
            cosInner = Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad);
        }
        // Any other type reads as a point light: cosOuter stays at -2, the shader's sphere sentinel.

        return true;
    }
}
