# Sistema de UI y Pausa — Guía para desarrolladores

Este documento explica cómo funciona la UI del juego: la arquitectura MVC, el ciclo de vida de las pantallas, la carga aditiva de escenas, el sistema de pausa, y las convenciones que hay que respetar al agregar features nuevas.

Está pensado para que alguien que se suma al proyecto pueda navegar el código y agregar pantallas nuevas sin romper lo existente.

> ⚠️ **Nota de idioma**: el código de `Assets/Scritps/` está íntegramente en inglés (comentarios,
> strings, logs y textos de UI). Este documento sigue en español. Ver `docs/CLAUDE.md` § Language rule.

---

## 1. Visión general

El proyecto usa **carga aditiva de escenas** para componer la UI. En cualquier momento del juego hay varias escenas cargadas a la vez (`Bootstrap`, `Data`, `LevelUI`, `UI_Settings`, etc.), cada una con responsabilidades distintas. La navegación entre menús no carga/descarga el juego entero — solo agrega o quita escenas específicas.

Cada pantalla sigue el patrón **MVC**:

- **Model** (`BaseScreenModel`) — estado puro (POCO, no MonoBehaviour). Persistencia, snapshots, lógica de datos.
- **View** (`BaseScreenView`) — solo presentación. Sliders, botones, textos. Expone `event Action` para que el controller se entere de los clicks.
- **Controller** (`BaseScreenController<TView, TModel>`) — orquesta. Suscribe handlers del view, llama métodos del model, decide cuándo abrir/cerrar.

La comunicación entre sistemas que viven en escenas distintas se hace por **eventos estáticos** (`GameResultManager.OnGameResult`, `NemesisEvents.OnChaseStarted`, `InventoryEvents.OnItemAdded`, etc.) o por **ScriptableObject event channels** (`ScreenEventChannel`). Nunca por referencias serializadas entre escenas (Unity las rompe).

---

## 2. La base: BaseScreenController / View / Model

### Archivos clave

| Archivo | Rol |
|---|---|
| `Assets/Scritps/UI/Screen/BaseScreenController.cs` | Clase genérica `<TView, TModel>`. Define `Open()`, `Close()`, hooks virtuales. |
| `Assets/Scritps/UI/Screen/BaseScreenView.cs` | Wrapper de `CanvasGroup` con `ShowAsync()`, `HideAsync()`, `Fade()`. |
| `Assets/Scritps/UI/Screen/BaseScreenModel.cs` | POCO con `Initialize()`, `IsInitialized`, evento `OnDataChanged`. |

### Lifecycle de una pantalla

```
Open()          →  OnBeforeOpen()  →  view.ShowAsync()  →  OnAfterOpen()
Close()         →  OnBeforeClose() →  view.HideAsync()  →  OnAfterClose()
```

Los 4 hooks (`OnBeforeOpen`, `OnAfterOpen`, `OnBeforeClose`, `OnAfterClose`) son virtuales en `BaseScreenController` y los override cada Controller concreto para hacer cosas específicas: setear `Time.timeScale`, popular el view, bloquear/desbloquear el cursor, etc.

### ShowAsync / HideAsync usan unscaledDeltaTime

**Importante**: los fades de `BaseScreenView.ShowAsync()` y `HideAsync()` usan `Time.unscaledDeltaTime`, así que **funcionan aunque `Time.timeScale = 0`**. Esto es clave porque muchas pantallas (Pausa, Settings, Inventario, SequencePanel) se abren con timeScale = 0 y deben poder animar el fade igual.

El método genérico `Fade(alpha, duration)` SÍ usa `Time.deltaTime` — está pensado para overlays que deben "congelarse" al pausar (ej: las viñetas del Nemesis).

---

## 3. Carga aditiva: ScreenManager + ScreenEventChannel + SO_SceneList

### Archivos clave

