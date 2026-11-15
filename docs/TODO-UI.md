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

- [x] **Topbar del inventario** — hecho el 2026-10-05: nodo `Topbar` (tira de 44px sobre el panel, con
      `UIBevelFrame`), `// INVENTORY` a la izquierda (`TopbarTitle`) y `CloseInventoryButton`
      (`[TAB] CLOSE`) encima de su extremo derecho. El encabezado de la lista pasó de
      `// INVENTORY ...00 OBJ` a `// ITEMS ...00 OBJ` (`InventoryView.RefreshList`) para no repetir el título.
- [x] **Bottom hint** — descartado (Iñaki, 2026-10-05): la E no hace nada dentro del inventario y ESC
      no lo cierra de una (primero suelta la selección, y con ella la nota), así que no hay fila de hints.
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

- **Panel de nota, no overlay pelado.** `Doc Box` (nació 820x560 centrado; hoy se reubica en la zona
  superior, ver abajo), borde `#1E1E1E`, fondo
  `#070707`, con barra de título (`DocTitle` + `CloseDocButton`, la cruz). Aparece y se pliega con
  `InventoryTabPanelAnimator` (escala + fade, y la inversa al ocultar).
- **Sin botón: la nota se muestra sola.** Playtest: el botón `[ OPEN DOC ]` sobraba. `OpenDocButton`
  se eliminó; al seleccionar un ítem `ContentType.Text`, `ItemDetailView` llama a
  `DocPanelView.Show(...)` y el `Doc Box` (zona superior del inventario, sin tapar el timer del
  módulo) aparece con el texto, sobre la descripción. Pasar a otra nota solo cambia el texto (sin
  cerrar/abrir); un ítem no-Text, deseleccionar o cerrar el inventario lo pliegan (`Hide()`).
  Seleccionar una nota ya la marca como leída (antes lo hacía abrir el doc).
- **El doc no es una capa del ESC.** Sigue a la selección: ESC suelta la selección (y con ella el doc) y
  después cierra el inventario; Tab cierra el inventario. La cruz de `CloseDocButton` hace lo mismo que
  ESC: `InventoryManagerUI.ClearSelection()`. `OpenDocument`/`CloseDocument`/`IsDocOpen` ya no existen.
- **El scroll de notas largas ahora funciona.** `Content` no tenía `ContentSizeFitter`, así que
  `sizeDelta.y` nunca crecía y `docScrollRect.vertical` jamás se habilitaba. Ahora tiene
  `VerticalLayoutGroup` + `ContentSizeFitter` (vertical = PreferredSize) y `DocText` dejó de
  tener autosizing (venía a 72pt).
- **Chips de categoría por dato.** `ItemDetailView` deriva el fondo oscuro y el color del label de
  la `MainColor`/`BackgroundColor` de `SO_ItemCategoryConfig` (×0.30 y ×1.65). No hace falta
  autorear un segundo color por categoría y el asset **no se tocó** — la lista sigue igual.
- **`Parameter Layout`** tenía `ChildForceExpandHeight` en una caja de 360px para 3 filas de 44:
  los parámetros salían desparramados. Ahora 150px con spacing 6.
- **Los botones del footer son chicos y anclados a un punto.** (`OpenDocButton` ya no existe; ver
  arriba.) `Discard Item Button` 260x44 anclado a
  `(0,0)` con pivot `(0,0)` — o sea esquina inferior izquierda del panel, offset fijo (24).
  Anclar a un punto con tamaño fijo es estable en cualquier aspect; lo que no servía era el
  `sizeDelta.x` fijo **con anclaje al centro**, que hacía que el botón ocupara una fracción
  distinta del panel según la relación de aspecto. Ver `UI-System.md` §7.5.
- **`[ DISCARD ]` dejó de interpolar el nombre del ítem.** En un botón de 260px,
  `$"[ DISCARD {item.ItemName.ToUpper()} ]"` desbordaba con cualquier nombre largo, y el nombre ya
  está en el header dos filas más arriba.
