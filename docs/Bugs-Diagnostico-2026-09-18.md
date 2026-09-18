# WIRED — Diagnóstico de bugs y estado del checklist

**Fecha:** 2026-09-18 · **Base:** `HEAD` de `iña` (`21d49656`, incluye `Alesio` y `Dress` al día).
**Fuentes:** *WIRED — Registro de Bugs* (S. Mastroberti, 2026-09-17) y *WIRED — Checklist de producción (sábado)*.

Método: análisis estático de código, escenas y prefabs (sin abrir Unity). Cada causa lleva su nivel:

- **CONFIRMADA** — se ve directamente en el código o en los datos de la escena.
- **PROBABLE** — el código explica el síntoma, pero hay que reproducirlo en Play para cerrarlo.
- **SIN CONFIRMAR** — el código no muestra un defecto claro; quedan pasos de diagnóstico.

---

## Bloqueantes

### WIR-001 — El acceso a Zona 2 está tapiado · CONFIRMADA

**Causa.** El portón nunca se colocó. `Prefabs/PuzzleGate/PuzzleGate.prefab` existe, pero hay **0 instancias** en `WIRED_Zona1_Blockout.unity`. Además, `GameResultManager.ReportWin` (`Scripts/Managers/GameResultManager.cs:36`) no se llama desde ningún lado. No hay ningún camino de código que lleve a la pantalla de victoria.

**Fix (para el sábado — TO BE CONTINUED al cruzar el portón).**
1. Sacar el panel que tapia el vano y poner una instancia de `PuzzleGate.prefab` con el `puzzleId` del hub (`SO_HubPuzzleData.PuzzleId`, el que se completa con los 3 núcleos). El portón sube solo cuando `HubPuzzleController.CheckHubCompletion` marca el hub como completo.
2. Script nuevo `ZoneExitTrigger` (`Scripts/Puzzles/`) en un hijo del portón, con un `BoxCollider` trigger ubicado **del otro lado** del vano (así solo se dispara si el jugador cruza):
   ```csharp
   private void OnTriggerEnter(Collider other)
   {
       if (!other.CompareTag("Player")) return;
       float time = ModuleManager.Exists ? ModuleManager.Instance.SessionTime : 0f;
       int resolved = ModuleManager.Exists ? ModuleManager.Instance.GetResolvedCount() : 0;
       GameResultManager.ReportWin(time, resolved);   // idempotente: _resultReported evita dobles
   }
   ```
   Como red de seguridad, que el trigger arranque desactivado y lo prenda `PuzzleGate` al terminar de abrir. Así no se puede ganar pasando por un hueco antes de tiempo.
3. Win screen: en `CanvasWin.prefab` cambiar `WinView._title` de `You win!` a **`TO BE CONTINUED`**. Ocultar `_btnNextLevel` y dejar Main Menu / Exit.
4. Re-hornear el NavMesh después (ver WIR-007).

### WIR-002 — El montacargas queda "en uso" después de morir · PROBABLE

El prompt "Forklift in use." sale cuando `!platform.IsAvailable` (`ElevatorRideButton.cs:84`). `IsAvailable = state == Idle && claimOwner == null` (`MovingPlatform.cs:134`). Hay dos estados que pueden quedar vivos entre vidas:

1. **Reserva (claim) del Nemesis.** `NemesisElevatorUser` reserva la plataforma con `TryClaim` (`:512`) y la libera recién en el `finally` del cruce (`:661`). El único token de cancelación de ese cruce es `GetCancellationTokenOnDestroy()` (`:236`). **Una captura no cancela el cruce.** Si el Nemesis ya se había comprometido con el ascensor cuando te agarra, conserva la reserva (y a veces el registro como pasajero) mientras `RepositionAfterCapture` lo teletransporta a otro lado. Ese proceso queda esperando timeouts de 12 a 20 s o se destraba recién en el próximo uso del ascensor por parte del Nemesis. Eso coincide con "se habilita cuando el Nemesis se sube o te mata otra vez".
2. **El respawn no resetea nada del nivel.** `CheckpointManager.RespawnAtActiveCheckpoint` (`:164`) solo teletransporta al jugador y dispara `OnRespawned`. `MovingPlatform` no escucha ese evento.

