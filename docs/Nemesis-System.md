# Sistema Nemesis

El perseguidor. No se lo puede matar, atrapar ni frenar: todo lo que el jugador tiene es evasión, distracción y sigilo. Tal como viene el prefab, lo despierta un puzzle y a partir de ahí patrulla las zonas que el jugador ya recorrió, que es lo que convierte el backtracking en una decisión de riesgo. **En Zona1 hoy no es así:** duerme toda la partida y sólo aparece en la secuencia de escape final (ver *Activación* y *En el escape de Zona1*).

```
DORMIDO         invisible, sin navegación, sin sentidos
   ↓ (se completa el puzzle de activación, o lo despierta un script: el escape)
PATRULLA        recorre cúmulos de waypoints, lento
   ↓ (ruido)              ↓ (te ve)
INVESTIGA  ──────────→  PERSIGUE  ──────────→  CAPTURA
   ↑                       ↓ (te pierde)
   └────  BUSCA  ←─────────┘
```

Código en `Assets/_Project/Scripts/Nemesis/`, ScriptableObjects en `Assets/_Project/ScriptableObjects/Nemesis/`, prefab en `Assets/_Project/Prefabs/Nemesis.prefab`, escena de pruebas en `Assets/_Project/Scenes/Dev/NemesisTestbed.unity`.

> Este documento es para tunear y armar niveles. La arquitectura interna —por qué cada cosa está donde está— vive en `docs/CLAUDE.md`, en inglés. El diseño (anti-cheese, escondites, Director, reglas duras y valores iniciales) vive en `docs/Plan-IA-Stalker.md`.

---

## Activación

El Nemesis **no existe** al empezar la partida. Está en la escena desde el principio pero dormido: sin agente, sin sentidos, invisible.

En el prefab, `NemesisController > Activation > Activated By Puzzle Id` (hoy `sp2_contenedores`). Ese id tiene que coincidir con el `PuzzleId` del `SO_PuzzleData` correspondiente. Vacío = activo desde que le das Play, que es lo que querés en la testbed y **no** lo que querés en el nivel.

**`Wake Only From Script`**, en el mismo bloque: prendido, el puzzle se ignora y el Nemesis duerme hasta que un script llama a `NemesisStateManager.ActivateInPlace()`, que lo despierta donde está **sin buscar spawn point** (el script lo pone él mismo en su marca). En Zona1 está prendido como override de la escena: lo despierta la cinemática del escape (`NemesisCinematicActor.TryTakeControl`). Apagarlo vuelve al despertar con `sp2_contenedores`.

Cuando el puzzle se completa, elige un spawn point. Un punto sólo sirve si cumple **las tres**:

1. está a más de `SpawnMinPlayerDistance` (15 m) **medido sobre el NavMesh**, no en línea recta — este piso no se relaja nunca;
2. está fuera del cono de visión del jugador (`SpawnSafeHalfAngle`, 90° = todo el hemisferio de adelante; 0 apaga el test);
3. está detrás de geometría.

Si ninguno cumple, **el Nemesis no aparece**: se vuelve a dormir y reintenta dos veces por segundo hasta que alguno sirva, cosa que normalmente pasa apenas el jugador camina o se da vuelta. Es a propósito — aparecer a la vista es la única forma de que la entrada se sienta tramposa.

Si nunca aparece, la consola dice cuál de los tres tests falló para todos los puntos. Poné los spawn points separados, detrás de cobertura, y lejos de donde el jugador va a estar parado cuando termine ese puzzle.

---

## Quién decide en qué estado está

**Los estados no deciden las transiciones.** Eso es lo primero que sorprende al leer el código: `NemesisPatrolState` no elige pasar a `Chasing`.

Quien decide es `NemesisDecision`, leyendo una **escalera de prioridades** que vive en `SO_NemesisPriorities.asset`. Cada peldaño es "si se cumplen estas condiciones, el estado pedido es X", y se leen en orden hasta que uno da verdadero. El asset es reordenable desde el inspector, así que **cambiar el orden de la escalera no recompila nada**.

Hoy son 19 peldaños, y el asset coincide con la escalera por defecto de `SO_NemesisPriorities.BuildDefaultLadder()`, donde cada uno tiene comentado por qué está en ese lugar (incluidos los de escondites: "sabe en qué escondite está", "está revisando un escondite", "sospecha de un escondite"). El peldaño que ganó en cada frame se ve en F9, fila *regla*.

Los seis estados:

| Estado | Qué es | Velocidad |
|---|---|---|
| `Patrolling` | Recorre cúmulos de waypoints. Espera en cada uno. | 2.75 |
| `Investigating` | Escuchó algo, vio algo de reojo o sospecha de un escondite, y va a ver. Al llegar se queda `investigationDwellTime` (4 s) mirando. | 2.5 |
| `Chasing` | Te ve. Persecución activa con predicción e intercepción. Si te pierde, corre hasta donde te vio por última vez. | 3.0 |
| `Searching` | Te perdió pero sabe por dónde andabas. Barre la zona; si sabe en qué escondite estás, va a ese. | 2.75 |
| `Traversing` | "Para llegar necesito el montacargas". | 3.0 |
| `Catch` | Te agarró (o te está sacando de un escondite). | — |