- **Botones que invierten color en hover.** Un botón claro (blanco `#E6E6E6`, label `#262626`)
  recibe en hover la `SweepBar` en rojo sólido `#CC1A1A` y el label pasa a
  blanco. `Discard Item Button` hace lo mismo desde su rojo oscuro. Lo hace
  `ButtonHoverColorSwap`, un componente nuevo aparte de `ButtonHoverSweepEffect`: uno desliza la
  barra y el otro recolorea un Graphic, y un botón puede querer cualquiera de los dos. Juntos son
  el botón que invierte.
- **`ButtonHoverColorSwap` también está en los 7 botones de Settings** (`BtnApply`, `BtnReset`,
  `BackButton` y los 4 tabs), que tenían el label en `#888888` y se leían mal cuando la barra roja
  pasaba por debajo. Ahora van a blanco en hover. **No** se puso en Pausa / Result / Win: esos
  labels ya son `#FFFFFF` y el swap sería un no-op.
- **`originRect` del animator.** `InventoryTabPanelAnimator` acepta un `originRect` opcional: si está
  asignado, el panel además *viaja* desde el centro de ese rect hasta su posición autoral mientras
  crece. Se resuelve en cada `Open()`, no se cachea, porque el widget se mueve con el aspect. Antes
  `Doc Box` salía de `OpenDocButton`; sin botón queda vacío (crece en su lugar desde el pivot,
  `collapsedScale 0.08`) o apunta a otro rect, p. ej. `Item Selection`.
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
      `UIBevelFrame` (nodo `BevelFrame`, puesto por el perfil de estilo del inventario): lo tienen
      `CloseInventoryButton`, `CloseDocButton` y el chip de categoría
      (`Item Type Color Box`). Ya no se resuelve solo con fondo.
- [x] **Sweep del `CloseDocButton`** — hecho el 2026-10-05: `SweepBar` rojo propio (stretch,
      `hiddenOffsetX -44`), `RectMask2D` y `ButtonHoverSweepEffect`, además del `ButtonHoverColorSwap`
      que ya tenía. En el inventario `CloseInventoryButton` sigue sin sweep.
- [ ] **Calibrar el gris del cuerpo de la nota.** La referencia usa `#3a3a3a` sobre `#070707`, que
      a 10px en un browser lee bien pero a pantalla completa queda casi ilegible. Hoy `DocText` va en
      blanco `#FFFFFF`: el rol `TextPrimary` del tema, vía su `UIThemeApplier`. Bajarlo
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
- [x] **Bip de cuenta regresiva** (`ModuleTimerBeeper`): cada 10 s desde que arranca el módulo (antes 30 s bajando a 10 s; 26/10), igual hasta
      el ámbar, 5 s en el rojo y de ahí exponencial a 0.5 s; clip urgente desde los últimos 10 s.
      Se calla solo en pausa y durante el agarre. Falta oírlo en Play (ver `BeepCadence`).
- [x] **El agarre no cuesta tiempo**: `SO_PlayerMovement.captureModuleTimePenalty = 0`; el timer
      sigue frenado desde el agarre hasta que el player se levanta.
- [x] **Borrar el builder de un solo uso** `Editor/UIStyle/ModuleTimerHUDBuilder.cs`: la ventana ya
      se retocó a mano (380×210, sin barra de título) y correrlo de nuevo pisaría esos cambios.
      Borrado el 2026-09-22 junto con `UIBuildKit.cs` y `HidingHUDBuilder.cs`: el prefab es la fuente
      de verdad.
- [ ] **`M1_Legs.timerDuration` está en 900 s** (`ScriptableObjects/Modules/M1_Legs.asset`; M2 también
      está en 900 y M3 en 180). Viene cambiando seguido (500, 30, 600, 15 y, desde `62774265`, 900): confirmar
      el valor final antes de una build.

> El look de la UI (tema, bordes Win95, fuentes con contorno, fondos animados, transición, tubo CRT)
> está serializado en los prefabs: `UIThemeApplier`, `UIBevelFrame`, los presets de contorno de TMP,
> `UISignalTransition` y `CanvasCRTPresenter`. Lo aplicaban los **perfiles de estilo**
> (`SO_UIStyleProfile`, menú `Tools/UI/Style`), borrados el 2026-09-25 junto con sus assets de
> `ScriptableObjects/UI/Style/`. Una pantalla nueva se estila a mano, copiando esos componentes de
> una que ya exista. Si hace falta de nuevo, están en git (`3d262aef`, `Scripts/Editor/UIStyle/`).

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