**Fix.**
- En `NemesisElevatorUser`, crear un `CancellationTokenSource` por cruce, enlazado con el de destroy, y cancelarlo cuando el FSM entra en `Catch` (o en `RepositionAfterCapture`). Así el `finally` libera reserva y pasajero en ese mismo frame.
- En `MovingPlatform`, suscribirse a `CheckpointManager.OnRespawned`. Si el jugador ya no está a bordo: limpiar `passengerRb`/`passengerPlayer`, remover pasajeros destruidos o desactivados, llamar a `ReleaseAfterRide()` si está en `WaitingForExit`, y volver `Waiting` → `Idle`.
- Para reproducir: `NemesisDebugHUD` + un log de `platform.IsClaimed` / `state` en el prompt.

---

## Mayores — UI e input

### WIR-003 — Escape no cierra la nota · CONFIRMADA (causa raíz compartida con WIR-005)

**Causa.** Escape está bindeado a dos acciones: `Player/Pause` (`PauseManager`) y `UI/Exit` (`UIStateManager`). El mapa `Player` va primero en `InputSystem_Actions.inputactions`, así que **`Player/Pause` se procesa antes**. `PauseManager.TryToggleFromInput` (`PauseManager.cs:74`) solo mira `IsBlockingPause`. **Nunca consulta `UIStateManager.TopConsumesEscape`**, aunque el contrato de `IModalUI` y el header de `UIStateManager` dicen que un modal con `ConsumesEscape = true` "se come" el Escape. El resultado:

- Con el **inventario / su panel de documento** abierto (`BlocksPause = false`), Escape abre la pausa. La pausa se pushea en ese frame y `OnExitPressed` descarta el `UI/Exit` por el guard `topPushedFrame == Time.frameCount` (`UIStateManager.cs:80`). La nota (DocPanel) **nunca recibe el Escape**: cerrás la pausa y la nota sigue ahí.
- El lector de notas al levantar un ítem (`DocumentReaderController`, modo lectura) sí bloquea la pausa, pero `CloseSafe` descarta el Escape mientras dura el fade de apertura (`isTransitioning`, `DocumentReaderController.cs:169`). Un Escape en los primeros 0,3 s se pierde.

**Fix (una línea, en `PauseManager.TryToggleFromInput`):**
```csharp
if (UIStateManager.Exists && UIStateManager.Instance.TopConsumesEscape) return; // ESC closes the top layer first
```
Con esto, Escape cierra capa por capa (doc → selección → inventario, gracias a `InventoryManagerUI.HandleCancelInput`) y recién después pausa. `SequencePanel` y `SkillCheck` (`ConsumesEscape = false`) siguen pausando encima, como está diseñado. Aparte: en `DocumentReaderController`, recordar un "close pendiente" si llega Escape durante el fade, en vez de descartarlo.

### WIR-004 — La X del inventario no responde · SIN CONFIRMAR

El cableado está bien: `CloseInventoryButton` tiene `InventoryCloseButton` → `InventoryManagerUI.CloseInventory()`, más un `Button` interactable con Image `raycastTarget = 1`. Ningún canvas del HUD tiene raycast targets, y el `CanvasGroup` de LAYOUT se reactiva al terminar la animación. Candidatos, en orden:

1. **Unwarp del CRT en el borde.** La X es el elemento más pegado al borde derecho de toda la UI (ancla x ≈ 0,991, pivot (1,0)). `CRTWarpedRaycaster` → `CanvasCRTPresenter.TryUnwarp` corrige más cuanto más cerca del borde está el clic, y descarta el clic si cae "fuera del tubo". Si la curvatura real del material no coincide con la que usa `TryUnwarp`, el primer botón en fallar es este.
2. El popup *Item Selection* (hermano posterior, 1150×320, con `ContentSizeFitter`) o el *Doc Box* tapando el rect de la X cuando crecen.