`Traversing` existe porque una losa corta la línea de visión durante todo el viaje: sin un estado propio que sostenga la decisión por `ElevatorCommitTime` (12 s), el Nemesis abandonaba el ascensor cada vez. El montacargas se marca con `NemesisElevatorLink` en su raíz estática (el header del script tiene la jerarquía). Uno que es sólo para el jugador, en una escena sin NavMesh, lleva `navMeshNotNeeded`: el link queda apagado y el Nemesis nunca lo usa.

> **Si agregás un estado al enum `ENemesisState`, agregalo AL FINAL.** `SO_NemesisPriorities.asset` guarda el estado destino de cada peldaño como un entero: insertar en el medio reescribe en silencio toda la escalera del diseñador en otra distinta.

---

## Los tres sentidos

Los tres corren simultáneos y se tunean desde `SO_NemesisData.asset`. Los números son los que están hoy en el asset.

### Vista — `FieldOfView`

| Parámetro | Valor | Qué hace |
|---|---|---|
| `viewRange` | 7 | Alcance máximo. |
| `viewAngle` | 170 | Cono total. Muy ancho a propósito: es *conciencia*, no detección. |
| `focusAngle` | 80 | Dentro de este cono la detección es inmediata. |
| `awarenessBuildTime` | 1.2 | Segundos en la periferia hasta sospechar. |
| `awarenessTriggerThreshold` | 0.4 | Cuánta sospecha hace falta para reaccionar. |
| `crouchVisionMultiplier` | 0.5 | Agachado te ve a la mitad de distancia. |

La detección **no es todo o nada**. Adentro de `focusAngle` te vio y listo. Entre `focusAngle` y `viewAngle` hay una banda periférica que va acumulando sospecha mientras te mantengas ahí, y la pierde a `awarenessDecayRate` cuando salís. Agacharse **no rompe la línea de visión**, sólo acorta el alcance: una silueta más baja es más difícil de distinguir, no invisible.

Estar `Hidden` (escondido) **ya no** salta la vista por completo: según el escondite, el Nemesis todavía te distingue a una fracción de `viewRange`, y eso nunca arranca una persecución directa. Ver *Escondites*.

### Oído — `FieldOfListening`

**El ruido en este proyecto es una esfera, no un evento.** No existe ningún `OnNoiseGenerated`. El jugador lleva un `SphereCollider` trigger (`PlayerStateManager.AudioEmitingZone`) cuyo radio setean los estados de movimiento según el paso, con los valores de `SO_PlayerMovement.asset`:

| Paso | Radio del emisor | Alcance sin paredes |
|---|---|---|
| Agachado (`crouchNoiseRadius`) | 1 | 2.5 m |
| Caminando (`footstepNoiseRadius`) | 4 | 10 m |
| Corriendo (`runNoiseRadius`) | 10 | 15 m (tope de `listenRange`) |
| Quieto | el GameObject se apaga entero — **quieto es silencioso** | — |

`FieldOfListening` barre cada 0.1 s, lee el radio real del collider y lo escala por `noiseRangeScale` (2.5), con `listenRange` (15) como tope, antes de atenuar por paredes (`wallOcclusionMultiplier` 0.8) y por pisos (`floorOcclusionMultiplier` 0.75). La distancia se mide **por camino de NavMesh**, no en línea recta. Si oye varias cosas a la vez va hacia la que oye mejor, no hacia la primera que devolvió la física.

Que los pisos atenúen en vez de cortar es deliberado: es el único canal que tiene el Nemesis hacia el piso de arriba.

Sólo cuentan los **triggers**. Un collider sólido en una capa que el Nemesis escucha es geometría en la capa equivocada (el `Stair_Divider` de Zona1 convertía la escalera en un ruido permanente, WIR-018/020): se ignora y la consola lo nombra una vez. Los señuelos son un segundo canal, aparte de las esferas — ver *Señuelos*.

> **Si algo tiene que "hacer ruido", tiene que durar.** Prendé el emisor al radio que quieras y dejalo más de 0.1 s. Un pulso de un frame puede caer entre dos barridos y no lo escucha nadie. Y devolvé el radio a donde estaba: el emisor lo comparten los estados de movimiento, y un radio olvidado deja al jugador permanentemente ruidoso sin que nada tire error.

### Proximidad extrema

`proximityDetectionRange` = 1.5, en horizontal y con el mismo tope de altura que la captura (`catchMaxVerticalOffset`, 1). A esa distancia te detecta **aunque estés escondido**, y existe para que un escondite pegado al Nemesis no sea un exploit. Respeta paredes (`proximityDetectionRespectsWalls`), pero no la carcasa del escondite en el que estás. Si estás escondido no arranca una persecución: marca ese escondite como conocido y el Nemesis va a sacarte (ver *Escondites*).

`proximityRadius` (12) es otra cosa: alimenta la viñeta de proximidad del HUD, no la detección. Se mide por NavMesh (`proximityUsesPathDistance`), para que no se prenda con el Nemesis en otro piso.

---

## Escondites

Código en `Scripts/Hiding/` (`HidingSpot`, `HidingEvents`, `EHidingSpotType`), un solo asset para todos en `ScriptableObjects/Hiding/SO_HidingData.asset`, prefabs en `Prefabs/HidingSpotFather/` (un padre y las variantes Locker, UnderTable y Container). Hoy sólo hay escondites colocados en `Scenes/Dev/TestIñaki.unity`.

