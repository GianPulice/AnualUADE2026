# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**WIRED** — a Unity 6 (6000.x LTS) first-person survival horror game with a PSX aesthetic. Render pipeline: **URP 17.4.0**, Compatibility mode. All game scripts live under `Assets/Scritps/` (note the typo in the folder name — this is intentional and consistent throughout the project).

## Language rule (hard requirement)

**All code is written in English.** This covers, without exception:

- Comments — inline `//`, block `/* */`, and XML doc comments (`/// <summary>`).
- String literals — `Debug.Log`/`LogWarning`/`LogError` messages, `[Header]` and `[Tooltip]`
  attributes, `[CreateAssetMenu]` and `[ContextMenu]` labels, and **player-facing text**
  (interaction prompts, UI labels, save-slot descriptions).
- Identifiers — class, method, field and local names.

The whole of `Assets/Scritps/` was migrated to English. Do not reintroduce Spanish in code,
including in new files. Team-facing documentation under `docs/` stays in Spanish (except this
file) — the rule applies to code, not to docs.

Files must be saved as **UTF-8**. A previous non-UTF-8 save corrupted accented characters
across the codebase (`V�lvula`, `M�dulos`); the English migration removed those, but check
your editor's encoding before committing.

## Development

This is a Unity project. There are no CLI build commands. All compilation, scene editing, and testing happen inside the **Unity Editor**. Open the project by launching Unity Hub and selecting the repo root. The Editor auto-compiles on file save.

To run the game from a fresh state, open the `Bootstrap` scene and press Play. Do not press Play from an isolated scene unless you are intentionally testing that scene in isolation.

Additional documentation in `docs/`:
- `docs/UI-System.md` — UI architecture, MVC pattern, scene lifecycle, pause system
- `docs/Ambience-System.md` — ambient audio: the four layers, mixer setup, zone profiles, verification
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

**Nemesis states**: `Patrolling -> Investigating -> Chasing -> Searching`, plus `Traversing` and the terminal `Catch` (managed by `NemesisStateManager`). `Traversing` is entered from `Chasing` when reaching the player means changing floor by freight elevator; it holds that decision open for `SO_NemesisData.elevatorCommitTime` even with the player out of sight, because a floor slab breaks line of sight for the whole trip and without it the lift ride was abandoned every time. Detection uses `FieldOfView.cs` (cone + obstacle raycast, polled every 0.1s) and `FieldOfListening.cs`, which occludes sight and sound with *different* masks — a floor blocks sight but only attenuates sound, and that is the Nemesis's only channel to the storey above. Route questions ("reachable? which floor? is the lift on the way?") go through `NemesisPathOracle`, which throttles them; that interval is a stability knob as much as a cost one, since a verdict flipping frame to frame makes the FSM oscillate. `NemesisTelemetry` fires `NemesisEvents.OnChaseStarted/Ended` when entering/leaving the `{Chasing, Catch}` set — `Traversing` is deliberately NOT in it, since the player is a storey away and unreachable — and `OnProximityChanged` every frame from the real distance to the player (`SO_NemesisData.proximityRadius`). Both drive `VignetteChaseView` and `VignetteProximityView` in the HUD. Entering `Catch` also schedules `GameResultManager.ReportLoss` after `captureDelay`.

**`NemesisStateManager` is a facade, not an implementation.** It owns the FSM and the shared references; everything else lives in sibling components on the same GameObject, all auto-added when missing so no existing prefab needs re-saving: `NemesisPathOracle` (throttled route queries), `NemesisTelemetry` (the events above), `NemesisStuckEscape` (no-progress watchdog and its warp out), `NemesisLifecycle` (dormancy, agent tuning from `SO_NemesisMovement`, and every teleport). The states keep calling `NemesisStateManager`, which forwards — that is what the facade is for. Teleports must go through `NemesisStateManager.WarpTo`, which invalidates the cached route verdict and resets the stuck sample; a warp that skips either leaves the FSM steering from the floor it just left, or the watchdog reading the jump as ground covered on foot.

Adding a state to `ENemesisState` has two non-obvious consequences: `NemesisAudio.stateLoops` is a designer-authored array, so a state with no entry crossfades the monster to **silence**, and `NemesisStateManager.IsNavigatingState()` decides whether the stuck watchdog runs in it.

**Player states**: `Idle, Moving, Crouching, Hidden, Interacting (BoxInteracting), Disabled` (managed by `PlayerStateManager`).

### Singletons

