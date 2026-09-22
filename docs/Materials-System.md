# Sistema de materiales, shaders y post-process — Guía técnica

Explica qué hace cada material, shader y script que armamos para el lenguaje visual del juego (WIRED). Pensado para que alguien que se suma pueda entender el flujo completo sin tener que reconstruirlo desde el código.

Stack: **URP 17.4.0**, Unity 6, Compatibility mode (Renderer Feature API tradicional).

> ⚠️ **Nota de idioma**: el código de `Assets/_Project/Scripts/` está íntegramente en inglés (comentarios,
> strings, logs y textos de UI). Este documento sigue en español. Ver `docs/CLAUDE.md` § Language rule.

---

## 1. Visión general

El sistema visual tiene estas capas, que se ejecutan en este orden por frame:

```
[ Escena 3D (opaques + transparents) ]
         │
         ▼
[ SSAO ]                                          ← existente, no se tocó
         │
         ▼
[ Vision Fog (VisionFog_HLSL.shader, fullscreen) ] ← BeforeRenderingPostProcessing (550)
         │
         ▼
[ Post-process de URP (Volumes) ]
         │
         ▼
[ PS1 PostProcess: PSX + dither + scanlines + CA ] ← AfterRenderingPostProcessing (600)
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
Assets/_Project/
├─ Art/Materials/
│   ├─ Environment/
│   │   ├─ Emergency/
│   │   │   └─ mat_luz_emergencia_emissive.mat       ← #CC1A1A baked
│   │   ├─ Monitor/
│   │   │   └─ mat_monitor_pantalla.mat              ← #8AB4D4 realtime + flicker
│   │   ├─ Device/
│   │   │   └─ mat_device_luz_ambar_jugador.mat      ← #FFC850 realtime
│   │   ├─ Fluorescent/
│   │   │   └─ shader_flicker_light.shader            ← HLSL custom URP unlit
│   │   └─ New FBXs/
│   │       └─ PSXIndustrial.shader                   ← props PSX; declara tint/emisión (el highlight lo maneja)
│   ├─ Items/
│   │   ├─ mat_item_keys_red.mat                      ← Llaves        (§5.2)
│   │   ├─ mat_item_components_green.mat              ← Componentes
│   │   ├─ mat_item_clues_blue.mat                    ← Pistas
│   │   ├─ mat_item_special.mat                       ← Especiales
│   │   ├─ mat_item_nota.mat                          ← Notas (PSXIndustrial)
│   │   ├─ ItemPSX_Outline.shader                     ← HLSL Lit + outline opt-in
│   │   ├─ ItemPsx.mat                                ← apunta a ItemPSX_Outline.shader
│   │   ├─ HighlightOverlay.shader                    ← capa aditiva del highlight (§5.3)
│   │   ├─ mat_highlight_overlay.mat                  ← la usa SO_Highlight_Cores
│   │   ├─ ItemGlint.shader                           ← estrella del destello (§5.5), unlit aditivo
│   │   └─ mat_item_glint.mat                         ← forma de la estrella (lo crea Set Up Item Glints)
│   └─ Post Process/
│       ├─ VisionFog_HLSL.shader                      ← shader del fog (el que corre, §6.1)
│       ├─ VisionFog.mat                               ← material del fog fullscreen (PC_Renderer)
│       ├─ VisionFog_SilentHill.mat                    ← variante con noise prendido
│       ├─ Fullscreen_VisionFog.shadergraph + VisionFog.hlsl ← LEGACY: rollback, no se compilan
│       ├─ PS1_PostProcess.shadergraph                 ← original (referencia, no se toca)
│       ├─ PS1_PostProcess_HLSL.shader                 ← PSX + dither/scanlines/CA
│       └─ PS1Effect.mat                               ← apunta a PS1_PostProcess_HLSL.shader
├─ Prefabs/Light/
│   ├─ Light Base.prefab                               ← lámpara estándar (§6.4)
│   └─ Light Base Switch.prefab                        ← variante para PoweredLightSwitch (§6.4.1)
├─ ScriptableObjects/
│   ├─ Highlight/                                      ← SO_Highlight_*.asset (§5.3), SO_Glint_Items.asset (§5.5)
│   ├─ Rendering/Fog/                                  ← SO_VisionFog_Dark / Darkness / Light / SilentHill
│   ├─ Escape/SO_VisionFog_Escape*.asset               ← presets del escape
│   └─ SO_PostProcessToggle.asset                      ← prende/apaga PS1 + fog de un botón (§6.5)
├─ Settings/PC_Renderer.asset                          ← renderer features (§6.6)
└─ Scripts/
    ├─ Environment/
    │   ├─ MonitorFlicker.cs                          ← pulso 0.2 Hz
    │   └─ FlickerLight.cs                            ← curve-driven, fluorescente
    ├─ Items/
    │   ├─ ItemProximityHighlight.cs                  ← lerp tint+emission al apuntar
    │   └─ ItemGlint.cs                               ← destello de lejos, estilo Resident Evil (§5.5)
    ├─ ScriptableScripts/Interactables/
    │   ├─ SO_HighlightProfile.cs                     ← valores del highlight (§5.3)
    │   └─ SO_GlintProfile.cs                         ← timing/tamaño/color/rango del destello (§5.5)
    ├─ ScriptableScripts/Rendering/
    │   ├─ SO_VisionFogConfig.cs                       ← preset de niebla por zona
    │   └─ SO_PostProcessToggle.cs
    ├─ Rendering/
    │   ├─ VisionRangeController.cs                    ← setea globals del fog
    │   ├─ VisionFogState.cs                           ← el set de valores + el único que escribe los globals
    │   ├─ LightZone.cs                                ← trigger que pushea un config
    │   ├─ VisionFogOverride.cs                        ← pushea un config mientras está activo (cinemáticas)
    │   ├─ VisionFogTrack.cs / VisionFogClip.cs        ← track de Timeline para previsualizar
    │   ├─ FogLightSource.cs                           ← luz del módulo del player
    │   ├─ FogLightBypass.cs                           ← zonas que perforan niebla
    │   ├─ FogLightBypassPlayerFade.cs                 ← baja el halo del bypass con el player adentro
    │   ├─ FogBeacon.cs                                ← punto visible a través de la niebla (§6.4.1)
    │   ├─ FogLightVolume.cs                           ← haz de una Light en la niebla (§6.4.1)
    │   ├─ PS1EffectApplier.cs                         ← toggles dither/scanlines (PlayerPrefs)
    │   └─ GlitchController.cs                         ← glitch VHS aleatorio (CA)
    └─ Editor/
        ├─ InteractableHighlightSetup.cs               ← Tools ▸ Interactables ▸ Set Up Highlights / Set Up Item Glints
        ├─ ItemHighlightValidator.cs                   ← Tools ▸ Items ▸ Validate Interactable Highlights
        ├─ SO_VisionFogConfigEditor.cs                 ← botones Aplicar como Default / Previsualizar
        └─ VisionFogPreviewDrawer.cs                   ← preview del preset en el inspector
```

---

## 3. Materiales del entorno

Cada uno respeta exactamente el hex del spec y usa **URP/Lit** (excepto el shader fluorescente custom).

> **Estado hoy**: ninguno de los tres `.mat` de esta sección, ni `MonitorFlicker` / `FlickerLight` (§4), está puesto en una escena o prefab de `_Project` (búsqueda por GUID). Son la receta lista para cuando se coloquen.

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

**Acompañado por** [`MonitorFlicker.cs`](../Assets/_Project/Scripts/Environment/MonitorFlicker.cs) — ver sec 4.1.

### 3.3 `mat_device_luz_ambar_jugador` — Encendedor del dispositivo del player

| Property | Valor | Por qué |
|---|---|---|
| Base Color | `(0.15, 0.09, 0.02)` ámbar oscuro | Spec: dispositivo del jugador. |
| Emission Color (HDR) | `(2.5, 1.5, 0.5)` = `#FFC850` intensity ~1.5 | Spec: ámbar cálido `#FFC850`. |
| Lightmap Flags | `1` (Realtime) | El device se mueve con el player → no bakeable. |

**Por qué realtime y no baked**: el player se mueve por el mundo, su luz tiene que recalcular shadows/lighting en cada frame.

