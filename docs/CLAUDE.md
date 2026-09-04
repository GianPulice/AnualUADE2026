# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**WIRED** — a Unity 6 (6000.x LTS) first-person survival horror game with a PSX aesthetic. Render pipeline: **URP 17.4.0**, Compatibility mode. All game scripts live under `Assets/_Project/Scripts/` (formerly `Assets/Scritps/`, with the typo — renamed during the asset reorganization).

## Assets layout

`Assets/` has exactly five top-level entries, and new content must go into one of them:

- **`_Project/`** — everything the team authors. `Art/` (`Animations`, `Fonts`, `Materials`,
  `Models`, `Textures`), `Audio/`, `Input/`, `Prefabs/`, `Scenes/`, `ScriptableObjects/`,
  `Scripts/`, `Settings/`. The leading underscore keeps it pinned at the top of the Project window.
- **`ThirdParty/`** — imported packs, each kept exactly as it shipped so a future re-import
  from the Asset Store overwrites cleanly. **Never edit or reorganize a pack in place.** If you
  need a variant of a pack asset, copy it into `_Project/` and change the copy.
- **`_Archive/`** — kept but not part of the game: the Unity URP template leftovers
  (`UnityTemplate/`), old screenshots, and a recovery scene. Nothing here should be referenced
  by a shipping scene.
- **`Resources/`** and **`TextMesh Pro/`** — Unity resolves both by folder name; do not move them.

Scenes live in `_Project/Scenes/` under `Bootstrapper/`, `Data/`, `GameScenes/`, `UI/`, and
`Dev/` (test and sandbox scenes). Editor tooling that hardcodes an `"Assets/..."` string —
`NemesisTestSceneBuilder`, `PlayModeStartSceneSetter`, `AmbienceToneBaker`, `AudioMixerSetup`,
`SO_PostProcessToggle` — must be updated whenever one of these folders moves; asset **references**
survive a move on their own because Unity resolves them by GUID, but **path strings do not**.

## Language rule (hard requirement)

**All code is written in English.** This covers, without exception:

- Comments — inline `//`, block `/* */`, and XML doc comments (`/// <summary>`).
- String literals — `Debug.Log`/`LogWarning`/`LogError` messages, `[Header]` and `[Tooltip]`
  attributes, `[CreateAssetMenu]` and `[ContextMenu]` labels, and **player-facing text**
  (interaction prompts, UI labels, save-slot descriptions).
- Identifiers — class, method, field and local names.

The whole of `Assets/_Project/Scripts/` was migrated to English. Do not reintroduce Spanish in code,
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

## Design specs

Four GDD documents define the intended scope of the systems below. They are team documents (Docs /
Drive), not files in this repo. They were written at different moments against different states of
the code, so read them with one rule: **where a spec and this file disagree about how something
works *today*, this file wins; where they disagree about what the feature is *meant to be*, the
spec wins.**

| Spec | Version | Where it stands in code |
|---|---|---|
| Inventory System | v2.0 | Built. Gaps: the audio player and the item catalogue — see *Inventory* |
| Nemesis System | v1.0 draft | Built, and well past the spec. The spec is the document that is behind — see *Spec deltas* below |
| Hiding System | v1.0 draft | **Not built.** `EPlayerState.Hidden` is an inert stub — see *Hiding spots* |
| Obstacle System | v1.0 draft | **Not built.** Nothing in the project climbs, vaults, pushes or clears — see *Environmental obstacles* |

### Spec vocabulary that does not exist in this codebase

The three newer specs are written in a generic Unity idiom and name APIs this project never had.
Implemented literally they compile against nothing, or worse, they get re-implemented alongside the
real system and the two disagree. The mapping:

| The spec says | This project actually has |
|---|---|
| `PlayerController` | `PlayerStateManager` (`_Project/Scripts/Player/Player FSM/`) |
| `OnNoiseGenerated(origin, radius)` | **No noise event exists.** See *Noise is a sphere, not an event* |
| `SetHidden(bool)` | `PlayerStateManager.IsHidden`, a plain settable bool, today toggled only by the `R` debug key |
| `SetTrapped(bool)` / "block all input" | `IsDisabled` → `EPlayerState.Disabled`, or `IsInteracting` → `Interacting`. There is no `SetTrapped` |
| `SetSpeedMultiplier(float)` | `PlayerStateManager.SpeedMultiplier`, written by the states. `EffectiveMoveSpeed` is the penalty-scaled base it multiplies |
| `CharacterController`, capsule height 0.6, step offset | Rigidbody + `CapsuleCollider`; stance heights are `SO_Movement.StandingHeight` / `CrouchHeight`, and standing up is gated by `HasHeadroomToStand()` |
| `HidingData` / `ObstacleData` SOs | Do not exist. Create them under `ScriptableScripts/` — tunables belong in an asset, not on the component |
| `NemesisController.Activate()` | `NemesisStateManager.Activate()`, gated on `NemesisController.activatedByPuzzleId` |
| `NemesisController.SetDifficultyLevel(n)` | **Does not exist.** The per-module escalation table (Nemesis spec §7.2) is unimplemented |
| `visionRange` / `hearingRange` / `proximityDetectionRange` | `SO_NemesisData.ViewRange` / `ListenRange` / `ProximityDetectionRange` — plus `FocusAngle` and a peripheral awareness band the spec predates |
| "state X transitions to Y" | States never decide transitions. `NemesisDecision` + `SO_NemesisPriorities` do — see *Nemesis: the decision layer* |
| "the Hub blocks the Nemesis" (in code) | A NavMesh `Not Walkable` modifier volume. There is no C# side — see *Safe zones* |
| `ModuleManager.GetActiveModuleTimeRemaining()` / `GetActiveModuleTotalTime()` | `GetActiveModule()` returns the `ModuleRuntime`; it already exposes `TimeRemaining`, `TimerProgress` (the bar fill the spec computes by hand), `FormattedTime` and `BarColor`. The total is `Data.TimerDuration`. `GetExplodedCount()` exists exactly as specified |

### Noise is a sphere, not an event

Every spec that talks about noise — hiding (breathing, exhaling), obstacles (scraping a shelf,
crossing rubble) — assumes a fire-and-forget event the Nemesis subscribes to. **That is not how
this project hears.**

The player carries a `SphereCollider` (`PlayerStateManager.AudioEmitingZone`) on the listen mask.
The movement states set its radius per gait from `SO_Movement` (crouch 1 / walk 2 / run 6) and
`PlayerIdleState` switches the whole GameObject **off**, which is why standing still is silent.
`FieldOfListening` sweeps every `listenDelay` (0.1 s), reads the emitter's real radius off the
collider it caught, and scales it by `SO_NemesisData.NoiseRangeScale` before attenuating through
walls (`WallOcclusionMultiplier`) and floors (`FloorOcclusionMultiplier`).

Three consequences for anything that wants to "make a noise":

1. **A noise is a duration, not an instant.** Enable the emitter at the radius you want and leave
   it on for longer than one sweep — anything under 0.1 s can fall between two sweeps and be heard
   by nobody. A single-frame pulse is a coin flip.
2. **Restore what you changed.** The emitter is shared with the movement states. A radius or an
   active flag left behind makes the player permanently loud or permanently deaf-to-the-monster for
   the rest of the run, and nothing errors.
3. **The number in the spec is not metres of audibility.** It is the emitter radius, before
   `NoiseRangeScale` and before occlusion. Tune against `NemesisGizmos`, which draws the three gait
   radii to scale, rather than against the spec table.

Adding a real `OnNoiseGenerated` event is a legitimate design change, but it is a change to the
Nemesis's hearing model and has to replace the sphere, not sit beside it. Two sources of truth for
"how loud is the player" is the same failure the project already paid for with layer masks.

### Spec deltas — Nemesis

The Nemesis spec v1.0 is the oldest of the four and the code has moved past it. Things it describes
that are **no longer true**: transitions living inside states, a single vision cone, `Vector3`
distance checks, and detection being all-or-nothing. Things it asks for that are **still missing**:

- **Difficulty escalation per module (§7.2).** No `SetDifficultyLevel`, no runtime SO copy. The
  comments in `SO_NemesisData` about "Tier 3.3 hands this a scaled copy" describe the intended
  mechanism (`ScriptableObject.Instantiate`, never write the asset), and `FieldOfListening.SetData`
  is already the seam for it.
- **A capture cinematic (§5).** `NemesisCatchState` plays out phases and `CaptureFadeView` fades;
  there is no cinematic. Everything else in the capture chain is wired.
- **`underTableVisionMultiplier` (hiding spec §3).** No field, no reader — see *Hiding spots*.

## Architecture

### Additive Scene Loading

Navigation between screens is done by loading and unloading **groups of scenes additively**, never by a single scene swap. The system has three pieces:

- **`SO_SceneList`** (ScriptableObject) — maps string labels (`"Menu"`, `"Level1_Group"`, `"UI_SaveSlots"`) to lists of scene names, and declares which scenes are **persistent** (never unloaded).
- **`ScreenEventChannel`** (ScriptableObject) — exposes `RaisePushScreen(label)`, `RaisePopScreen()`, `RaiseClearAll()`.
- **`ScreenManager`** (`_Project/Scripts/Managers/ScreenManager.cs`) — singleton that listens to the channel and performs async load/unload via UniTask.

**Persistent scenes** (`Bootstrap`, `Data`, `LevelUI`, `UI_Settings`, etc.) are loaded at boot by `BootingSceneLoader` and live for the entire session. Their singletons are always accessible. **Pushable scenes** (`Menu`, `Level1_Group`, `UI_SaveSlots`, etc.) are loaded on demand; managers in them die when unloaded. Cross-scene references must use static events or ScriptableObject channels — Unity breaks serialized cross-scene references.

### UI: MVC + UIStateManager

Every screen follows MVC:
- **Model** (`BaseScreenModel`) — plain C# state (no MonoBehaviour), with `Initialize()`, `IsInitialized`, and `OnDataChanged`.
- **View** (`BaseScreenView`) — wraps a `CanvasGroup`; exposes `ShowAsync()`/`HideAsync()` that use `Time.unscaledDeltaTime` so they work during pause (`Time.timeScale = 0`). Never call `SetActive` directly on UI GameObjects — always use these methods.
- **Controller** (`BaseScreenController<TView, TModel>`) — orchestrates. Overrides `OnBeforeOpen`, `OnAfterOpen`, `OnBeforeClose`, `OnAfterClose`.

**Modal UIs** (Inventory, Settings, SequencePanel, DocumentReader, Pause) live in persistent scenes and implement `IModalUI` (`_Project/Scripts/Interfaces/IModalUI/IModalUI.cs`). They must call `UIStateManager.Instance.Push(this)` on open and `UIStateManager.Instance.Pop(this)` on close. The `UIStateManager` is the single authority over `Time.timeScale` and `Cursor` state while any modal is open — individual controllers must not manipulate these directly.

`IModalUI` requires:
- `ModalId` — unique string for deduplication logging.
- `ConsumesEscape` — if `true`, the `UI/Exit` input action calls `RequestClose()` on this modal; if `false`, ESC passes through to the PauseManager.
- `BlocksPause` — if `true`, the Pause menu cannot open on top of this modal.
- `RequestClose()` — called externally to request closure.

### FSM (Player and Nemesis)

Both the player and the Nemesis AI use the same generic FSM base:

