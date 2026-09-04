using UnityEngine;
using UnityEditor;

/// <summary>
/// Draws the "what does this preset actually look like" block on the
/// <see cref="SO_VisionFogConfig"/> inspector.
///
/// Why it exists: the preset is fifteen numbers, and the only way to know what they add up to used
/// to be pressing Play, walking somewhere dark, and squinting. A designer tuning fog needs the
/// answer to two questions in front of them while they drag a slider — "how far can I see?" and
/// "what colour is the dark?" — and neither is readable off the numbers.
///
/// Three views, in the order a designer asks for them:
///   1. Two colour ramps: a reference wall at increasing distance, out in the dark and inside the
///      module light. This is the "what colour is the dark" answer, and it is the one that catches
///      a fog colour that has drifted to grey.
///   2. A visibility curve with the same two lines, so the SHAPE of the falloff is visible — which
///      is what fogFalloffPower and fogDensity actually control.
///   3. Plain-language readouts: the distance where half the scene is gone, and where 90% is.
///
/// It is a preview, not the shader: the maths lives in VisionFogState and mirrors
/// VisionFog_HLSL.shader. See the warning there before changing either.
/// </summary>
public static class VisionFogPreviewDrawer
{
    // A mid-grey wall. Bright enough that the darkening is obvious, neutral enough that any colour
    // showing up in the ramp came from the preset and not from the reference.
    private static readonly Color ReferenceSurface = new Color(0.6f, 0.6f, 0.6f);

    private const int RampResolution = 256;
    private const float RampHeight = 26f;
    private const float CurveHeight = 90f;

    // How far past visionEnd the preview goes. Beyond the band the curve is flat, but seeing that
    // flat tail is the point: it shows whether visionEnd really means "gone" or just "dim".
    private const float RangeOvershoot = 1.25f;

    private static Texture2D s_darkRamp;
    private static Texture2D s_litRamp;
    private static int s_cachedHash;

    // Cached rather than built inline: OnInspectorGUI repaints on every mouse move, and a GUIStyle
    // per repaint is garbage the editor does not need to collect.
    private static GUIStyle s_tickStyle;
    private static GUIStyle s_tickRightStyle;
    private static GUIStyle s_captionStyle;

    /// <summary>Draws the whole preview block. Safe to call every OnInspectorGUI.</summary>
    public static void Draw(SO_VisionFogConfig config)
    {
        if (config == null) return;

        VisionFogState state = VisionFogState.FromConfig(config);
        float maxDistance = Mathf.Max(config.visionEnd * RangeOvershoot, config.visionStart + 1f);

        EnsureStyles();
        RebuildRampsIfNeeded(config, state, maxDistance);

        EditorGUILayout.LabelField("Previsualización", EditorStyles.boldLabel);

        // The one case where the preview would be actively misleading rather than just empty.
        if (config.visionEnd <= config.visionStart + 0.001f)
        {
            EditorGUILayout.HelpBox(
                "visionEnd <= visionStart: el shader hace early-out, así que no hay nada que " +
                "previsualizar. Subí visionEnd.",
                MessageType.Warning);
            return;
        }

        DrawRamp(s_darkRamp, "En la oscuridad", config, maxDistance);
        DrawRamp(s_litRamp, "Dentro de la luz del módulo", config, maxDistance);

        EditorGUILayout.Space(6);
        DrawCurve(state, config, maxDistance);

        EditorGUILayout.Space(6);
        DrawReadouts(state, config);
    }

    // ── Colour ramps ────────────────────────────────────────────────────────

    /// <summary>
    /// Regenerates both ramp textures, but only when something that affects them changed —
    /// OnInspectorGUI runs on every mouse move, and rebuilding 512 pixels each time makes dragging
    /// a slider feel sticky.
    /// </summary>
    private static void RebuildRampsIfNeeded(SO_VisionFogConfig config, in VisionFogState state,
                                             float maxDistance)
    {
        int hash = ComputeHash(config);
        if (hash == s_cachedHash && s_darkRamp != null && s_litRamp != null) return;

        s_cachedHash = hash;
        s_darkRamp = FillRamp(s_darkRamp, state, maxDistance, throughPlayerLight: false);
        s_litRamp  = FillRamp(s_litRamp,  state, maxDistance, throughPlayerLight: true);
    }

