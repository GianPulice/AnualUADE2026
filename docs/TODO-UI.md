# TO DO — Trabajo pendiente de UI

Tareas que quedaron diferidas según las decisiones tomadas durante la migración a la
arquitectura modal (`BaseScreenController` + `IModalUI` + `UIStateManager`) y el cruce con
los specs de Inventario, Interacción y Puzzles.

> ⚠️ **Nota de idioma**: todo el código de `Assets/_Project/Scripts/` está en inglés (comentarios,
> strings, logs y textos de UI). Este documento sigue en español. Ver `docs/CLAUDE.md` §
> Language rule.

---

## 🔴 Bloqueantes del loop principal

Esto no es "pendiente de UI" sino de cableado, pero condicionaba todo lo de abajo. Los tres
están resueltos: el loop de juego (timers, victoria, reset de run) ya corre.

- [x] **Arrancar los timers de módulos.** Resuelto: los módulos viven en `ModuleManager` (escena
  Data), los arranca `ZoneTrigger` y los resuelve `PuzzleStateManager.OnPuzzleCompleted` vía
  `ModuleData.associatedPuzzleId`. Lo que sigue abajo sobre `InventoryManagerUI.StartModuleTimer` /
  `TickModuleTimers` es historia: ese código ya no existe.
- [x] **Condición de victoria.** Resuelto: `WinTrigger` (`Scripts/Puzzles/WinTrigger.cs`, colocado en
  `WIRED_Zona1_Blockout`) llama `GameResultManager.ReportWin()`; con el escape registrado como
  `GameResultManager.WinPresenter`, el plano del portón corre antes de la pantalla de victoria.
  `WinLoseTest.cs` ya no existe.
- [x] **Resetear estado de run en Retry / New Game.** Resuelto con `GameSession.BeginNewSession()`:
  los managers persistentes (`InventoryManager`, `PuzzleStateManager`, `ModuleManager`, …)
  implementan `ISessionResettable`. Ver `docs/CLAUDE.md` § Capture, checkpoints and session reset.

---

## 📦 Inventario

### Detalles diferidos (mejoras visuales, baja prioridad)

- [ ] **Topbar del inventario** — fila ~28px arriba con `// inventario` a la izquierda y `[TAB] cerrar` a la derecha. Spec inventory §2.
      Hoy hay un equivalente parcial sobre la lista, no una tira arriba: `ItemsText`
      (`// INVENTORY .........00 OBJ`) y `CloseInventoryButton` (`[X] CLOSE [TAB]`).
- [ ] **Bottom hint** — fila ~22px abajo con `[E] usar / insertar` y `[ESC] cerrar inventario`. Spec inventory §2.
- [x] **Estado vacío del panel de detalle** — `Empty State Panel/EmptyStateText`, texto centrado
      "SELECT AN ITEM / TO SEE THE DETAIL" en `#8A8A8A`. El panel de fondo quedó con alpha 0.
- [ ] **Borde izquierdo rojo** (`#cc1a1a`, 2px) en item seleccionado de la lista + fondo `#110808`. Spec inventory §4.3.
      Se probó y se volvió atrás: hoy la selección es, a propósito, el barrido animado (Filled
      Horizontal) de `ItemSlotView`. La versión con barra fija pintaba `SelectionBar` / `RowBackground`,
      que el prefab `InventoryItem` nunca tuvo, y la lista quedó sin highlight. Si se retoma, primero
      agregar y cablear esos dos nodos en el prefab (ver el summary de `ItemSlotView`).

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

- [x] **Bordes de 1px** en chips y botones. Obsoleto: el look pasó a Win95 y el borde lo pone un
      `UIBevelFrame` (nodo `BevelFrame`) que aplica `UIStyle_InventoryCanvas`: lo tienen
      `OpenDocButton`, `CloseInventoryButton`, `CloseDocButton` y el chip de categoría
      (`Item Type Color Box`). Ya no se resuelve solo con fondo.
- [ ] **Sweep del `CloseDocButton`** — ya tiene `ButtonHoverColorSwap` (la cruz se aclara a blanco)
      y `UIBevelPressFeedback`, pero no `ButtonHoverSweepEffect`, porque le faltan el `SweepBar` y el
      `RectMask2D`. En el inventario el sweep sólo lo tiene `OpenDocButton` (`CloseInventoryButton`
      tampoco; el botón de descarte ya no existe).
