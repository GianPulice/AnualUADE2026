// PSXIndustrial.shader
// Shader maestro para los props de "Puzzle Items" (Caja de Fusible, Puerta, Valve).
// Reconstruye en Unity los materiales que Blender no exporta por FBX.
//
// Por que existe: el FBX transfiere las texturas pero NO el grafo de nodos. En Blender
// el oxido marron-naranja salia de un mix procedural, y el Valve entero era 100%
// procedural (sin una sola textura). Este shader reimplementa esas dos cosas:
//
//   1) Fallbacks escalares en cada mapa  -> un material sin ninguna textura funciona
//      solo con _BaseColor + _Roughness + _Metallic (todo el Valve).
//   2) Capa de oxido procedural          -> ruido triplanar en world space enmascarado
//      por la luminancia del albedo y el AO, tweakeable desde el inspector.
//   3) Roughness -> Smoothness           -> el paso que URP necesita y Blender no da.
//   4) Height -> Normal                  -> para "Madera puerta", cuyo set vino sin
//      normal map (el FBX pide WoodSiding003_1K-JPG_NormalGL.jpg, que no existe).
//
// IMPORTANTE - LightMode = "UniversalForwardOnly", no "UniversalForward".
// El proyecto renderiza en DEFERRED (PC_Renderer.asset, m_RenderingMode: 2). En ese
// modo URP solo dibuja los tags "UniversalGBuffer" y "UniversalForwardOnly"
// (UniversalRenderer.cs:325-330); un pass tageado "UniversalForward" sin pass de
// GBuffer NO se dibuja nunca. "UniversalForwardOnly" en cambio lo levantan los dos
// modos, porque DrawObjectsPass lo trae en sus tags por defecto (DrawObjectsPass.cs:80).
// Es ademas lo que URP recomienda para materiales que no se iluminan en deferred.
//
// Nota: todos los mapas comparten _BaseMap_ST. Vienen del mismo set PBR con el mismo
// layout de UV, asi que un solo tiling/offset alcanza y evita 6 vars de ST.