**Del lado del jugador.** Entrar tarda `enterDuration` (0.6 s) con el jugador quieto y **completamente visible**: esa ventana es la que le permite al Nemesis "verte entrar". Adentro, `PlayerHiddenState` respira prendiendo el emisor de ruido: radio 0.8 cada 3 s (unos 2 m de alcance). Mantener **F** (`HoldBreath`) corta la respiración; soltarla larga una exhalación de radio 2.5 (unos 6 m), peor que no haber aguantado. El asset limita el aire a `maxHoldSeconds` 6 (el código trae 0 = sin tope) y lo recupera en `breathRecoverySeconds` (5 s). En el container los dos radios van ×0.5. El emisor es el mismo de los estados de movimiento, y al salir se devuelve como estaba.

**Lo que ve el Nemesis de alguien escondido** (`FieldOfView.HiddenViewRange`):

| Tipo | Hasta dónde te distingue | Condición |
|---|---|---|
| Locker | `viewRange` × `lockerVisionExposure` (0.35 → 2.45 m) | Sólo desde el lado de la puerta, por las rendijas |
| UnderTable | `viewRange` × `underTableVisionMultiplier` (0.5 → 3.5 m) | La mesa acorta la vista, no ciega |
| Container | nada | Sellado |

Eso **nunca** es detección inmediata: pasa por el acumulador de la periferia. Pasado `awarenessTriggerThreshold` el escondite queda *sospechado* y va a mirar (`Investigating`); con el medidor lleno queda *conocido*.

**Lo que sabe** (`NemesisHidingAwareness`, se agrega solo al Nemesis):

- **Conocido**: al terminar tu subida, si te estaba viendo o te vio en los últimos `seenEnteringWindow` (0.75 s), con el escondite dentro de `viewRange` y su puerta en línea de vista ("lo vio entrar"). También si el medidor se llenó mirando a través del escondite, o por proximidad extrema.
- **Sospechado**: te tenía de reojo, con el medidor pasado el umbral, cuando te metiste.
- **Se olvida** cuando lo revisa y está vacío, cuando te ve afuera, con una captura o un respawn, y como red de seguridad cuando nada lo confirma por más de `searchTimeOut` (conocido) o `investigationTimeOut` (sospechado).
- Salir sin que te vea no se le informa: va, encuentra vacío y se olvida. Puede equivocarse; no es omnisciente.

**Cómo te saca.** "sabe en qué escondite está" lo manda a `Searching`, que camina al `ApproachPoint` del escondite. El alcance del agarre se mide contra ese punto, y sólo para un escondite que conoce: pasar por delante de un locker que no sabe ocupado no saca a nadie. Antes de llamar a `OnCaptured()` se queda `hiddenPullOutTime` (0.8 s) parado en la puerta — todavía no hay animación propia, se reproduce la del agarre. No es una ventana para escapar: salís al lado de la puerta, al alcance.

**Armado**: el header de `HidingSpot.cs` tiene la jerarquía (`InteriorPose`, `ApproachPoint`, `ExitPose`, cámara interior) y el plan §14.4 dónde va cada pieza. `Tools > Player > Validate Hiding Spots` reporta `SpotId` vacío o repetido y un `ApproachPoint` fuera del NavMesh o a más de `catchMaxReach` del interior.

---

## Señuelos

Código en `Scripts/Decoys/`, datos en `ScriptableObjects/Decoys/`, prefabs en `Prefabs/Decoys/` (`Decoy_Radio`, `Decoy_FireAlarm`, `Decoy_Chains`). **Todavía no están colocados en ninguna escena.**

Un señuelo es un `DecoyNoiseSource` más el componente que decide cuándo suena. No es una esfera en la capa de escucha: `FieldOfListening` los lee de un registro propio (`DecoyNoiseSource.Active`), porque un señuelo dice en metros reales hasta dónde se oye —sin `noiseRangeScale` y sin el tope de `listenRange`— o que se oye desde cualquier lado. Paredes y pisos lo atenúan igual que al jugador. Uno que se oye en todos lados compite con margen 0: un ruido de verdad cerca le gana. El Nemesis va al `investigatePoint` proyectado al NavMesh; ponelo en el piso, del lado desde el que tiene que llegar.

| Señuelo | Usos | Se oye | Tuning (asset) |
|---|---|---|---|
| `RadioDecoy` | 1 | a 12 m | Suena hasta que la rompen (`maxPlayTime` 0). La rompe a 2.5 m: 1.2 s de corte enojado hasta el golpe, 1 s quieto después |
| `FireAlarmDecoy` | 1 | desde cualquier lado | Suena 10 s después de activarla, durante 30 s, y abre los rociadores |
| `ChainDecoy` | infinitos | a 8 m | 1.5 s de ruido, 2 s de enfriamiento |

La rotura la hace `NemesisDecoyBreaker`, que va en la raíz del Nemesis y **no** se agrega solo: **hoy el prefab no lo tiene**. Sin él la radio no se rompe, y con `maxPlayTime` 0 suena para siempre y lo sigue llamando. Sólo rompe el señuelo que lo trajo (`FieldOfListening.LastHeardDecoy`), no cualquiera por el que pase; si te ve durante el corte, lo abandona y la radio sigue sonando. Los tres assets tienen los ids de sonido vacíos.

---

## Rutas, waypoints y cúmulos