- [ ] **Calibrar el gris del cuerpo de la nota.** La referencia usa `#3a3a3a` sobre `#070707`, que
      a 10px en un browser lee bien pero a pantalla completa queda casi ilegible. Hoy `DocText` va en
      blanco `#FFFFFF`: el rol `TextPrimary` del tema, aplicado por `UIStyle_InventoryCanvas`. Bajarlo
      solo si en algún momento se prioriza fidelidad al mockup sobre legibilidad.
- [x] **Override huérfano en `LevelUI.unity`** — la instancia del prefab forzaba `Doc Box` a
      `m_IsActive: 1`. Ya no está: el override se fue en `e21b6de2` y hoy `LevelUI.unity` no tiene
      ningún override sobre `Doc Box`.
- [x] **El círculo del timer no se movía.** `ActiveModuleDisplay.UpdateDisplay` no lo llamaba nadie;
      ahora se maneja solo con `ModuleEvents` y drena `RadialFill` con `TimerProgress`.

### Timer del módulo fuera del inventario (HUD)

Decidido en "UI y timer": el timer se ve fuera del inventario, con el módulo activo; el bip arranca
a los 30 s de la explosión; el agarre no cuesta tiempo. Ver `UI-System.md` § Timer del módulo en el HUD.

- [x] **Ventana del timer en `HUDCanvas`** (`ModuleTimerHUDView`): MM:SS dentro de un anillo de
      bloques que se vacía, `M2 // CHEST`, un pip por módulo, popup "-5s"/"+3s". Visible sobre el
      skill check (`ModalVisibilityGate.ignoredModalIds`).
- [x] **Bip de cuenta regresiva** (`ModuleTimerBeeper`): 1/s desde 30 s, 2/s con el clip urgente
      desde 10 s. Se calla solo en pausa y durante el agarre.
- [x] **El agarre no cuesta tiempo**: `SO_PlayerMovement.captureModuleTimePenalty = 0`; el timer
      sigue frenado desde el agarre hasta que el player se levanta.
- [x] **Borrar el builder de un solo uso** `Editor/UIStyle/ModuleTimerHUDBuilder.cs`: la ventana ya
      se retocó a mano (380×210, sin barra de título) y correrlo de nuevo pisaría esos cambios.
      Borrado el 2026-09-22 junto con `UIBuildKit.cs` y `HidingHUDBuilder.cs`: el prefab es la fuente
      de verdad.
- [ ] **`M1_Legs.timerDuration` está en 900 s** (`ScriptableObjects/Modules/M1_Legs.asset`; M2 y M3
      están en 180). Viene cambiando seguido (500, 30, 600, 15 y, desde `62774265`, 900): confirmar
      el valor final antes de una build.

> El look de la UI (tema, bordes Win95, fuentes con contorno, fondos animados, transición, tubo CRT)
> se aplica con **perfiles de estilo**: un `SO_UIStyleProfile` por prefab en
> `ScriptableObjects/UI/Style/`, aplicado con `Tools/UI/Style/Apply All Profiles`. Un perfil nuevo
> sale de `Tools/UI/Style/Draft Profile From Prefab` y se revisa antes de aplicar. Ver
> `Scripts/Editor/UIStyle/UIStyleTools.cs`.

### Reproductor de audio (mediano)

- [ ] **Reproductor de audio en panel de detalle** para items de tipo Grabación. Spec inventory §5.3.
  La lógica ya está esbozada en `ItemDetailView` (Play/Stop, `ignoreListenerPause`, barra de progreso,
  corte al cerrar) detrás de `enableAudioFeatures`, que está en `false` en el prefab y sin
  `audioPlayerBox` asignado: falta armar el nodo en `Inventory Canvas.prefab` y encenderlo. Lo que pide el spec:
  - Botones Reproducir / Detener.
  - Barra de progreso roja con tiempo actual / duración total.
  - `AudioSource.ignoreListenerPause = true` para que el audio siga sonando con `Time.timeScale = 0`.
  - La barra se actualiza con `unscaledDeltaTime`.
  - Al cerrar el inventario, detener la reproducción.

---

## 📜 Lateral Inventory (Variante B de puzzle)