**Lo que lleva el player hoy**: no este material sino los LEDs de los módulos. `Player.prefab` tiene tres Point Lights (una por módulo: piernas, pecho, cabeza), color `(1, 0.5, 0.04)`, intensity 3.5, range 1.6, sin sombras. Las maneja `ModuleLED` (`Scripts/Player/Modules/`): apagado en Inactive, parpadeo naranja en Active, verde fijo en Resolved, rojo fijo en Exploded (desde que se ve la explosión). `ModuleLightLayers` pone cada módulo en su propio Rendering Layer (1 = Legs, 2 = Chest, 3 = Head) para que la luz de uno no ilumine a los otros.

> **Enganche con el fog**: la idea es que la luz del módulo alimente el "hueco" de niebla vía [`FogLightSource`](../Assets/_Project/Scripts/Rendering/FogLightSource.cs) (ver §6.4): un solo sistema de luz del player, dos lecturas. **Hoy `FogLightSource` no está en `Player.prefab` ni en ninguna escena**, así que el hueco sale de los valores `playerLight*` del preset activo y se centra en el pivot del player.

### 3.4 `shader_flicker_light.shader` — Tubos fluorescentes

Shader **HLSL custom** `Custom/FlickerLight` (no Shader Graph). Está en `Assets/_Project/Art/Materials/Environment/Fluorescent/shader_flicker_light.shader`.

**Por qué custom**: el tubo fluorescente necesita brillar con su propia luz (es la fuente luminosa, no un objeto iluminado). URP/Lit sería overkill — el tubo no recibe luz importante, solo emite. Un shader unlit con `BaseColor * Intensity` alcanza.

**Estructura**:
- Pass `ForwardUnlit`: pinta `BaseColor * _Intensity` con fog mix (de URP).
- Pass `ShadowCaster`: permite que el tubo proyecte sombra si el level designer lo quiere.

**Properties**:
- `_BaseColor` (HDR) — color del tubo.
- `_Intensity` (Float) — se anima desde [`FlickerLight.cs`](../Assets/_Project/Scripts/Environment/FlickerLight.cs).

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

Esto es el núcleo del lenguaje visual del juego. Spec sec 4.3: cada item tiene dos estados (lejano / próximo) que se transicionan suavemente; en la implementación, "próximo" es "la mira lo apunta" (§5.3).

### 5.1 Shader `ItemPSX_Outline` (HLSL URP/Lit)

**No es un shader fullscreen** — es un material que se aplica a los Renderers de los items.

Originalmente `ItemPSX` era un Shader Graph (URP/Lit). Se reemplazó por un **shader HLSL** `Custom/ItemPSXOutline` (`Art/Materials/Items/ItemPSX_Outline.shader`) que preserva todas las properties del grafo original y agrega el outline opt-in del §5.4. `ItemPsx.mat` apunta al shader nuevo.

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

### 5.2 Los materiales preset (uno por categoría)

| Material | Shader | `_TintColor` | Smoothness | Quién lo usa (prefabs) |
|---|---|---|---|---|
| `mat_item_keys_red` | `ItemPSX_Outline` | `(1, 0.1, 0.06)` rojo | 0.3 | `InventoryItemLlaveIngenieria` |
| `mat_item_components_green` | `ItemPSX_Outline` | `(0, 0.39, 0.05)` verde | 0.3 | Núcleo Energético, Núcleo Mecánico, Regulador de Presión |
| `mat_item_clues_blue` | `ItemPSX_Outline` | `(0.06, 0.3, 1)` azul | 0.3 | ninguno (sólo la escena Dev `TestIñaki`) |
| `mat_item_special` | `ItemPSX_Outline` | `(1, 0.68, 0)` naranja | 0.3 | ninguno (sólo `TestIñaki`) |
| `mat_item_nota` | `PSXIndustrial` | `(0.06, 0.3, 1)` azul, tint 0.25 | — | `NoteFather/Note` |

Los cuatro de `ItemPSX_Outline` tienen en el asset `_TintIntensity = 0.75` y `_EmissionIntensity = 0.65`, pero eso sólo se ve en el editor: en Play `ItemProximityHighlight` (§5.3) pisa por `MaterialPropertyBlock` la intensidad de tinte y emisión con las del perfil, y **sus colores con los de la categoría del ítem** (`ItemCategory.asset → shaderTintColor / shaderEmissionColor`). El color que se ve en juego se cambia ahí, no en el `.mat`.

**Ya no son los hex del spec** (`#37474F`, `#4E342E`, `#263238`, `#1A237E`, paleta fría y oscura): siguen el lenguaje de color de categorías de la UI del inventario (llave roja, componente verde, nota azul, especial naranja). Ojo que esto choca con dos reglas del §8 —rojo sólo para peligro, verde sólo para "módulo alimentado"— y no hay una decisión registrada en el código ni en `docs/` que lo habilite: confirmarlo con GD.

### 5.3 `ItemProximityHighlight.cs`

MonoBehaviour que resalta un interactuable mientras la mira apunta a él. Es la cabeza del sistema.

**Dónde va**: en la **raíz del interactuable, en el prefab Father** (`InventoryItemFather`, `SocketFather`, `ValveFather`, `DoorFather`), así todas las variantes lo heredan sin tocar nada. Los interactuables sin Father (`PanelElectrico`, `Box_A/B/C`) lo llevan en su propio prefab; el botón del montacargas, en `RideButton/Visual`. `Tools ▸ Interactables ▸ Set Up Highlights` deja todo eso armado y se puede volver a correr: crea `SO_Highlight_Items` / `SO_Highlight_Interactables` si faltan, y un highlight que ya tiene perfil lo conserva. También lo llevan, puestos a mano, `NoteFather/Note`, `HidingSpot`, los `Decoy_*` y `ElevatorCallSwitch`.

**Qué resalta**: todos los `Renderer` debajo de él — inactivos incluidos, p. ej. el ítem insertado de un socket — cuyo `IInteractable` más cercano es el mismo que el del componente. Un interactuable anidado más abajo se resalta con su propio componente.

**Qué escribe, slot por slot, por `MaterialPropertyBlock`** (sin instanciar materiales):

| Material del slot | Qué hace |
|---|---|
| Declara `_EmissionColor` y `_EmissionIntensity` (`ItemPSX_Outline`, `PSXIndustrial`, el Shader Graph `SH_AW_PBR_ORM` de las cajas de madera) | Emisión y su color; tint y su color solo si además declara `_TintIntensity`. |
| URP/Lit (tiene `_EmissionColor`) con Emission encendida | **Suma** `color × intensidad × litEmissionScale` a la emisión propia del material: un panel encendido sigue encendido. |
| URP/Lit con Emission encendida **y** `_EmissionMap` | La emisión sumada quedaría enmascarada a los puntitos del mapa, así que aclara `_BaseColor` en la misma proporción. Si el perfil tiene `overlayMaterial` (sólo `SO_Highlight_Cores`) y el renderer tiene un solo material sobre un mesh de un submesh, en cambio le agrega mientras está encendido una capa aditiva encima (`mat_highlight_overlay`, shader `WIRED/Highlight Overlay`, color por `_OverlayColor`), sin tocar su emisión. |
| URP/Lit con Emission apagada | Nada — URP compila la emisión afuera. `Set Up Highlights` la enciende en negro (sin cambio visual). |
| Cualquier otro (p. ej. Shader Graphs de terceros) | Nada. El validador lo lista. |

Se escribe por slot, leyendo antes el block de ese slot, porque un block por slot **reemplaza** al del renderer entero: escrito a nivel renderer quedaría ignorado donde otro script ya maneja un slot (`ElevatorCallPanel`, `FuseIndicatorLight`).

**De dónde salen los valores**: de un `SO_HighlightProfile` compartido (`ScriptableObjects/Highlight/`), nunca del componente — así una familia entera no se desincroniza prefab por prefab ni por overrides de escena. `litEmissionScale` multiplica la emisión sólo en materiales URP/Lit, que necesitan mucha más que los shaders de highlight para leerse igual en pantalla.

| Perfil | Lo usan | Far → near | `litEmissionScale` | Color |
|---|---|---|---|---|
| `SO_Highlight_Items` | `InventoryItem` (Father), `Note` | tint 0.195 → 1, emisión 0.127 → 1.502 | 1 | El de la categoría del ítem (`categoryConfig` = `ItemCategory.asset`) |
| `SO_Highlight_Interactables` | `Socket`, `Valve`, `Box_A/B/C`, `MontacargasRoot`, `HidingSpot`, `Decoy_*` | tint 0 → 0, emisión 0 → 0.15 (spec §6) | 6 | `#E0E0E0` (el "seleccionado" de la UI) |
| `SO_Highlight_Cores` | Sockets de núcleo (`SocketNucleoEnergetico/Mecanico`, `SocketReguladorPresion`) | igual que Interactables | 6 | `#E0E0E0` + `overlayMaterial` |
| `SO_Highlight_Doors` | `Door` (Father de todas las puertas) | igual que Interactables | 2 | `#E0E0E0` |
| `SO_Highlight_ElectricPanel` | `PanelElectrico`, `ElevatorCallSwitch` | tint 0 → 0.125, emisión 0 → 0.3 | 1 | `#E0E0E0` |

