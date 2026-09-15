// AnimatedSurface.shader
// A UI surface that is never quite still: the Image's own colour (the UI theme token, through
// UIThemeApplier) with a faint drifting grid, slow noise and a soft diagonal sweep laid over it.
// It gives the panels the "live screen" feel without adding a single node to the hierarchy.
//
// Everything is locked to the SCREEN, at the 1080p reference, rather than to each panel: neighbouring
// panels share one continuous pattern, and it keeps its size at any resolution.
//
// Premultiplied output (Blend One OneMinusSrcAlpha), so it is correct both drawn straight to the
// screen and drawn into CanvasCRTPresenter's texture.
//
// Animated with _UnscaledTime (pushed by UnscaledShaderTime), never _Time: Unity's _Time follows
// Time.timeScale, and these panels are the pause, settings and inventory screens — all shown paused.

Shader "WIRED/UI/Animated Surface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Pattern)]
        _PatternColor ("Pattern Colour (alpha = overall amount)", Color) = (0.35, 0.25, 0.25, 0.25)

        [Header(Grid)]
        _GridSize ("Cell (px at 1080p)", Float) = 32
        _GridLine ("Line (px at 1080p)", Float) = 1
        _GridScroll ("Scroll (px per second, xy)", Vector) = (0, -6, 0, 0)
        _GridAmount ("Amount", Range(0, 1)) = 0.4

        [Header(Drift)]
        _NoiseScale ("Scale (px at 1080p)", Float) = 220
        _NoiseDrift ("Drift (per second, xy)", Vector) = (0.03, 0.02, 0, 0)
        _NoiseAmount ("Amount", Range(0, 1)) = 0.35

        [Header(Sweep)]
        _SweepAmount ("Amount", Range(0, 1)) = 0.5
        _SweepWidth ("Width (px at 1080p)", Float) = 180
        _SweepPeriod ("Period (s)", Float) = 7

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            int _UIVertexColorAlwaysGammaSpace;   // set by the canvas; not declared by UnityUI.cginc

            fixed4 _PatternColor;
            float _GridSize, _GridLine, _GridAmount;
            float4 _GridScroll;
            float _NoiseScale, _NoiseAmount;
            float4 _NoiseDrift;
            float _SweepAmount, _SweepWidth, _SweepPeriod;

            // Global, from UnscaledShaderTime. Keep it out of Properties: a material property of the same
            // name would shadow the global and freeze the pattern again.
            float _UnscaledTime;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash(i);
                float b = Hash(i + float2(1.0, 0.0));
                float c = Hash(i + float2(0.0, 1.0));
                float d = Hash(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // The inventory canvas has "Vertex Color Always In Gamma Space" on, so the theme colour
                // arrives in gamma. UI/Default converts it; a custom UI shader has to as well, or every
                // near-black token lands several shades too light — #0B0909 came out mid grey.
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = GammaToLinearSpace(v.color.rgb);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 col = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // Screen pixels, rescaled to the 1080p reference.
                float2 p = IN.vertex.xy * (1080.0 / _ScreenParams.y);

                // Drifting grid.
                float2 g = frac((p + _UnscaledTime * _GridScroll.xy) / _GridSize) * _GridSize;
                float grid = (g.x < _GridLine || g.y < _GridLine) ? 1.0 : 0.0;

                // Slow noise, two octaves.
                float2 q = p / _NoiseScale + _UnscaledTime * _NoiseDrift.xy;
                float noise = ValueNoise(q) * 0.65 + ValueNoise(q * 2.3 + 17.0) * 0.35;

                // A soft diagonal band crossing the screen once every _SweepPeriod seconds.
                float diagonal = (p.x + p.y) / 3000.0;
                float phase = frac(_UnscaledTime / _SweepPeriod) * 1.4 - 0.2;
                float sweep = 1.0 - smoothstep(0.0, _SweepWidth / 3000.0, abs(diagonal - phase));

                float pattern = saturate(grid * _GridAmount + noise * _NoiseAmount + sweep * _SweepAmount);
                col.rgb = lerp(col.rgb, _PatternColor.rgb, pattern * _PatternColor.a);

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                col.rgb *= col.a;
                return col;
            }
        ENDCG
        }
    }

    Fallback Off
}
