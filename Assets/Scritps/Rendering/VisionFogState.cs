using UnityEngine;

/// <summary>
/// One complete set of vision fog values, and the single place that writes them to the shader.
///
/// Why this exists rather than a long parameter list: there are three callers that all have to
/// produce the same shader state — <see cref="VisionRangeController"/> at runtime, the Timeline
/// mixer scrubbing in the editor, and the preview button on the config's inspector. When each of
/// them pushed its own <c>Shader.SetGlobalXxx</c> calls, adding a parameter meant editing three
/// call sites, and the colour-space bug below only had to be missed in one of them to come back.
///
/// ── THE COLOUR-SPACE RULE ──────────────────────────────────────────────────
/// <c>Shader.SetGlobalColor</c> does NOT convert sRGB to linear. Unity's docs say it outright:
/// "unlike Material.SetColor, this function doesn't do color space conversion. It is just an
/// alias to SetGlobalVector." This project renders in Linear, so a colour pushed raw arrives in
/// the shader as if the picker's sRGB numbers were already linear — a (0.1, 0.1, 0.1) grey lands
/// about 3.5x brighter than authored, which is why nudging the fog colour off pure black used to
/// jump straight to visible grey.
///
/// <see cref="PushToShader"/> is therefore the only method allowed to touch these globals, and it
/// converts with <c>.linear</c> on the way out. Do not add a SetGlobalColor call elsewhere.
/// </summary>
[System.Serializable]
public struct VisionFogState
{
    // ── Range and density ───────────────────────────────────────────────────
    public float visionStart;
    public float visionEnd;
    public float density;
    public float falloffPower;

    // ── Extinction ──────────────────────────────────────────────────────────
    public float darkness;
    public Color extinctionTint;

    // ── In-scattering ───────────────────────────────────────────────────────
    public Color fogColor;
    public float fogColorIntensity;
    public float inscatterStrength;

    // ── Light preservation ──────────────────────────────────────────────────
    public float lightPreservation;
    public float lightThreshold;
    public float lightKnee;
    public float maxLightPreservation;
    public float lightDistanceFalloff;

    // ── Blur ────────────────────────────────────────────────────────────────
    public float blurStrength;

    // ── Player module lights ────────────────────────────────────────────────
    public float playerLightRange;
    public float playerLightClear;
    public float playerLightFalloff;
    public Color playerLightColor;
    public float playerLightTint;
    public float playerLightInjection;

    // ── World bypass zones ──────────────────────────────────────────────────
    public float bypassFalloff;
    public Color bypassDefaultColor;
    public float bypassDefaultIntensity;
    public float bypassDefaultClear;
    public float bypassPlayerFade;

    // ── Shader property IDs ─────────────────────────────────────────────────
    public static class Ids
    {
        public static readonly int PlayerPos            = Shader.PropertyToID("_PlayerPos");
        public static readonly int VisionStart          = Shader.PropertyToID("_VisionStart");
        public static readonly int VisionEnd            = Shader.PropertyToID("_VisionEnd");

        public static readonly int FogDensity           = Shader.PropertyToID("_FogDensity");
        public static readonly int FogFalloffPower      = Shader.PropertyToID("_FogFalloffPower");
        public static readonly int FogDarkness          = Shader.PropertyToID("_FogDarkness");
        public static readonly int FogExtinctionTint    = Shader.PropertyToID("_FogExtinctionTint");

        public static readonly int FogColor             = Shader.PropertyToID("_FogColor");
        public static readonly int FogInscatterStrength = Shader.PropertyToID("_FogInscatterStrength");

        public static readonly int LightPreservation    = Shader.PropertyToID("_LightPreservation");
        public static readonly int LightThreshold       = Shader.PropertyToID("_LightThreshold");
        public static readonly int LightKnee            = Shader.PropertyToID("_LightKnee");
        public static readonly int MaxLightPreservation = Shader.PropertyToID("_MaxLightPreservation");
        public static readonly int LightDistanceFalloff = Shader.PropertyToID("_LightDistanceFalloff");

        public static readonly int BlurStrength         = Shader.PropertyToID("_VisionFogBlurStrength");