**Diagnóstico (2 minutos en Play):** seleccionar el `EventSystem` en la jerarquía, pasar el mouse por la X y leer `pointerEnter` / *Current Raycast* en el preview del Inspector. Si aparece otro objeto, es superposición. Si no aparece nada, es el unwarp: probar con `_WarpStrength = 0`. **Fix de bajo riesgo en cualquier caso:** alejar la X del borde (ancla x ≈ 0,97) o agrandarle el área con `raycastPadding`.

### WIR-005 — Escape no pausa · PROBABLE / verificar en build

- Si había un modal que consume Escape (inventario, doc), hoy la pausa se abre igual. Ver WIR-003: el fix de WIR-003 cambia esto a propósito.
- En el Editor, el primer Escape libera el cursor del Game View (comportamiento de Play Mode). Eso explica "libera el cursor en pantalla". **Hay que confirmarlo en un build.**
- `IsBlockingPause` recorre **todo** el stack, no solo el tope. Un modal viejo con `BlocksPause = true` que no se haya popeado bloquea la pausa para siempre. Candidatos: `ModuleExplosionSequence`, `Result`, `Settings`, y `DocumentReaderController` si la escena se descarga con la nota abierta (su `OnDestroy` no hace `Pop`). Fix: que `UIStateManager` descarte entradas destruidas (`(m as Object) == null`) al consultarlo, y que `DocumentReaderController.OnDestroy` haga `Pop(this)`.
- Menor: `CanvasPause` quedó con sorting order 50 en `LevelUI` (override), igual que `SequencePanelCanvas` (50). Una pausa sobre el panel de secuencia puede dibujarse debajo. Subir la pausa a 70 (el `DocumentReader` está en 60).

---

## Mayores — Nemesis

### WIR-006 — VFX rojo y audio de persecución intermitentes · CONFIRMADA (hipótesis del registro corregida)

**Causa.** El VFX rojo (`VignetteChaseView`) y la música/audio de persecución (`NemesisChaseMusic`) escuchan solo `NemesisEvents.OnChaseStarted/Ended`. `NemesisTelemetry.EmitChaseTransitions` (`NemesisTelemetry.cs:168`) los emite **solo en `Chasing` o `Catch` sin resolver**. Pero hay otros dos estados que **corren** hacia el jugador (`EGait.Running`):

- **`Searching`** (`NemesisSearchingState.cs:89/153`): apunta al punto de intercepción del jugador. Se ve igual que una persecución.
- **`Traversing`** (`NemesisTraversingState.cs:49`).

En el ladder (`SO_NemesisPriorities.asset`) hay reglas que **le ganan a "lo está viendo"**:
- #1 "compromiso: la búsqueda dura al menos medio segundo" (en Searching, < 0,5 s) está *por encima* de "lo está viendo".
- #4 "ya se comprometió con el montacargas" y #5 "para llegar hay que tomar el montacargas" (`RouteToBeliefCrossesFloors`, con un veredicto cacheado) también están por encima. Con un veredicto viejo, el Nemesis puede verte en el mismo piso y seguir en Traversing, corriendo, sin feedback.

`Investigating` camina (`EGait.Walking`), así que la hipótesis del registro ("sigue en investigación") no explica el caso de "te viene corriendo encima". El caso real es Searching/Traversing. El segundo candidato del registro (entrar y salir de Chasing en frames seguidos) también pasa: Chasing → Searching al perder la línea de visión en una esquina.

**Fix.**
- Definir "te está cazando" por intención y no por un solo estado: `Chasing || (Catch && !resuelto) || (Searching && creencia fresca (BeliefAge < ~3 s) && distancia por NavMesh < radio de proximidad)`. Opcional: `Traversing` si el jugador está en el mismo piso.
- Agregar una **histéresis de salida** (~1,5–2 s antes de emitir `ChaseEnded`) para que el parpadeo Chasing↔Searching no corte VFX ni audio.
- Mover la regla #1 debajo de "lo está viendo", o marcar "lo está viendo" como interrupt real: ya lo es, pero la regla #1 se evalúa antes. Revisar lo mismo en #4 y #5.
- Para loguear: `NemesisDebugHUD` ya muestra `Decision.LastReason` (la nota de la regla ganadora). Grabar una corrida con el HUD prendido cierra la duda.

