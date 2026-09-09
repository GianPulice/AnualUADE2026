# TO DO — Trabajo pendiente de UI

Tareas que quedaron diferidas según las decisiones tomadas durante la migración a la
arquitectura modal (`BaseScreenController` + `IModalUI` + `UIStateManager`) y el cruce con
los specs de Inventario, Interacción y Puzzles.

> ⚠️ **Nota de idioma**: todo el código de `Assets/_Project/Scripts/` está en inglés (comentarios,
> strings, logs y textos de UI). Este documento sigue en español. Ver `docs/CLAUDE.md` §
> Language rule.

---

## 🔴 Bloqueantes del loop principal

Esto no es "pendiente de UI" sino de cableado, pero condiciona todo lo de abajo: **hoy el
loop de juego no corre**.

- [ ] **Arrancar los timers de módulos.** `InventoryManagerUI.StartModuleTimer(moduleId)` y
  `ResolveModule(moduleId)` no los llama nadie. Sin eso `ModuleData.Status` queda `Inactive`,
  `TickModuleTimers()` no hace nada, y en cascada no disparan: `OnModuleExploded`,
  `BlindnessOverlayView` (B5), ni `CheckGameOver()` → `ReportGameOver()` (B6). Todo lo marcado
  como ✅ en "Screens de resultado" está implementado pero es **inalcanzable**.
- [ ] **Condición de victoria.** `GameResultManager.ReportWin()` solo se llama desde
  `WinLoseTest.cs` (tecla `I` de debug). No hay camino de gameplay que gane la partida.
- [ ] **Resetear estado de run en Retry / New Game.** `InventoryManager` y `PuzzleStateManager`
  son `DontDestroyOnLoad` sin `Clear()`. `GameResultManager.ResetSession()` solo resetea el flag
  de resultado, así que al reintentar se conservan todos los ítems y puzzles ya completados.

---

## 📦 Inventario

### Detalles diferidos (mejoras visuales, baja prioridad)

- [ ] **Topbar del inventario** — fila ~28px arriba con `// inventario` a la izquierda y `[TAB] cerrar` a la derecha. Spec inventory §2.
- [ ] **Bottom hint** — fila ~22px abajo con `[E] usar / insertar` y `[ESC] cerrar inventario`. Spec inventory §2.
- [x] **Estado vacío del panel de detalle** — `Empty State Panel/EmptyStateText`, texto centrado
      "SELECT AN ITEM / TO SEE THE DETAIL" en `#8A8A8A`. El panel de fondo quedó con alpha 0.
- [ ] **Borde izquierdo rojo** (`#cc1a1a`, 2px) en item seleccionado de la lista + fondo `#110808`. Hoy el highlight es genérico. Spec inventory §4.3.

### Panel de detalle — pasada visual (hecha)

Realineado contra la referencia visual `inventory_list_hud_wired.html` (misma paleta que el spec
de inventario, que vive en Drive y no en este repo):

- **Pop-up de nota, no overlay pelado.** `Doc Box` mide 820x560 centrado, borde `#1E1E1E`, fondo
  `#070707`, con barra de título (`DocTitle` + `CloseDocButton`, la cruz). Abre y cierra con
  `InventoryTabPanelAnimator` (escala + fade, y la inversa al cerrar).
- **Toggle en un solo botón.** `OpenDocButton` alterna su label entre `[ OPEN DOC ]` y
  `[ CLOSE DOC ]` según el estado del panel. Solo aparece con ítems `ContentType.Text`.
- **Tres formas de cerrar, una sola ruta.** La cruz, el botón y ESC pasan todos por
  `InventoryManagerUI.CloseDocument()`, así el stack de capas y el label no se desincronizan.
- **El scroll de notas largas ahora funciona.** `Content` no tenía `ContentSizeFitter`, así que
  `sizeDelta.y` nunca crecía y `docScrollRect.vertical` jamás se habilitaba. Ahora tiene
  `VerticalLayoutGroup` + `ContentSizeFitter` (vertical = PreferredSize) y `DocText` dejó de
  tener autosizing (venía a 72pt).
