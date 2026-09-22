# Sistema de UI y Pausa — Guía para desarrolladores

Este documento explica cómo funciona la UI del juego: la arquitectura MVC, el ciclo de vida de las pantallas, la carga aditiva de escenas, el sistema de pausa, y las convenciones que hay que respetar al agregar features nuevas.

Está pensado para que alguien que se suma al proyecto pueda navegar el código y agregar pantallas nuevas sin romper lo existente.

> ⚠️ **Nota de idioma**: el código de `Assets/_Project/Scripts/` está íntegramente en inglés (comentarios,
> strings, logs y textos de UI). Este documento sigue en español. Ver `docs/CLAUDE.md` § Language rule.

---

## 1. Visión general

El proyecto usa **carga aditiva de escenas** para componer la UI. En cualquier momento del juego hay varias escenas cargadas a la vez (`Data`, `SettingsScene` y las del grupo activo, p. ej. `WIRED_Zona1_Blockout` + `LevelUI`), cada una con responsabilidades distintas. La navegación entre menús no carga/descarga el juego entero — solo agrega o quita escenas específicas.

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
| `Assets/_Project/Scripts/UI/Screen/BaseScreenController.cs` | Clase genérica `<TView, TModel>`. Define `Open()`, `Close()`, hooks virtuales. |
| `Assets/_Project/Scripts/UI/Screen/BaseScreenView.cs` | Wrapper de `CanvasGroup` con `ShowAsync()`, `HideAsync()`, `Fade()`. |
| `Assets/_Project/Scripts/UI/Screen/BaseScreenModel.cs` | POCO con `Initialize()`, `IsInitialized`, evento `OnDataChanged`. |

### Lifecycle de una pantalla

```
Open()          →  OnBeforeOpen()  →  view.ShowAsync()  →  OnAfterOpen()
Close()         →  OnBeforeClose() →  view.HideAsync()  →  OnAfterClose()
```

Los 4 hooks (`OnBeforeOpen`, `OnAfterOpen`, `OnBeforeClose`, `OnAfterClose`) son virtuales en `BaseScreenController` y los override cada Controller concreto para hacer cosas específicas: Push/Pop en el `UIStateManager` (que es quien gobierna `Time.timeScale` y el cursor, ver §7.3), popular el view, etc.

### ShowAsync / HideAsync usan unscaledDeltaTime

**Importante**: los fades de `BaseScreenView.ShowAsync()` y `HideAsync()` usan `Time.unscaledDeltaTime`, así que **funcionan aunque `Time.timeScale = 0`**. Esto es clave porque varias pantallas (Pausa, Settings, SequencePanel, el reader en modo lectura) se abren con timeScale = 0 y deben poder animar el fade igual. El inventario ya no pausa (`PausesGame => false`).

El método genérico `Fade(alpha, duration)` **también** usa `Time.unscaledDeltaTime` (antes usaba `deltaTime` y el fade del prompt de interacción quedaba a medias en pausa). Un overlay que tiene que "congelarse" al pausar anima su alpha por su cuenta con `Time.deltaTime`, sin `Fade()`: es el caso de `VignetteChaseView`.

---

## 3. Carga aditiva: ScreenManager + ScreenEventChannel + SO_SceneList

### Archivos clave

| Archivo | Rol |
|---|---|
| `Assets/_Project/Scripts/Managers/ScreenManager.cs` | Singleton que carga/descarga grupos de escenas. Escucha eventos del channel. |
| `Assets/_Project/Scripts/ScriptableScripts/Screens/SO_SceneList.cs` | Base de datos: nombre de grupo (`"Menu"`, `"TestBlocking"`) → lista de escenas, y lista de escenas **persistentes**. El asset es `ScriptableObjects/Screen and Scenes/Scene List.asset`. |
| `Assets/_Project/Scripts/ScriptableScripts/Screens/ScreenEventChannel.cs` | Event channel ScriptableObject. Expone `RaisePushScreen(label)`, `RaisePopScreen()`, `RaiseClearAll()`. |
| `Assets/_Project/Scripts/BootingScene/BootingSceneLoader.cs` | Carga las escenas iniciales al arrancar el juego. |

### Cómo funciona la navegación

1. Algún código (ej: `MainMenuController.EnterGameplay`, desde New Game o un slot) hace `screenChannel.RaisePushScreen(firstSceneLabel)` — hoy `"TestBlocking"` (`WIRED_Zona1_Blockout` + `LevelUI`).
2. `ScreenManager.OnPushScreenRequestedWrapper(label)` recibe el evento.
3. Descarga el grupo activo anterior (si hay) y carga las escenas del nuevo grupo en paralelo (`UniTask.WhenAll`).
4. Mantiene un `Stack<string>` de pantallas activas para que `RaisePopScreen()` vuelva atrás.

**Escenas persistentes**: las que están en `SO_SceneList.persistentSceneNames` no se descargan nunca. Hoy son dos: `Data` y `SettingsScene`. `Bootstrap` las carga, empuja el primer grupo (`defaultStartGroup` = `Menu`) y se descarga a sí misma.

### Por qué importa la distinción persistente vs pushable