### WIR-007 — Atraviesa paredes y objetos · CONFIRMADA

**Causa 1: el NavMesh no ve las paredes de arte.** El `NavMeshSurface` de la escena hornea solo las capas **Ground (3), Wall (11) y Props (12)** (`m_LayerMask: 6152`, geometría = physics colliders). Hay unos 130 prefabs sólidos colocados en **Default (0)**, sin `NavMeshObstacle`: `Wall_corner_3` ×29, `Bridges_support_2` ×28, `Wall_column` ×22, `Pipes_out_2` ×18, `Wall` ×15, `Roadblock` ×15, `Ventilation_2` ×14, `Fence_long` ×12, `Wall_corner_1` ×8, etc. Donde una de esas piezas es la única pared (y no está arriba de un muro de blockout en la capa Wall), el NavMesh pasa de largo y el agente la atraviesa. El `NavMeshAgent` no choca con colliders de física.

**Causa 2: el horneado está viejo.** El asset `WIRED_Zona1_Blockout/NavMesh-NavMesh Surface.asset` se horneó por última vez el **2026-09-08** (`c7b08d5c`). Desde entonces hubo 14 commits a la escena: tutorial (`tutoV1`, 29 prefabs), puertas nuevas, reposicionamiento del montacargas, etc.

**Causas menores (de diseño, conocidas):** el warp de `NemesisStuckEscape` y el `MoveTransformToAsync` del cruce de ascensor mueven el transform sin colisión.

**Fix.** Pasar los prefabs de estructura (`Prefabs/Environment/Wall*`, `Roadblock`, `Fence_long`, `Bridges_support_*`, `Pipes_*`) a la capa **Wall** o **Props**, o agregar Default al layer mask del surface con una `NavMeshModifier` que excluya lo que no corresponda. Después, **re-hornear**. Poner un paso fijo de "Bake NavMesh" en el checklist de cada cambio de escena.

### WIR-008 — Se traba en la puerta del hub · PROBABLE

- El horneado es del 08/09, anterior al cambio de puertas del 17/09 (`f9460b54`: 8 `DoorWood` → 10 `DoorMetalRed`). El pasillo baked no coincide con los vanos nuevos.
- `DoorMetalRed` tiene **escala no uniforme en la raíz** (0,93 × 0,756 × 1) y la hoja rota 90° como hija. Rotar una hija bajo un padre con escala no uniforme deforma la hoja. El `NavMeshObstacle` que agrega `DoorInteractable.EnsureNavMeshObstacle` (con carve) se dimensiona desde el `BoxCollider` de esa hoja deformada, así que con la puerta abierta puede carvear parte del vano. Eso encaja con "no puede entrar y queda mirando desde el umbral".

**Fix.** Arreglar la escala de la puerta (ver WIR-014) y re-hornear. Verificar con *Navigation → Show NavMesh* y la puerta abierta que el carve no invada el vano.

---

## Mayores — Interacción

### WIR-009 — El prompt desaparece pegado al objeto · RESUELTO EN CÓDIGO, falta verificar

`456d201b` (18/09, *Sistema de interacción resuelto*) agrega a `InteractionProbe` un fallback de corto alcance (`SO_InteractionManager.CloseRangeLead`, 1 m por defecto) que busca hacia atrás desde el jugador cuando no hay nada adelante. Es justo el caso "arranca adentro del collider". Queda probarlo con la caja de la Captura 3.

### WIR-010 — Hay 3 notas iguales · CONFIRMADA

El prefab `Prefabs/NoteFather/Note.prefab` trae por defecto `itemToPick = SO_NoteValvesRight` ("Turn it 2 or 3 times…"). **Tres instancias no lo pisan** y muestran esa misma nota:

| Instancia | Posición | Grupo |
|---|---|---|
| `Note` | (-0.93, 1.10, -10.69) | `---- TUTORIAL ----` |
| `Note` | (24.5, 5.97, 25.5) | nivel |
| `Note (3)` | (30.5, 1.32, 32.9) | nivel |

