# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**WIRED** — a Unity 6 (6000.x LTS) first-person survival horror game with a PSX aesthetic. Render pipeline: **URP 17.4.0**, Compatibility mode. All game scripts live under `Assets/Scritps/` (note the typo in the folder name — this is intentional and consistent throughout the project).

## Development

This is a Unity project. There are no CLI build commands. All compilation, scene editing, and testing happen inside the **Unity Editor**. Open the project by launching Unity Hub and selecting the repo root. The Editor auto-compiles on file save.

To run the game from a fresh state, open the `Bootstrap` scene and press Play. Do not press Play from an isolated scene unless you are intentionally testing that scene in isolation.

Additional documentation in `docs/`:
- `docs/UI-System.md` — UI architecture, MVC pattern, scene lifecycle, pause system
- `docs/Materials-System.md` — shaders, vision fog, item highlight, flicker scripts
- `docs/TODO-UI.md` — deferred UI work (inventory, save slots, settings tabs, puzzles)
- `docs/SaveSlots-Setup.md` — manual Unity Editor steps to wire up the SaveSlots screen

## Architecture

### Additive Scene Loading

Navigation between screens is done by loading and unloading **groups of scenes additively**, never by a single scene swap. The system has three pieces:

- **`SO_SceneList`** (ScriptableObject) — maps string labels (`"Menu"`, `"Level1_Group"`, `"UI_SaveSlots"`) to lists of scene names, and declares which scenes are **persistent** (never unloaded).
- **`ScreenEventChannel`** (ScriptableObject) — exposes `RaisePushScreen(label)`, `RaisePopScreen()`, `RaiseClearAll()`.
- **`ScreenManager`** (`Scritps/Managers/ScreenManager.cs`) — singleton that listens to the channel and performs async load/unload via UniTask.

**Persistent scenes** (`Bootstrap`, `Data`, `LevelUI`, `UI_Settings`, etc.) are loaded at boot by `BootingSceneLoader` and live for the entire session. Their singletons are always accessible. **Pushable scenes** (`Menu`, `Level1_Group`, `UI_SaveSlots`, etc.) are loaded on demand; managers in them die when unloaded. Cross-scene references must use static events or ScriptableObject channels — Unity breaks serialized cross-scene references.

### UI: MVC + UIStateManager

Every screen follows MVC:
- **Model** (`BaseScreenModel`) — plain C# state (no MonoBehaviour), with `Initialize()`, `IsInitialized`, and `OnDataChanged`.
- **View** (`BaseScreenView`) — wraps a `CanvasGroup`; exposes `ShowAsync()`/`HideAsync()` that use `Time.unscaledDeltaTime` so they work during pause (`Time.timeScale = 0`). Never call `SetActive` directly on UI GameObjects — always use these methods.
- **Controller** (`BaseScreenController<TView, TModel>`) — orchestrates. Overrides `OnBeforeOpen`, `OnAfterOpen`, `OnBeforeClose`, `OnAfterClose`.

**Modal UIs** (Inventory, Settings, SequencePanel, DocumentReader, Pause) live in persistent scenes and implement `IModalUI` (`Scritps/Interfaces/IModalUI/IModalUI.cs`). They must call `UIStateManager.Instance.Push(this)` on open and `UIStateManager.Instance.Pop(this)` on close. The `UIStateManager` is the single authority over `Time.timeScale` and `Cursor` state while any modal is open — individual controllers must not manipulate these directly.

`IModalUI` requires:
- `ModalId` — unique string for deduplication logging.
- `ConsumesEscape` — if `true`, the `UI/Exit` input action calls `RequestClose()` on this modal; if `false`, ESC passes through to the PauseManager.
- `BlocksPause` — if `true`, the Pause menu cannot open on top of this modal.
- `RequestClose()` — called externally to request closure.

### FSM (Player and Nemesis)

Both the player and the Nemesis AI use the same generic FSM base:

- **`StateManager<EState>`** (`Scritps/FSM/StateManager.cs`) — `MonoBehaviour` that owns a `Dictionary<EState, BaseState<EState>>`, drives `Update`/`TransitionToState`, and forwards `OnTriggerEnter/Stay/Exit` to the active state.
- **`BaseState<EState>`** (`Scritps/FSM/BaseState.cs`) — abstract class with `EnterState`, `ExitState`, `UpdateState`, `GetNextState`, and trigger callbacks.

**Nemesis states**: `Patrolling -> Investigating -> Chasing -> Searching` (managed by `NemesisStateManager`). Detection uses `FieldOfView.cs` (cone + obstacle raycast, polled every 0.2s). State transitions fire `NemesisEvents.OnChaseStarted/Ended` and `NemesisEvents.OnProximityChanged`, which drive `VignetteChaseView` and `VignetteProximityView` in the HUD.

**Player states**: `Idle, Moving, Crouching, Hidden, Interacting (BoxInteracting), Disabled` (managed by `PlayerStateManager`).

### Singletons

`Singleton<T>` (`Scritps/SingletonCreator/Singleton.cs`) is the base for global managers. Call `CreateSingleton(dontDestroyOnLoad)` in `Awake`. Use `Singleton<T>.Exists` before accessing `Instance` from contexts where the singleton might not be initialized.

Persistent UI controllers that live in persistent scenes (e.g., `SettingsController`, `InventoryManagerUI`) use a plain `public static T Instance { get; private set; }` set in `Awake` — they do not need `DontDestroyOnLoad` because the scene already ensures one instance.

