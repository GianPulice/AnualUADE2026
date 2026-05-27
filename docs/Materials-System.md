# Sistema de materiales, shaders y post-process — Guía técnica

Explica qué hace cada material, shader y script que armamos para el lenguaje visual del juego (WIRED). Pensado para que alguien que se suma pueda entender el flujo completo sin tener que reconstruirlo desde el código.

Stack: **URP 17.4.0**, Unity 6, Compatibility mode (Renderer Feature API tradicional).

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
[ Vision Fog (Fullscreen Shader Graph) ]          ← AGREGADO
         │
         ▼
[ PS1 PostProcess: PSX + pixelado ]               ← existente, no se tocó
         │
         ▼
[ UI Overlay (Canvas Screen Space Overlay) ]      ← se dibuja después del pipeline
```

Cada capa tiene una responsabilidad clara y se puede prender/apagar independientemente.

**Reglas duras del spec** (`color_visual_language_spec.docx`) que el sistema respeta:
- **El rojo (#CC1A1A) es exclusivo de peligro**. Solo aparece en luces de emergencia, indicadores de módulo explotado, trampas.
- **Estética PSX**: shaders mate, sin PBR realista. Smoothness baja, sin metallic.
- **Lenguaje sutil**: los items destacan por tinte+emisión casi imperceptible, NO por outlines marcados ni waypoints.

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
│   │   └─ mat_item_special.mat                       ← Especiales    (#1A237E)
│   ├─ VisionFog.hlsl                                  ← Custom function HLSL
│   └─ Post Process/
│       ├─ PS1_PostProcess.shadergraph                 ← existente, no se tocó
│       ├─ PS1Effect.mat                               ← existente, no se tocó
│       └─ VisionFog.mat                               ← material del fog fullscreen
└─ Scritps/
    ├─ Environment/
    │   ├─ MonitorFlicker.cs                          ← pulso 0.2 Hz
    │   └─ FlickerLight.cs                            ← curve-driven, fluorescente
    ├─ Items/
    │   └─ ItemProximityHighlight.cs                  ← lerp tint+emission por proximidad
    └─ Rendering/
        └─ VisionRangeController.cs                    ← setea globals del fog
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

### 3.4 `shader_flicker_light.shader` — Tubos fluorescentes

Único shader **HLSL custom** (no Shader Graph). Está en `Assets/Materials/Environment/Fluorescent/shader_flicker_light.shader`.

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

Esto es el núcleo del lenguaje visual del juego. Spec sec 2.1: cada item tiene dos estados (lejano / próximo) que se transicionan suavemente cuando el player entra al radio de interacción.

### 5.1 Shader `ItemPSX` (Shader Graph URP/Lit)

**No es un shader fullscreen** — es un material que se aplica a los Renderers de los items.

**Properties**:

| Nombre | Tipo | Descripción |
|---|---|---|
| `_BaseColor` | Color | Color base del item (gris medio del entorno por default). |
| `_TintColor` | Color | Color de categoría (Llaves, Componentes, etc.). |
| `_TintIntensity` | Float 0–1 | Cuánto tinte de categoría se ve. **Lo modula el script.** |
| `_EmissionColor` | Color HDR | El mismo tinte pero brillará. |
| `_EmissionIntensity` | Float 0–1 | Brillo de emisión. **Lo modula el script.** |

**Grafo simple**:
```
finalAlbedo   = lerp(_BaseColor, _TintColor, _TintIntensity)
finalEmission = _EmissionColor * _EmissionIntensity
```

Smoothness baja (0.0–0.3) y sin metallic → look mate industrial PSX. Se aplica el filtro fullscreen PSX encima, que pixela y le da el carácter retro final.

### 5.2 Los 4 materiales preset (uno por categoría del spec)

| Material | Tinte (sRGB) | Smoothness | Items que la usan |
|---|---|---|---|
| `mat_item_keys` | `#37474F` gris azulado | 0.2 | Llave armario, Llave sala control |
| `mat_item_components` | `#4E342E` marrón cálido | 0.15 | Núcleo Energético, Núcleo Mecánico, Regulador de Presión |
| `mat_item_clues` | `#263238` gris azulado oscuro | 0.0 | Notas, planos, diario |
| `mat_item_special` | `#1A237E` azul intenso | 0.3 | Cortador de cadenas (item único) |