| Archivo | Rol |
|---|---|
| `Assets/Scritps/Managers/ScreenManager.cs` | Singleton que carga/descarga grupos de escenas. Escucha eventos del channel. |
| `Assets/Scritps/ScriptableScripts/Screens/SO_SceneList.cs` | Base de datos: nombre de grupo (`"Menu"`, `"Level1_Group"`) → lista de escenas, y lista de escenas **persistentes**. |
| `Assets/Scritps/ScriptableScripts/Screens/ScreenEventChannel.cs` | Event channel ScriptableObject. Expone `RaisePushScreen(label)`, `RaisePopScreen()`, `RaiseClearAll()`. |
| `Assets/Scritps/BootingScene/BootingSceneLoader.cs` | Carga las escenas iniciales al arrancar el juego. |

### Cómo funciona la navegación

1. Algún código (ej: `MainMenuController.HandleNewGame`) hace `screenChannel.RaisePushScreen("Level1_Group")`.
2. `ScreenManager.OnPushScreenRequestedWrapper(label)` recibe el evento.
3. Descarga el grupo activo anterior (si hay) y carga las escenas del nuevo grupo en paralelo (`UniTask.WhenAll`).
4. Mantiene un `Stack<string>` de pantallas activas para que `RaisePopScreen()` vuelva atrás.

**Escenas persistentes**: las que están en `SO_SceneList.persistentSceneNames` no se descargan nunca (`Data`, `LevelUI`, `UI_Settings`, etc.). Cargan al boot y viven toda la sesión.

### Por qué importa la distinción persistente vs pushable

- **Pushable** (`Menu`, `Level1_Group`, `UI_SaveSlots`): se cargan/descargan según la navegación. Los managers que vivan ahí mueren al descargar.
- **Persistente** (`Bootstrap`, `Data`, `LevelUI`, `UI_Settings`): siempre vivas. Sus singletons (`PauseManager`, `InventoryManagerUI`, `SettingsController`, etc.) se pueden invocar desde cualquier escena.

---

## 4. UI modales: el patrón de "controller persistente con static Instance"

Hay un grupo de UIs que se abren **sobre** la pantalla actual: Pausa, Settings, Inventario, SequencePanel (puzzles), DocumentReader (notas). Estas no se cargan con el flujo de `ScreenManager` — viven en escenas persistentes y se invocan directo.

### Patrón común

Cada uno de estos controllers:

1. Vive en una escena persistente (`LevelUI` o `UI_Settings`).
2. Expone `public static SettingsController Instance { get; private set; }` (o el nombre que sea) y lo asigna en `Awake`.
3. Expone `public bool IsOpen` para que otros sistemas (típicamente `PauseManager`) sepan si está activo.
4. Tiene un método público `OpenScreen()` / `Open(data)` que cualquier código puede llamar.
5. **NO maneja su propio ESC.** El `UIStateManager` escucha la action `UI/Exit` y llama
   `RequestClose()` sobre la modal del top que declare `ConsumesEscape = true`. Los controllers
   no deben tener un `Update()` con `GetKeyDown(KeyCode.Escape)`.

Ejemplos en el código:
- `DocumentReaderController.Instance.Open(documentData)` — invocado desde `NoteInteractable`.
- `SequencePanelUIController.Instance.Open(panel)` — invocado desde `SequencePanelInteractable`.
- `SettingsController.Instance.OpenScreen()` — invocado desde `PauseManagerUI.HandleSettings()` y `MainMenuController.HandleSettings()`.
- `InventoryManagerUI.Instance.OpenInventory()` — invocado desde su propio `HandleInput()` con Tab.

### Por qué `static Instance` y NO `Singleton<T>`

`Singleton<T>` (el de `Assets/Scritps/SingletonCreator/Singleton.cs`) está pensado para managers globales que pueden hacer `DontDestroyOnLoad`. Los controllers de UI persistente NO necesitan eso — la escena ya garantiza una sola instancia. Solo necesitan el accessor global. `public static T Instance { get; private set; }` + asignar en `Awake` es suficiente.

---

## 5. Sistema de Pausa

### Componentes