Además, `SO_NoteSequence` (la nota del puzzle de secuencia) **no está en el nivel** (0 referencias), y las tres notas de válvulas se llaman igual ("Valve Note").

Esto no es solo repetición: `SO_NoteSequence` es **la única pista del panel eléctrico** ("3 workers… 1 serious incident… 4 gas tanks… 2nd floor" → secuencia **3-1-4-2**). Hoy el puzzle de secuencia no tiene pista en el nivel.

**Fix — un SO distinto por nota, ninguno repetido:**

| Instancia (posición) | SO actual | SO a asignar (`itemToPick`) | Motivo |
|---|---|---|---|
| `Note (2)` (-13.7, 5.9, 17.5) | `SO_NoteValvesLeft` | `SO_NoteValvesLeft` (sin cambio) | pista de válvula izquierda |
| `Note (1)` (-6.1, 5.9, 10.4) | `SO_NoteValvesCenter` | `SO_NoteValvesCenter` (sin cambio) | pista de válvula central |
| `Note (3)` (30.5, 1.3, 32.9) | *(default)* `SO_NoteValvesRight` | `SO_NoteValvesRight` (dejarlo explícito como override) | es la más cercana al `ValvePuzzleController` (23.1, 0, 32.2) |
| `Note` (24.5, 6.0, 25.5) | *(default)* `SO_NoteValvesRight` | **`SO_NoteSequence`** | está en el 2.º piso, que es justo el que nombra el texto. Si se prefiere cerca del `PanelElectrico` (-29.9, 1.5, 22.6), moverla ahí |
| `Note` del tutorial (-0.9, 1.1, -10.7) | *(default)* `SO_NoteValvesRight` | **`SO_NoteTutorial` (crear)** | la nota del tutorial necesita contenido propio. Duplicar `SO_NoteSequence` como base, `category: 2` (Note), `contentType: 1` (Text), y el texto que defina diseño (p. ej. el primer mensaje del Arquitecto / cómo leer notas desde el inventario) |

Además:
- Cambiar el default del prefab `Prefabs/NoteFather/Note.prefab` a `itemToPick: None`. `PickupInteractable` ya avisa cuando no tiene ítem, así una nota nueva sin asignar se detecta enseguida en vez de clonar en silencio la de la válvula derecha.
- Títulos distintos en los SO: `Valve Note — Left`, `Valve Note — Center`, `Valve Note — Right`, `Incident Report`, `Tutorial Note`. Hoy las tres de válvulas dicen "Valve Note" y en el inventario se leen como repetidas aunque el texto cambie.
- `SO_InventoryItem 2` ("Test Note", placeholder) solo está en `TestIñaki.unity`. No usarlo en el nivel.

---

## Menores (arte — un solo pase de props)

| ID | Estado | Nota |
|---|---|---|
| WIR-011 bloque gris | Pendiente | No hay cambios de escena posteriores al reporte que lo toquen. |
| WIR-012 cajas bajas | Pendiente | `Box_A/B/C.prefab` sin cambios desde el 17/09 01:05. Al escalar, revisar `SO_PushableBoxConfig` y el snap de `PlayerBoxInteractingState` (collider y empuje). |
| WIR-013b highlight de puertas perdido | **Arreglado 2026-09-18** | `DoorMetalRed` se creó como prefab suelto, no como variante de `DoorFather/Door.prefab`, así que al cambiar las puertas se perdió el `ItemProximityHighlight` que heredaban las `DoorWood`. **Rehecho como variante:** `DoorMetalRed` ahora es una variante de `DoorFather/Door.prefab` (hereda `DoorInteractable`, Rigidbody, bisagra e `ItemProximityHighlight`) y se movió a `Prefabs/Puzzle1/Doors/` con el mismo GUID. Los fileIDs de raíz, Transform y bisagra se conservan (el prefab original salió de desempaquetar una instancia con PrefabInstance `1000000000000008001`), así que las 10 puertas de la escena no cambian. La escala no uniforme de WIR-014 quedó como override de la variante (`m_LocalScale` 0.93063 × 0.7557848), y ahí se corrige. El material `metal_doors_texture` ya tenía `_EMISSION` activo desde `477d2f0d` |
| WIR-013 asset de puertas | Hecho a medias | `f9460b54` (17/09 22:09) cambió las 8 `DoorWood` por 10 `DoorMetalRed`. Falta confirmar que el modelo nuevo "pegue con el set". El prefab quedó en `ScriptableObjects/Puzzle1/Doors/` (carpeta equivocada). |
| WIR-014 puertas descentradas | Causa identificada | Raíz con escala no uniforme (0,93 × 0,756 × 1), instancias con overrides de escala X de 0,97 a 1,03, e hijos del FBX a escala 170. Fix: importar el FBX con *Scale Factor* correcto, dejar la raíz en 1/1/1 y poner el pivot de `Hinge` exactamente en el canto del marco. |

