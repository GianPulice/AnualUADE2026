// The ray a security camera throws at the player while it scans them: ONE fan of light from the
// lens to the scan band, whose far edge wraps the front of the body at that height - it ends on the
// body's surface and follows its curve, round over the chest, around each arm, on each leg. It
// moves down the body with the scan, and up and down it once the body is full.
//
// SecurityScanOverlay builds the fan every frame from the body's own pose (see its comment for how
// the curve is measured) and draws it with Graphics.RenderMesh, vertices already in world space.
// UVs: y runs from 0 at the lens to 1 on the body; x runs 0..1 across the fan and stays constant
// along each line from the lens, so the sides can be softened in a single step.
//
// So that it reads as light and not as a flat sheet: pulses of energy travel from the lens to the
// body, a faint flicker shimmers across it, it glows more the more edge-on it is seen - the way a
// thin layer of lit smoke looks brightest when you look along it - its sides are soft, dust drifts
// through it, and it fades softly where it meets the body or a wall instead of ending on a cut.
//
// The look is tuned here, on the material. _Strength (the fade) is written per ray by
// SecurityScanOverlay through a MaterialPropertyBlock.
Shader "WIRED/Security Scan Beam"
{
    Properties
    {
        [HDR] _BeamColor ("Beam Color", Color) = (1.6, 0.12, 0.08, 1)
        _BeamIntensity ("Intensity", Range(0, 4)) = 0.8

        [Header(Sheet)]
        _SideSoftness ("Side Softness", Range(0.01, 1)) = 0.25
        _LensFade ("Fade Near the Lens (fraction)", Range(0.01, 1)) = 0.12
        _EdgeOnBoost ("Glow when Seen Edge-On", Range(0, 4)) = 1.5
        _EdgeOnPower ("Edge-On Sharpness", Range(1, 16)) = 4

        [Header(Pulses)]
        _PulseDensity ("Pulses along the Ray", Range(0, 20)) = 5
        _PulseSpeed ("Pulse Speed (towards the body)", Range(0, 10)) = 2.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.45

        [Header(Shimmer)]
        _ShimmerDensity ("Shimmer Lines across the Ray", Range(1, 80)) = 30
        _ShimmerSpeed ("Shimmer Speed", Range(0, 20)) = 6
        _ShimmerAmount ("Shimmer Amount", Range(0, 1)) = 0.3

        [Header(Dust)]
        _DustScale ("Dust Scale", Range(0.5, 30)) = 7
        _DustSpeed ("Dust Drift (m per s)", Range(0, 2)) = 0.25
        _DustAmount ("Dust Amount", Range(0, 1)) = 0.35

        [Header(Contact)]
        _SoftDistance ("Soft Contact Distance (m)", Range(0.005, 1)) = 0.04

        // Written per ray by SecurityScanOverlay. Not for hand-tuning.
        [HideInInspector] _Strength ("Strength", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BeamColor;
                half _BeamIntensity;
                half _SideSoftness;
                half _LensFade;
                half _EdgeOnBoost;
                half _EdgeOnPower;
                half _PulseDensity;
                half _PulseSpeed;
                half _PulseAmount;
                half _ShimmerDensity;
                half _ShimmerSpeed;
                half _ShimmerAmount;
                half _DustScale;
                half _DustSpeed;
                half _DustAmount;
                half _SoftDistance;
                half _Strength;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
            };

            // Value noise in 3D: cheap, smooth, and enough to read as dust at beam scale.
            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.positionWS = TransformObjectToWorld(v.vertex.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normal);
                o.pos = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.pos);
                o.uv = v.uv;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half along = i.uv.y;

                // Soft sides: uv.x is constant along each line from the lens.
                half across = i.uv.x * 2.0 - 1.0;
                half side = saturate((1.0 - abs(across)) / _SideSoftness);

                // Faint at the lens, so the camera does not look like it is on fire.
                half lens = smoothstep(0.0, _LensFade, along);

                // Pulses travelling from the lens to the body: the scan is reaching for them.
                half wave = 0.5 + 0.5 * sin((along * _PulseDensity - _Time.y * _PulseSpeed) * 6.2831853);
                half pulses = lerp(1.0, wave * 2.0, _PulseAmount);

                // A faint flicker across the fan, line by line, like an unstable signal.
                float shimmerLine = floor(i.uv.x * _ShimmerDensity);
                float flick = frac(sin(shimmerLine * 91.345 + floor(_Time.y * _ShimmerSpeed) * 47.853) * 43758.5453);
                half shimmer = lerp(1.0, 0.4 + flick * 1.2, _ShimmerAmount);

                // Brighter seen edge-on, like a layer of lit smoke looked along.
                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half edgeOn = pow(1.0 - saturate(abs(dot(normalize(i.normalWS), viewDir))), _EdgeOnPower);
                half view = 1.0 + edgeOn * _EdgeOnBoost;

                float3 drift = float3(0.0, -_DustSpeed, _DustSpeed * 0.5) * _Time.y;
                half dust = lerp(1.0, ValueNoise3((i.positionWS + drift) * _DustScale) * 1.6, _DustAmount);

                // Soft contact: fade out where the fan is about to pass behind something. Kept short:
                // the fan ends ON the body, and a long fade would pull its curved edge off it.
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragmentDepth = LinearEyeDepth(i.pos.z, _ZBufferParams);
                half contact = saturate((sceneDepth - fragmentDepth) / _SoftDistance);

                half amount = side * lens * pulses * shimmer * view * dust * contact * _BeamIntensity;
                return half4(_BeamColor.rgb * amount * _Strength, 0);
            }
            ENDHLSL
        }
    }
}
