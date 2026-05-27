# SaveSlots — Setup manual en Unity

Pasos para terminar de armar la pantalla de SaveSlots una vez que el código está implementado.
Cada sección es un checkpoint que se prueba de forma aislada antes de pasar al siguiente.

> **Estado del código**: ya está completo (Model, View, Controller, SO Data, SO Database,
> Card View, helper Tester). Lo que queda es trabajo de editor.

---

## ✅ Checkpoint 1 — Compilación (DONE)
Verificar que no hay errores rojos en consola tras los cambios de código.

## ✅ Checkpoint 2 — Generar Mock Data (DONE)
1. Crear si no existe `Assets/ScriptableObjects/SaveSlots/SaveSlotDatabase.asset`
   (Create → Scriptable Objects → SaveSlots → Save Slot Database).
2. En el Inspector del Database → ⋮ (tres puntos) → **Generate Mock Data**.
3. Debe aparecer una lista de 6 entries + 6 sub-assets `SaveSlot_01` a `SaveSlot_06`.

## ✅ Checkpoint 3 — Card individual visual (DONE)
1. Armar prefab `SaveSlotCard` con `SaveSlotCardView` + hijos según wireframe.
2. Para probar: agregar `SaveSlotCardTester` al GameObject, asignar un `SO_SaveSlotData`,
   click derecho → **Bind Now**.

---

## 🟡 Checkpoint 4 — Grid completo en la escena `UI_SaveSlots`

### 4.1 Crear la escena
1. **File → New Scene** (con la plantilla URP/HDRP del proyecto).
2. Guardar en `Assets/Scenes/UI/UI_SaveSlots.unity`.

### 4.2 Armar la jerarquía

```
UI_SaveSlots (Scene)
├── EventSystem
└── SaveSlotsCanvas              (Canvas, Screen Space - Overlay)
    └── SaveSlotsView            (GameObject vacío)
        ├── [CanvasGroup]        ← requerido por BaseScreenView
        ├── [SaveSlotsView]      ← script
        ├── Header
        │   ├── BackButton       (Button)
        │   └── TitleLabel       (TMP: "// seleccionar partida")
        └── GridContainer        (GameObject con GridLayoutGroup)
```

**`SaveSlotsView` (componente)** — asignar:
- `_gridContainer` → el `GridContainer`.
- `_cardPrefab` → el prefab `SaveSlotCard` del Checkpoint 3.
- `_backButton` → el `BackButton` del Header.
- `canvasGroup` (heredado de BaseScreenView) → el CanvasGroup del mismo GameObject.

**`GridContainer`** — GridLayoutGroup con:
- Constraint: **Fixed Column Count**, count = **3**.
- CellSize y Spacing a gusto (probá 280×180 con spacing 12 para empezar).

### 4.3 Crear el Controller

```
UI_SaveSlots (Scene)
├── EventSystem
├── SaveSlotsCanvas / SaveSlotsView ...
└── SaveSlotsController          (GameObject vacío)
```

**`SaveSlotsController` (componente)** — asignar:
- `view` (heredado) → el GameObject `SaveSlotsView`.
- `_screenChannel` → el `ScreenEventChannel` que ya tenés en el proyecto (mismo que usa el MainMenu).
- `_database` → el `SaveSlotDatabase.asset` con los 6 mocks.

### 4.4 Probar en aislamiento
1. Abrir la escena `UI_SaveSlots` directamente.
2. **Play**.
3. Esperado:
   - En el primer frame, fade in del Canvas.
   - 6 cards instanciadas en grid 3×2.
   - 3 cards con datos (01–03), 3 vacías (04–06).
   - Sin errores en consola.

### Troubleshooting
- **No se instancia nada** → revisar `_database` asignado y `_cardPrefab` apuntando al prefab.
- **NullRef en `Populate`** → falta `view` asignado en el Controller.
- **CanvasGroup not found** → debe estar en el mismo GameObject que `SaveSlotsView`.
- **Card aparece pero sin datos** → el card prefab tiene que ser el mismo que ya probaste en Checkpoint 3 (con sus fields asignados).