## 🧩 Puzzle UI (todo lo demás)

El sistema de puzzles está parcialmente implementado:
- ✅ Sub-Puzzle 1: panel eléctrico + caja de fusibles (`SequencePanelInteractable` + `SequencePanelUIController`). Escribe directo en `PuzzleStateManager` sin pasar por `PuzzleController`. SP2, SP3 y el Hub también se completan de punta a punta.
- ✅ Sub-Puzzle 2: cajas empujables — **unificado**. Se eligió la variante física y se borraron `ContainerInteractable`, `ContainerSlot` y el muerto `PushableBall`. Queda `BallPuzzleItem` + `BasketTrigger` + `GrabbableBall` + `PushBoxTriggerLogic`, con `ContainerPuzzleController.CheckContainers()` como verificador. Ya no hay dos semánticas de clave: `PuzzleStateManager.SetContainerSlot()` se escribe **siempre con el `BallId`**, y `SO_ContainerPuzzleData.ContainerRequirement.containerId` conserva el nombre viejo pero se autora con un id de caja (documentado en su tooltip). Pendiente: **reprobar el puzzle de punta a punta en escena**.
- ✅ Sub-Puzzle 3: 3 válvulas — `ValveInteractable` + `ValvePuzzleController`. La rueda gira un paso por interacción (`RotateRotator`). `InitializeValveState()` ya no espera 3 s fijos: publica la posición inicial apenas existe `PuzzleStateManager`, sin esperar nada si ya estaba (2026-09-25; falta verlo en Play).
- ✅ **Estabilización del Hub de Ventilación** (Puzzle Central 2). **Reemplazó al Skill-Check**: los scripts `SkillCheck*` ya no existen. Hoy es `StabilizationPanelInteractable` + `StabilizationCheckController` (`VentilationHubPanel.prefab`, colocado en `WIRED_Zona1_Blockout`); completar la secuencia completa `puzzle_central_piso2` y resuelve M2. Se interrumpe con TAB si el asset lo permite; ESC abre la pausa encima. La calma entre checks está parcial (`SetTensionScalars`). Faltan clips reales. *Lo que sigue es la historia del Skill-Check original, que ya no aplica:* `SkillCheckModel` (estado puro: paso, zona sorteada, juicio de la aguja) / `SkillCheckView` (dial Win95 con estela de radar, zona con degradé, pips) / `SkillCheckController` (modal que no pausa; corre en tiempo escalado y sólo siendo el modal de arriba, así la pausa o la cinemática de explosión congelan la aguja). Prefab `Prefabs/UI/Canvas/SkillCheckCanvas.prefab` (CRT + transición), datos en `ScriptableObjects/Puzzle2/SO_SkillCheck_Ventilation.asset` con clips placeholder. **Fallar un check corta la secuencia en el acto** (2026-09-25): el overlay muestra el MISS y se cierra, y hay que volver a usar el panel desde el primer check. El último check pasó de 24° a 28° de zona porque era demasiado difícil. Vive en `LevelUI` (objeto `SkillCheckController`). La tecla de prueba F6 (`SkillCheckTestKey` + `SO_SkillCheck_TestF6`) se quitó el 2026-10-10: F6 ahora hace tp a la Zona 2. Para probarlo, usar la `SkillCheck Test Area` de `TestIñaki`. **Disparador del Hub hecho**: `SkillCheckPanelInteractable` + `SO_SkillCheckPuzzle_VentilationHub` (`puzzle_central_piso2`, ya puesto como `associatedPuzzleId` de `M2_Chest`); completar la secuencia completa el puzzle y M2 se resuelve. Probar en `TestIñaki` → `SkillCheck Test Area`: trigger M1 → `M1 Test Panel` (resuelve M1 con `SO_SkillCheckPuzzle_Test_M1`, sólo de prueba) → trigger M2 → `Ventilation Hub Panel`. M2 no arranca si M1 no está resuelto. **Falta colocarlo en el nivel real** (no hay Hub de Ventilación armado todavía). Falta también: la calma progresiva de shake/ambiente entre checks (spec §3) y clips reales. Ya se probó en Play (Iñaki, 2026-09-25); el cierre al fallar es posterior y falta verlo.
- ✅ Hub Central (Piso 1): las 3 ranuras completan `puzzle_central_piso1`, que resuelve M1 y arma el escape (`EscapeSequenceDirector`). Siguen pendientes el Piso 3 y el ascensor (el `// TO DO HERE` de `HubPuzzleController`).
- ✅ Cinemática post-Hub: la cubre el escape (`EscapeSequenceDirector`, arranca con `puzzle_central_piso1`). No se toca sin coordinar.

