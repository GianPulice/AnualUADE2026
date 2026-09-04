# Sistema de ambiente sonoro

Ambiente de fábrica abandonada en 4 capas constantes más una capa de eventos 3D aleatorios. Sin música durante la exploración normal.

```
AMBIENTE CONSTANTE      Factory bed (×2 loops de largo coprimo)
INCOMODIDAD SUBCONSC.   Pink noise + 17 Hz + drone 32 Hz
EVENTOS DEL EDIFICIO    One-shots 3D aleatorios con silencios largos
```

Código en `Assets/_Project/Scripts/Ambience/`, ScriptableObjects en `Assets/_Project/Scripts/ScriptableScripts/Ambience/`, herramienta de editor en `Assets/_Project/Scripts/Editor/AmbienceToneBaker.cs`.

---

## Setup, en orden

### 1. Mixer

```
Tools > Audio > Create or Update Master Mixer
```

Crea 4 sub-grupos bajo `Ambience`: **Bed**, **Events**, **Texture**, **Sub**. Después, a mano en `Assets/_Project/ScriptableObjects/Audio/MasterMixer.mixer`:

| Grupo | Fader | Efectos |
|---|---|---|
| `Ambience/Bed` | 0 dB | — |
| `Ambience/Events` | −2 dB | — |
| `Ambience/Texture` | −3 dB | — |
| `Ambience/Sub` | **−12 dB** | **Highpass** 12 Hz + **Lowpass** 120 Hz |

> ⚠ **Nunca poner un limiter, compresor o Duck Volume en `Master`.** El drone de 17 Hz es inaudible pero sigue siendo un pico *grande*: cualquier dinámica en Master va a duckear toda la mezcla al ritmo de su LFO. El juego entero va a respirar cada 20–60 segundos y la causa es casi imposible de encontrar de oído. Si alguna vez hace falta un limiter, reestructurar a `Master > { Gameplay, Sub }` y ponerlo en `Gameplay`.

> ⚠ **Nunca usar `mixer.SetFloat` para nada de ambiente.** `AudioManager.SetGameplaySfxBundle()` reescribe `AmbienceVolume` cada vez que el jugador toca el slider de SFX. El balance entre capas vive en los faders fijos de los sub-grupos (el volumen de un hijo es un *offset* que suma en dB con el padre, así que sobrevive) y en `AudioSource.volume`.

### 2. Clips generados

```
Tools > Audio > Bake Ambience Texture Clips
```

Genera en `Assets/_Project/Audio/Ambience/Generated/`:

| Archivo | Uso |
|---|---|
| `PinkNoise_20s`, `BrownNoise_20s` | Capa 3. Probá los dos; el brown se sienta mejor bajo una mezcla PSX |
| `Sub_17Hz_60s`, `Sub_32Hz_60s` | Capa 4. **Finales**, no son placeholders |
| `PLACEHOLDER_Bed_A_37s`, `_Bed_B_53s` | Capa 1, par coprimo. Reemplazar |
| `PLACEHOLDER_OneShot_*` (5) | Capa 2, uno o dos por tier. Reemplazar |

La herramienta configura sus propios import settings. Es determinista (`Seed = 1337`), así que re-bakear no genera churn en git.

**Las capas 3 y 4 no necesitan ningún audio conseguido.** Lo único que hay que salir a buscar es el bed real y las ~22 one-shots.

### 3. Escena de gameplay

En `WIRED_Zona1_Blockout.unity`, GameObject vacío llamado `AmbienceController` con el componente del mismo nombre — los otros cinco llegan solos por `[RequireComponent]`. Va en la escena de gameplay, **no** en `Data`: el ambiente es del nivel y muere con él, igual que `VisionRangeController`.

Después:
- Arrastrar los 4 sub-grupos del mixer al `AmbienceBusTable`.
- Asignar `defaultProfile`.
- En `AmbienceDriftLayer`, cargar los 3 tracks (pink + los dos sub) con `comfortGated` **activado en los dos sub**.
- En `AmbiencePlacementResolver`, setear `occluderMask` y `solidMask`.

> ⚠ **`occluderMask` = `Wall`. `solidMask` = `Wall` + `Ground`. NO incluyas `Default`** — ahí viven `VISUAL_MASS` y los `PATIO_CARGA_Mass_*`, bloques decorativos macizos que rodean el área jugable, y rechazan todo candidato que salga del cuarto actual. Decidir si un punto está fuera del edificio es trabajo del test de NavMesh, no de este.

⚠ Si le ponés colliders a las anclas `AmbienceEmitter`, mandalos a `Ignore Raycast` o hacelos triggers.

### 4. Zonas

Un GameObject con BoxCollider por área y el componente `AmbienceZone` (el `Reset()` ya fuerza `Is Trigger`). Las zonas anidadas funcionan solas: gana la más interna.

El blockout tiene 17 áreas nombradas en 2 pisos. **6 profiles alcanzan** — un `SO_AmbienceProfile` se reutiliza en muchos colliders:

| Profile | Áreas | Carácter |
|---|---|---|
| `Amb_Hub_Open` | `ENTRADA`, `HUB_01` | espacio grande y vacío, cola larga |
| `Amb_Corridor` | `PASILLO_CARGA`, `PASILLO_OESTE`, `PASILLO_PLANTA`, `PASILLO_TECNICO` | resonancia cercana y estrecha |
| `Amb_Machine` | `BOMBAS`, `ANTESALA_BOMBEO`, `TABLEROS`, `MANTENIMIENTO` | zumbido eléctrico presente, caños |
| `Amb_Office` | `OFICINA`, `VESTUARIOS`, `SALA_HISTORIA` | seco y silencioso, fluorescentes |
| `Amb_Vertical` | `ESCALERA_01`, `MONTACARGAS_01`, `PASARELA` | metal, eco vertical |
| `Amb_Exterior` | `PATIO_CARGA` | viento, `subScale = 0` |

`PATIO_CARGA` es el único con solo objetos `_Mass_` (sin `_Floor_`/`_Ceil_`): es un patio exterior. Necesita su propio bed sin room tone, y sus eventos de aire quieren `requireNavMeshNearby = false`.

### 5. Anclas (opcional pero muy recomendado)

`AmbienceEmitter` en props reales — caños, rejillas, ventilaciones, escaleras — con sus `acceptedTags`. El resolver las prefiere sobre el random validado (`anchorChance = 0.6`), y así una cadena suena desde donde hay una cadena.

Sin ninguna ancla el sistema funciona igual, 100% random validado.

### 6. Persistente

`AmbienceComfortApplier` en el GameObject del `AudioManager`, en `Data.unity`.

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
| Gaps de más de 60 s | 22% |

`logDerivedStatistics` loguea esto en `Start` con los valores reales del profile activo.

**El silencio es parte del sistema.** Resistí la tentación de acortar los gaps porque un playtest se sintió vacío — vacío es el punto.

> El 30% de "no reproducir nada y re-tirar el timer" del spec original está implementado como `skipChance`, en 0 por default. La razón: con esa fórmula solo el **7.6%** de los gaps pasa de 60 s, porque un ciclo solo nunca supera los 35 s. La rama de silencio largo da la misma media con una cola controlable. Para comparar: `skipChance = 0.3` y `longSilenceChance = 0`.

---

## Cómo verificar

1. **Loop no detectable** — quieto 4 min en `HUB_01`. Con dos beds coprimos el período compuesto es ~33 min.
2. **Crossfade** — cruzar `PASILLO_CARGA → HUB_01 → ESCALERA_01` caminando y corriendo. Sin clicks. Entrar y salir rápido del mismo trigger para probar el guard de triggers duplicados.
3. **Anidamiento** — zona chica dentro de una grande, entrando y saliendo en orden y fuera de orden.
4. **Pink noise** — subí `Ambience/Texture` a 0 dB para escuchar el drift, volvelo a −3 dB, y hacé el test de aceptación: **mutealo y fijate si la costura del loop del bed se vuelve más obvia.** Si mutearlo no cambia nada, está demasiado bajo para justificar una voz.
5. **Sub** — **no verificar de oído.** El VU del bus `Ambience/Sub` en la ventana AudioMixer es la verdad de campo: muestra nivel independientemente de lo que reproduzcan tus parlantes. Probá en auriculares y en parlantes de laptop; el 32 Hz debería sentirse apenas en auriculares decentes y nada en la laptop, y eso es correcto. Confirmá que no aparece suciedad en los medios (intermodulación) en los parlantes malos.
6. **Eventos** — `debugRapidFire` en el scheduler baja la espera a 1–2 s: convierte una audición de 10 minutos en 30 segundos. Con los gizmos del resolver, confirmá que ningún evento cae afuera del edificio ni dentro de una pared, que ~75% caen detrás de la cámara, y que los ocluidos suenan amortiguados *y* más bajos. **Apagalo antes de juzgar el pacing y antes de buildear** (avisa con un warning en `Start`).
7. **Pausa** — pausar: el ambiente sigue, ningún one-shot nuevo, un crossfade en curso termina. Alt-Tab con `Settings_AudioInBackground` off → silencio total.
8. **Slider de SFX** — moverlo de 0 a 1: todas las capas escalan juntas manteniendo el ratio. Es el chequeo de que ningún ratio quedó en un parámetro expuesto.

---

## Pendiente

- **Toggle de confort sin UI.** `Settings_LowFreqAmbience` se persiste y se aplica, pero no hay Toggle en el panel de Opciones — mismo estado que `Settings_VHSGlitch`. Mientras tanto: context menu `Toggle Low-Freq Ambience` en `AmbienceDriftLayer`.
- **Capa reactiva del enemigo.** No cableada, pero no bloqueada: `AmbienceController.SetTensionScalars(bedScale, eventRateScale, subScale)` y `FadeOutAll(seconds)` ya existen sin llamadores, y `NemesisEvents.OnProximityChanged` / `OnStateChanged` ya son públicos. Un `AmbienceTensionDriver` sería **un solo archivo nuevo**, sin ediciones en ningún otro lado.
- **Contenido.** El bed real y las ~22 one-shots. Los placeholders del baker sirven para tunear el sistema, no para shipear.