| Archivo | Rol |
|---|---|
| `Assets/Scritps/Managers/PauseManager.cs` | Singleton<PauseManager>. Maneja `Time.timeScale`, escucha ESC, dispara evento estático `OnPauseStateChanged`. |
| `Assets/Scritps/UI/Screen/Pause/PauseModel.cs` | Estado `PauseState { Unpaused, Paused }`. |
| `Assets/Scritps/UI/Screen/Pause/PauseView.cs` | Botones Continue/Options/Exit. |
| `Assets/Scritps/UI/Managers/PauseManagerUI.cs` | Controller. Escucha `OnPauseStateChanged` y abre/cierra el view. |

### Flow de pausa

```
Usuario aprieta ESC
      │
      ▼
InputAction Player/Pause → PauseManager.TryToggleFromInput()
   (el KeyCode.Escape de Update() es solo fallback si no hay InputActionReference asignada)
      │
      ▼
¿Ya está en pausa? → sí: return (el cierre va por UI/Exit → PauseManagerUI.RequestClose)
      │
      ▼
¿Hay una UI bloqueante abierta? (ver §5.1)
      ├─ Sí  → return (la UI bloqueante consume el ESC ella misma)
      └─ No  → model.Toggle() → state pasa a Paused → dispara OnPauseStateChanged
                    │
                    ▼
              PauseManagerUI.HandlePauseStateChanged(state)
                    │
                    ▼
              Open() → OnBeforeOpen() (Time.timeScale = 0, cursor visible)
                    │
                    ▼
              view.ShowAsync() (fade con unscaledDeltaTime)
```

Al apretar Continue (o ESC sin UI bloqueante), pasa lo inverso: `model.Unpause()` → evento → `Close()` → `OnBeforeClose()` (`Time.timeScale = 1`, cursor oculto).

### 5.1 Guard de ESC — UIStateManager.IsBlockingPause

`PauseManager.TryToggleFromInput()` **no mantiene ninguna lista manual** de controllers. Delega completamente al `UIStateManager`:

```csharp
private void TryToggleFromInput()
{
    if (IsPaused) return;
    if (UIStateManager.Exists && UIStateManager.Instance.IsBlockingPause) return;
    Pause();
}
```

`UIStateManager.IsBlockingPause` retorna `true` si alguna modal en el stack declara `BlocksPause = true`. **Cuando agregás una UI modal nueva**, solo necesitás implementar `IModalUI` correctamente y hacer Push/Pop en UIStateManager — no hay lista que mantener manualmente.

### 5.2 Bloqueo de inputs del player

`PauseManager` expone:

```csharp
public static bool IsGameplayInputBlocked
    => (Exists && Instance.IsPaused) || (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen);
```

`IsAnyModalOpen` es `true` cuando hay al menos una modal en el stack del `UIStateManager`, **sin importar si tiene `PausesGame = true` o false**. Eso significa que el player queda bloqueado aunque el tiempo no esté pausado (ej: leyendo un documento). Los scripts que lean `Input.*` directamente (movimiento, agarre de objetos, etc.) hacen early return:

```csharp
private void Update()
{
    if (PauseManager.IsGameplayInputBlocked) return;
    // ...lectura de input
}
```

`Time.timeScale = 0` ya congela el movimiento físico, pero NO previene que `Input.GetButtonDown("Crouch")` se dispare. El guard es necesario para inputs que cambian estado lógico.

---

## 6. Settings: caso de estudio

Settings es la UI más sofisticada hoy y muestra todos los patrones juntos.

### Estructura

```
UI_Settings (escena persistente)
└─ SettingsRoot (GameObject vacío con SettingsController)
    └─ Canvas (con SettingsView + CanvasGroup)
        ├─ SettingsTabSelector (4 botones: Brightness, Controls, Screen, Volume)
        ├─ Panels container
        │   ├─ BrightnessPanel  (con SettingsPanelBrightnessView — placeholder)
        │   ├─ ControlsPanel    (con SettingsPanelControlsView — sensibilidad funcional)
        │   ├─ ScreenPanel      (con SettingsPanelScreenView — placeholder)
        │   └─ VolumePanel      (con SettingsPanelVolumeView — funcional)
        └─ Footer (Apply / Reset / Back buttons)
```

### Modelo con snapshot/revert

