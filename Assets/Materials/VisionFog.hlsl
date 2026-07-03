// VisionFog.hlsl
// Custom Functions para usar desde un Fullscreen Shader Graph URP.
//
// Aplica un distance fog centrado en la posición del player (no en la cámara).
// El rango _VisionEnd se modula desde C# por VisionRangeController.cs según el
// nivel de luz ambiente — en zonas oscuras la niebla cierra más cerca, en
// zonas iluminadas se aleja.
//
// Properties globales que setea el controller:
//   _PlayerPos             (float3)  posición world del jugador
//   _VisionStart           (float)   distancia donde empieza la niebla
//   _VisionEnd             (float)   distancia donde la niebla cubre 100%
//   _FogColor              (float3)  color de la niebla (negro por default)
//   _LightPreservation     (float)   las zonas brillantes "perforan" la niebla
//   _FogDensityPower       (float)   curva de densidad — > 1 acelera el cierre (opresivo), < 1 lo suaviza
//   _PlayerLightPosition   (float3)  origen de la linterna del player en world-space
//   _PlayerLightRange      (float)   radio efectivo (metros) — si <= 0, la luz del player está apagada
//   _PlayerLightIntensity  (float)   fuerza del "hueco" que la linterna hace en la niebla (0..1+)
//   _PlayerLightColor      (float3)  reservado (no se usa para tintar — ver nota en VisionFog_float)
//   _FogLightBypassCount   (int)     cantidad efectiva de bypass zones (0..8)
//   _FogLightBypassData    (float4[])  xyz = world pos, w = radius (metros). Las luces adentro perforan.

#ifndef VISION_FOG_INCLUDED
#define VISION_FOG_INCLUDED

// ── Uniforms globales que declara el shader (setteadas desde C#) ────────────
// Se declaran aquí para que el HLSL las vea sin necesidad de exponer
// nuevos slots en el shadergraph. Todas son opcionales — el shader hace
// fallback si el controller no las setea.
float   _FogDensityPower;
float3  _PlayerLightPosition;
float   _PlayerLightRange;
float   _PlayerLightIntensity;
float3  _PlayerLightColor;

#define VISION_FOG_MAX_BYPASS 8
float4  _FogLightBypassData[VISION_FOG_MAX_BYPASS]; // xyz = pos, w = radius
int     _FogLightBypassCount;

// Reconstruye la posición world a partir de las UV pantalla y el depth raw.
// Usar el output de Sample Scene Depth (Raw) como input `depth`.
void WorldFromDepth_float(float2 uv, float depth, out float3 worldPos)
{
    float4 ndc = float4(uv * 2.0 - 1.0, depth, 1.0);
    #if UNITY_UV_STARTS_AT_TOP
        ndc.y = -ndc.y;
    #endif
    float4 worldH = mul(UNITY_MATRIX_I_VP, ndc);
    worldPos = worldH.xyz / worldH.w;
}

// ── Noise procedural (Value Noise + FBM) para densidad animada ─────────────

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
    float2 u = f * f * (3.0 - 2.0 * f); // smoothstep
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// FBM (Fractal Brownian Motion): sumas de noise en distintas frecuencias.
// Mejor look "nube" que un solo octave.
float vfFBM(float2 p)
{
    float v = 0.0;
    float amp = 0.5;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        v   += amp * vfValueNoise(p);
        p   *= 2.0;
        amp *= 0.5;
    }
    return v;
}

// Calcula qué tanto una linterna esférica "perfora" la niebla en este píxel.
// Distancia menor al range → factor 1 (sin niebla). Fuera del range → 0.
float vfPlayerLightMask(float3 worldPos)
{
    if (_PlayerLightRange <= 0.001) return 0.0;

    float d = distance(worldPos, _PlayerLightPosition);
    // Curva cuadrática — el hueco se siente "focal", no lineal.
    float mask = saturate(1.0 - d / _PlayerLightRange);
    mask = mask * mask;
    return mask * _PlayerLightIntensity;
}

// Acumula el efecto de todas las bypass zones activas (ej: farolas, hogueras).
// Cada bypass reduce fogFactor en su radio.
float vfBypassZonesMask(float3 worldPos)
{
    float acc = 0.0;
    int count = min(_FogLightBypassCount, VISION_FOG_MAX_BYPASS);
    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 bp = _FogLightBypassData[i];
        float r = bp.w;
        if (r <= 0.001) continue;
        float d = distance(worldPos, bp.xyz);
        float m = saturate(1.0 - d / r);
        m = m * m; // falloff cuadrático
        acc = max(acc, m);
    }
    return acc;
}