Los valores de `SO_Highlight_Items` se subieron a mano el 16/09 por encima de los del spec §2.1 (tint 0.15 → 0.4, emisión 0 → 0.2), que son los que `Set Up Highlights` escribe sólo al crear el asset. Su `emissionColor` propio es ámbar `(1, 0.784, 0.314)`, pero sólo se usa si el dueño no es un pickup con ítem: con `categoryConfig` asignado, los pickups toman el color de su categoría.

**Cómo está implementado**:
- Coroutine con `SmoothStep` para que la transición sea sigmoide, no lineal. Hace que el "respirar" se sienta orgánico, no mecánico.
- `SnapToFar()` opcional para forzar estado lejano sin animación (útil al ocultar el objeto o resetear estado).

**Cómo se acopla al sistema de interactuables**: solo, sin triggers ni cableado. En `Awake` resuelve su dueño con `GetComponentInParent<IInteractable>(true)`; en `OnEnable` se suscribe a `InteractionEvents.OnTargetChanged`. Pasa a *near* mientras el target del `InteractionManager` es su dueño **y** el dueño no terminó (`IInteractable.IsFinished()`): un socket lleno o un panel resuelto ya no se encienden aunque el player lo siga mirando. Mientras está apuntado re-evalúa en `Update`, así que si el socket se llena con la mira encima se apaga en ese momento. `OnPlayerEnteredRange()` / `OnPlayerExitedRange()` siguen siendo públicos por si algún sistema necesita forzar el estado a mano.

**Para puzzles e interactuables sin tinte de categoría** (palancas, paneles — spec sec 4.7): mismo script, perfil sin tinte (`farTint = nearTint = 0`, como `SO_Highlight_Interactables`). Solo brilla la emisión al apuntarlos, sin color de categoría. El prompt `[E]` lo diferencia visualmente del entorno, no el color.

**Categoría automática**: el componente ya no tiene dropdown de categoría. `SO_HighlightProfile.ResolveColors()` decide: si el perfil tiene `categoryConfig` y el dueño es un `PickupInteractable` con ítem, usa `shaderTintColor` / `shaderEmissionColor` de la categoría de ese ítem; si no, los `tintColor` / `emissionColor` del perfil.

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

> **Corregido (22/09)**: `ItemPsx.mat`, el material del renderer de `InventoryItemFather/InventoryItem.prefab` (el Father de los pickups), tuvo `_OutlineIntensity = 0.12` desde el commit `deeb0df2` (11/09), así que toda variante con ese material mostraba el rim. Volvió a 0, como el default del shader y los `mat_item_*`. Los únicos materiales de `ItemPSX_Outline` con outline encendido son los de prueba (`Materials/Dev/PSXTestbed/mat_tb_*` y `SaveOutline.mat`, sólo en la escena Dev `TestIñaki`).

### 5.5 `ItemGlint.cs` — Destello de lejos (estilo Resident Evil)

Una estrella de 4 puntas que parpadea sobre cada item recogible cada ~2 s, para que un objeto tirado en un rincón oscuro se vea desde el otro lado de la habitación. Es el complemento **de lejos** del highlight de §5.3, que actúa **de cerca**: los dos nunca se ven a la vez.

**Dónde va**: en la raíz del pickup, en los Father (`InventoryItemFather/InventoryItem`, `NoteFather/Note`), así las variantes lo heredan. `Tools ▸ Interactables ▸ Set Up Item Glints` crea `mat_item_glint.mat` y `ScriptableObjects/Highlight/SO_Glint_Items.asset` si faltan, agrega el componente a todo prefab de `Prefabs/` con `PickupInteractable` en la raíz que no sea variante de otro pickup, y a los pickups armados a mano en las escenas abiertas (esas escenas quedan sucias para guardar). Se puede volver a correr.

**Cuándo se ve** (todo sale del perfil; valores de `SO_Glint_Items.asset`):

| Condición | Resultado |
|---|---|
| Player más cerca que `hideWithin` (1.5 m) | Apagado: manda el highlight. Hace fade sobre `fadeBand` (0.85 m). |
| Player más lejos que `maxDistance` (12 m) | Apagado, con el mismo fade. |
| La mira apunta al item (`InteractionEvents.OnTargetChanged`) | Fade out en `targetFadeTime` (0.12 s). |
| Pickup sin item (`CanInteract()` = false) | Nunca brilla: no hay nada que encontrar. |
| Item recogido (destruido) o desactivado | Deja de dibujarse solo (se dibuja desde `Update`). |

> El tooltip de `hideWithin` pide dejarlo **en o por encima** del Interaction Distance de `SO_InteractionManager` (el asset hoy tiene 3 m; el default del script es 2.5), para que la estrella nunca se vea cuando la mira ya puede agarrar el ítem. Con 1.5 m hay una franja de 1.5–3 m donde el destello y el prompt `[E]` conviven; lo único que lo apaga ahí es apuntarle (`targetFadeTime`).

Tiempos: `interval` 2 s ± `intervalJitter` 0.75 s, `flashDuration` 0.65 s, `spinDegrees` 45.

**Color**: `color` del perfil (blanco HDR, ×3.44) mezclado con el **tono** del color de la categoría (`ItemCategory.asset → shaderEmissionColor`, el mismo que usa el highlight): llave roja, componente verde, nota azul, especial naranja. `categoryTint` (0.75 en el asset; 0.45 el default del script) dice cuánto; se toma solo el tono, llevado a brillo máximo, así el azul oscuro de las notas tiñe igual que el rojo puro de las llaves. "Otro" (sin color) queda blanco.

**Cómo está implementado**:
- **Sin GameObject hijo**: se dibuja con `Graphics.RenderMesh` y un `MaterialPropertyBlock` (color, alpha, rotación). Un quad hijo sería un `Renderer` más bajo el interactuable, y el highlight maneja —y su validador juzga— todos los renderers de abajo.
- **Billboard en el shader** (`ItemGlint.shader`): el script solo pasa posición y escala; el vertex arma el quad mirando a cámara. La estrella nace en el centro de los renderers del item, así que el shader la **desliza hacia la cámara** (`_CameraPull`, 0.3 m) sobre la línea de visión: sale del mesh del propio item sin cambiar de lugar ni de tamaño en pantalla. `ZTest LEqual` sin `ZWrite`: las paredes la tapan.
- **Estrella procedural**: sin textura. Forma en el material (`_RayThinness`, `_RayFalloff`, `_CoreSize`, `_PixelGrid`); también acepta un sprite propio (`_UseSprite` + `_MainTex`).
- **Tamaño mínimo en pantalla** (`minScreenHeight`, 5 % del alto en el asset; 4.5 % el default): el filtro PS1 (§7) se queda con un texel por bloque de 256 filas, y una estrella de pocas filas titila al mover la cámara. De lejos crece en mundo para no bajar de ese piso.
- Queue `Transparent+50`: la niebla (`BeforeRenderingPostProcessing`) y el PS1 (`AfterRenderingPostProcessing`) le caen encima como a todo lo demás. La niebla deja pasar píxeles brillantes (`lightPreservation`), así que un destello más allá de `visionEnd` puede verse como un punto en la oscuridad — si delata items de más, bajar `maxDistance`.
- Primer destello de cada item sorteado dentro de un intervalo entero, y `intervalJitter` en cada uno: items puestos juntos no parpadean al unísono.

**Tunear en Play**: click derecho sobre el componente ▸ **Glint Now** dispara uno ya (igual respeta `hideWithin`: alejarse primero). Con el item seleccionado, el gizmo marca dónde nace la estrella y las esferas de `hideWithin` y `maxDistance`. Si la estrella cae en un lugar raro en un item puntual, `offset` en el componente la corre.

---

## 6. Sistema de vision fog (post-process atmosférico)