`Singleton<T>` (`Scritps/SingletonCreator/Singleton.cs`) is the base for global managers. Call `CreateSingleton(dontDestroyOnLoad)` in `Awake`. Use `Singleton<T>.Exists` before accessing `Instance` from contexts where the singleton might not be initialized.

Persistent UI controllers that live in persistent scenes (e.g., `SettingsController`, `InventoryManagerUI`) use a plain `public static T Instance { get; private set; }` set in `Awake` — they do not need `DontDestroyOnLoad` because the scene already ensures one instance.

### Static Event Bus

Communication between systems in different scenes uses **static C# events**. Key events:

| Event | Dispatcher | Consumers |
|---|---|---|
| `PauseManager.OnPauseStateChanged` | PauseManager | PauseManagerUI |
| `GameResultManager.OnGameResult` | GameResultManager | WinController, ResultScreenController |
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

`IInteractable` (`Scritps/Interfaces/IInteractable/IInteractable.cs`) defines `CanInteract()`, `Interact()`, `IsRepeatable()`, `GetInteractText()`, `GetInfoText()`.

Detection is a **camera SphereCast**, not trigger registration: `InteractionManager.RaycastForInteractable()` casts from `Camera.main` forward with `SO_InteractionManager.InteractionDistance`, a 0.1 radius, against `InteractableLayers | BlockingLayers`. It resolves the `IInteractable` on the hit collider or its parents; if the first hit has none, it is a wall and nothing is targeted. `BaseRangeInteractable` no longer registers anything — it only describes *what* the interaction is. Each interactable needs a Collider on itself or on a child in the Interactable layer so the cast has something to hit.

The manager fires `InteractionEvents.TargetChanged(interactable)` when the target changes. Key `[E]` is processed in `InteractionManager.Interact()` with a 0.2s cooldown.

Selection is by **first hit along the ray**, with no dot-product priority when several interactables overlap — see `docs/TODO-UI.md` · Interaction Prompt.

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

**Settings appliers**: `SettingsModel` persists every field to PlayerPrefs and raises `OnSettingsApplied`. The appliers that consume those keys already exist and are wired:

| Keys | Applier |
|---|---|
| `Settings_Sensitivity`, `Settings_InvertYAxis` | `CameraSensitivityApplier` (on the camera rig) |
| `Settings_Brightness`, `Settings_Contrast`, `Settings_Gamma` | `PostProcessSettingsApplier` (on the URP global Volume) |
| `Settings_CRTScanlines`, `Settings_PSXDithering` | `PS1EffectApplier` (holds `PS1Effect.mat`) |
| `Settings_ResolutionIndex`, `Settings_WindowMode`, `Settings_FPSLimit`, `Settings_VSync` | `ScreenSettingsApplier` (persistent GameObject) |
| `Settings_AudioInBackground` | `AudioBackgroundApplier` (persistent GameObject) |
| `Settings_LowFreqAmbience` | `AmbienceComfortApplier` (persistent GameObject) — no UI yet, see below |
| `Settings_MasterVolume`, `Settings_MusicVolume`, `Settings_SFXVolume` | `AudioManager` (applied live by the setters, not on Apply) |

Still unconnected: keybind rebinding (`SettingsPanelControlsView` shows static labels), `Settings_VHSGlitch` (read by `GlitchController` but not exposed in the Options UI yet), and `Settings_LowFreqAmbience` (persisted by `SettingsModel` and applied by `AmbienceComfortApplier`, but with no toggle in the Options panel — use the `Toggle Low-Freq Ambience` context menu on `AmbienceDriftLayer` meanwhile).

### Ambience (`Scritps/Ambience/`)

Four constant layers plus randomised 3D one-shots, driven by `AmbienceController` in the **gameplay** scene (not `Data` — same choice as `VisionRangeController`).

- **`AmbienceController`** — owns a push/pop stack of `SO_AmbienceProfile`, resolves the mixer routing once and pushes it into each layer (the layers do nothing in their own `Start`). `AmbienceZone` trigger volumes push and pop profiles; innermost wins, exactly like `LightZone` + `VisionRangeController`.
- **`AmbienceBedLayer`** — Layer 1, the factory bed. N crossfade slots so a profile can run **two loops of coprime length** (37 s + 53 s gives a composite period of ~33 min, which is what actually defeats loop detection).
- **`AmbienceDriftLayer`** — Layers 3 and 4 collapsed into one data-driven component: pink noise plus the 17 Hz and 32 Hz drones, each slowly wandering to a new volume target. Never restarted on a zone change; profiles only retarget scales.
- **`AmbienceEventScheduler` / `AmbienceEventPool` / `AmbiencePlacementResolver` / `AmbienceEmitter`** — Layer 2. Weighted tiers, a soft repetition penalty, and hybrid placement: LD-placed anchors preferred, validated random as fallback (`CheckSphere` + `NavMesh.SamplePosition` + `Linecast`, with occluded points snapped to the blocking surface).

