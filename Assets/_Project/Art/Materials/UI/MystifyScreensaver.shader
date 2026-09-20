// MystifyScreensaver.shader
// The loading screen's background: the Windows 3.1 / 95 "Mystify" screensaver. A couple of polygons
// whose corners drift across the screen at constant speed, bouncing off the edges, each one trailing
// a stack of echoes of where it just was, all of it slowly changing colour.
//
// Not a UI shader. MystifyScreensaver.cs blits it into a small render texture (640x360 at 16:9) that
// a point-filtered RawImage stretches over the screen: the lines come out one texel wide and
// hard-edged — the staircase of a VGA screensaver — and the canvas's CRT tube then curves, dithers
// and scans them like the rest of the UI. Rendering small is also what keeps it cheap: every texel
// tests every edge of every echo.
//
// No state and no history buffer. A point moving at constant speed between two walls bounces in a
// triangle wave, so every corner's position is a closed-form function of time: tri(start + v * t).
// That gives the echoes for free — an echo is the same polygon at t - k * _EchoSpacing, coloured as
// it was then, the way the real screensaver left each line in the colour it was drawn with.
//
// Random per show: every start position, direction, speed and colour comes out of a hash of _Seed,
// which the script rolls each time the loading screen comes up, so no two loads show the same
// pattern. _Clock is the time since that roll, pushed by the script on the unscaled clock — the
// loading screen can start while Time.timeScale is still 0. Everything else is tuned here.