Niebla estilo Silent Hill, centrada en el player, con rango y look definidos por presets de zona (`SO_VisionFogConfig`), un "hueco" de visibilidad alrededor de las luces del módulo del player, zonas de bypass ancladas al mundo, y beacons / haces que se leen a través de la niebla (§6.4.1).

### 6.1 `VisionFog_HLSL.shader` — el shader del fog

Shader fullscreen `Hidden/Custom/VisionFogHLSL` (`Art/Materials/Post Process/VisionFog_HLSL.shader`) para el `FullScreenPassRendererFeature` "Vision Fog". Reemplazó a `Fullscreen_VisionFog.shadergraph` + `VisionFog.hlsl`, que quedan en la carpeta como **legacy / rollback**: ningún material los usa y `VisionFog.hlsl` no tiene los arreglos de abajo, así que no se editan.

Por qué se reescribió (está en el encabezado del shader): el grafo hacía `lerp(escena, fogColor, f)`, que a `f = 1` pinta la pantalla del color plano de la niebla; la preservación de luces saturaba sobre el buffer HDR y la niebla desaparecía; y el skybox no se nublaba en PC porque comparaba el depth contra 1 y con reversed-Z el far plane es 0.

**Modelo** (Beer-Lambert, extinción e in-scattering separados):

```
t            = saturate((distance(worldPos.xz, _PlayerPos.xz) - _VisionStart) / (_VisionEnd - _VisionStart))
t            = pow(t, _FogFalloffPower)
opticalDepth = t × _FogDensity × noise                     ← el noise modula densidad, no color
clear        = max(preservación de luces, bypassClear, luz del player × _PlayerLightClear)
opticalDepth *= 1 - clear
opticalDepth = lerp(opticalDepth, _FogDensity × 4, skyMask) ← el cielo siempre hundido

sceneColor  += luz inyectada por los bypass + _PlayerLightColor × máscara × _PlayerLightInjection
sceneColor   = lerp(sceneColor, sceneColor × _PlayerLightColor, máscara × _PlayerLightTint)
transmittance = exp(-opticalDepth × _FogExtinctionTint × _FogDarkness)   ← se come la escena hacia NEGRO
inscatter     = (1 - exp(-opticalDepth)) × _FogInscatterStrength        ← suma el color de niebla
result = sceneColor × transmittance + _FogColor × inscatter   (+ blur según cuánta niebla hay)
result += beacons + haces de luz                               ← DESPUÉS de la extinción (§6.4.1)
```

- Distancia **horizontal** (xz): niebla cilíndrica desde el player.
- **Preservación de luces**: la luminancia se comprime con Reinhard, pasa por umbral + rodilla (`_LightThreshold`, `_LightKnee`), cae con la distancia al player (`_LightDistanceFalloff`) y tiene techo (`_MaxLightPreservation`).
- **Bypass**: el *clear* se combina con `max` (dos focos no limpian el doble); la luz que *inyectan* se suma. `_FogBypassPlayerFade` apaga esa luz inyectada donde ya llega la luz del player.
- **Early-out**: si `_EnableVisionFog` está en 0 o `_VisionEnd <= _VisionStart + 0.001`, devuelve la escena sin niebla **pero con los beacons**: los ojos del Nemesis no pueden depender de un toggle de arte. `VisionRangeController` pone `_VisionEnd = 0` mientras no hay player.
- **Noise**: dos capas de FBM en world-space (queda "pegado al mundo"), modulan la densidad en torno a 1. Keyword `_ENABLEFOGNOISE_ON`, octavas con `_FogNoiseOctaves` (1–4).
- **Blur**: 13 taps en dos anillos, radio `_VisionFogBlurStrength` escalado por cuánta niebla hay en el píxel. Keyword `_ENABLEFOGBLUR_ON`.
- **Debug View** (`_FogDebugView` en el material): Transmittance, OpticalDepth, LightMask, Inscatter, BypassMask, Distance, Beacons, Volumes. Para tunear presets; ninguno es un look final.