**Two rules this system depends on.** Never call `mixer.SetFloat` for anything under Ambience — `AudioManager.SetGameplaySfxBundle` rewrites `AmbienceVolume` whenever the player touches the SFX slider, so per-layer balance lives in the fixed faders of the `Ambience/{Bed,Events,Texture,Sub}` sub-groups and in `AudioSource.volume`. And **never put a limiter or compressor on `Master`**: the inaudible 17 Hz drone is still a large peak signal and would duck the entire mix at its LFO rate.

Volume envelopes use `Time.unscaledDeltaTime` (a fade frozen mid-way by a modal is audible); the event timer uses scaled `Time.deltaTime` plus an `IsPaused` guard (a frozen timer is not). Run `Tools/Audio/Bake Ambience Texture Clips` to generate the noise, drones and placeholders — Layers 3 and 4 need no sourced audio at all.

## Current state — what is and is not wired

The systems below are **implemented but not connected to anything**. Read this before assuming a feature works end to end.

- **Module timers never start.** `InventoryManagerUI.StartModuleTimer()` and `ResolveModule()` have zero callers, so `ModuleData.Status` stays `Inactive` and `TickModuleTimers` is a no-op. Nothing downstream fires: no `ModuleExploded`, no `BlindnessOverlayView`, no `CheckGameOver` → `ReportGameOver`.
- **There is no win condition.** `GameResultManager.ReportWin` is only called from `WinLoseTest.cs` (debug key `I`). The only reachable ending is the Nemesis catching you.
- **`PuzzleController.CompletePuzzle()` and `PuzzleReward.GiveReward()` have zero callers.** Only `SequencePanelInteractable` (SP1) completes itself, writing straight to `PuzzleStateManager` and bypassing `PuzzleController`.
- **`SkillCheckController.Open()` has zero callers**, `OnFailed` is never invoked, and the model has no fail-out path.
- **`HubPuzzleController.CheckHubCompletion()` sets a flag and stops** — the cinematic / Floor 3 unlock is a TODO comment.
- **Doors animate open but stay solid**: `DoorInteractable.DisableBlockingCollider()` is commented out in both call sites.
- **Audio is nearly silent.** The only gameplay sound routed through `AudioManager` is `PlaySFX("PickUpInteractable")` — no footsteps, music, Nemesis or UI audio. The **ambience system is built** (`Scritps/Ambience/`, see below) but ships with placeholder clips and needs its scene wiring done.
- **Save/load is a stub.** `SaveSlotsController` logs and raises an event; `InventoryManager.RestoreFromIDs` has no callers; `PuzzleStateManager` has no serialization at all.
- **Retry does not reset run state.** `InventoryManager` and `PuzzleStateManager` are `DontDestroyOnLoad` with no `Clear()`, and `GameResultManager.ResetSession()` only resets the result flag — a retry keeps every collected item and completed puzzle.
- **`EPlayerState.InDanger`** is in the enum but never registered in the state dictionary; transitioning to it would throw `KeyNotFound`. `PlayerHiddenState` is inert (no collider/visibility change) and is toggled by debug keys `R`/`T`/`Y` still live in `PlayerStateManager.InputUpdate`.
- **Nemesis `OnTrigger*` throws.** Patrol, Investigating and Searching states `throw new NotImplementedException()` in their trigger overrides, and `StateManager` forwards trigger callbacks to the active state.
- **Most of `SO_NemesisMovement` is unused** — only `PatrolSpeed` and `ChaseSpeed` are ever assigned to the NavMeshAgent. `SO_NemesisData.InvestigationTimeOut` and `NoiseUpdateCooldown` are never read.
- **Two parallel unfinished grab/push implementations**: `GrabbableBall` + `PushBoxTriggerLogic` (active) and `PushableBall` (its `FixedUpdate` is entirely commented out). `GrabbableBall` reads `KeyCode.E` with no `PauseManager.IsGameplayInputBlocked` guard.
- **Debug scripts still ship in the game folder**: `WinLoseTest.cs`, `TestClick.cs`, `Editor/TestSceneBuilder.cs`.