- **`StateManager<EState>`** (`_Project/Scripts/FSM/StateManager.cs`) — `MonoBehaviour` that owns a `Dictionary<EState, BaseState<EState>>`, drives `Update`/`TransitionToState`, and forwards `OnTriggerEnter/Stay/Exit` to the active state.
- **`BaseState<EState>`** (`_Project/Scripts/FSM/BaseState.cs`) — abstract class with `EnterState`, `ExitState`, `UpdateState`, `GetNextState`, and trigger callbacks.

**Nemesis states**: `Patrolling -> Investigating -> Chasing -> Searching`, plus `Traversing` and the terminal `Catch` (managed by `NemesisStateManager`). `Traversing` means "getting there needs the freight elevator"; it holds that decision open for `SO_NemesisData.ElevatorCommitTime` even with the player out of sight, because a floor slab breaks line of sight for the whole trip and without it the lift ride was abandoned every time. **Which state the Nemesis is in is not decided by the states themselves** — see *Nemesis: the decision layer* below. Detection uses `FieldOfView.cs` (cone + obstacle raycast, polled every 0.1s) and `FieldOfListening.cs`, which occludes sight and sound with *different* masks — a floor blocks sight but only attenuates sound, and that is the Nemesis's only channel to the storey above. Route questions ("reachable? which floor? is the lift on the way?") go through `NemesisPathOracle`, which throttles them; that interval is a stability knob as much as a cost one, since a verdict flipping frame to frame makes the FSM oscillate. `NemesisTelemetry` fires `NemesisEvents.OnChaseStarted/Ended` when entering/leaving the `{Chasing, Catch}` set — `Traversing` is deliberately NOT in it, since the player is a storey away and unreachable — and `OnProximityChanged` every frame from the real distance to the player (`SO_NemesisData.proximityRadius`). Both drive `VignetteChaseView` and `VignetteProximityView` in the HUD. Entering `Catch` also schedules `GameResultManager.ReportLoss` after `captureDelay`.

**`NemesisStateManager` is a facade, not an implementation.** It owns the FSM and the shared references; everything else lives in sibling components on the same GameObject, all auto-added when missing so no existing prefab needs re-saving: `NemesisPathOracle` (throttled route queries), `NemesisTelemetry` (the events above), `NemesisStuckEscape` (no-progress watchdog and its warp out), `NemesisLifecycle` (dormancy, agent tuning from `SO_NemesisMovement`, and every teleport), `NemesisLookAround` (sweeps the gaze while standing still). `NemesisElevatorUser` is resolved with `GetComponent` but deliberately **not** auto-added: unlike the others it is a real feature with scene wiring behind it, and a level with no freight elevator should not silently grow one. The states keep calling `NemesisStateManager`, which forwards — that is what the facade is for. Teleports must go through `NemesisStateManager.WarpTo`, which invalidates the cached route verdict and resets the stuck sample; a warp that skips either leaves the FSM steering from the floor it just left, or the watchdog reading the jump as ground covered on foot.

Adding a state to `ENemesisState` has three non-obvious consequences: `NemesisAudio.stateLoops` is a designer-authored array, so a state with no entry crossfades the monster to **silence**; `NemesisStateManager.IsNavigatingState()` decides whether the stuck watchdog runs in it; and no rung of the priority ladder will ever ask for it until you add one, so it is unreachable by default. **Append the new value at the end of the enum** — `SO_NemesisPriorities.asset` stores every rung's target as an integer, so inserting in the middle silently rewrites the designer's whole ladder into a different one.

**Player states**: `Idle, Moving, Crouching, Hidden, Interacting (BoxInteracting), Disabled` (managed by `PlayerStateManager`). `Hidden` is registered and reachable but **inert** — it is the hook the hiding system will hang off, not a working state; see *Hiding spots*.

### Singletons

`Singleton<T>` (`_Project/Scripts/SingletonCreator/Singleton.cs`) is the base for global managers. Call `CreateSingleton(dontDestroyOnLoad)` in `Awake`. Use `Singleton<T>.Exists` before accessing `Instance` from contexts where the singleton might not be initialized.

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
| `NemesisEvents.OnStateChanged` | NemesisTelemetry | NemesisAudio, NemesisEyes |
| `NemesisEvents.OnCaptureResolved` | NemesisCatchState | CaptureFadeView |
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

`IInteractable` (`_Project/Scripts/Interfaces/IInteractable/IInteractable.cs`) defines `CanInteract()`, `Interact()`, `IsRepeatable()`, `GetInteractText()`, `GetInfoText()`.

Detection is a **camera SphereCast**, not trigger registration: `InteractionManager.RaycastForInteractable()` casts from `Camera.main` forward with `SO_InteractionManager.InteractionDistance`, a 0.1 radius, against `InteractableLayers | BlockingLayers`. It resolves the `IInteractable` on the hit collider or its parents; if the first hit has none, it is a wall and nothing is targeted. `BaseRangeInteractable` no longer registers anything — it only describes *what* the interaction is. Each interactable needs a Collider on itself or on a child in the Interactable layer so the cast has something to hit.

The manager fires `InteractionEvents.TargetChanged(interactable)` when the target changes. Key `[E]` is processed in `InteractionManager.Interact()` with a 0.2s cooldown.

Selection is by **first hit along the ray**, with no dot-product priority when several interactables overlap — see `docs/TODO-UI.md` · Interaction Prompt.

### Puzzles

Progress is **not** stored on the puzzle objects. `PuzzleStateManager` (`_Project/Scripts/Managers/`) is the
single source of truth and holds five collections keyed by string id: completed puzzles, inserted
sockets, opened doors, valve positions and container slots. Everything else reads from it and
writes to it — which is what lets a scene reload, a checkpoint rollback or a save restore work
without every puzzle object having to serialize itself.

It raises one event, `PuzzleStateManager.OnPuzzleCompleted(string puzzleId)`, and that string is
the spine of the whole game: routes unlock on it (`NemesisRoute.unlockedByPuzzleId`), the Nemesis
wakes up on it (`NemesisController.activatedByPuzzleId`), checkpoints activate on it
(`Checkpoint.puzzleId`), and modules resolve on it (`ModuleData.associatedPuzzleId`). All four use
the same **subscribe + catch-up** shape, and they have to: the event only fires on the transition,
and an object loaded after the puzzle was solved would otherwise never hear about it. Subscribe in
`OnEnable`/`Awake`, then re-check `IsPuzzleCompleted(id)` in `Start`.

Sub-puzzle types, each an interactable plus a controller that watches for its completion condition:

| Puzzle | Interactable | Controller | State key |
|---|---|---|---|
| SP1 — button sequence | `SequencePanelInteractable` + `SequenceButtonInteractable` | opens `SequencePanelUIController` | completed puzzle id |
| Sockets / fuses | `SocketInteractable` | — | `SetSocketInserted` |
| Valves | `ValveInteractable` | `ValvePuzzleController.CheckValves()` | `SetValvePosition` |
| Containers / balls | `BallPuzzleItem`, `BasketTrigger`, `GrabbableBall` + `PushBoxTriggerLogic` | `ContainerPuzzleController.CheckContainers()` | `SetContainerSlot` (keyed by **BallId**) |
| Hub | — | `HubPuzzleController.CheckHubCompletion()` | completed puzzle id |
| Doors | `DoorInteractable` | — | `SetDoorOpened` |

`SO_PuzzleData` and its siblings (`SO_SequencePuzzleData`, `SO_ValvePuzzleData`,
`SO_ContainerPuzzleData`, `SO_HubPuzzleData`, `SO_SocketData`, `SO_ValveData`, `SO_ContainerData`,
`SO_DoorData`) carry the ids and the solution. **The id string in the asset is the contract** — a
typo there fails silently, because nothing ever looks up a puzzle that does not exist.

`PuzzleController` / `PuzzleReward` are a generic wrapper that predates the per-type controllers and
still has no callers; see *Current state* below.

### Modules (the device timers)

The run's clock. `ModuleManager` (`_Project/Scripts/Player/Modules/`) owns one `ModuleRuntime` per
`ModuleData` in `SO_ModulesConfig`, in order, and enforces two rules: **one module Active at a
time**, and **module N cannot start until N−1 is Resolved**.

The loop is fully wired end to end:

```
ZoneTrigger (player walks into a zone)  -> ModuleManager.ActivateModule(data)   [timer starts]
PuzzleStateManager.OnPuzzleCompleted    -> ResolveModule(matching module)       [timer stops]
timer hits zero                         -> ModuleEvents.OnExploded              [penalty is permanent]
```

`ModuleData` is the designer asset and the manager **never writes to it** — live values are in the
POCO `ModuleRuntime`, so a run cannot leave dirty state in a `.asset`. Timers tick on
`Time.unscaledDeltaTime` on purpose: the inventory sets `timeScale = 0`, and a timer you can stop
by opening a menu is not a timer. `PauseManager` pauses them explicitly instead, through
`PauseTicking`/`ResumeTicking` (ref-counted).

Penalties are routed by `PenaltyType` into `PlayerStateManager.ApplyPenalty` and are **permanent
for the rest of the run by design** — there is no method to clear them:

| Penalty | Effect | Read by |
|---|---|---|
| `Legs` (M1) | `MoveSpeedPenaltyFactor` drops to `CojeraMultiplier` | `EffectiveMoveSpeed` |
| `Chest` (M2) | `SprintPenaltyFactor` drops by `SprintReduction` | `PlayerMovingState` sprint |
| `Head` (M3) | `IsBlindnessActive` | `BlindnessOverlayView`, which subscribes to `OnExploded` itself |

The movement states feed the Animator the **pre-penalty** speed so a limping player still plays the
run blend; only the physical velocity is scaled.

### Capture, checkpoints and session reset

A capture is a **cost, not a Game Over**. The chain is deliberately one-directional so no system
reaches into another:

```
NemesisCatchState -> PlayerStateManager.OnCaptured() -> PlayerEvents.OnPlayerCaptured
                 -> CheckpointManager (subscribed)   -> respawn + PuzzleStateManager.RestoreSnapshot
                                                     -> ModuleManager.ApplyTimePenalty
                 -> CheckpointManager.OnRespawned    -> NemesisStateManager (subscribed)
```

The Nemesis never calls into save or UI; it only raises and only listens. `Checkpoint` activates by
physical trigger, by puzzle id, or either, once only — and **snapshots puzzle progress at
activation, not at capture**, because the point of the rollback is to undo whatever you did after
the last safe moment.

`GameSession.BeginNewSession()` is the New Game / Retry reset, called from `MainMenuController` and
`ResultScreenController`. Persistent managers implement `ISessionResettable` and register in
`Awake`; statics subscribe to `GameSession.OnNewSessionStarting`. **Adding a new stateful manager
means implementing that interface — not editing the menu controllers.**

### Inventory

Spec: *Inventory System v2.0*. **The inventory is unlimited** — no slots, no capacity counter, no
weight. The only management decision is the discard, and it is voluntary and irreversible. Anything
that reintroduces a cap contradicts the design, which puts the pressure on the module timers and
the Nemesis instead.

`InventoryManager` (`_Project/Scripts/Managers/`) is the model: a flat `List<SO_InventoryItem>` with
`AddItem` / `DiscardItem` / `ConsumeItem`, each raising an `InventoryEvents` static event, plus the
queries `HasItem`, `GetItemsByCategory` and `HasMetallicItem`, and the save hooks `GetItemIDs` /
`RestoreFromIDs`. It is an `ISessionResettable`, so New Game / Retry empties it and re-seeds
`initialItems` — that list is a testing convenience and ships empty.

