# Plan — IA stalker y anti-cheese del Nemesis

> **Plan de implementación futura. Nada de esto está construido.**
> Compara el análisis *"IA de enemigos stalker"* (Alien: Isolation, Mr. X, Nemesis, Dimitrescu,
> Requiem, GDC) contra lo que el Nemesis de WIRED ya tiene hoy, y propone qué construir, en qué
> orden y dónde. **Asume que el sistema de escondites se construye** (spec *Hiding System v1.0*), y
> por eso el eje del plan es el anti-cheese alrededor de esconderse.
>
> Arquitectura vigente: `docs/CLAUDE.md` (inglés). Tuning del Nemesis: `docs/Nemesis-System.md`.
> Todo el código nuevo va en inglés, como el resto de `Assets/_Project/Scripts/`.
>
> Relevado contra el código el 14/09/2026 (rama `iña`, commit `79c2652`). La sección de bajadas
> ([§8](#8-traversing-hacia-abajo-bajadas-por-puntos-del-mapa)) se relevó el 15/09/2026 contra `718ee6a`.

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
8. [Traversing hacia abajo: bajadas por puntos del mapa](#8-traversing-hacia-abajo-bajadas-por-puntos-del-mapa)
9. [Arquitectura resultante](#9-arquitectura-resultante)
10. [Fases de implementación](#10-fases-de-implementación)
11. [Reglas del proyecto que este plan no puede romper](#11-reglas-del-proyecto-que-este-plan-no-puede-romper)
12. [Decisiones abiertas](#12-decisiones-abiertas)
13. [Valores iniciales](#13-valores-iniciales)
14. [Casos de prueba](#14-casos-de-prueba)

---

## 0. Resumen

**El Nemesis ya implementa la mayor parte de la arquitectura que propone el análisis**, con otros
nombres: percepción, creencia y decisión separadas; visión con banda periférica que acumula;
oído atenuado por paredes y pisos y medido sobre el NavMesh; persecución con predicción y desvíos;
búsqueda legible con barrido de habitación; un Director que nunca toca el FSM; la entrada tipo
Mr. X; y un set de herramientas de debug que el análisis pide construir "primero".

Lo que falta se concentra en seis agujeros:

| # | Agujero | Gravedad |
|---|---|---|
| 1 | **Escondites.** El lado jugador no existe. El lado Nemesis es binario (escondido = ciego) y, con los lockers del proyecto, **inmunidad total** — ver [§3.3](#33-hallazgo-con-los-lockers-actuales-esconderse-es-inmunidad-total). | Bloquea el feature |
| 2 | **Nadie cuenta los hábitos del jugador.** No hay ninguna contra-jugada. Es el anti-cheese entero. | Alta |
| 3 | **No se detecta la persecución estancada.** El jugador corriendo (4.5 m/s) es más rápido que el Nemesis persiguiendo (3.0 m/s): **un loop alrededor de una columna es un exploit hoy**, sin escondites. | Alta, existe ya |
| 4 | **El Director no mide tensión ni administra ritmo.** Sólo reacciona a pedidos (puzzles, API). No hay Relax ni retirada. | Media — pero sin esto el anti-cheese frustra |
| 5 | **Escalada por progreso** (spec Nemesis §7.2) sin hacer. | Media, diferida por diseño |
| 6 | **Bajadas.** Sólo cambia de piso por el montacargas, y el jugador puede bajar por bordes por donde él no (C11). Además, hoy cualquier link simple lo deja hasta 12 s en `Traversing`, con un peldaño que le gana a `"lo está viendo"` — ver [§8.2](#82-hallazgo-un-link-simple-hoy-lo-mete-en-traversing-hasta-12-s). | Media. El bug de los 12 s ya existe |

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
| 4 | Administrar la tensión, no maximizarla | ❌ | `NemesisDirector` aplica presión cuando se la piden (`puzzleTriggers`, `RequestPressure`). | No hay medidor, ni estados de ritmo, ni retirada. El único alivio es la gracia post-captura (4 s) y que la búsqueda se agote (15 s). |
| 5 | Nada guionado para el "cuándo" y el "dónde" | 🟡 | Patrulla por ruleta, cúmulos, satélites; spawn y entrada muestreados. | Los disparadores del Director son siempre "al completar el puzzle". Aceptable: el *qué* puede ser fijo. |
| 6 | Incertidumbre estructurada | 🟡 | 15 % de invertir la ronda, 15 % de saltear un waypoint. | **`patrolWaitVariance` está en 0 en el asset**: la espera en cada waypoint es un metrónomo. Se arregla con un número. |
| 7 | Anticipación dramática | 🟡 | Pasos reales, ocluidos por pared; puertas que suenan al abrirlas; música de persecución. | `NemesisAudio.stateLoops` vacío (sin respiración ni voz), sin cue de activación. La música de persecución delata el estado interno — ver D5. |
| 8 | Legibilidad por encima de inteligencia | ✅ | `SearchPauseTime` + `NemesisLookAround`; el HUD F9 muestra el peldaño ganador. | Las contra-jugadas nuevas tienen que **verse** (regla R3). |
| 9 | Anti-cheese con comportamiento | ❌ | Nada cuenta hábitos. | Todo [§4](#4-catálogo-de-cheeses-de-wired) y [§5](#5-hábitos-del-jugador-y-contra-jugadas). |
| 10 | Detectar el estancamiento | 🟡 | `NemesisStuckEscape` (cuerpo trabado: repath → warp). `NemesisPursuit` predice e intercepta. | Nadie mide "persigo pero no acorto". Ver C4. |
| 11 | El NavMesh expresa personalidad | 🟡 | Hub `Not Walkable`, puertas con carve, montacargas con links. | Área 3 `NemesisAvoid` sin uso. Sin rutas de flanqueo. Sin bajadas autoradas, y los links autogenerados de Zona1 bajan por cualquier borde de hasta 1.5 m ([§8](#8-traversing-hacia-abajo-bajadas-por-puntos-del-mapa)). |
| 12 | Herramientas de debug primero | ✅ | F9 HUD, F10 consola, `NemesisGizmos`, validadores, `SO_NemesisDataEditor`. | Falta un panel de hábitos y otro de tensión. |

---

## 2. Mapeo: lo que propone el análisis → lo que ya existe

### 2.1 Ya existe — se reusa, no se reescribe

| El análisis propone | En WIRED es | Diferencias que importan |
|---|---|---|
| `NoiseEmitter` + `HearingSensor` | `PlayerStateManager.AudioEmitingZone` (esfera en la capa `DetectableAudio`, radios agachado 1 / caminando 2 / corriendo 6, apagada en quieto) + `FieldOfListening` | El ruido **dura**, no es un evento: tiene que vivir más de 0.1 s o cae entre dos barridos. Alcance = radio × `NoiseRangeScale` (2.5), tope `ListenRange` (15), ×0.6 por pared, ×0.75 por piso, distancia medida sobre el NavMesh. |
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
- Las bajadas por puntos del mapa (`NemesisDropPoint`, `TraverseDropAsync`) y separar el flag de
  cruce que hoy mezcla el montacargas con cualquier link ([§8](#8-traversing-hacia-abajo-bajadas-por-puntos-del-mapa)).

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
  "irse a los ductos" es la **retirada** del [§6](#6-director-tensión-y-ritmo). Las bajadas del
  [§8](#8-traversing-hacia-abajo-bajadas-por-puntos-del-mapa) no son esto: se llega a ellas
  caminando por el mismo NavMesh, se ven y se oyen.
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
- Todo modificador por escondite se deshace en **todas** las salidas: salir normal, captura,
  checkpoint, descarga de escena.
- Se borra la tecla `R` (`PlayerStateManager.cs:358`).

**Lo que el anti-cheese necesita y el spec no pide:**

| Agregado | Para qué |
|---|---|
| `PlayerStateManager.CurrentHidingSpot` (referencia); `IsHidden` pasa a ser `CurrentHidingSpot != null` | El Nemesis tiene que saber **en qué** escondite estás para "te vi entrar" y para revisarlo. |
| `HidingEvents` estático: `OnEntered(spot)`, `OnExited(spot)`, `OnSpotBurned(spot)`, con el mismo `ResetStatics` que `NemesisEvents` | Quien cuenta y quien reacciona no llaman al escondite ni al revés. |
| En cada `HidingSpot`: `SpotId` estable, un `ApproachPoint` sobre el NavMesh y la lista de colliders propios | Dónde se para el Nemesis para revisarlo; qué colliders ignorar en la proximidad (§3.3). |
| Entrar y salir llevan tiempo (0.5–0.8 s de animación) durante el cual el jugador es visible con normalidad | Es la ventana de "te vi entrar". Sin ella, entrar es instantáneo y la regla no tiene de dónde agarrarse. |

La consola F10 conserva su toggle *Hide* como "escondido sin escondite", para seguir probando la
visión sin armar un nivel.

### 3.2 Lado Nemesis, hoy

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

### 3.4 Modelo propuesto: tres niveles de conocimiento

**Nivel A — "Te vi entrar".** Inmediato, sin contador. Es la regla de Alien: si te ve entrar, te saca.

- En `HidingEvents.OnEntered(spot)`: si el Nemesis tiene al jugador en el foco, o
  `FieldOfView.TimeSinceLastSighting < seenEnteringWindow` (0.75 s), y el escondite está a menos de
  `ViewRange` de su ojo → `KnownSpot = spot`.
- Si sólo había sospecha periférica (`Awareness` ≥ umbral, < 1) → el escondite queda **sospechoso**
  y va a revisarlo (Nivel C), pero no es seguro. Esa es la zona gris.
- No hace falta tocar el `return` de `FieldOfView`: su razón sigue valiendo. `TimeSinceLastSighting`
  no se borra al esconderse, así que se puede leer en el momento de entrar.

**Nivel B — Visibilidad residual por tipo.** Reemplaza el "0 o todo" por lo que el spec llama
riesgo (locker medio, mesa alto, container bajo):

| Tipo | Hoy | Propuesto |
|---|---|---|
| `Container` | ciego | ciego (el spec lo pide así) |
| `Locker` | ciego | sólo acumula por las rendijas: alcance × `lockerVisionExposure` (0.25), **siempre por el acumulador, nunca instantáneo** |
| `UnderTable` | ciego | alcance × `underTableVisionMultiplier` (spec), también por el acumulador |

Si el medidor llega a 1 con el jugador escondido, no arranca una persecución: marca `KnownSpot`
(Nivel A). Los números van a `SO_HidingData` por tipo; `underTableVisionMultiplier` va al final de
`SO_NemesisData`, al editor y a los gizmos, como pide `CLAUDE.md`.

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
| `NemesisSearchingState.PickNextPoint` | Prioridad: `KnownSpot.ApproachPoint` → escondites a revisar (Nivel C) → barrido de habitación → grafo. Al llegar a un escondite, la pausa es la de "revisar". |
| `NemesisCatchState` | Fase nueva al principio, **sólo si el jugador está escondido**: abrir / sacarlo (animación), después `OnCaptured()` como siempre. `ECatchPhase` es privado, no se serializa: se puede insertar sin riesgo. |
| `CanReachPlayerNow` y `CheckExtremeProximity` | Ignoran los colliders del escondite ocupado (§3.3). Así la captura normal funciona desde el `ApproachPoint` (que tiene que quedar a menos de `catchMaxReach`, 1 m, de la posición interior). |
| `ENemesisPredicate` | Se agregan **al final**: `KnowsHidingSpot`, `IsCheckingSpot`. |
| Escalera | Dos peldaños, **en el asset y en `BuildDefaultLadder()`**: <br>• `"sabe en qué escondite está"` → `Searching`, debajo de `"lo está viendo"` y **arriba de** `"lo perdió de vista recién"` (si no, la gracia de 2.5 s de `Chasing` le gana). <br>• `"está revisando un escondite"` → `Searching` (`InState(Searching)` + `IsCheckingSpot`), **arriba de** `"le queda presupuesto de búsqueda"`, para que el presupuesto de 15 s no lo arranque con la mano en la puerta. |

### 3.6 Memoria por escondite

Cada escondite tiene un estado que ve el jugador:

```
Normal ──(usos repetidos)──▶ Sospechoso ──(más usos)──▶ Quemado
                              revisa primero             lo rompe: puerta arrancada,
                                                         el escondite deja de servir
```

"Quemado" es la destrucción de coberturas de Requiem. El escondite no se apaga por código invisible:
**el Nemesis lo rompe**, a la vista o dejando la evidencia (la puerta en el piso). `CanInteract()`
pasa a `false` y se cambia el modelo. La persistencia va en el tracker de hábitos, no en
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
- **Mitigante que ya existe:** los timers de módulo corren mientras estás escondido. Pero sólo si
  hay un módulo activo; entre módulos esconderse es gratis.
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
- **Nunca** subir la velocidad del Nemesis para arreglarlo: el análisis lo prohíbe, se nota, y
  además la penalización de módulo M2 (menos sprint) ya acorta esa diferencia por diseño.

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

### C6 — Ruido de cebo *(futuro)*
- Hoy no hay objetos para tirar. `SightCommitTime` (6 s) ya impide escaparse de una habitación
  comprometida con un ruido de afuera. Si se agregan tirables: contar los ruidos que no son del
  jugador; al N-ésimo el Nemesis va, pero busca en dona alrededor de **quien lo tiró** y no del
  impacto.

### C7 — Montacargas de ida y vuelta *(vigilar)*
- Subir y bajar para cortar la persecución. El claim, el compromiso de 12 s y el enfriamiento de 10 s
  ya lo acotan. Si aparece en playtest: emboscada en el landing de llegada en vez de perseguir.
- Con bajadas ([§8](#8-traversing-hacia-abajo-bajadas-por-puntos-del-mapa)): si te sintió abajo
  mientras cazaba y hay una bajada cerca del hueco, baja por ahí en vez de esperar la cabina. Si ya
  estaba comprometido esperándola, sigue esperando (trade-off del §8.3).

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

### C11 — Bajar por donde él no puede
- **Qué hace:** se tira por un borde de más de 1.5 m (el jugador es un Rigidbody con gravedad: sin
  baranda, se puede) y el Nemesis tiene que dar toda la vuelta por la escalera o el montacargas.
- **Por qué funciona hoy:** el bake no genera bajadas más altas que `ledgeDropHeight` (1.5 m) y no
  hay ninguna autorada.
- **Contra-jugada:** no es de IA, es de nivel. Donde el jugador puede bajar hay un
  `NemesisDropPoint` o una baranda (D9). Así bajar deja de ser una escapatoria y pasa a ser una ruta
  más ([§8](#8-traversing-hacia-abajo-bajadas-por-puntos-del-mapa)).

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

## 8. Traversing hacia abajo: bajadas por puntos del mapa

Hoy el Nemesis cambia de piso **sólo por el montacargas**. La propuesta es sumarle puntos autorados
del mapa por donde **baja**: un balcón, una pasarela, un agujero en la rejilla. Son de una sola vía
(subir sigue siendo por montacargas o escalera) y se leen en pantalla: se asoma, cae, se oye el golpe.

### 8.1 Qué hay hoy

- **El montacargas:** `NemesisElevatorLink` (link bidireccional, área 4 `Forklift`, costo 3),
  `NemesisElevatorUser`, que lo cruza, y el estado `Traversing` con tres peldaños.
- **Los demás links el Nemesis ya los cruza, pero mal.** `NemesisElevatorUser` apaga
  `autoTraverseOffMeshLink` en `Awake` y se hace cargo de todos. Los que no son montacargas pasan por
  `TraverseSimpleLinkAsync` (`NemesisElevatorUser.cs:438`): un lerp recto de punta a punta a
  `linkTraversalSpeed` (2.5 m/s), sin arco, sin animación y sin sonido.
- **`WIRED_Zona1_Blockout` hornea con *Generate Links* prendido** (`m_GenerateLinks: 1`), con
  `ledgeDropHeight` 1.5 m y `maxJumpAcrossDistance` 2 m (`ProjectSettings/NavMeshAreas.asset`). Si el
  bake encontró bordes de hasta 1.5 m, el Nemesis ya baja por ahí, en cualquier punto del borde, no
  en puntos elegidos. Una bajada de un piso entero (≥ `floorHeightThreshold`, 2.5 m) el bake **no la
  puede generar**: o se autora, o no existe.
- **No hay canal de animación one-shot.** `EGait` es Idle/Walking/Running/Grabbing, y el propio
  código deja anotado que "reproducir una caída y esperar a que aterrice" es otro canal, a agregar al
  lado (`NemesisStateManager.cs:263`).

### 8.2 Hallazgo: un link simple hoy lo mete en `Traversing` hasta 12 s

- `IsUsingElevator` (`NemesisStateManager.cs:149`) lee `NemesisElevatorUser.IsTraversing`, y
  `TraverseSimpleLinkAsync` prende ese mismo flag (`:440`). El nombre dice montacargas, pero el flag
  significa "cualquier link".
- Cualquier salto o bajada simple dispara entonces el interrupt `"esta cruzando el montacargas"` y lo
  manda a `Traversing`.
- Al aterrizar, `"ya se comprometio con el montacargas"` (`InState(Traversing)` + tiempo en el estado
  y edad de la creencia bajo `ElevatorCommitTime`) lo **retiene hasta 12 s**. Ese peldaño está
  **arriba de `"lo está viendo"`** (`SO_NemesisPriorities.cs:155` lo dice explícito). Mientras
  tanto va hacia `believedTarget` sin la predicción ni los desvíos de `NemesisPursuit`, y la
  telemetría no lo cuenta como persecución (`NemesisTelemetry.cs:164`).
- Con *Generate Links* prendido en Zona1, esto puede estar pasando ya. **Para confirmarlo en F9:**
  cruzá un link autogenerado en plena persecución. Si después de aterrizar el peldaño ganador es
  `"ya se comprometio con el montacargas"`, es esto.
- El arreglo es el paso 1 de la Fase 4B, y sirve aunque nunca se autore una bajada.

### 8.3 Modelo: una bajada es un paso, no una intención

El FSM dice qué quiere el Nemesis (perseguir, buscar). Bajar es *cómo* llega, igual que abrir una
puerta, y `NemesisDoorUser` abre puertas sin tocar el FSM. Por eso **no hay estado ni peldaño nuevo
para la bajada**: el Nemesis sigue en `Chasing`, `Searching` o `Investigating` antes, durante y
después. Es el mismo razonamiento que descartó `CheckingSpot` en
[§3.5](#35-cómo-se-expresa-en-el-fsm--sin-estado-nuevo). `Traversing` queda para lo que justifica
su existencia: el compromiso largo del montacargas.

**Piezas**

| Pieza | Qué es |
|---|---|
| `NemesisDropPoint` ★ | Va en un objeto **estático** con `[RequireComponent(typeof(NavMeshLink))]`, como `NemesisElevatorLink`. Configura el link en `Awake`: `bidirectional = false` (sólo baja), área `NemesisDrop`, ancho ~1.5 m. Se registra en una lista `Active` estática, como los montacargas. Serializa sólo lo que es del lugar: `EDropStyle` (`Ledge`, `Railing`, `Hole`; **sólo se agrega al final**) y si vale patrullando (default no, D10). Velocidades y tiempos van a los SO. |
| Área `NemesisDrop` ★ | El slot 5, libre, de `NavMeshAreas.asset`, con costo ~2. Regula cuánto prefiere el pathfinder la bajada frente a la escalera o al montacargas (costo 3) sin tocar código. |
| `ECrossing` ★ | `NemesisElevatorUser` expone qué cruce hay en vuelo: `None`, `Elevator`, `Drop`. `IsUsingElevator` pasa a ser `== Elevator` (lo que su doc ya dice), `IsDropping` es `== Drop` e `IsBodyDriven` es `!= None`. No se serializa. |
| `NemesisElevatorUser.TraverseDropAsync` ★ | La ejecución. Va en el mismo componente porque ya es dueño de todos los links, del `PushStuckSuppression`, del `finally` con UniTask y de devolver el agente al NavMesh. En `Update`: si el dueño del link es `NemesisElevatorLink`, como hoy; si es `NemesisDropPoint`, `TraverseDropAsync`; si no tiene dueño (autogenerado), también `TraverseDropAsync` con el estilo más bajo, y los bordes de 1.5 m dejan de ser un lerp. El componente no se renombra; sólo su doc. |
| Canal one-shot ★ | El que anota `NemesisStateManager.cs:263`: reproducir y esperar a que termine, al lado de `SetGait` y sin mezclarse. Lo usan la bajada y, después, el "sacar del escondite" de `Catch` ([§3.5](#35-cómo-se-expresa-en-el-fsm--sin-estado-nuevo)). |

**La ejecución.** El agente queda **prendido**, con el patrón de link manual que ya usa
`TraverseSimpleLinkAsync`: el agente se queda en el link, se mueve el Transform y al final
`CompleteOffMeshLink()`. No hace falta apagarlo como en el montacargas, porque el cuerpo nunca sale
del link.

| Fase | Duración | Qué pasa | Por qué |
|---|---|---|---|
| 1. Asomarse | ~0.35 s | Se frena en el borde (`HoldStill`), gira hacia la caída (`TurnToFaceAsync` ya existe), anima el agarre del borde y suena un cue (metal que cruje). | **Es el tell** (principio 7, regla R3). Sin él, un monstruo que aparece abajo de golpe es un teleport con pasos. |
| 2. Caída | √(2h / `dropGravity`) | Arco balístico con un pico chico, no un lerp. Pasos **silenciados**: `FootstepEmitter.IsSuppressed` (`:324`) sólo tiene casos del jugador, y el arco avanza menos que `teleportThreshold` por frame, así que sin esto suenan pasos en el aire. | Un lerp recto a 2.5 m/s desde 3.5 m de altura se ve como un ascensor invisible. |
| 3. Impacto | un frame | Animación de aterrizaje y golpe fuerte en 3D (~20 m) desde `NemesisAudio`. `InvalidateRouteVerdict()`, porque cambió de piso. | Aunque no lo haya visto, el jugador se entera de que bajó. |
| 4. Recuperación | ~0.7 s | Quieto, agachado. Al final `CompleteOffMeshLink()`, y el estado retoma con su propia marcha. | Es la ventana del jugador: aterrizar y seguir corriendo en el mismo frame no se puede leer ni esquivar. |

- **El golpe no es un `NoisePulse`.** Una esfera en `DetectableAudio` es lo que **oye el Nemesis**:
  un golpe suyo en esa capa lo manda a investigar su propio aterrizaje. Es un `AudioSource` común.
- **Si el jugador está parado en la punta de abajo,** la caída termina a su lado, dentro del ancho
  del link, y no encima. Dos cuerpos que se superponen y se empujan por física son un bug, no una
  mecánica.
- **Cancelación** (captura, checkpoint o descarga de escena en plena caída): el `finally` deja el
  agente sobre la punta de abajo, que está horneada, y devuelve la supresión del watchdog y la
  marcha. Es el mismo contrato que el montacargas.

**La decisión, mientras cae**

- Mientras `IsDropping`, el facade saltea `TickDecision()`, `base.Update()` y
  `TickLocomotionAnimation()` (`NemesisStateManager.cs:1061–1067`); el Animator lo maneja el canal
  one-shot. Hoy el único congelamiento es con el agente apagado (`:521`), y en una bajada el agente
  está prendido. Sin esto, `Chasing` escribe `destination` desde su `UpdateState`
  (`NemesisChasingState.cs:85`) con el cuerpo parado en el link: es el mismo *grind* contra la
  dirección del link que ya costó un bug en el montacargas.
- **Los sentidos no se congelan.** Ve y oye durante la caída, y al aterrizar decide con lo último
  que sintió.
- **La captura no se evalúa en el aire.** Se evalúa cuando termina el congelamiento, después de la
  recuperación; `"lo tiene al alcance de la mano"` sigue arriba de todo (D11).
- **El guard del Director cambia.** `NemesisDirector.StageEntranceAsync` hoy saltea con
  `IsUsingElevator` (`NemesisDirector.cs:538`). Cuando ese flag pase a ser sólo el montacargas, el
  guard tiene que ser `IsBodyDriven`; si no, el Director lo podría warpear en plena caída.

**Cuándo las usa**

- **Para cazar, no para pasear.** Las bajadas quedan activas en los estados `FreeRoam` de
  `MovementOf` (`Chasing`, `Searching`, `Investigating`) e inactivas en los `NodeBound`
  (`Patrolling`, `Traversing`). Se implementa prendiendo y apagando `NavMeshLink.activated` en cada
  cambio de estado, igual que `SetShaftLinkActive` en el montacargas, y con
  `InvalidateRouteVerdict()`. Un punto marcado "vale patrullando" queda siempre activo: sirve para
  rondas que suben por el montacargas y bajan saltando.
- **Por qué el link y no el `areaMask` ni el `SetAreaCost` del agente:** el agente y el oráculo
  tienen que ver **el mismo grafo**. `NemesisNav` calcula con `NavMesh.CalculatePath`, con la
  máscara global `NemesisNav.AreaMask` (copiada del agente en `NemesisLifecycle.cs:138`) y con los
  costos globales: el costo por agente no lo ve. Si la decisión y el agente difieren, la escalera
  cree que la ruta baja y el agente camina la escalera, o al revés. Activar el link lo cambia para
  los dos a la vez.
- **Trade-off:** una vez comprometido con el montacargas (`Traversing`), no cambia a una bajada a
  mitad de camino, igual que hoy no cambia a la escalera. El compromiso es el diseño. Si en playtest
  molesta (C7), la salida es activarlas también en `Traversing` y soltar el compromiso cuando la ruta
  pasa a bajar; `HasGivenUpOnElevator` ya es la vía de salida de ese peldaño.

**Por qué no hace falta el compromiso de `Traversing`.** Existe porque el viaje en montacargas son
decenas de segundos con el jugador invisible detrás de una losa. Una bajada son pocos metros de
caminata hasta el borde y, por cómo se autoran (balcones, pasarelas, agujeros), casi siempre con
línea de visión hacia abajo. Si pierde la vista, el primer destino de `Searching` es la creencia o
la intercepción, que están abajo, y la ruta hacia ahí pasa por la bajada. **A confirmar en el
testbed (caso 13):** si `Searching` termina barriendo el piso de arriba (el barrido de 8 m alrededor
de una creencia que está justo debajo de un balcón puede muestrear puntos en el balcón), el arreglo
va en el muestreo de `NemesisFreeRoam`, descartando puntos fuera del piso de la creencia
(`|Δy| ≥ floorHeightThreshold`), y no en la escalera.

**Distancias: el NavMesh deja de ser simétrico.** Con links de una vía, de A a B pueden ser 4 m
(bajando) y de B a A, 60 m (por el montacargas). Toda consulta se tiene que medir **en el sentido en
que se va a caminar**. Relevado:

| Llamada | Mide | Estado |
|---|---|---|
| `NemesisPathOracle` (`:98`), `FieldOfListening` (`:276`), `NemesisTelemetry` (`:139`), `NemesisDebugHUD` (`:278`) | Nemesis → objetivo | ✅ |
| `NemesisController.DistanceToPlayer` (`:1121`, spawn) | punto → jugador | ✅ el Nemesis sale del punto |
| `NemesisDirector.TryFindEntrancePoint` (`:630`) | **jugador → punto** | ❌ **Hay que darlo vuelta.** Una entrada en el piso de abajo mide "cerca" desde el jugador de arriba (bajando se llega rápido), pero el Nemesis tiene que *subir* por el montacargas: la entrada nunca llega, justo lo que ese método dice que quiere evitar. |
| `NemesisClusterPatrol` (`:619`), `NemesisRouteGraph` (`:407`, `:501`, `:725`) | punto → ancla / nodo | Sin efecto mientras las bajadas estén apagadas patrullando. Si se marca un punto "vale patrullando", revisar que el agrupamiento no junte en un cúmulo nodos a los que sólo se llega de ida. |

**Validador** (en `NemesisSetupValidator`, *Tools/Nemesis/Validate Navigation Setup*):

- Link de una vía, con las dos puntas sobre NavMesh del agente, y `NemesisDrop` dentro del
  `areaMask` del agente.
- Altura entre `dropMinHeight` (por encima de `ledgeDropHeight`, para no pisarse con los
  autogenerados) y `dropMaxHeight`.
- **Toda bajada tiene vuelta:** desde la punta de abajo hay ruta a la de arriba sin usar bajadas.
  Si no, un Nemesis que baja cazando queda encerrado abajo.
- La punta de abajo no cae en el Hub (Not Walkable) ni en `NemesisAvoid`, ni pegada a ellos.
- Ninguna punta a menos de ~2 m de un landing de montacargas. `NemesisNav.FindCrossedElevator`
  (`:180`) reconoce el montacargas porque las esquinas del camino pasan cerca de sus landings: una
  bajada pegada se leería como un viaje en montacargas.
- Una cápsula del tamaño del agente en la punta de abajo no toca nada.

**Legibilidad.** Sin UI ni marcadores: la bajada la marca la geometría (una baranda doblada, un
tramo de rejilla arrancada, el borde rayado). La primera vez que el jugador lo ve bajar entiende el
lugar; las siguientes, lo reconoce.

**Subir queda afuera.** Trepar necesita una animación de trepada, y `docs/CLAUDE.md` pone
*climbing* entre los sistemas que no existen. Con esta base, un `NemesisClimbPoint` sería el mismo
patrón con otro estilo.

---

## 9. Arquitectura resultante

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

 CUERPO   (debajo del FSM, no decide) NemesisDoorUser · NemesisElevatorUser: montacargas + ★ bajadas
          (★ ECrossing, ★ canal one-shot)  ◀── ★ NemesisDropPoint: link de una vía, área NemesisDrop
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
| `NemesisDropPoint` | Configurar su link de una vía y su estilo; prenderlo o apagarlo según el estado del Nemesis | Mover al Nemesis (eso es `NemesisElevatorUser`); decidir si baja (eso es el pathfinder) |

`NemesisHidingAwareness` y `NemesisChaseProgress` son hermanos del facade, como `NemesisPathOracle`:
se agregan solos y el estado los consulta a través de `NemesisStateManager`.

---

## 10. Fases de implementación

Orden recomendado. La Fase 4 no depende de los escondites y arregla un cheese que ya existe, así que
puede ir en paralelo con la 1. La 4B tampoco depende de nada, y su paso 1 arregla un comportamiento
que ya existe (§8.2).

### Fase 0 — Ajustes sin código
- `patrolWaitVariance` → ~0.6 en `SO_NemesisData.asset` (hoy 0: metrónomo).
- Autorar `NemesisAudio.stateLoops` (los clips ya están en `Audio/SFX/Nemesis/`). La respiración del
  Nemesis es el tell de la emboscada (C1): sin eso, esa contra-jugada es invisible.
- Decidir D5 (música de persecución).
- **Verificación:** F9 muestra esperas distintas en cada waypoint.

### Fase 1 — Escondites, lado jugador *(prerrequisito)*
- `HidingSpot`, `SO_HidingData`, enum de tipo, `PlayerHiddenState` real, cámaras interiores,
  respiración por pulsos, `F` para aguantar, guard de `Tab`, snapshots del mixer.
- `CurrentHidingSpot`, `HidingEvents`, `ApproachPoint`, `SpotId`.
- Limpieza en todas las salidas; se borra la tecla `R`.
- **Verificación:** los casos del spec; con F10, el Nemesis no te ve; la respiración se oye a la
  distancia de los gizmos; después de salir el emisor queda como estaba (caminar vuelve a sonar a 2).

### Fase 2 — El Nemesis sabe de escondites
- `NemesisHidingAwareness`: Nivel A (visto entrando) y Nivel B (visibilidad residual por tipo).
- **Arreglo de §3.3**: proximidad y captura ignoran el escondite ocupado.
- `Searching` va primero al escondite conocido; `Catch` con fase de sacar al jugador.
- Predicados `KnowsHidingSpot`, `IsCheckingSpot` (al final del enum) y los dos peldaños (**asset y
  `BuildDefaultLadder()`**).
- `underTableVisionMultiplier` al final de `SO_NemesisData` + editor + gizmos.
- **Verificación:** casos 1–5 del [§14](#14-casos-de-prueba).

### Fase 3 — Contar sin reaccionar
- `PlayerHabitTracker` (`ISessionResettable`), `SO_CounterplayRules`, `EExploitKind`, puntos de
  registro (§5.3).
- Panel de hábitos en F9 + log de cada registro.
- **Sin contra-jugadas todavía.** Se juega para calibrar los umbrales con datos y no a ojo.

### Fase 4 — Persecución estancada *(independiente)*
- `NemesisChaseProgress`, predicado `IsChaseStagnant`, penalización del rastro en `NemesisPursuit`,
  soltar y emboscar.
- Agregar una columna o una mesa aislada a `NemesisTestSceneBuilder` para tener el test de la mesa
  siempre a mano.
- **Verificación:** caso 7.

### Fase 4B — Bajadas *(independiente)*
1. **El arreglo del §8.2:** `ECrossing` en `NemesisElevatorUser`; `IsUsingElevator` sólo para el
   montacargas; `IsDropping` / `IsBodyDriven`; el facade congela decisión, estado y locomoción
   durante la bajada; el guard del Director pasa a `IsBodyDriven`. Este paso solo ya cambia cómo se
   comportan los links autogenerados de Zona1.
2. Área `NemesisDrop`, `NemesisDropPoint`, activación según `MovementOf`, validador.
3. `TraverseDropAsync` (cuatro fases), canal one-shot, pasos silenciados en el aire, cue y golpe en
   `NemesisAudio`.
4. Dar vuelta la distancia de `NemesisDirector.cs:630`.
5. Números: velocidades y tiempos en `SO_NemesisMovement` **con inicializador** (su propio
   comentario avisa que un campo sin default deserializa en 0 y el Nemesis se congela a mitad del
   link); lo de comportamiento al final de `SO_NemesisData`, con `SO_NemesisDataEditor` y
   `NemesisGizmos` (la bajada dibujada con su altura); una fila en F9 (`cruce: Drop · DropPoint_Balcon · fase 2/4`).
6. Testbed: un balcón con bajada en `NemesisTestSceneBuilder`, con toggle en F10. **No puede quedar
   siempre activa:** `Spawn_Alta` es el caso de bajar por el montacargas
   (`NemesisTestSceneBuilder.cs:556`), y una bajada más barata se lo roba.
7. Zona1: auditar los links autogenerados (overlay de AI Navigation, *Show Links*) y decidir cuáles
   quedan. Después, autorar las bajadas.
- **Verificación:** casos 12–18.

### Fase 5 — Tensión y ritmo
- `NemesisTension`, estados de ritmo en el Director, retirada, sensibilidad creciente, fila en F9.
- **Verificación:** caso 9.

### Fase 6 — Contra-jugadas desbloqueables
- `CheckHidingSpots`, `PrioritizeSuspiciousSpots`, `ExitAmbush`, `BurnHidingSpot` (necesita el
  modelo del locker roto), `ChaseFlank`, `ZoneDefense` + `NemesisAmbushPoint`.
- La regla R3 (la primera vez se ve) implementada, no sólo pedida.
- **Verificación:** casos 6 y 8.

### Fase 7 — Escalada por puzzles
- Spec §7.2 con el mecanismo ya decidido (§7).

**Por qué en este orden:** la 1 es prerrequisito. La 2 cierra el agujero de inmunidad, sin el cual
esconderse rompe el juego. La 3 va antes que la 6 para que los umbrales salgan de datos. La 5 va
antes que la 6 porque contra-jugadas sin Relax frustran. La 4 es independiente y arregla algo que
hoy ya se puede explotar. La 4B también es independiente, y conviene hacer su paso 1 cuanto antes.

---

## 11. Reglas del proyecto que este plan no puede romper

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
  sola.
- **Todo valor tuneable tiene dónde verse:** `SO_NemesisDataEditor`, `NemesisGizmos`, F9.
- **Nada depende de la cámara del jugador.**
- **Un solo dueño del cuerpo por vez.** Mientras un cruce mueve al Nemesis (`IsBodyDriven`), ni la
  escalera ni los estados escriben `destination`, marcha ni animación.
- **Con bajadas, el NavMesh es de una sola vía:** toda distancia se mide en el sentido en que se
  camina.
- **El agente y el oráculo ven el mismo grafo.** Lo que cambia qué links existen se hace activando
  links, no con máscaras ni costos por agente.

---

## 12. Decisiones abiertas

| # | Pregunta | Recomendación |
|---|---|---|
| D1 | Cuando te encuentra escondido, ¿captura directa o te saca y arranca una persecución? | **Captura** (la captura ya es un costo, no un Game Over). El margen del jugador está **antes**: ver al Nemesis acercarse desde adentro y decidir salir corriendo antes de que abra. |
| D2 | ¿El Nemesis puede romper escondites para siempre? | Sí, con evidencia visible. Necesita arte: el locker roto. |
| D3 | ¿Lo aprendido sobrevive a la captura y al checkpoint? | Sí. Por eso vive en el tracker y no en `PuzzleStateManager`, que se revierte con el checkpoint. Se resetea con New Game. |
| D4 | ¿Los hábitos decaen? | Sí, lento (del orden de minutos sin repetirlo). |
| D5 | La música de persecución se apaga cuando `Chasing` termina, así que **avisa que te perdió de vista**: es un estado interno filtrado al audio (análisis §12.4). | Que termine cuando termina la **búsqueda** comprometida, o con un fade mucho más largo. |
| D6 | Espiar con la cámara orbital (C8). | Aceptarlo, como la mayoría de los juegos en tercera persona. Si molesta, se ajusta la cámara, no la IA. |
| D7 | ¿Locker con visibilidad residual (Nivel B) o ciego salvo proximidad? | Residual y baja. Si no, "riesgo medio" (spec) y "riesgo bajo" (container) son lo mismo. |
| D8 | ¿Va a haber dificultad seleccionable? | Si la hay, se escalan sentidos y umbrales de desbloqueo, nunca la velocidad (análisis §12.2). |
| D9 | ¿El jugador puede bajar por los mismos lugares? | Sí, donde la geometría lo deje. Regla de nivel: donde el jugador puede bajar más de 1.5 m hay un `NemesisDropPoint` o una baranda (C11). Una bajada que sólo usa el Nemesis vale si se ve. |
| D10 | ¿Usa bajadas patrullando? | No por defecto: se vuelven rutina y pierden impacto. Por punto, si una ronda lo pide. |
| D11 | ¿Te puede agarrar al aterrizar? | Sí, pero después de la recuperación (0.7 s). Asomarse es el aviso; un agarre en el aire no se puede leer. |

---

## 13. Valores iniciales

Puntos de partida para calibrar con la Fase 3, no para dejar fijos.

| Parámetro | Valor | Origen |
|---|---|---|
| `seenEnteringWindow` | 0.75 s | Ventana de la animación de entrada |
| `lockerVisionExposure` | 0.25 del alcance, sólo por acumulador | Spec: riesgo medio |
| `underTableVisionMultiplier` | 0.5 | Spec: riesgo alto |
| Umbral sospechoso / quemado | 2 / 4 usos del mismo escondite | Requiem, Isolation (2–3) |
| `CheckHidingSpots` | 3 escapes escondido | Isolation (lockers) |
| `chanceAtUnlock` / por uso extra / tope | 0.35 / +0.1 / 0.85 | Que siga siendo apuesta |
| Duración de la emboscada de salida | 8–15 s, sorteado | Isolation (emboscada con máximo) |
| `chaseProgressWindow` / `chaseMinProgress` | 4 s / 1.5 m (por NavMesh) | Análisis §12.1 |
| Penalización del rastro en `NemesisPursuit` | ×0.2 al peso del waypoint | RE4R (Flanker) |
| `SustainPeak` / `Relax` | 3–5 s / 30–45 s | Left 4 Dead (GDC 2009) |
| `quietTimeout` (sensibilidad creciente) | 90 s sin contacto | Mr. X; ajustar al tamaño del nivel |
| `patrolWaitVariance` | 0.6 s | `docs/CLAUDE.md` |
| `dropMinHeight` / `dropMaxHeight` | 1.6 m / 4.5 m | Por encima de `ledgeDropHeight` (1.5); hasta un piso |
| Asomarse (`dropWindup`) | 0.35 s | Legible sin frenar la persecución |
| `dropGravity` | 14 m/s² (3.5 m ≈ 0.7 s) | Estilizada: con 9.81 flota |
| `dropRecoveryTime` | 0.7 s | Ventana del jugador (D11) |
| Costo del área `NemesisDrop` | 2 | Montacargas (`Forklift`): 3 |
| Alcance del golpe (audio) | ~20 m | Que se oiga desde el piso de arriba |

Referencias del proyecto para calibrar: jugador 2.5 m/s (agachado 1.25, corriendo 4.5); Nemesis
patrulla 2.75 / investiga 2.5 / persigue 3.0 / busca 2.75; vista 7 m, foco 80°, periferia 170°;
oído 15 m de tope; proximidad extrema 1.5 m; alcance de captura 1 m; búsqueda 15 s; gracia de
persecución 2.5 s.

---

## 14. Casos de prueba

En `Scenes/Dev/NemesisTestbed` (F9 HUD, F10 consola) y después en `WIRED_Zona1_Blockout` desde
`Bootstrap`.

| # | Situación | Esperado |
|---|---|---|
| 1 | Te persigue, entrás al locker a la vista. | Nivel A: va directo al `ApproachPoint`, lo abre y te captura. F9 muestra `"sabe en qué escondite está"`. |
| 2 | Te persigue, doblás una esquina, entrás al locker fuera de su vista. | Barre la habitación y no revisa el locker (sin desbloqueo). Se va a los 15 s. Cuenta un `EscapedWhileHidden`. |
| 3 | Escondido en el locker, el Nemesis pasa a 1 m. | Te detecta por proximidad aunque el collider del locker esté en el medio (arreglo §3.3). |
| 4 | Debajo de la mesa, el Nemesis mirando de frente a 5 m. | La sospecha sube sin arrancar persecución; si llega a 1, pasa a Nivel A. |
| 5 | Escondido, soltás `F` (exhalás) con el Nemesis a 4 m. | `Investigating` hacia el escondite, no `Chasing`. |
| 6 | Tres escapes escondido, cuarta búsqueda. | A veces (sorteado) revisa escondites dentro de su barrido. La primera vez que lo hace estás en rango de verlo u oírlo (R3). |
| 7 | Loop alrededor de una columna corriendo. | En ~4 s F9 marca estancamiento; corta por el otro lado o suelta y embosca. Nunca se acelera. |
| 8 | Cuatro usos del mismo locker estando cazado. | Lo rompe: puerta arrancada, el locker ya no ofrece `[E]`. |
| 9 | Persecución larga que termina en escape. | La tensión no baja durante `Chasing`; al terminar, `PeakFade` → `Relax`, y la patrulla se va lejos durante 30–45 s. Si te lo cruzás igual, te persigue. |
| 10 | Te capturan con contadores altos y hacés respawn. | Los contadores **no** vuelven atrás con el checkpoint. New Game los pone en cero. |
| 11 | Salís del escondite durante una captura, un checkpoint o una descarga de escena. | El emisor, la cámara y los modificadores vuelven a lo normal; `CurrentHidingSpot` queda en `null`. |
| 12 | En plena persecución, el Nemesis cruza un borde autogenerado de 1.5 m. | Después de aterrizar, el peldaño ganador en F9 es `"lo está viendo"` o `"lo perdió de vista recién"`, nunca `"ya se comprometio con el montacargas"`. |
| 13 | Te ve desde un balcón con bajada; bajás por la escalera fuera de su vista. | Va al borde, se asoma (cue), cae, se recupera y sigue buscando **abajo**. No barre el balcón. |
| 14 | Estás parado al pie de la bajada cuando cae. | Aterriza a tu lado, no encima. No te agarra en el aire; si seguís ahí al terminar la recuperación, te agarra. |
| 15 | Patrullando pasa junto a una bajada sin la marca "vale patrullando". | No la usa. F9 muestra el link inactivo. |
| 16 | Captura, checkpoint o descarga de escena en plena caída. | El agente queda sobre el NavMesh (punta de abajo), con la marcha normal y la supresión del watchdog devuelta. |
| 17 | Cazando arriba, te siente abajo; hay bajada y montacargas. | Baja por la bajada (más barata); no va al montacargas. |
| 18 | Entrada tipo Mr. X con el jugador arriba de una bajada. | La entrada elegida está a distancia real de caminata **del Nemesis hacia el jugador**, no al revés. |
