// SecurityCamera_HLSL.shader
// Fullscreen post-process HLSL para URP: el plano se ve como el feed de una cámara de seguridad.
// Lo dibuja SecurityFeedRendererFeature (PC_Renderer) SOLO mientras está al aire una cámara con
// SecurityCameraFeed (Cam_Slam, Cam_4_Gate); el resto del juego el pase ni se encola.
//
// Va ANTES del PSXEffect (mismo injection point, antes en la lista): el feed entero, texto incluido,
// pasa después por el pixelado/dither/scanlines del juego como el resto de la imagen.
//
// Qué hace, en orden:
//   1) Lente gran angular (barril). Las esquinas quedan en las esquinas: el centro se acerca un poco.
//   2) Señal analógica: renglones corridos, rachas de glitch y un tirón fuerte al entrar al plano.
//   3) Arrastre (ghosting) horizontal.
//   4) Casi blanco y negro, contraste alto, negros levantados.
//   5) Grano a pocos FPS, banda clara que rueda, viñeta.
//   6) Teñido azul de monitor (#8AB4D4: el azul frío del spec es SOLO de monitores, y esto es uno).
//   7) Overlay "quemado" por el grabador (no pasa por la lente): etiqueta de cámara, REC que titila,
//      fecha y hora, esquineros de visor.
//
// El texto lo publica SecurityCameraFeed como globales (_SecurityFeedText, _SecurityFeedInfo).
// La tipografía es un bitmap de 5x7 embebido abajo (kGlyphs); el orden de los glifos tiene que
// coincidir con SecurityCameraFeed.GlyphOf.
//
// OJO con _OverlayGridRows: tiene que ser igual al _PixelSize de PS1Effect.mat (256). Así cada pixel
// de letra cae en exactamente un bloque del PSX y el texto sale entero; con otro valor las letras
// salen mordidas.
//
// Rojo: el REC NO es rojo a propósito (rojo #CC1A1A = solo peligro). Si se quiere rojo, _RecColor.

