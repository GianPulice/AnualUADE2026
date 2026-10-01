# Sistema de ambiente sonoro

Ambiente de fábrica abandonada en 4 capas constantes más una capa de eventos 3D aleatorios. Sin música durante la exploración normal.

```
AMBIENTE CONSTANTE      Factory bed (×2 loops de largo coprimo)
INCOMODIDAD SUBCONSC.   Pink/brown noise + 17 Hz + drone 32 Hz
EVENTOS DEL EDIFICIO    One-shots 3D aleatorios con silencios largos
```

Código en `Assets/_Project/Scripts/Ambience/` (más `AmbienceComfortApplier` en `Scripts/Managers/`), scripts de los ScriptableObjects en `Assets/_Project/Scripts/ScriptableScripts/Ambience/`, y los assets (profiles + `SO_AmbienceEventBank`) en `Assets/_Project/ScriptableObjects/Audio/Ambience/`. El rig armado vive en el prefab `Assets/_Project/Prefabs/Audio/AmbienceController.prefab`.

---

## Setup, en orden

### 1. Mixer

El mixer ya tiene 4 sub-grupos bajo `Ambience`: **Bed**, **Events**, **Texture**, **Sub**. A mano en `Assets/_Project/ScriptableObjects/Audio/MasterMixer.mixer`:

| Grupo | Fader | Efectos |
|---|---|---|
| `Ambience/Bed` | 0 dB | — |
| `Ambience/Events` | −2 dB | — |
| `Ambience/Texture` | −3 dB | — |
| `Ambience/Sub` | **−12 dB** | **Highpass** 12 Hz + **Lowpass** 120 Hz |

> ⚠ **Nunca poner un limiter, compresor o Duck Volume en `Master`.** El drone de 17 Hz es inaudible pero sigue siendo un pico *grande*: cualquier dinámica en Master va a duckear toda la mezcla al ritmo de su LFO. El juego entero va a respirar cada 20–60 segundos y la causa es casi imposible de encontrar de oído. Si alguna vez hace falta un limiter, reestructurar a `Master > { Gameplay, Sub }` y ponerlo en `Gameplay`.

> ⚠ **Nunca usar `mixer.SetFloat` para nada de ambiente.** El ambiente sigue al slider de **Música**, no al de SFX: `AudioManager.SetMusicVolume()` reescribe `AmbienceVolume` junto con `MusicVolume` cada vez que el jugador lo toca (`SetGameplaySfxBundle()` ya no lo incluye), y el duck de pausa (`AudioManager.PauseDuck`) lo vuelve a escribir en cada pausa. El balance entre capas vive en los faders fijos de los sub-grupos (el volumen de un hijo es un *offset* que suma en dB con el padre, así que sobrevive) y en `AudioSource.volume`.

**Snapshots de escondite.** El mixer tiene tres snapshots: `Snapshot` (el default), `InsideCloset` e `InsideContainer`. El grupo `Ambience` lleva un **Lowpass Simple** (además de los de `SFX` y `Nemesis`) que en `Snapshot` está en 22 kHz (transparente) y que `HidingSpot` baja al entrar a un escondite vía `SO_HidingData` (locker → `InsideCloset`, 1500 Hz; container → `InsideContainer`, 700 Hz; bajo mesa no cambia la mezcla). Los tres snapshots guardan también **copias** de los faders y filtros de los sub-grupos de la tabla de arriba: si se retoca el balance del ambiente hay que repetirlo en los tres, o esconderse cambia la mezcla.

### 2. Clips generados

Ya generados en `Assets/_Project/Audio/Ambience/Generated/`:

| Archivo | Uso |
|---|---|
| `PinkNoise_20s`, `BrownNoise_20s` | Capa 3. El prefab carga los dos a la vez; el brown se sienta mejor bajo una mezcla PSX |
| `Sub_17Hz_60s`, `Sub_32Hz_60s` | Capa 4. **Finales**, no son placeholders |
| `PLACEHOLDER_Bed_A_37s`, `_Bed_B_53s` | Capa 1, par coprimo. Reemplazar |

Son PCM (ver import settings más abajo). La herramienta que los generó (`AmbienceToneBaker`, `Tools/Audio/Bake Ambience Texture Clips`) se borró el 2026-09-17 junto con `AudioMixerSetup` (`Tools/Audio/Create or Update Master Mixer`); si hiciera falta re-bakear, está en el historial de git (`git show 190b9026^:Assets/_Project/Scripts/Editor/AmbienceToneBaker.cs`) y es determinista (`Seed = 1337`). Los `PLACEHOLDER_OneShot_*` que también generaba ya no están en el proyecto.