    private static Texture2D FillRamp(Texture2D texture, in VisionFogState state,
                                      float maxDistance, bool throughPlayerLight)
    {
        if (texture == null)
        {
            texture = new Texture2D(RampResolution, 1, TextureFormat.RGBA32, false)
            {
                // hideFlags so the texture is not saved into a scene or leaked as an asset, and
                // clamp so the edge pixel does not wrap when the rect is stretched.
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        Color surface = ReferenceSurface.linear;
        var pixels = new Color[RampResolution];

        for (int i = 0; i < RampResolution; i++)
        {
            float distance = maxDistance * i / (RampResolution - 1f);
            Color linear = state.EvaluateSurface(distance, surface, throughPlayerLight);

            // .gamma on the way out: the maths runs in linear, the texture is sampled as sRGB.
            Color shown = linear.gamma;
            pixels[i] = new Color(Mathf.Clamp01(shown.r), Mathf.Clamp01(shown.g),
                                  Mathf.Clamp01(shown.b), 1f);
        }

        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    private static void DrawRamp(Texture2D ramp, string caption, SO_VisionFogConfig config,
                                 float maxDistance)
    {
        if (ramp == null) return;

        EditorGUILayout.LabelField(caption, s_captionStyle);

        Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                             GUILayout.Height(RampHeight),
                                             GUILayout.ExpandWidth(true));
        GUI.DrawTexture(rect, ramp, ScaleMode.StretchToFill);
        DrawBandMarkers(rect, config, maxDistance, Color.white);

        DrawDistanceAxis(rect, maxDistance);
    }

    /// <summary>Vertical ticks at visionStart and visionEnd, the two numbers the ramp is about.</summary>
    private static void DrawBandMarkers(Rect rect, SO_VisionFogConfig config, float maxDistance,
                                        Color tint)
    {
        DrawVerticalTick(rect, config.visionStart / maxDistance, new Color(tint.r, tint.g, tint.b, 0.55f));
        DrawVerticalTick(rect, config.visionEnd / maxDistance, new Color(tint.r, tint.g, tint.b, 0.9f));
    }

    private static void DrawVerticalTick(Rect rect, float normalised, Color color)
    {
        if (normalised < 0f || normalised > 1f) return;
        float x = rect.x + rect.width * normalised;
        EditorGUI.DrawRect(new Rect(x - 0.5f, rect.y, 1f, rect.height), color);
    }

    private static void DrawDistanceAxis(Rect rampRect, float maxDistance)
    {
        Rect axis = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(12f));
        axis.x = rampRect.x;
        axis.width = rampRect.width;

        GUI.Label(new Rect(axis.x, axis.y, 40f, 12f), "0 m", s_tickStyle);
        GUI.Label(new Rect(axis.xMax - 60f, axis.y, 60f, 12f), $"{maxDistance:0.#} m",
                  s_tickRightStyle);
    }

    // ── Visibility curve ────────────────────────────────────────────────────

    private static void DrawCurve(in VisionFogState state, SO_VisionFogConfig config,
                                  float maxDistance)
    {
        EditorGUILayout.LabelField("Visibilidad según distancia", s_captionStyle);

        Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                             GUILayout.Height(CurveHeight),
                                             GUILayout.ExpandWidth(true));

        // GetRect above runs on every event so the layout stays consistent, but the drawing below
        // only happens on Repaint: Handles.DrawAAPolyLine does not guard itself the way
        // EditorGUI.DrawRect does, and calling it during Layout leaks into the scene view.
        if (Event.current.type != EventType.Repaint) return;

        EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.16f));

        // Horizontal grid at 25 / 50 / 75%: reading a percentage off a bare curve is guesswork.
        for (int i = 1; i <= 3; i++)
        {
            float y = rect.y + rect.height * (i / 4f);
            EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), new Color(1f, 1f, 1f, 0.07f));
        }
        DrawBandMarkers(rect, config, maxDistance, new Color(1f, 1f, 1f, 0.35f));

        PlotCurve(rect, state, maxDistance, throughPlayerLight: false,
                  new Color(0.55f, 0.62f, 0.75f));
        PlotCurve(rect, state, maxDistance, throughPlayerLight: true,
                  new Color(1f, 0.78f, 0.35f));

        // Legend inside the plot: an inspector is narrow and a separate legend row wastes a line.
        GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 200f, 14f),
                  "— oscuridad     — con luz del módulo", s_tickStyle);
    }

    private static void PlotCurve(Rect rect, in VisionFogState state, float maxDistance,
                                  bool throughPlayerLight, Color color)
    {
        const int samples = 96;
        var points = new Vector3[samples];

        for (int i = 0; i < samples; i++)
        {
            float n = i / (samples - 1f);
            float distance = maxDistance * n;

            float clear = 0f;
            if (throughPlayerLight && state.playerLightRange > 0.001f)
            {
                float mask = Mathf.Clamp01(1f - distance / state.playerLightRange);
                mask = Mathf.Pow(mask, Mathf.Max(state.playerLightFalloff, 0.01f));
                clear = Mathf.Clamp01(mask * Mathf.Clamp01(state.playerLightClear));
            }

            float visibility = state.VisibilityAt(distance, clear);
            points[i] = new Vector3(rect.x + rect.width * n,
                                    rect.y + rect.height * (1f - visibility),
                                    0f);
        }

        Handles.color = color;
        Handles.DrawAAPolyLine(2f, points);
    }

    // ── Readouts ────────────────────────────────────────────────────────────

    private static void DrawReadouts(in VisionFogState state, SO_VisionFogConfig config)
    {
        float half = FindDistanceForVisibility(state, 0.5f, config);
        float tenth = FindDistanceForVisibility(state, 0.1f, config);
        float atEnd = state.VisibilityAt(config.visionEnd) * 100f;

        // Phrased as distances rather than as coefficients: "I stop seeing at 7 m" is a level
        // design decision, "optical depth 4.6" is not.
        EditorGUILayout.HelpBox(
            $"Ves la mitad de la escena a {Format(half)} y sólo el 10% a {Format(tenth)}.\n" +
            $"En visionEnd ({config.visionEnd:0.#} m) queda el {atEnd:0.#}% de la escena.",
            MessageType.None);
    }

    private static string Format(float distance) =>
        float.IsNaN(distance) ? "nunca (en todo el rango)" : $"{distance:0.#} m";

    /// <summary>
    /// Distance at which visibility drops to <paramref name="target"/>. Scanned rather than solved:
    /// fogFalloffPower makes the curve non-invertible in closed form, and 200 samples over the
    /// preview range is exact enough to display to one decimal.
    /// </summary>
    private static float FindDistanceForVisibility(in VisionFogState state, float target,
                                                   SO_VisionFogConfig config)
    {
        const int steps = 200;
        float max = Mathf.Max(config.visionEnd * RangeOvershoot, config.visionStart + 1f);

        for (int i = 0; i <= steps; i++)
        {
            float distance = max * i / (float)steps;
            if (state.VisibilityAt(distance) <= target) return distance;
        }
        return float.NaN;
    }

    // ── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>
    /// Everything the ramps are drawn from. Deliberately not GetHashCode() on the whole object:
    /// transitionDuration and silhouetteMode do not change a single pixel, and including them
    /// would rebuild the textures for nothing.
    /// </summary>
    private static int ComputeHash(SO_VisionFogConfig c)
    {
        var hash = new System.HashCode();
        hash.Add(c.visionStart); hash.Add(c.visionEnd);
        hash.Add(c.fogDensity); hash.Add(c.fogFalloffPower);
        hash.Add(c.darkness); hash.Add(c.extinctionTint);
        hash.Add(c.fogColor); hash.Add(c.fogColorIntensity); hash.Add(c.inscatterStrength);
        hash.Add(c.playerLightRange); hash.Add(c.playerLightClear); hash.Add(c.playerLightFalloff);
        hash.Add(c.playerLightColor); hash.Add(c.playerLightTint); hash.Add(c.playerLightInjection);
        return hash.ToHashCode();
    }

    private static void EnsureStyles()
    {
        s_tickStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.65f, 0.65f, 0.68f) },
        };

        s_tickRightStyle ??= new GUIStyle(s_tickStyle) { alignment = TextAnchor.MiddleRight };

        s_captionStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.72f, 0.72f, 0.76f) },
        };
    }
}