- **Pushable** (`Menu` = `MainMenu` + `MainMenuUI`, `TestBlocking` = `WIRED_Zona1_Blockout` + `LevelUI`, y los grupos de test `TestIñaki`, `TestNemesis`…, todos con `LevelUI`): se cargan/descargan según la navegación. Los managers que vivan ahí mueren al descargar. **`LevelUI` no es persistente**: viaja dentro de cada grupo de gameplay, así que sus controllers (`PauseManagerUI`, `InventoryManagerUI`, `DocumentReaderController`, `SequencePanelUIController`, `SkillCheckController`, el HUD) se recrean con cada nivel.
- **Persistente** (`Data`, `SettingsScene`): siempre vivas. Sus singletons (`PauseManager`, `UIStateManager`, `ModuleManager`, `ScreenManager` en `Data`; `SettingsController` en `SettingsScene`) se pueden invocar desde cualquier escena.

---

## 4. UI modales: el patrón de "controller persistente con static Instance"

Hay un grupo de UIs que se abren **sobre** la pantalla actual: Pausa, Settings, Inventario, SequencePanel (puzzles), SkillCheck, DocumentReader (notas). Estas no se cargan con el flujo de `ScreenManager` — viven en una escena de UI que ya está cargada y se invocan directo.

### Patrón común

Cada uno de estos controllers:

1. Vive en `LevelUI` (viaja con cada grupo de gameplay) o en `SettingsScene` (persistente; `SettingsController` está en el root de `CanvasSettings.prefab`).
2. Expone `public static SettingsController Instance { get; private set; }` (o el nombre que sea) y lo asigna en `Awake`.
3. Expone `public bool IsOpen` para que otros sistemas (típicamente `PauseManager`) sepan si está activo.
4. Tiene un método público `OpenScreen()` / `Open(data)` que cualquier código puede llamar.
5. **NO maneja su propio ESC.** El `UIStateManager` escucha la action `UI/Exit` y llama
   `RequestClose()` sobre la modal del top que declare `ConsumesEscape = true`. Los controllers
   no deben tener un `Update()` con `GetKeyDown(KeyCode.Escape)`.

Ejemplos en el código:
- `DocumentReaderController.Instance.Open(inventoryItem)` — invocado desde `PickupInteractable` al levantar una nota (modo lectura: congela el juego).
- `DocumentReaderController.Instance.Open(documentData)` — invocado desde `NoteInteractable` (lectura in situ: el mundo sigue corriendo).
- `SequencePanelUIController.Instance.Open(panel)` — invocado desde `SequencePanelInteractable`.
- `SettingsController.Instance.OpenScreen()` — invocado desde `PauseManagerUI.HandleSettings()` y `MainMenuController.HandleSettings()`.
- `InventoryManagerUI.Instance.OpenInventory()` — invocado desde su propio `HandleInput()` con la action Player/Inventory (Tab). No abre con otra modal arriba, ni capturado, tirado o escondido.

### Por qué `static Instance` y NO `Singleton<T>`

`Singleton<T>` (el de `Assets/_Project/Scripts/SingletonCreator/Singleton.cs`) está pensado para managers globales que pueden hacer `DontDestroyOnLoad`. Los controllers de UI persistente NO necesitan eso — la escena ya garantiza una sola instancia. Solo necesitan el accessor global. `public static T Instance { get; private set; }` + asignar en `Awake` es suficiente. (Excepción: `InventoryManagerUI` hereda de `Singleton<T>` con `CreateSingleton(false)`, sin `DontDestroyOnLoad`.)

---

## 5. Sistema de Pausa

### Componentes

| Archivo | Rol |
|---|---|
| `Assets/_Project/Scripts/Managers/PauseManager.cs` | Singleton<PauseManager> en `Data`. Guarda el estado de pausa, escucha Player/Pause, dispara evento estático `OnPauseStateChanged`. **No toca `Time.timeScale`**: lo pone el `UIStateManager` cuando `PauseManagerUI` hace Push. |
| `Assets/_Project/Scripts/UI/Screen/Pause/PauseModel.cs` | Estado `PauseState { Unpaused, Paused }`. |
| `Assets/_Project/Scripts/UI/Screen/Pause/PauseView.cs` | Botones Continue / Settings / Main Menu / Exit. |
| `Assets/_Project/Scripts/UI/Managers/PauseManagerUI.cs` | Controller (en `LevelUI`). Escucha `OnPauseStateChanged` y abre/cierra el view. |

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
¿Cambio de escena en curso, o no hay player cargado (menú)? → sí: return
      │
      ▼
¿Hay una UI bloqueante abierta? (ver §5.1)
      ├─ Sí  → return (la UI bloqueante consume el ESC ella misma)
      └─ No  → Pause() → model.Pause() → state pasa a Paused → dispara OnPauseStateChanged
                    │
                    ▼
              PauseManagerUI.HandlePauseStateChanged(state)
                    │
                    ▼
              Open() → OnBeforeOpen() → UIStateManager.Push(this)
                                         (PausesGame: Time.timeScale = 0, cursor libre)
                    │
                    ▼
              view.ShowAsync() (fade con unscaledDeltaTime)