- **Chips de categoría por dato.** `ItemDetailView` deriva el fondo oscuro y el color del label de
  la `MainColor`/`BackgroundColor` de `SO_ItemCategoryConfig` (×0.30 y ×1.65). No hace falta
  autorear un segundo color por categoría y el asset **no se tocó** — la lista sigue igual.
- **`Parameter Layout`** tenía `ChildForceExpandHeight` en una caja de 360px para 3 filas de 44:
  los parámetros salían desparramados. Ahora 150px con spacing 6.
- **Los dos botones del footer son chicos, del mismo tamaño y anclados a un punto.**
  `Discard Item Button` a la izquierda y `OpenDocButton` a su derecha, ambos 260x44 anclados a
  `(0,0)` con pivot `(0,0)` — o sea esquina inferior izquierda del panel, offsets fijos (24 y 300).
  Anclar a un punto con tamaño fijo es estable en cualquier aspect; lo que no servía era el
  `sizeDelta.x` fijo **con anclaje al centro**, que hacía que el botón ocupara una fracción
  distinta del panel según la relación de aspecto. Ver `UI-System.md` §7.5.
- **`[ DISCARD ]` dejó de interpolar el nombre del ítem.** En un botón de 260px,
  `$"[ DISCARD {item.ItemName.ToUpper()} ]"` desbordaba con cualquier nombre largo, y el nombre ya
  está en el header dos filas más arriba.
- **Botones que invierten color en hover.** `OpenDocButton` va blanco `#E6E6E6` con label
  `#262626`; al pasar el mouse la `SweepBar` entra en rojo sólido `#CC1A1A` y el label pasa a
  blanco. `Discard Item Button` hace lo mismo desde su rojo oscuro. Lo hace
  `ButtonHoverColorSwap`, un componente nuevo aparte de `ButtonHoverSweepEffect`: uno desliza la
  barra y el otro recolorea un Graphic, y un botón puede querer cualquiera de los dos. Juntos son
  el botón que invierte.
- **`ButtonHoverColorSwap` también está en los 7 botones de Settings** (`BtnApply`, `BtnReset`,
  `BackButton` y los 4 tabs), que tenían el label en `#888888` y se leían mal cuando la barra roja
  pasaba por debajo. Ahora van a blanco en hover. **No** se puso en Pausa / Result / Win: esos
  labels ya son `#FFFFFF` y el swap sería un no-op.
- **El pop-up sale del botón que lo abre.** `InventoryTabPanelAnimator` acepta ahora un
  `originRect` opcional: si está asignado, el panel además *viaja* desde el centro de ese rect
  hasta su posición autoral mientras crece, como una ventana que se restaura desde la barra de
  tareas. En `Doc Box` apunta a `OpenDocButton`, con `collapsedScale 0.08`. El origen se resuelve
  en cada `Open()`, no se cachea, porque el botón se mueve con el aspect.
- **El inventario entero abre igual.** `LAYOUT` pasó a `growAxis Both` con `collapsedScale 0.05` y
  pivot `(0.5, 0)`, así que crece desde el borde inferior. Cambiar el pivot de un rect full-stretch
  con `sizeDelta (0,0)` no mueve el layout, solo el origen del escalado.
- **El verde del scroll de la lista** (`#00674A`) pasó a rojo bordo `#5C1622`.
- **Textos a blanco**: nombre del ítem `#FFFFFF`, descripción `#E0E0E0`, cuerpo de la nota
  `#E8E8E8`, título del pop-up `#FFFFFF`. Los parámetros dejaron de estar en gris `#666`
  (`MetallicNoColor` ahora es `#E0E0E0` y `MetallicYesColor` `#CC3333`).
- **`Doc Box` colgaba de `InventoryObjects` y lo tapaban `ItemsText` y `ModuleList`.** uGUI dibuja
  por orden de jerarquía; ahora es hijo de `LAYOUT` entre `ModuleList` y `DiscardDialogView`, así
  que el pop-up cubre todo el inventario y el diálogo de descarte le sigue ganando a él. Los dos
  padres son full-stretch con `sizeDelta (0,0)`, así que el rect en pantalla no se movió.