`SO_InventoryItem` carries `ItemID`, `ItemName`, `Category`, `ItemDescription`, `IsMetallic`,
`IsConsumable`, `ConsumeDescription`, `IsUnique`, `ContentType` (`None` / `Text` / `Audio`),
`TextContent`, `AudioClip` and `TargetID`. **`ItemID` is the save contract.** `TargetID` has no
reader anywhere — world interactables reference the item *asset*, not its id — so leave it empty
rather than authoring a second, unenforced wiring scheme.

**Items are used in the world, never from inside the inventory.** There is no "[E] use / insert"
action on the list, and the spec's bottom hint implies one that does not exist. The key and
component loop of spec §9–§10 lives on the interactables: `DoorInteractable` requires
`SO_DoorData.RequiredKey` and consumes it on the first unlock when `ConsumeKey` is set (re-opening
never re-checks); `SocketInteractable` requires `SO_SocketData.RequiredItem` and records the insert
in `PuzzleStateManager`. Both surface "You need X" through `GetInfoText()`.

The UI (`_Project/Scripts/UI/Inventory/`, controller `InventoryManagerUI`) is a modal on `Tab` and
therefore goes through `UIStateManager` — which is what sets `timeScale = 0`, which is why the
module timers are unscaled. The layout is the spec's: topbar, device HUD, grouped list, detail
panel, discard footer.

- `InventoryView` rebuilds the list on every refresh and instantiates a `GroupLabelView` per
  **non-empty** category; `ItemSlotView` is the row and reports clicks back to the controller.
- `ItemDetailView` fills header, description and metadata, and owns the **doc panel** for
  `ContentType.Text` items (its own layer, reset to the top of the scroll on each open).
- `DiscardDialogView` confirms. `RequestDiscard` only opens the dialog; `InventoryManager.DiscardItem`
  is the only thing that removes anything, and it is called on confirm alone.
- `ModuleHUDView` + `ModuleRowView` + `ActiveModuleTimerView` + `FailuresPipsView` are the device
  HUD of spec §3, inside the inventory: they subscribe to `ModuleEvents` and read `ModuleManager`
  (`GetActiveModule()`, `GetExplodedCount()`) rather than polling, and they keep counting with the
  inventory open because the timers tick on `Time.unscaledDeltaTime`.

**ESC is a layer stack, not a close button.** `InventoryManagerUI.HandleCancelInput` unwinds
discard dialog → doc panel → selection → inventory, one press per layer, and `RequestClose()` (the
`IModalUI` hook the `UI/Exit` action calls) *is* that method. `Tab` is handled separately, closes
the doc panel first if it is open, and only acts while the inventory is the top of the
`UIStateManager` stack. It also refuses to open while the player `IsDisabled`: opening a menu sets
`timeScale = 0`, which used to freeze the Nemesis mid-capture for as long as the player kept the
inventory up.

Two gaps against the spec, both intentional for now:

- **The audio player is off.** `ItemDetailView.enableAudioFeatures` ships `false`; the play/stop
  buttons, progress bar and `AudioSource` exist but are inert, and `CloseInventory`'s `StopAudio()`
  call is commented out. Turning it on means honouring spec §14: `ignoreListenerPause = true` (the
  `Awake` already sets it), progress advanced unscaled, and the clip stopped on close — a recording
  still playing over the gameplay scene is the failure mode, and restarting from the beginning on
  reselect is the specified behaviour, not a bug.
- **Two document paths exist and they are not interchangeable.** An item with `ContentType.Text`
  shows its text in the inventory's doc panel and can be re-read forever; a `NoteInteractable`
  opens `DocumentReaderController` with an `SO_DocumentData` and never enters the inventory. Spec
  §11 wants the notes to be held items, which is the first path. Choose one per piece of paper —
  wiring both means the same note exists twice with two different texts to keep in sync.

**Category is data, not a switch.** `SO_ItemCategoryConfig` holds, per category, the UI colours,
the group label, the tag, the pickup sound (`[SoundId]`, used when `PickUpInteractable` names none
of its own) and the 3D shader tint/emission. Worth knowing: the inventory palette in the asset
(red keys / green components / blue notes / amber special) is **not** the palette in spec §4.3 —
the spec's `#37474F` / `#4E342E` / `#263238` / `#1A237E` survive as the *world model* tints written
to the `ItemPSX` shader. Either can be changed in one asset; neither is a code change.

`IsMetallic` has exactly one reader, `HasMetallicItem()`, and **that method has no callers**: the
magnetic door of spec §13.1 does not exist yet.

**The item catalogue is a third built, and the placeholders are wrong in ways that will bite.**
Five of the spec's eleven items exist as assets, in `ScriptableObjects/Puzzle1/Items/` —
`llave_ingenieria`, `fusible_nuevo`, `nucleo_energetico`, `nucleo_mecanico`, `regulador_presion`,
all metallic and consumable exactly as specified. Missing: the room key, the three notes, the audio
recording and the chain cutter. The three `ScriptableObjects/InventoryItems/SO_InventoryItem*.asset`
placeholders are not a head start — **all three carry `itemID: 1`**, which makes them
indistinguishable to `GetItemIDs` / `RestoreFromIDs` the moment save/load is real, and the "Test
Note" is flagged `isMetallic` where spec §13.1 says a clue never is. Fix or delete them before they
get duplicated into the real items.

### Player

`PlayerStateManager` drives `Idle / Moving / Crouch / Interacting / Hidden / Disabled`, resolves its
own hierarchy references in `Awake` (so the character model is swappable) and disables itself with
one aggregated error if anything is still missing.

It is a **Rigidbody + CapsuleCollider** character, not a `CharacterController` — step offset and
slope limit do not exist here. Movement is written as `linearVelocity` through
`ApplyMoveVelocity(Vector3)`, which capsule-casts against `obstacleMask` and projects the horizontal
component along whatever it is about to hit. Without that deflection the raw assignment overwrites
the solver's collision response every frame and the player sticks to every prop. `CheckGround`
probes downward — masked, bounded, and with its return value checked — and its hit normal is what
`MoveDir` gets projected onto, so a bad probe corrupts movement rather than just grounding.

`SO_Movement` holds speeds, capsule heights per stance and the **noise radii** (crouch 1 / walk 2 /
run 6) that `FieldOfListening` picks up through the `AudioEmitingZone` sphere the states resize.
`SO_CameraConfig` holds the rig: FOV, shoulder offset, look limits and the crouch pivot drop.
`PlayerRegistry` is a static class (not a `Singleton<T>`) that every system uses to find the player,
with `SubscribeAndCatchUp` for consumers that load before the gameplay scene.

### Hiding spots (spec'd, not built)

Spec: *Hiding System v1.0*. Three spot types — metal locker (medium risk), under a work table
(high risk, the only one that does not blind the monster), cargo container (low risk, no vision at
all). Entering and leaving are always deliberate `E` presses, there is no time limit, and while
inside the player controls only the camera and their breathing.

**What already exists, and it is more than it looks like.**

- `EPlayerState.Hidden` and `PlayerHiddenState` are registered and reachable, but the state is
  inert: it changes no collider, no camera, no visibility, and its whole `UpdateState` is falling
  back to `Idle` when `IsHidden` goes false. The `R` key in `PlayerStateManager.InputUpdate` is the
  current stand-in for a hiding spot and goes away when the real one lands.
- **The Nemesis side is already correct and should not be rewritten.**
  `FieldOfView.FindVisibleTargets` returns early while `PlayerRegistry.Current.IsHidden`, clearing
  the peripheral awareness meter as it goes — without that clear the monster reasons its way into
  the locker off the sweep that watched the player climb in. Extreme proximity is tested in
  `Update` *before* that early return, so `SO_NemesisData.ProximityDetectionRange` stays the single
  thing that breaks hiding, exactly as spec §5.2 asks. `NemesisTestConsole` has a Hide toggle for
  exercising all of this with no spot in the scene.
- Hearing needs nothing special: a noise made while hidden reaches the Nemesis through the same
  emitter every other noise uses, and lands it in `Investigating`, not `Chasing` — which is the
  spec's own distinction (§5.1).

**What has to be built, and the shape it should take here.**

- `HidingSpotInteractable : BaseRangeInteractable` — one component with an enum for the three
  types, a `Transform` for the interior camera pose, and a reference to a new `SO_HidingData`.
  Detection is the `InteractionManager` SphereCast, so the spot needs a collider on the
  Interactable layer; the spec's "two spots overlap, which prompt wins" case is already answered by
  first-hit-along-the-ray.
- Entering must set `IsHidden` **and** stop gameplay input. There is no `SetHidden` or `SetTrapped`
  to call: add the transition on `PlayerStateManager` and make `PlayerHiddenState` do the work —
  zero `CurrentVelocity`, stop writing `linearVelocity`, keep the camera live. `Tab` must not open
  the inventory while hidden (spec §6): `InventoryManagerUI.HandleInput` already refuses while the
  player `IsDisabled`, and hiding needs the same guard added next to it.
- The camera is Cinemachine. Give each spot an interior virtual camera with the specified look
  clamps (locker ±15 / ±10, under-table ±45 / −5..+15, container ±10 / ±10) and let the brain blend
  in and out over the ~0.3 s transition; do not hand-lerp `Camera.main` against
  `PlayerCameraController` and `SO_CameraConfig`, which own the normal rig.
- **Breathing is the part the spec cannot be followed literally on.** There is no
  `OnNoiseGenerated`. Breathing has to be expressed through the emitter the Nemesis already polls:
  while hidden and not holding breath, enable `AudioEmitingZone` at the breathing radius for a beat
  every `breathingInterval`, off in between; holding `F` keeps it off; releasing `F` pulses it once
  at the larger exhale radius. Read *Noise is a sphere, not an event* before writing a line of it —
  in particular a pulse shorter than `FieldOfListening.listenDelay` can be heard by nobody, and the
  emitter must be restored to what the movement states expect on exit.
- `SO_HidingData` holds `breathingInterval` (3 s), the breathing and exhale radii, the container's
  `containerNoiseMultiplier` (0.5) and the locker's `closetBreathingMultiplier` (1.2, a *volume*
  multiplier on the player's own audio, not a radius). Per-spot modifiers are applied on entry and
  **undone on exit**, including on the paths that are not a normal exit — capture, checkpoint load,
  scene unload.
- Under-table is the exception that costs code on the Nemesis side: it does not blind vision, it
  narrows it. That is a new `underTableVisionMultiplier` on `SO_NemesisData` plus a branch beside
  the `IsHidden` early return in `FieldOfView`. Add the field at the end, add the range to
  `SO_NemesisDataEditor` and `NemesisGizmos` so it can be seen, and if the spot type ever becomes an
  enum a designer asset serialises, **append to it, never insert** — same rule as `ENemesisState`.
- Audio: the locker and the container want `AudioMixerSnapshot`s (`InsideCloset`, `InsideContainer`).
  The mixer is generated by `Tools/Audio/Create or Update Master Mixer` and today ships **only** the
  default snapshot — the same gap that keeps audio from responding to pause. One snapshot each; do
  them together.
- Checkpoints: spec §6 says a respawn never puts the player back inside a spot. `CheckpointManager`
  respawns at the checkpoint transform, so this holds for free — provided nothing leaves `IsHidden`
  set. Clear it in `OnCaptured()` and on respawn, not only in the exit interaction.
- The feedback already exists: `NemesisEvents.OnProximityChanged` drives `VignetteProximityView`
  from the real distance and keeps working while hidden, which is the "brief pulse of red while
  hidden" of Nemesis spec §6.2.

### Environmental obstacles (spec'd, not built)