        public static readonly int PlayerLightPosition  = Shader.PropertyToID("_PlayerLightPosition");
        public static readonly int PlayerLightRange     = Shader.PropertyToID("_PlayerLightRange");
        public static readonly int PlayerLightClear     = Shader.PropertyToID("_PlayerLightClear");
        public static readonly int PlayerLightFalloff   = Shader.PropertyToID("_PlayerLightFalloff");
        public static readonly int PlayerLightColor     = Shader.PropertyToID("_PlayerLightColor");
        public static readonly int PlayerLightTint      = Shader.PropertyToID("_PlayerLightTint");
        public static readonly int PlayerLightInjection = Shader.PropertyToID("_PlayerLightInjection");

        public static readonly int BypassFalloff        = Shader.PropertyToID("_FogBypassFalloff");
        public static readonly int BypassPlayerFade     = Shader.PropertyToID("_FogBypassPlayerFade");
        public static readonly int BypassData           = Shader.PropertyToID("_FogLightBypassData");
        public static readonly int BypassColor          = Shader.PropertyToID("_FogLightBypassColor");
        public static readonly int BypassCount          = Shader.PropertyToID("_FogLightBypassCount");
    }

    // ── Construction ────────────────────────────────────────────────────────

    public static VisionFogState FromConfig(SO_VisionFogConfig c)
    {
        return new VisionFogState
        {
            visionStart            = c.visionStart,
            visionEnd              = c.visionEnd,
            density                = c.fogDensity,
            falloffPower           = c.fogFalloffPower,

            darkness               = c.darkness,
            extinctionTint         = c.extinctionTint,

            fogColor               = c.fogColor,
            fogColorIntensity      = c.fogColorIntensity,
            inscatterStrength      = c.inscatterStrength,

            lightPreservation      = c.lightPreservation,
            lightThreshold         = c.lightThreshold,
            lightKnee              = c.lightKnee,
            maxLightPreservation   = c.maxLightPreservation,
            lightDistanceFalloff   = c.lightDistanceFalloff,

            blurStrength           = c.blurStrength,

            playerLightRange       = c.playerLightRange,
            playerLightClear       = c.playerLightClear,
            playerLightFalloff     = c.playerLightFalloff,
            playerLightColor       = c.playerLightColor,
            playerLightTint        = c.playerLightTint,
            playerLightInjection   = c.playerLightInjection,

            bypassFalloff          = c.bypassFalloff,
            bypassDefaultColor     = c.bypassDefaultColor,
            bypassDefaultIntensity = c.bypassDefaultIntensity,
            bypassDefaultClear     = c.bypassDefaultClear,
            bypassPlayerFade       = c.bypassPlayerFade,
        };
    }

    /// <summary>
    /// A state that makes the shader early-out (visionEnd &lt;= visionStart) and opens no light.
    /// Used while there is no player: Main Menu, LevelUI on its own, gameplay scene not loaded.
    /// </summary>
    public static VisionFogState Disabled => default;

    // ── Blending ────────────────────────────────────────────────────────────

