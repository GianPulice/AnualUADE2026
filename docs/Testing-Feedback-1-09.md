# Guía de testeo y cableado — correcciones post feedback 1/09

Todo lo que cambió, qué hay que **cablear a mano** antes de que funcione, y qué **probar** para
confirmarlo. Ordenado para que se pueda recorrer de arriba a abajo en una sola sesión.

> **Escena de referencia:** `Scenes/GameScenes/WIRED_Zona1_Blockout`, arrancando desde `Bootstrap`.
> Para lo del Nemesis conviene además `Scenes/Dev/NemesisTestbed`.

---

## PARTE 1 — Cableado pendiente (sin esto, no funciona)

Cinco cosas están codeadas pero **inertes** hasta que se toquen en el Editor. Están ordenadas por
cuánto rompe no hacerlas.

### 1.1 🔴 Sonido de puerta — falta el asset

Sin esto las puertas siguen mudas y no se puede oír al Nemesis abrirlas.

1. Crear `SO_SoundData` en `ScriptableObjects/Audio/SFX/`, **con el nombre exacto**
   `sfx_interaction_puerta_abrir` (o poner ese texto en su campo `id`).
2. Asignarle el clip.
3. `Category` = SFX.
4. **Importante:** poner `Max Distance` ≈ **25**. El default de Unity es 500, que es audible en todo
   el nivel y convierte la distancia en cero información — que es justo lo contrario de lo que la
   feature busca.
5. Agregar el asset a la lista `sounds` del `AudioManager` en la escena `Data`.

*No hace falta tocar ninguna puerta:* el id sale de `SO_DoorData.DefaultOpenSoundId`, así que las 110
instancias lo toman solas. Una puerta que necesite otro sonido lo overridea en su `SO_DoorData`.

### 1.2 🔴 Toast de pickup — falta el GameObject

1. En `HUDCanvas` (escena `LevelUI`), crear un GO hijo, p. ej. `ItemPickupToast`.
2. Agregarle: un `TextMeshProUGUI`, un `CanvasGroup`, un `UISlideTransition` y el componente
   `ItemPickupToastView`.
3. En el `UISlideTransition`: **Feedback Style = ON**, **Auto Hide = ON**, `Ignore Time Scale = ON`.
4. En el `ItemPickupToastView`: asignar `slide` y `label`; `Direction` = FromBottom.
5. Anclarlo abajo a la derecha, **con márgenes fijos** (stretch + inset), no fracciones — ver
   `docs/UI-System.md` §7.5.

### 1.3 🟡 Sonido de pickup por categoría

`PickupInteractable` gana un campo `categoryConfig`. **Está sin asignar** en `InventoryItem.prefab` y
`Note.prefab`, así que el fallback no corre todavía.

Asignarles `ScriptableObjects/CategoryConfig/ItemCategory.asset`.

> ⚠️ **Esto tiene un problema de diseño conocido:** exige acordarse de asignarlo en cada prefab de
> pickup nuevo — el mismo modo de falla que vinimos a arreglar. Hay una alternativa (mover el mapa
> categoría→sonido al `AudioManager`, cero wiring por prefab, ~20 líneas). **Está sin decidir.**

Los ids `sfx_interaction_key` y `sfx_interaction_nota` ya quedaron cableados en el asset de categorías
para Key y Note. Faltan los de Component y Special.

### 1.4 🟡 Montacargas — dos ajustes en el prefab

1. **`RideButton` → Layer `6 Interactable`, y su Collider con `Is Trigger` = ON.** Hoy está en
   Layer 0 y sólido. Funciona igual (el probe tiene un fallback), pero es frágil: ese fallback
   resuelve *sólo el blocker más cercano*, y el collider de la cabina también está en Layer 0 y
   puede ganarle.
2. **Sacar el objeto `Colider` de abajo del `RideButton`.** Hoy es hijo del botón, así que viaja con
   la cabina y hereda su escala `(1, 0.219, 0.225)` — queda una lámina de 7 mm a la altura del pecho
   justo donde te parás. **Esa es la causa de que no te pudieras mover.** Va en el landing de
   **arriba**, estático en el mundo, en Layer `11 Wall`, con el componente `ElevatorLandingBarrier`.

### 1.5 🟢 Correr el validador de highlights

`Tools/Items/Validate Interactable Highlights`. Un click. Define si el emissive de proximidad falla
por material, por Renderer o porque el pixelado se lo comía.