**Las capas 3 y 4 no necesitan ningún audio conseguido.** Las one-shots de la capa 2 ya son reales: `SO_AmbienceEventBank` (el único banco, compartido por todos los profiles) tiene 26 entradas — 15 COMMON, 8 UNCOMMON, 3 RARE — con los mp3 de `Audio/Ambience/` más moscas (`Audio/SFX/Animals/FlyBuzzing/`) y ratas (`Audio/SFX/Animals/Rats/`). Todas con `tags = None`. Lo que falta salir a buscar es el bed real.

### 3. Escena de gameplay

En `WIRED_Zona1_Blockout.unity` ya está: una instancia del prefab `AmbienceController` (sin overrides) bajo `---- SISTEMA ----`. El componente `AmbienceController` arrastra a los otros cinco por `[RequireComponent]`. Va en la escena de gameplay, **no** en `Data`: el ambiente es del nivel y muere con él, igual que `VisionRangeController`.

Lo que el prefab ya trae configurado (y lo que hay que repetir si se arma otro a mano):
- Los 4 sub-grupos del mixer en `buses` (`AmbienceBusTable`).
- `defaultProfile` = `SO_AmbienceProfile` (`Amb_Hub_Open`).
- En `AmbienceDriftLayer`, 4 tracks: `Pink Noise` (0.03) y `Brown Noise` (0.05) al bus `Texture`, relativos al bed, y `Sub 17Hz` (0.5, absoluto) y `Sub 32Hz` (0.55, relativo) al bus `Sub`, con `comfortGated` **activado en los dos sub**.
- En `AmbienceBedLayer`, `bedSlots = 2`.
- En `AmbiencePlacementResolver`, `occluderMask` y `solidMask` (abajo).

> ⚠ **`occluderMask` = `Wall`. `solidMask` = `Wall` + `Ground`. NO incluyas `Default`** — ahí vivían los bloques decorativos macizos del blockout que rodeaban el área jugable (`VISUAL_MASS`, `PATIO_CARGA_Mass_*`; hoy `VISUAL_MASS` quedó vacío y en `Props`, y los `_Mass_` no existen), y cualquier volumen así rechaza todo candidato que salga del cuarto actual. Decidir si un punto está fuera del edificio es trabajo del test de NavMesh, no de este.

⚠ Si le ponés colliders a las anclas `AmbienceEmitter`, mandalos a `Ignore Raycast` o hacelos triggers.

### 4. Zonas

Un GameObject con BoxCollider por área y el componente `AmbienceZone` (el `Reset()` ya fuerza `Is Trigger`). Las zonas anidadas funcionan solas: gana la más interna.

El blockout tiene 17 áreas nombradas en 2 pisos. **6 profiles alcanzan** — un `SO_AmbienceProfile` se reutiliza en muchos colliders:

Los seis existen como assets en `ScriptableObjects/Audio/Ambience/` (junto al `SO_AmbienceEventBank`;
antes estaban en `Audio/Music/`, que no era su carpeta). `Amb_Pink` (`SO_AmbienceProfilePink`) es aparte:
nació como profile de debug, con `Sub_17Hz_60s` + `BrownNoise_20s` como bed — pero hoy lo usan dos
zonas de la escena (ver abajo). Ojo: un sub cargado como bed sale por `Ambience/Bed`, sin el
highpass/lowpass de `Sub` y **sin** pasar por el toggle de confort.

| Profile | Asset | Áreas | Carácter |
|---|---|---|---|
| `Amb_Hub_Open` | `SO_AmbienceProfile` | `ENTRADA`, `HUB_01` | espacio grande y vacío, cola larga |
| `Amb_Corridor` | `SO_AmbienceProfileCorridor` | `PASILLO_CARGA`, `PASILLO_OESTE`, `PASILLO_PLANTA`, `PASILLO_TECNICO` | resonancia cercana y estrecha |
| `Amb_Machine` | `SO_AmbienceProfileMachine` | `BOMBAS`, `ANTESALA_BOMBEO`, `TABLEROS`, `MANTENIMIENTO` | zumbido eléctrico presente, caños |
| `Amb_Office` | `SO_AmbienceProfileOffice` | `OFICINA`, `VESTUARIOS`, `SALA_HISTORIA` | seco y silencioso, fluorescentes |
| `Amb_Vertical` | `SO_AmbienceProfileVertical` | `ESCALERA_01`, `MONTACARGAS_01`, `PASARELA` | metal, eco vertical |
| `Amb_Exterior` | `SO_AmbienceProfileExterior` | `PATIO_CARGA` | viento, `subScale = 0` |