`SettingsModel` tiene los valores actuales (`MasterVolume`, `Sensitivity`, etc.) y un snapshot interno (`_snapMaster`, `_snapSensitivity`, etc.). Al abrir, `TakeSnapshot()` captura el estado. Si el usuario cambia sliders y aprieta **Back**, `Revert()` restaura el snapshot. Si aprieta **Apply**, persiste en PlayerPrefs, llama `AudioManager.SetMasterVolume(...)`, dispara `OnSettingsApplied` (evento estático) y vuelve a tomar snapshot.

### Por qué un evento estático

`CameraSensitivityApplier` vive en el rig de cámara del player (escena `LevelUI`). `SettingsModel` vive en `UI_Settings`. **Son escenas distintas — no hay forma de pasarle referencia directa**. El evento estático `SettingsModel.OnSettingsApplied` permite que `CameraSensitivityApplier.HandleSettingsApplied()` se entere sin coupling.

Este patrón se repite en todo el proyecto:
- `NemesisEvents.OnChaseStarted` → escuchado por `VignetteChaseView`.
- `InventoryEvents.OnItemAdded` → escuchado por `InteractionPromptView`, `ModuleHUDView`.
- `GameResultManager.OnGameResult` → escuchado por `WinController`, `ResultScreenController`.

### Sub-views por tab

`SettingsView` no implementa los sliders directamente — delega en sub-views (`SettingsPanelVolumeView`, etc.) que viven en GameObjects hijos. Cada sub-view:

1. Tiene sus `[SerializeField] Slider` / `Toggle`.
2. Suscribe sus listeners en `Awake`, los limpia en `OnDestroy`.
3. Re-emite los cambios con su propio `event Action<float>` (ej: `OnMasterChanged`).
4. Expone `Populate(model)` para refrescar valores cuando Settings se abre.

`SettingsView` agrega esos eventos en `WireXxxPanel()` y los re-emite en sus propios eventos públicos para que `SettingsController` solo conozca a `SettingsView`.

### Estado "placeholder"

> ⚠️ **Desactualizado**: esta sección decía que ningún sistema leía los 12 campos extra
> (Brightness, Contrast, Gamma, CRTScanlines, ResolutionIndex, etc.). **Ya no es cierto** —
> existen `PostProcessSettingsApplier`, `PS1EffectApplier`, `ScreenSettingsApplier`,
> `AudioBackgroundApplier` y `CameraSensitivityApplier`, todos suscritos a
> `SettingsModel.OnSettingsApplied` y leyendo las keys de PlayerPrefs. Ver la tabla
> key → applier en `docs/CLAUDE.md`.
>
> Lo único que sigue sin conectar es el rebinding de teclas (`SettingsPanelControlsView`
> muestra labels estáticos) y el toggle de glitch VHS (`Settings_VHSGlitch` ya lo lee el
> `GlitchController`, pero Options no lo expone).

---

## 7. Convenciones que hay que respetar

### 7.1 Suscripción a eventos estáticos: Awake / OnDestroy

```csharp
private void Awake()
{
    GameResultManager.OnGameResult += HandleGameResult;
}

private void OnDestroy()
{
    GameResultManager.OnGameResult -= HandleGameResult;
}
```

**NO usar OnEnable/OnDisable para eventos estáticos**. El delegado vive más allá del lifecycle del GameObject. Si te suscribís en OnEnable y se desactiva temporalmente el objeto, perdés los disparos en ese intervalo — casi siempre eso es bug, no feature.

**OnEnable/OnDisable es para**:
- `InputAction.Enable()` (patrón estándar de Unity InputSystem).
- ScriptableObject event channels en managers que se activan/desactivan a propósito.
- Suscripciones a componentes hijos que comparten lifecycle con el padre y que querés bloquear cuando el padre está disabled.

### 7.2 Show/Hide de pantallas: nunca SetActive directo en código de UI

Usar siempre `view.ShowAsync()` / `view.HideAsync()` (o `BaseScreenController.Open()` / `Close()`). Eso garantiza:
- Fade visual consistente.
- `interactable`/`blocksRaycasts` se setean correctamente (bloqueo de clicks durante fade).
- `gameObject.SetActive(false)` al final del HideAsync libera el objeto del frame loop.