Todos arrancan con `_TintIntensity = 0.15` y `_EmissionIntensity = 0` → estado lejano default.

**Por qué estos hex y no otros**: están elegidos para mantener la **paleta fría y oscura** del spec (sec 3). La excepción es Componentes (cálido oscuro) que se diferencia narrativamente por tener "actividad mecánica interna".

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
El componente es **standalone**. No se modificó `BaseRangeInteractable.cs`. El wiring lo hace el desarrollador del sistema de interactuables agregando:

```csharp
// En BaseRangeInteractable o similar:
[SerializeField] private ItemProximityHighlight _highlight;

private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("Player")) _highlight?.OnPlayerEnteredRange();
}

private void OnTriggerExit(Collider other)
{
    if (other.CompareTag("Player")) _highlight?.OnPlayerExitedRange();
}
```

**Para puzzles e interactuables sin tinte de categoría** (palancas, paneles — spec sec 6): mismo shader, mismo script, pero `farTint = 0` y `nearTint = 0`. Solo brilla la emisión al acercarse, sin color de categoría. El prompt `[E]` lo diferencia visualmente del entorno, no el color.

---

## 6. Sistema de vision fog (post-process atmosférico)

Niebla volumétrica estilo Silent Hill, centrada en el player, con rango adaptativo según la iluminación ambiente y movimiento animado.

### 6.1 `VisionFog.hlsl` — Custom Functions del shader

Dos funciones que se invocan desde el Fullscreen Shader Graph:

**`WorldFromDepth_float(uv, depth, out worldPos)`**:
Reconstruye la posición world de cada píxel a partir del depth buffer. Necesita `Sample Scene Depth` en modo **Raw** (no Linear01, no Eye — la matemática asume valores NDC).

Cómo: arma un vector NDC `(uv*2-1, depth, 1)`, lo multiplica por `UNITY_MATRIX_I_VP` (inversa de view-projection), divide por w. El resultado es el punto del mundo que ese píxel está mostrando.

**`VisionFog_float(...)`**:
La función central. Mezcla scene color con fog color según:

```
fogFactor = (distance(worldPos.xz, playerPos.xz) - visionStart) / (visionEnd - visionStart)
          × (1 - luminance × lightPreservation)      ← si está iluminado, menos fog
          × modulación_noise_animado                  ← variación tipo "nubes"
          + skybox_mask                               ← skybox se hunde al 100%
```

`result = lerp(sceneColor, fogColor, fogFactor)`

**Guard al inicio**: si `visionEnd <= visionStart`, devuelve `sceneColor` sin tocar nada. Esto pasa cuando el controller no está activo (Main Menu, LevelUI aislado) o el player no apareció todavía. Sin este guard, la pantalla quedaría negra.

**Noise procedural animado**:
- Dos capas de FBM (Fractal Brownian Motion) scrolleando en direcciones distintas.
- UV en world-space (`worldPos.xz * scale + Time * speed`) → el noise está "pegado al mundo", no a la pantalla. Cuando el player camina, las nubes se quedan en su lugar.
- Modula el `fogFactor` en torno a 1.0 → no cambia la densidad promedio, solo agrega variación. La niebla parece tener "respiración".

### 6.2 `VisionRangeController.cs` — Driver del fog

Setea las globals del shader cada frame.

**Lo que hace**:
1. Busca el player por tag `"Player"` (`FindGameObjectWithTag`). Si no lo encuentra, reintenta cada 30 frames. Mientras tanto, setea `_VisionEnd = 0` → guard del shader dispara → no aplica fog.
2. Muestrea `RenderSettings.ambientLight` para saber qué tan iluminada está la escena.
3. Lerpea `_VisionEnd` entre `visionEndDark` (oscuridad → fog cierra cerca) y `visionEndLit` (zona iluminada → fog se aleja).
4. Setea como globals: `_PlayerPos`, `_VisionStart`, `_VisionEnd`, `_FogColor`, `_LightPreservation`.

