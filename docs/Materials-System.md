# Sistema de materiales, shaders y post-process — Guía técnica

Explica qué hace cada material, shader y script que armamos para el lenguaje visual del juego (WIRED). Pensado para que alguien que se suma pueda entender el flujo completo sin tener que reconstruirlo desde el código.

Stack: **URP 17.4.0**, Unity 6, Compatibility mode (Renderer Feature API tradicional).

> ⚠️ **Nota de idioma**: el código de `Assets/Scritps/` está íntegramente en inglés (comentarios,
> strings, logs y textos de UI). Este documento sigue en español. Ver `docs/CLAUDE.md` § Language rule.

---

## 1. Visión general

El sistema visual tiene cuatro capas que se ejecutan en este orden por frame:

```
[ Escena 3D (opaques + transparents) ]
         │
         ▼
[ SSAO ]                                          ← existente, no se tocó
         │
         ▼
[ Vision Fog (Fullscreen Shader Graph + HLSL) ]   ← EXTENDIDO (linterna, bypass, densidad)
         │
         ▼
[ PS1 PostProcess: PSX + dither + scanlines + CA ] ← REEMPLAZADO por shader HLSL
         │
         ▼
[ UI Overlay (Canvas Screen Space Overlay) ]      ← se dibuja después del pipeline
```

Cada capa tiene una responsabilidad clara y se puede prender/apagar independientemente.