Shader "Custom/PSXIndustrial"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Header(Superficie)]
        _RoughnessMap("Roughness Map", 2D) = "white" {}
        _Roughness("Roughness", Range(0, 1)) = 0.8
        _MetallicMap("Metallic Map", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 0
        _OcclusionMap("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1

        [Header(Normales)]
        [Toggle(_NORMALMAP)] _NormalMapEnabled("Usar Normal Map", Float) = 0
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0, 2)) = 1
        // Alternativa cuando el set no trae normal map (caso Madera puerta).
        [Toggle(_HEIGHT_NORMAL)] _HeightNormalEnabled("Derivar Normal del Height", Float) = 0
        _HeightMap("Height / Displacement Map", 2D) = "gray" {}
        _HeightNormalStrength("Height Normal Strength", Range(0, 8)) = 2

        [Header(Capa de oxido)]
        [Toggle(_RUST_ON)] _RustEnabled("Activar Oxido", Float) = 0
        _RustColor("Rust Color", Color) = (0.35, 0.16, 0.07, 1)
        _RustAmount("Rust Amount", Range(0, 1)) = 0.6
        _RustScale("Rust Scale (world)", Range(0.1, 40)) = 6
        _RustContrast("Rust Contrast", Range(0.5, 8)) = 2
        _RustLumaBias("Oxido sigue manchas oscuras", Range(0, 1)) = 0.6
        _RustRoughness("Rust Roughness", Range(0, 1)) = 0.95
        _RustMetallic("Rust Metallic", Range(0, 1)) = 0

        [Header(Emision)]
        // Mismos nombres que ItemPSX_Outline: cualquier codigo que ya maneje
        // _EmissionIntensity via MaterialPropertyBlock sirve igual sobre estos props.
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionIntensity("Emission Intensity", Range(0, 20)) = 0

        [Header(Alpha)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5

        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Surface("Surface Type", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"            = "Opaque"
            "RenderPipeline"        = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector"       = "True"
            "Queue"                 = "Geometry"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _HeightMap_TexelSize;
            float4 _BaseColor;
            float  _Roughness;
            float  _Metallic;
            float  _OcclusionStrength;
            float  _BumpScale;
            float  _HeightNormalStrength;
            float4 _RustColor;
            float  _RustAmount;
            float  _RustScale;
            float  _RustContrast;
            float  _RustLumaBias;
            float  _RustRoughness;
            float  _RustMetallic;
            float4 _EmissionColor;
            float  _EmissionIntensity;
            float  _Cutoff;
            float  _AlphaClip;
            float  _Cull;
            float  _Surface;
            float  _NormalMapEnabled;
            float  _HeightNormalEnabled;
            float  _RustEnabled;
        CBUFFER_END

        TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);      SAMPLER(sampler_BumpMap);
        TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
        TEXTURE2D(_MetallicMap);  SAMPLER(sampler_MetallicMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_HeightMap);    SAMPLER(sampler_HeightMap);

        // ---------------------------------------------------------------------
        // Ruido de valor. Usado solo por la capa de oxido.
        // ---------------------------------------------------------------------
        float PSXHash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float PSXValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);          // smoothstep en las dos direcciones

            float a = PSXHash21(i);
            float b = PSXHash21(i + float2(1.0, 0.0));
            float c = PSXHash21(i + float2(0.0, 1.0));
            float d = PSXHash21(i + float2(1.0, 1.0));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // 2 octavas alcanzan para manchas de oxido y mantiene el costo bajo
        // (son 3 planos x 2 octavas = 6 evaluaciones por pixel).
        float PSXFbm(float2 p)
        {
            return PSXValueNoise(p) * 0.667 + PSXValueNoise(p * 2.17) * 0.333;
        }

        // Triplanar en world space a proposito, por dos razones:
        //   - el oxido no respeta seams de UV (se acumula por gravedad/humedad);
        //   - hace que funcione en meshes sin UV map, que es justo el caso del
        //     Valve, exportado sin ninguna textura ni UV util.
        float PSXRustNoise(float3 positionWS, float3 normalWS)
        {
            float3 p = positionWS * _RustScale;
            float3 w = abs(normalWS);
            w /= max(w.x + w.y + w.z, 1e-4);
            return PSXFbm(p.yz) * w.x + PSXFbm(p.xz) * w.y + PSXFbm(p.xy) * w.z;
        }

        // Mascara de oxido. La clave para que no lea como pintura encima es anclarla
        // a lo que la textura ya trae: las manchas oscuras del basecolor y las
        // cavidades del AO son donde el oxido aparece primero.
        float PSXRustMask(float3 positionWS, float3 normalWS, half3 albedo, half occlusion, out float noiseOut)
        {
            float noise = PSXRustNoise(positionWS, normalWS);
            noiseOut = noise;

            float lumaInv = 1.0 - saturate(dot(albedo, half3(0.299, 0.587, 0.114)));
            float cavity  = 1.0 - saturate(occlusion);

            float mask = lerp(noise, noise * (0.35 + 1.3 * lumaInv), _RustLumaBias);
            mask = saturate(mask + cavity * 0.35);
            mask = saturate((mask - 0.5) * _RustContrast + 0.5);   // contraste alrededor del medio
            return mask * _RustAmount;
        }

        // Sobel de 4 taps sobre el height. Sustituto del normal map faltante.
        float3 PSXNormalFromHeight(float2 uv)
        {
            float2 ts = _HeightMap_TexelSize.xy;
            float hL = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, uv - float2(ts.x, 0)).r;
            float hR = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, uv + float2(ts.x, 0)).r;
            float hD = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, uv - float2(0, ts.y)).r;
            float hU = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, uv + float2(0, ts.y)).r;

            return normalize(float3((hL - hR) * _HeightNormalStrength,
                                    (hD - hU) * _HeightNormalStrength,
                                    1.0));
        }

        // Compartido por los pases de sombra/depth para que el cartel de advertencia
        // recorte igual en todos lados.
        void PSXAlphaClip(float2 uv)
        {
        #if defined(_ALPHATEST_ON)
            half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
            clip(a - _Cutoff);
        #endif
        }
        ENDHLSL

        // -----------------------------------------------------------------
        // Lit pass. "UniversalForwardOnly" para que lo dibuje tanto el
        // renderer forward como el deferred (ver nota del encabezado).
        // -----------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend One Zero
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _HEIGHT_NORMAL
            #pragma shader_feature_local _RUST_ON
            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #pragma vertex   VertLit
            #pragma fragment FragLit

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 tangentWS   : TEXCOORD2;   // w = signo del bitangente
                float2 uv          : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
                float4 shadowCoord : TEXCOORD5;
                float  fogCoord    : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertLit(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normals   = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                o.positionCS = positions.positionCS;
                o.positionWS = positions.positionWS;
                o.normalWS   = normals.normalWS;
                o.tangentWS  = float4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv         = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, o.lightmapUV);
                OUTPUT_SH(o.normalWS.xyz, o.vertexSH);

                o.shadowCoord = GetShadowCoord(positions);
                o.fogCoord    = ComputeFogFactor(positions.positionCS.z);
                return o;
            }

            half4 FragLit(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 1) Albedo + alpha.
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo     = baseSample.rgb * _BaseColor.rgb;
                half  alpha      = baseSample.a * _BaseColor.a;

            #if defined(_ALPHATEST_ON)
                clip(alpha - _Cutoff);
            #endif

                // 2) Mapas de superficie. Cada uno multiplica su escalar, asi un
                //    material sin texturas (default "white") queda gobernado solo
                //    por los sliders.
                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, input.uv).r * _Roughness;
                half metallic  = SAMPLE_TEXTURE2D(_MetallicMap,  sampler_MetallicMap,  input.uv).r * _Metallic;
                half occRaw    = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).g;
                half occlusion = lerp(1.0h, occRaw, _OcclusionStrength);

                // 3) Normal tangente: normal map, o derivada del height, o plana.
                float3 normalTS = float3(0, 0, 1);
            #if defined(_NORMALMAP)
                normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
            #elif defined(_HEIGHT_NORMAL)
                normalTS = PSXNormalFromHeight(input.uv);
            #endif

                float sgn = input.tangentWS.w;
                float3 bitangentWS = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                float3x3 tangentToWorld = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS.xyz);
                float3 N = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 4) Capa de oxido. Reemplaza al mix procedural de Blender.
            #if defined(_RUST_ON)
                float rustNoise;
                float rust = PSXRustMask(input.positionWS, N, albedo, occlusion, rustNoise);
                // Variacion tonal dentro del oxido para que no lea como pintura plana.
                half3 rustTint = _RustColor.rgb * (0.65h + 0.7h * rustNoise);
                albedo    = lerp(albedo, rustTint, rust);
                roughness = lerp(roughness, _RustRoughness, rust);
                metallic  = lerp(metallic,  _RustMetallic,  rust);
            #endif

                // 5) URP trabaja en smoothness. Esta conversion es exactamente el
                //    paso que se pierde al exportar desde Blender.
                half smoothness = 1.0h - saturate(roughness);

                InputData inputData        = (InputData)0;
                inputData.positionWS       = input.positionWS;
                inputData.normalWS         = N;
                inputData.viewDirectionWS  = V;
                inputData.shadowCoord      = input.shadowCoord;
                inputData.fogCoord         = input.fogCoord;
                inputData.vertexLighting   = half3(0, 0, 0);
                inputData.bakedGI          = SAMPLE_GI(input.lightmapUV, input.vertexSH, N);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask       = SAMPLE_SHADOWMASK(input.lightmapUV);

                SurfaceData surfaceData         = (SurfaceData)0;
                surfaceData.albedo              = albedo;
                surfaceData.metallic            = metallic;
                surfaceData.specular            = half3(0, 0, 0);
                surfaceData.smoothness          = smoothness;
                surfaceData.normalTS            = normalTS;
                surfaceData.emission            = _EmissionColor.rgb * _EmissionIntensity;
                surfaceData.occlusion           = occlusion;
                surfaceData.alpha               = alpha;
                surfaceData.clearCoatMask       = 0.0;
                surfaceData.clearCoatSmoothness = 0.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // ShadowCaster
        // -----------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct ShadowAttrs
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVarys
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 _LightDirection;
            float3 _LightPosition;

            float4 GetShadowPositionHClip(ShadowAttrs input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            ShadowVarys ShadowVert(ShadowAttrs input)
            {
                ShadowVarys o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = GetShadowPositionHClip(input);
                o.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return o;
            }

            half4 ShadowFrag(ShadowVarys input) : SV_Target
            {
                PSXAlphaClip(input.uv);
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // DepthOnly
        // -----------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag

            struct DepthAttrs
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVarys
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVarys DepthVert(DepthAttrs input)
            {
                DepthVarys o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return o;
            }

            half4 DepthFrag(DepthVarys input) : SV_Target
            {
                PSXAlphaClip(input.uv);
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // DepthNormals - lo consume el SSAO (activo en PC_Renderer.asset).
        // Usa la normal de vertice: alcanza de sobra para oclusion.
        // -----------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma vertex   DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct DNAttrs
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVarys
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DNVarys DepthNormalsVert(DNAttrs input)
            {
                DNVarys o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normals   = GetVertexNormalInputs(input.normalOS);
                o.positionCS = positions.positionCS;
                o.normalWS   = normals.normalWS;
                o.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return o;
            }

            half4 DepthNormalsFrag(DNVarys input) : SV_Target
            {
                PSXAlphaClip(input.uv);
                return half4(normalize(input.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