**Excepción**: vistas que necesitan estar permanentemente activas y suscriptas (ej: `InteractionPromptView`). Esas usan solo `CanvasGroup.alpha` para mostrar/ocultar, **nunca SetActive**, porque desactivar el GameObject dispararía OnDisable y desuscribiría eventos.

### 7.3 Cuando agregar una UI modal nueva

Checklist:

1. ¿Vive en escena persistente o pushable? Si necesita ser invocada desde varios contextos, persistente.
2. Si es persistente: `public static T Instance` + asignar en `Awake`.
3. `public bool IsOpen` para consultas externas (guards en otros sistemas).
4. **Implementar `IModalUI`** con las cuatro propiedades y `RequestClose()`:
   - `ModalId` — string único, para logs y deduplicación.
   - `ConsumesEscape` — si `true`, el `UIStateManager` llama `RequestClose()` al presionar ESC. Si `false`, ESC pasa al `PauseManager`.
   - `BlocksPause` — si `true`, la pausa no puede abrirse encima.
   - `PausesGame` — si `true`, el `UIStateManager` pone `Time.timeScale = 0`. Si `false`, el tiempo sigue corriendo pero el input del player igual queda bloqueado (porque `IsAnyModalOpen = true`).
5. En `OnBeforeOpen`: `UIStateManager.Instance.Push(this)`. En `OnBeforeClose`: `UIStateManager.Instance.Pop(this)`. **No tocar `Time.timeScale` ni `Cursor` directamente** — el UIStateManager los gobierna.
6. Si tu pantalla se abre **encima** de otra que ya pausó: no cambies nada de timeScale, el stack del UIStateManager lo resuelve solo.

### 7.4 Time.timeScale = 0: qué se rompe

- `Time.deltaTime` queda en 0 → cualquier `Update` que use eso para animar se congela.
- `WaitForSeconds` se queda esperando para siempre (usar `WaitForSecondsRealtime`).
- `Coroutines` con `yield return null` siguen disparando — pero `Time.deltaTime` es 0 dentro.

**Lo que NO se rompe**:
- `Input.GetKey*` sigue funcionando (por eso necesitamos el guard `IsGameplayInputBlocked` para bloquear input lógico).
- `Time.unscaledDeltaTime` sigue avanzando (lo usan los fades de UI y los timers del HUD del inventario).
- UniTask con `UniTask.Yield(PlayerLoopTiming.Update)` corre con o sin timeScale.

---

## 8. Cómo agregar una pantalla nueva (mini-tutorial)

Supongamos que querés agregar una pantalla de **estadísticas de la partida**, accesible desde Pausa.

### Paso 1: Decidir lifecycle

- ¿La abre algún botón desde otra UI? → persistente, con `static Instance`.
- ¿Es parte de un flujo lineal (Menu → Stats → Level)? → pushable, vía `ScreenEventChannel`.

Asumamos persistente para este ejemplo.

### Paso 2: MVC

Crear:
- `StatsModel : BaseScreenModel` — campos de stats (tiempo, items recolectados, muertes, etc.).
- `StatsView : BaseScreenView` — labels TMP, botón cerrar.
- `StatsController : BaseScreenController<StatsView, StatsModel>` — con `static Instance`, `IsOpen`, `OpenScreen()`.

### Paso 3: Suscribir desde donde se llama

En `PauseManagerUI.cs`, agregar un botón nuevo en `PauseView` y handler:

```csharp
private void HandleStats() => StatsController.Instance?.OpenScreen();
```

### Paso 4: Implementar IModalUI en StatsController

```csharp
public string ModalId        => "Stats";
public bool   ConsumesEscape => true;   // ESC cierra Stats
public bool   BlocksPause    => true;   // pausa no se abre encima
public bool   PausesGame     => true;   // congela tiempo al abrir
public void   RequestClose() => CloseSafe().Forget();
```