Cuando se hagan los sub-puzzles, cada uno necesita su UI propia. Se sugiere seguir el
patrón del `SequencePanelUIController` con MVC + `IModalUI`.

También pendiente en la capa de mundo (no es UI pero bloquea el testeo de puzzles):

- [x] **Puertas sólidas.** Obsoleto: las puertas ya no se deslizan. `DoorInteractable` hace girar la
  hoja sobre la bisagra (`AnimateHinge`), el collider sólido va con la hoja, y la caja de interacción
  del root es trigger (ver `InteractionManager`), así que el vano queda libre al abrir.
  `DisableBlockingCollider()` ya no existe.
- [x] **`PuzzleController.CompletePuzzle()` y `PuzzleReward.GiveReward()` no los llama nadie.**
  `SocketInteractable` puede arrancar un puzzle genérico (`StartPuzzle()`) pero nada lo completa.
  Además ni `PuzzleController` ni `PuzzleReward` están puestos en ninguna escena o prefab: los puzzles
  reales escriben directo en `PuzzleStateManager`. Decidido (Iñaki, 2026-09-25): **se dejan como
  están**, ni se borran ni se cablean.

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
- [ ] **Sonido de apertura** — `openSoundId` del `DocumentReaderController` sigue vacío en `LevelUI.unity`; hay `sfx_puzzle_document_read_02` (`Audio/SFX/Puzzles/`) sin usar y todavía sin SO de sonido que lo registre como id. (`_01` se borró el 17/10: era byte a byte `sfx_interaction_nota.mp3`, el que ya suena al leer una nota.)

---

## 🖱️ Interaction Prompt

- [x] **Ventana Win95 + línea de comando de fósforo + 3 tipos de mensaje** (común / ítem / global). Ver
      `UI-System.md` · Interaction Prompt. El texto de `PickupInteractable` pasó de "Press 'E' to pick up X"
      a "Pick up X": la tecla ahora se dibuja.
- [x] **Borrar `Scripts/Editor/UIStyle/InteractionPromptWindowBuilder.cs`** una vez commiteado el prefab. Es un
      builder de un solo uso; el prefab es la fuente de verdad. Borrado el 2026-09-22.
- [x] **Renombrar `IInteractable.GetInteractText()` → `GetPromptText()`** para alinear con spec interaction §1.1. Hecho el 2026-09-25: 19 archivos (interfaz, `BaseRangeInteractable`, las implementaciones e `InteractionPromptView`), ninguna escena ni prefab lo referenciaba.
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
- [x] **Verificar en build standalone** — verificado por Iñaki el 2026-09-25: resolución y fullscreen andan bien. `Screen.SetResolution` es no-op en Play Mode del Editor. Ojo al probar: "Fullscreen" ya no es `ExclusiveFullScreen` sino `FullScreenWindow`, igual que "Borderless" (crash DX12 al perder el foco, UUM-134743; ver `ScreenSettingsApplier`).

---

## 💾 Save Slots

**El save real ya existe** (`Scripts/SaveSystem/`: `SaveGame`, `SaveFileStore`, `SaveData`). Las
cards ya muestran datos reales leídos de disco; el mock data del `SO_SaveSlotDatabase` se ignora en Play.

Cómo funciona (reglas de diseño):

- **Se guarda por nivel, no en cualquier momento.** El slot se escribe una sola vez, cuando el
  jugador llega al inicio de un nivel por el camino real (hoy: Zona 2, al terminar la cinemática del
  portón → `Zone2EntryTeleport` → `SaveGame.ReachLevel`). En el nivel 1 nunca hay save. Nada de lo
  que se hace dentro del nivel se guarda: cargar siempre empieza ese nivel desde el principio.