```

Al apretar Continue (o ESC), pasa lo inverso: `PauseManager.RequestUnpause()` → `model.Unpause()` → evento → `Close()` → `OnBeforeClose()` → `UIStateManager.Pop(this)`, que restaura timeScale y cursor cuando el stack queda vacío. Continue no cierra la modal que hubiera abajo (p. ej. el panel de secuencia): se vuelve a ella.

### 5.1 Guard de ESC — UIStateManager.IsBlockingPause

`PauseManager.TryToggleFromInput()` **no mantiene ninguna lista manual** de controllers. Delega completamente al `UIStateManager`:

```csharp
private void TryToggleFromInput()
{
    if (IsPaused) return;
    if (ScreenManager.IsInputLocked) return;
    if (!PlayerRegistry.HasPlayer) return;   // ESC en el menú no debe dejar la pausa trabada
    if (UIStateManager.Exists && UIStateManager.Instance.IsBlockingPause) return;
    Pause();
}
```

`UIStateManager.IsBlockingPause` retorna `true` si alguna modal en el stack declara `BlocksPause = true`. **Cuando agregás una UI modal nueva**, solo necesitás implementar `IModalUI` correctamente y hacer Push/Pop en UIStateManager — no hay lista que mantener manualmente.

### 5.2 Bloqueo de inputs del player

`PauseManager` expone:

```csharp
public static bool IsGameplayInputBlocked
    => (Exists && Instance.IsPaused)
    || (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen)
    || ScreenManager.IsInputLocked;   // también durante un cambio de escena
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
SettingsScene (escena persistente)
└─ CanvasSettings (instancia de CanvasSettings.prefab; SettingsController en el root)
    └─ SettingsRoot (SettingsView + CanvasGroup)
        ├─ TopBar (BackButton)
        └─ Body
            ├─ TabsColumn (SettingsTabSelector: Tab_Brightness, Tab_Controls, Tab_Screen, Tab_Volume)
            └─ ContentColumn
                ├─ PanelsHost
                │   ├─ Panel_Brightness (SettingsPanelBrightnessView — brillo, contraste, gamma, CRT, dither)
                │   ├─ Panel_Controls   (SettingsPanelControlsView — sensibilidad + invertir Y)
                │   ├─ Panel_Screen     (SettingsPanelScreenView — resolución, modo, FPS, VSync)
                │   └─ Panel_Volume     (SettingsPanelVolumeView — master, música, SFX)
                └─ Footer (BtnApply / BtnReset)