    public static VisionFogState Lerp(in VisionFogState a, in VisionFogState b, float t)
    {
        return new VisionFogState
        {
            visionStart            = Mathf.Lerp(a.visionStart,            b.visionStart,            t),
            visionEnd              = Mathf.Lerp(a.visionEnd,              b.visionEnd,              t),
            density                = Mathf.Lerp(a.density,                b.density,                t),
            falloffPower           = Mathf.Lerp(a.falloffPower,           b.falloffPower,           t),

            darkness               = Mathf.Lerp(a.darkness,               b.darkness,               t),
            extinctionTint         = Color.Lerp(a.extinctionTint,         b.extinctionTint,         t),

            fogColor               = Color.Lerp(a.fogColor,               b.fogColor,               t),
            fogColorIntensity      = Mathf.Lerp(a.fogColorIntensity,      b.fogColorIntensity,      t),
            inscatterStrength      = Mathf.Lerp(a.inscatterStrength,      b.inscatterStrength,      t),

            lightPreservation      = Mathf.Lerp(a.lightPreservation,      b.lightPreservation,      t),
            lightThreshold         = Mathf.Lerp(a.lightThreshold,         b.lightThreshold,         t),
            lightKnee              = Mathf.Lerp(a.lightKnee,              b.lightKnee,              t),
            maxLightPreservation   = Mathf.Lerp(a.maxLightPreservation,   b.maxLightPreservation,   t),
            lightDistanceFalloff   = Mathf.Lerp(a.lightDistanceFalloff,   b.lightDistanceFalloff,   t),

            blurStrength           = Mathf.Lerp(a.blurStrength,           b.blurStrength,           t),

            playerLightRange       = Mathf.Lerp(a.playerLightRange,       b.playerLightRange,       t),
            playerLightClear       = Mathf.Lerp(a.playerLightClear,       b.playerLightClear,       t),
            playerLightFalloff     = Mathf.Lerp(a.playerLightFalloff,     b.playerLightFalloff,     t),
            playerLightColor       = Color.Lerp(a.playerLightColor,       b.playerLightColor,       t),
            playerLightTint        = Mathf.Lerp(a.playerLightTint,        b.playerLightTint,        t),
            playerLightInjection   = Mathf.Lerp(a.playerLightInjection,   b.playerLightInjection,   t),

            bypassFalloff          = Mathf.Lerp(a.bypassFalloff,          b.bypassFalloff,          t),
            bypassDefaultColor     = Color.Lerp(a.bypassDefaultColor,     b.bypassDefaultColor,     t),
            bypassDefaultIntensity = Mathf.Lerp(a.bypassDefaultIntensity, b.bypassDefaultIntensity, t),
            bypassDefaultClear     = Mathf.Lerp(a.bypassDefaultClear,     b.bypassDefaultClear,     t),
            bypassPlayerFade       = Mathf.Lerp(a.bypassPlayerFade,       b.bypassPlayerFade,       t),
        };
    }

    /// <summary>
    /// Adds <paramref name="other"/> scaled by <paramref name="weight"/>. For Timeline, which
    /// blends N overlapping clips by weight rather than pairwise — accumulate, then
    /// <see cref="Normalise"/> by the total weight.
    /// </summary>
    public void AddWeighted(in VisionFogState other, float weight)
    {
        visionStart            += other.visionStart            * weight;
        visionEnd              += other.visionEnd              * weight;
        density                += other.density                * weight;
        falloffPower           += other.falloffPower           * weight;

        darkness               += other.darkness               * weight;
        extinctionTint         += other.extinctionTint         * weight;

        fogColor               += other.fogColor               * weight;
        fogColorIntensity      += other.fogColorIntensity      * weight;
        inscatterStrength      += other.inscatterStrength      * weight;

        lightPreservation      += other.lightPreservation      * weight;
        lightThreshold         += other.lightThreshold         * weight;
        lightKnee              += other.lightKnee              * weight;
        maxLightPreservation   += other.maxLightPreservation   * weight;
        lightDistanceFalloff   += other.lightDistanceFalloff   * weight;

        blurStrength           += other.blurStrength           * weight;

        playerLightRange       += other.playerLightRange       * weight;
        playerLightClear       += other.playerLightClear       * weight;
        playerLightFalloff     += other.playerLightFalloff     * weight;
        playerLightColor       += other.playerLightColor       * weight;
        playerLightTint        += other.playerLightTint        * weight;
        playerLightInjection   += other.playerLightInjection   * weight;

        bypassFalloff          += other.bypassFalloff          * weight;
        bypassDefaultColor     += other.bypassDefaultColor     * weight;
        bypassDefaultIntensity += other.bypassDefaultIntensity * weight;
        bypassDefaultClear     += other.bypassDefaultClear     * weight;
        bypassPlayerFade       += other.bypassPlayerFade       * weight;
    }

