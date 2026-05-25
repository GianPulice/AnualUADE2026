# Setup manual en Unity — Shader de items + Vision Fog

Los `.shadergraph` no se generan por código de manera confiable y los `.mat` que apuntan a shaders custom necesitan el GUID que Unity asigna al importar. Por eso estos pasos van en el editor manualmente. **Una sola vez** — después se duplica.

## Estado de los entregables

| Asset | Estado | Acción |
|---|---|---|
| `Assets/Scritps/Items/ItemProximityHighlight.cs` | ✅ Listo | Ninguna, se agrega como componente. |
| `Assets/Scritps/Rendering/VisionRangeController.cs` | ✅ Listo | Pegar a GameObject persistente + asignar player. |
| `Assets/Shaders/VisionFog.hlsl` | ✅ Listo | Lo usa el shader graph como Custom Function. |
| `Assets/Materials/Environment/Emergency/mat_luz_emergencia_emissive.mat` | ✅ Ajustado a #CC1A1A | Ninguna. |
| `Assets/Materials/Environment/Monitor/mat_monitor_pantalla.mat` | ✅ Ajustado a #8AB4D4 | Ninguna. |
| `ItemPSX.shadergraph` | ⏳ Pendiente manual | Ver Paso 1. |
| 4 materiales de items | ⏳ Pendiente manual | Ver Paso 2. |
| `Fullscreen_VisionFog.shadergraph` | ⏳ Pendiente manual | Ver Paso 3. |
| `mat_vision_fog.mat` | ⏳ Pendiente manual | Ver Paso 3. |

---

## Paso 1 — `ItemPSX.shadergraph`

**Crear**: Botón derecho en `Assets/Shaders/` → Create → Shader Graph → URP → **Lit Shader Graph**. Nombre: `ItemPSX`.

**Graph Settings** (panel arriba a la derecha):
- Surface: `Opaque`
- Workflow: `Specular`
- Allow Material Override: `true` (permite que cada material setee su tinte)

**Blackboard** (panel izquierdo) — agregar las siguientes properties:

| Nombre | Tipo | Default | Notas |
|---|---|---|---|
| `_BaseMap` | Texture2D | white | Opcional. Para items con textura. |
| `_BaseColor` | Color (sRGB) | `#4A4A4A` | Gris medio del entorno. |
| `_TintColor` | Color (sRGB) | `#37474F` | Tinte de categoría. **No HDR**. |
| `_TintIntensity` | Float (slider 0–1) | `0.15` | Lo modula `ItemProximityHighlight`. |
| `_EmissionColor` | Color (HDR) | `#37474F` | Mismo tono que el tinte. |
| `_EmissionIntensity` | Float (slider 0–1) | `0.0` | Lo modula `ItemProximityHighlight`. |

**Grafo** (en el editor visual):

```
Base Color:
  Sample Texture 2D (_BaseMap) → output (rgb)
  Lerp(_BaseColor, _TintColor, _TintIntensity) → output
  Multiply los dos → Base Color del Master Stack

Emission:
  Multiply(_EmissionColor, _EmissionIntensity) → Emission del Master Stack

Metallic: 0
Smoothness: 0.1 (mate, look industrial PSX)
```

**Save Asset** (botón arriba a la izquierda).

---

## Paso 2 — Los 4 materiales preset

Crear carpeta `Assets/Materials/Items/`.

Para cada uno: Botón derecho → Create → Material. Asignar shader `Shader Graphs/ItemPSX`.

### `mat_item_keys.mat` — Llaves

| Property | Valor |
|---|---|
| `_TintColor` | `#37474F` (gris azulado metálico) |
| `_EmissionColor` (HDR) | `#37474F` con intensity 1.0 |
| `_TintIntensity` | 0.15 (default lejano) |
| `_EmissionIntensity` | 0.0 (default lejano) |
| Smoothness | 0.2 |

### `mat_item_components.mat` — Componentes (marrón cálido)

| Property | Valor |
|---|---|
| `_TintColor` | `#4E342E` (marrón cálido oscuro) |
| `_EmissionColor` (HDR) | `#5A3A2A` con intensity 1.2 — sesgo a ámbar, "actividad interna" del spec |
| `_TintIntensity` | 0.15 |
| `_EmissionIntensity` | 0.0 |
| Smoothness | 0.15 |

### `mat_item_clues.mat` — Pistas (papel mate)

| Property | Valor |
|---|---|
| `_TintColor` | `#263238` (gris azulado muy oscuro) |
| `_EmissionColor` (HDR) | `#263238` con intensity 0.5 — el más débil de los 4 |
| `_TintIntensity` | 0.15 |
| `_EmissionIntensity` | 0.0 |
| Smoothness | 0.0 (papel totalmente mate) |

### `mat_item_special.mat` — Especiales (azul único)

| Property | Valor |
|---|---|
| `_TintColor` | `#1A237E` (azul oscuro intenso) |
| `_EmissionColor` (HDR) | `#1A237E` con intensity 1.5 — el más visible de los 4 |
| `_TintIntensity` | 0.15 |
| `_EmissionIntensity` | 0.0 |
| Smoothness | 0.3 (acabado pulido sutil) |

