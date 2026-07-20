# TO DO — Trabajo pendiente de UI

Tareas que quedaron diferidas según las decisiones tomadas durante la migración a la
arquitectura modal (`BaseScreenController` + `IModalUI` + `UIStateManager`) y el cruce con
los specs de Inventario, Interacción y Puzzles.

---

## 📦 Inventario

### Detalles diferidos (mejoras visuales, baja prioridad)

- [ ] **Topbar del inventario** — fila ~28px arriba con `// inventario` a la izquierda y `[TAB] cerrar` a la derecha. Spec inventory §2.
- [ ] **Bottom hint** — fila ~22px abajo con `[E] usar / insertar` y `[ESC] cerrar inventario`. Spec inventory §2.
- [ ] **Estado vacío del panel de detalle** — texto centrado "Selecciona un item / para ver el detalle" en gris `#222` cuando no hay selección. Spec inventory §5.1.
- [ ] **Borde izquierdo rojo** (`#cc1a1a`, 2px) en item seleccionado de la lista + fondo `#110808`. Hoy el highlight es genérico. Spec inventory §4.3.

### Reproductor de audio (mediano)

- [ ] **Reproductor de audio en panel de detalle** para items de tipo Grabación. Spec inventory §5.3:
  - Botones Reproducir / Detener.
  - Barra de progreso roja con tiempo actual / duración total.
  - `AudioSource.ignoreListenerPause = true` para que el audio siga sonando con `Time.timeScale = 0`.
  - La barra se actualiza con `unscaledDeltaTime`.
  - Al cerrar el inventario, detener la reproducción.

---

## 📜 Lateral Inventory (Variante B de puzzle)

El esqueleto está creado (`LateralInventoryView.cs` + `LateralInventorySlotView.cs`).
Cuando se implemente la Variante B de interacción con puzzles, completar:

- [ ] **Paneo de cámara cinematográfico** (Lerp 0.6s) hacia un `puzzleCameraPoint` que define cada puzzle. Spec interaction §6.2.
- [ ] **Player en estado Interacting** con WASD + cámara libre + Tab bloqueados. Solo el lateral inventory + ESC activo.
- [ ] **Navegación con mouse/gamepad** sobre la lista de items.
- [ ] **Feedback shake/sonido** cuando el item es incorrecto. Spec interaction §9.2 ("Item incorrecto (Var B): Sonido corto de error").
- [ ] **Cancelación por ESC** — cerrar inventario lateral, lerp de cámara de vuelta, `SetState(Idle)`. Spec interaction §6.2.
- [ ] **Interrupción por Nemesis** — al disparar `OnDangerDetected`, cancelar igual que ESC y devolver control inmediatamente. Spec interaction §10.
- [ ] **Filtro por categoría opcional** — ej. para el Hub Central, solo mostrar `Component`.
- [ ] **Método `puzzle.CanAcceptItem(item)`** en los interactables que reciben items vía Variante B.

---

## 🧩 Puzzle UI (todo lo demás)

El sistema de puzzles está parcialmente implementado:
- ✅ Sub-Puzzle 1: panel eléctrico + caja de fusibles
- ❌ Sub-Puzzle 2: contenedores (no implementado)
- ❌ Sub-Puzzle 3: 3 válvulas (no implementado)
- ❌ Hub Central: 3 ranuras de inserción (no implementado, es el endgame del Piso 1)
- ❌ Cinemática post-Hub

Cuando se hagan los sub-puzzles, cada uno necesita su UI propia. Se sugiere seguir el
patrón del `SequencePanelUIController` con MVC + `IModalUI`.

---

## 🎯 Document Reader

Cambios aplicados:
- ✅ NO pausa el juego (sale del UIStateManager).
- ✅ Auto-close al cambiar el target de interacción.

Detalles diferidos:

- [ ] **Sorting order del Canvas** — verificar que la pausa quede VISIBLE encima del documento cuando se aprieta ESC durante la lectura. Spec implícito: la pausa es overlay global.
- [ ] **Indicador visual de reproducción** si el documento incluye audio (futuro, cuando haya audio en documents).

---

## 🖱️ Interaction Prompt

- [ ] **Renombrar `IInteractable.GetInteractText()` → `GetPromptText()`** para alinear con spec interaction §1.1. Cambio cosmético, alto número de archivos afectados.
- [ ] **Priorizar por dot product de mirada** cuando hay múltiples interactables solapados. Spec interaction §10. El comportamiento actual depende del orden de registro.