**Cómo están diferenciados hoy.** Hay tres clips de bed utilizables en todo el proyecto — el factory
de 30 s y los dos `PLACEHOLDER` de 37 y 53 s — así que salen tres pares coprimos y nada más:
30+53 (~26 min de período compuesto) va al Hub, 30+37 (~18,5 min) a Office, y 37+53 (~33 min) lo
comparten Corridor, Machine y Vertical. Esos tres **suenan a la misma sala** hasta que haya
grabaciones propias; lo que hoy los separa de verdad es la mezcla. (Machine tiene además un tercer
bed track, `Sub_32Hz_60s`, agregado el 2026-09-21, que **no suena**: con `bedSlots = 2` el bed layer
lo ignora y loguea un warning. Si se sube `bedSlots` a 3, ese drone saldría por `Ambience/Bed` sin
gate de confort — mejor sacarlo del profile.)

| | bed | texture | sub | c/u/r | interval |
|---|---|---|---|---|---|
| Hub | 0.32 | 1 | 1 | .72/.24/.04 | 1.43 |
| Corridor | 0.26 | 1.15 | 1.1 | .55/.32/.13 | 0.85 |
| Machine | 0.40 | 1 | 1 | .60/.30/.10 | 0.80 |
| Office | 0.22 | 0.7 | 0.6 | .72/.24/.04 | 1.60 |
| Vertical | 0.30 | 1 | 0.85 | .50/.35/.15 | 0.90 |
| Exterior | 0.30 | 1.3 | **0** | .65/.30/.05 | 1.20 |

El razonamiento detrás de los valores raros: el `sub` sube en Corridor porque la presión de sala es
lo que hace claustrofóbico un espacio estrecho, y baja en Vertical porque una escalera está abierta
hacia arriba y no retiene esa presión. Office es el bed más bajo y el ritmo más vacío del nivel —
son las salas donde el jugador se para a leer, y un golpe encima de un documento es el beat
equivocado. Exterior usa el `BrownNoise_20s` como bed en vez de un room tone: ruido pesado en graves
lee como viento lejano, era el único clip generado que nada más usaba, y un bed de 20 s alcanza acá
porque un loop se reconoce por su contorno y el ruido no tiene.

Dos cosas que corregí de los que ya existían: `Amb_Hub_Open` tenía **un solo** bed track (el tooltip
del SO pide dos) y su `rareWeight` en 0.1, cuando la spec §11 dice que el Hub no lleva elementos de
tensión — quedó en 0.04 con un segundo bed. Y `Amb_Machine` estaba en `eventIntervalScale = 1`, más
vacío que los pasillos, cuando la spec lo llama el ambiente más denso del juego — quedó en 0.8.

El patio exterior (`PATIO_CARGA` en la tabla; en la escena actual el objeto es `PATIO_ART`, sin `_Floor_`/`_Ceil_`) necesita su propio bed sin room tone, y sus eventos de aire quieren `requireNavMeshNearby = false`.

**Zonas en la escena hoy.** La tabla de arriba es el reparto de diseño; lo que está colocado es otra cosa. Hay 6 triggers `AmbienceZone` bajo `--- Ambience Triggers ---`, y no coinciden todos con su nombre:

| Trigger | Profile asignado |
|---|---|
| `Amb_Hub` | `Amb_Machine` |
| `Amb_Corridor` | `Amb_Corridor` |
| `Amb_Corridor (1)`, `Amb_Corridor (2)` | `Amb_Pink` |
| `Amb_Office` | `Amb_Office` |
| `Amb_Exterior` | `Amb_Exterior` |

`Amb_Vertical` no lo usa ninguna zona, y fuera de todo trigger suena el `defaultProfile` (`Amb_Hub_Open`).

### 5. Anclas (opcional pero muy recomendado)

`AmbienceEmitter` en props reales — caños, rejillas, ventilaciones, escaleras — con sus `acceptedTags`. El resolver las prefiere sobre el random validado (`anchorChance = 0.6`), y así una cadena suena desde donde hay una cadena.