Una ruta es un GameObject con el componente `NemesisRoute`. Sus waypoints son **los hijos directos con el tag `NemesisWaypoint`**, en el orden de la jerarquía. Reordenar la ruta es reordenar los hijos en la jerarquía; no hay lista en el inspector.

Cada ruta tiene:

| Campo | Qué hace |
|---|---|
| `weight` | Cuánto sale sorteada frente a las otras. Es la "frecuencia de aparición por zona". |
| `startUnlocked` | Si arranca disponible. |
| `unlockedByPuzzleId` | Puzzle que la abre. Vacío = usa `startUnlocked`. |

**La patrulla es por ZONA, no por waypoint.** `NemesisClusterPatrol` agarra un cúmulo de waypoints cercanos (`clusterRadius` 12, hasta `maxClusterSize` 5), lo barre entero, y después se muda a un cúmulo vecino. Sin esto el monstruo salta de una punta del nivel a la otra y se lee como teletransporte. Los cúmulos recién barridos bajan de peso, que es lo que evita que haga ping-pong entre dos vecinos.

El Nemesis **no está encerrado en la ruta que le tocó**: `NemesisRouteGraph` fusiona todas las rutas desbloqueadas y resuelve qué waypoints están en la misma isla de NavMesh, así que puede tomar prestado un waypoint de otra ruta y adoptarla. Así cambia de piso.

**Variación semialeatoria**: cada vez que vuelve a `Patrolling` hay 15 % de invertir el sentido y 15 % de saltear el próximo waypoint (`routeReverseChance` / `routeSkipWaypointChance`). Es lo que impide memorizar la ronda.

### El Hub es zona segura

El Hub **no** es un `NavMeshObstacle`. Es un volumen **Not Walkable** (`NavMeshModifierVolume`) pintado en el NavMesh. El Nemesis no puede calcular un camino que lo atraviese, punto. Si el jugador se mete ahí durante una persecución, el Nemesis espera afuera y vuelve a patrullar cuando se le vence el `searchTimeOut`.

El bloqueo sigue sin tener código, y no hay que agregárselo. Lo que sí hay es un **`SafeZoneMarker`** sobre cada volumen del Hub (en Zona1, `'Safe Area '` y `Safe Area  (1)`): no bloquea nada, sólo le dice a `NemesisSafeZones` cuál de los volúmenes Not Walkable es un refugio (también hay volúmenes así adentro de props sólidos). Con eso:

- el Director rechaza zonas de presión con el centro a menos de 6 m del Hub (`NemesisSafeZones.Clearance`, una regla y no un número a tunear) y no pone ruidos ni entradas ahí — el cheese C5 del plan;
- el sesgo de patrulla hacia la posición real del jugador se apaga mientras está adentro;
- `NemesisTension` no cuenta el silencio mientras el jugador está en el Hub.

Ojo con la capa del volumen: `NavMeshSurface` filtra los modifier volumes por sus Include Layers, así que uno en una capa que el surface no recolecta se descarta sin avisar y el Nemesis entra. `Validate Navigation Setup` lo reporta.

---

## Persecución

**Te pierde de vista → va a donde te vio.** Sin la vista no predice ni flanquea: corre al último punto donde te **vio** —no al último ruido: el que corta la línea de visión y sigue corriendo se oye hasta el locker— y recién al llegar pasa a `Searching`. Tope de 10 s (peldaño "va a donde lo vio por última vez"). La búsqueda arranca ahí mismo, parado `searchPauseTime` (1.2 s) mirando alrededor. Si te vio meterte en un cuarto a menos de `roomCommitRange` (12 m de camino), barre ese cuarto (`roomSweepRadius` 8) y durante `sightCommitTime` (6 s) ignora los ruidos de afuera. Qué es "el cuarto" lo decide `NemesisRooms` por el collider del piso: en Zona1, el nombre `<CUARTO>_Floor_<n>`.

**Donde no llega, no insiste.** Si tu posición no tiene camino completo (`IsBeliefUnreachable`, WIR-018), ni "lo está viendo" ni "va a donde lo vio" lo sostienen en `Chasing`: no se queda mirándote desde el borde del NavMesh.

**El loop de la mesa.** Corriendo, el jugador (4.5 m/s) siempre le gana al Nemesis (3.0), así que dar vueltas a un obstáculo no termina nunca. `NemesisChaseProgress` mide, por NavMesh, si acorta distancia: si en `chaseProgressWindow` (4 s) no bajó `chaseMinProgress` (1.5 m), la persecución queda estancada (`ChaseStalled`, en F9). Mientras tanto `NemesisPursuit` castiga los waypoints de desvío que están sobre el rastro por donde vino el jugador (×`chaseTrailPenalty` 0.2 dentro de `chaseTrailPenaltyRadius`, 3 m) y acepta desvíos más largos (`chaseStagnantDetourTolerance` 2.5), para que la ruta salga por el otro lado. **Nunca lo hace más rápido.** Si no hay waypoints cerca del obstáculo no hay otro lado que elegir: eso se arregla con waypoints, no con tuning.

---

## Captura

```
NemesisCatchState
   → (si estás escondido) te saca: hiddenPullOutTime (0.8 s) parado en la puerta
   → PlayerStateManager.OnCaptured()
      → PlayerEvents.OnPlayerCaptured
         → CheckpointManager  (carga el checkpoint)
         → CaptureFadeView    (fade a negro)
   → espera captureGracePeriod (4 s)
   → NemesisStateManager.RepositionAfterCapture()
   → NemesisEvents.OnCaptureResolved  (recién ahí se levanta el negro)
```