### Static Event Bus

Communication between systems in different scenes uses **static C# events**. Key events:

| Event | Dispatcher | Consumers |
|---|---|---|
| `PauseManager.OnPauseStateChanged` | PauseManager | PauseManagerUI |
| `GameResultManager.OnGameResult` | GameResultManager | WinController, LoseController |
| `SettingsModel.OnSettingsApplied` | SettingsModel | CameraSensitivityApplier |
| `NemesisEvents.OnChaseStarted/Ended` | NemesisStateManager | VignetteChaseView |
| `NemesisEvents.OnProximityChanged` | NemesisStateManager | VignetteProximityView |
| `InteractionEvents.OnTargetChanged` | InteractionManager | InteractionPromptView |
| `InventoryEvents.OnItemAdded/Removed/Consumed` | InventoryManager | InteractionPromptView, ModuleHUDView |
| `UIStateManager.OnModalPushed/Popped` | UIStateManager | (subscribers as needed) |

**Subscribe in `Awake`, unsubscribe in `OnDestroy`** — never in `OnEnable/OnDisable` for static events, as the delegate outlives the GameObject's enabled state.

### Gameplay Input Guard

```csharp
// In any Update() that reads gameplay input:
if (PauseManager.IsGameplayInputBlocked) return;
```

`IsGameplayInputBlocked` is `true` when the game is paused OR when any `IModalUI` is open (`UIStateManager.IsAnyModalOpen`). `Time.timeScale = 0` stops physics but does NOT stop `Input.GetKey*` — the guard is required for all logical input.

### Interactable System

`IInteractable` (`Scritps/Interfaces/IInteractable/IInteractable.cs`) defines `CanInteract()`, `Interact()`, `IsRepeatable()`, `GetInteractText()`, `GetInfoText()`. `BaseRangeInteractable` implements trigger-based registration with `InteractionManager`. The manager detects the nearest visible interactable each frame and fires `InteractionEvents.TargetChanged(interactable)` when the target changes. Key `[E]` is processed in `InteractionManager.Interact()` with a 0.2s cooldown.

### Visual Systems

**Vision Fog**: Fullscreen Shader Graph pass (`VisionFog.mat`) driven by `VisionRangeController.cs`. Sets `_PlayerPos`, `_VisionStart`, `_VisionEnd` as shader globals. Range lerps between `visionEndDark` (6m) and `visionEndLit` (25m) based on `RenderSettings.ambientLight` luminance. Guard: if `visionEnd <= visionStart`, the shader passes through unchanged — prevents a black screen when the controller is inactive.

**Item Highlight**: `ItemProximityHighlight.cs` uses `MaterialPropertyBlock` to lerp `_TintIntensity` (0.15 to 0.4) and `_EmissionIntensity` (0.0 to 0.2) over 0.3s with a SmoothStep curve when the player enters/exits range. Four preset materials by category: `mat_item_keys`, `mat_item_components`, `mat_item_clues`, `mat_item_special`.

**Color spec rules** (`color_visual_language_spec.docx` in Downloads): `#CC1A1A` red is exclusive to danger/emergency lights. `#FFC850` amber is exclusive to the player device. No outlines or waypoints — items are distinguished only by tint and emission.

**Renderer Feature order** (`PC_Renderer.asset`): SSAO then Vision Fog (BeforeRenderingPostProcessing) then PS1Effect (BeforeRenderingPostProcessing). Fog must precede PS1 so world-space coherence is preserved before the pixelation pass.

### ScriptableObjects

Data lives in `Assets/ScriptableObjects/`. Key types in `Scritps/ScriptableScripts/`:
- `SO_InventoryItem` — item data (ItemID, ItemName, Category, IsConsumable, IsMetallic, parameters).
- `SO_SceneList` / `ScreenEventChannel` — scene navigation.
- `SO_NemesisData` / `SO_NemesisMovement` — Nemesis tuning.
- `SO_Movement` / `SO_CameraConfig` — player tuning.
- `SO_SaveSlotData` / `SO_SaveSlotDatabase` — save slot stubs.
- Puzzle data: `SO_SequencePuzzleData`, `SO_ContainerPuzzleData`, `SO_ValvePuzzleData`, `SO_HubPuzzleData`.

### Async

Async operations (scene load/unload, UI transitions) use **UniTask** (`Cysharp.Threading.Tasks`). Use `UniTask.WhenAll` for parallel loads. Use `.Forget()` on fire-and-forget calls. For timers that run during pause, use `UniTask.Delay(ms, DelayType.UnscaledDeltaTime)`.

## Key Conventions

**Adding a modal UI**: implement `IModalUI`, call `UIStateManager.Instance.Push(this)` on open and `Pop(this)` on close. Do not touch `Time.timeScale` or `Cursor` — the `UIStateManager` owns those.

**Adding a pushable screen**: create Model/View/Controller inheriting the base classes, create a scene, add it to `SO_SceneList` under a group label, add it to Build Settings. Invoke via `screenChannel.RaisePushScreen("label")`.

**GameResultManager**: call `GameResultManager.ResetSession()` at the start of each gameplay session, otherwise a second Win/Lose cannot be reported (static `_resultReported` guard).

**Settings placeholders**: `SettingsModel` has fields for Brightness, Contrast, Gamma, CRT scanlines, Resolution, VSync, and Invert Y already wired to PlayerPrefs, but no system reads them yet. Connect them without changing the model shape.
