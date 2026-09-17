// ItemGlint.shader
// The four-point star of ItemGlint.cs: a camera-facing quad, additive, drawn procedurally so the
// shape is tuned here with sliders instead of repainting a sprite. ItemGlint writes colour, alpha and
// rotation through a MaterialPropertyBlock and passes the star's size as the object scale.
//
// Unlit, no shadows, depth-tested but not depth-writing: walls hide it, it hides nothing. It is drawn
// in the transparent queue, so VisionFog and the PS1 filter (both BeforeRenderingPostProcessing) land
// on it like on everything else.
//
// URP includes, like ItemPSX_Outline.shader.
Shader "WIRED/Items/Item Glint"
{
    Properties
    {
        [HDR] _GlintColor ("Colour (ItemGlint sets it)", Color) = (1, 1, 1, 1)
        _GlintAlpha ("Alpha (ItemGlint sets it)", Range(0, 1)) = 1
        _GlintRotation ("Rotation in radians (ItemGlint sets it)", Float) = 0

        [Header(Star shape)]
        _RayThinness ("Ray Thinness", Range(1, 20)) = 4.5
        _RayFalloff ("Ray Falloff (higher = shorter bright part)", Range(0.25, 6)) = 1.4
        _CoreSize ("Core Size", Range(0, 1)) = 0.3
        _PixelGrid ("Pixel Grid (cells across, 0 = smooth)", Range(0, 64)) = 0

        [Header(Optional sprite instead of the procedural star)]
        [ToggleUI] _UseSprite ("Use Sprite", Float) = 0
        [NoScaleOffset] _MainTex ("Sprite (white on black, or alpha)", 2D) = "white" {}

        [Header(Placement)]
        _CameraPull ("Pull Toward Camera (m)", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+50"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
            // The billboard is built from the object's origin and scale. Batching would bake quads
            // into one world-space mesh and lose both.
            "DisableBatching" = "True"
        }

        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Glint"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GlintColor;
                float  _GlintAlpha;
                float  _GlintRotation;
                float  _RayThinness;
                float  _RayFalloff;
                float  _CoreSize;
                float  _PixelGrid;
                float  _UseSprite;
                float  _CameraPull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            Varyings vert (Attributes input)
            {
                Varyings o;

                float3 centreVS = TransformWorldToView(TransformObjectToWorld(float3(0.0, 0.0, 0.0)));
                float  size     = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));

                float s = sin(_GlintRotation);
                float c = cos(_GlintRotation);
                float2 corner = float2(c * input.positionOS.x - s * input.positionOS.y,
                                       s * input.positionOS.x + c * input.positionOS.y) * size;

                // The star sits inside the item, so the item's own mesh would cut it. Every vertex
                // slides toward the camera along its own line of sight, all by the same factor: the
                // star clears the mesh without moving or changing size on screen.
                float distance = max(-centreVS.z, 1e-4);
                float pulled   = max(distance - _CameraPull, _ProjectionParams.y * 2.0);
                float k        = min(pulled / distance, 1.0);

                float3 positionVS = (centreVS + float3(corner, 0.0)) * k;

                o.positionCS = TransformWViewToHClip(positionVS);
                o.uv         = input.uv;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_PixelGrid >= 1.0)
                    uv = floor(uv * _PixelGrid + 0.5) / _PixelGrid;

                // 0 on the centre lines, 1 at the edge of the quad.
                float2 p = abs(uv * 2.0 - 1.0);

                // One ray per axis: brightest on the centre, tapering to a point at the edge.
                float alongX = saturate(1.0 - p.x);
                float alongY = saturate(1.0 - p.y);
                float rayX   = saturate(1.0 - p.y * _RayThinness / max(alongX, 1e-3)) * pow(alongX, _RayFalloff);
                float rayY   = saturate(1.0 - p.x * _RayThinness / max(alongY, 1e-3)) * pow(alongY, _RayFalloff);
                float core   = saturate(1.0 - length(p) / max(_CoreSize, 1e-3));
                float star   = saturate(max(rayX, rayY) + core * core);

                float4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float  shape  = lerp(star, max(sprite.r, max(sprite.g, sprite.b)) * sprite.a, _UseSprite);

                // Additive: fog fades it toward black, not toward the fog colour.
                float3 colour = MixFogColor(_GlintColor.rgb, float3(0.0, 0.0, 0.0), input.fogFactor);
                return float4(colour, shape * _GlintAlpha * _GlintColor.a);
            }
            ENDHLSL
        }
    }
}
