// PS1_PostProcess_HLSL.shader
// Fullscreen post-process HLSL para URP FullScreenPassRendererFeature.
//
// Reemplaza al shadergraph "PS1_PostProcess" con las mismas property references
// (para que PS1Effect.mat conserve sus valores) y agrega Dither, Scanlines y
// Chromatic Aberration, todos toggle-ables y modulables desde el inspector.
//
// Reference names (para MaterialPropertyBlock / SetFloat / SetColor desde C#):
//   _EnableEffect               master on/off
//   _PixelSize                  filas de la grilla de pixelado (mas alto = menos pixelado)
//   _WarpStrength               fuerza de la ondulación CRT
//   _WarpIntensity              amplitud espacial de la onda
//   _JitterResolution           segunda grilla de pixelado, mas gruesa (0 = off). NO es jitter
//   _EnableDither               toggle dither Bayer 4x4
//   _DitherStrength             mezcla 0..1 (0 = sin dither, 1 = full pattern)
//   _DitherLevels               niveles de cuantización de color (2..8)
//   _EnableScanlines            toggle CRT scanlines
//   _ScanlineIntensity          0..1 — profundidad de las bandas oscuras
//   _ScanlineCount              cantidad de líneas por altura de pantalla
//   _EnableChromaticAberration  toggle CA
//   _ChromaticAberrationOffset  offset en UV (0..0.02 usualmente)