---

## 🟡 Checkpoint 5 — Click en slot (log + evento)

Mismo PlayMode del Checkpoint 4.

1. Click en el botón `[ cargar partida ]` o `[ nueva partida ]` de cualquier card.
2. **Esperado en consola**:
   `[SaveSlotsController] Slot X clickeado (visual stub).`
3. Internamente se dispara `OnSlotSelected(slotIndex)` — hoy no hay nadie suscripto, así que solo se ve el log.

---

## 🟡 Checkpoint 6 — Botón Back (Pop Screen)

1. Click en `BackButton`.
2. **Esperado**: `_screenChannel.RaisePopScreen()` se dispara.
3. Si abriste la escena en aislamiento (sin el MainMenu cargado), no pasa nada visible. En consola podés ver el evento si tu `ScreenManager` loguea.
4. El test real del Back se hace en el Checkpoint 7.

---

## 🟡 Checkpoint 7 — Flow completo desde MainMenu

### 7.1 Agregar `UI_SaveSlots` al `SO_SceneList`

1. Abrir el asset `SO_SceneList` (probablemente en `Assets/ScriptableObjects/Scenes/` o donde lo tengas).
2. En la lista de **Scene Groups**, agregar un nuevo grupo:
   - `label`: `UI_SaveSlots` (debe coincidir EXACTAMENTE con lo que `MainMenuController.HandleLoadGame` invoca).
   - `sceneNames`: agregar `UI_SaveSlots`.
3. **No** marcar la escena como persistente.

### 7.2 Agregar la escena al Build Settings

1. File → Build Profiles (o Build Settings) → arrastrá `Assets/Scenes/UI/UI_SaveSlots.unity` a la lista de Scenes In Build.
2. Si no la agregás, Unity no la puede cargar en runtime.

### 7.3 Probar el flow

1. Abrir la escena `Bootstrap` (o desde donde arranca el juego).
2. **Play** → llega al MainMenu.
3. Click en **"Load Game"**.
4. **Esperado**:
   - `MainMenuController.HandleLoadGame` hace `RaisePushScreen("UI_SaveSlots")`.
   - La escena `UI_SaveSlots` se carga aditivamente.
   - `SaveSlotsController.Start()` dispara `Open()` → fade in del grid con 6 cards.
5. Click en un slot → log en consola.
6. Click en **"← volver"** → `RaisePopScreen()` → la escena `UI_SaveSlots` se descarga → vuelve a verse el MainMenu.

---

## 🧹 Limpieza post-checkpoints

Cuando estés conforme con todo:

- [ ] Borrar el componente `SaveSlotCardTester` del prefab (era solo para Checkpoint 3).
- [ ] Borrar el script `SaveSlotCardTester.cs` si no lo querés para futuras pruebas.
- [ ] Si la escena de prueba del Checkpoint 3 quedó, borrarla.

---

## Estructura preparada para conexión futura

El `SO_SaveSlotData` ya tiene los campos que el save real necesita:

| Campo del SO | Origen del save real |
|---|---|
| `modules` | `ModuleManager.GetAllModules()` snapshot |
| `currentZoneId` | sala donde el player guardó |
| `collectedItemIds` | `InventoryManager.GetAllItems()` → IDs |
| `completedPuzzleIds` + `insertedSocketIds` | `PuzzleStateManager.GetState()` |
| `playTimeSeconds` | tiempo acumulado de la partida |
| `lastSavedIso` | `DateTime.UtcNow.ToString("o")` |

El evento `SaveSlotsController.OnSlotSelected(int)` está listo para que un futuro `GameLoader`
se suscriba y decida qué cargar según `slot.IsEmpty`.

Pendientes documentados en `docs/TODO-UI.md` sección "💾 Save Slots".