El Nemesis **nunca** llama al guardado ni a la UI. Sólo llama a `OnCaptured()` y levanta eventos.

**Fuera del escape, una captura es un costo, no un game over**: volvés al checkpoint. Si no hay a dónde volver, `CheckpointManager` cae solo a la pantalla de derrota. En el escape de Zona1 sí es game over (ver abajo).

Dos cosas que importan para el diseño:

- **Período de gracia.** Después del checkpoint el Nemesis espera 4 s antes de volver a razonar, para que no te agarre en el frame en que reapareciste.
- **Se reposiciona.** Vuelve a un spawn point o waypoint al azar a más de `repositionMinPlayerDistance` (15), nunca al lugar donde te agarró. Si no, el checkpoint y la captura quedan pegados y es el mismo punto de tensión en loop.

`catchMaxReach` es 1 m, `catchMaxVerticalOffset` 1 m, y `catchRequiresLineOfSight` está prendido: no te agarra a través de una pared aunque el agente esté al lado, ni entre pisos.

**Cuando la captura no sale.** Si entró a `Catch` sin nadie a quien agarrar, vuelve a `Searching`. Si `OnCaptured()` no la toma —una cinemática tiene al jugador congelado— vuelve a `Chasing`, en vez de quedarse esperando un respawn que no va a llegar. Lo mismo si durante el `hiddenPullOutTime` saliste del escondite y ya no estás al alcance. En todos los casos `catchCooldown` (2 s, en el `NemesisStateManager` del prefab) impide que te vuelva a agarrar en el frame siguiente.

---

## En el escape de Zona1

En Zona1 el Nemesis sólo existe acá. La secuencia vive en `Scripts/Escape/` y no se documenta en este archivo; lo que cambia del lado del Nemesis:

- **Cómo aparece.** `NemesisCinematicActor.TryTakeControl()` lo despierta con `ActivateInPlace()` y **apaga** su `NemesisStateManager` mientras lo maneja entre marcas (no usa `SetExternalHold`, porque el FSM escribe destino y velocidad cada frame). `Release()` se lo devuelve a su FSM.
- **No baja de `Chasing`.** `NemesisEscapePursuit` prende `NemesisDecision.ChaseFloor`: cualquier respuesta de la escalera por debajo de `Chasing` (patrulla, investigación, búsqueda) se convierte en `Chasing`, pero `Catch` y `Traversing` siguen ganando. En F9 la regla dice *"escape: no baja de Chasing"*.
- **No te pierde.** Con `SO_EscapeSequenceConfig.PerfectTracking` le refresca la creencia (`FieldOfView.InjectSighting`), así que la niebla no se lo saca de encima. No enciende `HasVisualTarget`: no es un avistamiento de verdad.
- **Otra velocidad.** Mientras persigue, su velocidad deja de ser `chaseSpeed`: se mide contra lo que vale el sprint del jugador en ese momento (con las penalidades de módulo) y contra la distancia, con los números de `SO_EscapeSequenceConfig`.
- **Captura = game over.** `SO_EscapeSequenceConfig.CaptureOutcome` está en `GameOver`: `EscapeChaseRestart` apaga el `CheckpointManager` y, después de dejar ver el agarre, reporta la derrota. La otra opción, `RestartChase`, reinicia la persecución desde su propio checkpoint.
- **Sin Director.** `NemesisTension` queda suspendido con el Nemesis dormido, durante una cinemática y con `ChaseFloor` prendido, así que en Zona1 el ritmo del Director nunca corre (ver abajo).

---

## Director y ritmo

`NemesisDirector` pone al Nemesis donde está la tensión **sin tocar nunca el FSM**: no lo mete en `Chasing`, no le pasa tu posición, no le saltea un rango ni una gracia. Va uno por escena (es un `Singleton` del nivel) y usa cuatro palancas, de menos a más notoria:

1. **Ancla de patrulla**: el sesgo de zona de la patrulla apunta al centro de la zona presionada en vez de a vos.
2. **Pesos de ruta**: las rutas desbloqueadas con algún waypoint dentro de la zona multiplican su peso hasta ×`routeWeightBoost` (3).
3. **Ruido sintético**: cada `noiseInterval` (9 s), un emisor de radio 4 que vive 0.8 s en la capa `DetectableAudio` (tiene que estar en el `listenMask` del Nemesis, o no existe para nadie).
4. **Sentidos**: una copia en runtime de `SO_NemesisData` con oído y vista ×`sensoryBoost` (1.25). Nunca modifica el asset.

Aparte, la **entrada tipo Mr. X**: aparece a 10–22 m por NavMesh, fuera de tu vista, y se queda `entranceStareSeconds` (2.5 s) mirándote antes de moverse. Sale sin hacer nada si el Nemesis no está activo.

Las zonas son `NemesisPressureZone` (un id y un radio, 12 por defecto). Piden presión los `Puzzle Triggers` del Director, la API estática (`RequestPressure`, `ReleasePressure`, `RequestEntrance`), el ritmo y F10. Un pedido nuevo reemplaza al anterior, y los disparadores por puzzle siempre le ganan al ritmo.