```

### Modelo con snapshot/revert

`SettingsModel` tiene los valores actuales (`MasterVolume`, `Sensitivity`, etc.) y un snapshot interno (`_snapMaster`, `_snapSensitivity`, etc.). Al abrir, `TakeSnapshot()` captura el estado. Si el usuario cambia sliders y aprieta **Back**, `Revert()` restaura el snapshot. Si aprieta **Apply**, persiste en PlayerPrefs, llama `AudioManager.SetMasterVolume(...)`, dispara `OnSettingsApplied` (evento estático) y vuelve a tomar snapshot.

### Por qué un evento estático

`CameraSensitivityApplier` vive en el prefab del player (`Player.prefab`, escena de gameplay; también en `HidingSpot.prefab`). `SettingsModel` vive en `SettingsScene`. **Son escenas distintas — no hay forma de pasarle referencia directa**. El evento estático `SettingsModel.OnSettingsApplied` permite que `CameraSensitivityApplier.HandleSettingsApplied()` se entere sin coupling.

Este patrón se repite en todo el proyecto:
- `NemesisEvents.OnChaseStarted` → escuchado por `VignetteChaseView`.
- `InventoryEvents.OnItemAdded` → escuchado por `InteractionPromptView`, `InteractionNotificationFeed`.
- `GameResultManager.OnGameResult` → escuchado por `WinController`, `ResultScreenController`.

### Sub-views por tab

`SettingsView` no implementa los sliders directamente — delega en sub-views (`SettingsPanelVolumeView`, etc.) que viven en GameObjects hijos. Cada sub-view:

1. Tiene sus `[SerializeField] Slider` / `Toggle`.
2. Suscribe sus listeners en `Awake`, los limpia en `OnDestroy`.
3. Re-emite los cambios con su propio `event Action<float>` (ej: `OnMasterChanged`).
4. Expone `Populate(model)` para refrescar valores cuando Settings se abre.

`SettingsView` agrega esos eventos en `WireXxxPanel()` y los re-emite en sus propios eventos públicos para que `SettingsController` solo conozca a `SettingsView`.

### Quién aplica cada opción

Ya no queda ningún panel "placeholder": los cuatro escriben en el model, y los campos los leen
appliers suscritos a `SettingsModel.OnSettingsApplied` que leen las keys de PlayerPrefs:
`PostProcessSettingsApplier` (brillo/contraste/gamma), `PS1EffectApplier` y `UIPSXSettingsApplier`
(CRT/dither, mundo y UI), `ScreenSettingsApplier` (resolución/modo/FPS/VSync),
`CameraSensitivityApplier` (sensibilidad + invertir Y), `AudioBackgroundApplier` y
`AmbienceComfortApplier`. Ver la tabla key → applier en `docs/CLAUDE.md`.

El modo "Fullscreen" de Options aplica `FullScreenWindow`, igual que "Borderless": nada usa
`ExclusiveFullScreen`, que en DX12 crashea al perder el foco (UUM-134743; ver `ScreenSettingsApplier.Modes`).

Sin control en Options todavía:

- Rebinding de teclas (`SettingsPanelControlsView` muestra labels estáticos).
- `Settings_VHSGlitch`: la leen `GlitchController` y `UISignalStaticBurst`, pero ni `SettingsModel` ni
  Options la tienen.
- `Settings_AudioInBackground` y `Settings_LowFreqAmbience`: están en `SettingsModel` (con
  snapshot/revert) y tienen applier, pero ningún panel llama `SetAudioInBackground` /
  `SetLowFreqAmbience`, así que en la práctica quedan en su default.

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

**Hooks estáticos a `GameSession.OnNewSessionStarting`**: registrarlos con
`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]`, **no** con
`SubsystemRegistration`. `GameSession` limpia ese evento en `SubsystemRegistration`, y Unity no ordena
métodos del mismo tipo de carga: si el hook corría primero quedaba borrado. Así pasó con
`GameResultManager.ResetSession` (WIR-035: después del primer resultado se ignoraba el `WinTrigger`) y
con `InputHintEvents` (los hints no volvían a salir en la segunda partida).

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
- `Time.unscaledDeltaTime` sigue avanzando (lo usan los fades y tweens de UI y el tiempo de sesión de `ModuleManager`).
- UniTask con `UniTask.Yield(PlayerLoopTiming.Update)` corre con o sin timeScale.

### 7.5 Escalado y anclaje: márgenes fijos, no fracciones

El proyecto tiene **14 Canvas Scaler** (13 prefabs en `Prefabs/UI` + el `CrosshairCanvas` de `LevelUI.unity`), y el
layout ya está calibrado a 1920x1080. Las reglas de abajo existen para que agregar un nodo no
descalibre el resto.

**El criterio de aceptación es doble**, y hay que cumplir los dos:

1. A **1920x1080** el resultado tiene que ser **idéntico** al original. Si algo se movió aunque sea
   un pixel a la resolución de referencia, el anclaje está mal.
2. En el resto de las resoluciones, **nada puede quedar fuera del canvas**. Probar contra las cinco
   de `ScreenSettingsApplier.Resolutions` — 1920x1080, 2560x1440, 3840x2160, 1366x768 y 1280x720.
   Son todas 16:9, así que un fallo acá casi siempre es un anclaje en fracciones, no un problema de
   aspect ratio.

**Reglas concretas:**

- **Las tiras horizontales van con márgenes fijos.** Una topbar, un footer o una barra de hints se
  anclan con **stretch + inset** (left/right en pixeles, alto fijo), **nunca** con `anchorMin`/
  `anchorMax` en fracciones. Una fracción escala el alto de la tira con la pantalla, y una topbar de
  28px se convierte en una de 56px a 4K.
- **Los textos de una línea van a tamaño fijo, anclados a un punto** — esquina o borde —, **no
  estirados**. Un `TextMeshProUGUI` estirado reflowea distinto en cada resolución, y con
  `DotLeader()` (relleno de puntos por conteo de caracteres) eso rompe la alineación de la lista
  entera.
- **El relleno de puntos depende de fuente monoespaciada.** `InventoryTextFormat.DotLeader()` calcula
  sobre un ancho fijo **en caracteres**, no en pixeles. Funciona porque todo el inventario está en
  Share Tech Mono. Cambiar cualquier fila a una fuente proporcional desalinea la columna.

### 7.6 Animaciones de UI con LeanTween

Dos reglas, y las dos vienen de bugs reales:

- **`LeanTween.cancel(gameObject)` antes de cada tween nuevo.** Sin eso, dos tweens sobre la misma
  propiedad corren a la vez y el último en escribir gana por frame — el objeto tiembla o queda a
  mitad de camino. Es especialmente fácil de provocar donde hay **pooling**: un `ItemSlotView`
  reciclado puede traerse el tween del item anterior.
- **`setIgnoreTimeScale(true)` siempre.** La pausa, Settings, el panel de secuencia y el reader en
  modo lectura abren con `Time.timeScale = 0` (lo pone el `UIStateManager` cuando alguna modal declara
  `PausesGame = true`; el inventario ya no). Un tween que no ignora el timeScale se congela a mitad de
  la animación y no termina nunca.

**Con pooling, el tween se dispara en el `Setup()` de la fila, no en `Awake`.** El `Awake` de un
objeto pooleado corre una sola vez, la primera; las apariciones siguientes reusan el mismo
GameObject y nunca lo vuelven a llamar.

La misma lógica aplica a cualquier animación por código, no solo a LeanTween: los fades de
`BaseScreenView.ShowAsync()`/`HideAsync()` usan `Time.unscaledDeltaTime` por esta razón, y
`UISlideTransition` expone `ignoreTimeScale` (default `true`) por lo mismo. Ver §7.4.

### 7.7 Sorting order de los canvas

Los canvas de UI son todos **Screen Space - Overlay**, así que quién tapa a quién lo decide únicamente
el `sortingOrder` del Canvas raíz — la jerarquía no interviene, porque viven en escenas distintas.
La escalera vive en los prefabs; ninguna escena la pisa:

| Orden | Canvas |
|---|---|
| 0 | CanvasMainMenu, CanvasSaveSlots |
| 1 | Inventory Canvas, HUDCanvas |
| 3 | CanvasResult, CanvasWin |
| 50 | SequencePanelCanvas, SkillCheckCanvas |
| 60 | DocumentReaderCanvas |
| **70** | **CanvasPause** |
| **80** | **CanvasSettings** |
| 100 | InteractionCanvas (se esconde solo ante cualquier modal, ver `InteractionPromptView.HandleModalPushed`) |
| 1000 | CrosshairCanvas (objeto de escena en `LevelUI`, no es prefab) |
| 32000 | UI_LoadingScreen |

> **Sin overrides de `m_SortingOrder` en las escenas.** Hasta el 22/09 `LevelUI.unity` y
> `SettingsScene.unity` pisaban el orden (overrides de `326a6790`, anteriores a que la pausa y Settings
> subieran a 70/80 en `ae59f620`). En juego, la pausa quedaba en 50, debajo del reader, y el prompt en 1.
> Se revirtieron. El HUD bajó de 3 a 1 **en el prefab**, que es lo que valía en juego, para seguir
> debajo de Result/Win. Si un canvas tiene que cambiar de lugar, cambialo en el prefab y fijate que la
> instancia no quede con override (en el Inspector el campo aparece en negrita).

Dos reglas que la escalera codifica y que conviene no romper:

- **La pausa va encima de todo modal de gameplay.** `PauseManager.TryToggleFromInput()` la describe como
  un overlay global: se abre sobre el inventario, el reader y el panel de secuencia, y sólo respeta
  `IModalUI.BlocksPause`. Si un modal nuevo necesita quedar por encima, la respuesta es que declare
  `BlocksPause => true`, no que suba su canvas por encima de 70.
- **Settings va encima de la pausa**, porque se abre desde ella. (Con los overrides de hoy se sigue
  cumpliendo: 51 contra 50.)

`CanvasCRTPresenter` copia el `sortingOrder` del canvas al canvas overlay donde dibuja el tubo, así
que la escalera vale igual para las pantallas que pasan por CRT.

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
Assets/_Project/Scripts/
├─ BootingScene/
│   └─ BootingSceneLoader.cs              ← carga escenas iniciales
├─ Managers/
│   ├─ ScreenManager.cs                   ← carga aditiva de grupos
│   ├─ PauseManager.cs                    ← singleton de pausa, delega ESC guard a UIStateManager
│   ├─ AudioManager.cs                    ← SetMasterVolume/Music/SFX
│   ├─ GameResultManager.cs               ← evento OnGameResult (Win/Lose)
│   ├─ InteractionManager.cs              ← cast por la mira (InteractionProbe) + GameInput.InteractPressed
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
│   │   │   └─ PauseView.cs               ← botones continue/settings/main menu/exit
│   │   ├─ Settings/
│   │   │   ├─ SettingsModel.cs           ← campos + PlayerPrefs + snapshot/revert
│   │   │   ├─ SettingsView.cs            ← raíz que delega en sub-views
│   │   │   ├─ SettingsController.cs      ← static Instance + OpenScreen()
│   │   │   ├─ SettingsTabSelector.cs     ← cambio de tab
│   │   │   ├─ SettingsPanelVolumeView.cs
│   │   │   ├─ SettingsPanelControlsView.cs
│   │   │   ├─ SettingsPanelBrightnessView.cs
│   │   │   └─ SettingsPanelScreenView.cs
│   │   ├─ Document/                      ← DocumentReader (notas)
│   │   ├─ Win/                           ← WinController/View
│   │   ├─ Result/                        ← ResultScreenController/View + ResultPresentation
│   │   │                                    (reemplazó a LoseController y GameOverController)
│   │   ├─ Loading/                       ← LoadingController/View
│   │   └─ MainMenu/                      ← MainMenu, SaveSlots
│   ├─ Managers/
│   │   ├─ UIStateManager.cs              ← stack de modales: timeScale, cursor, ESC (UI/Exit)
│   │   ├─ PauseManagerUI.cs              ← controller del view de pausa
│   │   ├─ InventoryManagerUI.cs          ← Tab abre, ESC capas (los módulos están en ModuleManager)
│   │   ├─ SequencePanelUIController.cs   ← puzzles de secuencia
│   │   └─ SkillCheckController.cs        ← skill check (+ SkillCheckTestKey, F6)
│   ├─ Interaction/
│   │   ├─ InteractionPromptView.cs       ← prompt "Pick up X", "You need X"
│   │   └─ SequencePanel*/SkillCheck*     ← model + view de cada puzzle
│   └─ HUD/
│       ├─ ModuleTimerHUDView.cs          ← timer del módulo (+ ModuleTimerBeeper)
│       ├─ InteractionNotificationFeed.cs ← feed de notificaciones
│       ├─ HidingOverlayView.cs           ← lo que se ve desde el escondite
│       ├─ BreathHoldMeterView.cs         ← medidor de aliento
│       ├─ ModalVisibilityGate.cs         ← oculta un nodo del HUD bajo modales
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
`GameResultManager.ResetSession()` corre en cada `GameSession.BeginNewSession()`, colgado de `GameSession.OnNewSessionStarting` (ver §7.1 por qué con `AfterAssembliesLoaded`). `MainMenuController.EnterGameplay()` llama `BeginNewSession()` antes de empujar el grupo de gameplay, y lo usan tanto New Game como la elección de un slot (`HandleSlotSelected`), así que el Load Game futuro ya pasa por ahí.

### 10.4 ~~DocumentReader — race condition ESC con PauseManager~~ ✅ Resuelto en modo lectura
`DocumentReaderController` declara ahora `BlocksPause => isOpen && pausesWhileOpen`: en **modo lectura** (la hoja que se abre sola al agarrar una nota) la pausa queda bloqueada, así que ESC cierra la hoja y nada más. Además el juego ya está congelado, así que la pausa no aportaría nada. (Igual, la pausa ordena en 70, por encima del reader; ver §7.7.)

**Sigue abierto en lectura in situ** (`Open(SO_DocumentData)`, desde `NoteInteractable`): ahí el mundo sigue corriendo y la pausa tiene que poder abrirse, así que `BlocksPause` queda en `false` y la race condition original aplica igual. Hoy no hay ninguna `NoteInteractable` colocada en ninguna escena, así que no se manifiesta.

---

## 11. Eventos estáticos del proyecto (referencia rápida)

| Evento | Dispara | Escuchan |
|---|---|---|
| `PauseManager.OnPauseStateChanged` | toggle de pausa | PauseManagerUI, AudioBackgroundApplier, ModuleManager |
| `GameResultManager.OnGameResult` | ReportWin/ReportLoss/ReportGameOver (Win y GameOver pueden pasar antes por un presenter, ver abajo) | WinController, ResultScreenController, CaptureFadeView, SkillCheckController (+ audio del Nemesis y EscapeSequenceDirector) |
| `SettingsModel.OnSettingsApplied` | Apply en Settings | los appliers de §6, GlitchController, UISignalStaticBurst |
| `NemesisEvents.OnChaseStarted/Ended` | Nemesis entra/sale de `{Chasing, Catch}` | VignetteChaseView (+ NemesisChaseMusic, NemesisTension) |
| `NemesisEvents.OnProximityChanged` | cada frame, distancia real al player | VignetteProximityView, VignetteChaseView |
| `NemesisEvents.OnStateChanged` | el Nemesis cambia de estado | NemesisAudio, NemesisChaseMusic |
| `NemesisEvents.OnCaptureResolved` | terminó la captura: el Nemesis ya se reubicó | CaptureFadeView (+ PlayerStateManager, EscapeChaseRestart) |
| `InteractionEvents.OnTargetChanged` | InteractionManager cambia interactable activo | InteractionPromptView, DocumentReaderController (auto-close in situ), ItemGlint, ItemProximityHighlight |
| `InteractionEvents.OnGlobalMessage` | cualquier sistema publica un mensaje de interacción | InteractionNotificationFeed |
| `InventoryEvents.OnItemAdded/Removed` | item entra/sale del inventario | InteractionPromptView, InventoryManagerUI, InteractionNotificationFeed (sólo Added) |
| `ModuleEvents.OnTimerTick/OnStateChanged/OnExploded` | `ModuleManager` (los viejos `InventoryEvents.OnModule*` ya no existen) | ModuleHUDView, ActiveModuleDisplay, ModuleTimerHUDView, ModuleTimerBeeper |
| `ModuleEvents.OnTimeAdjusted` | `ModuleManager.ApplyTimePenalty` / `ApplyTimeBonus`, con el delta aplicado | ModuleTimerHUDView (popup "-5s"/"+3s"), ActiveModuleDisplay |
| `UIStateManager.OnModalPushed/Popped` | se abre/cierra un modal | ModalVisibilityGate, InteractionPromptView, CameraInputBlocker, ArchitectSubtitleView, InputHintView (sólo Popped) |
| `HidingEvents.OnEntered/OnExited` | el player entra/sale de un escondite | HidingOverlayView |

**Presenters de resultado.** `GameResultManager.ReportWin` y `ReportGameOver` marcan el resultado
como reportado y, si hay un presenter registrado (`WinPresenter` / `GameOverPresenter`), le dejan
correr su plano antes de disparar `OnGameResult`. Hoy `EscapeSequenceDirector` registra el de Win (el
portón que se cierra) y `ModuleExplosionSequence` el de GameOver (la explosión); sin presenter el
resultado sale en el acto. La victoria de gameplay la reporta `WinTrigger` (en `WIRED_Zona1_Blockout`).

### Timer del módulo en el HUD

`HUDCanvas.prefab` → `ModuleTimerHUD`: ventana Win95 arriba a la izquierda (anclada a un punto, en 24, -24) con el MM:SS del módulo activo adentro de un anillo de bloques que se vacía (`UIRingArc`,
30 bloques), la etiqueta `M2 // CHEST`, un pip por módulo y el popup de salto de tiempo.