El esqueleto está creado (`LateralInventoryView.cs` + `LateralInventorySlotView.cs`), sin colocar en
ninguna escena ni prefab. Ojo: el estado `Interacting` que existe hoy en el FSM del player
(`PlayerBoxInteractingState`) es el de empujar cajas, no éste.
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
- 🟡 **Skill-Check UI** (Puzzle Central 2 — Hub de Ventilación), **estilo Dead by Daylight, funcionando pero sin disparador real**. `SkillCheckModel` (estado puro: paso, zona sorteada, juicio de la aguja) / `SkillCheckView` (dial Win95 con estela de radar, zona con degradé, pips) / `SkillCheckController` (modal que no pausa; corre en tiempo escalado y sólo siendo el modal de arriba, así la pausa o la cinemática de explosión congelan la aguja). Prefab `Prefabs/UI/Canvas/SkillCheckCanvas.prefab` con perfil `UIStyle_SkillCheckCanvas` (CRT + transición), datos en `ScriptableObjects/Puzzle2/SO_SkillCheck_Ventilation.asset` con clips placeholder. Vive en `LevelUI` (objeto `SkillCheckController`). **Para probar: F6** (`SkillCheckTestKey`, sólo editor/dev) — sólo loguea el resultado, no completa nada. **Disparador del Hub hecho**: `SkillCheckPanelInteractable` + `SO_SkillCheckPuzzle_VentilationHub` (`puzzle_central_piso2`, ya puesto como `associatedPuzzleId` de `M2_Chest`); completar la secuencia completa el puzzle y M2 se resuelve. Probar en `TestIñaki` → `SkillCheck Test Area`: trigger M1 → `M1 Test Panel` (resuelve M1 con `SO_SkillCheckPuzzle_Test_M1`, sólo de prueba) → trigger M2 → `Ventilation Hub Panel`. M2 no arranca si M1 no está resuelto. **Falta colocarlo en el nivel real** (no hay Hub de Ventilación armado todavía). Falta también: la calma progresiva de shake/ambiente entre checks (spec §3), clips reales, y probarlo en Play (no se pudo con el editor sin foco).
- ❌ Hub Central: 3 ranuras de inserción. `SocketInteractable` + `HubPuzzleController.CheckHubCompletion()` existen, pero al completarse solo setean el flag y loguean — la cinemática, el acceso al Piso 3 y el ascensor son un comentario `// TO DO HERE`. Es el endgame del Piso 1.
- ❌ Cinemática post-Hub

Cuando se hagan los sub-puzzles, cada uno necesita su UI propia. Se sugiere seguir el
patrón del `SequencePanelUIController` con MVC + `IModalUI`.

También pendiente en la capa de mundo (no es UI pero bloquea el testeo de puzzles):

- [x] **Puertas sólidas.** Obsoleto: las puertas ya no se deslizan. `DoorInteractable` hace girar la
  hoja sobre la bisagra (`AnimateHinge`), el collider sólido va con la hoja, y la caja de interacción
  del root es trigger (ver `InteractionManager`), así que el vano queda libre al abrir.
  `DisableBlockingCollider()` ya no existe.
- [ ] **`PuzzleController.CompletePuzzle()` y `PuzzleReward.GiveReward()` no los llama nadie.**
  `SocketInteractable` puede arrancar un puzzle genérico (`StartPuzzle()`) pero nada lo completa.
  Además ni `PuzzleController` ni `PuzzleReward` están puestos en ninguna escena o prefab: los puzzles
  reales escriben directo en `PuzzleStateManager`. Decidir si se borran (código muerto) o se cablean.

---

## 🎯 Document Reader

Cambios aplicados:
- ✅ Se abre solo al **agarrar** una nota (`PickupInteractable` → `Open(SO_InventoryItem)`), para todo item con `ContentType = Text`. Desactivable por pickup con `openReaderOnPickup`.
- ✅ **Modo lectura congela el juego** (`PausesGame` true mientras esté abierto así) y bloquea la pausa. Ver UI-System §10.4.
- ✅ Hoja de 650×850 centrada sobre un dim: es el Doc Box del inventario clonado a tamaño página — mismo `InventorySurface`, mismo `UIBevelFrame`, misma header bar, mismo tubo CRT. El `DocPanelView` del inventario queda intacto.
- ✅ Cierra con ESC, con la X de la header o clickeando fuera de la hoja.
- ✅ Lectura in situ (`Open(SO_DocumentData)` desde `NoteInteractable`): sigue sin pausar y con auto-close al cambiar el target.