### HUD de módulos — "INACTIVE" cortado

`Module Group.prefab` tenía `Module Status` y `Active Module Name (1)` en cajas de **79.17px** a
20pt con wrapping activo. Share Tech Mono avanza ~10.9px por carácter a ese tamaño, así que entran
7 y se partían las de 8: `INACTIVE`, `RESOLVED`, `EXPLODED` y también `M2_Chest`. Las dos pasaron a
**120px de ancho** con `m_TextWrappingMode: 0`, y se les corrió el `anchoredPosition` la mitad del
ensanche (pivot 0.5) para que el borde izquierdo del texto no se moviera ni un pixel.

Pendiente en esta zona:

- [ ] **Bordes de 1px** en chips y botones. La referencia los tiene; un `Image` de uGUI sin sprite
      no dibuja borde, así que hace falta un sprite 9-sliced con outline o un nodo extra por chip.
      Hoy se resuelve solo con fondo.
- [ ] **Sweep del `CloseDocButton`** — ya tiene `ButtonHoverColorSwap` (la cruz se aclara a blanco)
      pero no `ButtonHoverSweepEffect` como el resto, porque le faltan el `SweepBar` y el
      `RectMask2D`.
- [ ] **Calibrar el gris del cuerpo de la nota.** La referencia usa `#3a3a3a` sobre `#070707`, que
      a 10px en un browser lee bien pero a pantalla completa queda casi ilegible. Quedó en
      `#E8E8E8` (`DocText`) tras el pedido de pasar los textos a blanco. Bajarlo solo si en algún
      momento se prioriza fidelidad al mockup sobre legibilidad.
- [ ] **Override huérfano en `LevelUI.unity`** — la instancia del prefab fuerza `Doc Box` a
      `m_IsActive: 1`. Hoy es inerte (el `CanvasGroup` arranca en alpha 0 y `ItemDetailView.Awake`
      lo desactiva), pero conviene hacerle Revert al override.

> ⛔ **No correr `Tools/UI/Apply Theme To Inventory`.** El sistema de theme
> (`UIThemeApplier` + `SO_UIThemeConfig` + `UITheme.asset` + `InventoryThemeSetup.cs`) existe pero
> hoy tiene **cero instancias** de `UIThemeApplier` en todo el proyecto: ningún prefab ni escena lo
> usa. Se probó sobre el inventario y el resultado no gustó, así que el inventario queda con
> colores literales a propósito. Los archivos se conservan por si se retoma; la decisión de
> reactivarlo es de diseño, no un pendiente técnico.

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
- ✅ Sub-Puzzle 1: panel eléctrico + caja de fusibles (`SequencePanelInteractable` + `SequencePanelUIController`). Es el único puzzle que se completa de punta a punta; escribe directo en `PuzzleStateManager` sin pasar por `PuzzleController`.
- ✅ Sub-Puzzle 2: cajas empujables — **unificado**. Se eligió la variante física y se borraron `ContainerInteractable`, `ContainerSlot` y el muerto `PushableBall`. Queda `BallPuzzleItem` + `BasketTrigger` + `GrabbableBall` + `PushBoxTriggerLogic`, con `ContainerPuzzleController.CheckContainers()` como verificador. Ya no hay dos semánticas de clave: `PuzzleStateManager.SetContainerSlot()` se escribe **siempre con el `BallId`**, y `SO_ContainerPuzzleData.ContainerRequirement.containerId` conserva el nombre viejo pero se autora con un id de caja (documentado en su tooltip). Pendiente: **reprobar el puzzle de punta a punta en escena**.
- 🟡 Sub-Puzzle 3: 3 válvulas — la lógica existe (`ValveInteractable` + `ValvePuzzleController`) pero **no hay feedback visual**: la válvula no rota ni cambia de estado al interactuar. Además `InitializeValveState()` espera con un `WaitForSeconds(3)` hardcodeado para que exista el singleton (workaround de race condition, no fix).
- 🟡 **Skill-Check UI** (Puzzle Central 2 — Hub de Ventilación): `SkillCheckController` + `SkillCheckView` + `SkillCheckModel` + `SO_SkillCheckData` están escritos, pero **`SkillCheckController.Instance.Open(data)` no lo llama nadie** y el check no tiene salida por fallo: `OnFailed` nunca se invoca, `HandleCheckSuccess`/`HandleCheckFailed` están vacíos y el modelo no cuenta fallos. Se puede errar indefinidamente, solo restando tiempo al módulo activo. **Prefab, cableado de escena y camino de fallo pendientes.**
- ❌ Hub Central: 3 ranuras de inserción. `SocketInteractable` + `HubPuzzleController.CheckHubCompletion()` existen, pero al completarse solo setean el flag y loguean — la cinemática, el acceso al Piso 3 y el ascensor son un comentario `// TO DO HERE`. Es el endgame del Piso 1.
- ❌ Cinemática post-Hub