- **Visibilidad**: entra deslizándose cuando un módulo pasa a Active; al resolverse o explotar se queda
  quieta mostrando el resultado (`RESOLVED` / `EXPLODED`) hasta que el siguiente módulo pasa a Active y
  la reemplaza (con `hideWhenSettled`, apagado en el prefab, saldría a los `settledHoldSeconds`). Cuando
  el Nemesis agarra al player sale, y vuelve a entrar cuando se levantó y recuperó el control
  (`PlayerStateManager.IsRecoveringFromCapture`, el mismo tramo en que el timer está frenado).
- **Planos sin HUD** (`CinematicState.HudHidden`): los planos de cámara de seguridad del escape (el
  portazo al salir al pasillo y el portón del final, hasta la pantalla de victoria) lo prenden con
  `CinematicState.SetHudHidden(true)` desde `EscapeSequenceDirector`. Mientras está prendido el timer
  sale **en el mismo frame**, sin slide (es un corte duro: una ventana deslizándose sobre el plano nuevo
  es justo lo que el plano no quiere), y vuelve a entrar deslizándose cuando se apaga. Es un flag, no
  un evento: la view lo lee en su `Update`. Otros planos de la misma cinemática conservan el HUD.
- El root nunca se desactiva (ahí viven las suscripciones): la muestra/oculta el `UISlideTransition` de
  `Window`, y el corte instantáneo apaga sólo `Window` (`SlideIn` la vuelve a prender). El pulso de cada
  bip escala `RingRoot` (el slide cancela todos los tweens de su propio objeto).