Sin ninguna ancla el sistema funciona igual, 100% random validado — que es como corre hoy: la escena no tiene ninguna `AmbienceEmitter`. Antes de poner anclas con `acceptedTags` hay que taggear las entradas del banco (todas están en `None`; un ancla en `None` acepta cualquier evento, pero un ancla con tags no matchea ninguna entrada sin tags).

### 6. Persistente

`AmbienceComfortApplier` en el GameObject del `AudioManager`, en `Data.unity` (al lado de `AudioBackgroundApplier`).

> ⚠ **Hoy no está puesto** — ni en `Data.unity` ni en ninguna otra escena o prefab. Sin él, `AmbienceComfort.LowFrequencyEnabled` arranca en `DefaultEnabled = true` y nunca lee `Settings_LowFreqAmbience`; el único modo de apagar los sub es el context menu del drift layer (ver Pendiente).

---

## Import settings de los clips que consigas

| Tipo | Load Type | Compression | Force To Mono | Load In Background |
|---|---|---|---|---|
| Bed loop (30–60 s, 2D) | Compressed In Memory | **Vorbis** q~70 | **No** | **No** |
| One-shot (0.3–8 s, 3D) | Decompress On Load | **ADPCM** (PCM si < 1 s) | **Sí** | **No** |

- **Mono es obligatorio en las one-shots.** Una source 3D las colapsa igual, así que un clip estéreo solo duplica memoria y hace que un sonido a 25 m se sienta "ancho".
- **`Load In Background = No` en todo.** El bed tiene que estar sonando al arrancar el nivel; una carga en background es un primer segundo mudo.
- **Vorbis sí para el bed** (7.9 MB en PCM contra ~600 KB), pero **nunca** para un loop *generado* seamless — el códec trata al ruido como su peor caso y su padding rompe la costura sample-exacta. Por eso los clips del baker son PCM.

---

## Números del scheduler

Con los defaults (`gapRange` 8–30 s, `longSilenceChance` 0.22, `longSilenceExtra` 40–100 s) y pesos 60/30/10:

| Métrica | Valor |
|---|---|
| Gap medio entre eventos | 34.4 s |
| COMMON / UNCOMMON / RARE | cada 57 s / 115 s / 344 s |
| Eventos en 20 min | ~35 total, 3–4 raros |
| Gaps largos | 22% cae entre 48 y 130 s (~21% pasa de 60 s) |

Son números con `eventIntervalScale = 1`. El profile activo multiplica la espera por ese factor (el `defaultProfile`, `Amb_Hub_Open`, va a ×1.43 → ~49 s de gap medio), igual que `SetIntervalScale`.

`logDerivedStatistics` (apagado en el prefab; prendelo en la instancia para verlo) loguea esto al inicializar, con los pesos del profile activo — pero **no** aplica su `eventIntervalScale`, así que el gap medio del log es siempre el de factor 1 (34.4 s con estos defaults).

**El silencio es parte del sistema.** Resistí la tentación de acortar los gaps porque un playtest se sintió vacío — vacío es el punto.

> El 30% de "no reproducir nada y re-tirar el timer" del spec original está implementado como `skipChance`, en 0 por default. La razón: con esa fórmula solo el **7.6%** de los gaps pasa de 60 s, porque un ciclo solo nunca supera los 35 s. La rama de silencio largo da la misma media con una cola controlable. Para comparar: `skipChance = 0.3` y `longSilenceChance = 0`.

---

## Cómo verificar