Cuando se hagan los sub-puzzles, cada uno necesita su UI propia. Se sugiere seguir el
patrón del `SequencePanelUIController` con MVC + `IModalUI`.

También pendiente en la capa de mundo (no es UI pero bloquea el testeo de puzzles):

- [ ] **Puertas sólidas.** `DoorInteractable.DisableBlockingCollider()` está comentado en los
  dos lugares donde se llamaba (`AnimateOpen` y `ApplyOpenStateImmediate`). Los paneles se
  deslizan pero el collider sigue bloqueando: no se puede atravesar ninguna puerta.
- [ ] **`PuzzleController.CompletePuzzle()` y `PuzzleReward.GiveReward()` no los llama nadie.**
  `SocketInteractable` puede arrancar un puzzle genérico (`StartPuzzle()`) pero nada lo completa.

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

El sistema está estructurado con tabs (Brightness / Controls / Screen / Volume). **Los appliers
ya existen y están conectados** — esta sección estaba desactualizada:

- [x] **Brightness / Contrast / Gamma** — `PostProcessSettingsApplier` (en el Volume global URP).
- [x] **CRT scanlines / PSX dithering** — `PS1EffectApplier` (escribe `_EnableScanlines` / `_EnableDither` sobre `PS1Effect.mat`).
- [x] **Resolución / Window Mode / FPS limit / VSync** — `ScreenSettingsApplier`.
- [x] **Invertir eje Y** — `CameraSensitivityApplier` lo lee junto con la sensibilidad.
- [x] **Audio en segundo plano** — `AudioBackgroundApplier`.

Todos se suscriben a `SettingsModel.OnSettingsApplied` y leen las keys de PlayerPrefs.
Ver la tabla de mapeo key → applier en `docs/CLAUDE.md`.

Lo que sigue pendiente:

- [ ] **Keybinds rebinding** — requiere InputSystem rebinding UI. `SettingsPanelControlsView` muestra labels estáticos.
- [ ] **Toggle de glitch VHS** — `GlitchController` ya lee `Settings_VHSGlitch` de PlayerPrefs, pero Options no expone el toggle. Basta agregar el control y escribir la key.
- [ ] **Verificar en build standalone** — `Screen.SetResolution` es no-op en Play Mode del Editor.

---

## 💾 Save Slots

Stub visual implementado. La estructura del `SO_SaveSlotData` ya está preparada para
recibir datos del save real:

- `modules` ← snapshot de `InventoryManagerUI.GetAllModules()` (con moduleId, status, timeRemaining, timerDuration). **Ojo**: no existe ningún `ModuleManager`; los módulos viven hoy en `InventoryManagerUI`.
- `currentZoneId` ← zona/sala donde el player guardó.
- `collectedItemIds` ← `InventoryManager.GetItemIDs()`. La restauración ya existe (`InventoryManager.RestoreFromIDs`) pero **no la llama nadie**.
- `completedPuzzleIds` + `insertedSocketIds` ← **falta escribirlo**: `PuzzleStateManager` no tiene ningún método de serialización (`GetState()` no existe). Sus `HashSet`/`Dictionary` son privados y no hay export ni restore.
- `playTimeSeconds` ← `InventoryManagerUI._sessionTime` (ya se trackea con `unscaledDeltaTime`).
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