- **`ModalVisibilityGate.ignoredModalIds`**: el gate del root lleva `SkillCheck`, así el timer queda
  visible durante el skill check (ahí caen las penalizaciones) y se oculta con inventario, pausa, etc.
  Con la lista vacía el gate se comporta como siempre.
- **Urgencia**: en marcha van en ámbar (`timerColor`); con ≤30 s el tiempo y el anillo pasan a Accent y titilan; `ModuleTimerBeeper` bipea
  1/s y 2/s por debajo de 10 s, alineado a la grilla del intervalo (un salto de tiempo = un bip, no una
  ráfaga).
- La armó un builder de un solo uso (ya borrado) y después se retocó a mano (380×210, sin barra de
  título). El prefab es la fuente de verdad.

### Skill check (Puzzle Central 2)

`SkillCheckCanvas.prefab` en `LevelUI`, manejado por el objeto `SkillCheckController`. Estilo Dead by
Daylight: en cada intento aparece la zona en un lugar sorteado (`zoneSectors` con peso) y la aguja da
**una** vuelta desde las 12. Cada check tiene un solo intento: [E] en la zona acierta, y en su franja
inicial "perfect" además devuelve tiempo al módulo; fuera de la zona, o sin apretar, resta tiempo. Acierto
o fallo, pasa al siguiente check. Al terminar la ronda, si hubo **algún** fallo la ronda entera se da
por perdida (`SEQUENCE FAILED - RESTART`, `failHoldTime`) y vuelve a empezar desde el primer check con
zonas nuevas (`SkillCheckModel.RestartRound`): hay que acertarlos todos seguidos. Todo el tuning está en
`SO_SkillCheckData` (`ScriptableObjects/Puzzle2/`).

