# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**WIRED** — a Unity 6 (6000.x LTS) third-person survival horror game with a PSX aesthetic. Render pipeline: **URP 17.4.0**, Compatibility mode. All game scripts live under `Assets/_Project/Scripts/` (formerly `Assets/Scritps/`, with the typo — renamed during the asset reorganization).

## Assets layout

`Assets/` has exactly five top-level entries, and new content must go into one of them:

- **`_Project/`** — everything the team authors. `Art/` (`Animations`, `Fonts`, `Materials`,
  `Models`, `Textures`), `Audio/`, `Input/`, `Prefabs/`, `Scenes/`, `ScriptableObjects/`,
  `Scripts/`, `Settings/`. The leading underscore keeps it pinned at the top of the Project window.
- **`ThirdParty/`** — imported packs, each kept exactly as it shipped so a future re-import
  from the Asset Store overwrites cleanly. **Never edit or reorganize a pack in place.** If you
  need a variant of a pack asset, copy it into `_Project/` and change the copy.
- **`_Archive/`** — kept but not part of the game: crash-recovery scenes (`Recovery/`) and old
  screenshots. Nothing here should be referenced by a shipping scene.
- **`Resources/`** and **`TextMesh Pro/`** — Unity resolves both by folder name; do not move them.

Where imported content goes (the folders drifted twice; these are the rules the 2026-09-25 cleanup applied):

- A pack with its own folder tree (Asset Store, a `.unitypackage`) imports at the `Assets/` root by
  default: move its folder into `ThirdParty/` right after importing.
- A single downloaded model (Sketchfab and the like) goes to `_Project/Art/Models/Downloaded/<name>/`
  with the `source/` and `textures/` it came with. Prefabs built from it go to `_Project/Prefabs/`.
- `Prefabs/` holds prefabs only, and `Materials/` holds materials and shaders only. Loose models go
  to `Art/Models/`, textures to `Art/Textures/`, materials to `Art/Materials/`.
- Exception: a texture that an FBX's embedded material finds **by file name** at import time stays
  next to that FBX. Moved elsewhere, a re-import (any fresh `Library/`) can come out untextured.
  Example: `Art/Models/Environment/LampBaked1k.png`.
- Unity drops crash-recovery scenes in `Assets/_Recovery/`: move them to `_Archive/Recovery/` or
  delete them. Never commit them at the root.
- Always commit an asset together with its `.meta`, and never regenerate a `.meta`: the GUID in it
  is what every reference points to. Almost every broken reference found in the cleanup came from
  assets created before 2026-06-07, when `.meta` files were git-ignored.

Scenes live in `_Project/Scenes/` under `Bootstrapper/`, `Data/`, `GameScenes/`, `UI/`, and
`Dev/` (test and sandbox scenes). Editor tooling that hardcodes an `"Assets/..."` string —
`PlayModeStartSceneSetter`, `SO_PostProcessToggle` — must be updated whenever one of these folders moves; asset **references**
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
- `docs/Nemesis-System.md` — the Nemesis for designers and level artists: activation, senses, routes, audio, tuning, how to verify
- `docs/UI-System.md` — UI architecture, MVC pattern, scene lifecycle, pause system
- `docs/Ambience-System.md` — ambient audio: the four layers, mixer setup, zone profiles, verification
- `docs/Materials-System.md` — shaders, vision fog, item highlight, flicker scripts
- `docs/TODO-UI.md` — deferred UI work (inventory, save slots, settings tabs, puzzles)
- `docs/SaveSlots-Setup.md` — manual Unity Editor steps to wire up the SaveSlots screen

## Design specs

Nine GDD documents define the intended scope of the systems below. They are team documents (Docs /
Drive), not files in this repo. They were written at different moments against different states of
the code, so read them with one rule: **where a spec and this file disagree about how something
works *today*, this file wins; where they disagree about what the feature is *meant to be*, the
spec wins.**

| Spec | Version | Where it stands in code |
|---|---|---|
| Inventory System | v2.0 | Built. Gaps: the audio player and the item catalogue — see *Inventory* |
| Player System | v1.1 | Built, and **deliberately divergent** — there is a sprint the spec forbids. See *Spec deltas — Player* |
| Interaction System | v1.0 draft | Built for Variant A. Variant B is an unopened skeleton — see *Spec deltas — Interaction* |
| Nemesis System | v1.0 draft | Built, and well past the spec. The spec is the document that is behind — see *Spec deltas — Nemesis* |
| Audio System | v1.1 | **Routing built, content thin.** 16 of the ~90 clips on disk are reachable — see *Spec deltas — Audio* |
| Music Spec | v1.1 | 1 of 7 pieces. No `MusicManager` — see *Spec deltas — Music* |
| Light System | v1.0 draft | Built as something else: the **vision fog**, not the spec's light model — see *Spec deltas — Light* |
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
| `NemesisController.SetDifficultyLevel(n)` | **Does not exist.** `NemesisEscalation` (Data scene) installs the tier for the **completed puzzle count**, not a module count: `ModuleManager` is the device timers and never advances the story. Nothing calls it; it re-reads the count by itself — see *Nemesis: escalation* |
| `visionRange` / `hearingRange` / `proximityDetectionRange` | `SO_NemesisData.ViewRange` / `ListenRange` / `ProximityDetectionRange` — plus `FocusAngle` and a peripheral awareness band the spec predates |
| "state X transitions to Y" | States never decide transitions. `NemesisDecision` + `SO_NemesisPriorities` do — see *Nemesis: the decision layer* |
| "the Hub blocks the Nemesis" (in code) | A NavMesh `Not Walkable` modifier volume. There is no C# side — see *Safe zones* |
| `ModuleManager.GetActiveModuleTimeRemaining()` / `GetActiveModuleTotalTime()` | `GetActiveModule()` returns the `ModuleRuntime`; it already exposes `TimeRemaining`, `TimerProgress` (the bar fill the spec computes by hand), `FormattedTime` and `BarColor`. The total is `Data.TimerDuration`. `GetExplodedCount()` exists exactly as specified |
| `AudioManager.PlaySFX(AudioClip, pos)` — an `AudioClip` argument | Every `Play*` takes a **string id** that must resolve to an `SO_SoundData` asset dragged into the `AudioManager.sounds` array. A clip sitting in `_Project/Audio/` with no SO is unreachable; a wrong id logs a warning and plays nothing |
| `IInteractable.GetPromptText()` | `GetPromptText()` (the same name since 2026-09-25; it was `GetInteractText()`) **plus** `GetInfoText()` — the second is exactly the spec's §6.1 "Necesitas X" informative prompt, and it already exists |
| `IInteractable.OnInteract(PlayerController player)` | `Interact()`, no argument. Interactables reach the player through `PlayerRegistry` and the manager singletons |
| `PlayerController.OnDangerDetected()` | **Does not exist**, and neither does the `InDanger` state it would set. The danger *feedback* survives as `VignetteProximityView` / `VignetteChaseView`, driven straight off `NemesisEvents` |
| `MusicManager` (`SetZone`, `PlayChaseMusic`, `OnEnterHiding`, `OnPuzzleResolved`, stinger source) | **Does not exist.** `NemesisChaseMusic` covers the chase cue only; `AudioManager.PlayMusic(id)` owns one 2D source and has **zero callers** |
| `AmbientManager` / `DuckAmbience` / `RestoreAmbience` | `AmbienceController` + `AmbienceZone`. The duck hooks are `FadeOutAll` / `FadeInAll` (already used by `NemesisChaseMusic`) and `SetTensionScalars` (no callers) |
| `ZoneTracker.OnPlayerZoneChanged` / a `ZoneType` enum | **Does not exist.** The project has no notion of "which zone is the player in". The nearest equivalents are two independent trigger push/pop stacks: `AmbienceZone` → `AmbienceController` and `LightZone` → `VisionRangeController` |
| `FootstepSystem` (player or Nemesis) | `FootstepEmitter` + `SO_FootstepBank`, one component on both walkers — see *Footsteps and breathing*. The spec's `footstepInterval = distance / speed` is not how either of them is cadenced today: both run on `AnimationEvent` |
| `AudioMixerSnapshot` (`Paused`, `InHiding`, `NemesisMuffled`, `NemesisClear`) | `MasterMixer.mixer` has exactly one snapshot, the default. Pause ducking is `AudioManager.PauseDuck`, a global multiplier; Nemesis occlusion is `NemesisAudio.occludedVolumeMultiplier`, eased per source. Neither applies a lowpass |
| `NemesisController.PlayVoiceLine(VoiceLineType)` | **Does not exist.** `NemesisAudio` crossfades one looping clip per state and nothing else |
| `LightManager`, `ZoneLightController`, the generator | **Do not exist.** No light in the project can be switched on by the player |
| `AdaptationController` / `adaptationProgress` / `darkThreshold` | **Do not exist.** Standing still buys the player nothing today |
| per-zone `lightLevel`, `playerVisibilityFactor`, `encendedorContribution` | **Do not exist.** `FieldOfView` has no light term at all — darkness does not shorten the Nemesis's sight and a lit room does not lengthen it. The per-zone knob that *does* exist is `SO_VisionFogConfig`, and it drives what the **player** sees, not what the Nemesis sees |
| "the device's light" / "el encendedor" | `FogLightSource` on the player's amber `Light`, read by `VisionRangeController` to punch a hole in the vision fog |

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
distance checks, and detection being all-or-nothing. Its difficulty escalation (§7.2) is built with
three deltas — by puzzles, never speed, search time untouched — see *Nemesis: escalation*. Things it
asks for that are **still missing**:

- **A capture cinematic (§5).** `NemesisCatchState` plays out phases and `CaptureFadeView` fades;
  there is no cinematic. Everything else in the capture chain is wired. Nor is there a capture
  **stinger** or an activation cue — §5.5 and §7.1 both ask for one and neither point makes a sound.
- **`underTableVisionMultiplier` (hiding spec §3).** No field, no reader — see *Hiding spots*.

**`NemesisStateManager.BaselineData` is a trap that has already been disarmed once.** The Director's
sensory boost is a *loan*: widened senses installed for the length of a pressure request and handed
back. It used to cache the first `SO_NemesisData` it ever saw and restore that, which is correct
only for as long as nothing changes the Nemesis's tuning permanently. Escalation is exactly such a
change, and against a cached restore target the first pressure request after a puzzle would silently
revert the whole progression — with the monster still behaving, so nothing would look broken. Both
ends of the loan read `BaselineData` fresh, and since plan Fase 7 it really moves:
`NemesisEscalation` swaps it through `InstallBaseline`, which raises `NemesisEvents.OnBaselineChanged`
so the Director rebuilds a loan cloned from the old one. **Do not reintroduce a cached restore
target**, and scale from `AuthoredData`, never from the current baseline.

### Spec deltas — Player

The player spec is the one where **the code deliberately left the spec behind**, so read the
divergences as decisions, not as bugs to fix back:

- **Sprint exists, and the spec forbids it.** Spec §1.2 lists "correr (sprint)" among the
  restrictions that "no deben implementarse". The project has `SO_Movement.SprintSpeedMultiplier`
  (1.5), a `Sprint` action in `InputSystem_Actions`, `CameraSprintEffect`, a third noise radius
  (`runNoiseRadius` 6, against walk 2 / crouch 1) that `NemesisGizmos` draws to scale — **and the
  M2 module penalty is defined as a sprint reduction** (`SprintPenaltyFactor`). Removing sprint now
  would delete one of the three module penalties. The spec is the document that is behind.
- **Third-person orbital camera**, which is what spec §4 asks for: `CinemachineOrbitalFollow` plus a
  Deoccluder plus a pivot that drops on crouch (`SO_CameraConfig.CrouchPivotDrop`). The spec's
  `shoulderOffset`, `maxVerticalAngle` (80) and camera-wall raycast are all present; `fov` ships at
  72 rather than the spec's 70, and `crouchSpeedMultiplier` at 0.45 rather than 0.6.
- **Crouch is not a plain toggle.** Standing up is gated by `HasHeadroomToStand()` against
  `standBlockMask`, with a `wantsToStand` latch honoured the frame the ceiling clears. A C press
  under a duct does nothing, on purpose. The same rule covers a *lock*: a plain `IsDisabled = true`
  sends the FSM through Disabled, whose way out of Crouch stands the capsule up into the ceiling
  and the player sinks through the floor. `ModuleExplosionSequence` therefore locks with
  `DisableKeepingCrouch()`: with no headroom the FSM stays in Crouch, frozen
  (`HoldsCrouchPose`), and control returns crouched; with room it is exactly `IsDisabled = true`.
  Captures, the Architect lines and the escape sequence still use the plain lock.
- **`InDanger` was deleted** (spec §7 lists it as a state). It was never registered in the state
  dictionary. Its feedback half survives in the vignette views.
- **Movement is Rigidbody + CapsuleCollider**, not `CharacterController` — see the vocabulary table.

Matching the spec without drama: `moveSpeed` 3.5, `acceleration` 8, unlimited inventory, capture →
checkpoint against three modules → Game Over as two different endings, ESC always live, and the
interaction/inventory input locks.

**Missing from the player spec:** everything in §10.2 that is audio (footsteps, breathing) and the
`Hidden` half of §8.3.

### Spec deltas — Interaction

Built to spec, and in one place stricter than it:

- One `E`, a 0.2 s cooldown (`InteractionManager.InteractCooldown`), exactly one prompt on screen,
  `CanInteract()` / `IsRepeatable()` honoured, and the four simple types (recoger, activar,
  inspeccionar, abrir puerta) all shipping.
- **Target selection does not use the spec's dot-product rule.** `InteractionProbe` fires a sphere
  cast through the crosshair's own viewport point, measures reach **from the player** rather than
  from the lens, and judges occlusion separately, with a thin solid-only line of sight, so
  interaction volumes may stay triggers. On a rig whose camera orbits ~3.4 m behind the character,
  the dot-product rule picks the wrong object. Do not replace this with the spec's version.
- **Variant A ships** (`SocketInteractable`: E plus the item in the inventory = immediate insertion).
- **Variant B is a skeleton nothing opens.** `LateralInventoryView` renders the item list and raises
  a selection event; the camera pan to `puzzleCameraPoint`, the `Interacting` lock, the ESC cancel
  and the Nemesis interrupt are all absent — its own class comment lists them as pending. No puzzle
  in the project requests it.
- **Spec §8's specials route into systems that do not exist**: hiding, climbing, the window vault,
  the shelf push and the sync station. Of that list only the elevator panels are real, and they are
  not in the spec at all.

### Spec deltas — Audio

The **plumbing matches the spec closely; the content does not reach the game.**

Built as specified: the eight-bus mixer (`Master > Music, Ambience, SFX, Player, Nemesis, UI,
Voice`), an `AudioManager` singleton with one entry point per bus, `PlayLoop(id, source)` for
externally-owned loops, `spatialBlend` 1 for anything played at a position and 0 otherwise, and
`ignoreListenerPause` forced on UI and Voice so menu clicks survive the pause.

**The content gap is the headline.** `_Project/Audio/` holds roughly ninety clips — footsteps for
six surfaces, five breathing loops, the whole module set (tick normal and urgent, activation,
explosion, damage loop, resolve, game-over, device hum, M2 chest vibe), Nemesis breathing and voice,
generator and flicker audio, the save-point set, the UI set. **Sixteen `SO_SoundData` assets exist.**
Everything else is a file on disk that no id resolves to and no call site asks for. Authoring the SO
is the cheap half of the work and it is the half that is missing, not the recording.

