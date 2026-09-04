// ItemPSX_Outline.shader
// Reemplazo HLSL del shadergraph "ItemPSX" que preserva las properties existentes
// (BaseMap, BaseColor, TintColor, TintIntensity, EmissionColor, EmissionIntensity,
//  Smoothness) y agrega un outline por fresnel que funciona sobre cualquier mesh
// sin conocer su forma. El "color language" del item se controla desde inspector
// con _OutlineColor + _OutlineIntensity + _OutlinePower.
//
// Formulación del outline:
//   fresnel  = pow(1 - saturate(dot(N_world, V_world)), _OutlinePower)
//   outline  = fresnel * _OutlineColor.rgb * _OutlineIntensity
//   emission = _EmissionColor * _EmissionIntensity + outline
// El outline sale por la vía de emission → participa del bloom y del lit pipeline.

Shader "Custom/ItemPSXOutline"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (0.29, 0.29, 0.29, 1)

        _TintColor("Tint Color", Color) = (0.216, 0.278, 0.310, 1)
        _TintIntensity("Tint Intensity", Range(0, 2)) = 0.15

        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionIntensity("Emission Intensity", Range(0, 10)) = 0

        _Smoothness("Smoothness", Range(0, 1)) = 0.1

        [Header(Outline)]
        // NOTA: intensidad 0 por default para respetar §4.6.1 del spec (WIRED
        // no usa outlines de items — feedback por tint+emission). El material
        // instala la property y el diseñador la sube manualmente sólo en el
        // subset donde el "color language" lo justifique (ej: puzzles del §4.7).
        [HDR] _OutlineColor("Outline Color", Color) = (0.4, 0.7, 1, 1)
        _OutlineIntensity("Outline Intensity", Range(0, 10)) = 0
        _OutlinePower("Outline Power", Range(0.5, 8)) = 3

        // Compatibilidad con inspector URP Lit — hidden pero necesarias para pases estándar.
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Surface("Surface Type", Float) = 0
        [HideInInspector] _Cutoff("Alpha Cutoff", Float) = 0.5
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0
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
            float4 _BaseColor;
            float4 _TintColor;
            float  _TintIntensity;
            float4 _EmissionColor;
            float  _EmissionIntensity;
            float  _Smoothness;
            float4 _OutlineColor;
            float  _OutlineIntensity;
            float  _OutlinePower;
            float  _Cutoff;
            float  _Surface;
            float  _AlphaClip;
            float  _Cull;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        // -----------------------------------------------------------------
        // Universal Forward — main lit pass with tint + emission + fresnel outline.
        // -----------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0

            // URP keywords que UniversalFragmentPBR mira.
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
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 3);
                float4 shadowCoord : TEXCOORD4;
                float  fogCoord   : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertLit(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normals   = GetVertexNormalInputs(input.normalOS);

                o.positionCS = positions.positionCS;
                o.positionWS = positions.positionWS;
                o.normalWS   = normals.normalWS;
                o.uv         = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, o.lightmapUV);
                OUTPUT_SH(o.normalWS.xyz, o.vertexSH);

                o.shadowCoord = GetShadowCoord(positions);
                o.fogCoord = ComputeFogFactor(positions.positionCS.z);
                return o;
            }

            half4 FragLit(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 1) Base color: sample × lerp(base, tint, tintIntensity).
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 tinted     = lerp(_BaseColor.rgb, _TintColor.rgb, saturate(_TintIntensity));
                half3 albedo     = baseSample.rgb * tinted;

                // 2) Fresnel outline usando normal mundial + view direction.
                //    Independiente de la forma del mesh — funciona en cualquier objeto.
                float3 N = normalize(input.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float  fresnel = pow(saturate(1.0 - saturate(dot(N, V))), max(_OutlinePower, 0.001));
                half3  outline = fresnel * _OutlineColor.rgb * _OutlineIntensity;

                // 3) Emission = emisión base + outline.
                half3 emission = _EmissionColor.rgb * _EmissionIntensity + outline;

                // 4) Armado de InputData / SurfaceData para el PBR de URP.
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
                surfaceData.metallic            = 0.0;
                surfaceData.specular            = half3(0, 0, 0);
                surfaceData.smoothness          = _Smoothness;
                surfaceData.normalTS            = float3(0, 0, 1);
                surfaceData.emission            = emission;
                surfaceData.occlusion           = 1.0;
                surfaceData.alpha               = _BaseColor.a * baseSample.a;
                surfaceData.clearCoatMask       = 0.0;
                surfaceData.clearCoatSmoothness = 0.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // ShadowCaster — necesario para que el objeto proyecte sombras.
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
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct ShadowAttrs
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVarys
            {
                float4 positionCS : SV_POSITION;
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
                return o;
            }

            half4 ShadowFrag(ShadowVarys input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // DepthOnly — usado por URP para depth prepass / MSAA.
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
            #pragma multi_compile_instancing
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag

            struct DepthAttrs
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVarys
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVarys DepthVert(DepthAttrs input)
            {
                DepthVarys o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 DepthFrag(DepthVarys input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // DepthNormals — usado por SSAO y otros efectos que consumen normals buffer.
        // -----------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma vertex   DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct DNAttrs
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVarys
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
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
                return o;
            }

            half4 DepthNormalsFrag(DNVarys input) : SV_Target
            {
                return half4(normalize(input.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