- **MVC**: `SkillCheckModel` es estado puro (paso, zona, juicio de un ángulo), `SkillCheckView` sólo
  dibuja y `SkillCheckController` corre la secuencia con UniTask.
- **Modal que no pausa** (`PausesGame = false`, `ConsumesEscape = false`): el mundo sigue y ESC abre
  la pausa encima. La aguja y las esperas corren en tiempo escalado y **sólo mientras es el modal de
  arriba**, así que la pausa o la cinemática de explosión la congelan en vez de gastar una vuelta.
- **Look**: ventana Win95 como el panel de secuencia (perfil `UIStyle_SkillCheckCanvas`: CRT,
  transición de señal sólo en la ventana, superficie animada). El dial usa `UIRingArc` con los fades
  de alfa (`startAlpha/endAlpha/outerAlpha/innerAlpha`): estela de radar detrás de la aguja, zona que
  se apaga desde el perfect y un brillo de fósforo en el centro.
- **API**: `Open(data, completed => …)` devuelve `false` si ya estaba abierto; el callback llega
  cuando el overlay se cerró (`true` = completó, `false` = cancelado por `Cancel()`, fin de la run o
  sesión nueva). **Prueba: F6** (`SkillCheckTestKey`, sólo editor/dev).
- **Disparador en el mundo**: `SkillCheckPanelInteractable` con un `SO_SkillCheckPuzzleData` (puzzle id +
  secuencia). Al completar llama `PuzzleStateManager.SetPuzzleCompleted`, y el módulo cuyo
  `associatedPuzzleId` coincide (`M2_Chest` → `puzzle_central_piso2`) se resuelve. Cancelar no completa
  nada; el panel se puede volver a usar desde el primer check. Hoy sólo está colocado en `TestIñaki`.

### Interaction Prompt — ventana Win95 y tipos de mensaje

El prompt es una ventana chica al estilo Win95 (`Window` con `UIBevelFrame` Raised, barra de título, fondo
animado) con una línea de comando de fósforo adentro: `> TEXTO_`, en mayúsculas, tipeada con
`TMPTypewriterReveal` y con un cursor `_` que parpadea en rojo (`#CC1A1A`, el acento del tema).

Muestra **dos tipos** en el mismo slot, cada uno ligeramente distinto:

| Tipo | Título | Slot izquierdo | Entrada |
|---|---|---|---|
| Común (puertas, válvulas, paneles, notas) | `C:\WIRED\INTERACT.EXE` | keycap `E` | desde abajo |
| Ítem (recoger / insertar) | `C:\WIRED\ITEM.DAT` | keycap `E` + pozo Sunken con el ícono del ítem | desde abajo |