- Qué guarda: nivel (`SO_SaveLevel`, en `ScriptableObjects/Levels/SO_SaveLevel_Zone2`), grupo de
  escenas, estado de cada módulo (resuelto / explotado), progreso de puzzles, tiempo jugado y fecha.
- **Al llegar a un nivel el inventario se vacía** (nada pasa de un nivel al siguiente, igual que
  `LevelTransition` entre escenas). Por eso el save no guarda ítems.
- Al cargar: no se reproduce la cinemática de despertar, el player aparece en `Zona_2_Spawnpoint`,
  el escape no se repite y el respawn por captura queda en `Checkpoint Llegada Zona 2`.
- **Perder borra el slot** (Lose o GameOver), en el momento en que se reporta la derrota (antes de
  la cinemática de explosión). Reintentar arranca el nivel 1 en el mismo slot, sin save hasta volver a
  llegar al nivel 2. La captura del Nemesis no es perder: no toca el save.
- **Ganar marca el slot RESOLVED** (`SaveData.resolved`), en el momento en que se reporta la victoria
  (`GameResultManager.OnRunWon`, antes del plano del portón). Lo mismo que perder, con otra cara: el
  slot ya no se puede cargar y cuenta como libre, pero en vez de verse vacío muestra un bloque verde
  `RESOLVED` con la fecha de la victoria y el tiempo jugado. Nunca es LAST PLAYED ni habilita Load Game.
- Un slot = una partida. New Game usa el **primer slot libre**: primero uno vacío, si no uno RESOLVED.
  Clic en una card vacía o RESOLVED arranca la partida en ese slot; clic en una card con datos la carga.
  Una partida nueva se queda con su slot entero: lo que había (RESOLVED o el save elegido para
  sobrescribir) se borra en `SaveGame.BeginNewRun`, no antes.
- El guardado existe para irse a mitad de la partida y volver: por eso perder (también una partida
  cargada) borra el slot y Reintentar vuelve al nivel 1. Es a propósito, no un bug.
- Archivos: `%USERPROFILE%/AppData/LocalLow/Deep Dive/Wired/Saves/slot_NN.json`. Escritura atómica
  (`.tmp` + reemplazo). Un archivo corrupto se ve como slot vacío. Para limpiar en el editor:
  borrar esa carpeta.
- Cantidad de slots: `slotCount` en `SO_SaveSlotDatabase` (6).

UI de la pantalla de slots (cableada el 2026-11-13):

- **Card clickeable.** El `ActionButton` de `SaveSlotCard` había quedado sin `_actionButton` asignado
  desde el rediseño del 12/09, así que las cards no respondían. Ahora `[ new game ]` / `[ load game ]`
  van a `MainMenuController.HandleSlotSelected`.
- **Borrar slot.** Cada card con datos tiene `[ delete ]` al lado del botón de acción (fila `ActionRow`;
  en una card vacía no aparece). Abre `ConfirmDialog` (`SaveSlotConfirmDialog`, hijo de
  `CanvasSaveSlots`): título `// DELETE SLOT 03?`, zona, fecha y tiempo jugado, `[ cancel ]` /
  `[ delete ]`. Sólo con `[ delete ]` se llama `SaveSlotsController.DeleteSlot` (suena `ItemDiscard`);
  las cards se redibujan y el MainMenu refresca Load Game. El dim tapa las cards mientras el diálogo
  está abierto, y cerrar o reabrir la pantalla lo descarta sin borrar nada.