---

## Cambios de diseño

- **DIS-001 — Sacar el descarte:** **Hecho** (`379820d7` borró `DiscardDialogView`). `InventoryManager.DiscardItem` quedó como API muerta. Se puede borrar.
- **DIS-002 — Ruido < 4 m colapsa la investigación:** **Pendiente.** `FieldOfListening` no distingue distancia de la fuente. Implementación sugerida: guardar la distancia a la fuente en la detección y agregar un predicado `NoiseWithin` **al final** del enum `ENemesisPredicate` (nunca en el medio: se renumera el ladder serializado). Después, una regla `IsInState(Investigating) && NoiseWithin(4 m) → Chasing` por encima de "sigue yendo hacia el último ruido". El feedback sale solo al entrar a Chasing.

## Extra encontrado (no está en el registro)

- **Teclas de debug vivas en build — HECHO (2026-09-18):**
  - `PlayerStateManager.cs`: **R** (alterna *hidden*) e **Y** (alterna *disabled*, congelaba al jugador) quedaron bajo `#if UNITY_EDITOR`.
  - `ModuleManager.cs`: **F8** (explota el módulo activo) pasó de `#if UNITY_EDITOR || DEVELOPMENT_BUILD` a `#if UNITY_EDITOR`, así tampoco funciona en un Development Build.
  - **F9** `NemesisDebugHUD`: la tecla es solo Editor, y fuera del Editor el HUD arranca oculto (el prefab `Nemesis` trae `visible` tildado).
  - **F10** y las teclas numéricas de `NemesisTestConsole`: solo Editor (antes también andaban en Development Build).
- **`ModalVisibilityGate`** solo baja el alpha (`:44`) y deja `blocksRaycasts` como estaba. Hoy no molesta porque el HUD no tiene raycast targets, pero si alguien agrega un botón al HUD, va a comerse clics con los menús abiertos.

---

## Tareas nuevas (agregadas 2026-09-18)

### NEW-01 — Luces de los núcleos: se prenden al colocar el ítem, de blanco a verde, de abajo hacia arriba

**Estado actual.** `SocketInteractable.OnInteract` consume el ítem, marca el socket en `PuzzleStateManager`, prende `insertedVisual` (un `SetActive`, sin animación), reproduce `insertSoundId` y avisa al hub. No hay ningún evento de "socket insertado" que otro componente pueda escuchar, y `FuseIndicatorLight` (que ya maneja emisión por `MaterialPropertyBlock`) no está en la escena.