Where audio actually fires today: doors (from `AnimateOpen`/`AnimateClose`, so the Nemesis opening a
door is audible), notes, pickups (with the `SO_ItemCategoryConfig` fallback), sockets, valves, the
sequence panel, the elevator, the sub-puzzle completion sting, and one UI click. That is the whole
list.

**Footsteps and hidden breathing are built** — see *Footsteps and breathing* under Architecture.
That covers `PLY_01`–`PLY_07`, `PLY_08`/`PLY_11` and `NEM_01`/`NEM_02`.

**Missing outright, in spec-table order:** `PLY_09`/`PLY_10` (the hold-breath input and its
involuntary exhale, which belong to the unbuilt hiding system); `NEM_06`–`NEM_13` voice lines (there
is no `PlayVoiceLine`); every `MOD_*` call site but the countdown tick — the module system runs its
activations, explosions, penalties and resolutions **silently**, even though all nine clips exist; every `TRP_*` (the traps
themselves do not exist); every `LUZ_*` — `FlickerLight`, `MonitorFlicker` and the generator have no
`AudioSource` between them; every `SAV_*`; and `UI_01`–`UI_06` apart from the panel click, so the
menus are silent.

Deltas in mechanism rather than in scope:

- **Pause.** The spec wants a `Paused` snapshot with a lowpass on everything but UI. What exists is
  `AudioManager.PauseDuck`, a global multiplier that `AudioBackgroundApplier` fades and that exempts
  UI. Same intent, no lowpass, and `NemesisChaseMusic` still ignores it.
- **Nemesis occlusion.** The spec wants `NemesisMuffled` / `NemesisClear` snapshots driven by a zone
  comparison. `NemesisAudio` instead eases a per-source volume multiplier off the same wall raycast
  `FieldOfListening` already runs — attenuation, never a cut, and no zone system needed.
- **`SO_SoundData` carries 3D rolloff, `minDistance` and `maxDistance` per clip.** The spec assumes
  the AudioSource is configured at the call site; pooled sources are created in code and would
  otherwise inherit Unity's `maxDistance` of 500, which makes distance useless as information.
- **Bank content does not go through ids.** `AudioManager.PlayClip` (and the `PlayPlayer` /
  `PlayNemesis` / `PlaySFX` overloads that take an `AudioClip`) plays a clip the caller already
  holds, with per-shot volume, pitch and falloff. The id-based path cannot express any of those:
  `PlayInternal` plays everything at volume 1 and pitch 1. Content drawn from a bank — footsteps
  today — uses the clip overloads; content with one fixed sound per event keeps using ids.

**Past the spec:** the entire ambience system. Spec §11 asks for one loop per zone plus an
`OccasionalSoundPlayer`. `_Project/Scripts/Ambience/` ships four layers, coprime loop pairs to defeat
loop detection, weighted event tiers with a repetition penalty, occlusion-aware 3D placement, an
offline tone baker, and a low-frequency comfort toggle for players sensitive to the sub drones. None
of that is in the spec.

### Spec deltas — Music

**One of the seven pieces exists.** `_Project/Audio/Music/` holds a single chase track, and
`NemesisChaseMusic` plays it off `NemesisEvents.OnChaseStarted` / `OnChaseEnded`.

There is **no `MusicManager`**, and therefore none of the spec's transition table: no `ZoneType`, no
`SetZone`, no exploration pieces for Hub / zone / corridor, no crossfade pair of AudioSources, no
stinger source, no menu music (`AudioManager.PlayMusic` has zero callers anywhere), no ending piece.
`MUS_02`–`MUS_05` and `MUS_07` are unwritten as content *and* unreachable as code.

Two deltas in the one piece that does exist:

- **Fade timings.** Spec §2 wants 0.5 s in and 2 s out with a reversible fade. `NemesisChaseMusic`
  uses a single `fadeDuration` (2 s default) in both directions. The reversal itself works — the
  target volume is retargeted, not restarted.
- **It fades the ambience out under itself.** `FadeOutAll` / `FadeInAll` on `AmbienceController` for
  the takeover. The music spec does not ask for this; the *audio* spec §11 does, as
  `DuckAmbience(0.3, 0.5)`. What ships is a full fade rather than a duck to 30% — a stronger reading
  of the same idea.

### Spec deltas — Light

**The light spec describes a system the project did not build. It built a different one, better
suited to the PSX look — but the two are not interchangeable, and the spec's gameplay consequences
are all missing.**

What the spec asks for: a per-zone `lightLevel`, a device light that contributes to a
`playerVisibilityFactor` the Nemesis reads, visual adaptation earned by standing still, and
generators the player switches on for a timed window of visibility.

What exists instead: the **vision fog**. A fullscreen shader (`VisionRangeController` +
`SO_VisionFogConfig`) whose radius is pushed and popped by `LightZone` trigger volumes, with the
player's amber device Light punching a hole through it via `FogLightSource`. It answers "how far can
the player see here", which is the spec's §5 table by another road.

Mapping, so nobody builds the second copy:

| Light spec | What plays that role here |
|---|---|
| per-zone `lightLevel` | the `SO_VisionFogConfig` preset a `LightZone` pushes |
| the device's light (§3.1) | `FogLightSource` reading the real amber `Light` |
| degradation by module count (§3.1.1) | **scaffolded, no caller.** `FogLightSource` reads the live `Light`, so dropping its intensity shrinks the fog hole for free — but nothing listens to `ModuleEvents.OnExploded` to drop it |
| flickering lights (§2.3) | `FlickerLight` — and it already follows the spec's own advice: a deterministic `AnimationCurve` per instance with a per-instance offset, never `Random.Range` in `Update` |
| monitors (§2.2) | `MonitorFlicker` (emission pulse through a `MaterialPropertyBlock`) |
| red emergency lights (§2.1) | level dressing, no script — which is exactly what the spec asks for |

**Missing outright:**

- **The generator / timed zone activation (§2.4).** No `ZoneLightController`, no `IInteractable`, no
  timed routine, no fade warning. The single most gameplay-bearing item in the whole spec.
- **Visual adaptation (§3.2).** No `AdaptationController`, no `adaptationProgress`, no exposure or
  lift drive on the post-process volume. Standing still is currently worth nothing to the player,
  which also removes the spec's risk/reward beat.
- **Light as an input to detection (§4).** `FieldOfView` has no light term whatsoever. Hiding in the
  dark does not shorten the Nemesis's sight, the device light never gives the player away, and
  `playerVisibilityFactor` does not exist. The vision fog changes what the *player* sees and nothing
  about what the *Nemesis* sees — the spec's central bargain is not in the game.
- **All of `LUZ_01`–`LUZ_07`.** The clips are on disk; no light component owns an `AudioSource`.

**Past the spec:** the fog shader itself, its 16 bypass zones, the linear-colour discipline in
`VisionFogState` (`Shader.SetGlobalColor` does no sRGB conversion, so every write goes through one
method), `VisionFogClip`/`VisionFogTrack` for driving the fog from Timeline, and
`FogLightBypassPlayerFade`.

### What the code has that no spec asks for

Useful to know before "finishing" a spec: a large share of the project is in none of the nine
documents, and none of it should be deleted for failing to match one.

Ambience (four layers, event scheduler, placement resolver, tone baker, comfort toggle) · the vision
fog and its Timeline track · the whole Nemesis director layer (`NemesisDirector`,
`NemesisRouteGraph`, `NemesisPathOracle`, `NemesisFreeRoam`, `NemesisPressureZone`, zone gravitation,
stuck escalation, telemetry, the test console and the editor validators) · the freight elevator and
its own NavMesh · `MovingPlatform` and the carrier hookup · the additive-scene MVC UI framework,
`UIStateManager` and `PauseManager` · the checkpoint system · the PS1 effect and every settings
applier · `SequencePanelInteractable` and its panel UI · `SkillCheckController` (DBD format;
`SkillCheckPanelInteractable` completes `puzzle_central_piso2` and so resolves M2, but no scene
places it yet — F6 opens it for testing) · the ball/basket push puzzle.

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

**Nemesis states**: `Patrolling -> Investigating -> Chasing -> Searching`, plus `Traversing` and the terminal `Catch` (managed by `NemesisStateManager`). `Traversing` means "getting there needs the freight elevator" (or a drop between floors, see *Drops between floors*); it holds that decision open for `SO_NemesisData.ElevatorCommitTime` even with the player out of sight, because a floor slab breaks line of sight for the whole trip and without it the lift ride was abandoned every time. **Which state the Nemesis is in is not decided by the states themselves** — see *Nemesis: the decision layer* below. Detection uses `FieldOfView.cs` (cone + obstacle raycast, polled every 0.1s) and `FieldOfListening.cs`, which occludes sight and sound with *different* masks — a floor blocks sight but only attenuates sound, and that is the Nemesis's only channel to the storey above. Route questions ("reachable? which floor? is the lift on the way?") go through `NemesisPathOracle`, which throttles them; that interval is a stability knob as much as a cost one, since a verdict flipping frame to frame makes the FSM oscillate. `NemesisTelemetry` fires `NemesisEvents.OnChaseStarted/Ended` when entering/leaving the `{Chasing, Catch}` set — `Traversing` is deliberately NOT in it, since the player is a storey away and unreachable — and `OnProximityChanged` every frame from the real distance to the player (`SO_NemesisData.proximityRadius`). Both drive `VignetteChaseView` and `VignetteProximityView` in the HUD. Entering `Catch` also schedules `GameResultManager.ReportLoss` after `captureDelay`.

**`NemesisStateManager` is a facade, not an implementation.** It owns the FSM and the shared references; everything else lives in sibling components on the same GameObject, all auto-added when missing so no existing prefab needs re-saving: `NemesisPathOracle` (throttled route queries), `NemesisTelemetry` (the events above), `NemesisStuckEscape` (no-progress watchdog and its warp out), `NemesisLifecycle` (dormancy, agent tuning from `SO_NemesisMovement`, and every teleport), `NemesisLookAround` (sweeps the gaze while standing still), `NemesisAudio` (the per-state loops — added **last, after the sensors**, for the reason its own entry gives). `NemesisElevatorUser` is resolved with `GetComponent` but deliberately **not** auto-added: unlike the others it is a real feature with scene wiring behind it, and a level with no freight elevator should not silently grow one. The states keep calling `NemesisStateManager`, which forwards — that is what the facade is for. Teleports must go through `NemesisStateManager.WarpTo`, which invalidates the cached route verdict and resets the stuck sample; a warp that skips either leaves the FSM steering from the floor it just left, or the watchdog reading the jump as ground covered on foot. In editor and development builds it also adds `NemesisTraceRecorder`, a debug tool nothing holds a reference to: it writes one CSV row every 0.25 s and one per state change (state, winning rung and note, gait, senses, belief age and source, path pending/status, commanded vs real speed, lift, stall, stuck counters, position) to `Logs/NemesisTrace/` (persistentDataPath in a dev build) — read it after a playtest instead of reconstructing the frame from memory.

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
| `NemesisEvents.OnSearchEnded` | NemesisTelemetry | PlayerHabitTracker |
| `NemesisEvents.OnChaseStalled` | NemesisChaseProgress | PlayerHabitTracker |
| `NemesisEvents.OnBaselineChanged` | NemesisStateManager.InstallBaseline | NemesisDirector |
| `HidingEvents.OnEntered/Exited` | HidingSpot | NemesisHidingAwareness (entered only), HidingOverlayView, PlayerHabitTracker |
| `InteractionEvents.OnTargetChanged` | InteractionManager | InteractionPromptView |
| `InteractionEvents.OnGlobalMessage` | any system, via `RaiseGlobalMessage` | InteractionNotificationFeed |
| `InventoryEvents.OnItemAdded/Removed/Consumed` | InventoryManager | InteractionPromptView, InteractionNotificationFeed, ModuleHUDView |
| `UIStateManager.OnModalPushed/Popped` | UIStateManager | (subscribers as needed) |

**Subscribe in `Awake`, unsubscribe in `OnDestroy`** — never in `OnEnable/OnDisable` for static events, as the delegate outlives the GameObject's enabled state.

### Gameplay Input Guard

```csharp
// In any Update() that reads gameplay input:
if (PauseManager.IsGameplayInputBlocked) return;
```

`IsGameplayInputBlocked` is `true` when the game is paused OR when any `IModalUI` is open (`UIStateManager.IsAnyModalOpen`). `Time.timeScale = 0` stops physics but does NOT stop `Input.GetKey*` — the guard is required for all logical input.

### Interactable System

`IInteractable` (`_Project/Scripts/Interfaces/IInteractable/IInteractable.cs`) defines `CanInteract()`, `Interact()`, `IsRepeatable()`, `GetPromptText()`, `GetInfoText()`.

Detection is a **crosshair SphereCast**, not trigger registration, and all of it lives in `InteractionProbe` (`_Project/Scripts/Interactables/InteractionProbe.cs`), shared by `InteractionManager` and the Scene-view `InteractionRangeGizmo`. The cast goes through the crosshair's viewport point but **starts at the point of that line closest to the player's chest**, and reaches `SO_InteractionManager.InteractionDistance` from there. It runs in steps:

- **Candidate**: the thick ray (`CastRadius`) against `InteractableLayers` only, triggers included, so aiming at small items stays forgiving. The nearest collider that resolves to an `IInteractable` (on itself or a parent) wins.
- **Line of sight**: a **thin** raycast against `BlockingLayers` (solid only) from the start to the point the thick ray touched. Judging it with the thick ray made the surface an item rests on hide the item. Never hiding the candidate: its own solid parts, and its **support** — a solid met within 3 cm of the aimed point, or a convex/primitive collider that holds the candidate whole (a key inside a toilet's convex MeshCollider), or a prop on `SupportLayers` (Props) that **touches** the candidate (a corner or the centre within 5 cm of it; a concave mesh falls back to its bounds overlapping): the barrel a fuse stands in, the crate a key lies on. The same prop with a gap before the item hides it, so a crate blocks a key on its far side. A wall between the player and a panel on its far side still hides it. A solid *interactable* in front (a crate, a door leaf) replaces the candidate.
- **Legacy layout**: with no candidate, the thick ray against `BlockingLayers` resolves interactables whose own collider is solid (door leaves, push boxes on Default). The same pass makes a solid interactable the player is pressed into win over what lies past it.
- **Close range**: with nothing ahead, the last `CloseRangeLead` metres before the player.

`Props` is in `BlockingLayers` (6153, the shared occlusion mask `NemesisSetupValidator` checks) and in `SupportLayers` (4096). From `9330589e` (22/09) until 03/10 it was left out of `BlockingLayers`, which made crates see-through: a key behind a crate could be taken. Now a prop blocks whatever is behind it but never the item it touches, so **a prop that holds a pickup must be on the Props layer** (set it on the prefab instance; walls are not support layers on purpose, a panel in a wall stays hidden from the next room). Barrels `Barrel_3`, `Barrel_3 (1)` and `Barrel_1 (11)` carry that override. `BaseRangeInteractable` only describes *what* the interaction is. Each interactable needs a Collider on itself or on a child in the Interactable layer so the cast has something to hit. When something is not detected, `InteractionRangeGizmo` (Show Hit Point) labels the candidate, the line of sight, what blocked it and what was skipped as its support, each with its layer.

The manager fires `InteractionEvents.TargetChanged(interactable)` when the target changes. Key `[E]` is processed in `InteractionManager.Interact()` with a 0.2s cooldown.

Selection is the **nearest candidate along the crosshair line**, with no dot-product priority when several interactables overlap — see `docs/TODO-UI.md` · Interaction Prompt.

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

**Time jumps.** Besides the countdown, the active timer only moves through two calls, both no-ops
with no module running: `ApplyTimePenalty(seconds)` (clamped at 0 — the tick explodes it on the next
unpaused frame) and `ApplyTimeBonus(seconds)` (capped at `TimerDuration`, so it can never revive an
exploded module). Both raise `ModuleEvents.OnTimeAdjusted(module, delta)` with the delta actually
applied — it also fires while the timer is paused, where no tick would follow to show it.

**On screen.** Outside the inventory the countdown is read off the player's camera feed
(`PlayerCameraFeed`, burnt into the picture bottom left): `00:00` until a module starts; its start
types the line in at 00:00, counts the time up to the module's and hands over to the countdown
(`M1:IN PROGRESS  14:35`), once per module. With none running it keeps the last one that ran
(`DISARMED` / `FAILED`). Its time turns amber with a quarter of the module's time left, red with a
tenth, and blinks red in the last 30 s (`FAILED` stays red). The old top-left Win95
window (`ModuleTimerHUDView`) was removed on 2026-09-26 at the designer's request; see
`docs/Materials-System.md` §7.3. `ModuleTimerBeeper` stays in `HUDCanvas.prefab` (object
`ModuleTimerBeeper`): it beeps from the start of the module, slow at first and quicker at every stage
(`BeepCadence`, `Scripts/Utils`, pure and tested): every 30 s shrinking to 10 s at the readout's amber,
10 s → 5 s at its red, then 5 s → 0.5 s exponentially up to the last 10 s, which hold 0.5 s with
`sfx_modulo_tick_urgente` (the rest use `sfx_modulo_tick_normal`). Amber and red come from
`SO_PlayerCameraFeed` (`WarningSecondsLeft` / `CriticalSecondsLeft`, shared with `ModuleTimerStage`), so
colour and beep change pace together. It runs off `OnTimerTick`, so it goes quiet by itself whenever the
timer is paused.

### Capture, checkpoints and session reset

A capture is a **cost, not a Game Over**. The chain is deliberately one-directional so no system
reaches into another:

```
NemesisCatchState -> PlayerStateManager.OnCaptured() -> PlayerEvents.OnPlayerCaptured
                 -> CheckpointManager (subscribed)   -> respawn + PuzzleStateManager.RestoreSnapshot
                                                     -> ModuleManager.ApplyTimePenalty
                 -> CheckpointManager.OnRespawned    -> NemesisStateManager (subscribed)
```

**A capture costs no module time** (design decision): `SO_PlayerMovement.captureModuleTimePenalty`
is 0, so `ApplyCaptureCost` returns early, and the timer is frozen from the grab until the player is
back on their feet (`PlayerStateManager.captureTimerPaused`). Raising the value brings the cost back
without code changes.

The Nemesis never calls into save or UI; it only raises and only listens. `Checkpoint` activates by
physical trigger, by puzzle id, or either, once only — and **snapshots puzzle progress at
activation, not at capture**, because the point of the rollback is to undo whatever you did after
the last safe moment.

`GameSession.BeginNewSession()` is the New Game / Retry reset, called from `MainMenuController` and
`ResultScreenController`. Persistent managers implement `ISessionResettable` and register in
`Awake`; statics subscribe to `GameSession.OnNewSessionStarting`. **Adding a new stateful manager
means implementing that interface — not editing the menu controllers.**

#### The grab (the capture's cinematic)

The capture is seen before it is covered. `E_KillPlayer` (the Nemesis's `Catch` animator state) and
`Grabbed` (the player's) are **one animation in two halves**: animated with the player 1.69 m in
front of the Nemesis, facing it, both clips starting on the same frame. Three pieces on the Player
prefab's root make a real capture match that, all reading `SO_CaptureGrabConfig`
(`ScriptableObjects/Player/`):

- **`CaptureGrabStaging`** closes the gap between where a capture happens (anywhere inside
  `CatchMaxReach`, 1 m, facing wherever the player was running) and where the pair was animated. Over
  `AlignSeconds`, before the hands close at 0.63 s: the Nemesis backs off along the line between the
  two as far as its NavMesh has room, the player is moved for whatever it could not give (over the
  NavMesh too, so nobody ends up in a wall or off a ledge), and the player's **model** turns to face
  it. It crossfades the player into `Grabbed` on the frame the Nemesis's Animator goes into `Catch`
  (0.15 s, the same as that controller's Any State → Catch), which is what keeps the two clips in
  step. Distances are measured from the Nemesis's **animated model**, not its root: a scene that
  offsets the model under the root (the test bed did, by 1.31 m, until 03/10) would otherwise leave
  the hands that far short.
- **`CaptureGrabCamera`** is the shot: a `CinemachineCamera` spawned on the output camera's pose
  (priority 500; the escape's shots and the defeat camera sit at 1000) that orbits to a spot **chosen
  by what it sees**. Every candidate — five angles round the pair on both sides, four distances,
  three heights — is scored on five points of the grab (the Nemesis's two wrists, the player's head
  and chest, the Nemesis's head): no level geometry between the point and the camera, and the other
  body not in the way. Sight lines are cast from the grab outwards (a collider is not hit from
  within, so cast from a camera inside a wall they all come back clear), against the gameplay rig's
  own `CinemachineDeoccluder` mask plus the doors' layer. It holds `SO_CaptureGrabConfig.FogPreset`
  on the vision fog's stack while it is up, only where that opens the view.
- **`PlayerEvents.CaptureShotSeconds`** is how long the grab stays on screen. A standing value that
  `CaptureGrabCamera` holds while enabled, not something written at the grab, so every listener of
  `OnPlayerCaptured` reads the same number whatever order the event reaches them in.
  `CaptureFadeView`, `CheckpointManager` and `EscapeChaseRestart` each wait it out **before** their
  own delay, which keeps the gaps between them as tuned (the cover is closed 0.9 s before the
  respawn). With no camera in the scene it is 0 and the capture is covered at once, as before. With
  a shot, `CaptureFadeView` drops the chase and proximity vignettes at the grab instead of at black.
- **`CaptureCanvasGate`** (on `CrosshairCanvas`, in the `LevelUI` scene) takes the crosshair off the
  screen for the whole capture — `IsRecoveringFromCapture`: the grab, the shot, the black cover, the
  respawn and the stand-up. That canvas sorts at 1000, above the HUD's, so the dot sat in the middle
  of the shot and on top of the black. It switches the **Canvas** off rather than fading the
  `CanvasGroup`: that alpha belongs to the object's `ModalVisibilityGate`, and two writers of one
  alpha flicker. Polled off `PlayerRegistry`, like `SafeZoneAlert`: there is an event for the grab
  and none for control coming back.

`Tools > Player > Setup Capture Grab` builds all of it and is safe to re-run: it writes
`Grabbed (Player Rig).anim` from the `E_Grabbed` take of `Player.fbx`, adds the `Grabbed` state to
`PlayerController` (no transitions out), measures the pair off the two clips — **the distance is
the reach of the Nemesis's wrists in the hold pose at the prefab's scale, plus `GripStandoff`** — and
puts the two components on the prefab. Run it again after changing the Nemesis's scale or either clip.

**Never put the raw `E_Grabbed` take in `PlayerController`.** Its root node carries the placement
it was animated at (1.69 m forward, turned round, rising 25 cm), and a clip with root-path curves
anywhere in a controller makes the Animator own the model root's transform in *every* state: the
moving states' `PlayerBody.forward = …` is overwritten each frame and the player stops turning.
Measured: model yaw set to 90, one Animator update later it reads 0 with the raw take in an unused
state, 90 with the rebuilt clip. The rebuilt clip drops the root curves and folds the rise into the
Hips, the same rule `PlayerStandUpSetup` applies.

`NemesisCatchState` does its own half only: it stops the agent, turns both to face (the player's
**model**, not its root — the root never rotates in play, so turning it added the two headings) and
sets the Grabbing gait from `Capture()` and nowhere earlier. The pull-out of a hidden player stands
in the Idle gait for that reason: started there, the clip ran `HiddenPullOutTime` ahead of the
player's.

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
  Rows carry two marks, state in `InventoryManager` (`IsNew`/`IsRead`, session only, not saved):
  an amber `NEW` before the category tag from pickup until the item is selected, and a grey text and
  icon for a `Note` already read (its doc opened; a note with no doc is read by being selected).
  `InventoryManagerUI` sets both and calls `InventoryView.RefreshMarks`.
- `ItemDetailView` fills header, description and metadata, and owns the **doc panel** for
  `ContentType.Text` items (its own layer, reset to the top of the scroll on each open).
- `DiscardDialogView` confirms. `RequestDiscard` only opens the dialog; `InventoryManager.DiscardItem`
  is the only thing that removes anything, and it is called on confirm alone.
- `ModuleHUDView` + `ModuleRowView` + `ActiveModuleTimerView` + `FailuresPipsView` are the device
  HUD of spec §3, inside the inventory: they subscribe to `ModuleEvents` and read `ModuleManager`
  (`GetActiveModule()`, `GetExplodedCount()`) rather than polling, and they keep counting with the
  inventory open because the timers tick on `Time.unscaledDeltaTime`. `ActiveModuleDisplay` drains
  the circle around the timer (`radialFill = TimerProgress`) off the same events; before, nothing
  called it and the circle sat full.

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
  goes to the inventory, is put in front of the player on the spot by `DocumentReaderController`
  (reading mode: the game freezes until they dismiss the sheet), and can be re-read forever from
  the inventory's doc panel; a `NoteInteractable` opens the same reader with an `SO_DocumentData`,
  with the world still running, and never enters the inventory. Spec §11 wants the notes to be held
  items, which is the first path — and it is the only one any note in the project actually uses.
  Choose one per piece of paper — wiring both means the same note exists twice with two different
  texts to keep in sync.

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

> **Stale since 21/09: this section predates the build.** Phases 1–2 of `docs/Plan-IA-Stalker.md`
> built the hiding system (`Scripts/Hiding/`, `NemesisHidingAwareness`); the plan's §3 and §17.6
> are the current description. One rule from 27/09 contradicts what follows: **holding breath
> inside a spot takes the player out of extreme proximity and out of what the Nemesis makes out
> through the slats** (`FieldOfView.CheckExtremeProximity` and `SenseThroughSpot` read
> `PlayerStateManager.IsHoldingBreath`, plan D21). Proximity is no longer "the single thing that
> breaks hiding": a spot the Nemesis suspects or knows is **opened** on arrival
> (`NemesisHidingAwareness.Open`, from `Searching` and `Investigating`), and that finds the player
> whatever their breath. `SO_HidingData.MaxHoldSeconds` (8) and the exhale that ends it are what
> keep holding from being immunity — never ship it at 0.

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
  The mixer (`ScriptableObjects/Audio/MasterMixer.mixer`) today ships **only** the
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
  `BaseRangeInteractable` subclasses: `GetPromptText()` is the prompt, `CanInteract()` goes false
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
  so it works in patrol, investigation and chase alike. It also checks the leaf it is already
  touching (a sphere cast does not see a collider it starts inside: the side doors it walked
  through), and shoves a door that is closing in its face back open (`DoorInteractable.IsClosing`).
  `DoorInteractable.nemesisCanOpen` / `nemesisCanForceLocked` are the per-door policy. **The leaf is
  not in the bake** (a `NavMeshModifier` on the hinge, or a scene that does not bake `Default`), so the
  doorway is walkable and a `NavMeshAgent` ignores the leaf's collider: what stops the Nemesis at a
  closed door is opening it. `EnsureNavMeshObstacle()` adds an avoidance-only obstacle on doors it
  can open (`autoCarveNavMesh` turns that off — `DoorMetalRed` and its variants, most of Zona1, ship
  with it off). A door it may NOT open — sealed by a sequence (`SetSequenceLocked`) or
  `nemesisCanOpen` off — carves the NavMesh for as long as that lasts (`RefreshNemesisBlock`, taking
  the door's obstacle or adding one on the leaf collider), since carving a door it can open would erase
  the doorway from its paths and it would never walk up to open it.
- **`NemesisAudio`** — per-state looping audio with crossfades. `stateLoops` is a designer-authored
  array, so **adding a value to `ENemesisState` without an entry crossfades the monster to
  silence**. `NemesisChaseMusic` is separate and driven by `OnChaseStarted/Ended`.

  Auto-added like the other siblings, but **resolved after the sensors and not with them**: its
  `Awake` asks the state manager for its `FieldOfListening` exactly once, and `AddComponent` runs
  that `Awake` synchronously — resolved earlier the read lands before the field is assigned, and
  occlusion silently switches itself off for the whole run. It is also the one sibling that needs
  **content**: growing the component costs nothing, but an unauthored `stateLoops` is a mute
  monster, which is why it warns once on `Start` rather than leaving that indistinguishable from a
  crossfade to silence.

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
- `la búsqueda sigue tibia` (antes "le queda presupuesto de búsqueda"), which sits **above** the noise rung. With the noise rung
  higher, hearing anything while searching voted the Nemesis into `Investigating` before
  `NemesisSearchingState.UpdateState` ever ran a frame, so its "a fresh noise re-aims the cut-off"
  logic was dead code and every noise cut the search short.
- `venia persiguiendo y todavia cree algo` / `venia hacia el montacargas y todavia cree algo` sit
  right under it, **above every Investigating rung** (22/09, WIR-006). At the bottom, a chase that
  arrived at the last seen point while the player was still audible dropped into `Investigating`
  instead of `Searching`: no look at the spot, no sweep anchored there (Plan-IA-Stalker §16.4), the
  red vignette off and the music tail cut (D5), and then a walking-pace pursuit by ear with none of
  the chase feedback.

`IsBeliefUnreachable` counts a path query that **cannot run** (the belief more than 2 m from anything
walkable: the upper stairwell, the Hub's interior, catwalk edges) as unreachable, not reachable
(22/09, WIR-018). Counted as reachable, "lo esta viendo" — an interrupt with no timeout — held the
Nemesis underneath the player, staring up, for as long as they stayed in view. A player on a crate
is not affected: the 2 m snap already lands them on the floor beside it.

`NemesisDebugHUD` shows the winning rung's index and note every frame. "Why is it doing this" is
not answerable without it.

### Nemesis: senses

**Three zones, and they add up** (03/10, `VisionZones` in `WIRED.Nemesis.Logic`, EditMode-tested):
FOCUS sees, PERIPHERY suspects ("creo que vi algo por acá"), REAR feels ("siento que hay alguien
atrás"). The rear zone is everything outside `ViewAngle`, out to `RearSenseRange` (3 m, flat from the
body, on its floor, nothing in between; ×`CrouchVisionMultiplier` crouched): it fills the same
suspicion meter at `RearSenseStrength` (0.25) of the peripheral rate and, like a soft noise, never past
`NoiseOnlySuspicionCap` — never a sighting on its own. Past the threshold "vio algo de reojo" sends it
to Investigating, which turns to face the spot first; then the eyes decide. **Adding up:** a glimpse
that falls inside a belief younger than `GlimpseCorroborationWindow` (8 s) IS a sighting at once
(`VisionZones.Corroborates`) — what a full meter already did mid-chase, extended to a search or a walk
to the player's noise. That is what keeps Searching → Investigating → Chasing from happening when it
glimpses the player it is hunting: it goes straight to Chasing. A corroborated rear presence weighs as
much as a glimpse. F9's `sospecha` row names the zone filling the meter, and says "de reojo, donde ya
lo creía" for a corroborated sighting; `NemesisGizmos.drawRearSense` draws the rear wedge.

**The search's half-second commitment does not hold against sight** (03/10): the rung "compromiso: la
búsqueda dura al menos medio segundo" asks `NOT SeesPlayer`. It sat above "lo está viendo" and kept a
Searching that had just started for half a second with the player in view. The loop it guards against
(a target seen but not reachable) is covered by `NOT IsBeliefUnreachable` on the sight rung since
WIR-018.

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

Hearing is described under the FSM section: `FieldOfListening` occludes sight and sound with
**different** masks, and how loud the player is (their emitter radius) decides the real range.

**The ear is not a GPS** (Plan-Busqueda-Nemesis Fase 1, WIR-057, D39). A noise of the PLAYER's is
reported at a *perceived* point: the real one plus an offset of up to `HearingLocalizationError` ×
that noise's belief radius (`NemesisBelief.NoiseRadiusFor`, shared so the two never drift apart),
drifting smoothly over `HearingErrorDriftTime` (`HearingLocalization`, pure, in
`WIRED.Nemesis.Logic`) and walked out from the player's spot on the NavMesh with a NavMesh raycast, so
it stays on their floor and never lands through a wall. `HeardNoise.Position` is that point and
`HeardNoise.LocalizationError` the longest offset it could have; the real transform never leaves the
sensor. `TryGetLastPlayerNoiseTruth` exists under `UNITY_EDITOR` only, for `NemesisGizmos`
(`drawHearingError`) and F9's `oído` row. Leads (decoys, Director pulses) stay exact.

### Nemesis: the rig (arms, eyes, view point)

The model (`TLLStalker`, the child of the prefab root that holds the Animator) is **scaled unevenly**
— 0.75 wide, 0.61 tall and deep — and its arms measure 2.25 m. Three things on `Nemesis.prefab`
follow from the shape of that rig (03/10):

**`NemesisArmWallGuard` (on the model) lowers an arm that would end up beyond a wall.** The walk
carries one arm stretched out at shoulder height, fingertips 3 m ahead of the pivot, and paddles the
other along the floor with the elbow 0.8 m out to the side, while the NavMesh only keeps the body's
**axis** clear, by the agent's radius: 0.3 m. The shoulder joints ride up to 0.55 m to the side of
that axis and 0.8 m ahead of it, so the creature is wider than its own agent.

In `LateUpdate`, after the Animator, each arm is tested **from the agent's axis**: the elbow, the
wrist, the middle of each bone and every fingertip have to be reachable from the axis, at their own
height, in a straight line with nothing upright in between (a sphere cast; a part that is not is
beyond a wall by as far as it lies behind that wall's face). The axis is the one place the NavMesh
guarantees is clear; testing from the shoulder failed exactly where it mattered, because hugging a
wall the shoulder is already inside it and a cast that starts inside a collider sees nothing. If
the animated pose is beyond something, the same test runs on the pose a quarter lower, half, three
quarters, fully lowered, and then lowered with the hand drawn in towards the middle of the body (a
wall at its side, `tuckedOffset`), drawn back beside the hip (a wall in front, `retractDistance`),
or both; the arm takes the first that clears (`lowerSpeed` 8 per second down, `raiseSpeed` 1.5 back
up), or the one that is beyond the least when none does. Each pose is a two-bone solve — the wrist
taken to a low spot beside the body, the elbow pointing back like the dragging arm's — so nothing is
scaled and the hand keeps the pose the clip gave it; the fingers are lifted to `floorClearance`
instead of sinking.

- **It holds a pose for a stride** (`holdCycles`, capped by `maxHoldSeconds`). A walk asks for
  something different on every frame of its cycle; giving way the moment one frame was clear pumped
  the arm up and down on every step beside a wall. A pose is dropped at once only for one that is
  needed more; otherwise it stays until a whole cycle of the clip has gone by without needing it.
- **The solve runs in the model's own space.** Under an uneven scale a bone turned in world space
  does not carry its children round rigidly: the pose that was tested and the pose that was applied
  came apart by 20 cm at the wrist, enough to test clear and put the hand in the wall. Only the
  casts, which need real distances, are done in the world.
- **Floors, ceilings and small things do not count** (`floorNormalY`, `minObstacleSize` 0.6 m), and
  the mask is Default, Interactable, Wall and Props — not Ground, not Player.
- **It stands down in `Catch`** (`suppressWhileState`): the grab needs the arms at full length.
- **It does not shorten the arm.** The first version scaled it towards the shoulder, which read as
  a small arm on a big body. It does not touch the body, the agent or the clips either: walking
  along a wall at the NavMesh edge the shoulder itself is some 16 cm into it, and the head leans
  past the pivot into a wall it walks straight up to. Only a wider agent would change that.

Measured in Play on Zona1's own walls, a copy of the model walking in place (share of the arm's
skin beyond the wall, averaged over the cycle): hugging a wall at the NavMesh edge (axis 0.38 m from
it) 26-36 % without the guard, 2-3 % with it, the arm held down and tucked in with no pumping; 0.8 m
from the wall the paddling elbow went 17 cm in without it and 0 with it.

**The eye lights hang from `Face.Upper`, not from `spine.006`.** They are the anchors of the two
`FogBeacon`s that `NemesisEyes` makes. The eyes' vertices are skinned 87-89 % to `Face.Upper`, which
turns against `spine.006` by up to 12° standing, 19° walking and 26° in the grab: on the spine bone
the lights sat 2 to 9 cm off the eyes, on `Face.Upper` they stay within 1 cm. A re-import that
renames the face bones needs them re-parented by hand.

**`ViewPoint`, where `FieldOfView` casts its cone from, is a child of the prefab ROOT at
(0, 2.0, 0.3) — not of a bone.** On the head bone it moved with the clip: 0.6 m from side to side on
the walk, turned between 9° and 30° off the body's heading, pitched down by up to 14° (40° in the
grab), so what the Nemesis could see depended on the frame of the animation. On the root it looks
where the body faces — the gaze is steered through `LookDirection`, see *senses* — from inside the
NavMesh clearance, so the cone never starts beyond a wall the head leans through. The cost is that
the eye sits 0.4 to 0.8 m behind where the head is drawn.

### Nemesis: belief (plan §17)

`NemesisBelief` — a facade sibling, added by `ResolveSibling` like the others — is the one answer to
"where is the player": `NemesisStateManager.TryGetBelief` and `BeliefAge` forward to it. It replaced
a *selector* (the freshest of the two sensors, the other discarded), under which the senses
competed instead of adding up and any noise at all — a decoy's, a Director pulse's — became the
player's position.

- **Only evidence that IS the player moves it:** sightings, and the player's own emitter.
  `FieldOfListening` tells the two apart by what the collider is (`PlayerStateManager.AudioEmitingZone`
  or anything under the player), and reports the best player noise and the best **lead** of each
  sweep separately (`HeardPlayer`, `HeardLead`, `TryGetLastPlayerNoise`, `TryGetLastLead`), with the
  measured distance and whether a wall or a floor was in the way. `HasAudioTarget` is still *any*
  noise, and the states that still read it are plan Fase 2B parts 2 and 4.
- **Position + radius.** A sighting sets a small radius; a noise one that grows with distance and
  with what it passed through (`SO_NemesisData` › *Creencia*). Evidence inside the grown radius is
  merged by inverse variance — the radius shrinks, which is the senses adding up — but **never below
  the newest noise's own radius** (D22, extended to every noise by Plan-Busqueda Fase 1): the sensor
  re-hears one continuous noise every 0.1 s, and folding those as independent witnesses is what let a
  run of footsteps pin the player to a locker door. Only a sighting anchors finer. Evidence that
  cannot be the same spot (outside it, or another floor: a merge would land in the slab) replaces
  it. With no evidence the radius grows at the player's top speed.
- **`BeliefAge` counts the player only.** A lead never keeps it young — that is what let a 30 s fire
  alarm hold chases and searches open. The walk towards a lead is held by its own rung, *sigue yendo
  hacia la pista* (`HasFreshLead`).
- **`HearsPlayer` means the player** (it used to read `HasAudioTarget`). Leads reach the ladder only
  through the focus (next section): *su atención está en una pista* (`FocusIsLead`).
- `IsAnchoredBySight` — what `TryGetBelief`'s `fromSight` returns — holds for 0.25 s after a
  sighting, so it no longer flickers while the player is both seen and heard (the two sensors tick
  on independent 0.1 s timers).
- `NemesisController` reads the facade too; it kept its own sight-first copy of the belief.
- Still to come: plan Fase 2B part 5, removing the per-state patches that worked around the old
  belief (parts 2–4 are built; see the plan's §9).
- `Sequence` goes up on every folded piece of evidence, roughly 10 times a second while the player is
  seen or heard. It is not "something new arrived": a consumer that re-targets on it re-targets every
  frame. Compare places ("same spot = update"), not sequence numbers alone.

### Nemesis: the focus (plan §17.4, Fase 2B part 4)

**`NemesisBelief` knows; `NemesisChoice` chooses what to follow.** The choice — a sibling added by
`ResolveSibling`, ticked right after the belief and before the ladder — keeps one FOCUS: the player,
a lead (a decoy, a Director pulse) or a glimpse. Every time something new comes in (the belief's
`Sequence`, its `LeadSequence`, a fresh glimpse once the meter is past the threshold — never because
a sensor is merely on) it asks `FocusArbiter` (pure, `WIRED.Nemesis.Logic`, `FocusArbiterTests`) the
§17.4 questions in order: sight switches at once; busy (breaking the radio, opening a spot at its
door) keeps; the same thing (the same decoy, the player again, the same kind within 3 m — but two
different decoys are never the same thing) updates without restarting anything; discarded (a decoy it
already checked while it still sounds) is ignored; a lead where it is already searching sums; under
the attention floor it is ignored; otherwise the new thing has to beat the current one by a margin,
against the commitment bonus of a fresh choice and "almost there".

- **The one repeat:** a lead still sounding that is not the focus is asked about again every 1 s. A
  decoy that never goes quiet (the fire alarm) does not move `LeadSequence`, so without it a lead
  that lost once — to a fresh belief, while busy, summed into a search — was ignored for as long as
  it sounded, however cold the belief got. The lead that IS the focus takes its age from the last time
  it was heard instead.
- **Letting go falls back on the player.** `MarkFocusChecked`, a glimpse or lead going stale, or a
  suspected spot dropping the lead leave the focus on the player while there is a belief — without
  the commitment bonus — not on "nothing": any Director pulse beat "0 + margin" and pulled a warm
  search out from under itself. "Nothing" only when there is no belief.

- **Value** = base (the player 1, radio 0.6, alarm 0.7, chains 0.45, other 0.4, glimpse 0.5) ×
  confidence (the evidence radius) × freshness (half-life 6 s) × the cost of getting there × the
  decoy's habituation (×0.6 per fruitless visit, for the session). All on `SO_NemesisData` ›
  *Elección*. "Almost there" never applies to the player's focus: a search circles the belief.
- **Who reads it.** The ladder, through `FocusIsLead` (22, appended): *su atención está en una pista*
  → Investigating, ABOVE the search budget — a lead that won the choice can pull it out of a cold
  search (case 31), and one that lost (habituated, or far while the belief is fresh) never moves it.
  It replaced *oye un señuelo u otro ruido* (`HearsLead`). The lift rung *para llegar hay que tomar
  el montacargas* asks `Not FocusIsLead`: it would otherwise ride towards the belief the choice just
  turned down. `NemesisInvestigatingState` walks to the
  focus and, on a switch, stops ~0.4 s turning to the new thing first ("it changed its mind"); after
  looking at a lead or a glimpse for the dwell it calls `MarkFocusChecked` (habituation, and the
  focus is let go). `NemesisDecoyBreaker` breaks the decoy that is the focus (`FocusDecoy`), not
  `FieldOfListening.LastHeardDecoy`, and is now installed by `ResolveSibling` (it was on no prefab).
- A hiding spot it suspects or knows outranks any lead: the choice drops a lead focus while one
  exists, which keeps the lead rung (above *sospecha de un escondite*) from sending it to the radio.
- `DecoyNoiseSource.Kind` (radio / alarm / chains / other) is read off the decoy component next to
  it, and `DecoyNoiseSource.Id` identifies it (`GetInstanceID` is obsolete in Unity 6.4).
- F9 row "foco": the focus, its value, how long ago it was chosen, and the last decision with its
  question number, e.g. `cambió: radio 0.36 > vos 0.28 [11]`.

**Shared suspicion (§17.3).** A SOFT player noise (emitter radius ≤ `SoftNoiseLoudness`, crouching;
not from a hiding spot) feeds `FieldOfView`'s suspicion meter the way a glimpse does
(`FieldOfListening.HeardSoftPlayerNoise` → `FieldOfView.NoteSoftNoise`), so a soft step and a glimpse
together cross the threshold sooner (case 26). A noise alone raises the meter only up to
`NoiseOnlySuspicionCap` (under 1: never a sighting) and never lowers it — clamping the whole meter
used to drop it the moment a glimpse ended and the steps went on.

**Hiding spots in the choice (§17.6, and the Nemesis half of Fase 2D).**
`NemesisHidingAwareness.ConsiderUsedSpots(centre, radius, rolled)`: while Searching sweeps an area
(on entry, re-centre and widen) and when Investigating arrives at a noise, the spots the player USED
inside that area (`PlayerHabitTracker.CollectUsedSpots`) are rolled once each, most used first,
against `OpenChance`; the first that comes up becomes SUSPECTED ("lo usaste antes") and the state
walks over and opens it (case 39). Never outside the area (case 40, R4). R3: until the first such
opening has happened (`CheckHidingSpots`, `HasRun`), nothing is rolled unless the player is within
12 m of the AREA's edge and on its floor — a **gate**, asked once about the area (Plan-Busqueda Fase
1). It used to be a per-spot filter against the player's real position, which always let their own
spot through and tilted the roll towards it. `MarkRun` is called when that spot is OPENED (`Open`),
not when it is picked — a suspicion dropped on the way does not spend the lesson. Spots gated out are
not rolled. The rolled set lives for a search, or for an investigation (cleared on `EnterState`). And
D22's second half: a second noise from inside the same spot (a new burst after more than 1 s of quiet,
within 1.5 m + the ear's `LocalizationError` of the first, inside 60 s) makes the spot nearest to
where it was HEARD suspected ("volvió a sonar ahí") — possibly the next locker along.
Breathing counts: two breaths heard from ~2 m send it to open the door, which is D21's band where
holding your breath decides. The first noise is forgotten on `MarkChecked`, capture, respawn, or
seeing the player out in the open. Investigating tracks a suspected spot as its own source (`Spot`),
not as a glimpse, and swallows focus switches while walking to it.

### Nemesis: habits (plan Fase 3, and the spot memory of 2D)

`PlayerHabitTracker` (`Scripts/Nemesis/`, in the **Data** scene: `Singleton` + `ISessionResettable`)
counts what the player keeps doing to get away and remembers which hiding spots they use. It is
**director-side** (plan R4): it reads the real player and the real Nemesis, but all it may change is
*which* behaviours exist. Whatever acts on it aims with what the Nemesis sensed ("open the used spots
inside the area it is searching"), never with the tracker's knowledge of where the player is.
**Fase 3 counts and does not react:** nothing in the game reads an unlocked counterplay yet.

- **What is counted** (`EExploitKind`, append-only):
  - `EscapedWhileHidden`: a search ended without finding the player, within `NearbyRadius` NavMesh
    metres of where they were hiding. Counted once the stay becomes an escape (below), one per
    **hunt** outlasted, not when the search ends. A hunt lasts until the Nemesis is back in
    `Patrolling`, so searching, investigating a breath and searching again is one hunt. Hidden with
    no spot (the F10 *Hide* toggle) it counts at once.
  - `SameSpotReused`: an escape from a spot the player had already escaped from.
  - `ChaseStalled`: one per `NemesisChaseProgress` window.
  - `SafeZoneEscape`: a chase ended with the player in the Hub, and not in a capture. The capture flag
    is consumed at `ChaseEnded`, never reset at `ChaseStarted`: a grab from patrol raises the capture
    and the chase start in the same frame.
- **Escapes, not attempts (R1).** Hiding scores the spot's meter +1 on the way in. Everything else
  (the +1 of D23, the outlasted hunts, `SameSpotReused`) waits until the stay is an **escape**, which
  `HidingStayBook` (pure, tested) decides:
  - The Nemesis hunted nearby during the stay: `Investigating`/`Chasing`/`Searching` within
    `NearbyRadius` by NavMesh, sampled every 0.5 s; or a search ended near the spot.
  - The player walked out. A capture is not a way out: `PlayerStateManager.OnCaptured` disables the
    player *before* it raises the event the spot releases them on, so `IsDisabled` at `OnExited`
    says why. Neither is a cinematic (`CinematicState.IsPlaying`) nor the player going away with the
    level.
  - Then 5 s went by with no capture and no chase running. Walking out into the pull-out, or being
    seen leaving and caught in the chase, is being caught. The capture cancels the pending escape,
    and hiding again confirms it.
- **Keys and decay.** The meter is keyed by `SpotId`; a spot without one falls back to its GameObject
  name and logs a warning. Decay is lazy: computed on read from the time of the last raise. Counts
  hold for `HabitDecayDelayMinutes` before draining, so a count does not slip back under its
  threshold the moment it reaches it.
- **The arithmetic is pure and has its own assembly.** `HabitLedger`, `HidingStayBook`, the enums
  and `CounterplayRule` live in `Scripts/Nemesis/Logic/` (`WIRED.Nemesis.Logic.asmdef`, no reference to Assembly-CSharp),
  which is what lets `Tests/EditMode` test them. **Anything put in that folder has to stay pure:** no
  `HidingSpot`, no `SO_*`, no scene types. A test assembly cannot see Assembly-CSharp.
- **Tuning is `SO_CounterplayRules`** (`ScriptableObjects/Nemesis/`, assigned on the tracker).
  - Rows of *kind × threshold → counterplay*. The chance is 0.35 at the threshold, +0.1 per extra
    use, capped at 0.85.
  - The spot meter and `NearbyRadius`.
  - `CheckHidingSpots`, `PrioritizeSuspiciousSpots` and `BurnHidingSpot` have no row on purpose:
    they are answered per spot by the meter.
- **Read API** for plan §17.4 question 7 and Phase 6:
  - `GetSpotUsage`, `OpenChance`, `IsPrioritySpot`, `IsBurnable`.
  - `CollectUsedSpots(centre, radius, list)`: NavMesh distance, most used first.
  - `IsUnlocked` and `CounterplayChance`.
  - `HasRun` / `MarkRun`: the bookkeeping for R3, "the first run is witnessed".
- **Two events feed it.**
  - `NemesisEvents.OnSearchEnded(area, found)` is raised by `NemesisTelemetry` when the Nemesis
    leaves `Searching` for anything but `Traversing`. A search carried to another floor stays open.
    The area is the belief; found means it left for `Chasing` or `Catch`. A Nemesis switched off
    mid-search (the escape cinematic) keeps it open and reports it when it next leaves `Searching`.
    Leaving for `Investigating` is an end too, even to investigate the player's own breath: a
    listener that means "it gave up" has to tell the two apart.
  - `NemesisEvents.OnChaseStalled` is raised in `NemesisChaseProgress.Stall`.
- **Survives the capture and the checkpoint.** It is outside the rollback, which only restores
  `PuzzleStateManager`. New Game clears it.
- **Debug:**
  - F9 rows `hábitos`, `desbloquea` and `escondites`.
  - F10 section HABITS: *Log ledger*, *Clear habits*.
  - One console line per count and per hide (`logRegistrations`).
  - *Validate Navigation Setup* checks the rows.

### Nemesis: escalation (plan Fase 7)

`NemesisEscalation` (`Scripts/Nemesis/`, in the **Data** scene: `Singleton` + `ISessionResettable`)
makes the Nemesis sense further and patrol less predictably as the story advances. It is spec §7.2
with the three deltas the project decided: it counts **puzzles**, it **never touches speed**, and it
leaves the search time alone (plan D31).

- **The tier comes from a count.** `PuzzleStateManager.CompletedPuzzleCount` is re-read when a
  level's scene loads and on every completion, respawn, activation and Nemesis state change. It
  never tallies `OnPuzzleCompleted`: a checkpoint restore refills the set without the event, and a
  rollback lowers the tier with it.
  - The scene load is the one the route needs. It lands after the Nemesis's Awake and before its
    Start, and the Nemesis enters Patrolling, rolling its first cycle's route chances, **before** it
    raises `OnActivated`. The Nemesis is looked up in the scene that loaded, since on a Retry the
    old level's can still be alive.
- **It is permanent, through the baseline.** It clones `NemesisStateManager.AuthoredData`, applies
  the tier and hands the copy to `InstallBaseline`. Scale from the authored asset, never from the
  current baseline, or one tier compounds on the last.
  - With nothing lent, the copy is installed at once.
  - With the Director's loan out, `NemesisEvents.OnBaselineChanged` makes the Director rebuild the
    loan on top of the new baseline (`loanedFrom`), instead of waiting for it to end.
  - The asset itself is never written: Play-mode edits to a ScriptableObject persist in the Editor.
  - A new session (New Game, Retry) throws the copy away. A Retry resets with the old level still
    loaded, so a Nemesis that is still alive gets its authored asset back first: its senses never
    point at a destroyed asset.
- **What a tier moves** (`SO_NemesisEscalation`; the tier with the highest threshold reached wins,
  whatever the list order):
  - sight: `ViewRange`, and with it every range measured as a fraction of it (locker slats, under
    the table);
  - hearing: `NoiseRangeScale` **and** `ListenRange` together. `ListenRange` alone is only the cap, so
    walking noise (10 m) would never change. The Director's senses loan (`RefreshLoan`) scales both
    too (fixed 28/09, plan D32);
  - search persistence: `SearchQuietWindow` and `SearchHardCap`, at 1 in every default tier (D31);
  - route variation: a floor under `RouteReverseChance` and `RouteSkipWaypointChance`, never lower
    than authored.
- **Defaults:** base from 0 puzzles; ×1.1 sight and a 0.25 route floor from 2; ×1.15 sight, ×1.1
  hearing and a 0.40 floor from 3. The first puzzle is the one that wakes the Nemesis, so the spec's
  "module 1" is 0–1 puzzles (D33).
- **Pure part:** `EscalationTier` and `EscalationRules` in `Scripts/Nemesis/Logic/`, tested by
  `EscalationRulesTests`.
- **Debug:**
  - F9 row `escalada`.
  - F10 section *ESCALATION*: *Tier -* / *Tier +* / *Auto*. The testbeds have no puzzles.
  - One console line per installed tier.
  - *Validate Navigation Setup* reports duplicate thresholds and sense multipliers under 1.

### Nemesis: chase and search

**Chasing runs `NemesisPursuit`, not `destination = belief`.** Seek aimed at where the player
already was means arriving after they have left the next place too, so against someone running in a
straight line the Nemesis holds station instead of closing.

- **Prediction.** `NemesisPursuit.PredictAhead` projects the belief forward by
  `ChaseTimePrediction` along the observed velocity, and **keeps the dot guard**: if the lead point
  lands on the far side of the Nemesis — which is what happens when the player runs *at* it — it
  aims at the target instead of turning around and sprinting away. Only the chase predicts: since
  plan Fase 2B part 2 the search sweeps around the belief and extrapolates nothing.
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
- **The route choice does its own path query, not through `NemesisPathOracle`.** It is affordable
  because the replan is already throttled by `ChaseRouteReplanInterval`. The one oracle read the
  pursuit does make — is the last point it SAW the player still reachable — is safe only because the
  oracle now keys its answers on the target (22/09, WIR-018): it used to hold ONE answer on a timer,
  so whichever asker came first in an interval got the query and the other read it as its own — the
  ladder's "is the belief unreachable" answered with the route to the last-seen point, or the
  reverse, flipping the unreachable rung on a borderline path. A target that drifts less than
  `TargetMatchRadius` (2.5 m) reuses the answer, which keeps a moving belief at one query per
  interval.

**Searching sweeps NavMesh points around the belief** (plan §18.5 A, Fase 2B part 2, 27/09). It
used to pick among patrol waypoints — a cut-off at "the waypoint ahead of you it reaches first"
(`TryGetInterceptPoint`), a weighted roll over waypoints (`PickSearchTarget`) and a room sweep that
offered the waypoints inside it first — and any noise at all re-aimed it every frame. In play that
read as "it went to some node nearby instead of where it lost me, sometimes". All of that is gone
(decision D24); now:

- **The disc** is centred on `NemesisBelief.Position` (snapped to the NavMesh) and sized off the
  precision of the LAST EVIDENCE, `NemesisBelief.EvidenceRadius` + `SearchSweepEvidenceMargin`,
  clamped to `SearchSweepMinRadius`..`RoomSweepRadius`. Not off the belief's current `Radius`: that
  grows at the player's top speed and is already at the maximum by the time the Nemesis gets there.
- **It moves only with new evidence about the PLAYER**, and only as much as it has to
  (`SearchSweepRules`, pure, in `WIRED.Nemesis.Logic`, EditMode-tested): evidence inside the disc
  slides the centre and changes nothing else; evidence outside it re-centres the disc and keeps the
  swept memory. `NemesisBelief.Sequence` alone is not enough to decide — it goes up ~10 times a
  second while the player is seen or heard. Leads (decoys, Director pulses) never move the sweep.
- **It goes to the evidence point itself first** (`NemesisFreeRoam.IsAnchorPending`), then sweeps
  around it: on entry, on a re-centre, and when evidence lands somewhere in the disc it has not
  stood at yet. Not for the player's noise from inside a hiding spot
  (`NemesisBelief.LastEvidenceFromHidingSpot`, D22): that point is the locker door. Rolling over the
  disc from the start (weighted mostly towards "sooner") left it at the near edge, metres short of
  where it lost the player — worst over long distances (playtest 27/09, plan §18.9). Entered at the
  lost spot but having heard the player further on since, it skips the look-around there and goes.
- **Once covered it widens** a step (`NemesisFreeRoam.WidenStep`) up to `RoomSweepRadius`; with
  nowhere reachable at all it widens before falling back to a scatter.
- **Without a belief** (entered from Catch, or on a known hiding spot alone) it sweeps
  `SearchSweepRadius` around itself.

`SearchPauseTime` makes it stop at each point and look around before choosing the next, and it is
armed on ARRIVAL (it used to be armed when setting off, which left the first point without a pause
and had `IsPausing` — what `NemesisLookAround` reads — true all the way to the next point). That is
what makes a search **legible**: without it the Nemesis chains destinations and, from inside a
hiding place, none of it says whether it is closing in or has already written the area off.

**A search cools down; it does not expire** (plan §18.5 B, Fase 2B part 3). **It lasts until the
evidence stops** — the designer's rule since 03/10: "hasta que sienta que no hay más evidencias
nuevas del player", no duration and no budget. The rung "la búsqueda sigue tibia" reads the
predicate `IsSearchWarm` (20, appended), which reads `NemesisSearchingState.IsWarm` (pure rule:
`SearchCooling`, in `WIRED.Nemesis.Logic`, EditMode-tested). It used to be a fixed
`TimeInStateUnder(SearchTimeOut)`. The rules, in order:

1. **Minimum.** Under `SearchMinTime` (6 s) in the state, always warm. Keep it under the player's
   `maxHoldSeconds` (8 s), or running out of breath in a hiding spot is always fatal (D21).
2. **Cap — off as shipped.** `SearchHardCap` is 0: no cap. With one set, at the cap it goes cold
   however much it still hears, and a player it hears sends it to investigate. It used to be 30 s,
   which cut a search short while the player was still audible and bounced it Searching ↔
   Investigating.
3. **Looked everywhere.** Cold once the sweep is fully swept at its widest.
4. **Silence.** Otherwise warm while the silence is under `SearchQuietWindow` (8 s) × the quality of
   the last evidence: ×1.25 for a sighting, ×0.75 for a noise through a wall, a floor or a hiding spot
   (`NemesisBelief.LastEvidenceMuffled`). The silence (`NemesisSearchingState.Silence`) counts from
   the later of the last evidence about the player and the moment it got to the point that evidence
   came from, and does not run at all while it is still walking there: counted from the evidence
   alone, the walk to a far footstep ate the window and the search gave up on arrival (§18.9).

Two cases change the numbers:
- **The Hub.** Evidence from inside the Hub never renews the search (C5): footsteps heard through
  its door would keep the Nemesis camping outside until the cap. Leads never renew it (they do not
  move `NemesisBelief.Sequence`).
- **The investigation lasts until the evidence stops, too** (03/10). `IsInvestigationWarm` (21,
  appended), read off `NemesisInvestigatingState.IsWarm`, is warm the WHOLE walk to what it sensed —
  only a destination with no path ends it — and, once there, while the silence since the later of the
  arrival and the last evidence (belief, or the glimpse it walked to on the muffled window) is under
  the window. It holds the walk ("sigue yendo hacia lo que sintió", which used to drop the walk
  `InvestigationTimeOut` after the last evidence wherever the Nemesis was) and, after the look-around
  (`InvestigationDwellTime`), turns the investigation into a search ("investigó lo que sintió y sigue
  tibio", D26) that runs on the same silence rule. A lead keeps the plain gate (belief younger than
  the window): a decoy found empty says nothing about the player. With a cap set, the escalated search
  gets a fraction of it (`SearchEscalatedCapScale`); Searching knows it was escalated from
  `StateManager.PreviousStateKey`.
- **What Investigating walks to.** A lead it is focused on, a glimpse, or — for the player — the
  BELIEF, never the raw noise (plan §17.3: "va a la posición de la creencia o del vistazo, no al
  último ruido"): the belief has already folded that noise in with what it saw, and the ear's point is
  only where the ear placed it (Fase 1). Newer evidence of the player re-aims it once the retarget
  interval allows. On ENTERING it first stops and turns until it faces what brought it there (max
  1.5 s), then walks: legible ("se frena y gira hacia el ruido"), and a presence felt behind it gets
  looked at with its eyes instead of being backed into.

The Director lends different window and cap values through its loan on `SO_NemesisData`
(persistence), so the state reads them fresh every frame. `SearchTimeOut` stays: it is still the
memory of a known hiding spot (`NemesisHidingAwareness`). F9's "búsqueda" row shows "tibia s/ventana",
"tope", and "se enfrió" or "revisó todo".

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
the state that uses it — and since 27/09 it is the whole search. It samples points on the NavMesh
around the anchor FIRST and adds two waypoints inside the area as extra candidates (a waypoint the
designer put in a room is a considered opinion about it, but offering them first made the sweep a
waypoint tour whenever enough of them fell inside the disc). Candidates more than 1.5 m above or
below the anchor are dropped (another floor). Dropping the graph drops two guarantees that were
free, and both are paid for explicitly:

- **Reachability** — `NavMesh.SamplePosition` returns the nearest surface, including one on another
  island. Every candidate is path-tested; an unreachable destination is how the agent ends up
  pressed against a wall with `remainingDistance` at zero.
- **Confinement** — a room is not a circle. The disc is clipped by
  `FieldOfListening.IsOccludedByWall` from the anchor, so a doorway stays open and the corridor
  behind the wall does not. There are no authored room volumes in the project; this is the derived
  stand-in for one, and it will treat an L-shaped room as two.

Swept memory here is **spatial** (a list of positions, `SweptRadius` apart), not node indices, since
most destinations are not nodes. A point is marked when the Nemesis gets THERE (`MarkSwept`), not
when it is picked; `Recenter`, `MoveAnchor` and `Widen` move or grow the area without forgetting it.
`IsFullySwept` ("every reachable candidate of the last pick was already swept") is distinct from
`HasCoverage` false ("nothing reachable at all"): only the first is "I have looked everywhere here".

**The entered room.** When the evidence is precise enough to say which side of a doorway the player
is on (a sighting, or a noise pinned to ~2 m), the sweep favours that room — for a sighting, the room
a step and a half ahead along the OBSERVED velocity, since the last sighting is usually the doorway.
The priority holds only while that room still has unswept candidates.

**A noise from inside a hiding spot marks the area, not the door** (plan D22):
`FieldOfListening.HeardNoise.FromHidingSpot` widens the belief radius by
`BeliefNoiseHidingSpotFactor` (×2), so a search sized off a breath heard from a locker sweeps the
room instead of pacing in front of the door for longer than the player can hold their breath.

**It walks to the evidence point only when the point means something** (Plan-Busqueda Fase 1,
WIR-057): a sighting, or a noise whose evidence radius is at most `SearchPreciseNoiseRadius` (heard
right beside it). Any vaguer noise is swept around without visiting the point, which for a run that
ends in a locker is the locker door. `NemesisSearchingState.MayVisitEvidence` is the rule; F9's
`creencia` row prints it as `ancla: vista / ruido preciso / zona`.

Tunables: `RoomSweepRadius` (the maximum), `SearchSweepMinRadius`, `SearchSweepEvidenceMargin`,
`SearchSweepRadius` (no belief), `SearchSweptPenalty`, `SearchPauseTime`. Drawn by `NemesisGizmos`
(`drawRoomSweep`) as the anchor, its radius, a line to the current point and the swept trail in
visit order — a trail that keeps crossing itself means `SearchSweptPenalty` is too weak. F9's
"búsqueda" row shows the same numbers.

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
per-waypoint patrol roll, the pursuit's detour candidates, the search's interception (removed on
27/09), and the sensed
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

**Off while it would cheat the most** (Plan-Busqueda D40): `TryGetZoneAnchor` ignores the real
position while the player is hidden, during a hunt (Chasing, Searching, Catch) and for
`HuntGraceSeconds` after it (`NemesisController.IsInHuntOrGrace`, fed by
`NemesisEvents.OnStateChanged`). That is exactly when the post-search patrol used to keep circling
the locker the player was in. F9's `presión` row says which of the two switched it off.

### Nemesis: the possibility map (Plan-Busqueda Fase 2)

**`NemesisPossibilityMap`** (a facade sibling, added by `ResolveSibling`, ticked after the belief and
the hiding awareness) keeps "how possible is it that the player is here" over a graph of the level —
Damián Isla's occupancy maps. The belief knows where it sensed the player; this says where they can be
now. **Fase 2a: it only watches** — nothing decides off it yet; the gizmo and F9 are how it is judged.

- **The graph** (`NemesisPossibilityGraphBuilder`, once per level load): the Nemesis agent's NavMesh
  triangles rasterised every `SearchMapNodeSpacing` m, one node per floor per column, linked to
  neighbours when a NavMesh raycast between them is clear — so value never crosses a wall. One node
  inside each hiding spot off its approach point; the freight elevator as one long edge behind a gate
  that follows `ElevatorPower` (the player cannot ride a dead lift); doorway nodes of a safe zone are
  drains — at the Hub's own floor (not a catwalk over a tall volume), with nothing but a
  `DoorInteractable` between them and its inside on the eyes' obstacle mask. Drops are not edges
  (D9). The build logs its node count and time to the Console. Measured 02/10 in batch mode: testbed
  517 nodes / 13 ms / 1 doorway; Zona1 1695 nodes / 25–31 ms / 2 doorways; a tick without clearing
  costs ~0.03 ms.
- **The rules** (`PossibilityMap`, pure, in `WIRED.Nemesis.Logic`, tested in `PossibilityMapTests`):
  a sighting seeds a point with the observed heading; a noise seeds the area of its (perceived)
  position ± evidence radius, half the map's prior inside it and half even — hearing the same thing
  twenty times never sharpens it. The value spreads along edges at `SearchMapSpreadSpeed`, faster
  along the heading for `SearchMapHeadingDuration` (`SearchMapHeadingBias`). What it looks at it
  clears: floor nodes in its cone within `ViewRange` × `SearchMapClearRangeScale`, with line of sight
  to `SearchMapProbeHeight` (a crouching player) over the node, plus whatever is under it; skipped
  while it sees the player. A closed hiding spot is cleared only by `NemesisHidingAwareness.Open`
  (`SpotOpened`). Doorway drains move value into a sink ("went into the Hub", C5) that never leaks
  back. Then everything, sink included, renormalises.
- **Seen in** `NemesisGizmos` › `drawPossibilityMap` (off by default: heat tiles, the cleared cone, a
  line to the likeliest place) and F9's `mapa` row (share on its floor, likeliest place and its share
  within 4 m, spread as m², Hub and hiding-spot shares, nodes cleared last tick).
- **Next** (the plan's 2b–2e): the search picks by value ÷ (1 + time to get there) instead of a disc,
  cooling becomes "the value is too spread to be worth it", the noise-led short search and the
  post-hunt patrol read it, and hiding spots open on value × habit (D38).

### Editor tools

All under `Tools/`:

| Menu item | What it does |
|---|---|
| `Nemesis/Validate Navigation Setup` | Reports mismatched layer masks and geometry outside the bake |
| `Items/Validate Interactable Highlights` | Finds interactables with no proximity highlight, and highlights whose material has no `_TintIntensity` / `_EmissionIntensity` — a silent no-op the inspector cannot show |
| `Player/Setup Capture Grab` | Rebuilds the player's `Grabbed` clip and state, measures the grab's distance off the Nemesis's arms into `SO_CaptureGrabConfig`, and wires the Player prefab. See *The grab* |

Custom inspectors live in `_Project/Scripts/Editor/`: `SO_MovementEditor` and `SO_CameraConfigEditor` draw
to-scale diagrams and live verdicts on top of `PlayerDiagramGUI`, a small shared IMGUI kit
(`Canvas`, `Bar`, `Verdict`, `Line`, `VMeasure`) with the project palette — green = healthy,
red = penalised, amber = warning. Reuse it for any new authoring inspector rather than starting a
new drawing helper.

**Id fields are dropdowns, not text boxes.** Three `PropertyAttribute`s in `_Project/Scripts/Attributes/` turn
a bare string into a list of the ids that actually exist, each with a drawer in `_Project/Scripts/Editor/`:

| Attribute | Drawer | Lists |
|---|---|---|
| `[PuzzleId]` | `PuzzleIdDrawer` | every `puzzleId` declared by the five puzzle SOs (collected by type NAME — they share no base class) |
| `[SoundId]` | `SoundIdDrawer` | every `SO_SoundData` id, grouped into submenus by `SoundCategory` |
| `[PressureZoneId]` | `PressureZoneIdDrawer` | every `NemesisPressureZone` id in the open scene(s) |

All three exist for the same reason: these ids are matched by **string** at runtime, and a typo does not
fail — it produces a gate that never opens or a sound that never plays, with nothing in the console.
All keep two escape hatches that are as load-bearing as the list: `(vacío)` to clear the field
(most of these are optional), and `(escribir a mano…)` for an id whose asset does not exist yet. A
value that matches nothing is shown with a `⚠ no existe` marker and **kept** — never silently
snapped to the first entry, which would rewrite wiring nobody asked to change.

`SoundIdDrawer` mirrors `SO_SoundData.Id`'s fallback to the **asset name** when the `id` field is
blank. Reading only the serialized field would omit exactly those sounds from the dropdown: present
at runtime, invisible in the inspector.

Add `[SoundId]` to any new string field that names a sound, and `[PuzzleId]` to any that names a
puzzle. The nine `[SoundId]` fields today are on `PickUpInteractable`, `SocketInteractable`,
`ElevatorCallPanel` (x2), `ElevatorRideButton` (x2), `SO_ItemCategoryConfig` and `SO_DoorData` (x2).
The `[PuzzleId]` ones are on `Checkpoint`, `NemesisRoute`, `NemesisController`, `SO_DoorData`,
`ModuleData`, `SO_ContainerData`, `SO_SocketData` and `SO_ValveData`.

Two different jobs hide behind "names a puzzle", and only one of them takes the attribute. A field
that **declares** an id — `puzzleId` on the five `SO_*PuzzleData` assets — is the source the
dropdown reads, so it stays a plain string; those five type names are the list at the top of
`PuzzleIdDrawer`, and a sixth kind of puzzle asset has to be added there or its ids never appear in
anyone's dropdown. A field that **references** one gets `[PuzzleId]`. Getting that backwards gives
you a dropdown offering the value you are trying to define.

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
Setup` turns that into a console message; the fix is done by hand. **It does not rebake the
NavMesh** — the mask decides what goes into a bake, it does not trigger
one. Navigation window ▸ Bake, by hand, afterwards.

### Freight elevator

`MovingPlatform` is the cabin, `NemesisElevatorLink` is the static root that owns the `NavMeshLink`
and measures the shaft, `NemesisElevatorUser` performs the crossing for the Nemesis, and
`ElevatorCallPanel` is the player's call button — one per landing, which is what stops a player who
rides up and steps off from being locked out of that floor.

**Power (WIR-063).** `ElevatorPower`, on the `MontacargasRoot` prefab next to the link, is one bool per
shaft — the `PoweredLightSwitch` pattern: set when `powerPuzzleId` completes (with the Start catch-up),
dropped again by a respawn whose rollback un-solves it, tickable by hand to test. Until then the call
panels go dark (`noPowerColor`) and the ride button refuses too, both showing `noPowerInfo`. The prefab
ships with the id **empty, which means always powered**, so the testbeds keep a working lift; Zona1's
instance sets `sp1_panel_electrico`, the puzzle the light switches wait for (F3 completes it in Play).
Only the player's controls read it: the Nemesis calls `MovingPlatform.RequestRide` directly.

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

Two follow-ups from the 22/09 pass (WIR-024). The half second of benefit of the doubt after a new
gait order is granted only when **setting off** from Idle/Grabbing: every state runs at its own speed
(chase 3, search and patrol 2.75, investigate 2.5), so each transition used to count as a new order
and re-arm it, and a Nemesis traded between states while wedged was never judged at all. And the two
states that steer continuously (Chasing, Traversing) send their destination through
`NemesisStateManager.SteerTo`, which re-sends only when the target has really moved (0.5 m far away,
down to 0.15 m at arm's length, plus once a second as a safety net). Re-sending every frame restarted
the path request each time — pathPending blurred the arrival test, which flipped Chasing's gait
Idle/Running around every frame, and a long route across floors could stay on Unity's quick partial
path. `WarpTo` calls `ForgetSteering` so the first frame after a teleport always re-sends.

**Generated links are off-limits.** Anything that is not a lift is crossed by
`TraverseSimpleLinkAsync`, a straight-line lerp of the transform, and Zona1's surface still bakes
with *Generate Links* on: dozens of generated jump links ran straight through the box room's
pillars, so the Nemesis walked through them, and each crossing raised `IsTraversing` and parked it in
`Traversing` for up to `ElevatorCommitTime` with no chase feedback. `NemesisLifecycle.ApplyMovementTuning`
clears the built-in `Jump` area (where Unity puts every generated link) from the agent's mask before
publishing it to `NemesisNav.AreaMask`, so the oracle and the route graph stop counting those links
too. It is Plan-IA-Stalker D10 taken on the agent's side; turning *Generate Links* off and rebaking
is still the real fix, and authored links (the lift's Walkable/Forklift, the drops' own
`NemesisDrop`) are untouched.

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

**Stepping off a link without crossing it is a warp** (`LeaveCurrentLink`): onto the near end
(`currentOffMeshLinkData.startPos`), and only then `ResetPath`. Never `ResetPath` or
`CompleteOffMeshLink` while the agent is ON a link: `CompleteOffMeshLink` crosses it by definition,
and `ResetPath` completes it too, per Unity's docs. Until 28/09 every step-off went through
`ResetPath` and landed the Nemesis on the other floor. The walk aboard then ran on the wrong floor for
the full 12 s, and the fallback lerp flew the body through the slab and the walls: 30+ s in
`Traversing`. The same helper serves abandoning a lift, the cooldown nets and `AbortDrop` (D30), so
all of them had the bug. `WalkAboardAsync` now also checks that it is still on the landing's floor
(`WrongFloorAfterStepOff`). A plain link crossed by hand (the cabin's doorway, on an ordinary
path) releases the `Traversing` commitment like a ride (`HasJustEndedRide`). Before that, the
commitment held it 12 s.

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
  still works. A finished trip releases it on the spot, whether a ride or a drop (`HasJustEndedRide`
  / `HasJustEndedDrop`, through `HasGivenUpOnElevator`): the commitment is for the approach, not for
  after arriving.

### Drops between floors (plan §15, Fase 8)

`NemesisDropLink` marks a one-way way down (a hole in the floor, a broken railing, a catwalk edge)
and owns its `NavMeshLink`, configured in `Awake` from two child transforms, `TopEdge` and
`BottomLanding`: one way, on the `NemesisDrop` area (index 5), and with **no cost override**. A
positive `costModifier` would replace the area cost, and the area cost is what
`NemesisStateManager.ApplyDropAreaCost` moves per state (2 while hunting, 20 on patrol; D11). It
mirrors `NemesisElevatorLink`, down to the static `Active` list `NemesisNav` needs to recognise a
drop among a path's corners.

The crossing is a third branch of `NemesisElevatorUser` (`TraverseDropAsync`), never a second
component watching `isOnOffMeshLink`. Its phases (plan §15.3) are align, look (the growl: the tell),
take off (Hop) or turn and hang (Hang), fall along a `DropArc`, land (impact) and recover. The agent
stays **on the link** throughout, and the landing closes it with `CompleteOffMeshLink`, like a plain
link. The only warp is the rescue for a drop cut short in the air (a respawn), through `WarpTo`.

- `isDropping`, from leaving the floor to the end of the recovery, stops a capture from cancelling
  the crossing (as `isRiding` does) and makes `CanReachPlayerNow` false. It never grabs in the air
  or on landing.
- `NavRoute.CrossedDrop` makes a route down a drop count as crossing floors, so `Traversing` holds
  the approach as it does for the lift. The two ends have to be consecutive corners, top then
  bottom: a drop's ends are a metre or two apart in plan, and a path past both by the stairs did not
  use it.
- **The commitment ends with the drop (D29).** A drop is crossed well inside `ElevatorCommitTime`,
  and `ya se comprometio con el montacargas` kept the Nemesis in `Traversing` after landing.
  `HasJustEndedDrop` is folded into the facade's `HasGivenUpOnElevator` for 0.5 s. That rung already
  asks it, so there is no new predicate and no ladder change. Since the 27/09 playtest a completed
  lift ride does the same (`HasJustEndedRide`). The ride eats most of the window but not all of it,
  and a noise from below the shaft sent the Nemesis straight back into the cabin.
- **It gives a drop up for a player up here (D30)**, while it looks or takes off. This is measured by
  height (is the belief closer to the top or to the landing?), not by the route verdict, which flips
  when measured from a point on a link.
- A drop used or given up is suspended for `SO_NemesisData.dropLinkCooldown` (8 s) through
  `activated`, and only `RestoreSuspendedDrops` or `OnDisable` brings it back: the same rule as an
  abandoned shaft.
- The geometry is pure and EditMode-tested in `WIRED.Nemesis.Logic` (`DropPath.cs`: `DropArc`,
  `DropPath`, `DropTuning`, `EDropKind`). The traversal, the gizmo and the validator all fly the same
  path.
- Animation goes through the one-shot channel, `NemesisStateManager.PlayTraversal(EDropPhase)`:
  CrossFade by state name, with `HasState` and a fallback, so a drop works with no clips (today's
  controller has none). Sound is `NemesisAudio.PlayDropCue`, played by code at each phase, not by
  animation events.

### The Director

`NemesisDirector` puts the monster where the tension is **without ever touching the FSM**. It
cannot force `Chasing`, hand over the player's position, or skip a detection range — not because
of layering hygiene, but because an AI the player can read is the product: a director that reaches
into the FSM produces behaviour with no explanation on screen, and once the player suspects the
game is deciding, every genuine piece of tracking reads as cheating too.

Four levers, in ascending order of how much the player notices:

1. **The patrol anchor.** `NemesisController.TryGetZoneAnchor` already biases the next zone towards
   an anchor (the player's own position, `ZoneBiasUsesRealPlayer`). The Director overrides that
   anchor with the pressure zone's centre. Coarse by construction — whole zones, a roll not an
   argmax, a falloff that says "this side of the level" — so it reads as the monster being around.
2. **Route weights.** `NemesisRoute.Weight` is now `authored × pressureMultiplier`. Scaled and not
   set: a route the designer weighted 0.2 stays the least frequent of its neighbours under maximum
   pressure, and one at 0 stays off. Only reaches the **waypoint** roll — the cluster roll reads
   `Cluster.Weight`, which is averaged once at graph build — so anchor and weights divide the work
   between the zone level and the waypoint level rather than doubling up.
3. **Synthetic noise.** `FieldOfListening` sweeps for colliders on its listen mask and reads
   loudness off the collider's radius; it does not care who made the sound. So a Director noise is
   the same object a thrown bottle would be, on the same channel, and pushes the Nemesis to
   `Investigating` exactly as a real one would. Sampled onto the NavMesh — a noise inside a wall
   sends it to investigate somewhere it cannot stand. The layer must be in the Nemesis's listen
   mask; `Start` reports it when it is not, because that failure is otherwise completely silent.
4. **Senses.** A runtime **copy** of `SO_NemesisData` with wider hearing and sight (hearing is
   `NoiseRangeScale` and `ListenRange` together, like the escalation: the cap alone never reaches a
   walking step — plan D32), installed via
   `NemesisStateManager.OverrideData` and thrown away after. A copy because ScriptableObject writes
   in Play mode persist into the asset in the Editor: mutating it directly would leave the boost in
   the project and a designer would find numbers nobody typed. Cloned fresh from the authored asset
   at install time, so edits between two requests are picked up. `OverrideData` pushes to
   `FieldOfView`, `FieldOfListening` and `NemesisPathOracle` too — each keeps its own reference, and
   assigning only the manager's leaves a Nemesis that decides with the new ranges and senses with
   the old.

`NemesisPressureZone` is the unit: an id, a centre, a radius, a gizmo that colours by live
intensity. Deliberately not `NemesisRoute` — a route is a path the Nemesis walks, a pressure zone
is an area a designer wants haunted, and the second should not have to be carved out of the first.

API: `NemesisDirector.RequestPressure(zoneId, intensity, duration)`, `ReleasePressure()`,
`RequestEntrance(zoneId)`. A new request **replaces** the one in flight rather than stacking:
pressure on two zones at once is pressure on none, since the patrol can only lean one way. Every
path out restores what it touched, `OnDisable` included — a Director torn down mid-request would
otherwise leave boosted weights and a cloned asset installed for the rest of the run. Evaluation
runs every `evaluationInterval` (3 s), which is not a cost decision: nothing it does is a reaction
to this frame.

**The staged entrance** (`StageEntranceAsync`) is the Mr. X beat, and the one place a teleport is
allowed. The rule worth keeping is not "never move the Nemesis" but "the player must never lose to
something they had no way to see coming", and two enforced properties buy that back: it arrives
**out of the player's sight** (`arriveOutOfSightOnly`, on by default) and never closer than
`entranceMinDistance` measured **over the NavMesh** — ten metres through a wall is not ten metres —
and then it **waits**, standing still and facing the player for `entranceStareSeconds`. Every other
system here can only make the Nemesis faster; this is the one that makes it slower, deliberately.
The player's short sight range is what makes it land: "out of sight" is a couple of rooms in this
level, so a fair arrival is still a close one.

The hold is `NemesisStateManager.SetExternalHold` — `isStopped` plus a zeroed velocity, the body
and not the decision. The FSM is free to already be in `Chasing`; it just does not get to move yet,
which is the difference between a beat and a lie. The stuck watchdog is suppressed for the window
(a body deliberately standing still with a path is exactly what it fires on) and the animation needs
no help, since `TickLocomotionAnimation` reads the body and finds it still.

It never enters the Hub, and that costs nothing to maintain: the warp goes through
`NemesisStateManager.WarpTo`, which refuses any point not on walkable NavMesh, and the Hub is a
Not Walkable volume. There is no path from the Director to a safe zone and none can be added by
accident.

**Nor does it park the Nemesis at the Hub's door (cheese C5).** Keeping it out of the Hub was never
enough: a zone centred on the Hub pulled the patrol to the entrance, a noise sampled by the door sent
it to investigate the threshold. `NemesisSafeZones` reads back as footprints the Not Walkable volumes
that carry a `SafeZoneMarker` and that a `NavMeshSurface` actually bakes (read-only — they still gate
nothing). The marker is not optional ceremony: Not Walkable also fills solid props — every
`Bridges_support_2` has a `NavMesh Blocker` inside — and counted as refuges those rejected half the
zones in Zona1. With no marker the Director warns on Start and the validator flags it. Every lever
stays `NemesisSafeZones.Clearance` (6 m) away:
`ApplyPressure` refuses a zone whose centre is closer (plan-view), and the noise and entrance
samplers skip points closer on the same floor. `NemesisController.TryGetZoneAnchor` also drops the
real-player bias while the player is **inside** the Hub — otherwise the gravitation itself kept the
monster circling the door for as long as they sheltered. What the Nemesis sensed still counts.

**Pacing (plan §6, phase 5).** With an `SO_DirectorPacing` assigned, `NemesisTension` (same
GameObject, added by the Director if missing) keeps a 0..1 meter — NavMesh proximity, chase, the
player seeing the Nemesis (head → chest raycast through `FieldOfListening.IsOccludedByWall`, never
the camera), hiding with a search nearby; a capture fills it — and a state:
`BuildUp → SustainPeak → PeakFade → Relax`. It never decays in `Chasing`/`Catch`, starts on
`NemesisEvents.OnActivated`, and pauses while the Nemesis is dormant, a cinematic plays
(`CinematicState`) or the escape runs (`Decision.ChaseFloor`). The peak only fires from BuildUp:
Relax begins with the meter still high, and peaking from there looped.

The Director turns the state into levers and still never touches the FSM. Each request carries a
source (`Scripted` = puzzle trigger / API, `RisingSensitivity`, `Retreat`) and a lever set. PeakFade
clears the rhythm's own pressure; Relax presses the zone farthest from the player by NavMesh with
**anchor and route weights only** (no noise, no widened senses: if you walk into it, it chases you
on its normal senses); BuildUp after `quietTimeout` without contact ramps pressure on the player's
zone (Mr. X's anti-stall), paused while the player is in the Hub. Scripted requests outrank the
rhythm: while one is live, pacing waits, and it only ever replaces or clears its own.

**Seeing it.** F9 has `ritmo` and `presión` rows; F10 has *Pico de tensión* / *Saltar silencio*,
which change inputs rather than states. Each `NemesisPressureZone` draws as a cylinder — one disc
per floor it has waypoints on — labelled with id, radius, waypoint count and live pressure, magenta
when it is too close to the Hub or covers no waypoint; selected, it draws a line to each waypoint it
covers. Selecting the Director shows coverage: waypoints outside every zone, and the Hub with its
6 m band. `PuzzleTrigger.zoneId` is a `[PressureZoneId]` dropdown.

### Stuck detection escalates

`NemesisStuckEscape` had one response to everything — teleport — which is the strongest move
available and the one that reads worst. Most of what it fired on was a corrupt path rather than a
wedged body: a destination issued mid-warp, a path computed against geometry a door has since
carved, a partial path being walked dutifully to its end. All of those are fixed by asking the
navigation system again.

So the first no-progress window buys a `ResetPath` + `SetDestination` to the same target and a
shorter second window (`SO_NemesisData.StuckRepathGrace`, 1.5 s — it has already spent a full
interval going nowhere); only the second buys the warp. Off the NavMesh entirely skips straight to
the warp, since there is no path to repair. The stage resets on any progress, so an episode has to
be continuous to escalate.

One subtlety worth not undoing: `ResetSample()` (public, for teleports) clears the stage, while the
private `ResetProgressSample()` used by `Tick`'s own early-outs does not. The frame after a repath
reports `pathPending`, which reads as "not trying to move" — clearing the stage there would forget
the repath had happened and the watchdog would repath forever, never escalating.

`RepathCount` / `WarpCount` are surfaced through the state manager onto the debug HUD ("trabas").
The split is the point: repaths are the cheap fix working, warps are the body genuinely wedged.
Warps climbing in one corner is a NavMesh bake or a misplaced waypoint, and no tuning in
`SO_NemesisData` will fix it.

**Where the warp lands** (22/09, WIR-028 / WIR-050). It used to be the nearest hidden waypoint in a
straight line, which is the `Vector3.Distance` mistake this project bans in a level with floors: the
nearest marker through the air could be on the other floor, on an island cut off from everything
(the floor of the freight elevator's shaft, where it landed, could not path out and warped to the
same marker again), or 1.4 m from the trap it was escaping (the top of Zona1's stairs, straight back
into the funnel it wedged in). The pick is now tiered — hidden from the player first, then able to
reach where it was going (the agent's destination, else the belief; a filter, never a pull towards
the player), then at least 3 m from where it wedged — with walking distance breaking ties, and a
50 m penalty for a candidate it cannot walk to from where it stands, so another island is only taken
when it is the way off a stranded one. Each requirement relaxes in order when nothing meets it, so a
level with few waypoints degrades to the old pick instead of leaving the Nemesis wedged.

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

`NemesisSafeZones` does not change that. It only reads the same volumes back ("is this point in the
Hub?", "how far from it?") so the Director can stay away from the door — see *The Director*.

Two readers outside the Nemesis use the same answer: `SocketEmissionShift.dimOutsideSafeZone` caps the
pressure regulator's glow while the player is out of the Hub, and `SafeZoneAlert` raises the
`|| Safe Zone ||` HUD alert (`HUDMessageEvents.ShowAlert`) every time the player walks in. The alert is
a level object (in Zona1, `Safe Zone Alert` under `---- SISTEMA ----`): in at the volume's edge,
re-armed only `rearmDistance` (1.5 m) out so the doorway does not repeat it, and the position is only
read while the player has control, so arriving behind a black screen is announced when control comes
back. It replaced the one-off alert on ARC_CTX_01, which now has none.

One consequence worth knowing: this only blocks *movement*. The Nemesis can still **see or hear**
the player inside the Hub if line of sight allows it (e.g. through a doorway) — it just cannot walk
in. If that turns out to read as a bug in playtest ("it grabbed me through the door" is different
from "it followed me in"; the Not Walkable area only prevents the second), the fix is back on the
sensor side: either extend the Not Walkable volume to cover the doorway approach, or block sight at
the door with a physical barrier the vision/hearing raycasts already respect.

### Visual Systems

**Vision Fog**: Fullscreen Shader Graph pass (`VisionFog.mat`) driven by `VisionRangeController.cs`. Sets `_PlayerPos`, `_VisionStart`, `_VisionEnd` as shader globals. Range lerps between `visionEndDark` (6m) and `visionEndLit` (25m) based on `RenderSettings.ambientLight` luminance. Guard: if `visionEnd <= visionStart`, the shader passes through unchanged — prevents a black screen when the controller is inactive.

**Item Highlight**: `ItemProximityHighlight.cs` sits on the interactable's root in the Father prefab (so variants inherit it) and lights every renderer under that interactable while the crosshair is on it, per material slot via `MaterialPropertyBlock`: `_TintIntensity`/`_EmissionIntensity` on ItemPSX/PSXIndustrial, added `_EmissionColor` on URP/Lit (Emission must be on). Values and colours come from a shared `SO_HighlightProfile` (`ScriptableObjects/Highlight/`): `SO_Highlight_Items` (tint 0.15→0.4, emission 0→0.2, colour from the item's category) and `SO_Highlight_Interactables` (emission only, one colour for all props). `Tools > Interactables > Set Up Highlights` wires it; `Tools > Items > Validate Interactable Highlights` checks it. See `docs/Materials-System.md` §5.3.

**Color spec rules** (`color_visual_language_spec.docx` in Downloads): `#CC1A1A` red is exclusive to danger/emergency lights. `#FFC850` amber is exclusive to the player device. No outlines or waypoints — items are distinguished only by tint and emission.

**Renderer Feature order** (`PC_Renderer.asset`): SSAO, then Vision Fog (BeforeRenderingPostProcessing, 550), then at AfterRenderingPostProcessing (600, where list order decides) SecurityCameraFeed, PlayerCameraFeed and PS1Effect last. Fog must precede PS1 so world-space coherence is preserved before the pixelation pass; the two camera feeds must precede it so their burnt-in overlay is pixelated with the picture.

**Camera feeds**: `SecurityCameraFeed` (security shots) and `PlayerCameraFeed` (the player's FreeLook rig) each drive a fullscreen pass that is only enqueued while their Cinemachine camera is live on the brain — see `docs/Materials-System.md` §7.3. The player feed also draws the camera's boots — the wake-up's (`WakeUpCinematicView`, `OpeningStyle.CameraBoot`) and its own reboot after a capture — through the shared static state in `PlayerCameraBoot`, and opens the lens for their fisheye through `PlayerCameraFeed.LensFovOffset`, which `CameraSprintEffect` — still the only writer of the lens FOV — adds on top.

### Footsteps and breathing (`FootstepEmitter`, `HiddenBreathing`)

Both walkers step through **one** component. `FootstepEmitter` fires a footstep every time its
transform has covered one stride, and `SO_FootstepBank` says which clips that maps to.

**Two cadence sources, and today both walkers use the same one.**

`Distance` fires a step every `strideLength` metres. It is the model the component was written
around — a `NavMeshAgent` has no footfall events, its steps would speed up in Chasing without this
ever reading the FSM or `SO_NemesisMovement`, and they stop on their own when it is blocked by a
wall or a `NavMeshObstacle`. **Nothing in the project uses it.** The Nemesis was moved off it in
`26f750d` to fix step saturation, and its `strideLength` (1.6) and `crouchStrideScale` have been
inert ever since: in `AnimationEvent` mode both are ignored.

The consequence is worth knowing before retuning anything. `NemesisController.controller` plays the
`TLLStalker` clips — `E_Walk` for both `Patrol` and `Chase`, the only clip carrying `Step` events (two
per cycle, imported in `TLLStalker.fbx`) — at the *Speed* of each state (×4 and ×4.5), so the
monster's cadence is that playback speed, switched by the `Walking`/`Running` bools out of
`ApplyGaitToAnimator`. It does not track `SO_NemesisMovement` speeds at all. Going back to `Distance` for the Nemesis is a legitimate change and would restore
that link; it means re-deriving `strideLength` against the chase speed, not guessing it.

`AnimationEvent` fires on an event in the clip, and it is what the **player** uses. The distance
model assumes the animation's cadence follows the speed, and this project's Mixamo clips do not:
`Walking` and `CrouchedWalking` are both 32 frames at 30 fps and play at a fixed rate, so the feet
land at the same 1.94 steps/s whether the character is moving at 2.5 m/s or crouched at 1.25 m/s.
Under `Distance` crouching therefore gets half the steps of walking, which is wrong — crouching
should be the same cadence, only quieter. The events settle it by construction.

The footfall frames are authored in the FBX importers: **Walking 10 and 24, Running 7 and 17**,
CrouchedWalking copying Walking. Imported clips are read-only as assets but the importer stores
events in the `.meta`, so no clip had to be duplicated. Unity delivers an AnimationEvent to the
GameObject owning the Animator, which here is the rig child rather than the root the emitter sits
on — hence `FootstepAnimationRelay`, whose `Step` is what the clips actually call.

`strideLength` still matters as the Distance fallback and is derived, not guessed: speed divided by
the clip's real footfall rate. 2.5 / 1.94 = 1.29 m. The first pass shipped 0.85 m, which is 2.94
steps/s at that speed — a jog cadence under a walk animation, and the first thing playtesting
caught.

**The teleport guard is load-bearing.** The Nemesis is warped — `NemesisStuckEscape`, the spawn
placement, `NemesisElevatorLink`. A warp is displacement with no walking in it, so any single-frame
move over `teleportThreshold` drops the accumulator instead of spending it. Without it a 30 m warp
fires thirty footsteps in one frame.

**Surface resolution**, in order: a `FootstepSurface` marker on the collider or any parent, then the
first bank entry whose `layers` mask claims that layer, then the bank's fallback. The marker is the
intended authoring path — this project has one Unity tag in total and one PhysicMaterial, so there
is nothing else to read. Only the Water entry claims a layer; giving Ground or Default to an entry
makes it swallow every floor and no marker is ever reached.

**Clip choice is a shuffle bag, not a random draw.** Independent draws over four clips repeat back
to back about a quarter of the time, and a repeated footstep is the loudest tell that a sound is
canned. The bag plays each clip once per cycle and refuses to open a new cycle on the clip the last
one ended with.

**Playback goes through the pool**, via `AudioManager.PlayClip` — 3D at the world point where the
foot landed, exactly like `DoorInteractable`. That is more correct than a source parented to the
walker, not just less code: a parented source keeps panning and dopplering as the walker runs past
the listener, and a footstep belongs where it happened.

**Occlusion lives here and not in the pool.** One `Physics.Linecast` per step from the foot to the
AudioListener against the Wall layer; a hit drops the step to `occludedVolume`. The pool has no
occlusion of its own, and without this the Nemesis's steps came through at full rolloff volume from
anywhere in the level — which is the difference between "the monster is somewhere" and "the monster
is over there". Wall only, never Ground: occluding on floors is technically true and makes a
storey's worth of vertical proximity inaudible.

**Overlap is the enemy, and clip length is how it is controlled.** Steps that outlast the gap
between them stack, and stacked copies of a moving source both clip and smear the localisation.
The Nemesis at chase speed 3 m/s with a 1.6 m stride steps every 0.53 s, so its clips are trimmed
to 0.34 s. The first pass had a 1.1 m stride against clips averaging 0.64 s — two voices overlapping
at all times, reported from playtest as "saturated and not coming from anywhere".

**Neither component generates noise.** Noise here is a sphere the movement states own per gait; a
second writer to its radius is how a player ends up permanently loud. See *Noise is a sphere, not
an event*.

`HiddenBreathing` plays a loop **only while `PlayerStateManager.IsHidden`**, which today is toggled
only by the `R` debug key — it needs no changes when the hiding system lands. It owns a source
because it fades (the shared pool returns no handle), and it reuses the previously-unused
`AudioSource` already sitting on the player prefab root rather than adding a second one. It swaps to
the ragged M2 clip when `ChestPenaltyActive`, chosen per episode so the swap never restarts a breath
mid-loop. Fully 3D at 3–15 m linear: this is a third-person camera, the listener rides it a few
metres back, and the breath is meant to come from the body on screen.

The banks live in `_Project/ScriptableObjects/Audio/Footsteps/` — 45 player clips across six
surfaces, 48 references for the Nemesis.

**The Nemesis has no per-surface recordings**, so its bank synthesises them instead of faking a
default. Its nine real recordings are the **Metal** surface — the audio spec describes its steps as
"pesados y metalicos", so that is what they are, not a generic fallback — and the other five
surfaces **reference the player's clips with a pitchRange centred near 0.82**. Three semitones down
is about 20% longer and much deeper: the same material under a far heavier body. No extra audio
files exist for this; the emitter already applies pitch per shot, so the whole thing is a tunable
number in the asset. `fallbackSurface` is Metal, so an unmarked floor still sounds like the monster
rather than like a heavy player.

**The clips were cut from the source recordings, not sourced as one-shots.** The originals were
long takes — concrete was 45 s — and playing one as a "footstep" left overlapping copies piling up.
The cutter finds onsets with an RMS envelope and a Schmitt trigger, keeps the first five steps of
each take, and writes them capped at 1 s with short fades so nothing clicks. Two things worth
knowing before re-running anything like it: `water_01.mp3` was the **same take** as
`water_03.wav` (onsets identical to the millisecond) and was dropped rather than split into
duplicate pairs; and outputs land in the same folder as the sources, so every source has to be
decoded before any output is written or a source gets overwritten before it is read.

**Pitch is varied twice, deliberately.** Each clip carries a baked varispeed from an even ladder
across +/-5%, so the clips have distinct identities; `FootstepEmitter` then draws a fresh pitch per
step out of the bank's `pitchRange`, which is kept narrow (+/-3%) precisely because the two
multiply. Metal is the exception — two clips, no baked ladder — so its runtime range is wide
(+/-8%) and does all the work. If steps ever start reading as different shoes, `pitchRange` is the
knob to reach for; the baked half needs a re-export.

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
- `SO_CaptureGrabConfig` — the capture's grab: the pair's distance and facing (written by the setup
  tool, not typed in), the shot's length, framing and fog. See *The grab*.
- `SO_FootstepBank` — footstep clips per surface, plus the M1 limp drags. Content only; the stride
  lives on the `FootstepEmitter`, because stride belongs to the body and not to the floor.
- `SO_AmbienceProfile` — one per area character. All six the ambience doc specifies now exist in
  `ScriptableObjects/Audio/Ambience/` (moved there from `Audio/Music/`, GUIDs intact), plus
  `Amb_Pink` for debug. Only three bed loops exist in the project, so Corridor/Machine/Vertical
  share a pairing and are separated by mix rather than by material — see `docs/Ambience-System.md`.
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

Volume envelopes use `Time.unscaledDeltaTime` (a fade frozen mid-way by a modal is audible); the event timer uses scaled `Time.deltaTime` plus an `IsPaused` guard (a frozen timer is not). The noise, drones and placeholders are already baked in `Audio/Ambience/Generated/` — Layers 3 and 4 need no sourced audio at all.

## Current state — what is and is not wired

The systems below are **implemented but not connected to anything**. Read this before assuming a feature works end to end. Verified against the code — if you fix one of these, delete the line.

**Still not wired:**

- **There is no win condition.** `GameResultManager.ReportWin` has no caller at all — the debug `WinLoseTest.cs` that used to call it (key `I`) was deleted. The only reachable ending is the Nemesis catching you.
- **`PuzzleController.CompletePuzzle()` and `PuzzleReward.GiveReward()` have zero callers.** The per-type controllers and `SequencePanelInteractable` write straight to `PuzzleStateManager` and bypass the generic wrapper entirely. Decide whether `PuzzleController` is the intended layer or dead code before building on it.
- **The skill check works but nothing in the level opens it.** `SkillCheckController` (LevelUI, canvas `SkillCheckCanvas.prefab`, data `ScriptableObjects/Puzzle2/SO_SkillCheck_Ventilation.asset`) plays the Dead by Daylight sequence and moves the active module's timer through `ApplyTimePenalty` / `ApplyTimeBonus`. Its only caller is the debug `SkillCheckTestKey` (F6, editor/dev builds), which just logs the result. Missing: the Hub panel that calls `Open(data, completed => …)` and completes the M2 puzzle, and the spec's progressive shake/ambience calm-down between checks.
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
  clip changed. Still missing: UI audio, the capture stinger, the activation cue, and content for
  `NemesisAudio.stateLoops`; the **ambience system** (`_Project/Scripts/Ambience/`) is built but
  ships with placeholder clips. `NemesisChaseMusic` is **done** — clip assigned, sitting in
  `WIRED_Zona1_Blockout`, correctly on the Music bus (it was on the Nemesis bus once, which made it
  ride the SFX slider) with the `AmbienceController` wired for the duck.
- **Most of the audio that exists on disk still cannot be played.** `_Project/Audio/` holds ~90
  clips and the project has **16 `SO_SoundData` assets**. Footsteps and hidden breathing now reach
  the game through `SO_FootstepBank` and direct clip references instead — see *Footsteps and
  breathing*. Every module sound, the Nemesis voice lines, the light and generator audio, the
  save-point set and the UI set are still unreachable. For a fixed one-sound-per-event case the
  blocking step is authoring the SO and dragging it into `AudioManager.sounds`; for anything with
  variations, use a bank and the `PlayClip` overloads rather than minting twenty dead SOs.
- **The module system is almost silent.** Only the countdown speaks: `ModuleTimerBeeper` plays the
  tick clips from the start of the module, getting faster up to the last 10 s. `ModuleManager` still runs activations, explosions, penalties and
  resolutions without a single audio call, and the rest of `MOD_01`–`MOD_09` have clips waiting.
- **There is no music system.** One chase track played by `NemesisChaseMusic`. No `MusicManager`, no
  zone/exploration music, no stinger, no menu or ending piece — `AudioManager.PlayMusic` has zero
  callers. See *Spec deltas — Music*.
- **Light is not an input to detection.** No `lightLevel`, no `playerVisibilityFactor`, no visual
  adaptation and no generator; `FieldOfView` has no light term. The vision fog changes what the
  player sees, not what the Nemesis sees. See *Spec deltas — Light*.
- **Audio does not respond to pause.** `MasterMixer.mixer` has the eight buses but only the default snapshot, and `NemesisChaseMusic.Update()` runs on `Time.unscaledDeltaTime` without an `IsPaused` guard — so chase music keeps playing over the pause menu. Needs a `Paused` snapshot driven from `PauseManager.OnPauseStateChanged`.
- **Save/load is a stub.** `SaveSlotsController` logs and raises an event; `InventoryManager.RestoreFromIDs` has no callers. `PuzzleStateManager.Snapshot()`/`RestoreSnapshot()` exist and work, but only in memory, for checkpoints — there is no disk format.
- **`EPlayerState.InDanger` was removed**, along with its `isInDanger` field and the `T` debug key — it was never registered in the state dictionary, so transitioning to it only ever logged an error. `PlayerHiddenState` is still inert (no collider/visibility change) and the `R` (hidden) and `Y` (disabled) debug keys are still live in `PlayerStateManager.InputUpdate`; `R` goes away when `HidingSpotInteractable` lands.
- **The two parallel grab/push implementations are resolved.** The physical-box version stayed (`GrabbableBall` + `PushBoxTriggerLogic` + `BallPuzzleItem` + `BasketTrigger`); `ContainerInteractable`, `ContainerSlot` and the dead `PushableBall` were deleted, so nothing competes for the `SetContainerSlot` keys any more. That key is a **BallId** — `SO_ContainerPuzzleData.ContainerRequirement.containerId` keeps the old field name but is authored with a ball id. `GrabbableBall` now carries its `PauseManager.IsGameplayInputBlocked` guard, and `BasketTrigger` caches the controller lookup instead of running two `FindObjectsByType` scans per trigger crossing.
- **Three Editor tools were deleted as stale**: `Door/Setup Door Visual` (reflected on `leftPanel`/`rightPanel`, fields the hinged door no longer has — running it disabled the root MeshRenderer and added two stray cubes), `Puzzle UI/Setup Sequence Panel UI` (drove the View through `SetPrivateField`, so every View refactor broke it; the panel prefab is maintained by hand now) and `Scenes/Build Testing Blockout`. `WinLoseTest.cs` and `TestSceneBuilder.cs` went with them.
- *(Stale since 19/09: the asset ships at 0.6, plan Phase 0.)* **`SO_NemesisData.patrolWaitVariance` ships at 0**, so the wait at every patrol waypoint is still
  the same length every time and a player who has timed one round has timed them all. It defaults
  off on purpose — the variance is expressed as +/- seconds around the authored wait, and no code
  default can know what that authored value is without retuning existing assets. Set it to about
  `0.6` to switch the feature on.
- *(Stale since 21/09: built — see* Hiding spots *and the plan's Phases 1–2.)* **The hiding system does not exist.** `EPlayerState.Hidden` is registered, `PlayerHiddenState` is
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
- **The escalation is visible only where the Nemesis hunts.** `NemesisEscalation` raises sight,
  hearing and route variation with the completed puzzles (plan Fase 7), but in Zona1 the Nemesis
  sleeps until the escape (plan D25), so there it only ever shows at the top tier. Zona 2 is where it
  will be felt; its thresholds (0 / 2 / 3 puzzles) will need a look then.

**Wired since this section was last written** — kept here because the old text said otherwise and people still quote it:

- **Module timers do run.** `ZoneTrigger` → `ModuleManager.ActivateModule`, and `PuzzleStateManager.OnPuzzleCompleted` → `HandlePuzzleCompleted` → `ResolveModule` closes the loop. Explosion, penalty and `BlindnessOverlayView` all fire.
- **Retry does reset run state**, through `GameSession.BeginNewSession()` and `ISessionResettable`.
- **`SO_NemesisMovement` is fully consumed.** `NemesisLifecycle.ApplyMovementTuning` applies `AngularSpeed`, `Acceleration` and `StoppingDistance`; the four state speeds are all assigned. `InvestigationTimeOut` and `NoiseUpdateCooldown` are both read.
- **Nemesis trigger overrides no longer throw** — they are empty, which is correct: the FSM is driven by the sensors, not by triggers.
- **Transitions no longer live inside the states.** `NemesisDecision` decides, the states only act. Anything in this file describing a state as "transitioning to" another is out of date; look at `SO_NemesisPriorities` instead.
- **Vision is gradual in the peripheral band**, chasing predicts and can route through a waypoint to open an angle, and searching rolls its target from the last known position rather than sweeping outward from its own feet. All three are on by default.
- **`DoorInteractable` no longer has a blocking-collider path at all.** Whether the Nemesis can pass is decided by `nemesisCanOpen` plus the NavMesh (a `NavMeshObstacle` with Carve), not by toggling colliders.