Spec: *Obstacle System v1.0*. Nine obstacles in three categories — pass through (broken wall,
window, tight shelves, low pipe), act on (blocking shelf, furniture barricade, rubble) and climb
(low rubble, desk). None of them needs an inventory item, all are permanent, and several exist to
make noise the player has to decide about. Nothing of this is implemented: there is no
`ClimbableObstacle`, no `SO_ObstacleData`, no vault animation.

What the project already gives you, and where the spec's implementation notes should be ignored:

- **Crouch-gated passages need real geometry, not a trick collider.** The spec proposes an
  invisible 1.0-high box that refuses a standing player. This project does not need one: the capsule
  really shrinks to `SO_Movement.CrouchHeight`, and `HasHeadroomToStand()` sweeps a sphere upward
  before allowing a stand-up, deferring it until the space clears. A low pipe modelled solid on
  `Wall` or `Props` behaves exactly as specified, *and* pressing crouch under it no longer wedges
  the Rigidbody — a real reported bug that an invisible blocker would reintroduce.
- **The Nemesis is excluded from a gap by the bake, not by a component.** `Props` bakes as Not
  Walkable and `Default` is excluded from the bake entirely, so a crouch-only opening is already
  closed to the agent once the geometry around it is on the right layer. See *Layers*.
  `Tools/Nemesis/Validate Navigation Setup` reports geometry that missed the bake; the bake itself
  is always manual.
- **An obstacle that opens must re-open the NavMesh.** Copy `DoorInteractable.EnsureNavMeshObstacle`:
  a `NavMeshObstacle` with Carve on the solid collider, switched off when the shelf is pushed or the
  rubble cleared. Not baked geometry — a bake is static and cannot be undone at runtime — and not
  `NavMesh.BuildNavMesh()` mid-run, which the spec suggests and which this project cannot afford.
- **Interaction is `IInteractable`.** `[E] Push shelf`, `Clear rubble`, `Climb` are
  `BaseRangeInteractable` subclasses: `GetInteractText()` is the prompt, `CanInteract()` goes false
  once the obstacle is done, `OnInteractAttemptBlocked()` is the refusal feedback, and
  `InteractionManager` owns targeting and the 0.2 s cooldown. The spec's "the climb prompt must not
  appear mid-puzzle" is already true — the player is in `Interacting` and the raycast targets one
  thing at a time.
- **Climbing needs an input lock and there is no `SetTrapped`.** Use `IsInteracting` →
  `EPlayerState.Interacting` for the 0.6–0.8 s of the vault and restore afterwards. Keep the spec's
  own ruling: a capture that lands during the animation still lands — a vault is not immunity. Move
  the player to the dismount `Transform` at the end rather than animating the Rigidbody through the
  obstacle.
- **Noise is the emitter sphere again.** The spec's numbers (shelf 12, rubble 10, tight passage 5–6,
  window frame 3) mean "enable the emitter at this radius for about a second", not "raise an event",
  and they are pre-`NoiseRangeScale`, pre-occlusion figures. Put them in an `SO_ObstacleData`
  alongside the clips and tune against `NemesisGizmos`.
- **Persistence belongs in `PuzzleStateManager`.** `hasBeenMoved` / `hasBeenCleared` have to survive
  a checkpoint rollback and a scene reload exactly like an opened door does; a bool on the prefab
  does not. The existing `SetDoorOpened` collection is the precedent, and adding a sixth collection
  is a smaller change than teaching every obstacle to serialise itself. An obstacle that forgets it
  was cleared re-blocks a corridor the player already paid noise for.
- **The tight-shelf passage is the one that touches `SpeedMultiplier`** (spec: 0.5) plus a noise
  pulse above a speed threshold. Write it on enter and **restore it on exit** — in `OnTriggerExit`
  and on disable — or a player who leaves the trigger during a scene transition keeps the penalty
  for the rest of the run, with nothing on screen to explain it.

### Nemesis: routes, activation and siblings

Beyond the FSM described above:

- **`NemesisRoute`** — a patrol route is a GameObject whose direct children tagged
  `NemesisWaypoint` are its waypoints, **in Hierarchy order**. Reordering the route means
  reordering the children. Each route has a weight and a lock, optionally gated on a puzzle id.
- **`NemesisRouteGraph`** — merges every unlocked route and works out which waypoints are on the
  same NavMesh island. This is what lets the Nemesis borrow a waypoint from another route and adopt
  it, which is how it changes floor without waiting for the route roll.
- **`NemesisController`** — owns the routes, rolls the active one (weighted, biased toward where it
  *believes* the player is), and picks the spawn point. Activation is gated on
  `activatedByPuzzleId`: until that puzzle is solved the Nemesis is dormant — invisible, no senses,
  no navigation, FSM not started.
  **The spawn-in has one rule and it is not negotiable: far, out of view, and behind cover.**
  `ChooseSpawnPoint()` runs from `NemesisStateManager.Activate()`, and that moment is the worst
  possible one for a bad spawn — the Nemesis wakes when a puzzle completes, so the player is
  standing still, looking at what they just solved, with no chase to explain a monster being there.
  Each point is graded `TooClose` / `FarButInView` / `FarAndBehind` / `FarBehindAndOccluded`, and
  **only the top tier is ever used**; the lower three exist to explain a failure, not as fallbacks.
  The pick among qualifying points is the distance-weighted roll, so the guarantee and the
  run-to-run variety come from different places.
  **A null is "not yet", not "never".** When nothing qualifies, `Activate()` puts the Nemesis back
  to sleep and retries every 0.5 s (`TickDeferredSpawn`), because the condition clears itself the
  moment the player walks on or turns round. It never settles for a worse point — an earlier version
  took the best available tier, which sounds equivalent and means that the one time nothing is
  hidden is exactly the time it spawns in plain sight.
  Two tests, not one: `IsHiddenFromPlayer` is only an occlusion raycast, so a point twenty metres
  down an open corridor the player is facing counts as perfectly hidden. `IsInPlayerView` adds the
  angle test (`SO_NemesisData.SpawnSafeHalfAngle`, measured off the character body, flattened to XZ
  so the storey above is not rejected wholesale). `SpawnMinPlayerDistance` is the distance floor.
  Later respawns are unaffected — they go through `NemesisLifecycle.RepositionAfterCapture` and its
  own `RepositionMinPlayerDistance`.
  `onAllSpawnPointsVisible` was **removed**: it existed to let a fade mask a visible spawn, and a
  visible spawn can no longer happen.
- **`NemesisNav`** — every distance and reachability question measured over the NavMesh instead of
  in a straight line. `Vector3.Distance` lies in a level with floors, and three separate bugs came
  from that one mistake.
- **`NemesisDoorUser`** — opens doors by sweeping along `desiredVelocity`, independent of the FSM,
  so it works in patrol, investigation and chase alike. `DoorInteractable.nemesisCanOpen` /
  `nemesisCanForceLocked` are the per-door policy. **Every door carves the NavMesh automatically**:
  `DoorInteractable.EnsureNavMeshObstacle()` adds a `NavMeshObstacle` with Carve to the leaf's solid
  BoxCollider on `Awake` when the door has none. It has to exist because a `NavMeshAgent` ignores
  physics colliders entirely, and the leaf lives on layer `Default`, which the surface excludes from
  its bake — without the obstacle the NavMesh runs straight through every doorway and the Nemesis
  walks through the closed panel. An obstacle and not baked geometry because the bake is static: a
  baked leaf would block just as hard with the door open. Doors that already carry a hand-placed
  obstacle anywhere in their hierarchy are left untouched, and `autoCarveNavMesh` turns it off.
- **`NemesisAudio`** — per-state looping audio with crossfades. `stateLoops` is a designer-authored
  array, so **adding a value to `ENemesisState` without an entry crossfades the monster to
  silence**. `NemesisChaseMusic` is separate and driven by `OnChaseStarted/Ended`.

- **`NemesisClusterPatrol`** — patrol is by ZONE, not by waypoint: it picks a cluster of nearby
  waypoints, sweeps it, then moves to one next door, so the monster walks through the level instead
  of teleporting across it. Recently swept zones are down-weighted (`ClusterRecencyPenalty` /
  `ClusterRecencyMemory`), which is what stops it ping-ponging between two neighbours — excluding
  only the zone it just left is not enough, because the neighbour bias immediately favours it again.
  A zone's sweep is a list of `TourStop`s, not of waypoints — see *Nemesis: generated sweep points*.
- **`NemesisPursuit`** — where to run while chasing. Plain class, not a MonoBehaviour, owned by
  `NemesisChasingState`. See *Nemesis: chase and search*.
- **`NemesisLookAround`** — sweeps the gaze while the Nemesis stands still. Auto-added like the
  other siblings; tuned from `SO_NemesisData`, and inert at `ScanHalfAngle` 0.

Two shared helpers live in `_Project/Scripts/Utils/` and exist because the same code had been written by
hand several times over:

- **`RouletteSelection`** — weighted random selection. Takes a list of weights and returns an
  **index**, because every caller already holds parallel buffers it reuses to avoid allocating on a
  hot path; a `Dictionary`-shaped API would mean building one per call to throw it away. It owns
  the two edge cases each hand-rolled copy had rediscovered separately: every candidate at weight
  zero (uniform among them), and the float-rounding fall-through. Note it also skips zero-weight
  entries outright — `Random.value` can return exactly 0, and without that guard the first bucket
  wins even when the caller had weighted it out.
- **`LineOfSight`** — see *Nemesis: senses*.

The Nemesis rolls rather than picks the best almost everywhere — patrol zone, waypoint, spawn point,
pursuit detour, search target. That is deliberate and consistent: *"the zone you are in gets more
tickets"* reads as the monster prowling around you, *"it always goes where you are"* reads as it
seeing through walls.

### Nemesis: the decision layer

**The states do not decide which state comes next.** `NemesisDecision` does, once per frame, and
writes the answer through `NemesisStateManager.RequestState`. Every state's `UpdateState` is
therefore only about *doing* its job, not about *leaving* it.

There is **exactly one voter**, and that is load-bearing. An earlier version ran a Unity Behavior
graph alongside the C# ladder; both wrote the same `NextState` channel, so the FSM transitioned
every frame and — because `StateManager.Update` runs a transition **or** `UpdateState`, never both
— never executed a single frame of any state. That reads in game as a monster that looks straight
at you and stands there twitching. If you add a second thing that writes `NextState`, you will
reproduce it.

**The ladder is data, the tree is structure.**

- `SO_NemesisPriorities` (asset) holds the **order** and **which questions** each rung asks. It is
  a reorderable list, read top to bottom, first match wins. Reordering is a designer action with no
  recompile.
- `SO_NemesisData` holds the **numbers**. A rung says "younger than the chase grace", never
  "younger than 2", so a threshold keeps one home.
- `NemesisDecision` holds the **predicates** — one side-effect-free property per question, so the
  asset can reorder the reasoning but can never hold a second definition of what "sees the player"
  means.
- Evaluation builds a **decision tree** from that rung list (`_Project/Scripts/AI/`: `ITreeNode`,
  `QuestionNode`, `ActionNode`): one `QuestionNode` per rung, its false branch being the rung
  below. Same first-match-wins answer a `for` loop gave, minus the ceiling — a false branch can
  open a different sub-ladder instead of always being the next line down.

**Two rules that fail silently if you break them:**

1. **Append to `ENemesisPredicate` / `ENemesisState` / `ENemesisThreshold`, never insert.** Unity
   serialises an enum field as its integer, so the asset stores `predicate: 6`, not
   `predicate: IsInState`. Inserting a member renumbers everything below it and rewrites the
   authored ladder into a different one — the rung that asked "am I in this state" starts asking
   "have I arrived", and nothing errors.