Shader "Hidden/Custom/SecurityCameraFeed"
{
    Properties
    {
        [Header(Lens)]
        _LensDistortion     ("Lens Distortion (barril)", Range(0, 0.5)) = 0.15
        _Vignette           ("Vignette", Range(0, 1)) = 0.5

        [Header(Image)]
        _Saturation         ("Saturation (0 = blanco y negro)", Range(0, 1)) = 0.05
        _TintColor          ("Tint (azul de monitor)", Color) = (0.5411765, 0.7058824, 0.8313726, 1)
        _TintStrength       ("Tint Strength", Range(0, 1)) = 0.35
        _Contrast           ("Contrast", Range(0.5, 2.5)) = 1.35
        _Brightness         ("Brightness", Range(0.25, 3)) = 1.2
        _BlackLevel         ("Black Level (negros levantados)", Range(0, 0.25)) = 0.04
        _Smear              ("Analog Smear (arrastre)", Range(0, 1)) = 0.35

        [Header(Signal)]
        _Noise              ("Noise (grano)", Range(0, 0.5)) = 0.09
        _NoiseFps           ("Noise FPS", Range(1, 60)) = 12
        _LineJitter         ("Line Jitter (UV)", Range(0, 0.01)) = 0.0008
        _GlitchChance       ("Glitch Chance (por cuarto de segundo)", Range(0, 1)) = 0.1
        _GlitchShift        ("Glitch Shift (UV)", Range(0, 0.05)) = 0.01
        _RollStrength       ("Rolling Band", Range(0, 0.5)) = 0.06
        _RollSpeed          ("Rolling Band Speed", Range(-1, 1)) = 0.1
        _RollWidth          ("Rolling Band Width", Range(0.01, 0.5)) = 0.07
        _CutNoise           ("Cut Noise (tirón al entrar al plano)", Range(0, 1)) = 0.6
        _CutNoiseSeconds    ("Cut Noise Seconds", Range(0, 1)) = 0.3

        [Header(Overlay)]
        [ToggleUI] _EnableOverlay   ("Enable Overlay (texto, REC, esquineros)", Float) = 1
        _OverlayColor       ("Overlay Color", Color) = (0.9, 0.93, 0.95, 1)
        _RecColor           ("REC Dot Color", Color) = (0.9, 0.93, 0.95, 1)
        _OverlayGridRows    ("Overlay Grid Rows (= _PixelSize de PS1Effect)", Float) = 256
        _OverlayMargin      ("Overlay Margin (celdas)", Range(0, 40)) = 12
        _OverlayShadow      ("Overlay Shadow", Range(0, 1)) = 0.7
        [ToggleUI] _EnableBrackets  ("Enable Corner Brackets", Float) = 1
        _BracketLength      ("Bracket Length (celdas)", Range(2, 60)) = 16
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float  _LensDistortion;
        float  _Vignette;

        float  _Saturation;
        float4 _TintColor;
        float  _TintStrength;
        float  _Contrast;
        float  _Brightness;
        float  _BlackLevel;
        float  _Smear;

        float  _Noise;
        float  _NoiseFps;
        float  _LineJitter;
        float  _GlitchChance;
        float  _GlitchShift;
        float  _RollStrength;
        float  _RollSpeed;
        float  _RollWidth;
        float  _CutNoise;
        float  _CutNoiseSeconds;

        float  _EnableOverlay;
        float4 _OverlayColor;
        float4 _RecColor;
        float  _OverlayGridRows;
        float  _OverlayMargin;
        float  _OverlayShadow;
        float  _EnableBrackets;
        float  _BracketLength;
    CBUFFER_END

    // Globales de SecurityCameraFeed. Fuera de Properties a propósito: una property del material
    // las taparía.
    //   _SecurityFeedText  índices de glifo: [0..31] etiqueta (arriba a la izquierda),
    //                      [32..63] fecha y hora (abajo a la izquierda).
    //   _SecurityFeedInfo  x = largo de la etiqueta, y = largo de la fecha/hora,
    //                      z = segundos desde que el plano salió al aire (para el tirón del corte).
    float  _SecurityFeedText[64];
    float4 _SecurityFeedInfo;

    // Bitmap 5x7. x = filas 0..3 (5 bits cada una, bit 4 = columna izquierda), y = filas 4..6.
    // Orden: espacio, 0-9, A-Z, : - / . # >  (ver SecurityCameraFeed.GlyphOf).
    #define GLYPH_COUNT 43
    static const uint2 kGlyphs[GLYPH_COUNT] =
    {
        uint2(0x00000, 0x0000), //  0 ' '
        uint2(0x74675, 0x662E), //  1 '0'
        uint2(0x23084, 0x108E), //  2 '1'
        uint2(0x74422, 0x111F), //  3 '2'
        uint2(0xF8882, 0x062E), //  4 '3'
        uint2(0x11952, 0x7C42), //  5 '4'
        uint2(0xFC3C1, 0x062E), //  6 '5'
        uint2(0x3221E, 0x462E), //  7 '6'
        uint2(0xF8444, 0x2108), //  8 '7'
        uint2(0x7462E, 0x462E), //  9 '8'
        uint2(0x7462F, 0x044C), // 10 '9'
        uint2(0x7463F, 0x4631), // 11 'A'
        uint2(0xF463E, 0x463E), // 12 'B'
        uint2(0x74610, 0x422E), // 13 'C'
        uint2(0xE4A31, 0x465C), // 14 'D'
        uint2(0xFC21E, 0x421F), // 15 'E'
        uint2(0xFC21E, 0x4210), // 16 'F'
        uint2(0x74617, 0x462F), // 17 'G'
        uint2(0x8C63F, 0x4631), // 18 'H'
        uint2(0x71084, 0x108E), // 19 'I'
        uint2(0x38842, 0x0A4C), // 20 'J'
        uint2(0x8CA98, 0x5251), // 21 'K'
        uint2(0x84210, 0x421F), // 22 'L'
        uint2(0x8EEB5, 0x4631), // 23 'M'
        uint2(0x8C735, 0x4E31), // 24 'N'
        uint2(0x74631, 0x462E), // 25 'O'
        uint2(0xF463E, 0x4210), // 26 'P'
        uint2(0x74631, 0x564D), // 27 'Q'
        uint2(0xF463E, 0x5251), // 28 'R'
        uint2(0x7C20E, 0x043E), // 29 'S'
        uint2(0xF9084, 0x1084), // 30 'T'
        uint2(0x8C631, 0x462E), // 31 'U'
        uint2(0x8C631, 0x4544), // 32 'V'
        uint2(0x8C635, 0x56AA), // 33 'W'
        uint2(0x8C544, 0x2A31), // 34 'X'
        uint2(0x8C544, 0x1084), // 35 'Y'
        uint2(0xF8444, 0x221F), // 36 'Z'
        uint2(0x03180, 0x3180), // 37 ':'
        uint2(0x0001F, 0x0000), // 38 '-'
        uint2(0x00444, 0x2200), // 39 '/'
        uint2(0x00000, 0x018C), // 40 '.'
        uint2(0x52BEA, 0x7D4A), // 41 '#'
        uint2(0x41041, 0x0888), // 42 '>'
    };

    #define GLYPH_R 28u
    #define GLYPH_E 15u
    #define GLYPH_C 13u

    // pixel.x = columna 0..4 (izquierda a derecha), pixel.y = fila 0..6 (arriba hacia abajo).
    float GlyphBit(uint glyph, int2 pixel)
    {
        if (glyph >= GLYPH_COUNT) return 0.0;
        uint2 g = kGlyphs[glyph];
        uint row = pixel.y < 4 ? (g.x >> (uint)((3 - pixel.y) * 5))
                               : (g.y >> (uint)((6 - pixel.y) * 5));
        return (float)((row >> (uint)(4 - pixel.x)) & 1u);
    }

    float Hash21(float2 p)
    {
        p = frac(p * float2(233.34, 851.73));
        p += dot(p, p + 23.45);
        return frac(p.x * p.y);
    }

    // Una línea de texto con su esquina inferior izquierda en la celda `origin`: 1 donde la letra
    // pinta la celda. Celdas de 6 de ancho (5 de letra + 1 de aire).
    // Divisiones en uint: con signo el compilador avisa que son lentas (y acá nunca hay negativos).
    float TextMask(int2 cell, int2 origin, int lineStart, int length)
    {
        int2 p = cell - origin;
        if (p.x < 0 || p.y < 0 || p.y > 6) return 0.0;

        uint index = (uint)p.x / 6u;
        if (index >= (uint)length) return 0.0;

        uint column = (uint)p.x - index * 6u;
        if (column > 4u) return 0.0;

        uint glyph = (uint)_SecurityFeedText[lineStart + (int)index];
        return GlyphBit(glyph, int2(column, 6 - p.y));
    }

    float RecMask(int2 cell, int2 origin)
    {
        int2 p = cell - origin;
        if (p.x < 0 || p.x > 16 || p.y < 0 || p.y > 6) return 0.0;

        uint index = (uint)p.x / 6u;
        uint column = (uint)p.x - index * 6u;
        if (column > 4u) return 0.0;

        uint glyph = index == 0u ? GLYPH_R : (index == 1u ? GLYPH_E : GLYPH_C);
        return GlyphBit(glyph, int2(column, 6 - p.y));
    }

    // Todo lo que el grabador pinta en la celda: `ink` en el color del overlay, `rec` el punto de REC.
    // `lastCell` es la última celda de la grilla (la de la esquina superior derecha).
    void OverlayAt(int2 cell, int2 lastCell, out float ink, out float rec)
    {
        int margin = (int)_OverlayMargin;
        int topRow = lastCell.y - margin - 6;

        // Etiqueta arriba a la izquierda, fecha y hora abajo a la izquierda.
        ink = TextMask(cell, int2(margin, topRow), 0, (int)_SecurityFeedInfo.x);
        ink = max(ink, TextMask(cell, int2(margin, margin), 32, (int)_SecurityFeedInfo.y));

        // REC arriba a la derecha, con el punto a su izquierda titilando a 1 Hz.
        int recX = lastCell.x - margin - 16;
        ink = max(ink, RecMask(cell, int2(recX, topRow)));

        float2 toDot = float2(cell) - float2(recX - 5, topRow + 3);
        rec = step(dot(toDot, toDot), 6.5) * step(frac(_Time.y), 0.5);

        // Esquineros de visor, a medio margen del borde.
        if (_EnableBrackets > 0.5)
        {
            int inset = max((int)(_OverlayMargin * 0.5), 1);
            int2 fromEdge = min(cell - inset, (lastCell - inset) - cell);
            int arm = (int)_BracketLength;
            if (fromEdge.x >= 0 && fromEdge.y >= 0 &&
                ((fromEdge.x == 0 && fromEdge.y < arm) || (fromEdge.y == 0 && fromEdge.x < arm)))
            {
                ink = 1.0;
            }
        }
    }

    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;

        float aspect = _ScreenParams.x / _ScreenParams.y;
        float rows = max(_OverlayGridRows, 16.0);
        float2 gridSize = float2(rows * aspect, rows);
        float frame = fmod(floor(_Time.y * _NoiseFps), 4096.0);

        // El tirón del corte: fuerte en el primer instante del plano y se apaga en _CutNoiseSeconds.
        float cut = _CutNoise * saturate(1.0 - _SecurityFeedInfo.z / max(_CutNoiseSeconds, 0.001));

        // 1) Lente gran angular. Normalizada para que las esquinas muestreen las esquinas: sin bordes
        //    negros, a cambio de acercar un poco el centro.
        float2 d = uv - 0.5;
        float2 da = float2(d.x * aspect, d.y);
        float r2 = dot(da, da);
        float maxR2 = 0.25 * (aspect * aspect + 1.0);
        float2 suv = 0.5 + d * (1.0 + _LensDistortion * r2) / (1.0 + _LensDistortion * maxR2);

        // 2) Señal: cada renglón se corre un poco; cada tanto una racha rompe una franja de renglones.
        float row = floor(suv.y * rows);
        float shift = (Hash21(float2(row, frame)) - 0.5) * _LineJitter;

        float slot = floor(_Time.y * 4.0);
        float burst = step(1.0 - _GlitchChance, Hash21(float2(slot, 17.0)));
        float inBand = 1.0 - step(0.05, abs(suv.y - Hash21(float2(slot, 29.0))));
        shift += burst * inBand * (Hash21(float2(row, slot)) - 0.5) * 2.0 * _GlitchShift;
        shift += cut * (Hash21(float2(row, frame + 7.0)) - 0.5) * 0.04;
        suv.x += shift;

        // 3) Arrastre: la imagen deja una estela hacia la derecha.
        half3 c = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, suv).rgb;
        if (_Smear > 0.001)
        {
            float2 step1 = float2(1.0 / gridSize.x, 0.0);
            half3 c1 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, suv - step1).rgb;
            half3 c2 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, suv - step1 * 2.5).rgb;
            c = lerp(c, c * 0.55 + c1 * 0.3 + c2 * 0.15, _Smear);
        }

        // 4) Casi blanco y negro. Contraste y brillo en espacio perceptual (gamma 2.2), donde el 0.5
        //    del contraste es un gris medio de verdad.
        half luma = dot(c, half3(0.2126, 0.7152, 0.0722));
        half3 p = pow(max(lerp(luma.xxx, c, _Saturation), 0.0), 1.0 / 2.2);
        p = (p - 0.5) * _Contrast + 0.5;
        p *= _Brightness;

        // 5) Grano (un valor por bloque del PSX, cambia _NoiseFps veces por segundo), la banda clara
        //    que rueda hacia abajo y la viñeta. Después los negros levantados.
        half grain = Hash21(floor(suv * gridSize) + frame * 1.618) - 0.5;
        p += grain * (_Noise + cut);

        float roll = (frac(uv.y + _Time.y * _RollSpeed) - 0.5) / max(_RollWidth, 0.001);
        p += exp(-roll * roll) * _RollStrength;

        p *= saturate(1.0 - _Vignette * pow(saturate(r2 / maxR2), 1.25));
        p = saturate(_BlackLevel + saturate(p) * (1.0 - _BlackLevel));

        // 6) De vuelta a lineal, teñido de monitor. El tinte se normaliza por su luminancia: tiñe sin
        //    oscurecer.
        half3 tint = _TintColor.rgb / max(dot(_TintColor.rgb, half3(0.2126, 0.7152, 0.0722)), 0.001);
        half3 color = pow(p, 2.2) * lerp(1.0, tint, _TintStrength);

        // 7) Overlay. Sobre la grilla de PS1Effect corrida medio bloque: el PSX muestrea cada bloque
        //    en su esquina, y así esa esquina cae en el centro de una de estas celdas.
        if (_EnableOverlay > 0.5)
        {
            int2 cell = int2(floor(uv * gridSize + 0.5));
            int2 lastCell = int2(floor(gridSize + 0.5));

            float ink, rec, inkShadow, recShadow;
            OverlayAt(cell, lastCell, ink, rec);
            OverlayAt(cell + int2(-1, 1), lastCell, inkShadow, recShadow);

            float shadow = max(inkShadow, recShadow) * _OverlayShadow * (1.0 - max(ink, rec));
            color = lerp(color, 0.0, shadow);
            color = lerp(color, _OverlayColor.rgb, ink * _OverlayColor.a);
            color = lerp(color, _RecColor.rgb, rec * _RecColor.a);
        }

        return half4(color, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SecurityCameraFeed"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
