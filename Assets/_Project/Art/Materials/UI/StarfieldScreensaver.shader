// StarfieldScreensaver.shader
// The exit screen's background: the Windows 95 "Starfield" screensaver, stars streaming out of the
// centre of the screen, going into warp — the game flying away as it closes. Only shown when quitting
// (LoadingScreenView's exit mode); scene changes keep Mystify.
//
// Same arrangement as MystifyScreensaver.shader, and driven by the same script (MystifyScreensaver.cs
// with Randomize Look off): blitted into a small point-filtered render texture, so the stars come out
// as hard VGA blocks that the canvas's CRT tube then curves and scans. The script pushes _Seed (a new
// field every show), _Clock (unscaled seconds since the show) and _TargetSize.
//
// No state. Each star has a fixed direction and a depth that cycles from far to near as the field
// travels; the distance travelled is a closed-form function of time (constant acceleration up to a
// top speed), so the field starts at a cruise and ramps into warp without any history.

Shader "WIRED/UI/Starfield Screensaver"
{
    Properties
    {
        [Header(Field)]
        [IntRange] _Stars ("Stars", Range(16, 256)) = 160
        _Spread ("Spawn Radius (screen heights)", Range(0.01, 0.3)) = 0.06

        [Header(Motion)]
        // Depth units per second: 1 = a star crosses from far to near in one second.
        _Speed ("Start Speed", Range(0, 2)) = 0.25
        _Accel ("Acceleration", Range(0, 4)) = 0.9
        _MaxSpeed ("Warp Speed", Range(0, 4)) = 2.2
        _Streak ("Streak Length", Range(0, 0.5)) = 0.12

        [Header(Look)]
        _StarSize ("Star Size (texels)", Range(0.5, 4)) = 1
        _StarColor ("Star Colour", Color) = (0.85, 0.92, 1, 1)
        _Brightness ("Brightness", Range(0, 1)) = 1
        [IntRange] _Levels ("Brightness Levels", Range(2, 16)) = 5
        _Background ("Background", Color) = (0, 0, 0, 1)

        // Written by MystifyScreensaver.cs on its runtime copy of the material, never on the asset.
        [HideInInspector] _Seed ("Seed", Float) = 1
        [HideInInspector] _Clock ("Clock", Float) = 0
        [HideInInspector] _TargetSize ("Target Size", Vector) = (640, 360, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "PreviewType" = "Plane" }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Starfield"

        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Integer hashing.
            #pragma target 3.5

            #include "UnityCG.cginc"

            #define MAX_STARS 256
            #define NEAREST_DEPTH 0.03

            float _Stars, _Spread;
            float _Speed, _Accel, _MaxSpeed, _Streak;
            float _StarSize, _Brightness, _Levels;
            float4 _StarColor, _Background;
            float _Seed, _Clock;
            float4 _TargetSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Same PCG hash as Mystify: one seed fixes the whole field.
            uint Pcg(uint v)
            {
                uint state = v * 747796405u + 2891336453u;
                uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                return (word >> 22u) ^ word;
            }

            float Rand(uint seed, uint key)
            {
                return Pcg(seed ^ Pcg(key)) * (1.0 / 4294967295.0);
            }

            // Speed ramps from _Speed to _MaxSpeed at _Accel, then holds.
            float SpeedAt(float t)
            {
                return min(_Speed + _Accel * t, max(_Speed, _MaxSpeed));
            }

            // Integral of SpeedAt from 0 to t.
            float TravelAt(float t)
            {
                float top = max(_Speed, _MaxSpeed);
                if (_Accel <= 0.0) return _Speed * t;

                float rampEnd = (top - _Speed) / _Accel;
                if (t <= rampEnd) return _Speed * t + 0.5 * _Accel * t * t;
                return _Speed * rampEnd + 0.5 * _Accel * rampEnd * rampEnd + top * (t - rampEnd);
            }

            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-5));
                return length(pa - ba * h);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 size = max(_TargetSize.xy, 1.0);
                float2 px = i.uv * size;
                float2 centre = size * 0.5;
                float radius = size.y * _Spread;

                float travel = TravelAt(_Clock);
                float speed = SpeedAt(_Clock);
                uint seed = (uint)_Seed;

                float lit = 0.0;

                [loop]
                for (uint s = 0u; s < MAX_STARS; s++)
                {
                    if (s >= (uint)_Stars) break;

                    uint key = s * 4u;
                    float angle = Rand(seed, key) * 6.2831853;
                    float2 dir = float2(cos(angle), sin(angle));
                    float rate = lerp(0.6, 1.4, Rand(seed, key + 1u));

                    // 1 = far (at the spawn radius), 0 = passing the viewer.
                    float depth = 1.0 - frac(Rand(seed, key + 2u) + travel * rate);
                    float head = max(depth, NEAREST_DEPTH);
                    // The tail is where the star was a moment ago: longer the faster the field goes.
                    float tail = min(head + _Streak * speed * rate * head, 1.0);

                    float2 headPx = centre + dir * radius / head;
                    float2 tailPx = centre + dir * radius / tail;

                    // Nearer stars are bigger and brighter; far ones are single dim texels.
                    float nearness = 1.0 - depth;
                    float width = _StarSize * (0.6 + nearness * 1.4);
                    float d = SegmentDistance(px, tailPx, headPx);

                    if (d < width) lit = max(lit, 0.25 + 0.75 * nearness * nearness);
                }

                // A few hard brightness steps, like a 16-colour palette, not a smooth glow.
                lit = floor(lit * _Levels) / _Levels;

                float3 colour = lerp(_Background.rgb, _StarColor.rgb * _Brightness, lit);
                return fixed4(colour, 1.0);
            }
        ENDCG
        }
    }
}