**Implementación propuesta.**
1. **Evento.** Agregar a `PuzzleStateManager` `public static event Action<string> OnSocketInserted`, disparado desde `SetSocketInserted` (y limpiado en `ResetStatics`, como `OnPuzzleCompleted`). No se dispara desde `RestoreSnapshot`: un rollback no es progreso.
2. **Componente nuevo `SocketLightSequence`** (`Scripts/Environment/`), uno por núcleo, con:
   - `[SocketId] socketId` y una lista de `Renderer` (las luces emisivas de afuera del módulo) + `materialIndex`.
   - `idleColor` blanco y `activeColor` verde (HDR), `stepDelay` ≈ 0,15 s, `fadeDuration` ≈ 0,4 s, y `intensity`.
   - Al recibir `OnSocketInserted(socketId)`: **ordenar las luces por `transform.position.y` ascendente** (abajo → arriba) y lanzar una corrutina que, para cada luz *i*, espera `i * stepDelay` y hace un lerp de `_EmissionColor` de blanco a verde durante `fadeDuration`. Escribir por `MaterialPropertyBlock`, igual que `FuseIndicatorLight`, para no instanciar materiales.
   - En `Start`, si `PuzzleStateManager.IsSocketInserted(socketId)` ya es true (carga o checkpoint), dejarlas en verde sin animar.
   - Opcional: un "flash" de la luz del núcleo (`insertedVisual`) antes de la cascada, y un SFX por paso con pitch creciente.