Shader "Hidden/Custom/PS1PostProcessHLSL"
{
    Properties
    {
        [Toggle] _EnableEffect              ("Enable Effect (master)", Float) = 1

        [Header(Pixelation and Warp)]
        _PixelSize                          ("Pixel Grid Rows (mas alto = menos pixelado)", Float) = 256
        _WarpStrength                       ("Warp Strength", Range(0, 3)) = 0.1
        _WarpIntensity                      ("Warp Intensity", Range(0, 0.01)) = 0.0018
        _JitterResolution                   ("Coarse Pixel Grid (0 = off, NO es jitter)", Float) = 0
        // Si _WarpStrength se sube mucho, las esquinas piden UV fuera de [0,1] y el
        // sampler estira el último pixel del borde (bordes "agrandados"/deformados).
        // Con esto en 1 el warp queda siempre clampeado al frame; apagalo (0) solo
        // si buscás a propósito ese look de stretch en los bordes.
        [Toggle] _ClampWarpEdges            ("Clamp Warp To Screen Edges", Float) = 1

        [Header(Dither)]
        [Toggle] _EnableDither              ("Enable Dither", Float) = 1
        _DitherStrength                     ("Dither Strength", Range(0, 1)) = 0.5
        _DitherLevels                       ("Dither Color Levels", Range(2, 8)) = 4

        [Header(Scanlines)]
        // Default 0.06 = casi imperceptible, tal como pide §6.10 del spec ("Scanlines CRT 0.06").
        [Toggle] _EnableScanlines           ("Enable Scanlines", Float) = 1
        _ScanlineIntensity                  ("Scanline Intensity", Range(0, 1)) = 0.06
        _ScanlineCount                      ("Scanline Count", Float) = 240

        [Header(Chromatic Aberration)]
        // Default 0 = apagado, el GlitchController se encarga de pulsarlo con timing aleatorio (§6.10).
        // Si se quiere CA "always on" (fuera de spec), subir manualmente en el material.
        [Toggle] _EnableChromaticAberration ("Enable Chromatic Aberration", Float) = 1
        _ChromaticAberrationOffset          ("CA Offset", Range(0, 0.02)) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float  _EnableEffect;
        float  _PixelSize;
        float  _WarpStrength;
        float  _WarpIntensity;
        float  _JitterResolution;
        float  _ClampWarpEdges;

        float  _EnableDither;
        float  _DitherStrength;
        float  _DitherLevels;

        float  _EnableScanlines;
        float  _ScanlineIntensity;
        float  _ScanlineCount;

        float  _EnableChromaticAberration;
        float  _ChromaticAberrationOffset;
    CBUFFER_END

    // Bayer 4x4 threshold matrix — clásico para dither ordenado (PSX ownage).
    static const float BayerMatrix4x4[16] =
    {
         0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
        12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
         3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
        15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
    };

    float BayerSample(float2 pixelCoord)
    {
        int2 p = int2(fmod(pixelCoord, 4.0));
        return BayerMatrix4x4[p.y * 4 + p.x];
    }

    // Aplica warp CRT tipo tubo curvado. Distorsiona radialmente desde el centro.
    float2 ApplyWarp(float2 uv)
    {
        float2 centered = uv - 0.5;
        float dist = dot(centered, centered);
        float warp = 1.0 + dist * _WarpStrength;
        // Suma senoidal chica para simular el "wobble" horizontal del CRT.
        centered.x += sin(uv.y * 100.0 + _Time.y) * _WarpIntensity * _WarpStrength;
        float2 warped = centered * warp + 0.5;
        // Toggle desde el material (_ClampWarpEdges): si está prendido, satura el
        // UV para que el warp nunca pida muestras fuera de [0,1] (evita el stretch
        // contra el borde de la textura). Apagalo si querés el look sin clamp.
        return _ClampWarpEdges > 0.5 ? saturate(warped) : warped;
    }

    // ADVERTENCIA: esto NO es un jitter. El nombre viene de que se pensó como el
    // vertex-snapping de PSX (la imagen "tiembla" un frame por pixel), pero para
    // temblar necesitaría un offset dependiente de _Time antes del floor, y no lo
    // tiene: es un floor(uv*res)/res estático. O sea, una SEGUNDA pasada de
    // pixelado, y como corre ANTES de ApplyPixelation, si su resolución es más
    // baja que _PixelSize es ella la que manda y _PixelSize deja de tener efecto.
    // Se deja en 0 (apagado). Subirlo solo si se quiere una grilla más gruesa que
    // _PixelSize a propósito; en ese caso el valor efectivo es el MENOR de los dos.
    float2 ApplyJitter(float2 uv)
    {
        if (_JitterResolution < 0.001) return uv;
        float aspect = _ScreenParams.x / _ScreenParams.y;
        float2 res = float2(_JitterResolution * aspect, _JitterResolution);
        float2 quantized = floor(uv * res) / res;
        return quantized;
    }

    // Pixelation clásica. OJO CON EL NOMBRE: _PixelSize NO es el tamaño del pixel,
    // es la CANTIDAD DE FILAS de la grilla. Más alto = más filas = MENOS pixelado.
    // El ancho en bloques se deriva del aspect ratio real de pantalla para que cada
    // bloque sea un cuadrado (píxel PS1 real), no un rectángulo estirado.
    float2 ApplyPixelation(float2 uv)
    {
        if (_PixelSize < 0.001) return uv;
        float aspect = _ScreenParams.x / _ScreenParams.y;
        float2 pixels = float2(_PixelSize * aspect, _PixelSize);
        return floor(uv * pixels) / pixels;
    }

    half3 ApplyDither(half3 color, float2 screenPixel)
    {
        // Cuantiza cada canal a N niveles con offset de Bayer para romper bandas.
        float levels = max(_DitherLevels, 2.0);
        float threshold = (BayerSample(screenPixel) - 0.5) / levels;
        half3 dithered = color + threshold * _DitherStrength;
        // Snap a niveles: floor(c*L)/L da posterización dura → mezcla con la version dithered.
        half3 posterized = floor(dithered * levels) / (levels - 1.0);
        return lerp(color, posterized, _DitherStrength);
    }

    half3 ApplyChromaticAberration(float2 uv)
    {
        // Offset R hacia afuera y B hacia adentro, G queda en el centro.
        float2 dir = uv - 0.5;
        float2 offset = dir * _ChromaticAberrationOffset;

        half r = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + offset).r;
        half g = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).g;
        half b = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - offset).b;
        return half3(r, g, b);
    }

    half ApplyScanlines(float2 uv)
    {
        // sin(uv.y * count * 2π) mapea a bandas — negative→dark, positive→bright.
        float s = sin(uv.y * _ScanlineCount * 3.14159265);
        // Convertimos a un multiplicador ∈ [1 - intensity, 1] para no clipar highlights.
        return 1.0 - _ScanlineIntensity * (0.5 - 0.5 * s);
    }

    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;

        // Master toggle — si está apagado devolvemos la escena limpia.
        if (_EnableEffect < 0.5)
        {
            return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
        }

        // 1) Warp CRT — ondulación radial + wobble horizontal.
        uv = ApplyWarp(uv);
        // 2) Jitter estilo vertex-snap de PSX.
        uv = ApplyJitter(uv);
        // 3) Pixelation — bloques de _PixelSize.
        uv = ApplyPixelation(uv);

        // 4) Muestreo de color (con CA opcional).
        half3 color;
        if (_EnableChromaticAberration > 0.5 && _ChromaticAberrationOffset > 0.00001)
        {
            color = ApplyChromaticAberration(uv);
        }
        else
        {
            color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
        }

        // 5) Dither posterization — ordena colores en pocos niveles con patrón Bayer.
        if (_EnableDither > 0.5)
        {
            // Coord de pixel real (no UV) para que el pattern se mueva 1-a-1 con pantalla.
            float2 screenPixel = uv * _ScreenParams.xy;
            color = ApplyDither(color, screenPixel);
        }

        // 6) Scanlines CRT — bandas oscuras horizontales.
        if (_EnableScanlines > 0.5 && _ScanlineIntensity > 0.001)
        {
            color *= ApplyScanlines(input.texcoord);
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
            Name "PS1PostProcessHLSL"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