## 💀 Screens de resultado (B5 / B6)

> ⚠️ **Desactualizado**: `GameOverController`, `GameOverView` y `LoseController` **ya no
> existen**. Se unificaron en `ResultScreenController` + `ResultView` + `ResultPresentation`
> (`UI/Screen/Result/`). Lose y GameOver compartían el 90% del comportamiento; ahora la
> diferencia (título, color, qué botones se ven, si hay stats) son **datos**: un array de
> presets `ResultPresentation[]` en el Inspector, uno por `GameState`. Los estados sin preset
> los ignora, por eso `WinController` puede seguir viviendo en paralelo.

Estado real:

- ✅ **B5 — Ceguera M3**: `BlindnessOverlayView.cs` (HUD, permanente). `causesBlindness` + `blindnessDuration` en `ModuleData`. Evento `InventoryEvents.OnBlindnessTriggered`, disparado desde `InventoryManagerUI.TickModuleTimers`. **Cableado de escena pendiente: agregar GO con CanvasGroup negro + BlindnessOverlayView al Canvas HUD en Level_UI.** ⚠️ Inalcanzable hoy — ver "Bloqueantes del loop principal".
- ✅ **B6 — Game Over por módulos**: `GameState.GameOver` en el enum, `GameResultManager.ReportGameOver()` con `OnSaveDeleteRequested`. **Pendiente**: configurar el preset `GameOver` en el array `_presentations` del `ResultScreenController` (título "GAME OVER" rojo `#CC1A1A`, vignette `#0D0000`, `ShowRetry = false`, `ShowStats = true`). `_mainMenuGroup` debe coincidir con la label del `SO_SceneList`. ⚠️ Inalcanzable hoy — los timers no arrancan.
- ⚠️ **`OnSaveDeleteRequested` no tiene ningún suscriptor** — no hay save system que borre el slot.
- ✅ **Preset Lose**: sin título, sin stats, con Retry. Retry usa `ScreenManager.ReloadCurrentGroup()` (antes tenía hardcodeado `"Level1_Group"`, que no existe en el `SO_SceneList`). **Actualizar labels de botones en el prefab.**
- ✅ **InventoryManagerUI**: `_sessionTime` tracker, `CheckGameOver()` (dispara cuando todos los módulos explotan), `GetActiveModule()` (para SkillCheck), `ResetSessionTime()`. ⚠️ `ResetSessionTime()` no lo llama nadie.

---

## 🧹 Limpieza / refactor menor

- [x] **`PauseManager.OnEnable/OnDisable` con InputAction** — resuelto con `pauseActionHandler` cached. Lambda ya no se pierde en `-=`.
- [ ] **Editor setup `SequencePanelUISetup.cs`** — depende del refactor reciente del View (BaseScreenView). El `SetPrivateField` ya busca en jerarquía de bases. Si se vuelve a romper, considerar dropear el editor setup y construir el prefab manualmente.
- [x] **`PausesGame` en IModalUI** — propiedad agregada a la interfaz. `UIStateManager.ApplyModalEnvironment` solo pone `timeScale = 0` si alguna modal en el stack declara `PausesGame = true`. `DocumentReader` integrado al sistema con `PausesGame = false` (tiempo corre, input bloqueado).
  - ⚠️ **Caveat abierto**: `DocumentReaderController` tiene `ConsumesEscape = true` + `BlocksPause = false`. ESC cierra el documento, pero el `PauseManager` puede disparar en el mismo frame (race condition). Si aparece en testing, cambiar a `BlocksPause = true`.
- [x] **`GameResultManager.ResetSession()` en flujo real** — se llama ahora en `MainMenuController.HandleNewGame()`. Pendiente: agregar el mismo llamado en `SaveSlotsController` cuando se implemente Load Game.