**Ritmo** (`SO_DirectorPacing`, asignado en el Director): `NemesisTension` (se agrega solo junto al Director) lleva un medidor de 0 a 1 que sube con la proximidad del Nemesis, la persecución, que vos lo veas a él y estar escondido con el Nemesis buscando cerca; baja 0.03/s después de 4 s sin estímulos, y nunca en `Chasing` ni `Catch`. En 0.85 pasa a `SustainPeak` (3–5 s), después a `PeakFade` (suelta la presión que había puesto el propio ritmo y espera a que el encuentro termine solo) y después a `Relax` (30–45 s: la patrulla se inclina hacia la zona más lejana a vos, sólo con ancla y pesos). En `BuildUp`, tras `quietTimeout` (90 s) sin contacto —sin contar el tiempo en el Hub—, presiona la zona donde estás: 0.3, +0.15 cada 20 s, hasta 1.

**En Zona1 está armado pero inerte**: 6 zonas (`montacargas`, `panel electrico`, `valvulas`, `fondo norte`, `ala oeste`, `centro este`) y dos disparadores (`sp2_contenedores` → `fondo norte` 0.5 / 45 s; `sp3_valvulas` → `valvulas` 0.7 / 60 s + entrada), pero con el Nemesis dormido no hay a quién mover, y el ritmo está suspendido (ver *En el escape de Zona1*). Receta de armado, estado en la escena y errores comunes: plan §14.1–14.3.

---

## Audio

Tres caminos independientes al mixer. **Ninguno de los tres es intercambiable con los otros** — la regla es: *loops continuos que comunican estado* van por `NemesisAudio`; *one-shots posicionales* van por el pool del `AudioManager`; *señales de score* van por el bus Music.

| Sonido | Componente | Bus | Estado |
|---|---|---|---|
| Pasos | `FootstepEmitter` en la raíz del prefab | Nemesis | Funciona |
| Respiración por estado | `NemesisAudio` (en el prefab; se agrega solo si falta) | Nemesis | Funciona |
| Voz por estado | `NemesisAudio` | Nemesis | **Falta enganchar** |
| Música de persecución | `NemesisChaseMusic`, objeto suelto en la escena | **Music** | Funciona |
| Puertas que abre | `DoorInteractable.AnimateOpen/Close` | SFX | Funciona |
| Stinger de captura | — | — | **No existe** |
| Cue de activación | El escape, cuando arranca a correr (`SO_EscapeSequenceConfig.revealSoundId`) | Nemesis | **Falta el clip** |

La música de persecución no se corta cuando te pierde de vista: sigue durante la búsqueda que viene después (también en `Traversing`) y termina cuando termina la búsqueda, así el silencio quiere decir "dejó de buscar" y no "dejó de verte" (decisión D5 del plan). `searchTailTimeout` (25 s) es la red de seguridad.

Cuando la partida tiene resultado (`GameResultManager.OnGameResult`), los loops de `NemesisAudio` y la música se desvanecen en tiempo unscaled, para no quedar sonando congelados sobre la pantalla de resultado.

### Pasos

El Nemesis usa el mismo `FootstepEmitter` que el jugador, con `bus = Nemesis` y `cadenceSource = AnimationEvent`. Los pasos los dispara un `Step` puesto en el frame de apoyo de la animación, que llega por `FootstepAnimationRelay` (está en el hijo con el Animator, no en la raíz).

> **El Animator del Nemesis usa las animaciones del jugador** (`Walking`, `Running`, `Idle` de `Player Animations/`, en `NemesisController.controller`). Son las únicas del proyecto que tienen el evento `Step`. Consecuencia: el `strideLength` del prefab está inerte, y la cadencia son los dos ritmos del jugador conmutados por los bools `isWalking`/`isRunning` — no sigue la velocidad real de `SO_NemesisMovement`.

**Los clips también son los del jugador**: el emisor del prefab usa `SO_FootstepBank_Player` (resuelve por superficie, no por estado) con `pitchMultiplier` 0.72, más grave para distinguirlo de oído, rolloff logarítmico entre 1.5 y 24 m, y oclusión por `Wall` que atenúa a 0.5. `SO_FootstepBank_Nemesis`, con los `pasos_chase_*` propios, sigue en el proyecto pero no lo usa nadie. Los pasos no dicen en qué estado está; eso lo dicen la respiración y la música.

### `NemesisAudio`

Se agrega solo a cualquier Nemesis que no lo tenga, pero el contenido —el array `stateLoops`, una entrada por estado con clip y volumen— se autora en el prefab. Un estado sin entrada hace crossfade a silencio; si el array está vacío avisa una vez por consola al arrancar.

Hoy el prefab tiene respiración: `breathing_patrol` en `Patrolling`, `breathing_search` en `Investigating` y `Searching`, `breathing_chase` en `Chasing` y `Catch`; `Traversing` no tiene entrada. Las voces (`voice_chase`, `voice_lost_01/02`, en `Audio/SFX/Nemesis/`) no las reproduce nada: los dos `SO_sfx_nemesis_voice_lost_*` están registrados en el `AudioManager` pero ningún código los pide.

Crossfade de 0.4 s entre estados, `spatialBlend` 1 (3D puro), y oclusión que **atenúa, nunca corta** (`occludedVolumeMultiplier`: 0.35 en el código, 0.5 en el prefab): que el monstruo desaparezca del audio apenas se mete detrás de una columna es peor información que que se escuche de más.