2. **Add a new rung in BOTH places.** `NemesisDecision.Ladder` prefers the asset whenever it has
   rungs, and the shipped prefab has one assigned. A rung added only to
   `SO_NemesisPriorities.BuildDefaultLadder()` never runs. The code default exists so the two
   cannot drift, not as the thing that executes.

**Hysteresis.** `MinimumStateDwell` (default 0.35 s) holds the answer briefly so a sensor flickering
at the edge of its range cannot trade the Nemesis back and forth every frame. Inside the window only
rungs marked `interrupts` may win. Reserve that flag for what must never wait — seeing the player,
being able to grab them, and a physical fact like riding the lift. Marking everything an interrupt
is the same as switching hysteresis off.

**The tree is rebuilt when the ladder's shape changes**, compared **element by element**. Reordering
a `List<T>` changes neither its identity nor its length, so both cheap checks miss a reorder — and
reordering is the ladder's entire authoring workflow.

**The shipped order, and the two commitment groups.** Read top to bottom it is the design: a
capture in progress is never re-decided; the lift outranks plain sight, because a visible player
one floor up is the case a flat chase handles worst; the bottom rung is unconditional so the ladder
can never fall through to nothing. Two groups exist purely to stop oscillation, and both were added
after watching it happen:

- `esta cruzando el montacargas` + `ya se comprometio con el montacargas` — see *Freight elevator*.
- `le queda presupuesto de busqueda`, which sits **above** the noise rung. With the noise rung
  higher, hearing anything while searching voted the Nemesis into `Investigating` before
  `NemesisSearchingState.UpdateState` ever ran a frame, so its "a fresh noise re-aims the cut-off"
  logic was dead code and every noise cut the search short.

`NemesisDebugHUD` shows the winning rung's index and note every frame. "Why is it doing this" is
not answerable without it.

### Nemesis: senses

**Vision is two cones, not one.** `SO_NemesisData.FocusAngle` is the inner cone where detection is
instant, exactly as it always was. Everything between it and `ViewAngle` is **peripheral**: it does
not trip a sighting, it fills an `Awareness` meter (0..1) for as long as the exposure lasts, faster
the closer the target is. At 1 it promotes to a real sighting and everything downstream behaves
normally; above `AwarenessTriggerThreshold` but below 1 it reads as `IsSuspicious`, which the
`vio algo de reojo` rung turns into `Investigating` — the Nemesis walks over to look instead of
sprinting. Losing the exposure decays the meter at `AwarenessDecayRate`, so leaning out twice in a
row is worse than leaning out once.

Before this, vision was all-or-nothing and "sees the player" is an **interrupt** rung, so peeking
round a corner started a full chase in the same frame with no beat for the player to react to.

Setting `FocusAngle` at or above `ViewAngle` removes the peripheral band entirely and restores the
old instant-detection behaviour. That is a legitimate choice and it fails invisibly, so
`SO_NemesisDataEditor` checks for it.

**The gaze is not the body.** `FieldOfView.LookDirection` is what the cone is cast from, and it
defaults to the view transform's forward but can be driven elsewhere. It has to be separable
because the `NavMeshAgent` owns the body's rotation — it turns towards whatever it is walking at —
so with the cone welded to that, a Nemesis standing still stares down the corridor it arrived from
for the whole wait and physically cannot look anywhere else. `NemesisLookAround` sweeps it
+/-`ScanHalfAngle` at `ScanSpeed` during the two moments the Nemesis is deliberately stationary:
waiting out a patrol waypoint, and pausing at a search point. It hands the gaze back on every other
state.

**`LineOfSight` (`_Project/Scripts/Utils/`) is the shared range/angle/occlusion test.** Use it rather than
writing the trio again — six hand-rolled copies existed before it. Two things about it are
deliberate:

- **Nothing flattens Y.** The angle test is full 3D, matching what `FieldOfView` has always done: a
  player on a catwalk directly overhead is outside a 90-degree cone. `NemesisGizmos` flattens when
  it *draws* a cone, which is a drawing concern and does not belong in the test.
- **`CheckConeSampled` samples three points up the target's bounds** (feet, centre, head) and tests
  angle and occlusion *together* per sample. Collapsing it to one central ray narrows detection
  everywhere at once — a head showing over a crate stops counting — and nothing errors; the monster
  just gets quietly worse at its job.

Hearing is unchanged and described under the FSM section: `FieldOfListening` occludes sight and
sound with **different** masks, and how loud the player is (their emitter radius) decides the real
range.

### Nemesis: chase and search

**Chasing runs `NemesisPursuit`, not `destination = belief`.** Seek aimed at where the player
already was means arriving after they have left the next place too, so against someone running in a
straight line the Nemesis holds station instead of closing.

- **Prediction.** `NemesisPursuit.PredictAhead` projects the belief forward by
  `ChaseTimePrediction` along the observed velocity, and **keeps the dot guard**: if the lead point
  lands on the far side of the Nemesis — which is what happens when the player runs *at* it — it
  aims at the target instead of turning around and sprinting away. `NemesisSearchingState` uses the
  same static with its own (deliberately shorter) `SearchLeadTime`; it used to have a second copy
  without the guard.
- **The velocity is OBSERVED** (`FieldOfView.LastKnownVelocity`, measured between sightings) and
  never read off the player's movement code. That is the difference between predicting and
  cheating, and it is what keeps changing direction the instant you break line of sight a real
  counterplay.
- **Route choice.** With the player out of sight, it scores patrol waypoints by: clear line of
  sight to the predicted point (the factor that produces flanking for free), being within
  `ListenRange` of the belief (so a footstep re-acquires you), path proximity to the last known
  position decayed by `BeliefFreshness`, and its own arrival time. It is a **roll, not an argmax** —
  always taking the single best vantage point is indistinguishable from knowing where you are. A
  detour must fit inside `ChaseDetourTolerance` of going direct, unless there is no complete direct
  route at all, in which case anything reachable beats standing against the wall.
- **It does its own path query and deliberately NOT through `NemesisPathOracle`.** The oracle holds
  one cached answer and does not key it on the target, so querying it here would hand the pursuit a
  verdict computed for the decision layer's belief and, worse, reset the oracle's timer with a
  verdict computed for the predicted point — which the elevator rung then reads as its own. It is
  affordable because the replan is already throttled by `ChaseRouteReplanInterval`.

**Searching picks where to look with a weighted roll** (`PickSearchTarget`), mixing the last known
position, the predicted position, what it has not swept yet (reduced, not excluded — a search that
refuses to double back runs out of places to go), and its own travel time. It used to be "the
nearest unvisited waypoint from where I am standing", which peeled outward from the spot it lost
you at while you walked away.

`SearchPauseTime` makes it stop at each point and look around before choosing the next. That is
what makes a search **legible**: without it the Nemesis chains destinations and, from inside a
hiding place, none of it says whether it is closing in or has already written the area off.

The interception (`TryGetInterceptPoint`) reasons about the *player's* travel time rather than its
own, and is the only part that can put the Nemesis somewhere before you get there. It runs on entry
and on a fresh noise, **never in the tick** — it costs two path queries per candidate.

### Nemesis: node movement vs free roam

**Two ways of getting around, and which one a state uses is now explicit.**
`NemesisStateManager.MovementOf` is the table; `CurrentMovement` is on the debug HUD next to the
state name.

- **Node-bound** (`Patrolling`, `Traversing`) — the waypoints *are* the route, walked in the order
  the designer authored them.
- **Free roam** (`Chasing`, `Searching`, `Investigating`) — anywhere on the NavMesh, with the
  waypoints demoted to hints. `NemesisPursuit` already worked this way; `Searching` did not.

The distinction used to be implicit, readable only by opening each state to see what it assigned to
`NavAgent.destination`, and `Searching` had drifted onto the wrong side of it without anyone
deciding that: `PickSearchTarget` rolls over graph nodes and reaches the free NavMesh only down an
error path, so **a room with no waypoint inside it was a room the Nemesis could not look in**,
however plainly it had just watched you walk into it.

**`NemesisFreeRoam`** is the free-roam mover — plain class, same shape as `NemesisPursuit`, owned by
the state that uses it. It samples points on the NavMesh around a committed anchor, offering
waypoints inside the area first (a waypoint the designer put in this room is a considered opinion
about it) and filling the rest with sampled points. Dropping the graph drops two guarantees that
were free, and both are paid for explicitly:

- **Reachability** — `NavMesh.SamplePosition` returns the nearest surface, including one on another
  island. Every candidate is path-tested; an unreachable destination is how the agent ends up
  pressed against a wall with `remainingDistance` at zero.
- **Confinement** — a room is not a circle. The disc is clipped by
  `FieldOfListening.IsOccludedByWall` from the anchor, so a doorway stays open and the corridor
  behind the wall does not. There are no authored room volumes in the project; this is the derived
  stand-in for one, and it will treat an L-shaped room as two.

Swept memory here is **spatial** (a list of positions, `SweptRadius` apart), not node indices, since
most destinations are not nodes.

**The room commitment** (`TryCommitRoomSweep`) runs *before* the interception, and that ordering is
the fix for "it saw me go in there and kept walking". Losing sight has two shapes and the state used
to answer both with a cut-off: across the level, cutting you off ahead of your heading is right;
through a door five metres away it is wrong, because `TryGetHeading` reads the trail of *corridor*
waypoints and the intercept lands further down that corridor. Three tests gate it — the belief must
be **from sight** (a noise through a wall is not evidence of which room you are in), **fresh**, and
**close measured over the NavMesh** (straight-line distance calls a room close when it is a
forty-metre walk around the wall). `RoomCommitRange` at 0 disables it and restores
interception-first.

**Sight outranks hearing for `SightCommitTime`.** A committed sweep ignores noises *outside* the
room — otherwise throwing something down the corridor is a free escape from the one situation the
monster should be most dangerous in. A noise *inside* the swept area always re-aims, and re-commits
the sweep rather than dropping it; that branch is load-bearing, since the belief it produces is no
longer from sight and would otherwise fail the first test and kick the Nemesis back out to an
interception.

Tunables: `RoomSweepRadius`, `RoomCommitRange`, `SightCommitTime`. Drawn by `NemesisGizmos`
(`drawRoomSweep`) as the anchor, its radius and the swept trail in visit order — a trail that keeps
crossing itself means `SearchSweptPenalty` is too weak.

**Level-design consequence worth knowing:** before this, a room with no waypoints was one the
Nemesis could not search. That was an accidental difficulty valve, and it is now gone — rooms that
played as safe need re-testing.

### Nemesis: generated sweep points

**One waypoint can mark a room the Nemesis actually prowls.** Before this, a cúmulo's sweep was its
member waypoints and nothing else, so a room marked with a single waypoint produced a tour of one
stop: walk to it, tour exhausted, leave. Getting a room swept meant hand-placing four or five
markers that said nothing the first one did not.

`WaypointSatellites` (0 = off) generates that many points on the NavMesh within
`WaypointSatelliteRadius` of each waypoint, at **graph build time** — `NemesisRouteGraph.BuildSatellites`,
after `BuildClusters`. Build time and not per visit because each candidate costs a path query, and
because a point that moved every visit could not be drawn, tuned or compared between sweeps;
variation comes from the tour shuffle instead. Reachability is tested **from the waypoint**, whose
island is already known, so the answer does not depend on where the Nemesis happens to be standing.