**Reglas duras del spec** (`WIRED_Handoff_Code.docx`) que el sistema respeta:
- **El rojo (#CC1A1A) es exclusivo de peligro**. Solo aparece en luces de emergencia, indicadores de módulo explotado, trampas.
- **Estética PSX**: shaders mate, sin PBR realista. Smoothness baja, sin metallic.
- **Lenguaje sutil**: los items destacan por tinte+emisión casi imperceptible, NO por outlines marcados ni waypoints. (Ver §5.4 — existe un outline opt-in que viene **apagado** por default justamente para respetar esta regla.)

---

## 2. Mapa de archivos

```
Assets/
├─ Materials/
│   ├─ Environment/
│   │   ├─ Emergency/
│   │   │   └─ mat_luz_emergencia_emissive.mat       ← #CC1A1A baked
│   │   ├─ Monitor/
│   │   │   └─ mat_monitor_pantalla.mat              ← #8AB4D4 realtime + flicker
│   │   ├─ Device/
│   │   │   └─ mat_device_luz_ambar_jugador.mat      ← #FFC850 realtime
│   │   └─ Fluorescent/
│   │       └─ shader_flicker_light.shader            ← HLSL custom URP unlit
│   ├─ Items/
│   │   ├─ mat_item_keys.mat                          ← Llaves        (#37474F)
│   │   ├─ mat_item_components.mat                    ← Componentes   (#4E342E)
│   │   ├─ mat_item_clues.mat                         ← Pistas        (#263238)
│   │   ├─ mat_item_special.mat                       ← Especiales    (#1A237E)
│   │   ├─ ItemPSX.shadergraph                        ← original (referencia, no se toca)
│   │   ├─ ItemPSX_Outline.shader                     ← NUEVO: HLSL Lit + outline opt-in
│   │   └─ ItemPsx.mat                                ← apunta a ItemPSX_Outline.shader
│   ├─ VisionFog.hlsl                                  ← Custom function HLSL (EXTENDIDO)
│   └─ Post Process/
│       ├─ Fullscreen_VisionFog.shadergraph           ← grafo fullscreen del fog
│       ├─ VisionFog.mat                               ← material del fog fullscreen
│       ├─ PS1_PostProcess.shadergraph                 ← original (referencia, no se toca)
│       ├─ PS1_PostProcess_HLSL.shader                 ← NUEVO: PSX + dither/scanlines/CA
│       └─ PS1Effect.mat                               ← apunta a PS1_PostProcess_HLSL.shader
└─ Scritps/
    ├─ Environment/
    │   ├─ MonitorFlicker.cs                          ← pulso 0.2 Hz
    │   └─ FlickerLight.cs                            ← curve-driven, fluorescente
    ├─ Items/
    │   └─ ItemProximityHighlight.cs                  ← lerp tint+emission por proximidad
    ├─ ScriptableScripts/Rendering/
    │   └─ SO_VisionFogConfig.cs                       ← preset de niebla por zona
    └─ Rendering/
        ├─ VisionRangeController.cs                    ← setea globals del fog
        ├─ LightZone.cs                                ← trigger que pushea un config
        ├─ FogLightSource.cs                           ← NUEVO: linterna del player
        ├─ FogLightBypass.cs                           ← NUEVO: zonas que perforan niebla
        ├─ PS1EffectApplier.cs                         ← toggles dither/scanlines (PlayerPrefs)
        └─ GlitchController.cs                          ← NUEVO: glitch VHS aleatorio (CA)
```

---

## 3. Materiales del entorno

Cada uno respeta exactamente el hex del spec y usa **URP/Lit** (excepto el shader fluorescente custom).

### 3.1 `mat_luz_emergencia_emissive` — Luces rojas de pasillo

| Property | Valor | Por qué |
|---|---|---|
| Base Color | `#0D0000` casi negro | El material en sí no debe verse — solo emite. |
| Emission Color (HDR) | `(1.51, 0.0247, 0.0247)` = `#CC1A1A` intensity 1.32 | Spec: rojo emergencia. |
| Lightmap Flags | `2` (Baked) | Las luces de emergencia están fijas en el mundo → bakeable → más performance, mejor GI. |

**Cuándo aparece**: pasillos de emergencia del complejo, salidas. Spec sec 1.2 — "Son luces de emergencia industrial. Su función narrativa y de diseño es la misma: peligro/salida."

**Cómo funciona el "baked"**: la emisión contribuye al lightmap en el bake (Window → Rendering → Lighting → Generate Lighting). Las superficies cercanas reciben el tinte rojo sin necesidad de Point Lights reales corriendo en runtime.

### 3.2 `mat_monitor_pantalla` — Pantallas activas

| Property | Valor | Por qué |
|---|---|---|
| Base Color | `(0.2541, 0.4564, 0.6584)` = `#8AB4D4` linear | Spec: azul/blanco frío de monitores. |
| Emission Color | Mismo color, sin HDR boost | El brillo se anima desde script (no estático). |
| Lightmap Flags | `1` (Realtime) | El flicker anima la emisión → no bakeable. |
| Enable Instancing | ✅ | Multiple monitores en escena = un solo draw call. |

**Acompañado por** [`MonitorFlicker.cs`](../Assets/Scritps/Environment/MonitorFlicker.cs) — ver sec 4.1.

### 3.3 `mat_device_luz_ambar_jugador` — Encendedor del dispositivo del player

| Property | Valor | Por qué |
|---|---|---|
| Base Color | `(0.15, 0.09, 0.02)` ámbar oscuro | Spec: dispositivo del jugador. |
| Emission Color (HDR) | `(2.5, 1.5, 0.5)` = `#FFC850` intensity ~1.5 | Spec: ámbar cálido `#FFC850`. |
| Lightmap Flags | `1` (Realtime) | El device se mueve con el player → no bakeable. |

**Por qué realtime y no baked**: el player se mueve por el mundo, su luz tiene que recalcular shadows/lighting en cada frame. Una Point Light separada va con shadows = Soft, range = 3, intensity = 1 (configurable a futuro si meten degradación por módulos explotados — feature postergada).

> **Enganche con el fog**: esta misma Point Light es la que alimenta el "hueco" de niebla vía [`FogLightSource`](../Assets/Scritps/Rendering/FogLightSource.cs) (ver §6.4). Un solo sistema de luz del player, dos lecturas: iluminación real + campo de visión en la niebla.

### 3.4 `shader_flicker_light.shader` — Tubos fluorescentes

Único shader **HLSL custom** de entorno (no Shader Graph). Está en `Assets/Materials/Environment/Fluorescent/shader_flicker_light.shader`.

**Por qué custom**: el tubo fluorescente necesita brillar con su propia luz (es la fuente luminosa, no un objeto iluminado). URP/Lit sería overkill — el tubo no recibe luz importante, solo emite. Un shader unlit con `BaseColor * Intensity` alcanza.

**Estructura**:
- Pass `ForwardUnlit`: pinta `BaseColor * _Intensity` con fog mix (de URP).
- Pass `ShadowCaster`: permite que el tubo proyecte sombra si el level designer lo quiere.

**Properties**:
- `_BaseColor` (HDR) — color del tubo.
- `_Intensity` (Float) — se anima desde [`FlickerLight.cs`](../Assets/Scritps/Environment/FlickerLight.cs).

---

## 4. Sistema de flicker (animación de luces)

Dos scripts independientes, mismo principio: anima `_EmissionColor` o `_Intensity` por frame usando `MaterialPropertyBlock` para no instanciar el material (mantiene SRP Batcher activo).

### 4.1 `MonitorFlicker.cs`

Pulso sinusoidal sobre `_EmissionColor`.

```
intensidad(t) = lerp(minIntensity, maxIntensity, (sin(t * freq * 2π) + 1) / 2)
emisión = baseColor × intensidad
```

**Properties Inspector**:
- `baseEmission` — color HDR base.
- `minIntensity` / `maxIntensity` — rango del pulso (default 0.9 / 1.0).
- `flickerSpeed` — Hz. Spec: 0.2 (un ciclo cada 5 segundos).
- `flickerOffset` — segundos. Permite desfasar instancias para que no parpadeen sincronizadas.

**Patrón clave**: `Time.time + flickerOffset` para que sea **determinista**. Dos monitores con mismo offset parpadean igual; con offset distinto, desincronizados pero predecibles.

### 4.2 `FlickerLight.cs`

Similar pero usa una **AnimationCurve** (editable en Inspector) para dibujar el patrón de parpadeo exacto. Más control artístico que un sinusoide.

```
t = ((Time.time + flickerOffset) % cycleDuration) / cycleDuration  ∈ [0,1)
intensidad = flickerCurve.Evaluate(t) * maxIntensity
light.intensity = intensidad
material._Intensity = intensidad
```

**Diferencia con MonitorFlicker**:
- Pinta tanto la `Light` real como la propiedad `_Intensity` del material (el tubo brilla coordinadamente con la luz que emite).
- Soporta curvas arbitrarias (parpadeo rápido + estable, glitch + recovery, etc.).
- Default curve: parpadeo brusco en t=0.55 (simula fluorescente fallando).

**Cómo desincronizar instancias**: en cada prefab/instancia, setear `flickerOffset` a un valor distinto (ej: 0, 0.33, 0.66). Igual que MonitorFlicker, es determinista.

---

## 5. Sistema de items interactuables

Esto es el núcleo del lenguaje visual del juego. Spec sec 4.3: cada item tiene dos estados (lejano / próximo) que se transicionan suavemente cuando el player entra al radio de interacción.

### 5.1 Shader `ItemPSX_Outline` (HLSL URP/Lit)

**No es un shader fullscreen** — es un material que se aplica a los Renderers de los items.

Originalmente `ItemPSX` era un Shader Graph (URP/Lit). Se reemplazó por un **shader HLSL** (`Materials/Items/ItemPSX_Outline.shader`) que preserva todas las properties del grafo original y agrega el outline opt-in del §5.4. El `.shadergraph` original queda como referencia, sin usar. `ItemPsx.mat` apunta al shader nuevo.

**Passes**: `ForwardLit`, `ShadowCaster`, `DepthOnly`, `DepthNormals` — el objeto sigue proyectando sombras, escribiendo depth y alimentando SSAO como cualquier URP/Lit.

**Properties**:

| Nombre | Tipo | Descripción |
|---|---|---|
| `_BaseMap` | Texture2D | Textura base del item. |
| `_BaseColor` | Color | Color base del item (gris medio del entorno por default). |
| `_TintColor` | Color | Color de categoría (Llaves, Componentes, etc.). |
| `_TintIntensity` | Float 0–2 | Cuánto tinte de categoría se ve. **Lo modula el script.** |
| `_EmissionColor` | Color HDR | El mismo tinte pero brillará. |
| `_EmissionIntensity` | Float 0–10 | Brillo de emisión. **Lo modula el script.** |
| `_Smoothness` | Float 0–1 | Baja (0.0–0.3) → look mate industrial PSX. |
| `_OutlineColor` / `_OutlineIntensity` / `_OutlinePower` | ver §5.4 | Outline opt-in, **apagado** por default. |

**Cálculo del albedo (igual que el grafo original)**:
```
finalAlbedo   = lerp(_BaseColor, _TintColor, _TintIntensity) × sample(_BaseMap)
finalEmission = _EmissionColor × _EmissionIntensity + outline (§5.4)
```

Smoothness baja y sin metallic → look mate industrial PSX. Se aplica el filtro fullscreen PSX (§7) encima, que pixela y le da el carácter retro final.

### 5.2 Los 4 materiales preset (uno por categoría del spec)

| Material | Tinte (sRGB) | Smoothness | Items que la usan |
|---|---|---|---|
| `mat_item_keys` | `#37474F` gris azulado | 0.2 | Llave armario, Llave sala control |
| `mat_item_components` | `#4E342E` marrón cálido | 0.15 | Núcleo Energético, Núcleo Mecánico, Regulador de Presión |
| `mat_item_clues` | `#263238` gris azulado oscuro | 0.0 | Notas, planos, diario |
| `mat_item_special` | `#1A237E` azul intenso | 0.3 | Cortador de cadenas (item único) |

Todos arrancan con `_TintIntensity = 0.15` y `_EmissionIntensity = 0` → estado lejano default.

**Por qué estos hex y no otros**: están elegidos para mantener la **paleta fría y oscura** del spec (sec 4). La excepción es Componentes (cálido oscuro) que se diferencia narrativamente por tener "actividad mecánica interna".

### 5.3 `ItemProximityHighlight.cs`

MonoBehaviour que se pega al GameObject del item. Es la cabeza del sistema.

**Cuándo cambia los valores**:
- `OnPlayerEnteredRange()` → lerp de `0.15 → 0.4` (tint) y `0.0 → 0.2` (emission) en 0.3s.
- `OnPlayerExitedRange()` → lerp inverso.

**Cómo está implementado**:
- Coroutine con `SmoothStep` para que la transición sea sigmoide, no lineal. Hace que el "respirar" se sienta orgánico, no mecánico.
- `MaterialPropertyBlock` para escribir las dos propiedades. Cada item tiene su estado independiente sin instanciar el material.
- `SnapToFar()` opcional para forzar estado lejano sin animación (útil al ocultar el item o resetear estado).

**Cómo se acopla al sistema de interactuables**:

> ⚠️ **Desactualizado**: esta sección describía un wiring manual por triggers. **Ya no hace
> falta.** El componente se engancha solo: en `OnEnable` se suscribe a
> `InteractionEvents.OnTargetChanged` y compara el target contra su propio `IInteractable`
> (resuelto en `Awake` con `GetComponent<IInteractable>() ?? GetComponentInParent<IInteractable>()`).
> Cuando el SphereCast del `InteractionManager` apunta a este ítem pasa a estado *near*; cuando
> deja de apuntarlo vuelve a *far*. No hay que cablear triggers ni tocar `BaseRangeInteractable`.

```csharp
// ItemProximityHighlight.cs — el enganche real:
private void OnEnable()  => InteractionEvents.OnTargetChanged += HandleTargetChanged;
private void OnDisable() => InteractionEvents.OnTargetChanged -= HandleTargetChanged;

private void HandleTargetChanged(IInteractable target)
{
    bool isTargeted = _selfInteractable != null && ReferenceEquals(target, _selfInteractable);
    if (isTargeted == _isNear) return;   // el evento es global: evita relanzar el lerp en todos los ítems

    _isNear = isTargeted;
    if (isTargeted) OnPlayerEnteredRange();
    else            OnPlayerExitedRange();
}
```

`OnPlayerEnteredRange()` / `OnPlayerExitedRange()` siguen siendo públicos por si algún sistema
necesita forzar el estado a mano.

**Para puzzles e interactuables sin tinte de categoría** (palancas, paneles — spec sec 4.7): mismo shader, mismo script, pero `farTint = 0` y `nearTint = 0`. Solo brilla la emisión al acercarse, sin color de categoría. El prompt `[E]` lo diferencia visualmente del entorno, no el color.

**Categoría automática (sin duplicar el dropdown)**: si el GameObject tiene un `PickupInteractable` con `itemToPick` asignado, `ItemProximityHighlight.ResolveCategory()` toma la categoría directamente de `itemToPick.Category` — no hace falta volver a elegirla en el dropdown de `ItemProximityHighlight`. El dropdown manual (`category`) solo se usa si se tilda `overrideCategory`, o en interactuables sin `SO_InventoryItem` (puzzles/decorativos). `categoryConfig` (la paleta compartida) se sigue asignando a mano una vez, igual que en el resto de las UI views que la consumen (`ItemDetailView`, `GroupLabelView`, `ItemSlotView`).

### 5.4 Outline genérico opt-in (fresnel)

El shader `ItemPSX_Outline` agrega un outline por **fresnel** que funciona sobre cualquier mesh sin conocer su forma:

```
fresnel  = pow(1 - saturate(dot(normalWorld, viewDir)), _OutlinePower)
outline  = fresnel × _OutlineColor.rgb × _OutlineIntensity
emission = _EmissionColor × _EmissionIntensity + outline
```

Al usar la normal mundial + view direction, el borde se ilumina en la silueta de **cualquier** objeto (cubo, esfera, mesh arbitrario) sin geometría extra ni passes de backface. Sale por la vía de emission → participa del bloom y del lit pipeline.

| Property | Rango | Default | Descripción |
|---|---|---|---|
| `_OutlineColor` | Color HDR | `#66B3FF` | Color del borde. Se toma del "color language" del item. |
| `_OutlineIntensity` | 0–10 | **0** | Fuerza. **Apagado por default** (ver abajo). |
| `_OutlinePower` | 0.5–8 | 3 | Exponente del fresnel: bajo = borde grueso, alto = borde fino. |

> ⚠️ **Regla del spec (§4.3 / §4.6.1 del handoff)**: WIRED **no usa outlines de items** — el feedback de proximidad es tint+emisión sutil, no un rim. Por eso `_OutlineIntensity` viene en **0**: el material lo instala pero apagado. Solo se debe subir manualmente en el subset donde el color language lo justifica (puzzles / decorativos interactuables del §4.7), y con visto bueno de GD. Subir el outline sobre un item recogible de las 4 categorías (§5.2) **rompe el spec**.

---

## 6. Sistema de vision fog (post-process atmosférico)

Niebla estilo Silent Hill, centrada en el player, con rango y look definidos por presets de zona (`SO_VisionFogConfig`), movimiento animado, y — nuevo — un "hueco" de visibilidad alrededor de la linterna del player más zonas de bypass ancladas al mundo.

### 6.1 `VisionFog.hlsl` — Custom Functions del shader

Dos funciones que se invocan desde el Fullscreen Shader Graph, más helpers y globales nuevos.

**`WorldFromDepth_float(uv, depth, out worldPos)`**:
Reconstruye la posición world de cada píxel a partir del depth buffer. Necesita `Sample Scene Depth` en modo **Raw** (no Linear01, no Eye — la matemática asume valores NDC). Arma un vector NDC `(uv*2-1, depth, 1)`, lo multiplica por `UNITY_MATRIX_I_VP`, divide por w.

**`VisionFog_float(...)`**:
La función central. Mezcla scene color con fog color según:

```
fogFactor  = saturate((distance(worldPos.xz, playerPos.xz) - visionStart) / (visionEnd - visionStart))
fogFactor  = pow(fogFactor, _FogDensityPower)             ← curva de densidad (NUEVO)
fogFactor *= 1 - saturate(luminance × lightPreservation)  ← zonas iluminadas perforan
fogFactor *= 1 - bypassMask                               ← bypass zones (NUEVO §6.4)
fogFactor *= 1 - playerLightMask                          ← linterna del player (NUEVO §6.4)
fogFactor  = saturate(fogFactor × modulación_noise)       ← variación tipo "nubes"
fogFactor  = lerp(fogFactor, 1, skyboxMask)               ← skybox se hunde al 100%

result = lerp(sceneColor, fogColor, fogFactor) + _PlayerLightColor × playerLightMask
```

**Guard al inicio**: si `visionEnd <= visionStart`, devuelve `sceneColor` sin tocar nada. Pasa cuando el controller no está activo (Main Menu, LevelUI aislado) o el player no apareció todavía. Sin este guard, la pantalla quedaría negra.

**Globales nuevas** (declaradas en el `.hlsl`, seteadas desde C# — no necesitan slots en el grafo):
- `_FogDensityPower` (float) — exponente de la curva de cierre. 1 = lineal; >1 aprieta la niebla cerca del `visionEnd` (más sensación de falta de visibilidad).
- `_PlayerLightPosition / _PlayerLightRange / _PlayerLightIntensity / _PlayerLightColor` — la linterna perfora la niebla (falloff cuadrático) y agrega tinte en la zona iluminada.
- `_FogLightBypassData[8]` (float4: xyz=pos, w=radius) + `_FogLightBypassCount` (int) — hasta 8 zonas world donde la luz "atraviesa" la niebla. Se combinan con `max`, no se acumulan.

**Noise procedural animado**: dos capas de FBM en world-space scrolleando en direcciones distintas. Como las UV son world-space, el noise queda "pegado al mundo" — cuando el player camina, las nubes se quedan en su lugar. Modula el `fogFactor` en torno a 1.0 → no cambia la densidad promedio, solo agrega "respiración".

### 6.2 `VisionRangeController.cs` — Driver del fog

Setea las globals del shader cada `LateUpdate` y maneja transiciones suaves entre presets.

**Modelo de configuración (config stack)**:
1. `defaultConfig` (un `SO_VisionFogConfig`) es el preset base — la niebla cuando el player no está en ninguna zona especial (pasillos oscuros).
2. `PushConfig(config)` / `PopConfig(config)` son la API para los `LightZone` triggers (§6.3): mantienen un **stack**, la zona más interna (última pusheada) gana. Al salir, vuelve a la anterior.
3. Cada frame lerpea los valores actuales hacia los del config activo (`_lerpRate` derivado del `transitionDuration` del preset). Los `LightZone` anidados funcionan solos.

**Búsqueda del player**: por tag `"Player"` (`FindGameObjectWithTag`), reintenta cada 30 frames. Mientras no aparece, setea `_VisionEnd = 0` → guard del shader → sin fog. El player vive en otra escena (gameplay aditivo), por eso no se puede asignar una referencia serializada cross-scene.

**Globals que setea**: `_PlayerPos`, `_VisionStart`, `_VisionEnd`, `_FogColor`, `_LightPreservation`, `_FogDensityPower`, la linterna (`_PlayerLight*`) y el array de bypass (`_FogLightBypassData` / `_FogLightBypassCount`).

**Por qué globals y no material properties**: las globals se setean desde C# y aplican a todos los shaders simultáneamente. El `.hlsl` las lee directo del scope global — por eso agregar features nuevas (linterna, bypass, densidad) **no requirió tocar el shadergraph**, solo declarar la global en el HLSL y pushearla desde el controller.

### 6.3 `SO_VisionFogConfig.cs` — Preset de niebla por zona

ScriptableObject con el "feeling" de una zona. Crear con: Project → click derecho → Create → Rendering → Vision Fog Config.

| Campo | Rango | Descripción |
|---|---|---|
| `visionStart` | ≥ 0 | Distancia sin niebla (metros). |
| `visionEnd` | ≥ 0 | Distancia donde la niebla cubre 100%. Chico = opresivo. |
| `fogColor` | Color | Negro = oscuridad; gris medio = Silent Hill. |
| `lightPreservation` | 0–5 | Cuánto perforan la niebla las zonas brillantes del buffer. |
| `densityPower` | 0.25–4 | **NUEVO**. 1 = lineal; 2 = cierre más agresivo sin cambiar el rango. |
| `playerLightRange` | 0–30 | **NUEVO**. Radio del hueco de la linterna. 0 = feature apagado. Fallback si no hay `FogLightSource` con Light. |
| `playerLightIntensity` | 0–3 | **NUEVO**. Fuerza del hueco. |
| `playerLightColor` | Color HDR | **NUEVO**. Tinte que agrega la linterna. Default ámbar `#FFC850` (coherente con §3.3). |
| `transitionDuration` | ≥ 0 | Segundos de lerp al activarse este config. |
| `silhouetteMode` | enum | GD pendiente (§3.7 del handoff): None/Items/Puzzles/All. |

Los `LightZone` (`Scritps/Rendering/LightZone.cs`) son trigger volumes que pushean/popean un config al entrar/salir el player — así una safe room, un boss arena o un pasillo pueden tener cada uno su niebla.

### 6.4 `FogLightSource.cs` y `FogLightBypass.cs` — Fuentes de luz del fog (NUEVO)

Dos componentes que alimentan las globales de linterna y bypass.

**`FogLightSource`** — se pega en el hijo del player que tiene la Point Light ámbar del dispositivo (§3.3). Reporta su posición al controller cada frame. Dos modos:
- `useLightComponent = true` (default) — lee `Light.range`, `Light.color`, `Light.intensity` del `Light` hermano y los pushea como **override** sobre los valores del SO. Así, si a futuro se implementa la degradación de la luz por módulos explotados (§2.5.1 del handoff), el `fogClearRadius` baja solo, sin código extra.
- `useLightComponent = false` — usa los valores del `SO_VisionFogConfig` activo.
- `rangeMultiplier` (default 2) — el radio del fog puede ser mayor que el `Light.range` visible, para que el player vea más lejos que lo que la luz ilumina físicamente.

**`FogLightBypass`** — se pega en objetos anclados al mundo (farolas, monitores, hogueras) donde la niebla debe disolverse localmente aunque el player no esté cerca. Property `radius` (metros); se registra/desregistra en el controller vía `RegisterBypass` / `UnregisterBypass` estáticos (patrón similar a `LightZone`). Gizmo esférico amarillo visible al seleccionar.

> **Alternativa más fiel al spec §3.4**: en vez del componente estático, disparar `VisionRangeController.RegisterBypass(...)` desde `ZoneLightController.OnActivate()` (evento del generador) y `UnregisterBypass(...)` en `OnDeactivate()`. Migrable cuando exista ese sistema.

### 6.5 `VisionFog.mat` — Material instancia del shader fullscreen

Es la instancia del `Fullscreen_VisionFog.shadergraph`. Asignado al **Full Screen Pass Renderer Feature** en `PC_Renderer.asset`.

**Properties tunables** (estos sí son material-local, no global):
- `_FogNoiseScale` — frecuencia del noise (0.05 = nubes grandes orgánicas).
- `_FogNoiseIntensity` — qué tanto modula el fog (0.5 = movimiento visible).
- `_FogScrollSpeed` — velocidad del scroll (0.08 = atmosférico lento).

### 6.6 Orden en el Renderer Feature stack

```
PC_Renderer.asset:
  1. ScreenSpaceAmbientOcclusion           (existente)
  2. Full Screen Pass: Vision Fog          (BeforeRenderingPostProcessing)
  3. Full Screen Pass: PS1Effect           (BeforeRenderingPostProcessing)
```

Vision Fog va antes del PSX porque el fog se calcula con world-space coherente (depth + matrices reales); el PSX pixela y warpea la imagen final. Si se invirtiera el orden, la niebla quedaría deformada siguiendo el warp en vez de tener forma natural.

---

## 7. Filtro PS1 (post-process PSX)

Filtro fullscreen que le da el carácter retro final a toda la imagen. Originalmente un Shader Graph (`PS1_PostProcess.shadergraph`, hacía pixelation + warp + jitter); reemplazado por un **shader HLSL** (`Materials/Post Process/PS1_PostProcess_HLSL.shader`) que preserva esas features y agrega dither, scanlines y chromatic aberration, todas toggle-ables desde el inspector. El `.shadergraph` original queda como referencia; `PS1Effect.mat` apunta al shader HLSL, y el `FullScreenPassRendererFeature` (`PSXEffect` en `PC_Renderer.asset`) sigue apuntando al mismo `.mat` — el pipeline agarra el shader nuevo sin tocar el renderer.

### 7.1 Properties (todas en `PS1Effect.mat`)

| Grupo | Property | Default | Descripción |
|---|---|---|---|
| Master | `_EnableEffect` | 1 | Toggle global. En 0 devuelve la escena limpia. |
| Pixelation/Warp | `_PixelSize` / `_WarpStrength` / `_WarpIntensity` / `_JitterResolution` | 256 / 1.2 / 0.0018 / 50 | Pixelado + ondulación CRT + jitter vertex-snap PSX. |
| Dither | `_EnableDither` / `_DitherStrength` / `_DitherLevels` | 1 / 0.5 / 4 | Bayer 4×4 con posterización (spec §6.10). |
| Scanlines | `_EnableScanlines` / `_ScanlineIntensity` / `_ScanlineCount` | 1 / **0.06** / 240 | Bandas CRT. Default 0.06 = casi imperceptible (spec §6.10). |
| Chromatic Aberration | `_EnableChromaticAberration` / `_ChromaticAberrationOffset` | 1 / **0** | RGB split. Offset 0 por default — lo pulsa el `GlitchController` (§7.2). |

**Pipeline del fragment**: warp CRT → jitter → pixelation → sample (con CA opcional) → dither posterization → scanline modulation.

**Compatibilidad con el applier**: [`PS1EffectApplier.cs`](../Assets/Scritps/Rendering/PS1EffectApplier.cs) sigue funcionando sin cambios — escribe `_EnableScanlines` y `_EnableDither` como floats desde PlayerPrefs (`Settings_CRTScanlines` / `Settings_PSXDithering`), que ahora existen en el shader. Es el enganche con el toggle de accesibilidad de Options.

### 7.2 `GlitchController.cs` — Glitch VHS aleatorio (NUEVO)

El spec §6.10 pide que la chromatic aberration sea parte de un **glitch VHS aleatorio**, no un efecto continuo. El controller pulsa `_ChromaticAberrationOffset` sobre `PS1Effect.mat`:

- Intervalo entre glitches: `Random(8, 45)` s. Duración: `Random(0.1, 0.4)` s. (spec §6.10)
- Usa `Time.unscaledTime` → sigue corriendo en pausa (el spec pide glitch durante menús/pausa).
- Accesibilidad: lee `PlayerPrefs "Settings_VHSGlitch"` (1=on) y se suscribe a `SettingsModel.OnSettingsApplied`. Cuando Options exponga el toggle, basta escribir esa key + `RaiseSettingsApplied()`.
- Gate por modales: setear `GlitchController.SuspendTriggering = true` al abrir Inventory/SkillCheck/Examine y `false` al cerrar (spec §6.10: no dispara ahí).

**Setup**: componente en un GameObject persistente (o el mismo del `PS1EffectApplier`), arrastrar `PS1Effect.mat` al campo `Ps1 Material`.

---

## 8. Reglas y convenciones del spec aplicadas

| Regla del spec | Cómo lo respeta el sistema |
|---|---|
| Rojo solo para peligro | Solo `mat_luz_emergencia_emissive` tiene rojo. Ningún material de item ni de UI lo usa. |
| Ámbar #FFC850 solo para módulos | Solo `mat_device_luz_ambar_jugador` (y el `playerLightColor` del fog, que es la misma luz). Componentes usa marrón oscuro (`#4E342E`), no ámbar puro. |
| Azul/blanco frío #8AB4D4 solo para monitores | Solo `mat_monitor_pantalla`. |
| Sin outline detective-mode (Sec 4.6.1) | El outline fresnel de `ItemPSX_Outline` viene **apagado** (`_OutlineIntensity = 0`). Solo se activa manualmente en puzzles/decorativos del §4.7, nunca en items recogibles. Items se distinguen por tinte+emisión sutil. |
| Sin waypoints, mapa, partículas sobre items | El sistema tampoco los implementa. |
| Lerp 0.15→0.4 (tint) y 0.0→0.2 (emission) en 0.3s | Defaults exactos en `ItemProximityHighlight.cs`. Editables en Inspector si se necesita afinar por item. |
| Cuatro categorías con hex específicos | 4 materiales preset, cada uno con el hex exacto del spec sec 4.4. |
| Estilo PSX (sin PBR realista) | Smoothness baja en todos los materiales. Filtro PS1 (§7) aplica encima como efecto final. |
| Scanlines/dither/glitch con toggle de accesibilidad | `_EnableScanlines` / `_EnableDither` vía `PS1EffectApplier` + PlayerPrefs; glitch VHS vía `GlitchController` + `Settings_VHSGlitch` (spec §6.10). |

---

## 9. Verificación y debug

### Cómo verificar que el sistema completo anda

**Vision fog**:
1. Abrí `Window → Analysis → Frame Debugger` → Enable.
2. Buscá el draw call del Vision Fog ("Draw Fullscreen").
3. En el panel derecho, sección Globals:
   - `_PlayerPos` = posición real del player (no `(0,0,0)`).
   - `_VisionEnd` > 0 (sino el guard dispara y no hay fog).
   - `_PlayerLightPosition` = pos de la Light ámbar; `_PlayerLightRange` ≈ `Light.range × rangeMultiplier`.
   - `_FogLightBypassCount` = número de bypass zones activas.
4. Movéte con el player → la zona sin niebla debe seguirlo. Pasá cerca de un `FogLightBypass` → la niebla se abre local.
5. Bajá `Light.range` en runtime → el hueco se achica (valida la degradación futura de §2.5.1).

**Items**:
1. Cubo con `ItemPsx.mat` + `ItemProximityHighlight`.
2. Sin player cerca: el cubo se ve casi negro/gris con tinte azulado apenas perceptible.
3. Al entrar al radio (`OnPlayerEnteredRange()`): el cubo "respira" — gana tinte más visible y emisión sutil. Transición 0.3s.
4. (Outline) Subir `_OutlineIntensity` en runtime sobre un cubo y una esfera → el borde sigue la forma en ambos, confirmando que funciona en cualquier mesh.
5. Verificar en Profiler que el SRP Batcher está activo y NO se instancia el material.

**Filtro PS1**:
1. Seleccionar `PS1Effect.mat` en Play y togglear cada `_EnableXxx` → dither / scanlines / RGB shift aparecen o desaparecen en tiempo real.
2. Esperar 8–45 s → flash rápido de CA (glitch VHS). Si nunca aparece: `PlayerPrefs.SetInt("Settings_VHSGlitch", 1)` o activar en Options.

**Flickers**:
1. Monitor: en Play, el emission pulsa entre 0.9× y 1.0× cada 5 segundos.
2. Tubo fluorescente: parpadeo según la curva. Duplicar 3 tubos con offsets distintos (0, 0.33, 0.66) → desfasados.

### Problemas comunes

| Síntoma | Causa probable | Fix |
|---|---|---|
| Pantalla negra en LevelUI aislado | `_VisionEnd` default > 0 en el Shader Graph | Cambiar default a 0 en el Blackboard. |
| Burbuja de fog clavada en (0,0,0) | `_PlayerPos` no se setea | Verificar tag "Player" en el player, o asignar `playerOverride` manualmente. |
| Burbuja se mueve "invertido" con la cámara | `Sample Scene Depth` en modo Linear01 o Eye | Cambiar a Raw. |
| El hueco de la linterna no aparece | Falta `FogLightSource` en la Light del player, o `playerLightRange`/`Light.range` en 0 | Agregar el componente y verificar `useLightComponent` + range. |
| `.mat` muestra "Hidden/InternalErrorShader" | GUID del `.meta` del shader mal formado o colisión | Revisar el GUID/fileID del YAML (para `.shader` es `fileID: 4800000`). |
| El glitch VHS nunca dispara | `Settings_VHSGlitch` en 0 o `Ps1 Material` sin asignar en el `GlitchController` | Setear la key a 1 y arrastrar `PS1Effect.mat`. |
| Items siempre brillan al máximo | El `ItemProximityHighlight` no está pegado, o `farEmission` está en >0 | Verificar el componente + valores. |
| Material instanciado por cada item (rompe batcher) | El script no usa `MaterialPropertyBlock` | Verificar que esté usando `GetPropertyBlock/SetPropertyBlock`. |

---

## 10. Lo que falta / fases futuras

- **Degradación de la luz del dispositivo por módulos explotados** (§2.5.1 del handoff). Con `FogLightSource.useLightComponent = true`, el fog ya se degrada solo al bajar `Light.range`/`intensity` — falta el sistema que dispare esa degradación. El evento a escuchar es `InventoryEvents.OnModuleExploded` / `OnModuleStateChanged` (no existe ningún `ModuleManager`: los módulos viven en `InventoryManagerUI`). ⚠️ **Bloqueado**: hoy los timers de módulos nunca arrancan (`StartModuleTimer()` no lo llama nadie), así que `OnModuleExploded` no dispara nunca. Ver `docs/TODO-UI.md` § Bloqueantes del loop principal.
- **`GlitchController.SuspendTriggering` no lo setea nadie.** El spec §6.10 pide que el glitch VHS no dispare con inventario / skill check / examine abiertos. La property existe pero ningún controller la sube a `true`. Lo más limpio sería suscribirse a `UIStateManager.OnModalPushed/Popped`.
- **VHS vertical shift** (`_VHSShift`) del §6.10 — el `GlitchController` pulsa la CA pero no el desplazamiento vertical de líneas. Agregar la property al shader PS1 y al controller.
- **Ojos del Nemesis a través de la niebla** (§3.6) — billboards emisivos rojos en un layer que el fog excluye. Feature separada.
- **Siluetas de interactuables a través de la niebla** (§3.7 / `silhouetteMode` del SO) — pendiente de GD. Nota: la técnica correcta es un mask en el fog pass, no el outline fresnel del material (que es rim always-visible, no silhouette-through-fog).
- **Save points** con shader propio + LED verde (§4.7). Aparte del sistema actual de items.
- **Vertex snapping PSX** en `ItemPSX_Outline` para look PSX más fiel (opcional).
- **Textura de noise tileable** para el fog en lugar del FBM procedural (mejor look pero requiere asset).
- **Migrar `FogLightBypass`** a evento de `ZoneLightController` cuando exista (§6.4).

---

## 11. Referencias cruzadas

- `docs/Setup-VisionFog-PS1-Items.txt` — pasos manuales en Unity para configurar `FogLightSource`, `FogLightBypass`, `GlitchController` y los `SO_VisionFogConfig`.
- `docs/CLAUDE.md` — arquitectura general del proyecto (scene management, MVC UI, FSM, event bus).
- `docs/UI-System.md` — sistema de UI y Pausa (no relacionado a materiales pero parte del mismo proyecto).
- `C:\Users\Iñaki\Downloads\WIRED_Handoff_Code.docx` — spec técnico consolidado (Luz · Niebla · Color · Sub-puzzles · UI).

---

## 12. Tabla rápida: qué archivo modificar para qué cambio

| Quiero cambiar... | Editar... |
|---|---|
| Color de un item de categoría | El `.mat` correspondiente (`mat_item_keys` etc.). |
| Velocidad de la transición lejano↔próximo | `ItemProximityHighlight.lerpDuration` en Inspector. |
| Activar/afinar el outline de un puzzle | `_OutlineColor` / `_OutlineIntensity` / `_OutlinePower` en el `.mat` del interactuable (solo puzzles §4.7, ver §5.4). |
| Color / rango de la niebla de una zona | El `SO_VisionFogConfig` de esa zona (`fogColor`, `visionStart`, `visionEnd`). |
| Sensación de opresión de la niebla sin tocar el rango | `SO_VisionFogConfig.densityPower` (1 = base, >1 = más cerrado). |
| Radio del hueco de la linterna | `FogLightSource.rangeMultiplier` + `Light.range`, o `SO_VisionFogConfig.playerLightRange` si `useLightComponent = false`. |
| Zona donde la niebla se abra sin la linterna | Agregar `FogLightBypass` con `radius` al objeto. |
| Velocidad del scroll del noise de la niebla | `VisionFog.mat → _FogScrollSpeed` en Inspector. |
| Intensidad de scanlines / dither / pixelado | `PS1Effect.mat` → `_ScanlineIntensity` / `_DitherStrength` / `_PixelSize`. |
| Frecuencia/intensidad del glitch VHS | `GlitchController` → `Interval Range` / `Duration Range` / `Max CA Offset` en Inspector. |
| Frecuencia del flicker del monitor | `MonitorFlicker.flickerSpeed` en Inspector. |
| Patrón de parpadeo del tubo fluorescente | `FlickerLight.flickerCurve` en Inspector (editor visual de AnimationCurve). |
| Threshold del guard del fog (cuándo se desactiva) | `VisionFog.hlsl` línea con `if (visionEnd <= visionStart + 0.001)`. |
| Color de la emisión de emergencia | `mat_luz_emergencia_emissive → _EmissionColor` en YAML o Inspector. |
