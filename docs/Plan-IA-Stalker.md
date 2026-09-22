# Plan — IA stalker y anti-cheese del Nemesis

> **Estado al 22/09/2026:** construidas las Fases 0, 1, 2, 4 y 5 (la 2 y la 5 falta jugarlas);
> pendientes la 3, 6, 7 y 8. La Fase 2 pasó por una revisión de cuatro modelos ([§16](#16-revisión-del-consejo-21092026))
> que encontró un bug bloqueante, ya corregido, y ajustó el modelo de los §3.3–§3.5. El Director de
> Zona1, que se había perdido en el merge `16b1962c` (20/09), se rehízo el 22/09 con seis zonas que
> cubren los 39 waypoints, y ahora ninguna palanca puede actuar a menos de 6 m del Hub (C5)
> ([§14.1](#141-el-director-hoy-estado-en-zona1)).
>
> Compara el análisis *"IA de enemigos stalker"* (Alien: Isolation, Mr. X, Nemesis, Dimitrescu,
> Requiem, GDC) contra lo que el Nemesis de WIRED ya tiene hoy, y propone qué construir, en qué
> orden y dónde. **Asume que el sistema de escondites se construye** (spec *Hiding System v1.0*), y
> por eso el eje del plan es el anti-cheese alrededor de esconderse.
>
> Arquitectura vigente: `docs/CLAUDE.md` (inglés). Tuning del Nemesis: `docs/Nemesis-System.md`.
> Todo el código nuevo va en inglés, como el resto de `Assets/_Project/Scripts/`.
>
> Relevado contra el código el 14/09/2026 (rama `iña`, commit `79c2652`).
> La guía de armado ([§14](#14-cómo-se-arma-en-unity)) se relevó el 15/09/2026 leyendo escena,
> prefabs y assets como YAML, sin conector MCP de Unity: lo marcado *verificar en el editor* no se
> pudo abrir.
>
> **Revisado el 19/09/2026 contra `6703f9d`** (48 commits después). El Nemesis casi no cambió y nada
> del plan se construyó. Se corrigieron tres datos que ya estaban mal (`patrolWaitVariance`,
> `stateLoops`, radios de ruido) y se incorporaron cambios de diseño posteriores: M2 es módulo fatal,
> la captura pausa el timer, se borró `NemesisTestSceneBuilder`, hay estados nuevos del jugador
> (levantarse, empujar la caja). Se agregó el [§15](#15-bajadas-entre-pisos) (bajadas entre pisos).

---

## Índice

0. [Resumen](#0-resumen)
1. [Los 12 principios contra el código](#1-los-12-principios-contra-el-código)
2. [Mapeo: lo que propone el análisis → lo que ya existe](#2-mapeo-lo-que-propone-el-análisis--lo-que-ya-existe)
3. [Escondites: qué falta y cómo tiene que "saber" el Nemesis](#3-escondites-qué-falta-y-cómo-tiene-que-saber-el-nemesis)
4. [Catálogo de cheeses de WIRED](#4-catálogo-de-cheeses-de-wired)
5. [Hábitos del jugador y contra-jugadas](#5-hábitos-del-jugador-y-contra-jugadas)
6. [Director: tensión y ritmo](#6-director-tensión-y-ritmo)
7. [Escalada por progreso](#7-escalada-por-progreso)
8. [Arquitectura resultante](#8-arquitectura-resultante)
9. [Fases de implementación](#9-fases-de-implementación)
10. [Reglas del proyecto que este plan no puede romper](#10-reglas-del-proyecto-que-este-plan-no-puede-romper)
11. [Decisiones abiertas](#11-decisiones-abiertas)
12. [Valores iniciales](#12-valores-iniciales)
13. [Casos de prueba](#13-casos-de-prueba)
14. [Cómo se arma en Unity](#14-cómo-se-arma-en-unity)
15. [Bajadas entre pisos](#15-bajadas-entre-pisos)
16. [Revisión del consejo (21/09/2026)](#16-revisión-del-consejo-21092026)

---

## 0. Resumen

**El Nemesis ya implementa la mayor parte de la arquitectura que propone el análisis**, con otros
nombres: percepción, creencia y decisión separadas; visión con banda periférica que acumula;
oído atenuado por paredes y pisos y medido sobre el NavMesh; persecución con predicción y desvíos;
búsqueda legible con barrido de habitación; un Director que nunca toca el FSM; la entrada tipo
Mr. X; y un set de herramientas de debug que el análisis pide construir "primero".

Lo que falta se concentra en cinco agujeros:

| # | Agujero | Gravedad |
|---|---|---|
| 1 | **Escondites.** El lado jugador no existe. El lado Nemesis es binario (escondido = ciego) y, con los lockers del proyecto, **inmunidad total** — ver [§3.3](#33-hallazgo-con-los-lockers-actuales-esconderse-es-inmunidad-total). | ✅ Construido (Fases 1 y 2); falta jugarlo |
| 2 | **Nadie cuenta los hábitos del jugador.** No hay ninguna contra-jugada. Es el anti-cheese entero. | Alta |
| 3 | **No se detecta la persecución estancada.** El jugador corriendo (4.5 m/s) es más rápido que el Nemesis persiguiendo (3.0 m/s): **un loop alrededor de una columna es un exploit hoy**, sin escondites. Sigue siéndolo con M1 (3.6 m/s), y M2 ya no lo acorta porque es el módulo fatal ([C4](#c4--el-loop-alrededor-de-un-obstáculo-el-bug-de-la-mesa-de-dimitrescu)). | ✅ Construido (Fase 4) |
| 4 | **El Director no mide tensión ni administra ritmo.** Sólo reacciona a pedidos (puzzles, API). No hay Relax ni retirada. | ✅ Construido (Fase 5, 22/09); falta jugarlo |
| 5 | **Escalada por progreso** (spec Nemesis §7.2) sin hacer. | Media, diferida por diseño |

Y hay cosas que el análisis recomienda y que **acá no conviene hacer**: un `NoiseBus`, Unity
Behavior, cuatro conos nuevos, ductos/backstage, santuarios de luz, roles de escuadra y LOD de IA.
El porqué, en [§2.3](#23-lo-que-no-conviene-copiar).

---

## 1. Los 12 principios contra el código

✅ hecho · 🟡 parcial · ❌ falta

| # | Principio del análisis | Estado | Dónde está hoy | Brecha |
|---|---|---|---|---|
| 1 | Separar percepción, conocimiento y decisión | ✅ | `FieldOfView` / `FieldOfListening` → `NemesisStateManager.TryGetBelief` / `BeliefAge` → `NemesisDecision` + `SO_NemesisPriorities`. Los estados sólo ejecutan. | Ninguna. Lo nuevo tiene que entrar igual: **un predicado y un peldaño, nunca un estado que decide**. |
| 2 | El agente no hace trampa; el director sí | ✅ | Persecución y búsqueda leen la creencia y la velocidad **observada** (`FieldOfView.LastKnownVelocity`). Sólo dos lecturas del jugador real, ambas deliberadas: `ZoneBiasUsesRealPlayer` (elige zona, no waypoint) y `CanReachPlayerNow` (el agarre). | Las contra-jugadas nuevas tienen que pasar el mismo filtro (ver regla R4, §5). |
| 3 | La detección es un acumulador | 🟡 | Banda periférica de 170° que llena `Awareness`; foco de 80° instantáneo a propósito (es un peldaño *interrupt*); agachado ×0.5 de alcance. | **Escondido es un `return` temprano**: 0 o todo. Sin término de luz. |
| 4 | Administrar la tensión, no maximizarla | 🟡 | `NemesisTension` (medidor + BuildUp/SustainPeak/PeakFade/Relax) y `NemesisDirector` (retirada en Relax, sensibilidad creciente tras 90 s de silencio), con `SO_DirectorPacing`. | Construido el 22/09, sin jugar: los números del §12 son puntos de partida. |
| 5 | Nada guionado para el "cuándo" y el "dónde" | 🟡 | Patrulla por ruleta, cúmulos, satélites; spawn y entrada muestreados. | Los disparadores del Director son siempre "al completar el puzzle". Aceptable: el *qué* puede ser fijo. |
| 6 | Incertidumbre estructurada | 🟡 | 15 % de invertir la ronda, 15 % de saltear un waypoint. | `patrolWaitVariance` está en 0.25 en el asset (el 0 es el default del código): la espera en cada waypoint varía 1.25–1.75 s, poco para que no se note el ritmo. Se arregla con un número. |
| 7 | Anticipación dramática | 🟡 | Pasos reales, ocluidos por pared; puertas que suenan al abrirlas; música de persecución. | `NemesisAudio.stateLoops` está cargado **sólo en la instancia de Zona1** (respiración de patrulla, búsqueda y persecución para Patrolling / Investigating / Chasing / Searching); faltan `Catch` y `Traversing`, y el prefab y la testbed no lo tienen. Los clips de voz (`sfx_nemesis_voice_*`) no se usan. Sin cue de activación (ahora existe `NemesisEvents.OnActivated` para engancharlo). La música de persecución delata el estado interno — ver D5. |
| 8 | Legibilidad por encima de inteligencia | ✅ | `SearchPauseTime` + `NemesisLookAround`; el HUD F9 muestra el peldaño ganador. | Las contra-jugadas nuevas tienen que **verse** (regla R3). |
| 9 | Anti-cheese con comportamiento | ❌ | Nada cuenta hábitos. | Todo [§4](#4-catálogo-de-cheeses-de-wired) y [§5](#5-hábitos-del-jugador-y-contra-jugadas). |
| 10 | Detectar el estancamiento | 🟡 | `NemesisStuckEscape` (cuerpo trabado: repath → warp). `NemesisPursuit` predice e intercepta. | Nadie mide "persigo pero no acorto". Ver C4. |
| 11 | El NavMesh expresa personalidad | 🟡 | Hub `Not Walkable`, puertas con carve, montacargas con links. | Área 3 `NemesisAvoid` sin uso. Sin rutas de flanqueo. |
| 12 | Herramientas de debug primero | ✅ | F9 HUD, F10 consola, `NemesisGizmos`, validadores, `SO_NemesisDataEditor`. | Falta un panel de hábitos y otro de tensión. |

---

## 2. Mapeo: lo que propone el análisis → lo que ya existe

### 2.1 Ya existe — se reusa, no se reescribe

| El análisis propone | En WIRED es | Diferencias que importan |
|---|---|---|
| `NoiseEmitter` + `HearingSensor` | `PlayerStateManager.AudioEmitingZone` (esfera en la capa `DetectableAudio`, radios agachado 1 / caminando 4 / corriendo 10 en `SO_PlayerMovement`, apagada en quieto; 1 / 2 / 6 son los defaults del código, no lo que corre) + `FieldOfListening` | El ruido **dura**, no es un evento: tiene que vivir más de 0.1 s o cae entre dos barridos. Alcance = radio × `NoiseRangeScale` (2.5), tope `ListenRange` (15): agachado 2.5 m, caminando 10 m, corriendo 15 m (tope). ×0.8 por pared, ×0.75 por piso, distancia medida sobre el NavMesh. |
| `StalkerBlackboard` | La creencia de `NemesisStateManager`: `TryGetBelief(out pos, out fromSight)`, `BeliefAge`; más `FieldOfView.Awareness` y `LastKnownVelocity` | Usa el sensor **más fresco** y dice si la creencia viene de la vista o del oído. El análisis no tiene esa distinción y acá es central (el barrido de habitación sólo se compromete con una creencia de vista). |
| `VisionSensor`, 4 conos | `FieldOfView`: foco 80° (Normal/Focused), periferia 170° con acumulador (Peripheral), `minDistance` 1 m (Close), proximidad extrema 1.5 m (rompe `Hidden`) | Equivalente funcional. Además muestrea pies, centro y cabeza. |
| `PlayerVisibilityState` | `PlayerStateManager.IsCrouch` / `IsHidden` | `IsHidden` es un bool suelto. Hace falta saber **en qué** escondite (§3.1). |
| `StalkerAgent` (FSM) | `NemesisStateManager` + 6 estados + `NemesisDecision` (escalera en `SO_NemesisPriorities`) | Una sola voz decide. Los estados no transicionan. |
| Stalk en dona | `NemesisSearchingState` + `NemesisFreeRoam` (barrido de habitación, radio 8) + `PickSearchTarget` (ruleta por última posición, predicción, no barrido) | El radio no se achica por pasada: el cerco no "se cierra". Mejora menor, opcional. |
| Intercept | `NemesisPursuit.PredictAhead` + desvíos por waypoints con línea de visión; `TryGetInterceptPoint` al entrar a Searching | Nada lo dispara por estancamiento. |
| `StalkerDirector` (hint zones) | `NemesisDirector`: ancla de patrulla, pesos de ruta, ruido sintético, boost de sentidos, entrada Mr. X; más `NemesisPressureZone` | Sin medidor de tensión y sin estados de ritmo. |
| `LightSanctuary` | El Hub: `NavMeshModifierVolume` en `Not Walkable` | Regla dura del proyecto: **el Hub no se toca**. |
| Herramientas de debug (§12.3) | F9, F10, gizmos, validadores | Casi todo. Faltan tensión y hábitos. |

### 2.2 Hay que crear

- El sistema de escondites, lado jugador (spec *Hiding* v1.0; forma ya decidida en `docs/CLAUDE.md` › *Hiding spots*).
- Que el Nemesis sepa de escondites (`NemesisHidingAwareness`).
- El contador de hábitos y sus reglas (`PlayerHabitTracker`, `SO_CounterplayRules`).
- El detector de persecución estancada (`NemesisChaseProgress`).
- El medidor de tensión y el ritmo (`NemesisTension`, junto al Director).
- La escalada por puzzles (spec §7.2).
- Las bajadas entre pisos (`NemesisDropLink`, [§15](#15-bajadas-entre-pisos)) y sus animaciones.

### 2.3 Lo que no conviene copiar

- **`NoiseBus` / `NoiseEvent`.** Sería un segundo modelo de oído al lado de la esfera, y
  `docs/CLAUDE.md` lo dice sin vueltas: un evento de ruido tiene que *reemplazar* la esfera, no
  convivir con ella. Si hace falta un ruido de mundo (la puerta de un escondite, una botella), se
  hace como ya lo hace el Director: una esfera en `DetectableAudio` que vive más de 0.1 s. Propuesta:
  sacar ese código privado de `NemesisDirector.TryEmitNoise` a un helper `NoisePulse` y reusarlo.
- **Unity Behavior (`com.unity.behavior`).** Ya se probó y se sacó: el grafo era una segunda voz
  escribiendo `NextState` y dejó al Nemesis "mirándote y temblando". Los comportamientos
  desbloqueables se expresan como predicados y peldaños, igual que el resto.
- **Cuatro conos nuevos.** Ya están (tabla de arriba).
- **Ductos / backstage** (segunda superficie de NavMesh, renderer apagado). El nivel no tiene ductos
  diseñados, el montacargas ya cumple el rol de "otra superficie", y un monstruo invisible que se
  mueve en línea recta choca con la regla del Director: todo lo que hace el Nemesis tiene que tener
  explicación en pantalla. Si algún día se diseñan ductos, es otro proyecto. Lo que acá hace de
  "irse a los ductos" es la **retirada** del [§6](#6-director-tensión-y-ritmo). Las bajadas
  entre pisos del [§15](#15-bajadas-entre-pisos) **no** son ductos: el Nemesis nunca deja de estar
  en el NavMesh, se ve bajar y se oye caer.
- **Modificadores por luz y por linterna.** La luz no es un input de detección (la niebla de visión
  afecta lo que ve el jugador, no lo que ve el Nemesis) y el jugador no puede apagar su luz. Esto se
  suma cuando se construya el spec de Luz §4, no antes.
- **`SquadDirector` y roles Attacker/Flanker/ZoneDefense.** Hay un solo Nemesis. De RE4R se toman
  dos ideas sueltas: el truco del rastro para flanquear (C4) y la emboscada en salidas probables.
- **`AILodController`.** Un agente. No hace falta.
- **Detalles del código del análisis que acá serían bugs:** `Vector3.Distance` para proximidad (acá
  se mide sobre el NavMesh, con `NemesisNav`, porque hay pisos); `LayerMask.GetMask("Wall")` como
  único oclusor (acá hay cuatro máscaras que tienen que coincidir); ids de desbloqueo como `string`
  (acá un typo falla en silencio, así que van enums que sólo se agregan al final); y
  `SetDestination(player.position)`.

---

## 3. Escondites: qué falta y cómo tiene que "saber" el Nemesis

### 3.1 Lado jugador — prerrequisito, ya especificado

La forma está decidida en `docs/CLAUDE.md` › *Hiding spots*. Resumen:

- `HidingSpot : BaseRangeInteractable` con un enum de tipo (`Locker`, `UnderTable`, `Container` —
  **sólo se agrega al final**), la pose interior de cámara y un `SO_HidingData` (intervalo de
  respiración 3 s, radios de respiración y exhalación, `containerNoiseMultiplier` 0.5,
  `closetBreathingMultiplier` 1.2).
- `PlayerHiddenState` deja de ser un stub: velocidad a cero, no escribe `linearVelocity`, cámara
  viva. Cámara interior Cinemachine por escondite con los límites del spec (locker ±15/±10, mesa
  ±45/−5..+15, container ±10/±10).
- `Tab` no abre el inventario escondido.
- La respiración pasa por el emisor que ya existe: pulsos de `AudioEmitingZone` cada 3 s; `F`
  aguanta; soltar `F` exhala con un pulso más grande. Todo pulso dura más de 0.1 s y el emisor
  vuelve a como lo dejaron los estados de movimiento.
  **El lado audio ya existe:** `HiddenBreathing` (en `Player.prefab`) toca el loop de respiración
  mientras `IsHidden`, con variante para la penalidad de M2, y a propósito no hace ruido para el
  Nemesis. La Fase 1 lo reusa tal cual y sólo agrega los pulsos de detección y la `F`.
- Todo modificador por escondite se deshace en **todas** las salidas: salir normal, captura,
  checkpoint, descarga de escena.
- `HidingSpot.CanInteract()` da `false` con `PlayerStateManager.IsImmobilized` (capturado,
  cinemática de despertar, levantándose) y en `PlayerBoxInteractingState` (empujando la caja).
  Estos estados son posteriores al spec.
- Se borra la tecla `R` (`PlayerStateManager.cs:521`).

**Lo que el anti-cheese necesita y el spec no pide:**

| Agregado | Para qué |
|---|---|
| `PlayerStateManager.CurrentHidingSpot` (referencia); `IsHidden` pasa a ser `CurrentHidingSpot != null` | El Nemesis tiene que saber **en qué** escondite estás para "te vi entrar" y para revisarlo. |
| `HidingEvents` estático: `OnEntered(spot)`, `OnExited(spot)`, `OnSpotBurned(spot)`, con el mismo `ResetStatics` que `NemesisEvents` | Quien cuenta y quien reacciona no llaman al escondite ni al revés. |
| En cada `HidingSpot`: `SpotId` estable, un `ApproachPoint` sobre el NavMesh y la lista de colliders propios | Dónde se para el Nemesis para revisarlo; qué colliders ignorar en la proximidad (§3.3). |
| Entrar y salir llevan tiempo (0.5–0.8 s de animación) durante el cual el jugador es visible con normalidad | Es la ventana de "te vi entrar". Sin ella, entrar es instantáneo y la regla no tiene de dónde agarrarse. |

La consola F10 conserva su toggle *Hide* como "escondido sin escondite", para seguir probando la
visión sin armar un nivel.

### 3.2 Lado Nemesis, antes de la Fase 2

> Así estaba hasta el 21/09/2026. Lo que se construyó, con las correcciones del consejo, está en los
> §3.3 a §3.5 y en el [§16](#16-revisión-del-consejo-21092026).

- `FieldOfView.cs:383` — con `IsHidden`, `hasVisualTarget = false` y limpia la periferia, a
  propósito: para que no deduzca el locker por haber estado mirando justo cuando entraste.
- `FieldOfView.cs:233` — la proximidad extrema (1.5 m) se chequea antes de ese `return`: es lo único
  que rompe `Hidden`.
- El oído no cambia: respirar hace ruido por el emisor y lleva a `Investigating`, no a `Chasing`.

Lo que pasa hoy si te ve entrar: pierde la vista en el acto, la creencia queda en la puerta del
escondite (de vista, fresca, cerca) y eso dispara el barrido de habitación (radio 8 m, compromiso de
6 s). A los 15 s (`SearchTimeOut`) patrulla la zona (`RequestNearbyPatrol`). **Nunca revisa el
escondite en sí.**

### 3.3 Hallazgo: con los lockers actuales, esconderse es inmunidad total

Verificado en los assets:

- `Prefabs/Environment/Locker.prefab` y `Locker2.prefab`: `BoxCollider` sólido (`m_IsTrigger: 0`)
  en la capa `Default`.
- `obstacleMask` de `FieldOfView` y de `FieldOfListening` en `Nemesis.prefab` = `6153` =
  `Default | Ground | Wall | Props`.
- `SO_NemesisData.proximityDetectionRespectsWalls = 1`, así que `CheckExtremeProximity`
  (`FieldOfView.cs:321`) tira un raycast del ojo al centro del jugador, y el collider del propio
  locker lo corta. `CanReachPlayerNow` (`NemesisStateManager.cs:594`, la captura) hace lo mismo con
  `IsOccludedByWall`.

**Consecuencia:** si el `HidingSpot` deja al jugador adentro o detrás del collider del mueble, ni la
proximidad ni la captura pueden alcanzarlo. La frase de la doc ("la proximidad extrema es lo único
que rompe `Hidden`") deja de ser cierta justo en el escondite más común. Es el error de la sección
12.4 del análisis: *un escondite que es inmunidad total rompe el juego*.

**Arreglo (Fase 2):** los dos tests ignoran los colliders del escondite **ocupado** (raycast que
descarta impactos con `CurrentHidingSpot.OwnsCollider(hit)` y sigue). No hay que mover el mueble de
capa: tiene que seguir tapando la visión normal, el oído y la cámara. Confirmarlo en la testbed
(F10) una vez que exista la posición interior real, porque depende de dónde quede el jugador.

La mesa no encierra al jugador, pero el rayo sale del ojo del Nemesis, que está alto, y baja hacia
un jugador agachado: el tablero (`Props`, dentro del `obstacleMask`) lo puede cortar según el ángulo.
El mismo arreglo cubre los dos casos, porque el tablero es un collider del escondite ocupado. El
spec pide además que la mesa **no ciegue** al Nemesis sino que le acorte la vista
(`underTableVisionMultiplier`).

✅ **Construido (21/09):** `HidingSpot.IsLineBlockedIgnoringSelf` (un `RaycastNonAlloc` que saltea los
colliders propios del escondite); lo usan la proximidad, el agarre (vía
`FieldOfListening.IsOccludedByWall(origen, destino, escondite)`) y la vista por las rendijas.

**Segundo hallazgo, al construirlo: la proximidad extrema no disparaba nunca en el mismo piso.** Era
una esfera de 1.5 m alrededor del **ojo** (el hueso de la cabeza, a ~1.8 m) medida hasta los **pies**
del jugador: la diferencia de altura sola ya supera el radio. La regla "lo único que rompe `Hidden`"
era código muerto también afuera de los escondites. Ahora es un **disco plano desde los pies del
Nemesis**, sólo en su mismo piso (`|Δy| ≤ CatchMaxVerticalOffset`), y el rayo de pared apunta al
cuerpo (+1 m), no al piso. Es lo que ya dibujaban el gizmo y el editor del SO. Cambia la dificultad
de todo el juego, no sólo de los escondites: antes el alcance real era ~1 m de pie y ~0.6 m agachado
(por `minDistance`); ahora es 1.5 m en cualquier postura. Vigilar pasillos de 2 m en Zona1.

**Tercer hallazgo, del consejo ([§16](#16-revisión-del-consejo-21092026)): con sólo ignorar la
carcasa, el Nemesis se quedaba clavado en la puerta.** La proximidad convertía al escondido en
"lo está viendo" → `Chasing` corría a la pose interior, que está **fuera del NavMesh** (adentro del
mueble); el agente frenaba 0.6 m antes del borde de la malla, a ~1.3 m del jugador, fuera del agarre
de 1 m, mirando la puerta para siempre con la música de persecución. Ni el estancamiento ni el
watchdog lo destrababan (para los dos, "llegó"). Arreglo:

- **Detectado estando escondido = escondite conocido, no avistamiento.** La proximidad sobre alguien
  en un escondite no prende `HasVisualTarget`: marca el escondite (`HiddenPlayerSpotted`) y el
  Nemesis va a la puerta a abrir. `Chasing` nunca persigue a alguien escondido. El toggle *Hide* de
  F10 (escondido sin escondite) sigue siendo un avistamiento: no hay puerta a la que ir.
- **El agarre de un escondido se mide contra el `ApproachPoint`**, y sólo si conoce ese escondite:
  "parado en la puerta = puede abrir". Pasar por la puerta de un locker ocupado que no conoce no saca
  a nadie (sin detección previa).
- Al revisar un escondite el agente frena a 0.25 m del `ApproachPoint`
  (`NemesisStateManager.SpotCheckStoppingDistance`), no al metro de la patrulla.

### 3.4 Modelo propuesto: tres niveles de conocimiento

**Nivel A — "Te vi entrar".** Inmediato, sin contador. Es la regla de Alien: si te ve entrar, te saca.

- En `HidingEvents.OnEntered(spot)`: si el Nemesis tiene al jugador en el foco, o
  `FieldOfView.TimeSinceLastSighting < seenEnteringWindow` (0.75 s), y el escondite está a menos de
  `ViewRange` de su ojo **y ve la puerta** (línea de vista al `ApproachPoint` a la altura del cuerpo,
  mirando a través de la carcasa del escondite) → `KnownSpot = spot`.
  - **0.75 y no 0.6.** Se cuenta desde el FINAL de la subida (`enterDuration` 0.6), y la vista barre
    cada 0.1 s: con 0.6 justo, alguien visto sólo en el primer barrido de la subida quedaría afuera.
    El consejo lo discutió (§16): lo que faltaba no era un número más chico sino la línea de vista
    a la puerta, que es lo que convierte "te vi hace un momento a la vuelta" en "te vi entrar".
- Si sólo había sospecha periférica (`Awareness` ≥ umbral, < 1) **con contacto vivo en el último
  barrido** → el escondite queda **sospechoso**: `Investigating` va a mirarlo, pero no es seguro. Esa
  es la zona gris. El medidor que todavía está **bajando** de una persecución que ya te perdió no
  cuenta: es memoria, no un vistazo, y castigaba al que cortó la línea de vista y se escondió bien
  (caso 2 del §13; lo encontró el consejo).
- No hace falta tocar el `return` de `FieldOfView`: su razón sigue valiendo. `TimeSinceLastSighting`
  no se borra al esconderse, así que se puede leer en el momento de entrar.

**Nivel B — Visibilidad residual por tipo.** Reemplaza el "0 o todo" por lo que el spec llama
riesgo (locker medio, mesa alto, container bajo):

| Tipo | Hoy | Propuesto |
|---|---|---|
| `Container` | ciego | ciego (el spec lo pide así) |
| `Locker` | ciego | sólo acumula por las rendijas y **sólo desde el lado de la puerta**: alcance × `lockerVisionExposure` (**0.35** → 2.45 m; ver §16.4), **siempre por el acumulador, nunca instantáneo** |
| `UnderTable` | ciego | alcance × `underTableVisionMultiplier` (0.5 → 3.5 m, spec) desde cualquier lado, también por el acumulador |

Si el medidor pasa el umbral con el jugador escondido, el escondite queda **sospechoso** y
`Investigating` camina a mirarlo (el mismo "fue a mirar" de la periferia; antes se iba al último
ruido, que podía estar en cualquier lado). Si llega a 1, no arranca una persecución: marca
`KnownSpot`. Los números van a `SO_HidingData` por tipo; `underTableVisionMultiplier` va al final de
`SO_NemesisData`, al editor y a los gizmos, como pide `CLAUDE.md`.

**Por qué el locker pasó de 0.25 a 0.5.** El alcance se mide desde el ojo (~1.8 m de alto) hasta el
cuerpo: con 7 × 0.25 = 1.75 m, casi toda la banda caía adentro del disco de proximidad de 1.5 m, y
el Nivel B del locker no existía. Locker y container eran lo mismo, que es justo lo que D7 dice que
no tiene que pasar. La mesa se queda en 0.5: el consejo votó 2 a 1 no subirla, y el caso 4 del §13
se corrigió a 3 m.

**Proximidad y Nivel B son la misma respuesta.** Estar pegado a un escondite ocupado (≤1.5 m plano,
sin pared en el medio salvo la carcasa) también lo marca conocido — ver el tercer hallazgo del §3.3.

**Nivel C — Revisar escondites.** Dentro de `Searching`, los escondites que caen dentro del barrido
de habitación entran como candidatos: ir al `ApproachPoint`, pararse, abrir o agacharse a mirar (la
misma pausa de `SearchPauseTime`, con animación), y seguir. La probabilidad sale de: si está
desbloqueado (§5), cuán sospechoso es el escondite y si oyó respiración cerca. Un escondite
revisado vacío se marca como barrido para esa búsqueda, igual que un punto.

### 3.5 Cómo se expresa en el FSM — sin estado nuevo

Se evaluó un estado `CheckingSpot`. **Se descarta**: agregar un valor a `ENemesisState` cuesta una
entrada en `NemesisAudio.stateLoops` (o se queda mudo), una rama en `IsNavigatingState`, otra en
`MovementOf` y peldaños que lo pidan. Todo lo que haría ya lo hace `Searching`: ir a un punto,
pararse, mirar.

| Pieza | Cambio |
|---|---|
| `NemesisSearchingState` (`TickSpotCheck`) | ✅ Prioridad: escondite **conocido** → **sospechoso** → barrido de habitación (con prioridad de la habitación donde te vio entrar) → grafo. Va al `ApproachPoint`, se queda `SearchPauseTime` y, si en ese rato no pasó nada, lo marca revisado (`MarkChecked`) y sigue. **No le pregunta al escondite si está ocupado**: si estás adentro, la proximidad te encontró apenas llegó a la puerta. |
| `NemesisInvestigatingState` | ✅ Un escondite **sospechoso** le gana al ruido: camina a su puerta y mira `investigationDwellTime` (4 s). Si no pasó nada, lo marca revisado. |
| `NemesisCatchState` | ✅ Fase nueva al principio, **sólo si el jugador está escondido** (`PullingOut`, `hiddenPullOutTime` 0.8 s): quieto, mirando al escondite, con la animación de agarre; después `OnCaptured()` como siempre, y el jugador **aparece en la `ExitPose`**, afuera del mueble (dejarlo adentro lo expulsaba PhysX). Si en ese rato salió y quedó fuera de alcance, suelta y la escalera retoma la persecución. `ECatchPhase` es privado, no se serializa: se pudo insertar sin riesgo. |
| `CanReachPlayerNow` y `CheckExtremeProximity` | ✅ Ignoran la carcasa del escondite ocupado (§3.3). La proximidad sobre un escondido lo marca **conocido**; el agarre de un escondido se mide contra el `ApproachPoint` de un escondite **conocido**. |
| `NemesisHidingAwareness` (nuevo, hermano de `NemesisStateManager`, se agrega solo) | ✅ `KnownSpot` / `SuspectedSpot`. Se olvida: revisado vacío (pero no mientras lo sigue viendo por las rendijas), visto afuera, captura, respawn, escondite quemado, o sin confirmar por `SearchTimeOut` / `InvestigationTimeOut` (cada confirmación reinicia el reloj). |
| `ENemesisPredicate` | ✅ Se agregaron **al final**: `KnowsHidingSpot` (15), `IsCheckingSpot` (16), `SuspectsHidingSpot` (17). |
| Escalera | ✅ Tres peldaños, **en el asset y en `BuildDefaultLadder()`** (verificados idénticos, 19 peldaños): <br>• `"sabe en qué escondite está"` → `Searching`, debajo de `"lo está viendo"` y **arriba de** `"lo perdió de vista recién"` (si no, la gracia de 2.5 s de `Chasing` le gana). <br>• `"está revisando un escondite"` → `Searching` (`InState(Searching)` + `IsCheckingSpot`), **arriba de** `"le queda presupuesto de búsqueda"`, para que el presupuesto de 15 s no lo arranque con la mano en la puerta. <br>• `"sospecha de un escondite"` → `Investigating`, **arriba de** `"vio algo de reojo"`: la sospecha periférica decae en menos de un segundo y sin este peldaño se soltaba antes de llegar a la puerta. |
| Escape (`ChaseFloor`) | Sube "sabe en qué escondite está" a `Chasing`. Queda cubierto igual: la persecución termina en la puerta y el agarre se mide ahí. |
| Debug | ✅ F9: fila `escondite` (sabe / sospecha / lo distingue por… · yendo / revisando). Gizmos: cono "bajo mesa", línea al escondite conocido o sospechoso y su alcance residual. Editor del SO: cuña "bajo mesa". |

### 3.6 Memoria por escondite

Cada escondite tiene un estado que ve el jugador:

```
Normal ──(usos repetidos)──▶ Sospechoso ──(más usos)──▶ Quemado
                              revisa primero             lo rompe: puerta arrancada,
                                                         el escondite deja de servir
```

"Quemado" es la destrucción de coberturas de Requiem. El escondite no se apaga por código invisible:
**el Nemesis lo rompe**, a la vista o dejando la evidencia (la puerta en el piso). `CanInteract()`
pasa a `false`, `IsFinished()` (nuevo en `IInteractable`, lo usa el prompt) a `true`, y se cambia
el modelo. La persistencia va en el tracker de hábitos, no en
`PuzzleStateManager` — ver D3.

---

## 4. Catálogo de cheeses de WIRED

Para cada estrategia dominante: qué hace el jugador, por qué funciona hoy, qué señal se cuenta y
cuál es la contra-jugada. Las contra-jugadas son **escalonadas** y **sorteadas**, no seguras: el
Nemesis tira dados en casi todo (patrulla, spawn, desvío, búsqueda), y un anti-cheese determinista
se aprende igual que el cheese.

### C1 — El escondite eterno
- **Qué hace:** se esconde y espera hasta que el Nemesis se va.
- **Por qué funciona:** la búsqueda dura 15 s y después patrulla. Nunca vuelve a propósito.
- **Mitigante que ya existe:** los timers de módulo corren mientras estás escondido (sólo se
  pausan durante una captura, desde el agarre hasta que el jugador se levanta). Pero sólo si hay un
  módulo activo; entre módulos esconderse es gratis.
- **Señal:** búsquedas que terminan sin encontrarlo con el jugador escondido dentro del radio de
  barrido.
- **Contra-jugada:** (1) *sensibilidad creciente* (Mr. X): la siguiente búsqueda en esa zona dura
  más y lleva el boost de sentidos del Director; (2) *emboscada de salida*: al terminar la búsqueda,
  en vez de irse, se planta en un punto con línea de visión a la salida del escondite durante un
  tiempo acotado, y después se cansa y sigue (las emboscadas de Alien tienen duración máxima). Se lee
  desde adentro por la cámara del escondite, y quieto no hace pasos: el tell es la respiración de
  `NemesisAudio`.

### C2 — Siempre el mismo escondite
- **Señal:** usos por `SpotId` estando cazado.
- **Contra-jugada:** 2 usos → sospechoso (lo revisa primero); 4 → quemado (lo rompe).

### C3 — Esconderse a la vista
- Entrar mientras te persigue o te está mirando. Lo resuelve el **Nivel A** en el acto; no hace falta
  contador.

### C4 — El loop alrededor de un obstáculo (el bug de la mesa de Dimitrescu)
- **Qué hace:** da vueltas alrededor de una columna, una mesa o un bloque de estantes.
- **Por qué funciona hoy:** `SO_PlayerMovement` = 2.5 m/s × sprint 1.8 = **4.5 m/s**;
  `SO_NemesisMovement.chaseSpeed` = **3.0 m/s**. Corriendo, el jugador siempre es más rápido. En el
  loop la vista se renueva a cada vuelta, así que `"lo está viendo"` sostiene `Chasing` para siempre.
  `NemesisStuckEscape` no dispara porque el cuerpo sí se mueve. No hay nada que diga "no estoy
  acortando distancia".
- **Señal:** `NemesisChaseProgress` — en `Chasing` con creencia de vista fresca, la distancia **por
  NavMesh** a la creencia no baja `chaseMinProgress` (1.5 m) en una ventana de `chaseProgressWindow`
  (4 s).
- **Contra-jugada:**
  1. *Invertir el giro.* Es el truco del Flanker de RE4R, pero sobre el grafo de waypoints y no
     sobre áreas del NavMesh, porque acá no se rebakea en runtime. `NemesisPursuit` ya puntúa
     waypoints de desvío; se agrega una penalización a los que están cerca del **rastro sensado**
     que ya lleva `NemesisController` (§ *Sensed trail*), y se sube `ChaseDetourTolerance` mientras
     dure el estancamiento. El A* devuelve el camino por el otro lado sin escribir un comportamiento
     de flanqueo.
  2. *Soltar y emboscar.* Si sigue sin progreso, deja la persecución a propósito y va a un punto de
     salida probable, fuera de tu vista, a esperar (Zone Defense).
  3. *Desbloqueado* (≥ 2 estancamientos): las persecuciones siguientes arrancan ya con la
     penalización del rastro.
- **Nunca** subir la velocidad del Nemesis para arreglarlo: el análisis lo prohíbe y se nota.
  **Las penalidades de módulo no lo resuelven:** M2 (menos sprint) es hoy el módulo fatal
  (`SO_GameOverRules`: `useFatalModule = 1`, `fatalModule = M2_Chest`), así que nunca se aplica como
  penalidad; y M1 (`walkAndRunSpeedPercent` 80) deja el sprint en 4.5 × 0.8 = **3.6 m/s**, todavía
  más rápido que el Nemesis. El loop sigue abierto en toda la partida.

### C5 — El umbral del Hub
- **Qué hace:** se para en la puerta del Hub mirando al Nemesis, que no puede entrar, y sale cuando
  se va.
- **Por qué funciona:** el Hub es `Not Walkable`; el Nemesis espera afuera hasta que se le vence la
  búsqueda. Ya está documentado el riesgo de "te agarra por el vano".
- **Límite:** el Hub sigue siendo inviolable. Es regla del proyecto y no se negocia.
- **Contra-jugada:** (1) al perderte en el Hub no se queda mirando la puerta: se retira fuera de tu
  vista (no verlo irse no es lo mismo que se haya ido); (2) desbloqueado (≥ 2): emboscada en una de
  las salidas del Hub, fuera de la vista y acotada en tiempo.
- **Nota de implementación:** contar que el jugador está en el Hub necesita un trigger que sólo
  **informe** presencia. No bloquea nada, así que no contradice la regla de que el Hub no tiene
  lado C#.
- ✅ **Construido (22/09): el Director ya no puede fabricar C5.** `NemesisSafeZones` lee como
  huellas los volúmenes *Not Walkable* que llevan un `SafeZoneMarker` (un componente sin lógica al
  lado del `NavMeshModifierVolume` del Hub; sin trigger ni capa nueva) y responde "¿dentro del
  Hub?" y "¿a qué distancia del Hub?". El marcador es necesario porque *Not Walkable* no quiere
  decir refugio: los 28 `Bridges_support_2` de Zona1 llevan uno adentro (`NavMesh Blocker`,
  0.86 × 1.11 m) y, contados como Hub, rechazaban `panel electrico`, `fondo norte` y `ala oeste`.
  Sin ningún marcador, el Director lo avisa al arrancar y el validador lo reporta. Con eso:
  - ninguna zona de presión puede tener el centro a menos de `NemesisSafeZones.Clearance` (6 m) del
    Hub: el Director la rechaza en runtime, y el gizmo y el validador la marcan;
  - el ruido sintético y la entrada tipo Mr. X descartan puntos a menos de 6 m del Hub en el mismo
    piso;
  - la gravitación por la posición real del jugador (`ZoneBiasUsesRealPlayer`) se apaga mientras el
    jugador está dentro del Hub: si no, la patrulla rondaba la puerta todo el tiempo que se quedara
    adentro;
  - la sensibilidad creciente no presiona mientras el jugador está en el Hub, y su reloj de silencio
    se pausa.
  Lo que el Nemesis **sintió** sigue valiendo (si te vio entrar, busca en la puerta hasta que se le
  vence la búsqueda). La contra-jugada (1) sale sola de la Fase 5: una persecución que termina en el
  Hub es un pico, y el Relax que sigue es la retirada.

### C6 — Ruido de cebo *(futuro)*
- Hoy no hay objetos para tirar. `SightCommitTime` (6 s) ya impide escaparse de una habitación
  comprometida con un ruido de afuera. Si se agregan tirables: contar los ruidos que no son del
  jugador; al N-ésimo el Nemesis va, pero busca en dona alrededor de **quien lo tiró** y no del
  impacto.

### C7 — Montacargas de ida y vuelta *(vigilar)*
- Subir y bajar para cortar la persecución. El claim, el compromiso de 12 s y el enfriamiento de 10 s
  ya lo acotan. Si aparece en playtest: emboscada en el landing de llegada en vez de perseguir.
  Las bajadas del [§15](#15-bajadas-entre-pisos) lo cierran en un sentido: si el jugador baja en el
  montacargas, el Nemesis puede bajar por una bajada sin esperar la cabina. Para subir sigue
  necesitando el montacargas o las escaleras.

### C8 — Espiar con la cámara en tercera persona *(no es cheese de IA)*
- La cámara orbital va a unos 3.4 m detrás del personaje y deja ver por encima de coberturas y
  alrededor de esquinas sin exponer el cuerpo. **No se arregla con IA**: la lección de los Lickers de
  Requiem es que ninguna condición puede depender de la cámara del jugador. Es decisión de diseño
  (D6). Adentro del escondite, los límites de la cámara interior del spec ya lo acotan.

### C9 — Entrar y salir para "resetear"
- Cubierto por el Nivel A (si te ve, te ve) y por C2 (cada entrada cuenta como uso).

### C10 — Quieto y agachado en un rincón *(no es cheese)*
- Quieto el emisor se apaga: es el núcleo del sigilo, diseñado así. La periferia y la proximidad
  extrema ya lo acotan. No se toca.

---

## 5. Hábitos del jugador y contra-jugadas

### 5.1 Reglas de diseño

| # | Regla | Por qué |
|---|---|---|
| R1 | **Se cuentan escapes, no intentos.** Un uso de escondite suma sólo si el Nemesis estaba cazando cerca (`Searching` / `Chasing` / `Investigating`) y no te encontró. | Esconderse por las dudas no es explotar nada. Castigarlo enseña a no usar la mecánica. |
| R2 | **La contra-jugada pasa en el mundo, nunca en la mecánica.** El escondite no se desactiva; el Nemesis lo rompe. | "Cerrá el exploit con comportamiento, no con un parche." |
| R3 | **El jugador la ve.** La primera vez que se ejecuta cada contra-jugada tiene que pasar con el jugador en rango de ver u oír. Si no, deja la evidencia. | Una luz que se apaga sola es un bug; un monstruo que arranca el cable es una escena. |
| R4 | **El tracker es director; su ejecución es agente.** El tracker lo sabe todo (como el Director), pero sólo cambia *qué comportamientos existen*. *Dónde* los aplica el Nemesis sale de lo que sintió: revisa escondites dentro de su barrido, no el escondite donde estás. | Si el tracker le dijera dónde estás, sería trampa, y se notaría. |
| R5 | **Lo aprendido sobrevive a la captura** y se resetea con New Game (`ISessionResettable`). Ver D3. | El xenomorfo no olvida el lanzallamas porque te agarró. |
| R6 | **Enums que sólo se agregan al final** (`EExploitKind`, `ECounterplay`). `SpotId` con validador de editor, como `[PuzzleId]`. | La misma trampa de serialización que ya costó en la escalera. |
| R7 | **Decaimiento lento** (opcional): un hábito que no se repite en N minutos baja. Ver D4. | Que lo aprendido en la zona 1 no castigue un estilo distinto en la zona 3. |

### 5.2 Datos

`SO_CounterplayRules` (asset, reordenable), una fila por regla:

| Campo | Ejemplo |
|---|---|
| `EExploitKind kind` | `EscapedWhileHidden` |
| `int threshold` | 3 |
| `ECounterplay unlocks` | `CheckHidingSpots` |
| `float chanceAtUnlock` | 0.35 |
| `float chancePerExtraUse` | 0.1 (tope 0.85) |

Probabilidad y no interruptor: al desbloquear, la contra-jugada **puede** pasar, y es más probable
cuanto más insistís. Nunca 100 %: tiene que seguir siendo una apuesta.

### 5.3 Qué se cuenta y quién lo registra

| `EExploitKind` | Lo registra | Cuándo |
|---|---|---|
| `EscapedWhileHidden` | `NemesisHidingAwareness` | Una búsqueda termina (`Searching` → otro estado) sin captura, con el jugador escondido dentro del radio de barrido. |
| `SameSpotReused` | `PlayerHabitTracker`, desde `HidingEvents.OnEntered` | Entrada a un `SpotId` estando cazado (por escondite). |
| `ChaseStalled` | `NemesisChaseProgress` | Cada ventana sin progreso (C4). |
| `SafeZoneEscape` | El trigger informativo del Hub + `NemesisEvents.OnChaseEnded` | Una persecución termina con el jugador en el Hub. |

### 5.4 Contra-jugadas desbloqueables

| `ECounterplay` | Se desbloquea con | Qué hace | Dónde vive |
|---|---|---|---|
| `CheckHidingSpots` | 3 × `EscapedWhileHidden` | Nivel C: `Searching` incluye los escondites dentro de su barrido. | `NemesisSearchingState` + `NemesisHidingAwareness` |
| `PrioritizeSuspiciousSpots` | 2 × `SameSpotReused` (por escondite) | Ese escondite se revisa primero. | ídem |
| `BurnHidingSpot` | 4 × `SameSpotReused` (por escondite) | Lo rompe (§3.6). | `HidingSpot` + `NemesisHidingAwareness` |
| `ExitAmbush` | 3 × `EscapedWhileHidden` | Al terminar la búsqueda, espera mirando la salida (C1). | `NemesisSearchingState` (fase final) |
| `ChaseFlank` | 1 × `ChaseStalled` | Penalización del rastro en `NemesisPursuit` desde el arranque (C4). | `NemesisPursuit` |
| `ZoneDefense` | 2 × `ChaseStalled` o 2 × `SafeZoneEscape` | Emboscada en salidas probables (C4, C5). | Puntos `NemesisAmbushPoint` autorados + `NemesisDirector` |

**Nota sobre Zone Defense:** es la única que necesita que el Nemesis vaya a un lugar donde **no**
te sintió. Para que no sea trampa se ejecuta como las demás palancas del Director: presión sobre la
zona de la salida (ancla y pesos) y, como mucho, la entrada fuera de vista con su pausa. Nunca
escribiendo el estado.

---

## 6. Director: tensión y ritmo

### 6.1 Por qué hace falta antes de las contra-jugadas

Un monstruo que aprende y nunca afloja hace que el jugador abandone (análisis §2.2 y §8.1: la
tensión se administra, no se maximiza). Hoy los únicos respiros son la gracia post-captura y el fin
de la búsqueda. Si se suman contra-jugadas sin un Relax, el juego se vuelve más difícil y peor.

Y cada captura cuesta: `captureModuleTimePenalty` (30 s) se descuenta de los timers de módulo, y con
M2 como módulo fatal esos segundos acercan el Game Over. Toda contra-jugada que convierte un escape
en una captura (Nivel A, `CheckHidingSpots`, emboscadas) gasta ese recurso. Por eso el Relax va
**antes** de la Fase 6, sin excepción.

**Ojo:** todo esto supone un Director andando, y en `WIRED_Zona1_Blockout` está desactivado desde
que se agregó. Activarlo es un paso sin código de la Fase 0
([§14.2](#142-activar-el-director-en-zona1-fase-0-sin-código)).

### 6.2 `NemesisTension` — el medidor

Un componente de un solo propósito: calcula un float. No decide nada.

| Sube con | De dónde sale (ya existe) |
|---|---|
| Proximidad por NavMesh | `NemesisEvents.OnProximityChanged` (0..1, ya se calcula todos los frames) |
| Persecución activa | `NemesisEvents.OnChaseStarted` / `OnChaseEnded` |
| El jugador **ve** al Nemesis | Raycast de la cabeza del jugador al pecho del Nemesis contra `obstacleMask`. **Estado del mundo, no la cámara** (regla de los Lickers). |
| Escondido con el Nemesis buscando cerca | `HidingEvents` + `NemesisEvents.OnStateChanged` |
| Captura | pico |

**No decae** mientras hay `Chasing` o `Catch` (la regla de Left 4 Dead: sin esto el medidor baja en
plena persecución y el Director vuelve a apretar antes de tiempo).

**Arranca con `NemesisEvents.OnActivated`**, no con la carga del nivel: antes de eso el Nemesis está
dormido (cinemática de despertar, `activatedByPuzzleId`) y `quietTimeout` contaría un silencio que
es de diseño.

### 6.3 Estados de ritmo

`BuildUp → SustainPeak (3–5 s) → PeakFade → Relax (30–45 s) → BuildUp`

| Estado | Palancas (todas las que ya existen) |
|---|---|
| `BuildUp` | Gravitación de zona normal. Si pasan `quietTimeout` segundos sin contacto → **sensibilidad creciente**: `RequestPressure` sobre la zona del jugador con intensidad en rampa. Es el anti-estancamiento de Mr. X y cubre C1 y C10 de forma global. |
| `SustainPeak` | Nada nuevo. |
| `PeakFade` | Corta el ruido sintético y el boost. **Espera el final natural del encuentro**: nada de `Chasing` y ninguna búsqueda con creencia de vista fresca. |
| `Relax` | **Retirada:** presión sobre la `NemesisPressureZone` más lejana del jugador (por NavMesh). La patrulla gravita lejos. Los sentidos siguen andando: si te lo cruzás, te persigue. Honesto. |

**El Director sigue sin tocar el FSM.** El ritmo sólo mueve ancla, pesos, ruido y sentidos. Un
`PeakFade` que no puede sacar al Nemesis de `Chasing` es exactamente lo que tiene que pasar: el Relax
arranca cuando el encuentro termina solo.

Los `puzzleTriggers` del Director son golpes narrativos y le ganan al ritmo (entran aunque esté en
Relax), igual que los jefes de L4D no respetan el pacing.

### 6.4 Debug

Una fila más en F9: estado de ritmo, tensión, tiempo restante del estado y zona de presión activa.
Sin esto, "¿por qué se fue justo ahora?" no se puede contestar.

---

## 7. Escalada por progreso

Ya está decidido en `docs/CLAUDE.md` y en `Nemesis-System.md`: cuenta **puzzles**, no módulos; lee
`completedPuzzles.Count`, no suma eventos; aplica una copia runtime de `SO_NemesisData` que pasa por
`BaselineData`.

Cómo convive con este plan:

| Sistema | Qué cambia | Duración |
|---|---|---|
| Escalada | Números (sentidos, tiempos de búsqueda) | Permanente, vía `BaselineData` |
| Boost del Director | Números | Préstamo sobre la escalada, se devuelve |
| Hábitos | **Qué comportamientos existen** (flags), no números | Sesión |

No se pisan porque tocan cosas distintas. **La trampa de `BaselineData` sigue vigente:** nada de este
plan cachea un `SO_NemesisData` para restaurarlo después.

Siguiendo al análisis (§12.2), la escalada mueve sentidos y tiempos, **nunca la velocidad**.

---

## 8. Arquitectura resultante

`★` = nuevo · el resto ya existe

```
                     ┌──────────────── DIRECTOR (omnisciente, no toca el FSM) ─────────────────┐
                     │  NemesisDirector ── palancas: ancla · pesos · ruido · sentidos · entrada│
                     │     ▲                                                                   │
                     │  ★ NemesisTension ── medidor + BuildUp/Sustain/Fade/Relax               │
                     │  ★ PlayerHabitTracker ── cuenta exploits → desbloquea ECounterplay      │
                     │        ▲ SO_CounterplayRules ★                                          │
                     └────────┼────────────────────────────────────────────────────────────────┘
                              │ eventos (HidingEvents ★, NemesisEvents, PlayerEvents)
┌─────────────────────────────┼─────────────── AGENTE (sólo lo que sintió) ───────────────────────┐
│ PERCEPCIÓN                  │  CREENCIA                        DECISIÓN (una sola voz)          │
│ FieldOfView ─(★ nivel B)────┼─▶ TryGetBelief / BeliefAge ────▶ NemesisDecision                  │
│ FieldOfListening            │   ★ NemesisHidingAwareness        + SO_NemesisPriorities           │
│                             │     (KnownSpot, sospechas)        ★ KnowsHidingSpot, IsCheckingSpot│
│                             │   ★ NemesisChaseProgress          ★ IsChaseStagnant                │
│                             │                                       │                           │
│                             │                          ┌────────────▼────────────┐              │
│                             │                          │ estados: Patrol, Invest.,│             │
│                             │                          │ Chasing (★ rastro),      │             │
│                             │                          │ Searching (★ revisar),   │             │
│                             │                          │ Catch (★ sacar), Travers.│             │
│                             │                          └──────────────────────────┘             │
└─────────────────────────────┴───────────────────────────────────────────────────────────────────┘
          ▲
 JUGADOR  │  ★ HidingSpot + SO_HidingData · PlayerHiddenState (real) · CurrentHidingSpot ★
          │  AudioEmitingZone (respiración por pulsos)
```

**Qué hace cada componente nuevo, y qué queda afuera de cada uno** (un solo propósito por componente):

| Componente | Hace | No hace |
|---|---|---|
| `HidingSpot` | Entrada/salida, pose interior, colliders propios, estado Normal/Sospechoso/Quemado | Contar usos, decidir nada del Nemesis |
| `PlayerHabitTracker` | Contar y desbloquear | Saber dónde está nadie; ejecutar contra-jugadas |
| `NemesisHidingAwareness` | Qué escondites conoce o sospecha el Nemesis; expone los predicados | Mover al Nemesis; escribir estados |
| `NemesisChaseProgress` | Medir progreso de la persecución; expone `IsChaseStagnant` | Elegir la ruta (eso sigue siendo `NemesisPursuit`) |
| `NemesisTension` | Calcular el medidor y el estado de ritmo | Aplicar palancas (eso sigue siendo `NemesisDirector`) |
| `NoisePulse` | Emitir una esfera de ruido en un punto por un tiempo | Decidir cuándo |
| `NemesisDropLink` | Configurar una bajada de un solo sentido y describirla (alto, tipo, dirección) | Cruzarla (eso sigue siendo `NemesisElevatorUser`); decidir cuándo usarla (eso es el costo de área y la escalera) |

`NemesisHidingAwareness` y `NemesisChaseProgress` son hermanos del facade, como `NemesisPathOracle`:
se agregan solos y el estado los consulta a través de `NemesisStateManager`.

---

## 9. Fases de implementación

Orden recomendado. La Fase 4 no depende de los escondites y arregla un cheese que ya existe, así que
puede ir en paralelo con la 1.

### Fase 0 — Ajustes sin código
- ✅ `patrolWaitVariance` → 0.6 en `SO_NemesisData.asset` (19/09; estaba en 0.25).
- ✅ `NemesisAudio` movido de la instancia de Zona1 a `Nemesis.prefab`, con `Catch` autorado
  (misma respiración que `Chasing`, así el loop no se reinicia en el agarre: es el caso que el
  propio código anticipa). `Traversing` **no** hace falta autorarlo — si no tiene entrada, el código
  le presta el loop de `Chasing`.
- ✅ D5 decidido e implementado (ver §12).
- ✅ Director activado en Zona1 ([§14.1](#141-el-director-hoy-estado-en-zona1)). Se había perdido
  (entró en `359081fd` y el merge `16b1962c` del 20/09 se quedó con la escena que no lo tenía); se
  rehízo el 22/09 con seis zonas y dos disparadores. El Nemesis se despierta con `sp2_contenedores`
  (valor del prefab, a propósito: las puertas del montacargas se abren con `sp1`).
- **Pendiente de jugar:** F9 tiene que mostrar esperas distintas en cada waypoint; F10 tiene que
  listar las cinco zonas y un botón de presión tiene que inclinar la patrulla hacia esa zona en uno
  o dos ciclos de ruta (12 s). Nada de esto se puede verificar sin entrar a Play.

### Fase 1 — Escondites, lado jugador *(prerrequisito)* — ✅ construida (commit `9eba9b46`)
- Área de prueba: `TestIñaki.unity` → *Hiding Test Area* (`Tools/Hiding/Build Test Area (TestIñaki)`),
  con los prefabs `Prefabs/HidingSpotFather/HidingSpot_Locker`, `_UnderTable` y `_Container`, y
  blends de 0.3 s hacia la cámara interior (`CB_HidingSpotBlends`) en TestIñaki y la testbed. **Falta
  en Zona1:** no hay ningún escondite puesto ni el blend asignado en su `CinemachineBrain`.
- `HidingSpot`, `SO_HidingData`, enum de tipo, `PlayerHiddenState` real, cámaras interiores,
  respiración por pulsos, `F` para aguantar, guard de `Tab`, snapshots del mixer.
- `CurrentHidingSpot`, `HidingEvents`, `ApproachPoint`, `SpotId`.
- Limpieza en todas las salidas; se borra la tecla `R`.
- **Verificación:** los casos del spec; con F10, el Nemesis no te ve; la respiración se oye a la
  distancia de los gizmos; después de salir el emisor queda como estaba (caminar vuelve a sonar a 4).

### Fase 2 — El Nemesis sabe de escondites — ✅ construida (21/09, sin commitear), falta jugarla
- ✅ `NemesisHidingAwareness`: Nivel A (visto entrando, con línea de vista a la puerta; sospecha sólo
  con contacto vivo) y Nivel B (visibilidad residual por tipo; sospecha al pasar el umbral, conocido
  al llenarse).
- ✅ **Arreglo de §3.3**, más los dos hallazgos que aparecieron al construirlo: proximidad plana desde
  los pies (antes nunca disparaba en el mismo piso) y "detectado escondido = escondite conocido", con
  el agarre medido en la puerta.
- ✅ `Searching` va primero al escondite conocido (después al sospechoso); `Investigating` revisa el
  sospechoso; `Catch` con fase de sacar al jugador, que aparece en la `ExitPose`.
- ✅ Predicados `KnowsHidingSpot`, `IsCheckingSpot`, `SuspectsHidingSpot` (al final del enum) y tres
  peldaños (**asset y `BuildDefaultLadder()`**, verificados idénticos en Unity).
- ✅ `underTableVisionMultiplier` al final de `SO_NemesisData` + editor + gizmos; fila `escondite` en F9.
- ✅ Prioridad de habitación (pedido del 21/09): visto entrando a una habitación, los puntos de adentro
  van primero (`NemesisRooms` lee la habitación del nombre del piso, `<SALA>_Floor_<n>`, o del padre).
- **Verificación:** casos 1–5, 11 y 17–21 del [§13](#13-casos-de-prueba), en TestIñaki (la testbed no
  tiene escondites). Checklist completo: `docs/Checklist-NemesisTestbed.md`.
- **Pendiente:**
  - Animación `Pull Out` (§15.5) y SFX de puerta: hoy el pull-out son 0.8 s de la animación de
    agarre. Con la animación, revisar si 0.8 s alcanza (el consejo propuso 1.2 s).
  - **Aviso audible al saber el escondite** (un sting de voz): hoy nada le dice al jugador, desde
    adentro, que el Nemesis *sabe* y no que *adivina*. Es el margen real para decidir salir (D1).
  - Alternativa a probar (D17): al pasar el umbral por las rendijas, clavar la mirada en el
    escondite (`NemesisLookAround`) en vez de ir directo a la puerta.
  - En escenas que no hornean `Default` (la testbed hornea `Ground|Wall|Props`), el collider sólido
    del container no hace hueco en el NavMesh: pasarlo a `Props` o sumarle un
    `NavMeshModifierVolume` en una capa horneada antes de poner containers ahí.

### Fase 3 — Contar sin reaccionar
- `PlayerHabitTracker` (`ISessionResettable`), `SO_CounterplayRules`, `EExploitKind`, puntos de
  registro (§5.3).
- Panel de hábitos en F9 + log de cada registro.
- **Sin contra-jugadas todavía.** Se juega para calibrar los umbrales con datos y no a ojo.

### Fase 4 — Persecución estancada *(independiente)* — ✅ construida (commit `9eba9b46`), salvo "soltar y emboscar"
- `NemesisChaseProgress`, predicado `IsChaseStagnant`, penalización del rastro en `NemesisPursuit`,
  soltar y emboscar. *Soltar y emboscar no está: ningún peldaño lee `IsChaseStagnant` todavía; va con
  la emboscada de la Fase 6.* La testbed tiene `Column_Loop` en ENTRADA, pero sin waypoints alrededor
  la penalización del rastro no tiene de dónde elegir: sumar 2–3 o probar en `Cover_Pasillo`.
- Autorar a mano una columna o una mesa aislada en `Scenes/Dev/NemesisTestbed.unity`, para tener el
  test de la mesa siempre a mano. (`NemesisTestSceneBuilder` se borró el 17/09; la testbed ya no se
  regenera, así que lo que se agregue queda.) Rebakear el NavMesh de la testbed.
- **Verificación:** caso 7.

### Fase 5 — Tensión y ritmo — ✅ construida (22/09, sin commitear), falta jugarla
- ✅ `NemesisTension` (en el GameObject del Director; el Director lo agrega si falta): medidor 0..1
  alimentado por proximidad, persecución, "el jugador lo ve" (raycast cabeza → pecho con el
  `obstacleMask` de los sentidos, sin cámara) y "escondido con el Nemesis buscando cerca"; pico con
  la captura. No decae en `Chasing` ni `Catch`. Arranca con `NemesisEvents.OnActivated` y se pausa
  con el Nemesis dormido, durante una cinemática (`CinematicState`) y durante el escape
  (`ChaseFloor`).
- ✅ Estados `BuildUp → SustainPeak → PeakFade → Relax`. El pico sólo se dispara desde BuildUp: el
  Relax arranca con el medidor todavía alto, y desde ahí volvía a picar en el frame siguiente.
- ✅ En el Director: PeakFade corta la presión propia del ritmo; Relax presiona la zona más lejana
  por NavMesh **sólo con ancla y pesos** (sin ruido ni sentidos); BuildUp con 90 s sin contacto
  arranca la sensibilidad creciente (0.3 → +0.15 cada 20 s → 1.0) sobre la zona del jugador. Los
  disparadores por puzzle y la API le ganan siempre: mientras hay uno vivo, el ritmo espera.
- ✅ `SO_DirectorPacing` en `ScriptableObjects/Nemesis/`, asignado al Director de Zona1 y al de
  `NemesisTestbed` (el de la testbed tiene 4 zonas que cubren 5 de sus 17 waypoints: alcanza para
  ver la retirada y la sensibilidad, no para calibrarlas).
- ✅ F9: filas `ritmo` (estado, tiempo, medidor, silencio) y `presión` (zona, intensidad, origen,
  tiempo). F10: línea de ritmo y botones *Pico de tensión* / *Saltar silencio* (cambian entradas,
  no estados).
- **Diferencia con el §14.3:** no hay trigger informativo del Hub. Un `SafeZoneMarker` en el mismo
  GameObject que el volumen *Not Walkable* del Hub reusa su caja, así que no hay capa que elegir ni
  collider que mantener alineado. Están en los dos `Safe Area` de Zona1 y en
  `SafeVolume (CORRECT - on Props)` de la testbed. El conteo de `SafeZoneEscape` (Fase 3) puede usar
  lo mismo.
- **Verificación:** caso 9. Con F10: *Pico de tensión* → F9 pasa por SustainPeak y PeakFade a Relax,
  y la fila `presión` muestra la zona más lejana como `retirada`; *Saltar silencio* → la zona del
  jugador aparece como `sensibilidad` y sube un escalón cada 20 s.

### Fase 6 — Contra-jugadas desbloqueables
- `CheckHidingSpots`, `PrioritizeSuspiciousSpots`, `ExitAmbush`, `BurnHidingSpot` (necesita el
  modelo del locker roto), `ChaseFlank`, `ZoneDefense` + `NemesisAmbushPoint`.
- La regla R3 (la primera vez se ve) implementada, no sólo pedida.
- **Verificación:** casos 6 y 8.

### Fase 7 — Escalada por puzzles
- Spec §7.2 con el mecanismo ya decidido (§7).

### Fase 8 — Bajadas entre pisos *(independiente)*
- Detalle completo en el [§15](#15-bajadas-entre-pisos). Tiene tres partes, y cada una se puede
  mergear sola:
  1. **Sin código:** apagar *Generate Links*, rebakear y medir qué links automáticos se pierden
     (§15.2).
  2. **Código:** `NemesisDropLink`, la rama de bajada en `NemesisElevatorUser`, `CrossedDrop` en
     `NemesisNav.NavRoute`, validador y gizmos (§15.4). Funciona con animaciones de placeholder.
  3. **Arte:** las animaciones del §15.5 y el setup del Animator.
- **Verificación:** casos 12–16 del [§13](#13-casos-de-prueba).

**Por qué en este orden:** la 1 es prerrequisito. La 2 cierra el agujero de inmunidad, sin el cual
esconderse rompe el juego. La 3 va antes que la 6 para que los umbrales salgan de datos. La 5 va
antes que la 6 porque contra-jugadas sin Relax frustran (y cada captura de más le cuesta 30 s de
módulo al jugador). La 4 es independiente y arregla algo que hoy ya se puede explotar. La 8 también
es independiente: puede ir apenas termine la Fase 0, y conviene que llegue antes de la 6, porque
`ZoneDefense` y la emboscada de C7 la pueden aprovechar.

---

## 10. Reglas del proyecto que este plan no puede romper

Todas salen de `docs/CLAUDE.md`. Cada una ya costó un bug.

- **Una sola voz escribe `NextState`:** `NemesisDecision`. Ni el Director, ni el tracker, ni un
  componente nuevo.
- **Enums serializados, sólo al final:** `ENemesisPredicate`, `ENemesisState`, `ENemesisThreshold`,
  y los nuevos `EExploitKind`, `ECounterplay` y el tipo de escondite.
- **Un peldaño nuevo va en los dos lugares:** el asset `SO_NemesisPriorities` y
  `BuildDefaultLadder()`.
- **`BaselineData` se lee fresco;** nunca se cachea un `SO_NemesisData` para restaurarlo.
- **El ruido es una esfera que dura más de 0.1 s,** y el emisor del jugador vuelve a como estaba.
- **Eventos estáticos:** suscribir en `Awake`, desuscribir en `OnDestroy`, y `ResetStatics` con
  `RuntimeInitializeOnLoadMethod`.
- **Estado de sesión** → `ISessionResettable`, no tocar los controllers del menú.
- **Distancias por NavMesh** (`NemesisNav`), nunca `Vector3.Distance`, porque hay pisos.
- **El Hub es `Not Walkable` y no tiene lado C# que lo bloquee.** Un trigger que sólo informa está
  permitido.
- **Las cuatro máscaras de "qué es sólido" tienen que coincidir.** No se arregla §3.3 cambiando una
  sola. Desde el 17/09 ya no existen *Repair Layer Masks* ni *Migrate Prop Layers*: *Validate
  Navigation Setup* avisa y el arreglo se hace a mano.
- **Todo NavMeshLink lo cruza `NemesisElevatorUser`** (apaga `autoTraverseOffMeshLink`). Un tipo de
  link nuevo es una rama ahí, no un segundo componente que también mire `isOnOffMeshLink`.
- **Todo teletransporte pasa por `NemesisStateManager.WarpTo`**, también la recuperación de una
  bajada cortada por un respawn. (El aterrizaje normal no es un warp: cierra el link con
  `CompleteOffMeshLink`, como el link simple.)
- **Todo valor tuneable tiene dónde verse:** `SO_NemesisDataEditor`, `NemesisGizmos`, F9.
- **Nada depende de la cámara del jugador.**

---

## 11. Decisiones abiertas

| # | Pregunta | Recomendación |
|---|---|---|
| D1 | Cuando te encuentra escondido, ¿captura directa o te saca y arranca una persecución? | **Captura** (la captura es un costo, no un Game Over). El margen del jugador está **antes**: ver al Nemesis acercarse desde adentro y decidir salir corriendo antes de que abra. **A revisar con playtest:** desde el 18/09 la captura pesa más, porque son 30 s de timer de módulo y M2 es fatal (§6.1). Si en la Fase 3 las capturas desde escondite resultan frecuentes, la alternativa es sacarlo y arrancar una persecución (el costo pasa a ser el riesgo, no el timer). |
| D2 | ¿El Nemesis puede romper escondites para siempre? | Sí, con evidencia visible. Necesita arte: el locker roto. |
| D3 | ¿Lo aprendido sobrevive a la captura y al checkpoint? | Sí. Por eso vive en el tracker y no en `PuzzleStateManager`, que se revierte con el checkpoint. Se resetea con New Game. |
| D4 | ¿Los hábitos decaen? | Sí, lento (del orden de minutos sin repetirlo). |
| D5 | La música de persecución se apaga cuando `Chasing` termina, así que **avisa que te perdió de vista**: es un estado interno filtrado al audio (análisis §12.4). | **Decidido (19/09):** corta al terminar la búsqueda comprometida. Implementado en `NemesisChaseMusic`: `OnChaseEnded` ya no baja el volumen, sino que abre una cola que `OnStateChanged` cierra cuando el estado deja de ser `Searching`/`Traversing`, con `searchTailTimeout` (25 s) de red de seguridad. |
| D6 | Espiar con la cámara orbital (C8). | Aceptarlo, como la mayoría de los juegos en tercera persona. Si molesta, se ajusta la cámara, no la IA. |
| D7 | ¿Locker con visibilidad residual (Nivel B) o ciego salvo proximidad? | Residual y baja. Si no, "riesgo medio" (spec) y "riesgo bajo" (container) son lo mismo. |
| D8 | ¿Va a haber dificultad seleccionable? | Si la hay, se escalan sentidos y umbrales de desbloqueo, nunca la velocidad (análisis §12.2). |
| D9 | Bajadas: ¿el jugador también puede usarlas? | **No, salvo las que diseño quiera compartir.** Si el jugador puede bajar por el mismo hueco, es una ruta de escape de ida que el Nemesis también tiene, y eso está bien. Pero entonces tiene que ser una decisión de nivel, no algo que pase porque falta una baranda. Las que son sólo del Nemesis llevan baranda o collider de jugador. |
| D10 | ¿*Generate Links* sigue prendido en la NavMeshSurface de Zona1? | **Apagarlo** y autorar cada link (§15.2). Un link generado es una bajada o un salto sin animación, sin validar y en lugares que nadie eligió. |
| D11 | ¿Bajadas en patrulla o sólo cazando? | **Cazando** (`Chasing`, `Traversing`, `Searching` hacia una creencia), por costo de link alto en patrulla. Una patrulla que se tira por el hueco cada ronda deja de asustar a la tercera vez. |
| D12 | ¿El Director puede usar una bajada como entrada tipo Mr. X? | Sí, más adelante: la entrada ya muestrea puntos fuera de vista a 10–22 m. Una variante "cae por el hueco de la zona presionada" es un candidato más, no un sistema nuevo. Fuera del alcance de la Fase 8. |
| D13 | Un jugador escondido detectado por proximidad, ¿es un avistamiento o un escondite conocido? | **Decidido (21/09, consejo 4/4): escondite conocido.** Como avistamiento, `Chasing` corría a un punto dentro del mueble y se clavaba en la puerta (§3.3). Además gana el golpe de Alien: llega en silencio, la música pega cuando abre (`Catch` ya está en el set de persecución de la telemetría). |
| D14 | La proximidad y el agarre a través de la carcasa, ¿desde cualquier lado o sólo desde la puerta? | **Decidido (consejo 2 a 1):** la **detección** desde cualquier lado — es presencia, no vista, y restringirla reabre pegarse a la chapa de atrás; una pared detrás del locker igual ocluye. El **agarre**, sólo desde la puerta (se mide contra el `ApproachPoint`). La vista por las rendijas (Nivel B), sólo desde la puerta. *Disidencia:* detección sólo desde la puerta en locker y container. A revisar con playtest si "te detectó por la espalda del locker" se siente omnisciente. |
| D15 | El container (ciego + respiración ×0.5), ¿es dominante? | **No se toca por ahora (consejo 3 a 1).** Es el "riesgo bajo" del spec; lo compensan la colocación (rareza, cámara ±10/±10, el lowpass más pesado) y la Fase 6 (`CheckHidingSpots` revisa escondites fríos). Parecía dominante sobre todo porque el Nivel B del locker estaba muerto (0.25, ver §3.4). *Disidencia:* `containerNoiseMultiplier` a 1.0 ya. Si la Fase 3 muestra que todos eligen container, se sube. |
| D16 | Los 0.8 s de sacarte del escondite, ¿son una ventana para escapar? | **No.** Salir en ese momento te deja en la `ExitPose`, a centímetros del Nemesis y quieto 0.6 s: te agarra igual. Es el golpe de verlo en la puerta. El margen real es **antes** (D1), y para que exista falta el aviso audible cuando el Nemesis **sabe** (Fase 2, pendiente). |
| D17 | Viéndote por las rendijas, al pasar el umbral: ¿va a mirar o se frena y clava la mirada? | **Implementado: va a mirar** (es lo que promete "vio algo de reojo", y antes se iba a un ruido viejo). *Alternativa a probar (consejo):* clavar la mirada en el escondite a 0.4 y conocerlo recién a 1.0 — más legible desde adentro, pero con el mismo final si no cortás el contacto. |

---

## 12. Valores iniciales

Puntos de partida para calibrar con la Fase 3, no para dejar fijos.

| Parámetro | Valor | Origen |
|---|---|---|
| `seenEnteringWindow` | 0.75 s, **y línea de vista a la puerta** | Subida 0.6 s + un barrido de la vista (0.1 s) + margen |
| `lockerVisionExposure` | **0.35** del alcance (2.45 m), sólo por acumulador y sólo del lado de la puerta | Spec: riesgo medio. Con 0.25 quedaba adentro del disco de proximidad (§3.4); con 0.5 agarraba casi siempre en el playtest (§16.4) |
| `underTableVisionMultiplier` | 0.5 (3.5 m) | Spec: riesgo alto. El consejo votó no subirla |
| Proximidad extrema | 1.5 m **plano** desde los pies, sólo mismo piso (`CatchMaxVerticalOffset`) | Antes era una esfera desde el ojo que nunca llegaba al piso (§3.3) |
| `hiddenPullOutTime` | 0.8 s | Placeholder hasta la animación `Pull Out`; el consejo propuso 1.2 s con SFX |
| Frenado al revisar un escondite | 0.25 m del `ApproachPoint` | El de patrulla (1 m) dejaba al Nemesis fuera del agarre y de la proximidad |
| Respiración escondido | Sin cambios (radio 0.8) | Se oye a ~2.3 m por la chapa: el oído mide por camino hasta el emisor proyectado al NavMesh, así que la franja 1.5–2.3 m, donde aguantar `F` decide, existe. El consejo propuso 1.6 y se descartó por eso |
| Umbral sospechoso / quemado | 2 / 4 usos del mismo escondite | Requiem, Isolation (2–3) |
| `CheckHidingSpots` | 3 escapes escondido | Isolation (lockers) |
| `chanceAtUnlock` / por uso extra / tope | 0.35 / +0.1 / 0.85 | Que siga siendo apuesta |
| Duración de la emboscada de salida | 8–15 s, sorteado | Isolation (emboscada con máximo) |
| `chaseProgressWindow` / `chaseMinProgress` | 4 s / 1.5 m (por NavMesh) | Análisis §12.1 |
| Penalización del rastro en `NemesisPursuit` | ×0.2 al peso del waypoint | RE4R (Flanker) |
| `SustainPeak` / `Relax` | 3–5 s / 30–45 s | Left 4 Dead (GDC 2009) |
| `quietTimeout` (sensibilidad creciente) | 90 s sin contacto | Mr. X; ajustar al tamaño del nivel |
| Medidor: ganancias por segundo | proximidad 0.05 (× 0..1) · persecución 0.08 · el jugador lo ve 0.05 (hasta 12 m) · escondido con búsqueda cerca 0.04 · captura = 1 | Una persecución de ~5–6 s llega al pico (0.85); con 0.12 bastaban 4 s y cualquier escapada corta compraba un Relax |
| Medidor: decaimiento | 0.03/s, después de 4 s sin estímulos; nunca en `Chasing`/`Catch` | ~30 s de lleno a vacío, del orden del Relax |
| Rampa de la sensibilidad creciente | 0.3, +0.15 cada 20 s, tope 1.0 | Llega al máximo en ~100 s después del `quietTimeout` |
| Intensidad de la retirada | 0.8, sólo ancla y pesos | Si te lo cruzás, sus sentidos están intactos |
| `patrolWaitVariance` | 0.6 s (hoy 0.25) | `docs/CLAUDE.md` |
| Bajadas: alto mínimo / máximo | 1.5 m / 5 m (verificar el alto real entre `PISO_01` y `PISO_02` en el editor) | Debajo de 1.5 m lo cubre `agentClimb`/escalón; arriba de 5 m un humanoide no cae sin consecuencias |
| Bajadas: umbral salto corto ↔ descolgarse | 2.5 m (= `FloorHeightThreshold`) | Mismo número que ya separa "otro piso" de "desnivel" |
| Bajadas: costo del link | 2 cazando / 20 en patrulla (D11) | Más barato que el montacargas (10) al cazar |
| Bajadas: recuperación al aterrizar | 0.6–0.9 s (la duración del clip) | La ventana del jugador; menos se siente injusto |
| Bajadas: enfriamiento por link | 8 s | Que no suba por la escalera y vuelva a tirarse en loop |

Referencias del proyecto para calibrar: jugador 2.5 m/s (agachado 1.25, corriendo 4.5; con M1
2.0 / 3.6); ruido del jugador agachado 2.5 m / caminando 10 m / corriendo 15 m (tope); Nemesis
patrulla 2.75 / investiga 2.5 / persigue 3.0 / busca 2.75; vista 7 m, foco 80°, periferia 170°;
oído 15 m de tope; proximidad extrema 1.5 m; alcance de captura 1 m; búsqueda 15 s; gracia de
persecución 2.5 s.

---

## 13. Casos de prueba

En `Scenes/Dev/NemesisTestbed` (F9 HUD, F10 consola) y después en `WIRED_Zona1_Blockout` desde
`Bootstrap`. Los de escondites (1–5, 11, 17–21) van en `TestIñaki.unity` → *Hiding Test Area*
mientras la testbed no tenga escondites. El checklist completo de la testbed, paso a paso, está en
`docs/Checklist-NemesisTestbed.md`.

| # | Situación | Esperado |
|---|---|---|
| 1 | Te persigue, entrás al locker a la vista. | Nivel A: va directo al `ApproachPoint`, lo abre y te captura. F9 muestra `"sabe en qué escondite está"` y `escondite: sabe … (lo vio entrar) · yendo → revisando`. No se queda clavado en la puerta. |
| 2 | Te persigue, doblás una esquina, entrás al locker fuera de su vista. | Barre la habitación y no revisa el locker (sin desbloqueo). Se va a los 15 s. Cuenta un `EscapedWhileHidden`. Tampoco **sospecha** aunque el medidor siga bajando de la persecución. |
| 3 | Escondido en el locker, el Nemesis pasa a 1 m. | Te detecta por proximidad aunque el collider del locker esté en el medio (arreglo §3.3): `escondite: sabe … (lo tiene encima)`, sin `Chasing`, y te saca. |
| 4 | Debajo de la mesa, el Nemesis mirando de frente a **3 m**. | La sospecha sube sin arrancar persecución (`lo distingue por …`); al pasar el umbral va a mirar; si llega a 1, pasa a "sabe". **A 5 m no pasa nada:** el alcance bajo la mesa es 7 × 0.5 = 3.5 m. |
| 5 | Escondido, soltás `F` (exhalás) con el Nemesis a 4 m. | `Investigating` hacia el escondite, no `Chasing`. |
| 6 | Tres escapes escondido, cuarta búsqueda. | A veces (sorteado) revisa escondites dentro de su barrido. La primera vez que lo hace estás en rango de verlo u oírlo (R3). |
| 7 | Loop alrededor de una columna corriendo. | En ~4 s F9 marca estancamiento; corta por el otro lado o suelta y embosca. Nunca se acelera. |
| 8 | Cuatro usos del mismo locker estando cazado. | Lo rompe: puerta arrancada, el locker ya no ofrece `[E]`. |
| 9 | Persecución larga que termina en escape. | La tensión no baja durante `Chasing`; al terminar, `PeakFade` → `Relax`, y la patrulla se va lejos durante 30–45 s. Si te lo cruzás igual, te persigue. |
| 10 | Te capturan con contadores altos y hacés respawn. | Los contadores **no** vuelven atrás con el checkpoint. New Game los pone en cero. |
| 11 | Salís del escondite durante una captura, un checkpoint o una descarga de escena. | El emisor, la cámara y los modificadores vuelven a lo normal; `CurrentHidingSpot` queda en `null`. En la captura, `CurrentHidingSpot` se limpia **antes** de la animación de levantarse (`EStandUp.AfterCapture`), que siempre pone al jugador de pie. |
| 12 | Te persigue en `PISO_02`, bajás por la escalera o el montacargas. Hay una bajada entre los dos. | Pasa a `Traversing` (`RouteToBeliefCrossesFloors` ahora también ve bajadas), va al borde, anticipa (se ve y se oye), cae, aterriza con recuperación y sigue en `Chasing`. F9 muestra el link. |
| 13 | Estás en `PISO_01`, él en `PISO_01`, y la única bajada es de arriba hacia abajo. | Nunca intenta subir por la bajada: el link es de un solo sentido. Usa escalera o montacargas. |
| 14 | Llega una captura o un respawn mientras está en el aire (forzarlo desde F10). | La caída termina igual; nada se cancela a mitad del salto. El respawn lo pone en tierra. |
| 15 | Parado justo abajo del punto de aterrizaje. | Cae igual, **no** te agarra en el aire; la captura sólo puede empezar después de la recuperación. |
| 16 | Patrullando, sin creencia. | No usa la bajada (costo de patrulla), salvo que no exista otra ruta al waypoint. |
| 17 | Te ve de reojo (medidor subiendo, sin llegar a 1) mientras te subís al locker. | `sospecha de un escondite` → `Investigating` camina a la puerta y mira 4 s. Si seguís adentro, al llegar te detecta por proximidad y te saca; si saliste antes, lo da por revisado y se va. |
| 18 | En el locker, él pasa de frente a 2–3.5 m. | Por las rendijas: `lo distingue por …`, sospecha → va a mirar → te saca. Por detrás del locker, o dentro del container, nada (salvo que pase a ≤1.5 m). |
| 19 | Te saca de un escondite. | Después de 0.8 s quieto en la puerta, captura; aparecés en la `ExitPose`, afuera del mueble, no adentro ni despedido por la física. |
| 20 | Pasa a ≤1 m de la puerta de un locker ocupado **por detrás de una pared**. | Nada: la pared ocluye; la carcasa es lo único que se atraviesa. |
| 21 | Durante el escape (cinemática final en marcha, `ChaseFloor`), te escondés a su vista. | Sigue en `Chasing`, llega a la puerta y te agarra (el agarre de un escondido se mide en la puerta). |

---

## 14. Cómo se arma en Unity

Guía de armado del Director (lo más nuevo, y lo que hoy está sin armar en el nivel) y dónde va cada
pieza nueva del plan. Se relevó leyendo `WIRED_Zona1_Blockout.unity`, los prefabs y los assets como
YAML, porque esta sesión no tiene conector MCP de Unity. Lo marcado *verificar en el editor* no se
pudo abrir.

### 14.1 El Director hoy: estado en Zona1

> **22/09/2026: rehecho.** El setup de la Fase 0 (commit `359081fd`, 19/09) se perdió en el merge
> `16b1962c` (20/09). Se volvió a armar con las cinco zonas de entonces, una sexta y la protección
> contra C5. Los dos cambios de diseño respecto del 19/09 son decisiones del equipo: el Nemesis se
> despierta con `sp2` (el montacargas está cerrado hasta `sp1`), y el puzzle central no tiene
> disparador porque dispara la cinemática del escape.

| Qué | En la escena (22/09) | Qué implica |
|---|---|---|
| GameObject `Nemesis Director`, bajo `---- SISTEMA ----` | **Activo**, en el origen (su posición no se usa), con `NemesisDirector` + `NemesisTension` | Corre `Awake` y hay singleton: la API responde y el ancla de presión llega a `NemesisController`. |
| `Puzzle Triggers` | 2: `sp2_contenedores` → `fondo norte` 0.5 / 45 s · `sp3_valvulas` → `valvulas` 0.7 / 60 s **+ entrada Mr. X** | La presión sube con el progreso. La entrada no va en `sp2` porque es el puzzle que despierta al Nemesis (§14.2), ni en el central (cinemática). |
| `NemesisPressureZone` | 6, bajo `Nemesis Director / Pressure Zones`, reubicadas a mano en el editor el 22/09: `montacargas` (-16.6, 0, 9.9) r 10 · `panel electrico` (-23.1, 0, 22) r 10 · `valvulas` (28.6, 0, 25.6) r 12 · `fondo norte` (0.7, -0.8, 27.4) r 15 · `ala oeste` (-9.8, 0, 27.3) r 9 · `centro este` (15.9, 0, 12.3) r 11 | Cubren **38 de 39** waypoints (seleccionando el Director se ve cuál falta). Ningún centro queda a menos de 6 m del Hub: el más cercano es `centro este`, a 7.8 m. El ruido y la entrada igual evitan los 6 m alrededor de la puerta. |
| `SafeZoneMarker` | En `'Safe Area '` y `Safe Area  (1)` | Es cómo el Director sabe dónde está el Hub (C5). Sin él la protección se apaga, y lo avisan el Director y el validador. |
| `SO_DirectorPacing` | Asignado | El ritmo de la Fase 5 está prendido. |
| Quién llama a la API | Los disparadores, el ritmo y `NemesisTestConsole` (F10) | Ni los módulos ni la narrativa piden presión todavía. |
| `noiseLayer` 8 (`DetectableAudio`) contra el `listenMask` del prefab (256) | ✅ Coinciden | El ruido sintético se oye. Si no coincidieran, `Start` lo reporta. |
| El resto de la tuning del componente | Valores por defecto: evaluación cada 3 s, pesos ×3, ruido cada 9 s con radio 4, sentidos ×1.25, entrada a 10–22 m con 2.5 s de pausa | Sirven para arrancar. |
| Rutas | 4 `NemesisRoute` asignadas al `NemesisController`, con pesos 3 / 1 / 1 / 2; la de peso 3 (`ROUTE 2 2F`) se abre con `sp1_panel_electrico` | La palanca 2 tiene con qué trabajar. |
| Nemesis: `activatedByPuzzleId` | `sp2_contenedores`, del prefab (sin override en la escena) | Despierta con el segundo sub-puzzle. |

**Cómo se ven las zonas.** Cada zona se dibuja como un cilindro: un disco por cada piso donde tiene
waypoints, con etiqueta de id, radio, waypoints cubiertos y presión en vivo (color según la
intensidad), y en magenta si está pegada al Hub o no toca ningún waypoint. Seleccionada, traza una
línea a cada waypoint que cubre. **Seleccionando el Director** se ve la cobertura: cada waypoint
fuera de toda zona marcado como `sin zona`, el contorno del Hub y la banda de 6 m donde no puede ir
ningún centro. *Validate Navigation Setup* lista lo mismo como texto.

### 14.2 Activar el Director en Zona1 (Fase 0, sin código)

1. **El componente.** Prender el GameObject `---- SISTEMA ---- / Nemesis Director`. Va uno solo por
   escena: es un `Singleton` con `CreateSingleton(false)`, así que nace y muere con el nivel. **No va
   en `Bootstrap` ni en `Data`.** No necesita referencias: encuentra solo al Nemesis (aunque esté
   dormido) y a las rutas, por tipo.
2. **Las zonas.** Un contenedor `Pressure Zones` como hijo del Director y, adentro, un GameObject
   vacío por área que valga la pena nombrar, cada uno con `NemesisPressureZone`:
   - **`Zone Id`:** el lugar, no el evento (`sala de bombas`, no `después del puzzle 2`). Es texto
     libre y la comparación no distingue mayúsculas; del lado de los disparadores es un dropdown
     (`[PressureZoneId]`) con las zonas de la escena.
   - **Centro y `Radius`** (12 m por defecto): entre una habitación y un ala. El gizmo se dibuja
     siempre, a escala, y en Play pasa de ámbar a rojo según la presión.
   - **Cada zona tiene que tocar al menos un waypoint de una ruta desbloqueada.** La palanca 2 elige
     las rutas que tienen waypoints dentro del radio (`RouteTouchesZone`). Una zona sin waypoints
     sólo tiene ruido, sentidos y ancla.
   - **`Contains` ignora la altura.** Una zona en `PISO_01` también agarra lo que está arriba, en
     `PISO_02`, dentro del radio. Donde los pisos se superponen, radios más chicos o centros corridos.
   - **Ningún centro dentro del Hub ni pegado a su puerta.** El ancla tira la patrulla hacia el
     centro, y con el Hub `Not Walkable` eso deja al Nemesis rondando la entrada del Hub: el cheese
     C5 fabricado por el propio Director. Desde el 22/09 no es sólo una regla: a menos de 6 m el
     Director rechaza la zona (ver C5 en el §4).
   - **Para la Fase 5, que cubran lo jugable.** La retirada del Relax elige la zona más lejana al
     jugador, y la sensibilidad creciente presiona la zona donde está el jugador. Con un par de zonas
     sueltas no hay adónde retirarse ni qué presionar. Mínimo: una por ala y una por piso, ninguna en
     el Hub.
3. **Los disparadores** (`Puzzle Triggers` en el Director), uno por golpe de ritmo:

   | Campo | Qué poner |
   |---|---|
   | `puzzleId` | Dropdown (`[PuzzleId]`) con los ids que existen hoy: `sp1_panel_electrico`, `sp2_contenedores`, `sp3_valvulas`, `puzzle_central_piso1`. |
   | `zoneId` | La zona a presionar, del dropdown de zonas de la escena. Vacío = sólo la entrada, sin presión. |
   | `intensity` | 0..1; escala las cuatro palancas juntas. Empezar bajo (0.5) en el primer puzzle y subir con el progreso. |
   | `duration` | 45–60 s. Un pedido nuevo **reemplaza** al anterior; no se suman. |
   | `stageEntrance` | La entrada tipo Mr. X. No en el primer golpe, y **nunca en el puzzle que despierta al Nemesis**: `StageEntranceAsync` sale sin hacer nada si el Nemesis todavía no está activo (`NemesisDirector.cs:538`), y despertarlo puede tardar (reintenta el spawn dos veces por segundo hasta que un punto sirve). |

   Con zona, los candidatos de la entrada se muestrean **dentro del radio de la zona**, fuera de la
   vista del jugador y a 10–22 m por NavMesh. La zona tiene que tener NavMesh a esa distancia de
   donde el jugador va a estar parado al terminar el puzzle; si no, la entrada no encuentra lugar.
4. **Verificar en Play**, desde `Bootstrap` para que esté cargada la escena `Data`:
   - F10, panel *DIRECTOR*: un botón por zona (presión 1.0), *Release* y *Staged entrance*. Si dice
     *"No pressure zones in the scene"*, las zonas no se registraron: id vacío o GameObject apagado.
   - En la consola: `[NemesisDirector] Pressure on '<zona>' at 1.00 for <n>s.`
   - El gizmo de la zona en ámbar o rojo, y la patrulla inclinándose hacia ahí en uno o dos ciclos
     de ruta (`RouteReplanInterval`, 12 s). No es inmediato: es un sesgo, no una orden.
   - Resolver el puzzle del disparador y ver lo mismo sin tocar F10.

**Errores comunes**

| Síntoma | Causa |
|---|---|
| *"there is no Director in the scene"* | El GameObject está apagado (como hoy en Zona1) o se está jugando otra escena. |
| *"No pressure zone called '…'"* | El `zoneId` del pedido no coincide con ninguna zona activa. |
| *"'…' has no id"* | Una zona con `Zone Id` vacío; se ignora. |
| *"Noise Layer … not in the Nemesis's Listen Mask"* | Alguien cambió `noiseLayer` o el `listenMask` del prefab. Los dos tienen que ser `DetectableAudio`. |
| Hay presión pero la patrulla no cambia | Ninguna ruta desbloqueada tiene waypoints dentro del radio, o las que la tocan pesan 0. |
| Se completa el puzzle y no pasa nada | `zoneId` mal escrito, o un `puzzleId` cargado con *(escribir a mano…)* que no existe (el dropdown lo marca con ⚠). |

### 14.3 Lo nuevo del Director (§6): cómo se armó

Construido el 22/09 así, salvo el trigger del Hub, que no hizo falta (lo reemplaza un
`SafeZoneMarker` sobre el volumen del Hub, ver la Fase 5 en el §9).

| Pieza | Dónde va | Qué se configura |
|---|---|---|
| `NemesisTension` | En el mismo GameObject que `NemesisDirector` | Nada en escena: escucha `NemesisEvents` y `HidingEvents`, y encuentra al jugador y al Nemesis igual que el Director. |
| `SO_DirectorPacing` (asset nuevo, en `ScriptableObjects/Nemesis/`) | Referenciado desde el Director | Peso de cada entrada del medidor, velocidad de decaimiento, umbrales de pico y de fade, `SustainPeak` 3–5 s, `Relax` 30–45 s, `quietTimeout` 90 s, intensidad de la retirada. Va en un SO y no en el componente para poder cambiar el ritmo por nivel o por dificultad (D8) sin tocar la escena. |
| Zonas de presión | Las mismas del 14.2 | Que cubran lo jugable (14.2, paso 2). La retirada reusa `RequestPressure` sobre la zona más lejana por NavMesh. |
| "El jugador ve al Nemesis" | Código | Raycast desde la cabeza del jugador al pecho del Nemesis contra el mismo `obstacleMask` (6153). **No usa la cámara.** |
| Trigger informativo del Hub (C5, `SafeZoneEscape`) | Un GameObject **aparte**, hijo de `Safe Area`, con un `BoxCollider` trigger que cubra el Hub | Capa **`Ignore Raycast`**, no `Props`. `Props` está en las máscaras de obstáculo y `Queries Hit Triggers` está prendido en `DynamicsManager`. Desde el 21/09 (WIR-020) los raycasts de visión, oído y agarre pasan `QueryTriggerInteraction.Ignore`, así que un trigger ya no tapa la visión; igual va en `Ignore Raycast` para no depender de que todo código nuevo se acuerde. Sólo informa presencia; no bloquea nada. El patrón de código ya existe: `ZoneTrigger` / `ArchitectZoneTrigger` (trigger + tag `Player`). Revisar en qué capa quedaron los de Zona1 antes de copiarlos. |
| F9 | `NemesisDebugHUD` | La fila de ritmo del §6.4: estado, tensión, tiempo restante, zona activa. |

### 14.4 Dónde va cada pieza nueva del plan

| Pieza | Fase | Dónde | Qué configurar | Qué tiene que avisar el validador |
|---|---|---|---|---|
| `HidingSpot` | 1 | Raíz de `Locker.prefab` / `Locker2.prefab` (y de los prefabs de mesa y container) | Tipo; `SpotId`; hijo `ApproachPoint`; hijo `InteriorPose` con la cámara Cinemachine interior y los límites del spec; colliders propios (se llenan solos en `OnValidate`). El `BoxCollider` sólido **se queda en `Default`**. Hoy se usan los prefabs de `Prefabs/HidingSpotFather/` (variantes Locker / UnderTable / Container de un padre con un `NavMeshModifier` *Not Walkable* en `Model`, capa `Props`). **Ojo:** en una escena que no hornea `Default` (la testbed: `Ground|Wall|Props`) el collider sólido del container no hace hueco y queda NavMesh adentro; ahí hay que pasarlo a `Props` o sumar un `NavMeshModifierVolume` en una capa horneada. `ExitPose` y `ApproachPoint` a ≤1 m del interior: la `ExitPose` es también donde aparece el jugador cuando lo sacan. | `SpotId` vacío o repetido; `ApproachPoint` fuera del NavMesh o a más de `catchMaxReach` (1 m) de la pose interior. *(Pendiente: que también mire la `ExitPose`.)* |
| `SO_HidingData` | 1 | `ScriptableObjects/Hiding/` | Respiración, radios, multiplicadores por tipo, `lockerVisionExposure`. | — |
| `NemesisHidingAwareness`, `NemesisChaseProgress` | 2 / 4 | Raíz de `Nemesis.prefab`, junto a `NemesisStateManager` | Nada: se enganchan solos, como `NemesisPathOracle`. Sus números van al final de `SO_NemesisData`. | — |
| Peldaños nuevos | 2 / 4 | `SO_NemesisPriorities.asset` **y** `BuildDefaultLadder()` | En la posición que dice el §3.5. | Que el asset y el default no coincidan. |
| `PlayerHabitTracker` | 3 | Escena `Data`, junto a `PuzzleStateManager`, `ModuleManager` e `InventoryManager` (los otros `ISessionResettable`) | `Singleton` persistente que se registra en `GameSession`: así sobrevive a la captura y al checkpoint y se resetea con New Game (D3). Referencia a `SO_CounterplayRules`. | — |
| `SO_CounterplayRules` | 3 | `ScriptableObjects/Nemesis/` | Las filas del §5.2. | Un umbral en 0, o un `chanceAtUnlock` fuera de 0..1. |
| `NemesisAmbushPoint` | 6 | En el nivel: GameObjects vacíos cerca de las salidas probables (del Hub, de las habitaciones con escondites), mirando hacia la salida | Posición y orientación. | Fuera del NavMesh, dentro del Hub, o con línea de visión directa desde la salida que vigila (tiene que esperar fuera de la vista). |
| `NemesisDropLink` + `NavMeshLink` | 8 | En el nivel: un GameObject estático por bajada, bajo un contenedor `Drop Links` (§15.6) | Hijos `TopEdge` y `BottomLanding`; tipo (auto por alto); costo; enfriamiento. | Ver la lista del §15.6. |

### 14.5 Mejoras chicas de editor para hacer en el camino

- ✅ (22/09) Un atributo `[PressureZoneId]` con drawer, igual que `[PuzzleId]`, para
  `PuzzleTrigger.zoneId`, que lista las zonas de la escena abierta.
- ✅ (22/09) *Validate Navigation Setup* reporta: el Director apagado habiendo zonas o disparadores;
  zonas sin id, repetidas, que no tocan ningún waypoint o con el centro a menos de 6 m del Hub;
  disparadores sin puzzle, sin efecto, con un `zoneId` que no existe o con entrada en el puzzle que
  despierta al Nemesis; y, como nota, la cobertura (`N/M waypoints`) y si falta `SO_DirectorPacing`.

---

## 15. Bajadas entre pisos

> Agregado el 19/09/2026. Nada de esto está construido. Relevado contra `6703f9d` leyendo código,
> escena y `ProjectSettings` como texto; lo marcado *verificar en el editor* no se pudo abrir.

### 15.1 Qué es y para qué

El Nemesis puede **bajar** de `PISO_02` a `PISO_01` por puntos que diseño elige: un hueco en el
piso, una baranda rota o el borde de una pasarela. Salta si es bajo y se descuelga si es alto. Es
**de un solo sentido**: para subir sigue usando el montacargas o las escaleras.

Para qué sirve, en términos de este plan:

- **Principio 11 (el NavMesh expresa personalidad).** Hoy el Nemesis se mueve entre pisos como el
  jugador, o peor (espera la cabina). Una bajada es algo que el jugador no puede hacer, y eso lo
  hace sentir otra cosa.
- **Cierra C7 en un sentido.** Bajar en el montacargas deja de cortar la persecución. El jugador
  gana distancia, pero no se lleva la cabina como escudo.
- **Da puntos de emboscada verticales** para `ZoneDefense` (Fase 6) sin inventar movimiento nuevo.

Y lo que **no** es:

- No son ductos (§2.3). El Nemesis está siempre en el NavMesh, se lo ve bajar y se lo oye caer.
- No es un atajo invisible. Toda bajada tiene anticipación (se ve y se oye antes de caer) y
  recuperación al aterrizar (la ventana del jugador).
- No es una forma de subir. Trepar necesita otras animaciones y otra lógica de visibilidad, y queda
  fuera.

### 15.2 Lo que ya hay y lo que se rompe si se hace ingenuamente

| Qué | Estado hoy | Consecuencia para las bajadas |
|---|---|---|
| Quién cruza los links | `NemesisElevatorUser` apaga `autoTraverseOffMeshLink` y cruza **todos** los links: los de montacargas con la secuencia completa, el resto con `TraverseSimpleLinkAsync` (interpolación lineal a `linkTraversalSpeed` 2.5 m/s, sin animación). | La bajada es una **rama nueva ahí**, no un componente que también mire `isOnOffMeshLink` (§10). Hoy una bajada ya "funcionaría": el Nemesis se deslizaría en diagonal por el aire caminando. |
| Links generados | La NavMeshSurface de Zona1 tiene **`m_GenerateLinks: 1`**, con `ledgeDropHeight` 1.5 m y `maxJumpAcrossDistance` 2 m (`ProjectSettings/NavMeshAreas.asset`). | Ya existen links que nadie autoró: caídas de hasta 1.5 m y saltos de hasta 2 m en cualquier borde que cumpla. Se cruzan con la interpolación lineal. *Verificar en el editor cuántos hay* (Navigation → *Show NavMesh* con *Show Links*). D10: apagarlo. |
| Áreas | 0 Walkable, 1 Not Walkable, 2 Jump, 3 `NemesisAvoid` (sin uso), 4 `Forklift`; **5–7 libres**. | Las bajadas van en un área propia (5, `NemesisDrop`) para poder cambiarles el costo según lo que esté haciendo (D11). |
| ¿La bajada cuenta como "otro piso"? | `NemesisNav.NavRoute.CrossesLink` es `CrossedElevator != null`: **sólo ve el montacargas**. Es lo único que consume `NemesisPathOracle.IsAcrossFloors`. | Sin cambio, una ruta por bajada no activa `RouteToBeliefCrossesFloors`: el Nemesis queda en `Chasing`, el piso le corta la vista en el borde y a los 2.5 s de gracia pasa a `Searching`… del piso de arriba. Es el mismo bug por el que existe `Traversing`. |
| Captura durante un link | `HandlePlayerCaptured` cancela el cruce salvo `isRiding` (dentro de la cabina). Un link simple **se cancela**. | Cancelar una bajada a mitad de camino deja al Nemesis colgado en el aire. En el aire, la bajada tiene que ser tan intocable como `isRiding`. |
| Protecciones que ya sirven | `IsTraversing` alimenta `IsUsingElevator` (predicado), que ya frena la entrada Mr. X (`NemesisDirector.cs:538`) y la escalera. `PushStuckSuppression` apaga el watchdog de `NemesisStuckEscape`. | Se reusan sin cambios. El nombre `IsUsingElevator` queda corto, pero **no se renombra**: el predicado se guarda como entero en el asset y el nombre sólo cambiaría el código. |
| Animator | `NemesisController.controller` tiene tres estados: **Idle, Patrol, Chase**. `isCatching` existe como parámetro pero **ninguna transición lo usa**: hoy no hay animación de captura. Root motion apagado en `Nemesis.prefab`. | Todo lo del §15.5 es nuevo. Con root motion apagado, las animaciones van **in place** y el código mueve el cuerpo. |
| Grafo de rutas | `NemesisRouteGraph` agrupa waypoints en islas probando caminos desde cada nodo al representante de la isla. | Un link de un solo sentido hace que "A llega a B" deje de implicar "B llega a A". Mientras cada bajada tenga vuelta (escalera o montacargas), las islas no cambian. **Una bajada sin vuelta rompe el grafo**: el validador lo tiene que prohibir (§15.6). |

### 15.3 Diseño

**Dos tipos, elegidos por altura** (no por un campo que alguien pueda poner mal):

| Tipo | Alto | Cómo se ve |
|---|---|---|
| `Hop` (salto corto) | 1.5 – 2.5 m | Se para en el borde, flexiona y salta hacia adelante. |
| `Hang` (descolgarse) | 2.5 – 5 m | Se da vuelta de espaldas al hueco, apoya las manos, se cuelga, se suelta y cae agachado. |

El corte en 2.5 m es `FloorHeightThreshold`: el mismo número que ya separa "otro piso" de "un
desnivel". Por encima de 5 m no se autora una bajada; *verificar en el editor* el alto real entre
`PISO_01` y `PISO_02`. Si pasa de 5 m, sólo se puede bajar a una pasarela o entrepiso intermedio.

**Fases de una bajada** (dentro de `TraverseDropAsync`, igual que el montacargas tiene las suyas):

| # | Fase | Qué pasa | ¿Se cancela con una captura? |
|---|---|---|---|
| 1 | Llegar al borde | El agente camina hasta `TopEdge` con su gait normal. Es lo único que hace el NavMesh. | Sí (todavía está en el piso). |
| 2 | Alinearse | Gira hasta mirar la dirección del link (≤ 0.3 s, `turnSpeed` del montacargas). | Sí. |
| 3 | Anticipar | Clip de anticipación. Suena un gruñido y el golpe de manos en la baranda (clips `sfx_nemesis_voice_*`, hoy sin uso). **Es el tell:** desde abajo, el jugador lo ve asomarse. | Sí: en esta fase todavía no se tiró. |
| 4 | En el aire | Arco parabólico de `TopEdge` a `BottomLanding` (horizontal lineal y vertical con gravedad). Clip de caída en loop, o el tramo de salto de `Hop`. Flag `isDropping`. | **No.** Igual que `isRiding`. |
| 5 | Aterrizar | Clip de impacto. Golpe fuerte, que es un evento de animación que dispara el sonido de aterrizaje. `CompleteOffMeshLink()`. | **No.** |
| 6 | Recuperarse | Termina el clip de impacto (0.6–0.9 s) con el agente quieto. **No puede capturar** (`CanReachPlayerNow` da `false` mientras `isDropping` o recuperando). Es la ventana del jugador. | — |
| 7 | Salir | Se suelta el cuerpo; el estado activo pone su gait al volver, igual que después del montacargas. | — |

**En el FSM no hay estado nuevo**, por la misma razón que en el §3.5. `Traversing` ya hace
exactamente esto: mantener la decisión de ir a otro piso aunque el piso le corte la vista. Sólo hay
que avisarle que una bajada también es cruzar pisos (`CrossedDrop` en `NavRoute`, §15.4). La
bajada es corta, así que `ElevatorCommitTime` (12 s) le sobra y no hace falta un umbral propio.

**Costo según lo que esté haciendo (D11).** El link va en el área `NemesisDrop` y
`NemesisStateManager` ajusta `agent.SetAreaCost(NemesisDrop, …)` al cambiar de estado:

- barato (≈ 2) en `Chasing`, `Traversing`, `Searching` e `Investigating`;
- caro (≈ 20) en `Patrolling`.

Es el mismo lugar donde ya se escribe la velocidad del agente: un estado configura el cuerpo, no
decide nada. En patrulla sigue pudiendo usar la bajada si es la única ruta a un waypoint.

**Reglas de juego limpio:**

- **Nunca agarra en el aire ni al aterrizar.** Si el jugador está justo abajo, cae igual, se
  recupera y recién ahí puede entrar a `Catch`. Aterrizar encima del jugador y agarrarlo en el mismo
  frame es el tipo de muerte que se siente como un bug.
- **Enfriamiento por link** (8 s): no vuelve a tirarse por la misma bajada hasta que pase. Evita
  "sube por la escalera, se tira, sube, se tira" si el jugador hace un loop entre pisos. Se
  implementa igual que el enfriamiento del montacargas: suspender el link para este agente.
- **Nada depende de la cámara.** El tell es el mundo (anticipación y sonido), no un corte de cámara.
- **El Hub no se toca.** Ninguna bajada aterriza dentro del Hub ni a menos de 3 m de su puerta
  (con el Hub `Not Walkable` no podría, pero un aterrizaje pegado a la puerta fabrica C5).

**Respawn o dormido a mitad de la bajada:** `CheckpointManager.OnRespawned` cancela como hoy, y el
`finally` deja al Nemesis sobre el NavMesh con `WarpTo(BottomLanding)` (con la búsqueda de tierra
de `AgentRecoveryRadius` si hace falta). Pasa detrás del fade de captura, así que no se ve.

### 15.4 Código

| Pieza | Dónde | Qué hace |
|---|---|---|
| `NemesisDropLink` (nuevo) | `Scripts/Nemesis/`, `[RequireComponent(typeof(NavMeshLink))]` | Espejo de `NemesisElevatorLink`: en `Awake` configura su `NavMeshLink` desde `TopEdge` y `BottomLanding` con `bidirectional = false`, área `NemesisDrop`, ancho y costo. Calcula `Height` y `Kind` (`Hop` / `Hang`) y expone `FacingDirection`. Mantiene una lista estática `Active`, como el montacargas, para `NemesisNav`. `EDropKind` sólo se agrega al final. |
| Rama en `NemesisElevatorUser.Update` | Donde hoy se elige entre montacargas y link simple | Si `data.owner` tiene `NemesisDropLink` → `TraverseDropAsync(drop, token)`. Si no, sigue igual. Con esta tercera rama el nombre del componente queda chico; renombrarlo a `NemesisLinkTraverser` es opcional y aparte (el GUID no cambia si se renombra con el `.meta`). |
| `TraverseDropAsync` | `NemesisElevatorUser` | Las fases del §15.3. `isDropping` se agrega a la condición de `HandlePlayerCaptured` junto a `isRiding`. Push/Pop de la supresión del watchdog, como el link simple. Arco con un `MoveTransformAlongArcAsync` hermano de `MoveTransformToAsync`. |
| Animación | `NemesisStateManager` | Método `PlayTraversal(EDropPhase)` que hace `CrossFade` al estado por nombre, **con `HasState` y fallback** (si falta el estado, la bajada se hace igual, sin animación): el patrón de `PlayerStateManager.TryGetStandUpState`. No se agregan valores a `EGait`: la bajada no es un gait. Al salir, `ApplyGaitToAnimator` retoma el control. |
| `NavRoute.CrossedDrop` | `NemesisNav` | Campo nuevo al final del struct. `FindCrossedDrop(path)` usa la misma técnica que `FindCrossedElevator` (las esquinas del camino tocan `TopEdge` **y** `BottomLanding`). `CrossesLink => CrossedElevator != null \|\| CrossedDrop != null`. Con eso `IsAcrossFloors` y `RouteToBeliefCrossesFloors` cubren las bajadas sin tocar la escalera. Actualizar el doc-comment del predicado ("freight elevator" → "elevator or drop"). |
| `CanReachPlayerNow` | `NemesisStateManager` | Primer `return false` si `elevatorUser.IsDroppingOrRecovering`. |
| Costo por estado | `NemesisStateManager`, en el cambio de estado | `SetAreaCost` del §15.3. Los dos costos van al final de `SO_NemesisMovement`. |
| Tuning | Al final de `SO_NemesisMovement` | Gravedad del arco, duración mínima en el aire, velocidad de giro al alinear, nombres de los estados del Animator (con defaults). Al final de `SO_NemesisData`: enfriamiento por link. **Editor, gizmos y F9** (§10). |
| `NemesisGizmos` | — | El arco de cada bajada, coloreado por tipo; el radio libre de aterrizaje; una bajada en enfriamiento, en gris. |
| F9 | `NemesisDebugHUD` | Una línea mientras dura: `DROP Hang 3.8 m · fase Anticipar · 0.4 s`. |
| Validador | `NemesisSetupValidator` | La lista del §15.6. |

### 15.5 Animaciones

Todas **in place** (root motion apagado en el prefab; al bajar de Mixamo, tildar *In Place*
cuando esté la opción), en el rig del Nemesis y con los bones renombrados si el FBX viene con otro
prefijo. Eso último es el problema que ya resolvió `PlayerStandUpSetup` (bones `mixamorig8:*` contra
`mixamorig:*`), y conviene un `NemesisTraversalAnimSetup` hermano: una herramienta de editor
idempotente que arregla las rutas de los clips y agrega los estados al controller.

**Para las bajadas:**

| # | Estado del Animator | Tipo | Duración aprox. | Loop | Qué tiene que mostrar | Eventos de animación | Buscar en Mixamo (referencia) |
|---|---|---|---|---|---|---|---|
| A1 | `Drop Look` | los dos | 0.5–0.8 s | no | Se detiene en el borde, inclina el torso y mira hacia abajo. **Es el tell**: tiene que leerse desde el piso de abajo. | gruñido | *Looking Down*, *Standing Look Around* |
| A2 | `Hop Takeoff` | `Hop` | 0.3–0.5 s | no | Flexiona y se impulsa hacia adelante y abajo. | — | *Jump Down*, *Jumping Down* |
| A3 | `Hang Turn` | `Hang` | 0.6–0.9 s | no | Se da vuelta de espaldas al hueco, se agacha y apoya las manos en el borde. | golpe de manos | *Climbing Down Wall*, *Crouch To Hang* |
| A4 | `Hang Release` | `Hang` | 0.3–0.5 s | no | Colgado de las manos, se suelta. | — | *Hanging Idle* → *Drop From Ledge* |
| A5 | `Fall Loop` | los dos | — | **sí** | En el aire, brazos abiertos y piernas preparadas. Cubre cualquier alto: el código decide cuánto dura. | — | *Falling Idle* |
| A6 | `Land Heavy` | los dos | 0.6–0.9 s | no | Cae agachado, apoya una mano y se levanta. **Su duración es la ventana del jugador** (§12). | impacto al contacto (sonido + pasos), fin de recuperación | *Hard Landing*, *Falling To Landing* |
| A7 | `Land Roll` *(opcional)* | `Hop` en `Chasing` | 0.5–0.7 s | no | Rueda o amortigua y sale corriendo. Variante más rápida para que un salto corto no frene tanto una persecución. | impacto | *Falling To Roll* |

Transiciones:

- Se entra **por código** (`CrossFade`, 0.1–0.15 s) a A1, después A2 o A3→A4, después A5, después
  A6/A7. Sin parámetros nuevos en el controller: el orden lo pone `TraverseDropAsync`, no el grafo.
- A6/A7 salen por *exit time* a `Idle`, y desde ahí `isWalking` / `isRunning` llevan a la
  locomoción, como ya pasa.
- A5 no tiene *exit time*: lo corta el código cuando el arco toca el piso.
- Los pasos y el impacto van por eventos de animación, porque así ya funcionan los pasos del
  Nemesis (`FootstepEmitter` con `AnimationEvent`, ver `docs/CLAUDE.md` › *Footsteps and
  breathing*).

**Sonido** (la animación sola no alcanza, porque desde abajo el jugador lo oye antes de verlo):
gruñido en A1 (`sfx_nemesis_voice_*`, hoy sin uso), golpe de manos en A3, impacto fuerte en A6 con
más volumen que un paso de persecución. Todo en 3D por el bus del Nemesis, como `NemesisAudio`.
Nada de esto pasa por `DetectableAudio`: esa capa es lo que el Nemesis oye, no lo que hace.

**Otras animaciones que el plan ya pide y que tampoco existen** (para tener el inventario en un
solo lugar; el Animator de hoy no tiene más que Idle / Patrol / Chase):

| Estado | Fase del plan | Para qué |
|---|---|---|
| `Catch` (agarre) | ya debería existir | `isCatching` está en el controller sin transición: hoy la captura no se anima. |
| `Search Look` | 2 | La pausa de `SearchPauseTime` al revisar un punto; hoy la pausa es `Idle` más `NemesisLookAround`. |
| `Check Locker` (abrir la puerta) | 2 | Nivel A y Nivel C en un locker. |
| `Check Under Table` (agacharse a mirar) | 2 | Nivel C en una mesa. |
| `Pull Out` (sacar al jugador del escondite) | 2 | La fase nueva de `Catch` (§3.5). Va en par con una del jugador. |
| `Break Locker` (arrancar la puerta) | 6 | Escondite quemado (§3.6, D2). Necesita además el modelo del locker roto. |
| `Ambush Idle` (quieto, respirando, mirando una salida) | 6 | `ExitAmbush` y `ZoneDefense`: esperar sin parecer trabado. |

Del lado del jugador: entrar y salir de cada tipo de escondite (0.5–0.8 s, §3.1) y ser sacado del
locker (en par con `Pull Out`).

### 15.6 Cómo se arma en Unity

1. **Apagar los links automáticos (D10).** En la NavMeshSurface de Zona1, *Generate Links* en off,
   y rebakear. Antes de apagarlos, mirar con *Show Links* dónde había links: cualquier lugar donde el
   Nemesis dependía de un salto automático para no quedar trabado pasa a ser una bajada autorada, o
   se arregla la geometría. Repetir en `NemesisTestbed`.
2. **El área.** En *Navigation → Areas*, índice 5 → `NemesisDrop`, con costo 1: el costo real lo
   pone el código por estado.
3. **Una bajada.** Bajo un contenedor `---- NAV ---- / Drop Links` (estático):
   ```
   Drop_<lugar>           ← NemesisDropLink + NavMeshLink. Estático. Escala 1.
   |-- TopEdge            ← sobre el NavMesh de arriba, a 0.3–0.5 m del borde, con el eje Z
   |                         apuntando al vacío (es la dirección en que mira al anticipar)
   \-- BottomLanding      ← sobre el NavMesh de abajo, a 0.8–1.5 m de la vertical del borde
                             (el arco necesita avance horizontal; justo abajo se ve como un
                             ascensor)
   ```
   Los extremos del `NavMeshLink` se pisan en `Awake` desde los dos hijos, como en el montacargas;
   lo que se cargue a mano en el inspector se ignora.
4. **Que el jugador no la use** (D9), salvo que sea compartida a propósito: baranda o collider de
   jugador en el borde. En la capa del jugador, no en `Props`, para no tapar la visión del Nemesis
   hacia abajo.
5. **Testbed.** Agregar un entrepiso con una bajada `Hop` y otra `Hang`, más una escalera de vuelta,
   en `Scenes/Dev/NemesisTestbed.unity`, para probar los casos 12–16 sin cargar el nivel.

**Qué tiene que avisar *Validate Navigation Setup*:**

| Problema | Por qué importa |
|---|---|
| `TopEdge` o `BottomLanding` fuera del NavMesh (`SamplePosition` a 0.3 m) | El link no se registra y la bajada no existe. |
| Alto fuera de 1.5–5 m | Debajo lo cubre el escalón; encima es irreal. |
| `BottomLanding` sin camino de vuelta a `TopEdge` (escalera o montacargas) | Rompe las islas de `NemesisRouteGraph` (§15.2) y puede dejar al Nemesis atrapado abajo. |
| El arco choca con geometría (`CapsuleCast` por tramos contra la máscara de oclusión) | Atravesaría una viga en el aire. |
| Radio libre de 1 m alrededor de `BottomLanding` con colliders sólidos | Aterriza adentro de una caja. |
| `BottomLanding` dentro de un volumen `Not Walkable` o a menos de 3 m de la puerta del Hub | C5 fabricado por el nivel. |
| `bidirectional` prendido en el `NavMeshLink` | El agente intentaría "subir" por una bajada. |
| *Generate Links* prendido en alguna NavMeshSurface | Vuelven los links sin autorar (D10). |
| Estados del §15.5 que faltan en el controller | La bajada anda igual pero sin animación; avisar, no fallar. |

### 15.7 Riesgos

- **Links automáticos de los que el nivel depende sin saberlo.** Si apagar *Generate Links* deja un
  rincón sin salida para el Nemesis, `NemesisStuckEscape` lo va a sacar con warps y eso se ve. Por
  eso el paso 1 del §15.6 empieza mirando dónde estaban.
- **Que el jugador no entienda por qué lo alcanzó.** Una bajada que el jugador nunca vio usar
  parece trampa. Aplica la regla R3: la primera bajada de la partida debería pasar con el jugador en
  rango de verla u oírla. Puede ser un disparador del Director (`stageEntrance` con una variante de
  bajada, D12) o, más simple, ubicar la primera bajada en un lugar de paso obligado.
- **Abuso desde abajo.** El jugador se queda debajo de la bajada para que el Nemesis caiga y
  esquivarlo durante la recuperación. Es legítimo, porque es leer al enemigo. Si se vuelve dominante,
  se acorta la recuperación, nunca se quita.

---

## 16. Revisión del consejo (21/09/2026)

La Fase 2 recién construida se revisó con un consejo local armado sobre la idea de
[the-llm-council](https://github.com/sherifkozman/the-llm-council): borradores independientes en
paralelo → crítica cruzada adversarial → síntesis. En vez de proveedores externos, cuatro subagentes
de modelos distintos, cada uno con un enfoque, todos en sólo lectura sobre el código sin commitear.

| Consejero | Modelo | Enfoque | Veredicto final | Confianza |
|---|---|---|---|---|
| 1 | Opus | Arquitectura y corrección | Aprobar con cambios | 0.75 |
| 2 | Sonnet | Adversario: cheeses y trabas | Aprobar con cambios (bloqueante hasta verificar el fix de C1 #1) | 0.6 |
| 3 | Fable | Diseño y experiencia del jugador | Aprobar con cambios (bloqueante C1 #1) | 0.85 |
| 4 | Haiku | Unity, rendimiento y tests | Rehacer — sus hallazgos principales resultaron falsos en la ronda 2 y retiró dos | 0.25 |

En la ronda 2 los tres primeros rankearon igual: el Consejero 1 como el más útil (encontró el bug
bloqueante con la geometría real) y el 4 como el menos (tres premisas falsas).

### 16.1 Qué se encontró y qué se hizo

| Hallazgo | Acuerdo | Qué se hizo |
|---|---|---|
| **El Nemesis se clavaba frente al locker** (C1 #1): la proximidad lo pasaba a `Chasing`, que frenaba a ~1.3 m de un interior fuera del NavMesh, fuera del agarre de 1 m, para siempre. | 4/4, bloqueante | Detectado escondido = escondite conocido (D13); agarre medido en el `ApproachPoint` de un escondite conocido; frenado de 0.25 m al revisar. §3.3. |
| Sospechaba con el medidor **bajando** de una persecución perdida (C3 #3). | 3/3 | Sospecha sólo con contacto periférico vivo. §3.4. |
| "Te vi entrar" sin haber visto la puerta (C1 #2). | 2/2 (con distinto arreglo) | Línea de vista a la puerta; la ventana se queda en 0.75 s. §3.4. |
| Con el medidor subiendo por las rendijas, `Investigating` se iba al último ruido (C1 #3). | 2/3 | Al pasar el umbral el escondite queda sospechoso y va a mirarlo (D17 deja abierta la alternativa). |
| El Nivel B del locker no existía: 1.75 m desde el ojo cae dentro del disco de 1.5 m (C3 #2). | 3/3 | `lockerVisionExposure` 0.25 → 0.5. |
| El caso 4 del §13 pedía sospecha a 5 m bajo la mesa, con alcance de 3.5 m (C3 #6). | 2/3 | Caso reescrito a 3 m; la mesa se queda en 0.5. |
| Capturado adentro, el cuerpo volvía a ser dinámico **dentro del collider del mueble** (C1, ronda 2). | Nadie lo había visto antes | Al capturar desde adentro, el jugador aparece en la `ExitPose`. |
| El "olvido por expiración" podía dejar al Nemesis sin volver a saber aunque siguiera viéndote (C1 #4). | — | Cada confirmación reinicia el reloj; no se da por revisado un escondite que está viendo por las rendijas. |
| Un escondite destruido dejaba el frenado en 0.25 (C1 #5). | — | Guardas con `ReferenceEquals`. |
| El tooltip decía que el pull-out era "la última ventana para salir corriendo" (C3 #4). | 3/3 | Tooltip corregido; D16. |
| `ChaseFloor` del escape sube "sabe" a `Chasing` (C1, ronda 2). | — | Cubierto por el agarre en la puerta; documentado. |

### 16.2 Lo que quedó abierto

- **Aviso audible al saber el escondite** y SFX/animación del pull-out (C3). Sin eso el jugador no
  distingue desde adentro "sabe" de "adivina", y el margen de D1 es teórico. Fase 2, pendiente.
- **D14:** detección a través de la carcasa sólo desde la puerta (C3) — playtest.
- **D15:** container a paridad de ruido (C2) — datos de la Fase 3.
- **D17:** clavar la mirada en vez de ir a mirar (C3) — playtest.
- Container en escenas que no hornean `Default` (C3, ronda 2) — §14.4.
- `NemesisFreeRoam.AddSampledPoints` podría rechazar puntos con `|Δy| > 1` para no mezclar pisos en
  el barrido (C2 #3 / C3). Hoy lo mitiga la prioridad por habitación.
- Tests automáticos de Play mode (C4): el proyecto no tiene ninguno. El checklist
  (`docs/Checklist-NemesisTestbed.md`) es el sustituto manual por ahora.

### 16.3 Lo que se descartó, y por qué

- **Respiración a 1.6** (C3, apoyado por C2): el oído mide por camino hasta el emisor proyectado al
  NavMesh, que descuenta ~0.7 m frente al locker; ya se oye a ~2.3 m, así que la franja donde
  aguantar `F` decide existe. Con 1.6 pasaría a ~3.9 m (C1).
- **Mesa a 0.75** (C3): 5.25 m sin restricción de lado la vuelve casi tan visible como estar parado
  afuera (C2); se mantiene el valor del spec.
- **Pull-out a 1.2 s** (C3): se decide cuando exista la animación.
- **"Rehacer por falta de tests"** (C4): desproporcionado en un proyecto sin ningún test (C1); sus
  hallazgos de rendimiento eran falsos: la vista por las rendijas corre en el barrido de 0.1 s, no en
  cada frame, y el evento `HiddenPlayerSpotted` estaba latcheado (ahora se dispara cada frame a
  propósito, para que la confirmación renueve la memoria; es una invocación de delegado, no un
  raycast).

### 16.4 Playtest del 21/09 — ajustes

- **Siempre te agarraba escondido.** Tres cosas empujaban al Nemesis hacia el locker: al perderte, el
  `Chasing` corría al punto *predicho* (adelantado según tu velocidad, que es justo hacia el locker
  al que ibas); ahí miraba alrededor y con `lockerVisionExposure` 0.5 (3.5 m) llenaba el medidor en
  ~1 s; y a 1.5 m la proximidad lo delataba. Se bajó la exposición a **0.35** y se cambió la
  persecución (abajo).
- **Al perderte ya no volvía a donde te vio.** Seguía persiguiendo 2.5 s (`visionLossGracePeriod`)
  hacia el punto predicho o un waypoint de flanqueo, y después `Searching` barría la sala o cortaba
  el paso. Ahora, sin visión, `NemesisPursuit` va **derecho al último punto conocido** (si el camino
  es completo) y la regla "va a donde lo vio por última vez" mantiene `Chasing` **hasta llegar**
  (tope de seguridad: 10 s de antigüedad de la creencia). Recién ahí arranca la búsqueda.
  `visionLossGracePeriod` ya no lo usa la escalera por defecto.
- **Detrás de una mesa no la rodeaba.** El punto adelantado caía dentro del hueco de la mesa en el
  NavMesh y `SamplePosition` (2 m) lo tiraba al lado del Nemesis: llegaba ahí y te espejaba. Ahora
  el punto adelantado se camina por el NavMesh desde donde estás (`NavMesh.Raycast`) y se corta en
  el primer borde, siempre de tu lado; y a menos de 3 m no se adelanta.

- **Segundo playtest (21/09): la búsqueda no arrancaba en el último punto y el locker agarraba
  igual.** Causa común: la creencia es "lo más reciente entre vista y oído", así que si el jugador
  corta la línea de vista y sigue corriendo, los pasos llevan la creencia hasta el locker. La
  persecución lo seguía hasta ahí (proximidad → agarre), y el barrido de sala nunca se armaba porque
  un ruido no compromete barrido. Ahora `NemesisPursuit` vuelve al **último punto visto**
  (`TryGetRecentSighting`, 10 s), `Searching` al entrar se queda un `SearchPauseTime` mirando ahí y
  arma el barrido anclado en ese punto visto (si es más nuevo que `SightCommitTime`). Los ruidos que
  se siguen oyendo después retargetean la búsqueda como antes.
