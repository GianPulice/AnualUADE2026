// VisionFog_HLSL.shader
// Fullscreen post-process HLSL para URP FullScreenPassRendererFeature (feature "Vision Fog"
// en Settings/PC_Renderer.asset, injection point 550 = BeforeRenderingPostProcessing).
//
// Reemplaza a Fullscreen_VisionFog.shadergraph + VisionFog.hlsl. Conserva los mismos
// reference names de material (_EnableVisionFog, _FogNoiseScale, _FogNoiseIntensity,
// _FogScrollSpeed) para que VisionFog.mat, VisionFog_SilentHill.mat y SO_PostProcessToggle
// sigan funcionando sin tocar nada.
//
// ── POR QUE SE REESCRIBIO ──────────────────────────────────────────────────
// El grafo tenia tres problemas que no se arreglaban con valores:
//
// 1) El color de niebla se aplicaba con lerp(escena, fogColor, f). A f=1 la pantalla ES
//    fogColor, un relleno plano: no existia "oscuro pero con un dejo de azul". Aca se usa
//    la separacion clasica extincion / in-scattering (Beer-Lambert, cf. iquilezles.org/
//    articles/fog): la extincion se come la escena hacia el negro, el in-scattering inyecta
//    color, y son dos perillas independientes.
//
// 2) La preservacion de luces hacia saturate(luminance * k) sobre un buffer HDR ANTES del
//    tonemap. Una pared iluminada da luminancia 2.0, un emissive 10.0, asi que saturate()
//    devolvia 1.0 y la niebla desaparecia del todo. Aca la luminancia se normaliza primero,
//    pasa por umbral + rodilla, se atenua con la distancia y tiene techo.
//
// 3) El skybox no se estaba nublando en PC: el grafo hacia step(0.9999, rawDepth), pero con
//    reversed-Z (D3D/Vulkan) el far plane es 0, no 1. Aca se usa UNITY_REVERSED_Z.
//
// ── UNIFORMS ───────────────────────────────────────────────────────────────
// Las que setea VisionRangeController por Shader.SetGlobalXxx NO van en el bloque Properties
// a proposito: si una property existe en el material, el valor del material le gana al global
// y el controller dejaria de tener efecto. Van declaradas mas abajo, fuera de UnityPerMaterial.
//
// OJO con los colores: Shader.SetGlobalColor NO convierte de gamma a lineal (la doc dice
// literal que es un alias de SetGlobalVector). El proyecto esta en Linear, asi que el
// controller manda los colores ya convertidos con .linear. No los conviertas otra vez aca.
//
// ── ESPEJO EN C# ───────────────────────────────────────────────────────────
// VisionFogState.EvaluateSurface() / VisibilityAt() / OpticalDepthAt() reimplementan la
// matematica de Frag() para dibujar la previsualizacion del inspector sin entrar en Play.
// Si cambias el modelo aca (el orden extincion/in-scattering, la rampa, la mascara de la luz
// del player), cambialo alla tambien: una preview que miente es peor que no tener preview.