1. **Loop no detectable** — quieto 4 min en `HUB_01`. Con dos beds coprimos el período compuesto es de ~26 a ~33 min según el par.
2. **Crossfade** — cruzar `PASILLO_CARGA → HUB_01 → ESCALERA_01` caminando y corriendo. Sin clicks. Entrar y salir rápido del mismo trigger para probar el guard de triggers duplicados.
3. **Anidamiento** — zona chica dentro de una grande, entrando y saliendo en orden y fuera de orden.
4. **Pink noise** — subí `Ambience/Texture` a 0 dB para escuchar el drift, volvelo a −3 dB, y hacé el test de aceptación: **mutealo y fijate si la costura del loop del bed se vuelve más obvia.** Si mutearlo no cambia nada, está demasiado bajo para justificar una voz.
5. **Sub** — **no verificar de oído.** El VU del bus `Ambience/Sub` en la ventana AudioMixer es la verdad de campo: muestra nivel independientemente de lo que reproduzcan tus parlantes. Probá en auriculares y en parlantes de laptop; el 32 Hz debería sentirse apenas en auriculares decentes y nada en la laptop, y eso es correcto. Confirmá que no aparece suciedad en los medios (intermodulación) en los parlantes malos.
6. **Eventos** — `debugRapidFire` en el scheduler baja la espera a 1–2 s: convierte una audición de 10 minutos en 30 segundos (para uno solo, context menu `Fire One Event Now` del scheduler, en Play). Con los gizmos del resolver (necesitan `drawGizmos` y la familia `ambience` del `GizmoManager` de Zona1 encendidas), confirmá que ningún evento cae afuera del edificio ni dentro de una pared, que ~75% caen detrás de la cámara, y que los ocluidos suenan amortiguados *y* más bajos. **Apagalo antes de juzgar el pacing y antes de buildear** (avisa con un warning al inicializar). `debugLogEvents` y `logProfileChanges` loguean cada evento y cada push/pop de zona. En el prefab están apagados (22/09, para que el build no loguee): prendelos en la instancia de la escena mientras auditás y no guardes ese override.
7. **Pausa** — abrir el menú de pausa: todo el mundo, ambiente incluido, baja en ~0.15 s (`AudioBackgroundApplier.pauseFadeDuration`) y queda congelado por `AudioListener.pause` (las fuentes del ambiente usan `ignoreListenerPause = false`); al despausar vuelve al instante desde donde estaba. Ningún one-shot nuevo mientras tanto (el scheduler corta con `PauseManager.IsPaused`), y un crossfade en curso termina igual porque los envelopes corren en tiempo unscaled. Abrir el inventario **no** frena el ambiente. Alt-Tab con `Settings_AudioInBackground` off (el default) → silencio total.
8. **Slider de Música** — moverlo de 0 a 1: todas las capas escalan juntas manteniendo el ratio. Es el chequeo de que ningún ratio quedó en un parámetro expuesto. El slider de SFX no tiene que mover el ambiente.
9. **Persecución** — cuando el Nemesis arranca a perseguir, `NemesisChaseMusic` llama `FadeOutAll(2)`: bed y drift bajan en 2 s y las one-shots que estén sonando se cortan. El scheduler **no** se frena, así que pueden entrar one-shots nuevas durante la persecución. Vuelve con `FadeInAll` recién cuando termina la búsqueda que sigue a la persecución (no cuando el Nemesis pierde de vista al jugador); tras un resultado de partida no vuelve.
10. **Escondite** — entrar a un locker o container: el ambiente se opaca (lowpass por snapshot, 0.3 s) sin tocar los faders; al salir vuelve a `Snapshot`.

---

## Pendiente

- **Toggle de confort sin UI.** El modelo ya está: `SettingsModel` tiene `LowFreqAmbience` / `SetLowFreqAmbience()`, con default `true`, y lo persiste en `Settings_LowFreqAmbience` en `Apply()` (también entra en `Revert()` y `ResetToDefaults()`). Falta el Toggle: ninguna vista de Opciones llama a `SetLowFreqAmbience` (los únicos toggles son CRT/Dither, VSync e Invert Y). `Settings_VHSGlitch` está peor: ni siquiera está en `SettingsModel`, solo lo leen `GlitchController` y `UISignalStaticBurst`. Y aunque hubiera Toggle, falta colocar `AmbienceComfortApplier` (sección 6) para que la preferencia llegue a `AmbienceComfort`. Mientras tanto: context menu `Toggle Low-Freq Ambience` en `AmbienceDriftLayer` (solo editor, rampa de `comfortRampSeconds` = 3 s).
- **Capa reactiva del enemigo.** No cableada, pero no bloqueada: `AmbienceController.SetTensionScalars(bedScale, eventRateScale, subScale)` sigue sin llamadores, y `NemesisEvents.OnProximityChanged` / `OnStateChanged` ya son públicos. Un `AmbienceTensionDriver` sería **un solo archivo nuevo**, sin ediciones en ningún otro lado. (`FadeOutAll` / `FadeInAll` ya tienen llamador: `NemesisChaseMusic`, ver Cómo verificar 9.)
- **Contenido.** El bed real: Corridor, Machine, Vertical y parte de Hub y Office siguen sobre los `PLACEHOLDER_Bed_*`, que sirven para tunear el sistema, no para shipear. Las one-shots ya están (sección 2).