Shader "WIRED/UI/Mystify Screensaver"
{
    Properties
    {
        [Header(Shape)]
        [IntRange] _Polygons ("Polygons", Range(1, 4)) = 2
        [IntRange] _Corners ("Corners per Polygon", Range(3, 6)) = 4

        [Header(Trail)]
        [IntRange] _Echoes ("Lines per Polygon (itself + echoes)", Range(1, 16)) = 6
        _EchoSpacing ("Time Between Echoes (s)", Range(0.01, 0.3)) = 0.07
        _EchoFade ("Dimming of the Oldest Echo", Range(0, 1)) = 0

        [Header(Motion)]
        // In screen heights per second, per axis. The real screensaver never moves a corner straight
        // along an axis: each one has its own horizontal and vertical speed, both in this range.
        _SpeedMin ("Min Corner Speed", Range(0, 1)) = 0.1
        _SpeedMax ("Max Corner Speed", Range(0, 1)) = 0.28
        _Margin ("Edge Margin", Range(0, 0.2)) = 0.02

        [Header(Look)]
        _LineWidth ("Line Width (texels)", Range(0.5, 4)) = 1
        _ColorCycle ("Seconds per Colour Change", Range(0.25, 20)) = 2.5
        // The colours are not the screensaver's full wheel: they are rolled inside one band of it.
        // 0 is red, 1/12 amber, 1/6 yellow; the band wraps, so a centre near 0 reaches back into
        // crimson. Width is capped at half the wheel so the shortest way between two hues in the
        // band never leaves it.
        _HueCenter ("Hue Centre (0 red, .04 orange, .08 amber)", Range(0, 1)) = 0.04
        _HueRange ("Hue Spread", Range(0, 0.5)) = 0.14
        _Saturation ("Saturation", Range(0, 1)) = 1
        _Brightness ("Brightness", Range(0, 1)) = 1
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
            Name "Mystify"

        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Integer hashing.
            #pragma target 3.5

            #include "UnityCG.cginc"

            // Upper bounds of the Properties' ranges; the loops break on the material's own counts.
            #define MAX_POLYGONS 4
            #define MAX_CORNERS  6
            #define MAX_ECHOES   16

            float _Polygons, _Corners;
            float _Echoes, _EchoSpacing, _EchoFade;
            float _SpeedMin, _SpeedMax, _Margin;
            float _LineWidth, _ColorCycle, _Saturation, _Brightness;
            float _HueCenter, _HueRange;
            float4 _Background;
            float _Seed, _Clock;
            // (width, height) of the render texture in texels. Pushed by the script: a Blit outside a
            // camera does not set _ScreenParams.
            float4 _TargetSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;   // full float: vert_img's half2 would round texel centres
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // PCG hash: every random number here is hash(seed, key), so one seed fixes the whole pattern.
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

            // 0 -> 1 -> 0 over every 2 units: where a point moving at constant speed between walls at
            // 0 and 1 is. Fine with negative input (echoes before the first frame).
            float2 Bounce(float2 x)
            {
                return 1.0 - abs(1.0 - 2.0 * frac(x * 0.5));
            }

            float3 HueToRgb(float h)
            {
                return saturate(abs(frac(h + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            // A rolled 0..1 number placed inside the allowed band of the wheel, wrapping at 1.
            float BandHue(float r)
            {
                return frac(_HueCenter + (r - 0.5) * _HueRange);
            }

            // A polygon's colour at time t: it drifts from one random hue to the next every
            // _ColorCycle seconds, the short way round the wheel so it never greys out halfway.
            // Both hues come out of the band, and the band is under half the wheel wide, so the
            // short way between them stays inside it: nothing ever passes through green or blue.
            float3 PolygonColour(uint seed, uint polygon, float t)
            {
                // Offset per polygon, so they do not all turn at the same moment.
                float phase = t / _ColorCycle + Rand(seed, 7000u + polygon) * 16.0;
                float step = floor(phase);
                float blend = smoothstep(0.0, 1.0, phase - step);

                uint key = 8000u + polygon * 65536u + (uint)step;
                float from = BandHue(Rand(seed, key));
                float to = BandHue(Rand(seed, key + 1u));
                float hue = from + (frac(to - from + 0.5) - 0.5) * blend;

                return lerp(1.0, HueToRgb(hue), _Saturation) * _Brightness;
            }

            float SegmentDistance(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 size = max(_TargetSize.xy, 1.0);
                float aspect = size.x / size.y;
                float2 p = i.uv * size;   // in texels; lands on texel centres

                uint seed = (uint)_Seed;
                int polygons = (int)_Polygons;
                int corners = clamp((int)_Corners, 3, MAX_CORNERS);
                int echoes = (int)_Echoes;
                float halfWidth = _LineWidth * 0.5;

                float3 lit = 0.0;
                bool hit = false;

                [loop]
                for (int polygon = 0; polygon < MAX_POLYGONS; polygon++)
                {
                    if (polygon >= polygons) break;

                    // Each corner's start and velocity, once per polygon — every echo shares them.
                    float2 start[MAX_CORNERS];
                    float2 velocity[MAX_CORNERS];
                    [unroll]
                    for (int c = 0; c < MAX_CORNERS; c++)
                    {
                        uint key = ((uint)polygon * 16u + (uint)c) * 8u;
                        start[c] = float2(Rand(seed, key), Rand(seed, key + 1u));
                        float2 speed = lerp(_SpeedMin, _SpeedMax, float2(Rand(seed, key + 2u), Rand(seed, key + 3u)));
                        float2 direction = float2(Rand(seed, key + 4u) < 0.5 ? -1.0 : 1.0,
                                                  Rand(seed, key + 5u) < 0.5 ? -1.0 : 1.0);
                        // Speeds are in screen heights: x is divided by the aspect so a corner covers
                        // the same distance on screen whichever way it goes.
                        velocity[c] = direction * speed * float2(1.0 / aspect, 1.0);
                    }

                    [loop]
                    for (int echo = 0; echo < MAX_ECHOES; echo++)
                    {
                        if (echo >= echoes) break;

                        float t = _Clock - echo * _EchoSpacing;
                        // The trail builds up from the first line, as when the screensaver kicks in.
                        if (t < 0.0) break;

                        float2 points[MAX_CORNERS];
                        float2 lo = size;
                        float2 hi = 0.0;
                        [unroll]
                        for (int k = 0; k < MAX_CORNERS; k++)
                        {
                            points[k] = lerp(_Margin, 1.0 - _Margin, Bounce(start[k] + velocity[k] * t)) * size;
                            if (k < corners)
                            {
                                lo = min(lo, points[k]);
                                hi = max(hi, points[k]);
                            }
                        }

                        // Most texels are nowhere near a given echo: skip its edges outright.
                        if (any(p < lo - halfWidth) || any(p > hi + halfWidth)) continue;

                        bool onEdge = false;
                        [loop]
                        for (int e = 0; e < MAX_CORNERS; e++)
                        {
                            if (e >= corners) break;
                            int next = e + 1 < corners ? e + 1 : 0;
                            if (SegmentDistance(p, points[e], points[next]) <= halfWidth)
                            {
                                onEdge = true;
                                break;
                            }
                        }
                        if (!onEdge) continue;

                        float dim = 1.0 - _EchoFade * echo / max(echoes - 1.0, 1.0);
                        lit = max(lit, PolygonColour(seed, (uint)polygon, t) * dim);
                        hit = true;
                    }
                }

                // The line colours are worked out as on-screen (sRGB) values. _Background needs no
                // conversion: Unity already hands Color properties over in the working colour space.
                #if !defined(UNITY_COLORSPACE_GAMMA)
                lit = GammaToLinearSpace(lit);
                #endif
                return float4(hit ? lit : _Background.rgb, 1.0);
            }
        ENDCG
        }
    }

    Fallback Off
}