**Por qué globals y no material properties**: las globals se setean desde C# y aplican a TODOS los shaders simultáneamente. Las properties del Shader Graph deben estar configuradas como **NO Exposed** (o Shader Declaration = Global) para leer del setter global en vez de su valor local guardado en el material. Esto está documentado en `Shader-Items-Setup.md`.

**Por qué buscar por tag y no asignar referencia directa**: el player vive en una escena distinta (gameplay aditivo). Unity no permite referencias serializadas cross-scene. Sin el find dinámico, el controller intentaría asignar null o crashearía.

**Patrón ambient → vision range**:
- Zona oscura (`ambientLight` luminance ≈ 0) → `visionEnd = visionEndDark` (6m default).
- Zona iluminada (`ambientLight` luminance ≈ 1) → `visionEnd = visionEndLit` (25m default).
- Lerp suavizado con `lerpSpeed` para evitar pops al cruzar entre zonas.

### 6.3 `VisionFog.mat` — Material instancia del shader fullscreen

Es la instancia del `Fullscreen_VisionFog.shadergraph`. Asignado al **Full Screen Pass Renderer Feature** en `PC_Renderer.asset`.

**Properties tunables** (estos sí son material-local, no global):
- `_FogNoiseScale` — frecuencia del noise (0.05 = nubes grandes orgánicas).
- `_FogNoiseIntensity` — qué tanto modula el fog (0.5 = movimiento visible).
- `_FogScrollSpeed` — velocidad del scroll (0.08 = atmosférico lento).

### 6.4 Orden en el Renderer Feature stack

```
PC_Renderer.asset:
  1. ScreenSpaceAmbientOcclusion           (existente)
  2. Full Screen Pass: Vision Fog          ← AGREGADO, BeforeRenderingPostProcessing
  3. Full Screen Pass: PS1Effect           (existente, BeforeRenderingPostProcessing)
```

Vision Fog va antes del PSX porque:
- El fog se calcula con world-space coherente (depth + matrices reales).
- El PSX pixela y warpea — si lo aplicás antes, la niebla queda "deformada" siguiendo el warp en vez de tener forma natural.
- Orden actual: fog primero (look natural) → PSX deforma todo junto (look PSX coherente sobre la imagen final).

---

## 7. Reglas y convenciones del spec aplicadas

| Regla del spec | Cómo lo respeta el sistema |
|---|---|
| Rojo solo para peligro | Solo `mat_luz_emergencia_emissive` tiene rojo. Ningún material de item ni de UI lo usa. |
| Ámbar #FFC850 solo para módulos | Solo `mat_device_luz_ambar_jugador`. Si Componentes tiene tinte cálido (Sec 3), es marrón oscuro (`#4E342E`), no ámbar puro. |
| Azul/blanco frío #8AB4D4 solo para monitores | Solo `mat_monitor_pantalla`. |
| Sin outline detective-mode (Sec 5.4) | El sistema **no tiene Sobel multi-canal** ni Renderer Feature de mask. Items se distinguen solo por tinte+emisión sutil (Sec 2.1). |
| Sin waypoints, mapa, partículas sobre items | El sistema tampoco los implementa. |
| Lerp 0.15→0.4 (tint) y 0.0→0.2 (emission) en 0.3s | Defaults exactos en `ItemProximityHighlight.cs`. Editables en Inspector si se necesita afinar por item. |
| Cuatro categorías con hex específicos | 4 materiales preset, cada uno con el hex exacto del spec sec 3. |
| Estilo PSX (sin PBR realista) | Smoothness baja en todos los materiales. PS1_PostProcess aplica encima como filtro final. |

---

## 8. Verificación y debug

### Cómo verificar que el sistema completo anda

**Vision fog**:
1. Abrí `Window → Analysis → Frame Debugger` → Enable.
2. Buscá el draw call del Vision Fog.
3. En el panel derecho, sección Globals:
   - `_PlayerPos` = posición real del player (no `(0,0,0)`).
   - `_VisionEnd` > 0 (sino el guard dispara y no hay fog).
4. Movéte con el player → la zona sin niebla debe seguirlo.

**Items**:
1. Cubo con `ItemPSX` + `mat_item_keys` + `ItemProximityHighlight`.
2. Sin player cerca: el cubo se ve casi negro/gris con tinte azulado apenas perceptible.
3. Al entrar al radio (llamar `OnPlayerEnteredRange()`): el cubo "respira" — gana tinte azul más visible y emisión sutil. Transición 0.3s.
4. Verificar en Profiler que el SRP Batcher está activo y NO se instancia el material (un solo draw call para múltiples items idénticos).