---

## PARTE 2 — Testeo por sistema

### 2.1 Render / pixelado

- [ ] La cara del personaje **se lee**. Antes la resolución efectiva era de 200 filas por una segunda
      cuantización oculta (`_JitterResolution`), no las 512 que decía `_PixelSize`.
- [ ] En Play, seleccionar `PS1Effect.mat` y mover `_PixelSize` → el pixelado responde de verdad.
      **Número más alto = menos pixelado.**
- [ ] Las scanlines son sutiles, sin moiré (bajaron de 0.125 a 0.06).

### 2.2 Player — movimiento

- [ ] **Soltar el input y frenar es inmediato.** Antes seguía caminando ~333 ms a velocidad completa,
      porque `Input.GetAxis` es el eje suavizado y los ejes están con `gravity: 3`.
- [ ] Correr y soltar de golpe: no hay deslizamiento.
- [ ] Caminar en diagonal: **la velocidad es la misma** que en recto (el `inputDir` se normaliza).
- [ ] Con gamepad, si hay: el stick sigue siendo analógico.

> Si ahora se siente **demasiado** seco, el lugar correcto para agregar peso es una deceleración en
> `SO_Movement`, **no** volver al input suavizado.

### 2.3 Player — captura

- [ ] Dejarse agarrar **corriendo**: la animación corta, no sigue caminando.
- [ ] Dejarse agarrar **agachado**: ⚠️ este es el caso que fallaba. El rig no debe saltar a
      `Walking`/`Running` de pie.
- [ ] El player **no se desliza** durante la captura (antes coasteaba durante los 1.5 s de
      `captureCutsceneDelay`).
- [ ] Después del respawn: se mueve normal, y si tenía penalización de piernas **la conserva**.

### 2.4 Nemesis — spawn

- [ ] Completar el puzzle que lo activa **mirando de frente** hacia los spawn points: **no aparece**
      a la vista. Espera y aparece cuando te movés o girás.
- [ ] Repetir 3 veces: nunca aparece cerca ni en tu campo de visión.
- [ ] Si en consola sale *"No spawn point is far enough, out of the player's view cone AND behind
      cover"*: los puntos están mal ubicados. El mensaje dice cuántos fallaron por cada causa.
- [ ] Con `NemesisDebugHUD` (**F9**): mientras espera, figura como no activo.

### 2.5 Nemesis — navegación (requiere rebake)

- [ ] Ventana Navigation: el Hub queda **sin navmesh**, no sólo con costo alto.
- [ ] Entrar al Hub perseguido → el Nemesis **se queda afuera**.
- [ ] ⚠️ Confirmar que **no te agarra por el vano de la puerta**. `Not Walkable` bloquea el
      movimiento, no los sentidos, y la captura es un chequeo de distancia (2 m).
- [ ] `Tools/Nemesis/Validate Navigation Setup` → sin problemas en la escena real.

### 2.6 Nemesis — puertas

- [ ] Perseguido, cruzar una puerta cerrada → el Nemesis **la abre y pasa por el vano**, no atraviesa
      el panel.
- [ ] Una puerta con `nemesisCanOpen = OFF` → no la cruza.
- [ ] Con la puerta **abierta**, el Nemesis pasa sin trabarse (el carve rota con la hoja).
- [ ] **Se oye** cuando la abre, y se distingue **de qué lado** viene.
- [ ] A ~30 m ya no se oye (si no, revisar `Max Distance` del `SO_SoundData`).

> **No hace falta rebakear por las puertas**: el `NavMeshObstacle` se agrega en `Awake` y carva en
> runtime.

### 2.7 Montacargas

- [ ] Caminar dentro de la cabina → **no arranca sola**.
- [ ] Se puede **atravesar la cabina caminando** sin que se vaya con vos.
- [ ] Panel del landing → la cabina viene.
- [ ] Botón de adentro → viaja al otro piso, **y te lleva**.
- [ ] Piso 2 sin cabina → la pared invisible **no te deja caer al hueco**.
- [ ] Llega la cabina → la pared **se desactiva** y podés entrar.
- [ ] Se va la cabina → la pared **vuelve**.
- [ ] El Nemesis reclama la cabina → el botón se rehúsa y avisa *"Freight elevator in use"*.

### 2.8 Audio