---

## Escalada de dificultad — no implementada

El spec §7.2 pide que el Nemesis se ponga más agresivo a medida que se completan módulos. **No está construido**, y el propio spec lo marca como pulido diferido: pide mantener los valores base constantes en la primera iteración.

Dos cosas quedan decididas de antemano para cuando se construya:

- **Cuenta puzzles, no módulos.** `ModuleManager` son los timers de los dispositivos y nunca avanza la historia. La espina de progresión de este proyecto es completar puzzles: es lo que desbloquea rutas, despierta al Nemesis y arma los checkpoints.
- **Tiene que leer un contador, no sumar eventos.** `PuzzleStateManager.RestoreSnapshot` rellena los puzzles resueltos **sin** emitir `OnPuzzleCompleted`, así que algo que cuente eventos volvería de una partida guardada creyendo que el jugador recién empieza.

Y una trampa que ya está desarmada: cuando esto se implemente, va a cambiar la sintonía del Nemesis de forma **permanente**, mientras que el boost sensorial de `NemesisDirector` la cambia de forma **temporal** y la devuelve. El Director ya lee su punto de retorno desde `NemesisStateManager.BaselineData` en vez de cachearlo, justamente para que las dos cosas se compongan en lugar de pisarse.

---

## Los ScriptableObjects

En `ScriptableObjects/Nemesis/`:

| Asset | Qué contiene |
|---|---|
| `SO_NemesisData` | Todo lo que no es velocidad: rangos, tiempos, umbrales, sesgos de ruta, cúmulos, captura, persecución estancada, investigación, escondites. |
| `SO_NemesisMovement` | Velocidades por estado + tuning del `NavMeshAgent` (angular 160, aceleración 14, stopping 1) + el movimiento a mano cuando el agente está apagado (links 2.5, subir y bajar del montacargas 1.5, giro 180). |
| `SO_NemesisPriorities` | La escalera de prioridades. Reordenable. Incluye `minimumStateDwell` (0.35 s): la histéresis que evita que dos peldaños se lo pasen ida y vuelta cada frame. |
| `SO_DirectorPacing` | El ritmo del Director (ver *Director y ritmo*). Lo lee `NemesisDirector`, no el Nemesis. |

Fuera de esa carpeta pero leídos del lado del Nemesis: `SO_HidingData` (`ScriptableObjects/Hiding/`) y los tres de señuelos (`ScriptableObjects/Decoys/`).

Los `LayerMask` **no** están en los SO: viven en los componentes, porque son cableado de escena y no valores de diseño.

> **Campos nuevos y el asset.** Un campo agregado a `SO_NemesisData` no figura en el `.asset` hasta que alguien lo guarda desde el editor, y mientras tanto toma el valor inicial del código. El 22/09 se escribieron en el asset, con esos mismos valores iniciales, los nueve que faltaban: `stuckRepathGrace`, `spawnMinPlayerDistance`, `spawnSafeHalfAngle`, los tres `investigation*`, `underTableVisionMultiplier`, `seenEnteringWindow` y `hiddenPullOutTime`. Al agregar un campo, guardá el asset (o escribilo) para que el valor de diseño quede a la vista en el diff.

---

## Cómo verificar

**Escena de pruebas**: abrí `Scenes/Dev/NemesisTestbed.unity` (lista de chequeo en `docs/Checklist-NemesisTestbed.md`). `Tools > Nemesis > Build Bug Lab (NemesisTestbed)` le arma estaciones que reproducen los bugs de QA que necesitan geometría (escalera con puertas, pilares, balcón inalcanzable, trigger en una puerta). Para escondites, `Scenes/Dev/TestIñaki.unity` tiene la *Hiding Test Area*: los tres tipos, con su propio Nemesis.

**En Play** (las dos teclas funcionan sólo en el editor):

| Tecla | Qué abre |
|---|---|
| `F9` | HUD de debug (`NemesisDebugHUD`, está en el prefab): estado y la regla que ganó, sospecha, escondite conocido, creencia, distancia recta y por NavMesh, progreso de la persecución (`ChaseStalled`), búsqueda, cúmulo, agente, trabas, ritmo y presión del Director, y "seguro en": segundos desde la última detección hasta volver a patrullar. |
| `F10` | Consola de test (`NemesisTestConsole`, hoy en la testbed y en `TestIñaki`; en otra escena se agrega a mano al Nemesis): armar situaciones (Nemesis detrás o delante tuyo, vos encima de él, escondido, captura), la sección del Director (un botón por zona, *Release*, *Staged entrance*, pico de tensión, saltar el silencio). |
| `1`–`6` / `0` | Con la consola en la escena, aunque esté cerrada: fija el estado que responde la escalera (Patrol, Investig, Chase, Search, Traverse, Catch); `0` o la misma tecla lo suelta. |

**Registro**: `NemesisTraceRecorder` (se agrega solo; editor y development build) escribe un CSV por sesión en `Logs/NemesisTrace/` (en un development build, en `persistentDataPath/NemesisTrace`): regla ganadora, sentidos, estado del camino y velocidad real, cada 0.25 s y en cada cambio de estado. La ruta sale una vez por consola. Se apaga con `record` en el componente.