**Flickers**:
1. Monitor: en Play, el emission pulsa entre 0.9× y 1.0× cada 5 segundos.
2. Tubo fluorescente: parpadeo según la curva. Duplicar 3 tubos con offsets distintos (0, 0.33, 0.66) → desfasados.

### Problemas comunes

| Síntoma | Causa probable | Fix |
|---|---|---|
| Pantalla negra en LevelUI aislado | `_VisionEnd` default > 0 en el Shader Graph | Cambiar default a 0 en el Blackboard. |
| Burbuja de fog clavada en (0,0,0) | `_PlayerPos` no se setea | Verificar tag "Player" en el player, o asignar `playerOverride` manualmente. |
| Burbuja se mueve "invertido" con la cámara | `Sample Scene Depth` en modo Linear01 o Eye | Cambiar a Raw. |
| Properties del fog visibles en Inspector pero no responden a script | Están como Exposed (default), no como Global | En Blackboard, desmarcar Exposed o cambiar Shader Declaration a Global. |
| Items siempre brillan al máximo | El `ItemProximityHighlight` no está pegado, o `farEmission` está en >0 | Verificar el componente + valores. |
| Material instanciado por cada item (rompe batcher) | El script no usa `MaterialPropertyBlock` | Verificar que esté usando `GetPropertyBlock/SetPropertyBlock`. |

---

## 9. Lo que falta / fases futuras

- **Degradación de la luz del dispositivo por módulos explotados** (Sec 6 del spec). Suscribir un componente nuevo `DeviceLightDegradation.cs` a `InventoryEvents.OnModuleExploded` y lerpear `light.intensity` + `light.range`.
- **Save points** con shader propio + LED verde (Sec 6). Aparte del sistema actual de items.
- **Trampas visibles** con emisión roja sutil (Sec 6). Cuando se diseñen las trampas concretas.
- **Vertex snapping PSX** en `ItemPSX` para look PSX más fiel (opcional).
- **Textura de noise tileable** para el fog en lugar del FBM procedural (mejor look pero requiere asset).
- **Vignette circular tipo linterna** que se cierre alrededor del jugador junto con el fog.
- **LUT/paleta unificada** como Volume override para forzar paleta limitada coherente con look PSX.

---

## 10. Referencias cruzadas

- `docs/Shader-Items-Setup.md` — pasos manuales en Unity para crear los `.shadergraph` y materiales.
- `docs/UI-System.md` — sistema de UI y Pausa (no relacionado a materiales pero parte del mismo proyecto).
- `color_visual_language_spec.docx` (`C:\Users\Iñaki\Downloads\`) — spec original del lenguaje visual.

---

## 11. Tabla rápida: qué archivo modificar para qué cambio

| Quiero cambiar... | Editar... |
|---|---|
| Color de un item de categoría | El `.mat` correspondiente (`mat_item_keys` etc.). |
| Velocidad de la transición lejano↔próximo | `ItemProximityHighlight.lerpDuration` en Inspector. |
| Color de la niebla | `VisionRangeController.fogColor` en Inspector. |
| Rango de la niebla en oscuridad | `VisionRangeController.visionEndDark` en Inspector. |
| Velocidad del scroll del noise de la niebla | `VisionFog.mat → _FogScrollSpeed` en Inspector. |
| Cómo se calcula el light level (ambient vs Light Probe vs trigger zones) | `VisionRangeController.SampleLightLevel()` — método privado, reemplazar implementación. |
| Frecuencia del flicker del monitor | `MonitorFlicker.flickerSpeed` en Inspector. |
| Patrón de parpadeo del tubo fluorescente | `FlickerLight.flickerCurve` en Inspector (editor visual de AnimationCurve). |
| Threshold del guard del fog (cuándo se desactiva) | `VisionFog.hlsl` línea con `if (visionEnd <= visionStart + 0.001)`. |
| Color de la emisión de emergencia | `mat_luz_emergencia_emissive → _EmissionColor` en YAML o Inspector. |