// Aplica el vision fog al color de escena.
// Distancia horizontal (xz) — niebla cilíndrica desde el player.
// Cambiar a distance(worldPos, playerPos) para niebla esférica si el nivel tiene desnivel vertical.
void VisionFog_float(
    float3 sceneColor,
    float3 worldPos,
    float  depth,
    float3 playerPos,
    float  visionStart,
    float  visionEnd,
    float3 fogColor,
    float  lightPreservation,
    float  noiseScale,       // ~0.05 — frecuencia del noise (más alto = "nubes" más chicas)
    float  noiseIntensity,   // 0–1 — qué tanto modula el fogFactor
    float  noiseScrollSpeed, // ~0.1 — velocidad del scroll en world space
    out float3 result)
{
    // Guard: si visionEnd <= visionStart, el controller no está activo todavía
    // (player en otra escena no cargada, Main Menu, LevelUI aislado, etc.).
    // Skip fog y devuelve la escena limpia.
    if (visionEnd <= visionStart + 0.001)
    {
        result = sceneColor;
        return;
    }

    float distFromPlayer = distance(worldPos.xz, playerPos.xz);

    // Banda lineal entre start y end. Saturate evita negativos antes de start.
    float fogFactor = saturate((distFromPlayer - visionStart) / (visionEnd - visionStart));

    // Curva de densidad — permite al diseñador acelerar el cierre para reforzar
    // la sensación de falta de visibilidad sin tocar visionEnd. Default = 1 (lineal).
    float densityPower = max(_FogDensityPower, 0.001);
    fogFactor = pow(fogFactor, densityPower);

    // Preservar zonas iluminadas: una luz brillante a 15m sigue siendo visible.
    // Patrón clásico de survival horror — las luces guían al jugador a través de la niebla.
    float luminance = dot(sceneColor, float3(0.299, 0.587, 0.114));
    fogFactor *= 1.0 - saturate(luminance * lightPreservation);

    // ── Bypass zones: puntos del mundo donde el diseñador quiere que la luz
    // atraviese la niebla incluso sin brightness en el buffer (ej: farolas
    // apagadas visualmente pero narrativamente iluminadas). ─────────────────
    float bypass = vfBypassZonesMask(worldPos);
    fogFactor *= 1.0 - saturate(bypass);

    // ── Linterna del player: crea un "hueco" en la niebla y agrega tinte. ────
    float playerLight = vfPlayerLightMask(worldPos);
    fogFactor *= 1.0 - saturate(playerLight);

    // ── Noise animado estilo Silent Hill ──────────────────────────────────
    // Dos capas de FBM en world-space scrolleando en direcciones distintas
    // → da sensación de "nubes" moviéndose dentro de la niebla.
    float2 noiseUV1 = worldPos.xz * noiseScale + _Time.y * noiseScrollSpeed * float2( 1.0,  0.7);
    float2 noiseUV2 = worldPos.xz * noiseScale * 1.7 + _Time.y * noiseScrollSpeed * float2(-0.6,  1.0);
    float n1 = vfFBM(noiseUV1);
    float n2 = vfFBM(noiseUV2);
    float noise = (n1 + n2) * 0.5; // ∈ [0,1] aprox

    // Modular el fogFactor: zonas con noise alto = más fog, zonas bajas = un poco menos.
    // Multiplicamos en torno a 1.0 para que el promedio no cambie la densidad global.
    float modulation = lerp(1.0 - noiseIntensity * 0.5, 1.0 + noiseIntensity * 0.5, noise);
    fogFactor = saturate(fogFactor * modulation);

    // Skybox (depth ~= 1) hundido en niebla — no se ve "el cielo" detrás del fog.
    float skyMask = step(0.9999, depth);
    fogFactor = lerp(fogFactor, 1.0, skyMask);

    float3 blended = lerp(sceneColor, fogColor, fogFactor);

    // NO sumamos tinte extra acá. La iluminación real (personaje, paredes, etc.)
    // ya viene de la Light de Unity que FogLightSource está leyendo — este shader
    // solo "perfora" la niebla (baja fogFactor) para revelar esa escena ya
    // iluminada. Sumar _PlayerLightColor encima duplicaba la luz (Light real +
    // tinte aditivo del post-fx) y sobre-exponía todo lo que quedaba dentro del
    // radio, sobre todo superficies claras como el personaje.
    result = blended;
}

#endif