En `OnBeforeOpen`: `UIStateManager.Instance.Push(this)`. En `OnBeforeClose`: `UIStateManager.Instance.Pop(this)`.
No hace falta ningún `Update()` con `GetKeyDown` — el `UIStateManager` maneja ESC vía `UI/Exit` y llama `RequestClose()` automáticamente.

### Paso 5: Setup en Unity

- Crear escena `UI_Stats`, agregarla al `SO_SceneList` como **persistente**.
- En esa escena, GameObject raíz con `StatsController` + Canvas hijo con `StatsView`.
- Asignar la view en el Inspector del controller.

Listo. La pantalla se abre desde Pausa, ESC la cierra (gestionado por UIStateManager), y el timeScale y cursor quedan en manos del stack modal.

---

## 9. Mapa rápido de archivos

```
Assets/Scritps/
├─ BootingScene/
│   └─ BootingSceneLoader.cs              ← carga escenas iniciales
├─ Managers/
│   ├─ ScreenManager.cs                   ← carga aditiva de grupos
│   ├─ PauseManager.cs                    ← singleton de pausa, delega ESC guard a UIStateManager
│   ├─ AudioManager.cs                    ← SetMasterVolume/Music/SFX
│   ├─ GameResultManager.cs               ← evento OnGameResult (Win/Lose)
│   ├─ InteractionManager.cs              ← SphereCast desde la cámara + KeyCode.E al IInteractable activo
│   ├─ InventoryManager.cs                ← lista de ítems (lógica de negocio, no UI)
│   └─ PuzzleStateManager.cs              ← flags de puzzles/sockets/puertas/válvulas (sin persistencia)
├─ ScriptableScripts/
│   └─ Screens/
│       ├─ SO_SceneList.cs                ← base de datos de grupos + persistentes
│       └─ ScreenEventChannel.cs          ← canal Push/Pop/ClearAll
├─ UI/
│   ├─ Screen/
│   │   ├─ BaseScreenView.cs              ← ShowAsync/HideAsync (unscaledDeltaTime)
│   │   ├─ BaseScreenController.cs        ← Open/Close + hooks
│   │   ├─ BaseScreenModel.cs             ← POCO con Initialize/NotifyDataChanged
│   │   ├─ Pause/
│   │   │   ├─ PauseModel.cs              ← state machine de pausa
│   │   │   └─ PauseView.cs               ← botones continue/options/exit
│   │   ├─ Settings/
│   │   │   ├─ SettingsModel.cs           ← campos + PlayerPrefs + snapshot/revert
│   │   │   ├─ SettingsView.cs            ← raíz que delega en sub-views
│   │   │   ├─ SettingsController.cs      ← static Instance + OpenScreen()
│   │   │   ├─ SettingsTabSelector.cs     ← cambio de tab
│   │   │   ├─ SettingsPanelVolumeView.cs
│   │   │   ├─ SettingsPanelControlsView.cs
│   │   │   ├─ SettingsPanelBrightnessView.cs (placeholder)
│   │   │   └─ SettingsPanelScreenView.cs    (placeholder)
│   │   ├─ Document/                      ← DocumentReader (notas)
│   │   ├─ Win/                           ← WinController/View
│   │   ├─ Result/                        ← ResultScreenController/View + ResultPresentation
│   │   │                                    (reemplazó a LoseController y GameOverController)
│   │   ├─ Loading/                       ← LoadingController/View
│   │   └─ MainMenu/                      ← MainMenu, SaveSlots
│   ├─ Managers/
│   │   ├─ PauseManagerUI.cs              ← controller del view de pausa
│   │   ├─ InventoryManagerUI.cs          ← Tab abre, ESC capas, timers
│   │   └─ SequencePanelUIController.cs   ← puzzles de secuencia
│   ├─ Interaction/
│   │   └─ InteractionPromptView.cs       ← prompt "Agarrar", "Necesitas X"
│   └─ HUD/
│       └─ Vignette/                      ← Vignettes de proximidad/chase
├─ Player/
│   ├─ PlayerCameraController.cs          ← Cinemachine config + lock del cursor
│   ├─ CameraSensitivityApplier.cs        ← aplica Settings_Sensitivity + InvertY al rig
│   ├─ CameraInputBlocker.cs              ← apaga el InputAxisController con modal abierta
│   └─ Player FSM/                        ← state machine del player
│       └─ PlayerStateManager.cs          ← movimiento, lee inputs (no existe PlayerController.cs)
└─ Interactables/
    └─ ...                                ← items, doors, sockets (implementan IInteractable)
```