Shader "Hidden/Custom/VisionFogHLSL"
{
    Properties
    {
        // Master on/off. Lo lee SO_PostProcessToggle con GetFloat/SetFloat, asi que tiene que
        // seguir siendo un Float. [ToggleUI] y no [Toggle] a proposito: da el mismo checkbox y
        // el mismo float, pero no genera un keyword que ningun variant declara — que es lo que
        // deja keywords "invalidos" serializados en el .mat.
        [ToggleUI] _EnableVisionFog        ("Enable Vision Fog (master)", Float) = 1

        [Header(Noise)]
        [Toggle] _EnableFogNoise           ("Enable Noise", Float) = 1
        _FogNoiseScale                     ("Noise Scale (world)", Float) = 0.05
        _FogNoiseIntensity                 ("Noise Intensity", Range(0, 1)) = 0.4
        _FogScrollSpeed                    ("Noise Scroll Speed", Float) = 0.1
        _FogNoiseOctaves                   ("Noise Octaves", Range(1, 4)) = 3

        [Header(Blur)]
        // El blur del grafo eran 4 taps fijos: a blurStrength 0.005 era imperceptible.
        // Aca son 13 taps en dos anillos y el radio escala con la densidad de niebla.
        [Toggle] _EnableFogBlur            ("Enable Blur", Float) = 1

        [Header(Debug)]
        // Para tunear los presets sin adivinar. Ninguno de estos modos es un look final.
        // Nombres sin espacios a proposito: el drawer de [Enum] parte la lista por comas y un
        // nombre con espacio queda con el espacio adentro del label.
        [Enum(Off, 0, Transmittance, 1, OpticalDepth, 2, LightMask, 3, Inscatter, 4, BypassMask, 5, Distance, 6)]
        _FogDebugView                      ("Debug View", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // Tiene que coincidir con VisionRangeController.MaxBypassZones.
    #define VISION_FOG_MAX_BYPASS 16

    CBUFFER_START(UnityPerMaterial)
        float _EnableVisionFog;

        float _EnableFogNoise;
        float _FogNoiseScale;
        float _FogNoiseIntensity;
        float _FogScrollSpeed;
        float _FogNoiseOctaves;

        float _EnableFogBlur;

        float _FogDebugView;
    CBUFFER_END

    // ── Globals del VisionRangeController ──────────────────────────────────
    // Fuera de UnityPerMaterial: son globals, no properties de material.
    float3 _PlayerPos;
    float  _VisionStart;
    float  _VisionEnd;

    float  _FogDensity;         // profundidad optica acumulada al llegar a _VisionEnd
    float  _FogFalloffPower;    // reshape de la rampa antes de la exponencial
    float  _FogDarkness;        // 0..1 — escala global de la extincion
    float3 _FogExtinctionTint;  // pesos por canal de la extincion (blanco = neutro)

    float3 _FogColor;           // color del in-scattering, YA en lineal
    float  _FogInscatterStrength;

    float  _LightPreservation;
    float  _LightThreshold;
    float  _LightKnee;
    float  _MaxLightPreservation;
    float  _LightDistanceFalloff;

    float  _VisionFogBlurStrength;

    float3 _PlayerLightPosition;
    float  _PlayerLightRange;
    float  _PlayerLightClear;      // cuanta niebla disuelve (0..1)
    float  _PlayerLightFalloff;    // exponente de la curva de caida
    float3 _PlayerLightColor;      // YA en lineal
    float  _PlayerLightTint;       // 0..1 — cuanto MULTIPLICA la escena (oscurece + tine)
    float  _PlayerLightInjection;  // cuanta luz SUMA (glow dentro de la niebla)

    float  _FogBypassFalloff;
    float  _FogBypassPlayerFade;   // 0..1 — cuanto apaga la luz del modulo del player al glow del bypass
    float4 _FogLightBypassData[VISION_FOG_MAX_BYPASS];   // xyz = world pos, w = radio
    float4 _FogLightBypassColor[VISION_FOG_MAX_BYPASS];  // rgb = color*intensidad (lineal), a = clear 0..1
    float4 _FogLightBypassAxis[VISION_FOG_MAX_BYPASS];   // xyz = eje del cono (norm), w = cos(medio angulo); w >= 2 => esfera
    int    _FogLightBypassCount;

    // ── Noise procedural ───────────────────────────────────────────────────

    float vfHash(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float vfValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float a = vfHash(i);
        float b = vfHash(i + float2(1, 0));
        float c = vfHash(i + float2(0, 1));
        float d = vfHash(i + float2(1, 1));
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    // FBM con cantidad de octavas variable. En el grafo estaban fijas en 3 y siempre se
    // pagaban; aca 1 octava alcanza para niebla suave y cuesta un tercio.
    float vfFBM(float2 p, int octaves)
    {
        float v = 0.0;
        float amp = 0.5;
        [loop]
        for (int i = 0; i < octaves; i++)
        {
            v   += amp * vfValueNoise(p);
            p   *= 2.0;
            amp *= 0.5;
        }
        return v;
    }

    // Devuelve un multiplicador de densidad alrededor de 1.0 — asi el noise reparte niebla
    // sin cambiar la densidad promedio del preset.
    float vfDensityNoise(float3 worldPos)
    {
        #ifndef _ENABLEFOGNOISE_ON
            return 1.0;
        #else
            if (_FogNoiseIntensity < 0.001) return 1.0;

            int octaves = (int)clamp(_FogNoiseOctaves, 1.0, 4.0);
            float2 uv1 = worldPos.xz * _FogNoiseScale       + _Time.y * _FogScrollSpeed * float2( 1.0,  0.7);
            float2 uv2 = worldPos.xz * _FogNoiseScale * 1.7 + _Time.y * _FogScrollSpeed * float2(-0.6,  1.0);
            float n = (vfFBM(uv1, octaves) + vfFBM(uv2, octaves)) * 0.5;
            return lerp(1.0 - _FogNoiseIntensity * 0.5, 1.0 + _FogNoiseIntensity * 0.5, n);
        #endif
    }

    // ── Blur ───────────────────────────────────────────────────────────────
    // Dos anillos: 4 taps internos + 8 externos, mas el centro. El grafo tenia 4 taps fijos
    // en cruz, que a radios chicos ni se notaban y a radios grandes se veian como una cruz.
    static const float2 kFogBlurTaps[12] =
    {
        float2( 0.500,  0.000), float2( 0.000,  0.500),
        float2(-0.500,  0.000), float2( 0.000, -0.500),
        float2( 1.000,  0.000), float2( 0.707,  0.707),
        float2( 0.000,  1.000), float2(-0.707,  0.707),
        float2(-1.000,  0.000), float2(-0.707, -0.707),
        float2( 0.000, -1.000), float2( 0.707, -0.707)
    };

    half3 vfBlurScene(float2 uv, float radius)
    {
        // El disco se corrige por aspect ratio: sin esto queda ovalado en 16:9.
        float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
        float2 scale = float2(radius / aspect, radius);

        // El centro pesa doble para no perder del todo la silueta de lo que hay atras.
        half3 acc = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 2.0;
        float weight = 2.0;

        [unroll]
        for (int i = 0; i < 12; i++)
        {
            float2 sampleUV = saturate(uv + kFogBlurTaps[i] * scale);
            acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb;
            weight += 1.0;
        }
        return acc / weight;
    }

    // ── Mascaras de luz ────────────────────────────────────────────────────

    // Cuanto perfora la niebla la luz del modulo del player.
    float vfPlayerLightMask(float3 worldPos)
    {
        if (_PlayerLightRange <= 0.001) return 0.0;

        float d = distance(worldPos, _PlayerLightPosition);
        float m = saturate(1.0 - d / _PlayerLightRange);
        return pow(m, max(_PlayerLightFalloff, 0.01));
    }

    // Recorre los FogLightBypass activos (farolas, hogueras, paneles). Devuelve dos cosas
    // distintas y por eso no se puede resolver con un solo acumulador:
    //   clear — cuanta niebla disuelve. Se combina con MAX: dos focos superpuestos no
    //           limpian el doble, se tratan como uno solo.
    //   light — cuanta luz tenida INYECTA. Esa si se suma: dos lamparas iluminan mas que una.
    void vfBypassZones(float3 worldPos, out float clearAmount, out float3 injectedLight)
    {
        clearAmount   = 0.0;
        injectedLight = 0.0;

        int count = min(_FogLightBypassCount, VISION_FOG_MAX_BYPASS);
        float falloff = max(_FogBypassFalloff, 0.01);

        [loop]
        for (int i = 0; i < count; i++)
        {
            float4 zone = _FogLightBypassData[i];
            if (zone.w <= 0.001) continue;

            float3 toP  = worldPos - zone.xyz;
            float  dist = length(toP);
            float  m = saturate(1.0 - dist / zone.w);
            m = pow(m, falloff);

            // Cono: recorta el charco esferico por angulo respecto del eje. w >= 2 marca "esfera"
            // y se saltea. Para una Spot el eje es su forward y el angulo su Spot Angle.
            float4 ax = _FogLightBypassAxis[i];
            if (ax.w < 1.5)
            {
                float3 dir  = dist > 1e-4 ? toP / dist : ax.xyz;
                float  cosP = dot(dir, ax.xyz);
                m *= smoothstep(ax.w, lerp(ax.w, 1.0, 0.25), cosP);
            }

            float4 tint = _FogLightBypassColor[i];
            clearAmount    = max(clearAmount, m * tint.a);
            injectedLight += tint.rgb * m;
        }
    }

    // Cuanto "perfora" la niebla el brillo que ya tiene la escena.
    //
    // Sobre buffer HDR pre-tonemap la luminancia NO esta en 0..1 (un emissive da 10), asi que
    // primero se comprime con Reinhard. Despues umbral + rodilla para que perfore una lampara
    // y no una pared apenas iluminada, caida con la distancia para que un foco lejano no se
    // vea igual de nitido que uno a tres metros, y techo para que nunca limpie del todo.
    float vfLightPreservation(half3 sceneColor, float distFromPlayer)
    {
        if (_LightPreservation <= 0.001) return 0.0;

        // Rec.709: el buffer es lineal. El 0.299/0.587/0.114 del grafo es luma de gamma.
        float lum   = dot(sceneColor, half3(0.2126, 0.7152, 0.0722));
        float lNorm = lum / (1.0 + lum);

        float k = smoothstep(_LightThreshold, _LightThreshold + max(_LightKnee, 0.001), lNorm);
        k *= exp(-distFromPlayer * _LightDistanceFalloff);

        return min(k * _LightPreservation, _MaxLightPreservation);
    }

    // ── Fragment ───────────────────────────────────────────────────────────

    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;
        half3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

        if (_EnableVisionFog < 0.5) return half4(sceneColor, 1.0);

        // Early-out: el controller pone _VisionEnd = 0 cuando no hay player (Main Menu,
        // LevelUI aislado, escena de gameplay todavia sin cargar).
        if (_VisionEnd <= _VisionStart + 0.001) return half4(sceneColor, 1.0);

        // ── Posicion world del pixel ───────────────────────────────────────
        float rawDepth = SampleSceneDepth(uv);
        float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

        // El skybox no tiene geometria: su worldPos reconstruida es basura, asi que se marca
        // aparte y se hunde en niebla al maximo.
        // Con reversed-Z (D3D/Vulkan/Metal) el far plane es 0, no 1 — el grafo comparaba
        // contra 1 y por eso en PC el cielo nunca se nublaba.
        #if UNITY_REVERSED_Z
            float skyMask = step(rawDepth, 1e-6);
        #else
            float skyMask = step(1.0 - 1e-6, rawDepth);
        #endif

        // Distancia horizontal (xz): niebla cilindrica desde el player, no esferica. Con
        // esferica, mirar hacia arriba o hacia abajo cambiaria la visibilidad y en un nivel
        // con desnivel se nota como un error.
        float distFromPlayer = distance(worldPos.xz, _PlayerPos.xz);

        // ── Profundidad optica ─────────────────────────────────────────────
        // Rampa normalizada dentro de la banda start..end.
        float t = saturate((distFromPlayer - _VisionStart) / max(_VisionEnd - _VisionStart, 1e-4));

        // Reshape de la rampa. OJO: para t en [0,1], power > 1 da MENOS niebla cerca y un
        // cierre mas brusco al final; power < 1 la adelanta. El comentario del shader viejo
        // (y el tooltip del SO) decian lo contrario.
        t = pow(t, max(_FogFalloffPower, 0.01));

        // Beer-Lambert: la densidad optica es lo que se integra, no el resultado. Por eso el
        // noise multiplica ACA y no al color final — modula cuanta niebla hay, no cuanto se ve.
        float opticalDepth = t * max(_FogDensity, 0.0) * vfDensityNoise(worldPos);

        // ── Lo que abre huecos en la niebla ────────────────────────────────
        float preservation = vfLightPreservation(sceneColor, distFromPlayer);

        float  bypassClear;
        float3 bypassLight;
        vfBypassZones(worldPos, bypassClear, bypassLight);

        float playerMask  = vfPlayerLightMask(worldPos);
        float playerClear = saturate(playerMask * _PlayerLightClear);

        // Los tres reducen densidad optica, no el color final: asi la luz que atraviesa la
        // niebla sigue teniendo algo de niebla encima en vez de abrir un agujero limpio.
        float clearTotal = saturate(max(max(preservation, saturate(bypassClear)), playerClear));
        opticalDepth *= 1.0 - clearTotal;

        // El cielo va al final, DESPUES de los clears: su worldPos reconstruida cae sobre el far
        // plane, asi que las mascaras de distancia dan 0 de por si, pero la preservacion de luces
        // mira la luminancia del pixel y un skybox brillante se abriria un hueco en la niebla.
        // Forzarlo aca deja el cielo siempre hundido, que es lo que un area de oscuridad quiere.
        opticalDepth = lerp(opticalDepth, max(_FogDensity, 0.0) * 4.0, skyMask);

        // El glow del bypass se difumina donde el player ya ilumina. El player lleva su luz de
        // modulo encima; cuando una bypass zone se solapa con ese radio su inyeccion se SUMA a
        // lo que la luz del player ya puso, y lo que este parado ahi (tipicamente el Nemesis) se
        // sobre-ilumina. Atenuamos la luz inyectada -no el clear, que sigue con MAX- en
        // proporcion a cuanto de este pixel ya cae dentro de la luz del player. Con
        // _FogBypassPlayerFade = 0 no hace nada (comportamiento viejo); con 1 el halo del bypass
        // se apaga del todo en el centro de la luz del player.
        bypassLight *= 1.0 - saturate(playerMask) * saturate(_FogBypassPlayerFade);

        // ── Luz que los focos meten DENTRO de la niebla ────────────────────
        // Esto es lo que hace que una lampara se lea como un halo en la oscuridad en vez de
        // como un agujero recortado. Se suma a la escena ANTES de la extincion para que la
        // niebla que queda entre el foco y la camara tambien la atenue.
        sceneColor += bypassLight;
        sceneColor += _PlayerLightColor * (playerMask * _PlayerLightInjection);

        // Tinte multiplicativo de la luz del player. Ademas de tenir OSCURECE, porque un color
        // saturado aplasta los canales que no son el suyo (rojo puro deja la luminancia en
        // ~34%). Eso es lo que mantiene oscuro el entorno dentro del radio. Se conserva como
        // opcion (_PlayerLightTint) porque es el look que tenia el shader viejo, pero ahora se
        // puede bajar a 0 y usar solo la extincion, que es mas predecible.
        float tintAmount = saturate(playerMask * _PlayerLightTint);
        sceneColor = lerp(sceneColor, sceneColor * _PlayerLightColor, tintAmount);

        // ── Extincion + in-scattering ──────────────────────────────────────
        // La clave del rediseno. En vez de lerp(escena, fogColor, f), que a f=1 pinta la
        // pantalla del color plano de la niebla:
        //   - transmittance: cuanto sobrevive de la escena. Va a NEGRO, no al fogColor.
        //   - inscatter:     cuanto color de niebla se inyecta, con su propia perilla.
        // Con _FogInscatterStrength = 0 y _FogDarkness = 1 se obtiene oscuridad pura sin
        // importar que color tenga _FogColor. Subiendolo de a poco se consigue "oscuro con
        // un dejo de azul" en vez de saltar directo al gris plano.
        float3 extinction    = max(_FogExtinctionTint, 0.0) * saturate(_FogDarkness);
        float3 transmittance = exp(-opticalDepth * extinction);
        float  inscatter     = (1.0 - exp(-opticalDepth)) * saturate(_FogInscatterStrength);

        // ── Blur ───────────────────────────────────────────────────────────
        // Lejos no solo se oscurece: se pierde nitidez. El radio escala con cuanta niebla hay
        // ya calculada, asi que la linterna y los bypass limpian el blur al mismo tiempo que
        // limpian el color, sin dejar un halo borroso donde ya no hay niebla.
        #ifdef _ENABLEFOGBLUR_ON
        if (_VisionFogBlurStrength > 0.00001)
        {
            float fogAmount = 1.0 - dot(transmittance, float3(0.2126, 0.7152, 0.0722));
            sceneColor = lerp(sceneColor, vfBlurScene(uv, _VisionFogBlurStrength * fogAmount), fogAmount);
        }
        #endif

        half3 result = sceneColor * transmittance + _FogColor * inscatter;

        // ── Debug views ────────────────────────────────────────────────────
        if (_FogDebugView > 0.5)
        {
            int mode = (int)round(_FogDebugView);
            if (mode == 1) result = transmittance;
            if (mode == 2) result = saturate(opticalDepth / max(_FogDensity, 0.001)).xxx;
            if (mode == 3) result = half3(preservation, 0, 0);
            if (mode == 4) result = _FogColor * inscatter;
            if (mode == 5) result = half3(saturate(bypassClear), playerClear, 0);
            if (mode == 6) result = frac(distFromPlayer / 10.0).xxx;
        }

        return half4(result, 1.0);
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
            Name "VisionFogHLSL"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            // Los toggles que valen un variant son los que tienen costo real: el noise son
            // hasta 8 samples de value noise y el blur son 13 samples de textura. Apagados
            // desde el material, ni se compilan.
            #pragma shader_feature_local_fragment _ _ENABLEFOGNOISE_ON
            #pragma shader_feature_local_fragment _ _ENABLEFOGBLUR_ON
            ENDHLSL
        }
    }

    Fallback Off
}