- [ ] **Pausar durante una persecución**: la música baja con un fade de ~0.15 s, no de golpe. ✅ ya
      confirmado.
- [ ] Los clicks del menú **siguen sonando** en pausa (UI está exento del duck).
- [ ] Despausar → el audio vuelve **al instante**, sin fade-in.
- [ ] Pausar y despausar rápido varias veces: no queda audio a medio volumen.
- [ ] **Slider de Music**: mueve música, ambiente **y** la música de persecución, los tres juntos.
- [ ] **Slider de SFX**: ya **no** toca el ambiente.
- [ ] Recoger un ítem → suena, y se oye que viene del objeto (es 3D).

### 2.9 Options

- [ ] Abrir Options en la pestaña **Volume**, cerrar, reabrir → **abre en Volume**.
- [ ] Cambiar de pestaña y salir con **Back** → igual recuerda la pestaña (es estado de navegación,
      no preferencia).
- [ ] Cerrar el juego y reabrir → sigue recordándola.

### 2.10 Inventario

- [ ] Las filas se leen como listado de directorio alineado: `03 MECHANICAL_CORE ......[CMP]`.
- [ ] Seleccionar una fila → **barra roja de 2 px a la izquierda** + fondo tintado. Sin barrido
      animado.
- [ ] Hover sobre una fila **no seleccionada** → cambia el fondo.
- [ ] Hover fuera de la seleccionada → **sigue viéndose seleccionada**.
- [ ] Abrir/cerrar 10 veces y agregar/quitar ítems: **ninguna fila del pool** conserva selección o
      hover del uso anterior.
- [ ] El toast aparece al recoger, y con varios pickups seguidos **encola** sin superponerse.

### 2.11 Puzzles

- [ ] **Panel eléctrico**: el keypad va `1 2 3` arriba, `7 8 9` abajo, `0` centrado.
- [ ] Completarlo de punta a punta: los números que se ingresan son los que se aprietan.
- [ ] **Cajas**: empujar y completar el puzzle; `OnPuzzleCompleted` dispara **una sola vez**.
- [ ] Abrir el inventario con una caja cerca → **no** se puede agarrar (guard de input nuevo).

### 2.12 Escena de testeo del Nemesis

`Tools/Nemesis/Build Nemesis Test Scene` genera `Scenes/Dev/NemesisTestbed`. **No bakea.**

- [ ] Bakear a mano.
- [ ] La sala **verde** (SafeRoom) queda sin navmesh; la **roja** (BrokenRoom) conserva el suyo —
      esa diferencia es el bug de layer, reproducido a propósito.
- [ ] `Validate Navigation Setup` reporta **exactamente un** problema: el volumen roto.
- [ ] **F10** abre la consola de test; **F9** el HUD de debug.
- [ ] Los botones de la consola **arman situaciones** (Nemesis atrás/adelante, esconderse, capturar).
      No fuerzan estados a propósito: un segundo escritor de `NextState` hace que el FSM transicione
      cada frame y no ejecute ninguno.

---

## PARTE 3 — Warnings pendientes de decisión

| Warning | Estado |
|---|---|
| `ElevatorSwitch`: transición `AnyState -> Down 0` sin condición | Es una transición **huérfana** — `ElevatorCallPanel` no tiene referencia a Animator, o sea que nada la dispara. **Borrarla**, o cablear un trigger para que el switch se anime al apretarlo. Sin decidir |
| `Caja de Fusible.fbx`: polígono auto-intersectante | Arreglo en Blender. Ignorable si no se nota |
| `Input Manager` deprecado | Deuda grande, atada al rebinding de teclas (`docs/TODO-UI.md`) |

---

## PARTE 4 — Lo que quedó sin hacer

- **§G1 zonas para esconderse** — salteado. El sensor ya está (`FieldOfView` ciega al Nemesis con
  `IsHidden`) y los prefabs `Locker` existen; falta el interactuable y darle cuerpo a
  `PlayerHiddenState`. Las teclas debug `R`/`Y` siguen vivas esperando esto.
- **§M3–M6 (UI)** — typewriter, stagger, pause alineado, sonidos de UI, CRT global.
- **§E3 bloquear el montacargas** — pendiente de tu testeo.
- **§H1 problema antes que solución** — level design.
- `AmbienceComfortApplier` y `GlitchController` no están en ninguna escena.