---

## 10. Bugs conocidos y caveats

### 10.1 PauseManager.OnEnable/OnDisable con InputAction
`pauseAction.action.performed += _ => Toggle();` crea un lambda nuevo cada vez. El `-=` correspondiente crea otro lambda distinto, así que el unsubscribe no funciona. Resuelto: `PauseManager` cachea el handler en `pauseActionHandler` y lo reutiliza en `OnEnable`/`OnDisable`. Verificar si hay otros lugares en el proyecto con el mismo patrón.

### 10.2 Doble ESC durante fade de Settings
Si apretás ESC dos veces muy rápido (en los 300ms del fade out), el segundo ESC puede llegar al PauseManager porque `SettingsController.IsOpen` ya pasó a false al inicio del fade. Resultado: despausa el juego. Edge case chico, ignorable salvo que importe.

### 10.3 ~~GameResultManager — estado estático persistente~~ ✅ Resuelto
`GameResultManager.ResetSession()` se llama ahora en `MainMenuController.HandleNewGame()` antes de empujar el grupo de gameplay. **Pendiente**: cuando se implemente Load Game en `SaveSlotsController`, ese flujo también debe llamar `ResetSession()` antes de cargar la partida guardada.

### 10.4 DocumentReader — race condition ESC con PauseManager
`DocumentReaderController` tiene `ConsumesEscape = true` y `BlocksPause = false`. Cuando ESC se presiona con el documento abierto, el `UIStateManager` cierra el documento **y** el `PauseManager` puede disparar en el mismo frame (porque `IsBlockingPause` es `false`). Resultado posible: documento se cierra y el menú de pausa se abre en la misma pulsación. Si esto molesta en testing, cambiar a `BlocksPause = true` en `DocumentReaderController`.

---

## 11. Eventos estáticos del proyecto (referencia rápida)

| Evento | Dispara | Escuchan |
|---|---|---|
| `PauseManager.OnPauseStateChanged` | toggle de pausa | PauseManagerUI |
| `GameResultManager.OnGameResult` | ReportWin/ReportLoss/ReportGameOver | WinController, ResultScreenController |
| `SettingsModel.OnSettingsApplied` | Apply en Settings | CameraSensitivityApplier |
| `NemesisEvents.OnChaseStarted/Ended` | Nemesis entra/sale de `{Chasing, Catch}` | VignetteChaseView |
| `NemesisEvents.OnProximityChanged` | cada frame, distancia real al player | VignetteProximityView |
| `InteractionEvents.OnTargetChanged` | InteractionManager cambia interactable activo | InteractionPromptView |
| `InventoryEvents.OnItemAdded/Removed` | item entra/sale del inventario | InteractionPromptView, ModuleHUDView |
| `InventoryEvents.OnModuleTimerTick/StateChanged/Exploded` | timers de módulos | ModuleHUDView |

---

## 12. Referencias en código

Para entender un pattern específico, leer estos archivos como modelo:

- **Controller persistente con static Instance + IModalUI no-pausante**: `DocumentReaderController.cs` — ejemplo de `PausesGame = false` (tiempo corre, input bloqueado).
- **Controller con InjectDependencies + apertura por evento estático**: `WinController.cs`, `ResultScreenController.cs`.
- **Presentación por datos en vez de por subclase**: `ResultPresentation.cs` — un preset serializado por `GameState` en lugar de un controller por pantalla.
- **Model con snapshot/revert + PlayerPrefs**: `SettingsModel.cs`.
- **View con sub-views y re-emisión de eventos**: `SettingsView.cs`.
- **Vista permanentemente activa con CanvasGroup.alpha**: `InteractionPromptView.cs`.
- **HUD overlay que se congela con timeScale=0**: `VignetteChaseView.cs` (usa `Fade()` con deltaTime).