    /// <summary>Divides an accumulated state by its total weight. No-op below 1e-4.</summary>
    public void Normalise(float totalWeight)
    {
        if (totalWeight <= 0.0001f) return;
        float k = 1f / totalWeight;

        visionStart *= k; visionEnd *= k; density *= k; falloffPower *= k;
        darkness *= k; extinctionTint *= k;
        fogColor *= k; fogColorIntensity *= k; inscatterStrength *= k;
        lightPreservation *= k; lightThreshold *= k; lightKnee *= k;
        maxLightPreservation *= k; lightDistanceFalloff *= k;
        blurStrength *= k;
        playerLightRange *= k; playerLightClear *= k; playerLightFalloff *= k;
        playerLightColor *= k; playerLightTint *= k; playerLightInjection *= k;
        bypassFalloff *= k; bypassDefaultColor *= k;
        bypassDefaultIntensity *= k; bypassDefaultClear *= k;
        bypassPlayerFade *= k;
    }

    // ── Output ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes every fog global except the bypass-zone arrays, which only the controller can
    /// build because only it knows which zones are registered.
    /// </summary>
    /// <param name="playerPosition">Centre the fog closes in around.</param>
    /// <param name="playerLightPosition">Where the module lights actually sit — usually a child
    /// of the player, not the player's own pivot.</param>
    /// <remarks>
    /// <c>readonly</c> so callers that hold the state in an <c>in</c> parameter (the Timeline
    /// mixer, the inspector preview) do not get a defensive copy of the whole struct on every
    /// call.
    /// </remarks>
    public readonly void PushToShader(Vector3 playerPosition, Vector3 playerLightPosition)
    {
        Shader.SetGlobalVector(Ids.PlayerPos, playerPosition);
        Shader.SetGlobalFloat(Ids.VisionStart, visionStart);
        Shader.SetGlobalFloat(Ids.VisionEnd, visionEnd);

        Shader.SetGlobalFloat(Ids.FogDensity, density);
        Shader.SetGlobalFloat(Ids.FogFalloffPower, falloffPower);
        Shader.SetGlobalFloat(Ids.FogDarkness, darkness);

        // extinctionTint is per-channel WEIGHTS, not a colour being displayed, so it goes out
        // raw. Running it through .linear would silently reweight a tint the designer set by eye.
        Shader.SetGlobalVector(Ids.FogExtinctionTint,
            new Vector4(extinctionTint.r, extinctionTint.g, extinctionTint.b, 0f));

        Shader.SetGlobalVector(Ids.FogColor, ToLinear(fogColor) * fogColorIntensity);
        Shader.SetGlobalFloat(Ids.FogInscatterStrength, inscatterStrength);

        Shader.SetGlobalFloat(Ids.LightPreservation, lightPreservation);
        Shader.SetGlobalFloat(Ids.LightThreshold, lightThreshold);
        Shader.SetGlobalFloat(Ids.LightKnee, lightKnee);
        Shader.SetGlobalFloat(Ids.MaxLightPreservation, maxLightPreservation);
        Shader.SetGlobalFloat(Ids.LightDistanceFalloff, lightDistanceFalloff);

        Shader.SetGlobalFloat(Ids.BlurStrength, blurStrength);

        Shader.SetGlobalVector(Ids.PlayerLightPosition, playerLightPosition);
        Shader.SetGlobalFloat(Ids.PlayerLightRange, playerLightRange);
        Shader.SetGlobalFloat(Ids.PlayerLightClear, playerLightClear);
        Shader.SetGlobalFloat(Ids.PlayerLightFalloff, playerLightFalloff);
        Shader.SetGlobalVector(Ids.PlayerLightColor, ToLinear(playerLightColor));
        Shader.SetGlobalFloat(Ids.PlayerLightTint, playerLightTint);
        Shader.SetGlobalFloat(Ids.PlayerLightInjection, playerLightInjection);

        Shader.SetGlobalFloat(Ids.BypassFalloff, bypassFalloff);
        Shader.SetGlobalFloat(Ids.BypassPlayerFade, bypassPlayerFade);
    }

    /// <summary>
    /// sRGB → linear, as a Vector4 so it can go out through SetGlobalVector and never be mistaken
    /// for a value SetGlobalColor already handled. Alpha is dropped: nothing here uses it.
    /// </summary>
    public static Vector4 ToLinear(Color c)
    {
        Color l = c.linear;
        return new Vector4(l.r, l.g, l.b, 1f);
    }