**They are not graph nodes, and that is the whole design.** Promoting them would have pulled them
into the cluster centroid and weight (silently re-aiming the zone the director bias targets), the
per-waypoint patrol roll, the pursuit's detour candidates, the search's interception, and the sensed
trail — and multiplied `AssignComponents` (a path query per node) and `FindDensestUnassigned` (N²)
by the satellite count. One authored waypoint still means one node everywhere the Nemesis *reasons*
about the level; it means a small area only when it comes to *walking* it. `BuildSatellites` runs
after `BuildClusters` specifically so the centroid/weight independence is structural rather than a
rule someone has to remember.

The tour is now `TourStop { Node, Position, IsSatellite }`: the chain of waypoints is still a greedy
nearest-neighbour walk, then `ExpandTour` drops each waypoint's generated points in **immediately
after it** — interleaved, so the sweep stays local (arrive, look around, move on) instead of walking
the zone twice. `ClusterMinWaypoints`/`ClusterMaxWaypoints` now budget **stops**, not waypoints;
budgeting by waypoint would walk two of an eight-stop zone and call it swept, which is what the
feature exists to fix.

`NemesisController.CurrentWaypointPosition` is the destination now, not `CurrentWaypoint.position`
— the Transform stays the handle for "which authored waypoint is this" (warnings, validator, gizmos)
but cannot express a point with no marker. `currentStopPosition` is an offset cleared in `AdoptNode`,
so it can never outlive the sweep that produced it.

`NemesisSetupValidator` reports the multiplier as a note: the generated points are runtime positions,
not GameObjects, so they appear in no hierarchy and no search, and a designer watching the Nemesis
walk five points around their one marker otherwise has no way to find out where the other four came
from. Gizmos draw them as small spheres in the tour, distinct from the ringed authored waypoints.

**`WaypointSatellites` at 0 restores the previous behaviour exactly**, not approximately: a zone whose
waypoints have no generated points comes out of `ExpandTour` identical to the chain that went in.

### Nemesis: zone gravitation (director bias)

**`ZoneBiasUsesRealPlayer` reads the player's live transform.** It is the one place in the system
that decides where to go from something the Nemesis did not sense, and it is deliberate.

`RoutePlayerBiasStrength` could never do what it was asked to: it is gated on
`TryGetPlayerBeliefPosition`, which returns false until the player has been seen or heard **at least
once**, and is then scaled by `BeliefFreshness`, which decays to nothing over `BeliefMemoryTime`. On
a cold patrol the bias was exactly zero, so "make it tend towards the player" could not be fixed by
raising a number that was being multiplied by zero.

What keeps it from reading as omniscience is that it is coarse in three separate ways: it weights
**zones only** and never individual waypoints (the per-waypoint roll still runs on the belief), the
weight is a **roll and not an argmax**, and `ZonePlayerBiasFalloff` is wide enough to say "your side
of the level" rather than "your room". It is **not** scaled by `BeliefFreshness` — it descends from
no sighting, so there is nothing to go stale.

It also decides the `KeepClosest` prefilter's anchor in `PickCluster`, which is not cosmetic: the
prefilter drops candidates *before* any weight is computed, so keyed on the belief alone the
player's zone is discarded in exactly the case the bias exists for.

`RouteReplanInterval` was lowered 25 → 12 s. That replan is the one moment the player bias runs
without `ClusterNeighbourBias` competing against it (`BeginPatrolCycle` passes
`applyNeighbourBias: false`), so at 25 s the gravitation was barely perceptible.

### Editor tools

All under `Tools/`:

| Menu item | What it does |
|---|---|
| `Nemesis/Validate Navigation Setup` | Reports mismatched layer masks and geometry outside the bake |
| `Nemesis/Repair Layer Masks` | Fixes every "what is solid" mask at once. Does **not** rebake |
| `Nemesis/Migrate Prop Layers` | Moves configured Hierarchy subtrees onto Props/Ground |
| `Audio/Create or Update Master Mixer` | Builds the 8-bus mixer |
| `Audio/Bake Ambience Texture Clips` | Generates the noise and drone clips (layers 3 and 4 need no sourced audio) |
| `Nemesis/Build Nemesis Test Scene` | Generates `Scenes/TestScenes/NemesisTestbed.unity`: correct layers throughout, plus one deliberately BROKEN modifier volume so working and broken sit side by side. Does **not** bake |
| `Items/Validate Interactable Highlights` | Finds interactables with no proximity highlight, and highlights whose material has no `_TintIntensity` / `_EmissionIntensity` — a silent no-op the inspector cannot show |

Custom inspectors live in `_Project/Scripts/Editor/`: `SO_MovementEditor` and `SO_CameraConfigEditor` draw
to-scale diagrams and live verdicts on top of `PlayerDiagramGUI`, a small shared IMGUI kit
(`Canvas`, `Bar`, `Verdict`, `Line`, `VMeasure`) with the project palette — green = healthy,
red = penalised, amber = warning. Reuse it for any new authoring inspector rather than starting a
new drawing helper.

**Id fields are dropdowns, not text boxes.** Two `PropertyAttribute`s in `_Project/Scripts/Attributes/` turn
a bare string into a list of the ids that actually exist, each with a drawer in `_Project/Scripts/Editor/`:

| Attribute | Drawer | Lists |
|---|---|---|
| `[PuzzleId]` | `PuzzleIdDrawer` | every `puzzleId` declared by the five puzzle SOs (collected by type NAME — they share no base class) |
| `[SoundId]` | `SoundIdDrawer` | every `SO_SoundData` id, grouped into submenus by `SoundCategory` |

Both exist for the same reason: these ids are matched by **string** at runtime, and a typo does not
fail — it produces a gate that never opens or a sound that never plays, with nothing in the console.
Both keep two escape hatches that are as load-bearing as the list: `(vacío)` to clear the field
(most of these are optional), and `(escribir a mano…)` for an id whose asset does not exist yet. A
value that matches nothing is shown with a `⚠ no existe` marker and **kept** — never silently
snapped to the first entry, which would rewrite wiring nobody asked to change.

`SoundIdDrawer` mirrors `SO_SoundData.Id`'s fallback to the **asset name** when the `id` field is
blank. Reading only the serialized field would omit exactly those sounds from the dropdown: present
at runtime, invisible in the inspector.

Add `[SoundId]` to any new string field that names a sound. The six that exist today are on
`PickUpInteractable`, `SocketInteractable`, `ElevatorCallPanel` (x2) and `ElevatorRideButton` (x2).