---

## ⚙️ Settings

El sistema está estructurado con tabs (Brightness / Controls / Screen / Volume) pero solo
Volume + Sensibilidad están conectados. El usuario indicó que el resto se completa más
adelante. Tabs y campos placeholder ya están preparados en `SettingsModel`.

Pendiente cuando se quieran conectar:

- [ ] **Brightness / Contrast / Gamma** — requiere post-process URP volume.
- [ ] **CRT scanlines / PSX dithering** — requiere shader o post-process volume.
- [ ] **Resolución / Window Mode / FPS limit / VSync** — `Screen.SetResolution`, `Application.targetFrameRate`, `QualitySettings.vSyncCount`.
- [ ] **Keybinds rebinding** — requiere InputSystem rebinding UI.
- [ ] **Invertir eje Y** — leer desde `SettingsModel.InvertYAxis` en `CameraSensitivityApplier`.
- [ ] **Audio en segundo plano** — `Application.runInBackground` + manejo de AudioListener.

---

## 💾 Save Slots

Stub visual implementado. La estructura del `SO_SaveSlotData` ya está preparada para
recibir datos del save real:

- `modules` ← `ModuleManager.GetAllModules()` snapshot (con moduleId, status, timeRemaining, timerDuration).
- `currentZoneId` ← zona/sala donde el player guardó.
- `collectedItemIds` ← `InventoryManager.GetAllItems()` → IDs.
- `completedPuzzleIds` + `insertedSocketIds` ← `PuzzleStateManager.GetState()`.
- `playTimeSeconds` ← tiempo acumulado desde el inicio de la partida.
- `lastSavedIso` ← `DateTime.UtcNow.ToString("o")` al momento del save.

Pendiente:

- [ ] **Conectar `OnSlotSelected(int)`** al sistema de save real cuando exista. Un futuro `GameLoader` (o el `MainMenuController`) se suscribe y decide:
  - Si `slot.IsEmpty` → carga la escena de inicio nueva.
  - Si NO `slot.IsEmpty` → carga la escena de gameplay aplicando los datos del slot.
- [ ] **Save / Load real**: serializar `SO_SaveSlotData` a JSON en `Application.persistentDataPath` y reconstruirlos al boot. Hoy los datos viven como sub-assets del `SO_SaveSlotDatabase`.
- [ ] **Diferenciar "cargar" vs "nueva"** en `HandleSlotClicked` según `slot.IsEmpty`. Hoy ambos disparan el mismo evento.
- [ ] **Confirmación "¿Sobrescribir slot?"** si el slot ya tenía datos al hacer "nueva".
- [ ] **Botón "borrar slot"** con confirmación, similar al discard del inventario.
- [ ] **Indicador de slot recién guardado** (animación o destacado visual).
- [ ] **Timer del módulo activo** en la card: si `modules[i].status == Active`, mostrar `timeRemaining / timerDuration` como barra debajo del pip correspondiente.
- [ ] **Tooltip al hover** sobre cada pip con el nombre del módulo (`moduleId`).

---

## 🧹 Limpieza / refactor menor

- [x] **`PauseManager.OnEnable/OnDisable` con InputAction** — resuelto con `pauseActionHandler` cached. Lambda ya no se pierde en `-=`.
- [ ] **Editor setup `SequencePanelUISetup.cs`** — depende del refactor reciente del View (BaseScreenView). El `SetPrivateField` ya busca en jerarquía de bases. Si se vuelve a romper, considerar dropear el editor setup y construir el prefab manualmente.
- [x] **`PausesGame` en IModalUI** — propiedad agregada a la interfaz. `UIStateManager.ApplyModalEnvironment` solo pone `timeScale = 0` si alguna modal en el stack declara `PausesGame = true`. `DocumentReader` integrado al sistema con `PausesGame = false` (tiempo corre, input bloqueado).
  - ⚠️ **Caveat abierto**: `DocumentReaderController` tiene `ConsumesEscape = true` + `BlocksPause = false`. ESC cierra el documento, pero el `PauseManager` puede disparar en el mismo frame (race condition). Si aparece en testing, cambiar a `BlocksPause = true`.
- [x] **`GameResultManager.ResetSession()` en flujo real** — se llama ahora en `MainMenuController.HandleNewGame()`. Pendiente: agregar el mismo llamado en `SaveSlotsController` cuando se implemente Load Game.