---

## Paso 3 — `Fullscreen_VisionFog.shadergraph` + material

**Crear**: Botón derecho en `Assets/Shaders/` → Create → Shader Graph → URP → **Fullscreen Shader Graph**. Nombre: `Fullscreen_VisionFog`.

**Blackboard properties:**

| Nombre | Tipo | Default | Notas |
|---|---|---|---|
| `_PlayerPos` | Vector3 | (0,0,0) | Globala, la setea `VisionRangeController.cs`. |
| `_VisionStart` | Float | 5 | Global. |
| `_VisionEnd` | Float | 20 | Global. |
| `_FogColor` | Color | negro | Global. |
| `_LightPreservation` | Float | 2 | Global. |

> Las 5 propiedades son **globales** — no se asignan en el material, las setea el script en runtime.

**Grafo:**

1. **Scene Color** node → `sceneColor`.
2. **Screen Position** (Default) → `uv`.
3. **Scene Depth (Raw)** → `depth`.
4. **Custom Function** node #1:
   - Type: `File`
   - Source: `Assets/Shaders/VisionFog.hlsl`
   - Name: `WorldFromDepth`
   - Inputs: `uv` (Vector2), `depth` (Float)
   - Outputs: `worldPos` (Vector3)
5. **Custom Function** node #2:
   - Type: `File`
   - Source: `Assets/Shaders/VisionFog.hlsl`
   - Name: `VisionFog`
   - Inputs: `sceneColor` (Vector3), `worldPos` (Vector3), `depth` (Float), `playerPos` (Vector3 ← `_PlayerPos`), `visionStart` (Float ← `_VisionStart`), `visionEnd` (Float ← `_VisionEnd`), `fogColor` (Vector3 ← `_FogColor`), `lightPreservation` (Float ← `_LightPreservation`)
   - Outputs: `result` (Vector3)
6. `result` → output Color del Fragment node.

**Save Asset**.

**Crear `mat_vision_fog.mat`** en `Assets/Materials/PostProcess/`:
- Botón derecho → Create → Material.
- Shader: `Shader Graphs/Fullscreen_VisionFog`.
- No tocar propiedades (van por global).

---

## Paso 4 — Renderer Feature en `PC_Renderer.asset`

En el Inspector del renderer:

1. Agregar **Full Screen Pass Renderer Feature** nuevo:
   - Name: "Vision Fog"
   - Pass Material: `mat_vision_fog`
   - Injection Point: `BeforeRenderingPostProcessing`
   - Fetch Color Buffer: ✅
   - Bind Depth Stencil: ✅
2. **Mover este feature arriba del PSX existente** (drag&drop) para que el orden sea: SSAO → Vision Fog → PSX.

---

## Paso 5 — Configurar VisionRangeController en escena

1. En `LevelUI` (o escena persistente), crear GameObject `VisionFogRoot`.
2. Agregar componente `VisionRangeController`.
3. Asignar el `Transform` del player.
4. Ajustar `visionEndDark` (default 6m, en oscuridad total) y `visionEndLit` (default 25m, en zona iluminada) según el level design.

---

## Verificación visual

- **En Main Menu o escena sin player**: el shader del fog igual aplica pero el rango es de boot (`visionEndLit`), por lo que casi no se ve fog → ✓.
- **En gameplay, ambient bajo**: el fog cubre todo más allá de ~6m. Acercarse a una lámpara → la luz brilla a través del fog (gracias a `lightPreservation`).
- **Cambio de zona oscura → iluminada**: la transición no es brusca (lerp suavizado por `lerpSpeed`).
- **Items con `ItemProximityHighlight`**: al acercarse al radio, el item "respira" (tinte + emisión sube en 0.3s).
- **Items lejos**: tinte casi imperceptible. Sin emisión.

---

## Wiring pendiente (hace otro dev)

`ItemProximityHighlight` se entregó **standalone**. El acople al sistema de interactuables (BaseRangeInteractable, IInteractable, o el que aplique) lo hace quien conozca mejor el sistema. Patrón:

```csharp
// En BaseRangeInteractable o similar:

[SerializeField] private ItemProximityHighlight _highlight;

private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("Player"))
    {
        _highlight?.OnPlayerEnteredRange();
        // ... resto de la lógica de interacción ...
    }
}

private void OnTriggerExit(Collider other)
{
    if (other.CompareTag("Player"))
    {
        _highlight?.OnPlayerExitedRange();
    }
}
```

---

## Para puzzles e interactuables sin tinte de categoría

El spec (sección 6) dice que palancas, paneles, válvulas usan el mismo shader pero **sin tinte de categoría** — solo emisión neutra al acercarse.

Setup:
- Asignar el mismo shader `ItemPSX`.
- `_TintColor` = `#FFFFFF` (no se nota porque...).
- En el `ItemProximityHighlight` del Inspector: `farTint = 0`, `nearTint = 0`.
- `nearEmission = 0.2` (solo brilla al acercarse).

El prompt `[E]` diferencia un puzzle de un decorativo, no el color.