Scene gizmos worth knowing about: `NemesisGizmos` (every `SO_NemesisData` range, drawn to scale
with its metre value), `NemesisRoute` (waypoints and the route polyline), `InteractionRangeGizmo`
(the interaction SphereCast's real reach), `NemesisElevatorLink` and `ElevatorCallPanel`. All of
them use `OnDrawGizmos` rather than `OnDrawGizmosSelected` **on purpose** — a selected-only gizmo
is invisible in Prefab Mode, which is where tuning happens — and they read their values from the
ScriptableObject, never from a local copy that could drift from what the game actually uses.

### Layers

Four layers carry meaning. Everything else in the project is decoration.

| Layer | Holds | In the NavMesh bake | Blocks line of sight |
|---|---|---|---|
| `3 Ground` | floors, stairs, landings | yes, walkable | yes |
| `11 Wall` | walls, columns, door headers | yes | yes |
| `12 Props` | loose props, pipes, visual mass | yes, as **Not Walkable** | yes |
| `0 Default` | ceilings, everything unclassified | **no** | yes |

Two rules follow from that table, and both were learned the hard way:

**`Default` is deliberately outside the bake.** This project keeps ceilings there, and a ceiling
that goes into the bake comes out as a perfectly walkable roof. `Props` exists precisely so props
can be baked without dragging the ceilings in with them.

**Four separate systems ask "what is solid?" and they must all give the same answer** —
`FieldOfView.obstacleMask`, `FieldOfListening.obstacleMask`, the camera's
`CinemachineDeoccluder.CollideAgainst`, `SO_InteractionManager.blockingLayers`, and
`PlayerStateManager.obstacleMask`. They drifted apart once and every symptom looked like a
different bug: the Nemesis walked through props, the camera clipped through floors, the player
interacted through walls, and item highlights lit up across the level.

A mask that is wrong does not fail — it quietly does less. `Tools/Nemesis/Validate Navigation
Setup` turns that into a console message, `Tools/Nemesis/Repair Layer Masks` fixes every mask
above at once, and `Tools/Nemesis/Migrate Prop Layers` moves whole Hierarchy subtrees onto the
right layer (the roots it knows about are a constant at the top of `NemesisSetupValidator.cs`).
**None of them rebakes the NavMesh** — the mask decides what goes into a bake, it does not trigger
one. Navigation window ▸ Bake, by hand, afterwards.

### Freight elevator

`MovingPlatform` is the cabin, `NemesisElevatorLink` is the static root that owns the `NavMeshLink`
and measures the shaft, `NemesisElevatorUser` performs the crossing for the Nemesis, and
`ElevatorCallPanel` is the player's call button — one per landing, which is what stops a player who
rides up and steps off from being locked out of that floor.

The platform can be **claimed** (`TryClaim`/`ReleaseClaim`/`IsClaimed`). `NemesisElevatorUser`
holds the claim for the whole attempt, from before it starts waiting until its `finally`. That is
what stops a panel press from stealing a ride the monster has already committed to, and what stops
`autoReturnToBottom` from setting off under a Nemesis that is still walking aboard. A panel refuses
while the platform is claimed rather than queueing the call.

When the Nemesis gives up on a shaft it must call `ActivateCurrentOffMeshLink(false)` — **not**
`CompleteOffMeshLink()`, which reports the crossing as done and teleports it to the other floor for
free. It then shelves that elevator for `SO_NemesisData.ElevatorAbandonCooldown` seconds; without
that the agent is still standing on the link and restarts the same doomed wait on the next frame,
forever, with the stuck watchdog suppressed by the traversal.

**The agent is still ENABLED while it waits for the cabin**, and that window can run twenty seconds.
It is only switched off once boarding starts. Two bugs lived in that gap and both had the same
cause — nothing had told the agent to stop:

- `NemesisTraversingState` re-issued `destination` every frame at a point on the far side of the
  shaft. An agent asked to keep going while it sits on an off-mesh link it may not auto-traverse
  grinds along the link direction, which points straight through the shaft wall, because that is
  what the link is for. It is not that the Nemesis ignores the wall; nobody had told it to stop
  walking at it.
- The gait stayed `Running` from `Traversing.EnterState`, so it sprinted on the spot at the landing.

`NemesisElevatorUser.HoldStill(true)` now stops the agent, zeroes its velocity and drops the gait to
`Idle` before the wait, releasing on boarding and again in the `finally` so no early return strands a
frozen agent. `NemesisTraversingState` skips writing `destination` for the whole of
`IsUsingElevator`, not merely while `agent.isOnOffMeshLink` — `IsAgentReady` does **not** cover that
window, and the link is only one of the stages where something else owns the body. (The other one
cost a bug of its own: with boarding now agent-driven, a destination re-issued here every frame
walked the Nemesis back out of the lift it was boarding, one frame at a time.)

**The gait is now derived from the body, not just declared.** `SetGait` still says what the Nemesis
is *trying* to do; `NemesisStateManager.TickLocomotionAnimation` says what it is *actually* doing,
and drops the animator to Idle when a Walking/Running gait has produced no **flat** movement for
0.15 s. Flat because the lift ride is five metres of pure vertical travel with the body carried by
the platform, and counting that as movement plays a walk cycle for the whole ascent — the same bug
wearing the opposite sign. This retires "sprinting on the spot" as a class of bug rather than at one
call site: the cabin wait, a door being swept open and an agent stopped against geometry all used to
produce it independently. Teleports (a Warp, a spawn) are detected by step size and reset the sample
instead of registering as a sprint.

### The cabin has a NavMesh of its own

`ElevatorCabinNavMesh` — built at runtime, auto-added by `NemesisElevatorLink`, no scene wiring.

Before it, boarding was a `Vector3.Lerp` from the landing to the ride point with the agent switched
off, and that line passes through the `ElevatorLandingBarrier` and the shaft wall. Nothing was
ignoring the wall; a lerp has no opinion about geometry. Two Unity facts do the work:

- **A `NavMeshSurface` follows its transform.** The package re-adds its data instance whenever the
  transform moves (`NavMesh.onPreUpdate` → `UpdateDataIfTransformChanged`), so a surface parented to
  the cabin travels with it for free.
- **Separate surfaces do not connect to each other.** Two NavMesh instances that merely touch are
  still two islands; the only bridge is a `NavMeshLink`. Hence one short landing↔cabin link per
  landing, live only while the cabin is parked there — the same rule `ElevatorLandingBarrier` uses,
  polled for the same reason (arriving is only one of the four ways the cabin's whereabouts change,
  and it is the only one that raises an event).

The surface is collected by **Volume** and not by Children, because the volume is the one collect
mode that ignores the transform's scale — and this project's cabin is scaled 4.57 x 1 x 4.45.
Collecting children off a scaled transform is how a cabin ends up with a floor several metres wider
than itself. Its layer mask defaults to the cabin collider's own layer (`Interactable` here), which
is deliberately outside the level bake: nothing should bake a floor that moves.

While the cabin travels, the surface **and** both links go off. That is not thrift: the package
moves navmesh data by removing and re-adding it, so an agent standing on a moving island loses
`isOnNavMesh` every frame and its path with it, and a live link whose far end is climbing the shaft
is an invitation to walk into thin air. The ride itself is unchanged — agent off, body carried by
`MovingPlatform`. What the cabin's mesh makes solid is the two ENDS, which is all boarding needs.

Boarding and stepping off are therefore ordinary paths now (`WalkAboardAsync` / `WalkAshoreAsync`).
`WalkAgentToAsync` borrows three agent settings and hands them back in a `finally`:
`stoppingDistance` (the pursuit value of 1.5 m stops the Nemesis on the landing, not inside a 4.5 m
cabin), `autoBraking` (off globally, for patrol flow), and `autoTraverseOffMeshLink` (off since
`Awake` so lifts can be driven by hand — but the boarding link is a step through an open doorway,
and `NemesisElevatorUser.Update` returns early while a traversal is in flight, so nobody else would
ever cross it). **A partial path counts as failure**: it means the agent stopped at the nearest
point it could reach — against the barrier, typically — where `remainingDistance` falls to zero and
reads exactly like having arrived.

The shaft link is **suspended** for the duration of a crossing
(`NemesisElevatorLink.SetShaftLinkActive`), and restored unconditionally in the `finally`. Getting
the agent off that link is what makes the walk possible at all; leaving it suspended would delete
the lift from every future route query and quietly cost the Nemesis the ability to change floors.

The hand-driven lerp is **kept as a fallback**, decided once per trip (`boardByWalking`) rather than
per leg — boarding on foot and stepping off by hand would leave the body somewhere the other half of
the traversal does not expect. A bake that produces nothing says so and boarding reverts to crossing
the wall: ugly, but it still crosses, and a Nemesis that cannot change floors at all is worse.

**A crossing is abandoned when the player is reachable without the lift.** `IsUsingElevator` is an
interrupt in the ladder and spans the whole attempt, cabin wait included — up to twenty seconds
during which nothing the senses report can change the Nemesis's mind. That is correct while it is
riding and absurd while it is standing at a landing with the player in front of it, and it is where
"I got on the elevator with it and it ignored me" came from. `ShouldAbandonForPlayer` cuts the
pre-boarding waits short when the player is visible **and**
`NemesisDecision.RouteToBeliefCrossesFloors` is false. Visibility alone is deliberately not enough:
a player one storey up, visible through the shaft opening, is the exact case the lift exists for.
The question is never asked once the body is aboard — stepping off mid-shaft is a fall, and the grab
rung already outranks the crossing, so a player who rides up with it can still be caught.

**Two ladder rungs exist purely to keep the FSM from re-deciding mid-trip.**
`RouteToBeliefCrossesFloors` is measured from wherever the Nemesis is standing *right now*, and
walking towards a landing changes that path continuously — near the doors it flips outright, because
a route computed from a point already on the link no longer counts as crossing it. Each flip is a
real transition, and a machine that transitions never runs `UpdateState`, so it bounced
`Traversing`/`Searching` several times a second and never finished the walk.

- `esta cruzando el montacargas` (`IsUsingElevator`, an **interrupt**) — while
  `NemesisElevatorUser` is driving the body, the FSM says `Traversing` whatever the route verdict
  thinks. It is not a sensor that can flicker; it is a fact about who owns the body.
- `ya se comprometio con el montacargas` (`InState(Traversing)` + `TimeInStateUnder` +
  `BeliefAgeUnder`, both on `ElevatorCommitTime`) — the rung below can only *enter* the state, not
  hold it. Once committed, the approach runs on its own clock instead of being re-justified every
  frame. **Trade-off:** it holds for up to `ElevatorCommitTime` even if the route stops crossing
  floors. Lower that number if it feels sticky; the capture rung is an interrupt above it, so a grab
  still works.

### Safe zones (the Hub)

Handled purely on the NavMesh: a `NavMeshModifierVolume` over the Hub with its area set to
**Not Walkable** (not merely high-cost — a cost penalty only makes the Nemesis prefer another
route, it does not stop it entering if that route is the only one, or if pathfinding decides the
detour is worth it). Not Walkable means the NavMeshAgent physically cannot path there, full stop.

**The volume must sit on a layer the surface collects.** `NavMeshSurface` filters modifier volumes
through the same `Include Layers` mask as geometry, so a volume on `Default` — which this project
excludes on purpose, for the ceilings — is dropped from the bake **without a word**. That is exactly
how all three volumes in `WIRED_Zona1_Blockout` ended up doing nothing while looking correct in the
inspector, and the Nemesis walked into the Hub. They live on `Props` now: a volume has no renderer
and no collider, so nothing else that layer means can touch it. `Tools/Nemesis/Validate Navigation
Setup` reports this case, and the one where a volume's `Affected Agents` excludes every agent type
baked in the scene.

**Area 3 is `NemesisAvoid` (cost 99), and it blocks nothing.** It was called `NemesisBlocked`, which
is what the name problem was: a cost only makes a route expensive, so a "safe zone" built on it is
not safe, merely unpopular. Nothing in the project uses it — the safe zones are `Not Walkable`. It is
kept rather than deleted because NavMesh areas serialise by index, so removing index 3 would silently
renumber `Forklift` into it. The validator warns when a volume uses a high-cost area, in case someone
reaches for it expecting a wall.

There is no C# side to this rule. An earlier version gated it in code (suppressing the Nemesis's
sensors while the player stood in a trigger volume), which needed careful handling in
`NemesisStateManager`/`NemesisChasingState`/`NemesisInvestigatingState` to avoid the FSM
oscillating between Chasing and Patrolling every other frame. The NavMesh-only approach sidesteps
all of that: if the Nemesis can never reach the space, there is nothing to gate.

One consequence worth knowing: this only blocks *movement*. The Nemesis can still **see or hear**
the player inside the Hub if line of sight allows it (e.g. through a doorway) — it just cannot walk
in. If that turns out to read as a bug in playtest ("it grabbed me through the door" is different
from "it followed me in"; the Not Walkable area only prevents the second), the fix is back on the
sensor side: either extend the Not Walkable volume to cover the doorway approach, or block sight at
the door with a physical barrier the vision/hearing raycasts already respect.

### Visual Systems

**Vision Fog**: Fullscreen Shader Graph pass (`VisionFog.mat`) driven by `VisionRangeController.cs`. Sets `_PlayerPos`, `_VisionStart`, `_VisionEnd` as shader globals. Range lerps between `visionEndDark` (6m) and `visionEndLit` (25m) based on `RenderSettings.ambientLight` luminance. Guard: if `visionEnd <= visionStart`, the shader passes through unchanged — prevents a black screen when the controller is inactive.

**Item Highlight**: `ItemProximityHighlight.cs` uses `MaterialPropertyBlock` to lerp `_TintIntensity` (0.15 to 0.4) and `_EmissionIntensity` (0.0 to 0.2) over 0.3s with a SmoothStep curve when the player enters/exits range. Four preset materials by category: `mat_item_keys`, `mat_item_components`, `mat_item_clues`, `mat_item_special`.

**Color spec rules** (`color_visual_language_spec.docx` in Downloads): `#CC1A1A` red is exclusive to danger/emergency lights. `#FFC850` amber is exclusive to the player device. No outlines or waypoints — items are distinguished only by tint and emission.

**Renderer Feature order** (`PC_Renderer.asset`): SSAO then Vision Fog (BeforeRenderingPostProcessing) then PS1Effect (BeforeRenderingPostProcessing). Fog must precede PS1 so world-space coherence is preserved before the pixelation pass.

### ScriptableObjects

Data lives in `Assets/_Project/ScriptableObjects/`. Key types in `_Project/Scripts/ScriptableScripts/`:
- `SO_InventoryItem` — item data (ItemID, ItemName, Category, IsConsumable, IsMetallic, parameters).
- `SO_SceneList` / `ScreenEventChannel` — scene navigation.
- `SO_NemesisData` / `SO_NemesisMovement` — Nemesis tuning. `SO_NemesisData` has a custom inspector
  (`SO_NemesisDataEditor`) with a to-scale range diagram, a distance/angle case tester and a set of
  checks; add a detection value there too or it exists only as a float nobody can picture.
- `SO_NemesisPriorities` — the Nemesis's priority ladder as a reorderable asset. See
  *Nemesis: the decision layer*, especially the two rules about enum ordering and editing both the
  asset and `BuildDefaultLadder()`.
- `SO_Movement` / `SO_CameraConfig` — player tuning.
- `SO_SaveSlotData` / `SO_SaveSlotDatabase` — save slot stubs.
- Puzzle data: `SO_SequencePuzzleData`, `SO_ContainerPuzzleData`, `SO_ValvePuzzleData`, `SO_HubPuzzleData`.

### Async

Async operations (scene load/unload, UI transitions) use **UniTask** (`Cysharp.Threading.Tasks`). Use `UniTask.WhenAll` for parallel loads. Use `.Forget()` on fire-and-forget calls. For timers that run during pause, use `UniTask.Delay(ms, DelayType.UnscaledDeltaTime)`.

## Key Conventions

**Implementing a system from a GDD spec**: read *Design specs* before writing anything. Three of the four specs name APIs this project does not have (`OnNoiseGenerated`, `SetHidden`, `SetTrapped`, `SetSpeedMultiplier`, `CharacterController`), and following them literally either fails to compile or, worse, grows a second implementation beside the real one. A spec's **design** is the requirement; its implementation notes are suggestions written from outside this codebase. When the two collide, keep the design and use the mechanism that already exists here — and if the spec's mechanism really is better, replace the old one rather than running both.

**Adding a modal UI**: implement `IModalUI`, call `UIStateManager.Instance.Push(this)` on open and `Pop(this)` on close. Do not touch `Time.timeScale` or `Cursor` — the `UIStateManager` owns those.

**Adding a pushable screen**: create Model/View/Controller inheriting the base classes, create a scene, add it to `SO_SceneList` under a group label, add it to Build Settings. Invoke via `screenChannel.RaisePushScreen("label")`.