Detalles diferidos:

- [x] **Sorting order del Canvas** — se subió la pausa (1 → 70) y Settings detrás de ella (3 → 80) en los **prefabs**, en vez de bajar el del reader. El 22/09 se revirtieron los overrides de escena que lo pisaban (`LevelUI.unity`: `CanvasPause`, `HUDCanvas`, `InteractionCanvas`; `SettingsScene.unity`: `CanvasSettings`) y el HUD pasó a 1 en el prefab. La escalera completa está en UI-System §7.7. En modo lectura la pausa igual sigue bloqueada por `BlocksPause`.
- [ ] **Indicador visual de reproducción** si el documento incluye audio (futuro, cuando haya audio en documents).
- [ ] **Sonido de apertura** — `openSoundId` del `DocumentReaderController` sigue vacío en `LevelUI.unity`; hay `sfx_puzzle_document_read_01/02` (`Audio/SFX/Puzzles/`) sin usar y todavía sin SO de sonido que los registre como id.

---

## 🖱️ Interaction Prompt

- [x] **Ventana Win95 + línea de comando de fósforo + 3 tipos de mensaje** (común / ítem / global). Ver
      `UI-System.md` · Interaction Prompt. El texto de `PickupInteractable` pasó de "Press 'E' to pick up X"
      a "Pick up X": la tecla ahora se dibuja.
- [x] **Borrar `Scripts/Editor/UIStyle/InteractionPromptWindowBuilder.cs`** una vez commiteado el prefab. Es un
      builder de un solo uso; el prefab es la fuente de verdad. Borrado el 2026-09-22.
- [ ] **Renombrar `IInteractable.GetInteractText()` → `GetPromptText()`** para alinear con spec interaction §1.1. Cambio cosmético, alto número de archivos afectados.
- [x] **Priorizar por dot product de mirada** cuando hay múltiples interactables solapados. Spec interaction §10. Obsoleto: ya no hay orden de registro. `InteractionManager` elige con `InteractionProbe.Find`, que tira un SphereCast por el punto exacto de la mira y se queda con el hit más cercano al player sobre ese rayo: lo que se mira es lo que se elige.

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
- [ ] **Toggle de glitch VHS** — `GlitchController` y `UISignalStaticBurst` ya leen `Settings_VHSGlitch` de PlayerPrefs, pero ni `SettingsModel` ni Options la tienen. Hay que sumarla al model (con snapshot/revert, como las demás) y agregar el control. Mismo caso: `Settings_AudioInBackground` y `Settings_LowFreqAmbience` ya están en `SettingsModel` y tienen applier (`AudioBackgroundApplier`, `AmbienceComfortApplier`), pero ningún panel de Options los escribe.
- [ ] **Verificar en build standalone** — `Screen.SetResolution` es no-op en Play Mode del Editor. Ojo al probar: "Fullscreen" ya no es `ExclusiveFullScreen` sino `FullScreenWindow`, igual que "Borderless" (crash DX12 al perder el foco, UUM-134743; ver `ScreenSettingsApplier`).

---

## 💾 Save Slots

Stub visual implementado. La estructura del `SO_SaveSlotData` ya está preparada para
recibir datos del save real:

- `modules` ← snapshot de `ModuleManager.Instance.GetAllModules()` (con moduleId, status, timeRemaining, timerDuration). Los módulos viven en `ModuleManager` (escena Data); `InventoryManagerUI` ya no los tiene.
- `currentZoneId` ← zona/sala donde el player guardó.
- `collectedItemIds` ← `InventoryManager.GetItemIDs()`. La restauración ya existe (`InventoryManager.RestoreFromIDs`) pero **no la llama nadie**.
- `completedPuzzleIds` + `insertedSocketIds` ← **falta escribirlo** a disco. `PuzzleStateManager` ya tiene `Snapshot()` / `RestoreSnapshot(PuzzleSnapshot)` (los usan los checkpoints), pero es una copia en memoria con campos `internal`: no hay export serializable.
- `playTimeSeconds` ← `ModuleManager.SessionTime` (se trackea con `unscaledDeltaTime`).
- `lastSavedIso` ← `DateTime.UtcNow.ToString("o")` al momento del save.

Pendiente:

