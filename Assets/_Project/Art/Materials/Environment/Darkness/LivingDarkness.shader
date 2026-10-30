// LivingDarkness.shader
// A membrane of living darkness stretched across an opening the player must not go back through.
// Near-black smoke that curls slowly on its own and, while the player looks at it, churns faster,
// bows toward them and reaches out where their gaze lands. DarknessGaze.cs measures the look and
// writes _Agitation, _LookTarget and _GazePoint per renderer through a MaterialPropertyBlock; the
// material asset stays shared. Blocking is NOT done here: a plain BoxCollider on the object does it.
//
// MESH. Built for a subdivided plane with 0..1 UVs (Unity's built-in Plane, 10x10 quads): the UVs
// fray the border and pin it in place, and the vertex count is what the reach can bend. A Quad has
// four vertices and cannot bulge at all.
//
// UNLIT ON PURPOSE. Darkness is the absence of light: no Light, lightmap or probe touches it, and
// it casts no shadow (no ShadowCaster pass). What makes it read is its silhouette eating the lit
// floor and walls around it, plus a faint oily sheen on the folds and slow tendrils inside.
//
// TRANSPARENT, BUT WRITES DEPTH. Alpha-blended for the frayed border and the soft contact with the
// walls; ZWrite On so whatever is drawn later in the transparent queue (ItemGlint at
// Transparent+50, particles) is hidden behind it. But URP copies _CameraDepthTexture before the
// transparent queue (right after the GBuffer: the renderer is deferred and SSAO reads depth early,
// see PC_Renderer.asset), so the membrane never reaches that texture and two things see through it:
//   - the Vision Fog fullscreen pass fogs each darkness pixel by the depth of what is BEHIND it.
//     The membrane is black, so fogged or not it stays black; only the sheen and tendrils fade
//     where the space behind is past the fog's clear radius.
//   - FogBeacons (the Nemesis's eyes) are composited in that pass against the same depth texture,
//     so a beacon behind the membrane still shows through it.
//
// Every value is tuned on the material; only the three runtime properties at the bottom belong to
// DarknessGaze. With _LookTarget.w = 0 (the material default) the reach and the swell stay off, so
// the Agitation slider can be previewed on the material in edit mode without a target.
//
// URP includes, like ItemGlint.shader and SecurityScanBeam.shader.
Shader "WIRED/Environment/Living Darkness"
{
    Properties
    {
        [MainColor] _BaseColor ("Darkness Color", Color) = (0.006, 0.005, 0.008, 1)
        _SwirlColor ("Tendril Color (inner smoke)", Color) = (0.09, 0.07, 0.11, 1)
        _SheenColor ("Sheen Color (oily folds)", Color) = (0.16, 0.14, 0.2, 1)
        _SheenPower ("Sheen Sharpness", Range(1, 8)) = 3
        _AgitatedGlow ("Extra Tendril/Sheen when Agitated", Range(0, 4)) = 1.5

        [Header(Smoke)]
        _NoiseScale ("Noise Scale (cells per metre)", Range(0.1, 8)) = 1.1
        _CalmSpeed ("Calm Flow Speed", Range(0, 2)) = 0.12
        _AgitatedSpeed ("Agitated Flow Speed", Range(0, 6)) = 0.9
        _FlowDirection ("Flow Direction (world, length scales speed)", Vector) = (0, 1, 0, 0)

        [Header(Border)]
        _EdgeWidth ("Frayed Edge Width (UV, 0 = hard edge)", Range(0, 0.5)) = 0.12
        _EdgeRaggedness ("Edge Raggedness", Range(0, 2)) = 0.8
        _EdgePin ("Pinned Border (UV, 0 = border moves too)", Range(0, 0.5)) = 0.15
        _SoftContact ("Soft Contact with Walls (m)", Range(0.001, 2)) = 0.3
        _CameraFade ("Fade Near the Camera (m)", Range(0.001, 2)) = 0.3

        [Header(Writhing)]
        _CalmWrithe ("Calm Writhe (m)", Range(0, 0.5)) = 0.04
        _AgitatedWrithe ("Agitated Writhe (m)", Range(0, 1)) = 0.22

        [Header(Reach)]
        _Swell ("Swell toward the Target (m)", Range(0, 2)) = 0.35
        _ReachDistance ("Reach Distance (m)", Range(0, 4)) = 1.1
        _ReachRadius ("Reach Radius (m)", Range(0.1, 6)) = 1.2
        _ReachStopDistance ("Never Closer to the Target than (m)", Range(0, 3)) = 0.9

        [Header(Written by DarknessGaze at runtime)]
        _Agitation ("Agitation", Range(0, 1)) = 0
        _LookTarget ("Look Target (world, w = 1 when set)", Vector) = (0, 0, 0, 0)
        _GazePoint ("Gaze Point (world, w = 1 when set)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LivingDarkness"
            // Forward-only: the renderer is deferred, and the transparent pass picks this tag up in
            // both modes (see the header of PSXIndustrial.shader).
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SwirlColor;
                float4 _SheenColor;
                float  _SheenPower;
                float  _AgitatedGlow;
                float  _NoiseScale;
                float  _CalmSpeed;
                float  _AgitatedSpeed;
                float4 _FlowDirection;
                float  _EdgeWidth;
                float  _EdgeRaggedness;
                float  _EdgePin;
                float  _SoftContact;
                float  _CameraFade;
                float  _CalmWrithe;
                float  _AgitatedWrithe;
                float  _Swell;
                float  _ReachDistance;
                float  _ReachRadius;
                float  _ReachStopDistance;
                float  _Agitation;
                float4 _LookTarget;
                float4 _GazePoint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 restWS     : TEXCOORD1;   // Before displacement: the smoke rides on the membrane.
                float3 positionWS : TEXCOORD2;   // After displacement: facets and view direction.
                float  eyeDepth   : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            // Value noise in 3D, same recipe as SecurityScanBeam: eight hashes per sample, smooth,
            // and cheap enough to take four samples per pixel.
            float DarkHash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float DarkNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = DarkHash13(i);
                float n100 = DarkHash13(i + float3(1, 0, 0));
                float n010 = DarkHash13(i + float3(0, 1, 0));
                float n110 = DarkHash13(i + float3(1, 1, 0));
                float n001 = DarkHash13(i + float3(0, 0, 1));
                float n101 = DarkHash13(i + float3(1, 0, 1));
                float n011 = DarkHash13(i + float3(0, 1, 1));
                float n111 = DarkHash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            // 0 on the mesh border, 0.5 in the middle: distance to the nearest edge, in UV.
            float DarkEdgeDistance(float2 uv)
            {
                float2 e = min(uv, 1.0 - uv);
                return min(e.x, e.y);
            }

            // Calm and agitated motion are two layers at two FIXED speeds, mixed by _Agitation.
            // Scaling one clock by the agitation instead would jump the pattern every time the
            // agitation changes: _Time.y * speed moves by _Time.y * dSpeed, which after a few minutes
            // of play is thousands of cells in a single frame.

            Varyings vert(Attributes input)
            {
                Varyings o;

                float3 restWS   = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float3 p     = restWS * _NoiseScale;
                float3 flow  = _FlowDirection.xyz;
                float  calmT = _Time.y * _CalmSpeed;
                float  agitT = _Time.y * _AgitatedSpeed;

                // The border stays put and the middle moves, like a membrane stretched across the
                // opening: a border that moved would open a gap between the darkness and the walls.
                float pin = _EdgePin > 0.0001 ? smoothstep(0.0, _EdgePin, DarkEdgeDistance(input.uv)) : 1.0;

                float calmN  = DarkNoise(p * 0.6 - flow * calmT);
                float agitN  = DarkNoise(p * 1.7 - flow * agitT + 23.17);
                float writhe = (calmN - 0.5) * 2.0 * _CalmWrithe
                             + (agitN - 0.5) * 2.0 * _AgitatedWrithe * _Agitation;

                float3 positionWS = restWS + normalWS * (writhe * pin);

                if (_LookTarget.w > 0.5)
                {
                    float3 toTarget = _LookTarget.xyz - restWS;
                    float  dist     = length(toTarget);
                    float3 dir      = toTarget / max(dist, 1e-4);

                    // The whole membrane bows toward the side the player is on.
                    float side = dot(normalWS, toTarget) >= 0.0 ? 1.0 : -1.0;
                    positionWS += normalWS * (side * _Swell * _Agitation * pin);

                    // And reaches for them around the point the gaze lands on: a gaussian spot,
                    // roughened by the churn so it comes out as uneven fingers, not a smooth dome.
                    if (_GazePoint.w > 0.5)
                    {
                        float g     = distance(restWS, _GazePoint.xyz) / max(_ReachRadius, 1e-3);
                        float spot  = exp(-2.0 * g * g);
                        float reach = _ReachDistance * _Agitation * spot * (0.6 + 0.8 * agitN) * pin;

                        // Never into the player's face, however close they stand.
                        reach = min(reach, max(dist - _ReachStopDistance, 0.0));
                        positionWS += dir * reach;
                    }
                }

                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv         = input.uv;
                o.restWS     = restWS;
                o.positionWS = positionWS;
                o.eyeDepth   = -TransformWorldToView(positionWS).z;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 p     = input.restWS * _NoiseScale;
                float3 flow  = _FlowDirection.xyz;
                float  calmT = _Time.y * _CalmSpeed;
                float  agitT = _Time.y * _AgitatedSpeed;

                // Calm smoke: a slow, large warp bends two octaves into curling tendrils.
                float  warp  = DarkNoise(p * 0.45 + float3(0.0, 0.0, calmT * 0.5));
                float3 pc    = p - flow * calmT + warp * 1.7;
                float  smoke = DarkNoise(pc) * 0.65 + DarkNoise(pc * 2.07 + 11.3) * 0.35;

                // Agitated churn: finer and faster, mixed in only while it is being looked at.
                [branch]
                if (_Agitation > 0.001)
                {
                    float3 pa    = p * 2.3 - flow * agitT + warp * 2.5;
                    float  churn = DarkNoise(pa);
                    smoke = lerp(smoke, smoke * 0.45 + churn * 0.55, _Agitation);
                }

                // Ridged: thin filaments where the noise crosses its middle value.
                float ridge   = 1.0 - abs(smoke * 2.0 - 1.0);
                float tendril = ridge * ridge;
                tendril *= tendril;

                // Faceted normal of the DISPLACED surface, from screen derivatives: the folds the
                // writhing and the reach make catch a faint sheen, flat-shaded like PSX geometry.
                float3 facet   = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float  rim     = pow(1.0 - saturate(abs(dot(facet, viewDir))), _SheenPower);

                float  glow  = 1.0 + _AgitatedGlow * _Agitation;
                float3 color = _BaseColor.rgb
                             + _SwirlColor.rgb * (tendril * glow)
                             + _SheenColor.rgb * (rim * glow);

                // Frayed border: 0 exactly on the mesh edge, 1 past _EdgeWidth, eaten by the smoke
                // in between.
                float edgeAlpha = 1.0;
                if (_EdgeWidth > 0.0001)
                {
                    float e = DarkEdgeDistance(input.uv) / _EdgeWidth;
                    edgeAlpha = smoothstep(0.0, 1.0, saturate(e * (1.0 + _EdgeRaggedness) - smoke * _EdgeRaggedness));
                }

                // Soft contact: fades where a wall or the floor is just behind the membrane, so it
                // seeps into them instead of cutting a hard line. The depth texture does not hold
                // the membrane itself (it is copied before the transparent queue).
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float  sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float  contact  = saturate((sceneEye - input.eyeDepth) / _SoftContact);

                // A fold that comes right up to the lens would black out the screen.
                float cameraFade = saturate((input.eyeDepth - _ProjectionParams.y) / _CameraFade);

                float alpha = _BaseColor.a * edgeAlpha * contact * cameraFade;

                // Wisps that are almost gone write no depth: they must not hide what is behind them.
                clip(alpha - 0.02);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