**Globales** (las escribe C#, van fuera de `UnityPerMaterial` y **no** están en el bloque Properties a propósito: si existieran en el material, el valor del material le ganaría al global). Los colores llegan ya en lineal (ver §6.2).

**Espejo en C#**: `VisionFogState.OpticalDepthAt / VisibilityAt / EvaluateSurface` reimplementan esta matemática para la preview del inspector (§6.3). Si cambia el modelo del shader, hay que cambiarlo también ahí.

### 6.2 `VisionRangeController.cs` — Driver del fog

Setea las globals del shader cada `LateUpdate` y maneja transiciones suaves entre presets. Vive en la escena persistente `Data.unity` (`defaultConfig` = `SO_VisionFog_Dark`).

**Modelo de configuración (config stack)**:
1. `defaultConfig` (un `SO_VisionFogConfig`) es el preset base — la niebla cuando el player no está en ninguna zona especial (pasillos oscuros).
2. `PushConfig(config)` / `PopConfig(config)` son la API para los `LightZone` triggers (§6.3): mantienen un **stack**, la zona más interna (última pusheada) gana. Al salir, vuelve a la anterior. También la usan `VisionFogOverride` (pushea un preset mientras su GameObject está activo; pensado para prenderlo con un Activation Track en una cinemática) y el escape (`EscapeFogCycle`, `EscapeSequenceDirector`).
3. Cada frame relee el config activo (así los sliders del SO se ven en Play sin re-push) y lerpea los valores actuales hacia él (`_lerpRate` derivado del `transitionDuration` del preset). Los `LightZone` anidados funcionan solos.

**Player**: sale de `PlayerRegistry` (`SubscribeAndCatchUp`; lo registra `PlayerStateManager`), no de un tag. Mientras no hay player, setea `_VisionEnd = 0` → early-out del shader → sin fog, y vacía bypass / beacons / volúmenes. El player vive en otra escena (gameplay aditivo), por eso no se puede asignar una referencia serializada cross-scene. `playerOverride` es el enganche manual para previsualizar con Timeline.

**Centro de la niebla**: por default, el player. `SetCentreOverride(transform)` / `ClearCentreOverride(transform)` la miden desde otro punto (una toma de cinemática lejos del player, WIR-040); la luz del módulo se queda en el player.

**Globals que setea**: todas las escalares y de color salen por `VisionFogState.PushToShader()` —el único lugar que las escribe, porque `Shader.SetGlobalColor` **no** convierte sRGB → lineal y en este proyecto (Linear) un color escrito crudo llega 2–3× más brillante—: `_PlayerPos`, `_VisionStart`, `_VisionEnd`, `_FogDensity`, `_FogFalloffPower`, `_FogDarkness`, `_FogExtinctionTint`, `_FogColor`, `_FogInscatterStrength`, `_LightPreservation`, `_LightThreshold`, `_LightKnee`, `_MaxLightPreservation`, `_LightDistanceFalloff`, `_VisionFogBlurStrength`, `_PlayerLight*` (`Position`, `Range`, `Clear`, `Falloff`, `Color`, `Tint`, `Injection`), `_FogBypassFalloff`, `_FogBypassPlayerFade`. Los arrays los arma el controller (convierten con `VisionFogState.ToLinear`): bypass (`_FogLightBypassData` / `Color` / `Axis` / `Count`), beacons (`_FogBeacon*`) y volúmenes (`_FogVolume*`).

**Knobs propios del controller** (Inspector): *Beacons* (`beaconDepthBias`, `beaconMaxPixels`, `beaconFalloff`) y *Light volumes* (§6.4.1). Con el objeto seleccionado dibuja en la escena los anillos de `visionStart` / `visionEnd` y la esfera de la luz del módulo del preset activo.

**Por qué globals y no material properties**: las globals se setean desde C# y aplican a todos los shaders simultáneamente, y agregar una feature es declarar la global en el shader y pushearla desde `VisionFogState`, sin tocar el material.

**Timeline**: `VisionFogTrack` (bindeado a un `VisionRangeController`) con clips `VisionFogClip` que referencian un preset; los clips superpuestos hacen crossfade. Escribe por `ApplyPreviewBlend` salteando el stack: es para scrubbear en el editor, no para gameplay (pelearía con un `LightZone` activo).

### 6.3 `SO_VisionFogConfig.cs` — Preset de niebla por zona

ScriptableObject con el "feeling" de una zona. Crear con: Project → click derecho → Create → Rendering → Vision Fog Config.

Es la **v2** del modelo (`CurrentDataVersion = 2`). Los tooltips están en español y dan valores para probar; lo de abajo es el mapa.

| Grupo | Campos (rango) | Qué hace |
|---|---|---|
| Rango de visión | `visionStart` (≥ 0, def. 2), `visionEnd` (≥ 0, def. 12) | Metros sin niebla / donde llega a densidad completa. `visionEnd <= visionStart` apaga el shader. |
| Densidad | `fogDensity` (0.25–12, def. 4.6), `fogFalloffPower` (0.1–4, def. 1) | Profundidad óptica en `visionEnd` (4.6 = sobrevive el 1 %) y forma de la rampa (>1 = menos niebla cerca y cierre de golpe). |
| Oscuridad (extinción) | `darkness` (0–1), `extinctionTint` | Cuánto se come la escena hacia negro, y qué canal sobrevive más (blanco = neutro). |
| Color (in-scattering) | `fogColor`, `fogColorIntensity` (0–4), `inscatterStrength` (0–1, def. 0) | El color que la niebla **suma** y cuánto. 0 = oscuridad pura; 0.03–0.1 = oscuro con un dejo de color; 1 = Silent Hill. |
| Preservación de luces | `lightPreservation` (0–4), `lightThreshold`, `lightKnee`, `maxLightPreservation`, `lightDistanceFalloff` | Cuánto perforan la niebla los píxeles brillantes, con umbral, rodilla, techo y caída con la distancia. |
| Blur | `blurStrength` (0–0.05) | Radio del desenfoque a densidad plena. Necesita *Enable Blur* en el material. |
| Luces del módulo del player | `playerLightRange` (0–30, def. 8), `playerLightClear` (0–1), `playerLightFalloff` (0.5–8), `playerLightColor`, `playerLightTint` (0–1), `playerLightInjection` (0–4) | Esfera (no cono) alrededor del player: cuánta niebla limpia, curva, color, cuánto multiplica la escena y cuánta luz suma. Un `FogLightSource` puede pisar range / clear / color (§6.4). |
| Focos del mundo | `bypassFalloff`, `bypassDefaultColor`, `bypassDefaultIntensity`, `bypassDefaultClear`, `bypassPlayerFade` | Valores que usan los `FogLightBypass` que no traen los suyos (§6.4). |
| Transición | `transitionDuration` (≥ 0) | Segundos de lerp al activarse este config. |
| Siluetas | `silhouetteMode` (None/Items/Puzzles/All) | GD pendiente (§3.7 del handoff). **No implementado** en el shader. |

**Migración v1 → v2**: los campos viejos `densityPower` y `playerLightIntensity` quedan ocultos sólo para leer los assets viejos. `Migrate()` corre solo en `OnEnable` / `OnValidate` si `dataVersion < 2` (también en build) y se puede forzar con el context menu *Migrate v1 values → v2*. `densityPower` pasa tal cual a `fogFalloffPower` (el tooltip viejo lo describía al revés).

**Inspector** (`SO_VisionFogConfigEditor` + `VisionFogPreviewDrawer`): una preview calculada con `VisionFogState` (rampas de color de una pared de referencia afuera y adentro de la luz del módulo, curva de visibilidad, y a qué distancia se pierde el 50 % / 90 %), que anda sin escena cargada; y botones **Aplicar como Default** (escribe `defaultConfig` del controller, queda en la escena) y **Previsualizar** / **Limpiar preview** (escriben los globals y nada más).

**Presets** (`ScriptableObjects/Rendering/Fog/`): `SO_VisionFog_Dark` (default: 2 → 6.8 m, densidad 5.43, in-scattering rojo 0.033), `SO_VisionFog_Darkness`, `SO_VisionFog_Light` (10 → 25 m; lo pushea el `LightZone` de `Light Base`) y `SO_VisionFog_SilentHill`. El escape tiene los suyos en `ScriptableObjects/Escape/SO_VisionFog_Escape*.asset`.

Los `LightZone` (`_Project/Scripts/Rendering/LightZone.cs`) son trigger volumes que pushean/popean un config al entrar/salir el player (tag `playerTag`, default `"Player"`) — así una safe room, un boss arena o un pasillo pueden tener cada uno su niebla.

### 6.4 `FogLightSource.cs` y `FogLightBypass.cs` — Fuentes de luz del fog

Dos componentes que alimentan las globales de la luz del módulo y del bypass.

**`FogLightSource`** — va en el objeto del player que tiene la Light del módulo (§3.3). El controller lee su posición cada frame (sin él, la luz se centra en el pivot del player). **Hoy no está puesto en ningún prefab ni escena.** Se empareja con el controller en cualquier orden de carga (se busca desde los dos lados). Dos modos:
- `useLightComponent = true` (default) — toma de la `Light` (`lightComponent`, o la del mismo objeto) y pisa los valores del SO: range = `Light.range × rangeMultiplier`, clear = `saturate(Light.intensity × intensityMultiplier)`, color = `Light.color`. Así, si se implementa la degradación de la luz por módulos explotados (§2.5.1 del handoff), el radio del hueco baja solo, sin código extra.
- `useLightComponent = false` — usa los valores `playerLight*` del `SO_VisionFogConfig` activo.
- `rangeMultiplier` (0.25–5, default 2) — el radio del fog puede ser mayor que el `Light.range` visible, para que el player vea más lejos que lo que la luz ilumina físicamente. `intensityMultiplier` (0–5, default 1).

**`FogLightBypass`** — se pega en objetos anclados al mundo (lámparas, monitores, hogueras) donde la niebla debe disolverse localmente aunque el player no esté cerca. Se registra/desregistra en el controller vía `RegisterBypass` / `UnregisterBypass` estáticos; tope `MaxBypassZones` = `VISION_FOG_MAX_BYPASS` = 16.
- Forma: `radius` (m, 0 = no hace nada), `centerOffset` (local, como el Center de un SphereCollider), `shape` = `Sphere` o `Cone` (`coneAngle`; si `Light Component` es una Spot usa su forward y su Spot Angle).
- Aspecto, con **aclarar** y **brillar** separados: `clearAmount` (cuánta niebla disuelve; entre zonas se combina con `max`) e `intensity` + `color` (cuánta luz inyecta; se suma). Una lámpara vista a través de niebla espesa quiere intensity alto y clear bajo.
- De dónde salen: si tiene `Light Component` asignada y activa, color e intensidad salen de la Light (`× lightIntensityScale`) y el clear del componente; si no, con `overrideAppearance` usa sus propios campos; si no, los `bypassDefault*` del preset activo.
- **Ojo (bug conocido, sin arreglar)**: el tooltip dice que con la Light apagada la zona deja de aportar, pero `Resolve()` sólo lee la Light si está `isActiveAndEnabled`; con la Light asignada y apagada cae a `overrideAppearance` o a los defaults del preset y sigue brillando/limpiando. Apagar la lámpara desactivando el GameObject entero (como hace `PoweredLightSwitch`) no tiene el problema.
- Gizmo al seleccionar: esfera o cono en el color de la zona (el de la Light, el override, o amarillo). Se oculta con el `GizmoManager` → *Luz y niebla*.

**`FogLightBypassPlayerFade`** — al lado de un `FogLightBypass` con `Light Component` y un `SphereCollider` trigger: baja `LightIntensityScale` del bypass de `outsideScale` (1) a `insideScale` (0.3) a medida que el player entra a la esfera del collider (no al `radius` del bypass), con curva `falloff` y `responseSpeed`. Para que un halo pensado para leerse de lejos no encandile parado debajo.

**`Light Base.prefab`** (`Prefabs/Light/`) es la lámpara estándar que junta todo eso: una Spot (range 5, intensity 15), un `FogLightBypass` cono que lee esa Light, `FogLightBypassPlayerFade`, un `SphereCollider` trigger de 2.5 m y un `LightZone` que pushea `SO_VisionFog_Light`.

> **Alternativa más fiel al spec §3.4**: en vez del componente estático, disparar `VisionRangeController.RegisterBypass(...)` desde `ZoneLightController.OnActivate()` (evento del generador) y `UnregisterBypass(...)` en `OnDeactivate()`. Migrable cuando exista ese sistema.

#### 6.4.1 `FogBeacon` y `FogLightVolume` — luces que se leen a través de la niebla

El brillo de un `FogLightBypass` se suma **antes** de la extinción, así que pasado `visionEnd` llega multiplicado por ~0.004 (preset Dark). No hay intensidad que lo haga leer de lejos sin quemar lo que esté debajo, y el player y el Nemesis son **Unlit**: ninguna Light real los ilumina, lo único que los aclara es este shader. Por eso lo que tiene que verse de lejos se compone **después** de la extinción:

- **`FogBeacon`** — un punto en pantalla, con piso en píxeles y tapado por la geometría. No ilumina nada. Lo usan los ojos del Nemesis (`NemesisEyes`), las luces guía del escape (`EscapeGuideDoor`), las sirenas del escape (`EscapeAlarmLights`) y las lámparas del switch. `IntensityScale` es el multiplicador de runtime para que un driver lo module sin tocar `intensity`. Tope: `MaxBeacons` = `VISION_FOG_MAX_BEACONS` = 16. Entran por orden de registro y los ojos se re-registran al despertar el Nemesis, así que un array lleno tira los ojos primero.
- **`FogLightVolume`** — el aire que ilumina una Light: el cono de una Spot (o la esfera de una Point) integrado a lo largo del rayo de vista (`vfLightVolumes`). Es el look de "se ve por dónde pasa la luz", sólo en las lámparas que lo llevan. Forma, alcance, color y dirección salen de la Light; si la Light o el objeto se apagan (o la Light queda en intensity 0), el haz también, y una Light atenuada por debajo de su primera intensidad encendida (un flicker) atenúa el haz en proporción. Por componente: `intensity` (brillo absoluto del haz, def. 0.8) y `rangeScale`. Los knobs globales están en `VisionRangeController` → *Light volumes*: cuánto los apaga la niebla (`volumeFogExtinction`), el fundido cerca del player (`volumeNearFade*`, para que el haz entre la cámara y el personaje no le deje una pátina) y el polvo (`volumeDust*`). Tope: `MaxLightVolumes` = `VISION_FOG_MAX_VOLUMES` = 16.

Cambiar cualquiera de los topes pide reiniciar el editor: un array global conserva el largo de su primer upload durante toda la sesión.

**`Light Base Switch.prefab`** (variante de `Light Base`, para las lámparas que prende un `PoweredLightSwitch`) junta las piezas: FogBeacon en el artefacto, FogLightVolume, un `FogLightBypass` esfera que sólo limpia niebla (intensity 0, clear 0.8, centrado en la pasarela) para que se vea el charco que pinta la Spot, y la Spot con range ≈ 1.6× la altura. No lleva `FogLightBypassPlayerFade`. Como el bypass no lee la Light (`overrideAppearance`, sin `Light Component`), no lo afecta el bug de `Resolve()`; el switch apaga la lámpara desactivando el GameObject entero, que se lleva las tres piezas.

Las **sirenas del escape** (`EscapeAlarmLights`, `Scripts/Escape/`) arman la misma receta en runtime sobre las lámparas `Light Base` del pasillo: FogBeacon en la lámpara, bajado para que no lo tape el techo; el bypass convertido en pool sólo-clear; FogLightVolume; y la Spot en intensity 0 (no desactivada) en la mitad apagada del ciclo. Al terminar deja todo como estaba.

### 6.5 `VisionFog.mat` — Material instancia del shader fullscreen

Es la instancia de `VisionFog_HLSL.shader`. Asignado al **Full Screen Pass Renderer Feature** "Vision Fog" en `PC_Renderer.asset`.

**Properties tunables** (estos sí son material-local, no global):
- `_EnableVisionFog` — master (lo lee `SO_PostProcessToggle`). En 0 la escena pasa limpia, con beacons.
- `_EnableFogNoise` + `_FogNoiseScale` / `_FogNoiseIntensity` / `_FogScrollSpeed` / `_FogNoiseOctaves` — el noise (defaults del shader: 0.05 / 0.4 / 0.1 / 3).
- `_EnableFogBlur` — prende el blur (el radio sale del preset, `blurStrength`).
- `_FogDebugView` — vistas de debug (§6.1).

Hoy `VisionFog.mat` tiene el **noise apagado** (`_EnableFogNoise` 0, escala/intensidad/velocidad en 0) y el blur prendido. `VisionFog_SilentHill.mat` es la misma con el noise prendido (0.05 / 0.4 / 0.1); no está en el renderer, sólo en `SO_PostProcessToggle`.

**`SO_PostProcessToggle.asset`** (`ScriptableObjects/`): su inspector prende/apaga de un botón el PS1 (`_EnableEffect`), el fog (`_EnableVisionFog` en los dos `.mat`) y las dos renderer features, leyendo el estado real y con Undo. Apagar la feature ahorra el pass; apagar sólo el material deja el blit corriendo.

### 6.6 Orden en el Renderer Feature stack

```
PC_Renderer.asset (en orden de ejecución):
  1. ScreenSpaceAmbientOcclusion                      (existente)
  2. Full Screen Pass "Vision Fog"  → VisionFog.mat   (BeforeRenderingPostProcessing, 550)
     … post-process de URP (Volumes) …
  3. Full Screen Pass "PSXEffect"   → PS1Effect.mat   (AfterRenderingPostProcessing, 600)
```

En la lista del asset están como SSAO, PSXEffect, Vision Fog, pero manda el injection point. Vision Fog corre antes del post-process de URP, así que lo que suma después de la extinción (beacons, haces) todavía pasa por lo que tengan los Volumes (el Global Volume de `Data.unity` trae Color Adjustments y Lift Gamma Gain). El PS1, en cambio, corre después. Vision Fog va antes del PSX porque el fog se calcula con world-space coherente (depth + matrices reales); el PSX pixela y warpea la imagen final. Si se invirtiera el orden, la niebla quedaría deformada siguiendo el warp en vez de tener forma natural.

---

## 7. Filtro PS1 (post-process PSX)

Filtro fullscreen que le da el carácter retro final a toda la imagen. Originalmente un Shader Graph (`PS1_PostProcess.shadergraph`, hacía pixelation + warp + jitter); reemplazado por un **shader HLSL** `Hidden/Custom/PS1PostProcessHLSL` (`Art/Materials/Post Process/PS1_PostProcess_HLSL.shader`) que preserva esas features y agrega dither, scanlines y chromatic aberration, todas toggle-ables desde el inspector. El `.shadergraph` original queda como referencia; `PS1Effect.mat` apunta al shader HLSL, y el `FullScreenPassRendererFeature` (`PSXEffect` en `PC_Renderer.asset`) sigue apuntando al mismo `.mat` — el pipeline agarra el shader nuevo sin tocar el renderer.

### 7.1 Properties (todas en `PS1Effect.mat`)

| Grupo | Property | Default (shader) | Descripción |
|---|---|---|---|
| Master | `_EnableEffect` | 1 | Toggle global. En 0 devuelve la escena limpia. |
| Pixelation/Warp | `_PixelSize` / `_WarpStrength` / `_WarpIntensity` / `_JitterResolution` / `_ClampWarpEdges` | 256 / 0.1 / 0.0018 / 0 / 1 | `_PixelSize` es la **cantidad de filas** de la grilla (más alto = menos pixelado). Warp = ondulación CRT; `_ClampWarpEdges` evita que las esquinas estiren el último píxel. `_JitterResolution` **no es un jitter**: es una segunda grilla de pixelado, estática, que corre antes; si es menor que `_PixelSize` es la que manda. 0 = apagada. |
| Dither | `_EnableDither` / `_DitherStrength` / `_DitherLevels` | 1 / 0.5 / 4 | Bayer 4×4 con posterización (spec §6.10). |
| Scanlines | `_EnableScanlines` / `_ScanlineIntensity` / `_ScanlineCount` | 1 / **0.06** / 240 | Bandas CRT. Default 0.06 = casi imperceptible (spec §6.10). |
| Chromatic Aberration | `_EnableChromaticAberration` / `_ChromaticAberrationOffset` | 1 / **0** | RGB split. Offset 0 por default — lo pulsa el `GlitchController` (§7.2). |

Los valores de `PS1Effect.mat` no son los defaults: hoy tiene, entre otros, `_JitterResolution` 256, `_WarpStrength` 0.025, `_DitherStrength` 0.05 con 8 niveles, `_ScanlineIntensity` 0.125 con 150 líneas, y **`_ChromaticAberrationOffset` = 0.00249** (con la property bloqueada en `m_LockedProperties`): la CA está siempre prendida, no sólo durante el glitch.

**Pipeline del fragment**: warp CRT → segunda grilla (`_JitterResolution`) → pixelation → sample (con CA opcional) → dither posterization → scanline modulation.

**Compatibilidad con el applier**: [`PS1EffectApplier.cs`](../Assets/_Project/Scripts/Rendering/PS1EffectApplier.cs) sigue funcionando sin cambios — escribe `_EnableScanlines` y `_EnableDither` como floats desde PlayerPrefs (`Settings_CRTScanlines` / `Settings_PSXDithering`), que ahora existen en el shader. Es el enganche con el toggle de accesibilidad de Options.

### 7.2 `GlitchController.cs` — Glitch VHS aleatorio (NUEVO)

El spec §6.10 pide que la chromatic aberration sea parte de un **glitch VHS aleatorio**, no un efecto continuo. El controller pulsa `_ChromaticAberrationOffset` sobre `PS1Effect.mat`:

- Intervalo entre glitches: `Random(8, 45)` s. Duración: `Random(0.1, 0.4)` s. (spec §6.10)
- Usa `Time.unscaledTime` → sigue corriendo en pausa (el spec pide glitch durante menús/pausa).
- Accesibilidad: lee `PlayerPrefs "Settings_VHSGlitch"` (1=on) y se suscribe a `SettingsModel.OnSettingsApplied`. Cuando Options exponga el toggle, basta escribir esa key + `RaiseSettingsApplied()`.
- Gate por modales: setear `GlitchController.SuspendTriggering = true` al abrir Inventory/SkillCheck/Examine y `false` al cerrar (spec §6.10: no dispara ahí).

**Setup**: componente en un GameObject persistente (o el mismo del `PS1EffectApplier`, que está en `Data.unity`), arrastrar `PS1Effect.mat` al campo `Ps1 Material`. **Hoy no está puesto en ninguna escena ni prefab**, así que el glitch no dispara en juego. `UISignalStaticBurst` (UI) lee `GlitchController.SuspendTriggering` y la misma key `Settings_VHSGlitch`.

---

## 8. Reglas y convenciones del spec aplicadas

| Regla del spec | Cómo lo respeta el sistema |
|---|---|
| Rojo solo para peligro | En el mundo 3D, `mat_luz_emergencia_emissive`, el LED de módulo explotado (`ModuleLED`) y las sirenas del escape. En la UI `#CC1A1A` es el acento único (`SO_UIThemeConfig.Accent`), decisión explícita: la regla es del mundo 3D. **Desvío sin decisión registrada**: la categoría Llave es roja (`ItemCategory.asset`, `mat_item_keys_red`, §5.2), y el preset default `SO_VisionFog_Dark` tiene in-scattering rojo (0.033). |
| Ámbar #FFC850 solo para módulos | Solo `mat_device_luz_ambar_jugador` (y el `playerLightColor` del fog, que es la misma luz). Ojo: el `emissionColor` de fallback de `SO_Highlight_Items` es ámbar, aunque con `categoryConfig` asignado no lo usa ningún pickup (§5.3). **Excepción (22/09, pedido de Iñaki):** las sirenas del pasillo del escape (`EscapeAlarmLights`) alternan rojo `#CC1A1A` (peligro, que es lo que son) y el ámbar del escape `(1, 0.72, 0.38)`, el de sus luces del camino. Colores en `SO_EscapeSequenceConfig` → *Sirenas del pasillo*. |
| Azul/blanco frío #8AB4D4 solo para monitores | Solo `mat_monitor_pantalla`. |
| Verde solo para "módulo alimentado / núcleo colocado" | `SocketEmissionShift` (zona emisiva de las estaciones de núcleo al insertar la pieza, HDR `(0.25, 2.4, 0.55)`) y el LED de módulo resuelto (`ModuleLED`). **Desvío sin decisión registrada**: la categoría Componente es verde (`ItemCategory.asset`, `mat_item_components_green`, §5.2). |
| Sin outline detective-mode (Sec 4.6.1) | El outline fresnel de `ItemPSX_Outline` viene **apagado** (`_OutlineIntensity = 0` en el shader y en los `mat_item_*`). Solo se activa manualmente en puzzles/decorativos del §4.7, nunca en items recogibles. Items se distinguen por tinte+emisión sutil. `ItemPsx.mat`, el del Father de los pickups, estuvo en 0.12 hasta el 22/09 y volvió a 0 (§5.4). |
| Sin waypoints, mapa, partículas sobre items | No hay waypoints ni mapa. **Excepción pedida**: el destello estilo Resident Evil de §5.5 — una estrella que parpadea sobre el pickup cada ~2 s, solo de lejos (se apaga a menos de `hideWithin` o al apuntarle). No es un sistema de partículas ni un marcador permanente; si GD lo objeta, se saca quitando `ItemGlint` de los Father. |
| Lerp 0.15→0.4 (tint) y 0.0→0.2 (emission) en 0.3s | Es lo que `Set Up Highlights` escribe al crear `SO_Highlight_Items`, pero el asset hoy tiene 0.195→1 y 0.127→1.502 (subido el 16/09). La duración sí es 0.3 s. Los puzzles y dispositivos usan sus perfiles sin tinte (§5.3). Se afinan en el asset, no por item. |
| Cuatro categorías con hex específicos | **Ya no**: los `mat_item_*` y `ItemCategory.asset` usan rojo / verde / azul / naranja en vez de los hex del spec sec 4.4 (§5.2). |
| Estilo PSX (sin PBR realista) | Smoothness baja en todos los materiales. Filtro PS1 (§7) aplica encima como efecto final. |
| Scanlines/dither/glitch con toggle de accesibilidad | `_EnableScanlines` / `_EnableDither` vía `PS1EffectApplier` + PlayerPrefs; glitch VHS vía `GlitchController` + `Settings_VHSGlitch` (spec §6.10) — aunque hoy el `GlitchController` no está en ninguna escena y la CA de `PS1Effect.mat` queda fija en 0.00249 (§7). |

---

## 9. Verificación y debug

### Cómo verificar que el sistema completo anda

**Vision fog**:
1. Abrí `Window → Analysis → Frame Debugger` → Enable.
2. Buscá el draw call del Vision Fog ("Draw Fullscreen").
3. En el panel derecho, sección Globals:
   - `_PlayerPos` = posición real del player (no `(0,0,0)`), o el centro de la cinemática si hay `SetCentreOverride`.
   - `_VisionEnd` > 0 (sino el early-out dispara y no hay fog).
   - `_PlayerLightPosition` = pos del `FogLightSource`, o del player si no hay (hoy no hay); `_PlayerLightRange` = `playerLightRange` del preset, o `Light.range × rangeMultiplier` con `FogLightSource`.
   - `_FogLightBypassCount` / `_FogBeaconCount` / `_FogVolumeCount` = bypass, beacons y haces activos.
4. Movéte con el player → la zona sin niebla debe seguirlo. Pasá cerca de un `FogLightBypass` → la niebla se abre local.
5. Para ver cada término por separado: `VisionFog.mat → Debug View` (§6.1). Para ver un preset sin Play: su inspector (§6.3).
6. Con un `FogLightSource` puesto: bajá `Light.range` en runtime → el hueco se achica (valida la degradación futura de §2.5.1).

**Items**:
1. Cubo con `ItemPsx.mat` + `PickupInteractable` + `ItemProximityHighlight` con perfil `SO_Highlight_Items`.
2. Sin apuntarlo: el cubo se ve con el estado *far* del perfil (tinte leve del color de su categoría).
3. Al apuntarlo con la mira (`OnPlayerEnteredRange()`): el cubo "respira" — gana tinte y emisión. Transición 0.3s.
4. (Outline) Subir `_OutlineIntensity` en runtime sobre un cubo y una esfera → el borde sigue la forma en ambos, confirmando que funciona en cualquier mesh.
5. Verificar en Profiler que el SRP Batcher está activo y NO se instancia el material.

**Destello de items** (§5.5):
1. `Tools ▸ Interactables ▸ Set Up Item Glints` una vez; el log lista qué creó y a qué prefabs lo agregó.
2. En Play, a más de ~2.4 m de un item (`hideWithin` 1.5 + `fadeBand` 0.85): cada ~2 s aparece la estrella, teñida con el color de su categoría.
3. Caminar hacia él: se apaga a menos de 1.5 m (con los valores de hoy, después de que aparezca el prompt `[E]`, ver §5.5). Apuntarlo con la mira: se apaga en ~0.1 s.
4. Poner una pared entre la cámara y el item: la estrella queda tapada.
5. Recogerlo: no queda ninguna estrella flotando.

**Filtro PS1**:
1. Seleccionar `PS1Effect.mat` en Play y togglear cada `_EnableXxx` → dither / scanlines / RGB shift aparecen o desaparecen en tiempo real.
2. Con un `GlitchController` en escena (hoy no hay, §7.2) y `_ChromaticAberrationOffset` del material en 0: esperar 8–45 s → flash rápido de CA (glitch VHS). Si nunca aparece: `PlayerPrefs.SetInt("Settings_VHSGlitch", 1)` o activar en Options.

**Flickers**:
1. Monitor: en Play, el emission pulsa entre 0.9× y 1.0× cada 5 segundos.
2. Tubo fluorescente: parpadeo según la curva. Duplicar 3 tubos con offsets distintos (0, 0.33, 0.66) → desfasados.

### Problemas comunes

| Síntoma | Causa probable | Fix |
|---|---|---|
| No hay niebla en una escena abierta sola | No está cargada `Data.unity` (ahí vive el `VisionRangeController`) o no hay player registrado → `_VisionEnd = 0` → early-out | Cargar `Data` en aditivo; el player se registra solo en `PlayerRegistry` (`PlayerStateManager`). Para preview sin Play: `playerOverride` o el botón *Previsualizar* del preset. |
| La niebla se ve plana / gris en vez de oscura | `inscatterStrength` alto con un `fogColor` claro | Bajar `inscatterStrength` (0.03–0.1) y oscurecer con `fogDensity` / `darkness` (§6.3). |
| Un color del fog sale 2–3× más brillante | Un global de color escrito con `Shader.SetGlobalColor` fuera de `VisionFogState` (no convierte a lineal) | Escribirlo por `VisionFogState.PushToShader` o convertir con `VisionFogState.ToLinear`. |
| El hueco de la luz del módulo no aparece | `playerLightRange` o `playerLightClear` del preset en 0, o un `FogLightSource` con `useLightComponent` sobre una Light con range/intensity 0 | Revisar el preset activo (y la Light, si hay `FogLightSource`). |
| Una lámpara apagada sigue brillando/limpiando la niebla | Bug de `FogLightBypass.Resolve()` con la `Light Component` asignada pero deshabilitada (§6.4) | Apagar desactivando el GameObject, o usar `overrideAppearance` sin Light. |
| Un beacon o un haz no aparece con muchas luces prendidas | Se llenó el array (16 beacons, 16 volúmenes, 16 bypass): entran por orden de registro | Liberar slots (beacon a escala 0, Light en 0) o subir el tope en C# y en el shader, y reiniciar el editor. |
| `.mat` muestra "Hidden/InternalErrorShader" | GUID del `.meta` del shader mal formado o colisión | Revisar el GUID/fileID del YAML (para `.shader` es `fileID: 4800000`). |
| El glitch VHS nunca dispara | No hay `GlitchController` en la escena (hoy no hay), `Settings_VHSGlitch` en 0 o `Ps1 Material` sin asignar | Ponerlo en un GameObject persistente, setear la key a 1 y arrastrar `PS1Effect.mat`. |
| Items siempre brillan | El perfil tiene `farEmission` > 0 (`SO_Highlight_Items` tiene 0.127 a propósito: un brillo leve en reposo) | Revisar el `SO_HighlightProfile` (`ScriptableObjects/Highlight/`). |
| Un interactuable no reacciona a la mira, o solo parte de él | Sin `ItemProximityHighlight`, sin perfil, o piezas con un shader sin propiedades de highlight | `Tools ▸ Items ▸ Validate Interactable Highlights` dice cuál y por qué. |
| Material instanciado por cada item (rompe batcher) | El script no usa `MaterialPropertyBlock` | Verificar que esté usando `GetPropertyBlock/SetPropertyBlock`. |

---

## 10. Lo que falta / fases futuras

- **Degradación de la luz del dispositivo por módulos explotados** (§2.5.1 del handoff). Con `FogLightSource.useLightComponent = true`, el fog ya se degrada solo al bajar `Light.range`/`intensity` — falta el sistema que dispare esa degradación, y antes poner un `FogLightSource` en el player (hoy no hay ninguno, §6.4). Los eventos a escuchar son los de `ModuleEvents` (`Scripts/Player/Modules/`, los publica `ModuleManager`): `OnExploded`, o `OnPenaltyApplied` si tiene que esperar a que termine la cinemática de la explosión. Los módulos se activan con un `ZoneTrigger` en la entrada de cada zona de puzzle.
- **`GlitchController` no está en ninguna escena**, y `PS1Effect.mat` tiene la CA fija en 0.00249 (§7). Decidir si el glitch va (poner el componente y dejar el offset del material en 0) o si la CA constante es el look buscado.
- **`GlitchController.SuspendTriggering` no lo setea nadie.** El spec §6.10 pide que el glitch VHS no dispare con inventario / skill check / examine abiertos. La property existe pero ningún controller la sube a `true`. Lo más limpio sería suscribirse a `UIStateManager.OnModalPushed/Popped`.
- **VHS vertical shift** (`_VHSShift`) del §6.10 — el `GlitchController` pulsa la CA pero no el desplazamiento vertical de líneas. Agregar la property al shader PS1 y al controller.
- ~~**Ojos del Nemesis a través de la niebla** (§3.6)~~ — hecho: `NemesisEyes` pone un `FogBeacon` en cada ojo (§6.4.1).
- **Borrar el fog legacy** (`Fullscreen_VisionFog.shadergraph` + `VisionFog.hlsl`) cuando se dé por bueno `VisionFog_HLSL.shader`; hoy quedan sólo como rollback.
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
| Color de un item de categoría | `ItemCategory.asset` (`ScriptableObjects/CategoryConfig/`) → `shaderTintColor` / `shaderEmissionColor`: en Play el highlight pisa el color del `.mat` (§5.2). |
| Velocidad de la transición lejano↔próximo | `lerpDuration` del `SO_HighlightProfile` (`ScriptableObjects/Highlight/`). |
| Color / intensidad del resaltado de puzzles y dispositivos | El `SO_Highlight_*` de esa familia (§5.3): `Interactables`, `Cores`, `Doors` o `ElectricPanel`. |
| Frecuencia / tamaño / rango / cuánto color de categoría tiene el destello de items | `SO_Glint_Items.asset` (`ScriptableObjects/Highlight/`), ver §5.5. |
| Forma de la estrella del destello (rayos, núcleo, pixelado) | `mat_item_glint.mat` (`Art/Materials/Items/`). |
| Activar/afinar el outline de un puzzle | `_OutlineColor` / `_OutlineIntensity` / `_OutlinePower` en el `.mat` del interactuable (solo puzzles §4.7, ver §5.4). |
| Color / rango de la niebla de una zona | El `SO_VisionFogConfig` de esa zona (`visionStart`, `visionEnd`, `fogColor` + `inscatterStrength`). |
| Sensación de opresión de la niebla sin tocar el rango | `SO_VisionFogConfig.fogDensity` / `darkness`, y la forma de la rampa con `fogFalloffPower` (§6.3). |
| Radio del hueco de la luz del módulo | `SO_VisionFogConfig.playerLightRange` (hoy, sin `FogLightSource`); con `FogLightSource` + `useLightComponent`, `rangeMultiplier` + `Light.range`. |
| Zona donde la niebla se abra sin la luz del player | Agregar `FogLightBypass` con `radius` al objeto (o usar `Light Base.prefab`). |
| Que una lámpara se lea de lejos a través de la niebla | `FogBeacon` (el punto) y/o `FogLightVolume` (el haz) — §6.4.1. Receta armada: `Light Base Switch.prefab`. |
| Velocidad del scroll del noise de la niebla | `VisionFog.mat → Enable Noise` + `_FogScrollSpeed` en Inspector. |
| Intensidad de scanlines / dither / pixelado | `PS1Effect.mat` → `_ScanlineIntensity` / `_DitherStrength` / `_PixelSize`. |
| Frecuencia/intensidad del glitch VHS | `GlitchController` → `Interval Range` / `Duration Range` / `Max CA Offset` en Inspector. |
| Frecuencia del flicker del monitor | `MonitorFlicker.flickerSpeed` en Inspector. |
| Patrón de parpadeo del tubo fluorescente | `FlickerLight.flickerCurve` en Inspector (editor visual de AnimationCurve). |
| Threshold del guard del fog (cuándo se desactiva) | `VisionFog_HLSL.shader`, `Frag()`: `if (_VisionEnd <= _VisionStart + 0.001)`. |
| Color de la emisión de emergencia | `mat_luz_emergencia_emissive → _EmissionColor` en YAML o Inspector. |