    // ── Evaluation (CPU mirror of the shader) ───────────────────────────────
    //
    // ⚠ These three methods reimplement the maths in VisionFog_HLSL.shader's Frag() so the
    // inspector can draw a preview without entering Play Mode. They are a MIRROR, not the source
    // of truth: if the shader's model changes, change these too or the preview starts lying to the
    // designer, which is worse than having no preview. The shader carries the same warning.
    //
    // Simplifications, all of them safe for a preview of "what do I see looking straight ahead":
    //   - No noise. It modulates density around 1.0, so the average is what is drawn anyway.
    //   - No blur, which changes sharpness rather than colour.
    //   - No bypass zones: those are placed in the world, not in the preset.
    //   - The player-light mask uses 3D distance in the shader and the fog ramp uses horizontal
    //     distance. For a point on the ground straight ahead they are the same number.

    /// <summary>Optical depth accumulated at <paramref name="distance"/> metres.</summary>
    public readonly float OpticalDepthAt(float distance, float clearAmount = 0f)
    {
        float band = Mathf.Max(visionEnd - visionStart, 1e-4f);
        float t = Mathf.Clamp01((distance - visionStart) / band);
        t = Mathf.Pow(t, Mathf.Max(falloffPower, 0.01f));
        return t * Mathf.Max(density, 0f) * (1f - Mathf.Clamp01(clearAmount));
    }

    /// <summary>
    /// Fraction of the scene's light that survives at <paramref name="distance"/> metres, as
    /// perceived luminance. This is the number a designer actually wants: "at 8 m I still see 30%".
    /// </summary>
    public readonly float VisibilityAt(float distance, float clearAmount = 0f)
    {
        float od = OpticalDepthAt(distance, clearAmount);
        float dark = Mathf.Clamp01(darkness);
        float r = Mathf.Exp(-od * Mathf.Max(extinctionTint.r, 0f) * dark);
        float g = Mathf.Exp(-od * Mathf.Max(extinctionTint.g, 0f) * dark);
        float b = Mathf.Exp(-od * Mathf.Max(extinctionTint.b, 0f) * dark);
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    /// <summary>
    /// The colour a reference surface ends up as at <paramref name="distance"/> metres — extinction
    /// plus in-scattering, the same two halves the shader applies.
    /// </summary>
    /// <param name="surface">Reference surface colour, in LINEAR space.</param>
    /// <param name="throughPlayerLight">Include the module light's clearing, tint and injection,
    /// i.e. "what the player sees inside their own light" rather than out in the dark.</param>
    public readonly Color EvaluateSurface(float distance, Color surface, bool throughPlayerLight)
    {
        float clear = 0f;
        Color lit = surface;

        if (throughPlayerLight && playerLightRange > 0.001f)
        {
            float mask = Mathf.Clamp01(1f - distance / playerLightRange);
            mask = Mathf.Pow(mask, Mathf.Max(playerLightFalloff, 0.01f));

            Color lightLinear = playerLightColor.linear;
            clear = Mathf.Clamp01(mask * Mathf.Clamp01(playerLightClear));

            // Injection first, then the multiply tint — same order as the shader.
            lit += lightLinear * (mask * playerLightInjection);
            float tint = Mathf.Clamp01(mask * Mathf.Clamp01(playerLightTint));
            lit = Color.Lerp(lit, lit * lightLinear, tint);
        }

        float od = OpticalDepthAt(distance, clear);
        float dark = Mathf.Clamp01(darkness);
        Color fog = fogColor.linear * Mathf.Max(fogColorIntensity, 0f);
        float insc = (1f - Mathf.Exp(-od)) * Mathf.Clamp01(inscatterStrength);

        return new Color(
            lit.r * Mathf.Exp(-od * Mathf.Max(extinctionTint.r, 0f) * dark) + fog.r * insc,
            lit.g * Mathf.Exp(-od * Mathf.Max(extinctionTint.g, 0f) * dark) + fog.g * insc,
            lit.b * Mathf.Exp(-od * Mathf.Max(extinctionTint.b, 0f) * dark) + fog.b * insc,
            1f);
    }
}