- [ ] **Conectar `OnSlotSelected(int)`** al sistema de save real cuando exista. Hoy lo escucha `MainMenuController.HandleSlotSelected`, que distingue vacío / con datos sólo en el log y entra igual al grupo `firstSceneLabel` por `EnterGameplay`. Cuando exista el save, ahí se decide:
  - Si `slot.IsEmpty` → carga la escena de inicio nueva.
  - Si NO `slot.IsEmpty` → carga la escena de gameplay aplicando los datos del slot.
- [ ] **Save / Load real**: serializar `SO_SaveSlotData` a JSON en `Application.persistentDataPath` y reconstruirlos al boot. Hoy los datos viven como sub-assets del `SO_SaveSlotDatabase`.
- [ ] **Diferenciar "cargar" vs "nueva"** en `HandleSlotClicked` según `slot.IsEmpty`. Hoy ambos disparan el mismo evento.
- [ ] **Confirmación "¿Sobrescribir slot?"** si el slot ya tenía datos al hacer "nueva".
- [ ] **Botón "borrar slot"** con confirmación. (El diálogo de descarte del inventario que servía de modelo ya no existe: el inventario no descarta.)
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

- ✅ **B5 — Ceguera M3**: `BlindnessOverlayView.cs` (HUD, permanente). `blindnessDuration` en `ModuleData`. Escucha `ModuleEvents.OnPenaltyApplied` (el viejo `InventoryEvents.OnBlindnessTriggered` y `InventoryManagerUI.TickModuleTimers` ya no existen). **Cableado pendiente: no está en ninguna escena ni prefab; agregar un GO con CanvasGroup negro + `BlindnessOverlayView` a `HUDCanvas.prefab`.**
- ✅ **B6 — Game Over por módulos**: `GameState.GameOver` en el enum, `GameResultManager.ReportGameOver()` con `OnSaveDeleteRequested`. `ModuleManager` lo reporta según `SO_GameOverRules`, y el resultado pasa antes por la cinemática de explosión (`GameOverPresenter`). El preset `GameOver` ya está en `_presentations` de `CanvasResult.prefab` (título `GAME OVER` en Accent, vignette `#0D0000`, `ShowRetry = false`, `ShowStats = true`) y `_mainMenuGroup` = `Menu`.
- ⚠️ **`OnSaveDeleteRequested` no tiene ningún suscriptor** — no hay save system que borre el slot.
- ✅ **Preset Lose**: título `YOU DIED`, sin stats, con Retry. Retry usa `ScreenManager.ReloadCurrentGroup()` (antes tenía hardcodeado `"Level1_Group"`, que no existe en el `SO_SceneList`). **Actualizar labels de botones en el prefab.**
- ✅ **Tiempo de sesión y fin de run**: lo que antes estaba en `InventoryManagerUI` (`_sessionTime`, `CheckGameOver()`, `GetActiveModule()`) vive ahora en `ModuleManager` (`SessionTime`, `CheckGameOver`, `GetActiveModule()`), que se resetea con `GameSession.BeginNewSession()`.

---

## 🧹 Limpieza / refactor menor

- [x] **`PauseManager.OnEnable/OnDisable` con InputAction** — resuelto con `pauseActionHandler` cached. Lambda ya no se pierde en `-=`.
- [x] **Editor setup `SequencePanelUISetup.cs`** — Obsoleto: el script se borró en `26bcc941` (2026-09-03) y `SequencePanelCanvas.prefab` es la fuente de verdad (estilado con `UIStyle_SequencePanelCanvas`).
- [x] **`PausesGame` en IModalUI** — propiedad agregada a la interfaz. `UIStateManager.ApplyModalEnvironment` solo pone `timeScale = 0` si alguna modal en el stack declara `PausesGame = true`. `DocumentReader` integrado al sistema con `PausesGame = false` (tiempo corre, input bloqueado).
  - ✅ **Caveat cerrado en modo lectura**: `BlocksPause` pasó a ser `isOpen && pausesWhileOpen`, así que la nota que se abre al agarrarla se come el ESC sin que la pausa dispare en el mismo frame. En lectura in situ (`NoteInteractable`) sigue en `false` a propósito — ahí el mundo corre y la pausa tiene que andar. Ver UI-System §10.4.
- [x] **`GameResultManager.ResetSession()` en flujo real** — corre en cada `GameSession.BeginNewSession()`, que llama `MainMenuController.EnterGameplay()` tanto para New Game como al elegir un slot, así que el Load Game futuro ya queda cubierto.