- El tipo lo declara el interactable con la interfaz **opcional** `IPromptPresentation` (`Kind` + `PromptIcon`).
  Hoy la implementan `PickupInteractable` y `SocketInteractable`; lo que no la implemente es Común.
- El prompt sólo describe lo que estás mirando. Lo que el juego dice sobre una interacción (una llave
  usada, un ítem que entró al inventario) va al feed de notificaciones de abajo: compartiendo este slot,
  el prompt de lo siguiente que mirabas lo pisaba al instante.
- El estado "info" (`GetInfoText`) conserva el tipo pero va en gris y sin tecla.
- **La barra de título NO está en el perfil de estilo**: la pinta la view según el tipo, y un `UIThemeApplier`
  la repintaría en `OnEnable`. El resto del prompt lo estila `UIStyle_InteractionCanvas.asset`.

### Notificaciones de interacción

`HUDCanvas.prefab` → `InteractionFeed` (`InteractionNotificationFeed`): filas cortas apiladas arriba a la
derecha (anclado en -24, -24). Entra una fila por:

- `InventoryEvents.OnItemAdded` → `+ HUB KEY`, tanto si se levantó del mundo como si la dio un puzzle.
- `InteractionEvents.RaiseGlobalMessage(texto, segundos)` → cualquier mensaje de interacción:
  `USED THE HUB KEY` (la puerta al consumir la llave), `X left behind: hands full` (recompensa que no entra).

Reglas:

- **Máximo 3 a la vez**: la cuarta saca a la más vieja; las de abajo suben a ocupar el hueco.
- Cada una se va sola a los segundos (los del mensaje; 3 s las de ítem). El tiempo corre en **tiempo
  escalado y sólo sin modales abiertos**: lo que llega con el panel de secuencia o el inventario abierto
  espera ahí y hace su entrada al cerrarse. Las animaciones van en unscaled.
- `ModalVisibilityGate` en el root la oculta bajo cualquier modal.
- Las filas se clonan de `RowTemplate` (inactivo): la consola del input hint con barra de acento a la
  izquierda. Para cambiar el look se edita el template en el prefab, no el código.
- Las alertas del Arquitecto (`HUDAlertView`, arriba al centro, de a una) NO pasan por acá.

### HUD del escondite: overlay y medidor de aliento

Dos nodos de `HUDCanvas.prefab`, el prefab es la fuente de verdad de su layout:

- **`HidingOverlay` (`HidingOverlayView`)**: lo que el player ve del escondite desde adentro, a
  pantalla completa y **debajo** del resto del HUD (viñetas, timer y medidor se dibujan encima). Un
  look por `EHidingSpotType`: rendijas de locker, la parte de abajo y las patas de una mesa, la juntura
  de luz de un contenedor. Es procedural: cada look es un `RawImage` con una textura de alfa de 320×180
  generada en `Awake`, teñida con `shade` (casi negro, nunca rojo: el rojo es peligro). Entra y sale con
  `HidingEvents.OnEntered` / `OnExited` (0.35 s / 0.25 s, en unscaled) y nunca toma clicks.
- **`BreathMeter` (`BreathHoldMeterView`)**: ventana abajo a la izquierda (anclada a un punto, en 24, 24)
  con 10 pips del aire que le queda al player (`PlayerStateManager.BreathAir`) y una línea de estado:
  `[F] HOLD BREATH` (con la tecla real de `GameInput.HoldBreath`), `HOLDING...` o `RECOVERING`. Por
  debajo de `lowAir` (0.3) los pips pasan a Accent. Se ve mientras el player está en un escondite
  (`CurrentHidingSpot`) y no está deshabilitado; como el timer, **pollea** `PlayerRegistry.Current` en
  `Update` porque el aire no tiene evento. Sólo dibuja: el estado escondido es dueño del aliento y del
  ruido. El root lleva un `ModalVisibilityGate` sobre su propio `CanvasGroup` y la view fadea el de la
  ventana, así no se pelean por el mismo alfa.

---

## 12. Referencias en código

Para entender un pattern específico, leer estos archivos como modelo:

- **Controller persistente con static Instance + IModalUI cuyo `PausesGame` depende de cómo se abrió**: `DocumentReaderController.cs` — la misma modal congela el juego en modo lectura y lo deja correr en lectura in situ. Las flags de `IModalUI` son propiedades, no constantes: el `UIStateManager` las relee en cada Push/Pop.
- **Controller con InjectDependencies + apertura por evento estático**: `WinController.cs`, `ResultScreenController.cs`.
- **Presentación por datos en vez de por subclase**: `ResultPresentation.cs` — un preset serializado por `GameState` en lugar de un controller por pantalla.
- **Model con snapshot/revert + PlayerPrefs**: `SettingsModel.cs`.
- **View con sub-views y re-emisión de eventos**: `SettingsView.cs`.
- **Vista permanentemente activa con CanvasGroup.alpha**: `InteractionPromptView.cs`.
- **HUD overlay que se congela con timeScale=0**: `VignetteChaseView.cs` (anima el alfa en su propio `Update` con `Time.deltaTime`; el `Fade()` del base es unscaled).