**Gizmos** (`NemesisGizmos`): se dibujan siempre, no sólo con el Nemesis seleccionado, con un toggle por bloque y un interruptor maestro `drawGizmos` que también apaga las rutas. Conos de visión a escala (normal, agachado, bajo mesa, foco), proximidad, oído, los tres radios de ruido del jugador por paso, alcance de captura, barrido de búsqueda y de cuarto, lo que sabe de escondites, el rastro de la persecución, el punto predicho, el de flanqueo y el de intercepción. En Zona1, el `GizmoManager` de la escena (`Scripts/Managers/GizmoManager.cs`) oculta gizmos por familia; un script nuevo que dibuje gizmos va en su `Families()`, no con un bool propio.

**Validación de nivel**:

- `Tools > Nemesis > Validate Navigation Setup` reporta geometría que se quedó afuera del bake, máscaras mal puestas, waypoints sin tag o fuera del NavMesh, y modifier volumes que el bake descarta.
- `Tools > Player > Validate Hiding Spots`, ver *Escondites*.

El `NavMeshSurface` de Zona1 hornea Default + Ground + Wall + Props; el de la testbed, Ground + Wall + Props (a propósito).

---

## Errores comunes

**"El Nemesis nunca aparece."** En Zona1 es lo esperado: `Wake Only From Script` está prendido y sólo lo despierta el escape. En otra escena, ningún spawn point pasa los tres tests. Mirá la consola, dice cuál falla. Casi siempre están todos demasiado cerca de donde termina el puzzle de activación.

**"Se queda trabado contra una esquina."** Hay un watchdog (`NemesisStuckEscape`) que escala: primero recalcula el camino y le da `stuckRepathGrace` (1.5 s), después lo teletransporta a un waypoint fuera de la vista del jugador, desde el que pueda seguir hacia donde iba y a 3 m o más de donde se trabó. Si pasa seguido en un lugar concreto, es geometría, no IA — corré el validador.

**"Ignora un NavMeshLink que puse."** Si está en el área `Jump` es a propósito: ahí caen los links que genera el bake solo, y el Nemesis no la usa (`NemesisLifecycle` la saca de su `areaMask`; atravesaba columnas por esos links, WIR-028). Un link autorado para él va en otra área.

**"La consola dice que un collider SÓLIDO está en una capa que escucha el Nemesis."** Es geometría en la capa de ruido (`DetectableAudio`). Se ignora, pero así también queda afuera del bake y de la máscara de visión: pasala a Wall, Props o Default.

**"No hace ningún ruido."** O `stateLoops` quedó vacío (te lo avisa por consola), o estás en una escena abierta sola — todo el audio del proyecto depende de que esté cargada la escena `Data`. Dale Play desde `Bootstrap`.

**"Me encontró escondido sin haberme visto entrar."** Revisá, en orden: si te acercó a 1.5 m (proximidad extrema), si estabas en un locker con él del lado de la puerta a menos de 2.45 m o bajo una mesa a menos de 3.5 m el tiempo suficiente para llenar el medidor, y si lo trajiste con ruido (la respiración se oye a unos 2 m, la exhalación a unos 6): el ruido lo lleva hasta ahí y la proximidad hace el resto.

**"Toqué el enum de estados y se rompió todo."** Insertaste en el medio en vez de al final. Ver arriba.

**"Cambié un número en el SO durante Play y quedó guardado."** Sí, Unity hace eso. Los cambios a ScriptableObjects en Play mode persisten al asset.

---

## Pendiente

| Qué | Quién |
|---|---|
| Enganchar la voz (`voice_chase`, `voice_lost_01/02`): la respiración ya está en `stateLoops`, la voz no la reproduce nada | Audio + código |
| Decidir qué hacer con `SO_FootstepBank_Nemesis` y los `pasos_chase_*`: quedaron sin uso desde que los pasos usan el banco del jugador | Audio |
| **Conseguir dos clips**: el stinger de captura y el cue de activación — ver abajo | Audio |
| Marcar superficies con `FootstepSurface` en el blockout | Nivel |
| Definir qué es la cinemática de captura del spec §5.6, y la animación de sacar al jugador de un escondite | Diseño |
| Poner `NemesisDecoyBreaker` en el prefab antes de colocar radios en un nivel (ver *Señuelos*), y los ids de sonido de los tres señuelos | Nivel + audio |
| Escalada de dificultad del spec §7.2 — diferida a propósito, ver arriba | Diseño + código |

### Dos `SO_SoundData` esperando clip

Están creados y ya enganchados al `AudioManager`, pero con el campo `clip` vacío. **Reservan el id** para que el código que los dispara se pueda escribir contra ellos; hasta que alguien arrastre un clip suenan silencio, sin error y sin warning.

| Asset | id | Para qué |
|---|---|---|
| `SO_sfx_nemesis_captura_stinger` | `sfx_nemesis_captura_stinger` | El impacto sonoro que cierra el intento (spec §5.5) |
| `SO_sfx_nemesis_activacion` | `sfx_nemesis_activacion` | El momento en que el Nemesis entra al juego (spec §7.1). Ya tiene quien lo dispare: el escape, cuando el Nemesis arranca a correr |

Llenarlos es arrastrar el clip al campo `Clip`, nada más. El id, la categoría (Nemesis) y el rango 3D ya están puestos.

Los de la voz de «te perdí» (`SO_sfx_nemesis_voice_lost_01` y `_02`) sí tienen clip y están registrados, pero todavía no los pide ningún código.