**GameResultManager**: call `GameResultManager.ResetSession()` at the start of each gameplay session, otherwise a second Win/Lose cannot be reported (static `_resultReported` guard).

**Enums that a ScriptableObject serialises are append-only.** Unity stores an enum field as its
integer, so an asset holds `predicate: 6`, not `predicate: IsInState`. Inserting a member anywhere
above the end renumbers everything below it and silently rewrites the authored data into something
else — nothing errors, the behaviour just changes. This applies to `ENemesisPredicate`,
`ENemesisState`, `ENemesisThreshold` and any enum a designer's asset references. Add at the end,
always.

**Reach for the shared helpers before writing the maths again.** `RouletteSelection` (weighted
random) and `LineOfSight` (range / cone / occlusion) in `_Project/Scripts/Utils/` both exist because the same
few lines had been re-derived in four to six places, and copies drift: two of them had already
stopped agreeing on what happens when every candidate weighs zero. `NemesisNav` plays the same role
for distance and reachability — measure over the NavMesh, never with `Vector3.Distance`, in a level
with floors.

**Anything a designer has to tune needs somewhere to see it.** A detection or navigation value that
exists only as a float on `SO_NemesisData` is untunable in practice. The three places that make it
visible are `SO_NemesisDataEditor` (the asset's diagram, case tester and checks), `NemesisGizmos`
(the same ranges drawn to scale against real level geometry) and `NemesisDebugHUD` (live state,
winning rung, belief age, suspicion). A decision with no visible tell — an interception, a flanking
detour — is one nobody can confirm ever happened.

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

### Ambience (`_Project/Scripts/Ambience/`)

Four constant layers plus randomised 3D one-shots, driven by `AmbienceController` in the **gameplay** scene (not `Data` — same choice as `VisionRangeController`).

- **`AmbienceController`** — owns a push/pop stack of `SO_AmbienceProfile`, resolves the mixer routing once and pushes it into each layer (the layers do nothing in their own `Start`). `AmbienceZone` trigger volumes push and pop profiles; innermost wins, exactly like `LightZone` + `VisionRangeController`.
- **`AmbienceBedLayer`** — Layer 1, the factory bed. N crossfade slots so a profile can run **two loops of coprime length** (37 s + 53 s gives a composite period of ~33 min, which is what actually defeats loop detection).
- **`AmbienceDriftLayer`** — Layers 3 and 4 collapsed into one data-driven component: pink noise plus the 17 Hz and 32 Hz drones, each slowly wandering to a new volume target. Never restarted on a zone change; profiles only retarget scales.
- **`AmbienceEventScheduler` / `AmbienceEventPool` / `AmbiencePlacementResolver` / `AmbienceEmitter`** — Layer 2. Weighted tiers, a soft repetition penalty, and hybrid placement: LD-placed anchors preferred, validated random as fallback (`CheckSphere` + `NavMesh.SamplePosition` + `Linecast`, with occluded points snapped to the blocking surface).

**Two rules this system depends on.** Never call `mixer.SetFloat` for anything under Ambience — `AudioManager.SetGameplaySfxBundle` rewrites `AmbienceVolume` whenever the player touches the SFX slider, so per-layer balance lives in the fixed faders of the `Ambience/{Bed,Events,Texture,Sub}` sub-groups and in `AudioSource.volume`. And **never put a limiter or compressor on `Master`**: the inaudible 17 Hz drone is still a large peak signal and would duck the entire mix at its LFO rate.

Volume envelopes use `Time.unscaledDeltaTime` (a fade frozen mid-way by a modal is audible); the event timer uses scaled `Time.deltaTime` plus an `IsPaused` guard (a frozen timer is not). Run `Tools/Audio/Bake Ambience Texture Clips` to generate the noise, drones and placeholders — Layers 3 and 4 need no sourced audio at all.

## Current state — what is and is not wired

The systems below are **implemented but not connected to anything**. Read this before assuming a feature works end to end. Verified against the code — if you fix one of these, delete the line.

**Still not wired:**

- **There is no win condition.** `GameResultManager.ReportWin` has no caller at all — the debug `WinLoseTest.cs` that used to call it (key `I`) was deleted. The only reachable ending is the Nemesis catching you.
- **`PuzzleController.CompletePuzzle()` and `PuzzleReward.GiveReward()` have zero callers.** The per-type controllers and `SequencePanelInteractable` write straight to `PuzzleStateManager` and bypass the generic wrapper entirely. Decide whether `PuzzleController` is the intended layer or dead code before building on it.
- **`SkillCheckController.Open()` has zero callers**, `OnFailed` is never invoked, and the model has no fail-out path.
- **`HubPuzzleController.CheckHubCompletion()` sets a flag and stops** — the cinematic / Floor 3 unlock is a TODO comment.
- **Audio is still thin, but pickups and doors now speak.** `PickupInteractable` falls back to a
  per-category `pickupSoundId` on `SO_ItemCategoryConfig` when its own field is empty — which it is
  on every prefab, so before that every pickup in the game was silent. `DoorInteractable` emits from
  `AnimateOpen`/`AnimateClose`, the single point both the player's `OpenDoor` and the Nemesis's
  `TryOpenForNemesis` pass through, so **hearing the monster open a door is a real tell**; hung off
  `OpenDoor` it would have stayed silent for the one case it exists for. `SO_SoundData` now carries
  `minDistance` / `maxDistance` / `rolloff`, applied by `PlayInternal` for positioned sounds — pooled
  sources are created in code and otherwise inherit Unity's `maxDistance` of 500, which is audible
  across the level and makes distance useless as information. Defaults match Unity's, so no existing
  clip changed. Still missing: footsteps, UI audio, and clips for `NemesisAudio` /
  `NemesisChaseMusic`; the **ambience system** (`_Project/Scripts/Ambience/`) is built but ships with
  placeholder clips.
- **Audio does not respond to pause.** `MasterMixer.mixer` has the eight buses but only the default snapshot, and `NemesisChaseMusic.Update()` runs on `Time.unscaledDeltaTime` without an `IsPaused` guard — so chase music keeps playing over the pause menu. Needs a `Paused` snapshot driven from `PauseManager.OnPauseStateChanged`.
- **Save/load is a stub.** `SaveSlotsController` logs and raises an event; `InventoryManager.RestoreFromIDs` has no callers. `PuzzleStateManager.Snapshot()`/`RestoreSnapshot()` exist and work, but only in memory, for checkpoints — there is no disk format.
- **`EPlayerState.InDanger` was removed**, along with its `isInDanger` field and the `T` debug key — it was never registered in the state dictionary, so transitioning to it only ever logged an error. `PlayerHiddenState` is still inert (no collider/visibility change) and the `R` (hidden) and `Y` (disabled) debug keys are still live in `PlayerStateManager.InputUpdate`; `R` goes away when `HidingSpotInteractable` lands.
- **The two parallel grab/push implementations are resolved.** The physical-box version stayed (`GrabbableBall` + `PushBoxTriggerLogic` + `BallPuzzleItem` + `BasketTrigger`); `ContainerInteractable`, `ContainerSlot` and the dead `PushableBall` were deleted, so nothing competes for the `SetContainerSlot` keys any more. That key is a **BallId** — `SO_ContainerPuzzleData.ContainerRequirement.containerId` keeps the old field name but is authored with a ball id. `GrabbableBall` now carries its `PauseManager.IsGameplayInputBlocked` guard, and `BasketTrigger` caches the controller lookup instead of running two `FindObjectsByType` scans per trigger crossing.
- **Three Editor tools were deleted as stale**: `Door/Setup Door Visual` (reflected on `leftPanel`/`rightPanel`, fields the hinged door no longer has — running it disabled the root MeshRenderer and added two stray cubes), `Puzzle UI/Setup Sequence Panel UI` (drove the View through `SetPrivateField`, so every View refactor broke it; the panel prefab is maintained by hand now) and `Scenes/Build Testing Blockout`. `WinLoseTest.cs` and `TestSceneBuilder.cs` went with them.
- **`SO_NemesisData.patrolWaitVariance` ships at 0**, so the wait at every patrol waypoint is still
  the same length every time and a player who has timed one round has timed them all. It defaults
  off on purpose — the variance is expressed as +/- seconds around the authored wait, and no code
  default can know what that authored value is without retuning existing assets. Set it to about
  `0.6` to switch the feature on.
- **The hiding system does not exist.** `EPlayerState.Hidden` is registered, `PlayerHiddenState` is
  inert, and the only way into it is the `R` debug key. The *Nemesis* half is done — vision is
  blinded by `IsHidden`, extreme proximity still detects — so what is missing is entirely on the
  player and level side: the interactable, the interior cameras, the input lock and the breathing.
  See *Hiding spots*.
- **The obstacle system does not exist.** No `ClimbableObstacle`, no `SO_ObstacleData`, no vault
  animation, no push/clear interactable. Crouch-only passages already work through the real capsule
  and `HasHeadroomToStand()`; everything else in the spec is unbuilt. See *Environmental obstacles*.
- **The magnetic door does not exist.** `InventoryManager.HasMetallicItem()` has no callers, so
  `SO_InventoryItem.IsMetallic` is authored data nothing reads yet.
- **The inventory audio player is switched off.** `ItemDetailView.enableAudioFeatures` is `false`
  and `CloseInventory`'s `StopAudio()` is commented out — turn both on together, or a recording
  keeps playing over the gameplay scene.
- **`SO_InventoryItem.TargetID` has no reader.** Doors and sockets reference the item asset
  directly; the id-based wiring the spec describes was never built.
- **The Nemesis does not escalate per module.** There is no `SetDifficultyLevel`, so the whole of
  Nemesis spec §7.2 (speed, ranges, search timeout and route count rising with each module) is
  unimplemented. The mechanism it should use — a runtime `ScriptableObject.Instantiate` copy pushed
  through `FieldOfListening.SetData` and friends, never a write to the asset — is already in place.
- **There is no capture cinematic.** `NemesisCatchState` runs its phases and `CaptureFadeView`
  fades to black; the rest of the chain (checkpoint, penalty, grace period, reposition) is wired.

**Wired since this section was last written** — kept here because the old text said otherwise and people still quote it:

- **Module timers do run.** `ZoneTrigger` → `ModuleManager.ActivateModule`, and `PuzzleStateManager.OnPuzzleCompleted` → `HandlePuzzleCompleted` → `ResolveModule` closes the loop. Explosion, penalty and `BlindnessOverlayView` all fire.
- **Retry does reset run state**, through `GameSession.BeginNewSession()` and `ISessionResettable`.
- **`SO_NemesisMovement` is fully consumed.** `NemesisLifecycle.ApplyMovementTuning` applies `AngularSpeed`, `Acceleration` and `StoppingDistance`; the four state speeds are all assigned. `InvestigationTimeOut` and `NoiseUpdateCooldown` are both read.
- **Nemesis trigger overrides no longer throw** — they are empty, which is correct: the FSM is driven by the sensors, not by triggers.
- **Transitions no longer live inside the states.** `NemesisDecision` decides, the states only act. Anything in this file describing a state as "transitioning to" another is out of date; look at `SO_NemesisPriorities` instead.
- **Vision is gradual in the peripheral band**, chasing predicts and can route through a waypoint to open an angle, and searching rolls its target from the last known position rather than sweeping outward from its own feet. All three are on by default.
- **`DoorInteractable` no longer has a blocking-collider path at all.** Whether the Nemesis can pass is decided by `nemesisCanOpen` plus the NavMesh (a `NavMeshObstacle` with Carve), not by toggling colliders.