- **No hay slots libres → sobrescribir.** New Game con los 6 slots con un save cargable abre la
  pantalla con `SaveSlotsController.ShowOverwrite()`: banner `NoFreeSlotNotice` ("ALL SAVE SLOTS ARE IN
  USE // CHOOSE ONE TO OVERWRITE", texto en `SaveSlotsView._overwriteNoticeText`), cada card ofrece
  `[ overwrite ]` y no muestra `[ delete ]`. Elegir una pregunta `// OVERWRITE SLOT 03?` con `[ YES ]` /
  `[ NO ]`; con YES, `OnSlotOverwrite` → `MainMenuController.HandleSlotOverwrite` arranca la partida
  nueva en ese slot. `MainMenuController.OnNoFreeSlot` se sigue disparando.
- **ESC.** La pantalla de slots es un `IModalUI` de `UIStateManager` con `CloseKey = Escape`, como la
  pausa y Settings: ESC contesta NO / cancela el diálogo si está abierto, y si no cierra la pantalla.
- **RESOLVED en la card.** Nodo `ResolvedStamp` (`_resolvedNodes`): bloque verde oscuro con marco
  biselado teñido de verde, `RESOLVED` y abajo `_resolvedDetailLabel` (fecha de la victoria // tiempo
  jugado). La card se ve "apagada" como una vacía, sin datos alrededor del bloque, con `[ new game ]`.
  Los verdes son colores fijos del prefab (el tema no tiene tokens verdes).
- **Fecha y hora.** La línea "Saved" muestra `13 NOV 2026  14:32  (12 min ago)`, en hora local del
  jugador y con el mes en letras inglesas (`SO_SaveSlotData.GetSavedDateTimeText()`). El "(… ago)" sólo
  aparece con guardados de menos de una semana: más viejos, la fecha sola (si no, no entra en la línea).
- **Slot vacío "apagado".** Una card sin save se ve gris en vez de roja: superficies con
  `_emptySurfaceRole` (SurfacePanel), marcos multiplicados por `_emptyFrameTint` (0.45), todo el texto en
  `_emptyTextRole` (TextDisabled) y `[ new game ]` en `_emptyButtonTextRole` (TextMuted), que con su
  marco claro queda como lo único para clickear. Se ajusta en el Inspector de `SaveSlotCard`
  (`SaveSlotCardView` → "Empty slot: powered off").
- **Tamaños de texto.** Las cards tenían los px del wireframe de 800 px (10–14 px) y no se leían a
  1080p. Ahora: número de slot 18, zona 28, líneas de datos 20, pips 14 px, chip 16. Para que entren,
  la grilla pasó de 475×315 a 500×330 (`m_CellSize` en `CanvasSaveSlots`) y el banner sin slots subió
  un poco. Si se agranda algo más, la línea "Saved" es la más larga: a 20 px usa ~421 de los 448 px
  útiles (por eso es "just now" y no "seconds ago").
- **Última partida jugada.** La card del slot jugado último lleva arriba a la derecha `LAST PLAYED` en
  blanco con un punto rojo que titila como el REC de la cámara del jugador (`UIRecordingDotBlink`: 1 Hz,
  mitad encendido, sin fundido; sprite `Knob` de Unity teñido con `Accent`), y una barra roja en el
  borde izquierdo (`_lastPlayedNodes` en la card). Es el slot que se
  empezó o cargó último desde el menú (`SaveGame.LastPlayedSlot`, en PlayerPrefs
  `WIRED.Save.LastPlayedSlot`); si ese slot ya no tiene save (se perdió o nunca llegó al nivel 2), se
  marca el save más nuevo. Lo calcula `SaveSlotsModel.LastPlayedSlot`.

API:

- `SaveSlotsController.DeleteSlot(int slotIndex)` → borra el archivo, redibuja las cards y dispara
  `OnSlotDeleted`. Desde la UI sólo se llega después de la confirmación.
- `SaveSlotsController.Show()` / `ShowOverwrite()` / `Hide()`; eventos `OnSlotSelected`,
  `OnSlotOverwrite`, `OnSlotDeleted`.
- `SaveSlotsController.RefreshSlots()`, `FindFirstEmptySlot()`, `SlotCount`, `HasAnySave`.
- `SaveGame.HasSave` (cargable) / `IsResolved` / `TryReadSlot` (cualquier archivo válido).
- `SaveGame.OnSlotSaved` / `OnSlotDeleted` / `OnSlotResolved` (static, índice del slot).
- **Indicador de guardado (hecho):** prefab `Prefabs/UI/Canvas/CanvasSaveIndicator` (una instancia
  en la escena LevelUI, canvas propio con sort order 1500, encima del HUD). `SaveIndicatorView`
  escucha `SaveGame.OnSlotSaved` y muestra abajo a la derecha un anillo de bloques girando +
  "SAVING..." durante ~3 s (tiempo real), y se va solo. Colores del `UITheme`, fuente Share Tech Mono.
  Textos, tiempos y velocidad del giro se ajustan en el Inspector; clic derecho en el componente →
  "Show (preview)" para verlo en Play.
- Pip de módulo explotado: nuevo `ModuleSlotStatus.Exploded` con su color (`_pipExploded` en la card).

Pendiente (UI):

- [x] Conectar `OnSlotSelected(int)` al save real: vacío → partida nueva en ese slot; con datos → carga.
- [x] Save / Load real en JSON (`Application.persistentDataPath`).
- [x] Diferenciar "cargar" vs "nueva" (lo decide `MainMenuController.HandleSlotSelected` leyendo el disco).
- [x] **Card clickeable** (`_actionButton` reasignado en `SaveSlotCard`).
- [x] **Botón "borrar slot"** con confirmación → `SaveSlotConfirmDialog` + `SaveSlotsController.DeleteSlot`.
- [x] **Sin slots libres → elegir cuál sobrescribir** con confirmación YES / NO (`ShowOverwrite`).
- [x] **Ganar → RESOLVED** (no cargable, cuenta como libre, bloque verde).
- [x] **ESC** cierra el diálogo y la pantalla de slots (`IModalUI`).
- [x] **Indicador de guardado** al llegar al nivel 2 → `CanvasSaveIndicator` (ver arriba).
- [x] **Fecha y hora del guardado** en la card.
- [x] **Destacar el último slot jugado** (chip `LAST PLAYED` + barra).
- [ ] **Probar en Play** de punta a punta: borrar, New Game con 6 llenos (sobrescribir con YES y con
  NO / ESC), ganar y ver el slot RESOLVED, New Game que toma un RESOLVED, cargar, y que LAST PLAYED siga
  al slot correcto después de cargar uno viejo.
- [ ] **Navegación con teclado / gamepad** por las cards y el diálogo (ESC ya funciona).
- [ ] **Load Game no mostraba nada (13/11, sin confirmar).** Sospecha: `CanvasSaveSlots` y
  `CanvasMainMenu` tenían los dos sortingOrder 0 y el menú queda abierto debajo: con el tubo CRT el menú
  podía taparla. Se subió `CanvasSaveSlots` a 10. Probar en Play; si sigue, mirar en la Hierarchy si
  `CanvasSaveSlots` se activa al tocar Load Game.
- [ ] **El final del escape de Zona 1 se reporta como victoria** (`ReportWin` → `Zone2EntryTeleport`
  sigue la partida) y dispara `GameResultManager.OnRunWon`. Hoy no marca nada porque a esa altura el
  slot no tiene archivo, pero `SaveGame.HandleRunWon` debería distinguir la victoria real del final del
  escape antes de que cambie el flujo.
- [ ] **Ventana de vidrio** (pedido 13/11): que el vidrio de la ventana tenga algo de desenfoque y no se
  vea tan transparente; hoy parece que no hay vidrio.
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
- [x] **Editor setup `SequencePanelUISetup.cs`** — Obsoleto: el script se borró en `26bcc941` (2026-09-03) y `SequencePanelCanvas.prefab` es la fuente de verdad (su estilo quedó serializado en el prefab).
- [x] **`PausesGame` en IModalUI** — propiedad agregada a la interfaz. `UIStateManager.ApplyModalEnvironment` solo pone `timeScale = 0` si alguna modal en el stack declara `PausesGame = true`. `DocumentReader` integrado al sistema con `PausesGame = false` (tiempo corre, input bloqueado).
  - ✅ **Caveat cerrado en modo lectura**: `BlocksPause` pasó a ser `isOpen && pausesWhileOpen`, así que la nota que se abre al agarrarla se come el ESC sin que la pausa dispare en el mismo frame. En lectura in situ (`NoteInteractable`) sigue en `false` a propósito — ahí el mundo corre y la pausa tiene que andar. Ver UI-System §10.4.
- [x] **`GameResultManager.ResetSession()` en flujo real** — corre en cada `GameSession.BeginNewSession()`, que llama `MainMenuController.EnterGameplay()` tanto para New Game como al elegir un slot, así que el Load Game futuro ya queda cubierto.