3. **Color.** El verde no choca con los colores reservados de `Materials-System.md` (rojo = peligro, ámbar = dispositivo del jugador, azul frío = monitores). Documentarlo ahí como "verde = módulo alimentado / núcleo colocado", para que no se use en otro lado.
4. Cubre también la tarea del checklist *Luz que se prende arriba de cada módulo al meter el núcleo* (#27): la luz superior puede ser el último escalón de la cascada.

### NEW-02 — Botón del montacargas: mensaje gris de "deshabilitado" cuando ya está en ese piso

**Estado actual.** `ElevatorCallPanel` con la cabina en el piso (`PanelState.CabinPresent`) devuelve `CanInteract = false`. En ese caso `InteractionPromptView` muestra `GetInfoText()` en gris (`infoColor` #888888), pero `GetInfoText()` devuelve `""` para todo lo que no sea `Busy`. El texto "Forklift is here" existe, pero está en `GetInteractText()`, que solo se muestra cuando se *puede* interactuar. Resultado: con la cabina abajo, el panel no dice nada.

**Fix (en `ElevatorCallPanel.cs`):**
```csharp
public override string GetInfoText()
{
    switch (State)
    {
        case PanelState.CabinPresent: return "Forklift is already here.";
        case PanelState.Busy:         return "Forklift in use — wait for it to come free.";
        default:                      return string.Empty;
    }
}
```
Así el prompt aparece en gris (el estilo "deshabilitado" que ya usa el sistema) sin tocar la vista. Verificar que `ElevatorCallPanel.LateUpdate` pida `RequestPromptRefresh` cuando cambia el estado, como hace `ElevatorRideButton`, para que el mensaje cambie mientras el jugador lo mira.

---

## Estado del checklist

Criterio: Hecho = 1 · Parcial = 0,5 · Pendiente = 0. "Hecho" incluye lo que está en el repo pero falta probar en Play.

| # | Ítem | Estado | Evidencia |
|---|---|---|---|
| 1 | WIR-001 portón | Pendiente | 0 instancias de `PuzzleGate` |
| 2 | WIR-002 montacargas | **Hecho** (verificar) | Cruce del Nemesis cancelable (captura/respawn), `MovingPlatform` se resetea en `OnRespawned`, `ElevatorLandingBarrier` solo abre con la cabina estacionada |
| 3 | WIR-003 Escape nota | Pendiente | causa confirmada, fix de 1 línea |
| 4 | WIR-004 X inventario | Pendiente | — |
| 5 | WIR-005 Escape pausa | Pendiente | — |
| 6 | WIR-006 VFX/audio | Pendiente | — |
| 7 | WIR-007 paredes | Pendiente | — |
| 8 | WIR-008 puerta hub | Pendiente | — |
| 9 | WIR-009 prompt | **Hecho** (verificar) | `456d201b` |
| 10 | WIR-010 notas | Pendiente | — |
| 11 | WIR-011 bloque gris | Pendiente | — |
| 12 | WIR-012 cajas | Pendiente | — |
| 13 | WIR-013 asset puertas | Parcial | `f9460b54` |
| 14 | WIR-014 centrado puertas | Pendiente | — |
| 15 | DIS-001 descarte | **Hecho** | `379820d7` |
| 16 | DIS-002 ruido cercano | Pendiente | — |
| 17 | VFX explosión + SFX | Parcial | `ExplosionVFX` / `ModuleExplosionSequence` existen (`190b9026`); falta la mezcla "low SFX / high VFX" |
| 18 | Sonido al abrir inventario | Pendiente | `sfx_abrir_inventario` sin cambios después del testeo |
| 19 | Voz del enemigo (texto) | Pendiente | no hay sistema. Se puede reusar `ArchitectSubtitleView` |
| 20 | Animación de agarre | Pendiente | `PlayerController.controller` sin estado de agarre |
| 21 | Items visibles de lejos | **Hecho** (verificar) | `ItemGlint` + ajustes de highlight del 17 y 18/09 |
| 22 | Caja atrás y diagonal | Parcial | `194f747d`: atrás sí, diagonal no (gana la última tecla) |
| 23 | Deslizarse con objetos | **Hecho** | `d0a0e1e3` |
| 24 | FOV al agacharse | **Hecho** | `cacbd25f` |
| 25 | Level design escalonado | Pendiente | — |
| 26 | Puertas con sus llaves | Pendiente | las 10 `DoorMetalRed` tienen `doorData = none` |
| 27 | Luz sobre cada módulo | Pendiente | `FuseIndicatorLight` existe pero no está en la escena |
| 28 | Marcas de fluido | Pendiente | nada en escena. Elegir UN solo fluido |
| 29 | Inventario sin zona derecha + notas en slide | Parcial | inventario rehecho (`379820d7`); el doc sigue siendo un popup que crece, no un slide desde la derecha |
| 30 | Portón + Win "TO BE CONTINUED" (ReportWin al cruzar) | Parcial | Código listo: `WinTrigger` (llama a `ReportWin`, opcionalmente condicionado al puzzle del hub), `WinController` ahora es modal (antes dejaba el cursor bloqueado en la pantalla de victoria), título `TO BE CONTINUED` en `CanvasWin`, `PuzzleGate` con apertura editable. Falta colocar portón + trigger en la escena |
| 31 | NEW-01 Luces de núcleos blanco → verde en cascada | Pendiente | plan arriba |
| 32 | NEW-02 Mensaje gris en el panel del montacargas | **Hecho** (verificar) | `ElevatorCallPanel.GetInfoText`: "Forklift is already down here." / "…up here." según el piso, y el de "en uso" |
| 33–42 | Secuencia de escape (post-sábado, 10 ítems) | Pendiente | no hay trigger, fog dinámico, trote ni alarma |

**Alcance del sábado (32 ítems): 7 / 32 ≈ 22 %.** Contando solo lo terminado del todo (sin parciales): 5 / 32 ≈ 16 %.
**Checklist completo con la secuencia de escape (42 ítems): 7 / 42 ≈ 17 %.**
Fuera de la cuenta (no estaba en el checklist): teclas de debug R / Y / F8 limitadas al Editor — hecho.
**Solo los bugs WIR (14):** 1,5 / 14 ≈ 11 %. Ninguno de los dos bloqueantes está resuelto.

### Orden sugerido para el sábado

1. **WIR-001 + ítem 30**: portón en el vano + `ZoneExitTrigger` que llama a `ReportWin` al cruzarlo + Win screen con el título **TO BE CONTINUED**. Sin esto no hay slice que terminar.
2. **WIR-003/005** (1 línea en `PauseManager`) y **WIR-010** (asignar los SO de la tabla, crear `SO_NoteTutorial`): ganancias rápidas. **NEW-02** (mensaje gris del montacargas) es otra de 5 minutos.
3. **WIR-007/008**: capas de los prefabs de estructura + re-horneado del NavMesh. Arregla los dos juntos.
4. **WIR-006**: histéresis y definición de "cazando" en `NemesisTelemetry`.
5. **WIR-002**: cancelar el cruce del ascensor en `Catch` y resetear la plataforma en `OnRespawned`.
6. **NEW-01**: luces de los núcleos (evento de socket + `SocketLightSequence`).
7. Pase de arte único: WIR-011 a WIR-014 y puertas con llaves.
