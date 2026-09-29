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

Hoy son 21 peldaños, y el asset coincide con la escalera por defecto de `SO_NemesisPriorities.BuildDefaultLadder()`, donde cada uno tiene comentado por qué está en ese lugar (incluidos los de escondites: "sabe en qué escondite está", "está revisando un escondite", "sospecha de un escondite"; y los de pistas, desde el 27/09: "oye un señuelo u otro ruido", "sigue yendo hacia la pista"). El peldaño que ganó en cada frame se ve en F9, fila *regla*.

Los seis estados:

| Estado | Qué es | Velocidad |
|---|---|---|
| `Patrolling` | Recorre cúmulos de waypoints. Espera en cada uno. | 2.75 |
| `Investigating` | Escuchó algo, vio algo de reojo o sospecha de un escondite, y va a ver. Al llegar se queda `investigationDwellTime` (4 s) mirando. | 2.5 |
| `Chasing` | Te ve. Persecución activa con predicción e intercepción. Si te pierde, corre hasta donde te vio por última vez. | 3.0 |
| `Searching` | Te perdió pero sabe por dónde andabas. Barre la zona; si sabe en qué escondite estás, va a ese. | 2.75 |
| `Traversing` | "Para llegar necesito el montacargas" (o una bajada, ver *Bajadas entre pisos*). | 3.0 |
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

**Jugador o pista (desde el 27/09, plan §17).** El oído distingue el ruido del jugador (su emisor: pasos, respiración) de todo lo demás (señuelos, pulsos del Director). Sólo el del jugador mueve la **creencia** sobre dónde está; lo demás es una **pista**: puede hacerlo ir a investigar si la **elección** la prefiere (ver *A qué le presta atención*), pero no rejuvenece la creencia ni le cambia el objetivo a una persecución. La creencia junta vista y oído en una posición con un radio: con evidencia que coincide se achica (los sentidos suman), sin evidencia crece a la velocidad del jugador corriendo. Se ve en F9, fila *creencia* (vista u oído, antigüedad, radio y pista activa), y se tunea en `SO_NemesisData` › *Creencia*.

Que los pisos atenúen en vez de cortar es deliberado: es el único canal que tiene el Nemesis hacia el piso de arriba.

Sólo cuentan los **triggers**. Un collider sólido en una capa que el Nemesis escucha es geometría en la capa equivocada (el `Stair_Divider` de Zona1 convertía la escalera en un ruido permanente, WIR-018/020): se ignora y la consola lo nombra una vez. Los señuelos son un segundo canal, aparte de las esferas — ver *Señuelos*.

> **Si algo tiene que "hacer ruido", tiene que durar.** Prendé el emisor al radio que quieras y dejalo más de 0.1 s. Un pulso de un frame puede caer entre dos barridos y no lo escucha nadie. Y devolvé el radio a donde estaba: el emisor lo comparten los estados de movimiento, y un radio olvidado deja al jugador permanentemente ruidoso sin que nada tire error.

### Proximidad extrema

`proximityDetectionRange` = 1.5, en horizontal y con el mismo tope de altura que la captura (`catchMaxVerticalOffset`, 1). A esa distancia te detecta **aunque estés escondido**, y existe para que un escondite pegado al Nemesis no sea un exploit. Respeta paredes (`proximityDetectionRespectsWalls`), pero no la carcasa del escondite en el que estás. Si estás escondido no arranca una persecución: marca ese escondite como conocido y el Nemesis va a sacarte (ver *Escondites*). **Salvo que estés aguantando la respiración** dentro del escondite: ahí la proximidad no te detecta (plan D21).

`proximityRadius` (12) es otra cosa: alimenta la viñeta de proximidad del HUD, no la detección. Se mide por NavMesh (`proximityUsesPathDistance`), para que no se prenda con el Nemesis en otro piso.

---

## Escondites

Código en `Scripts/Hiding/` (`HidingSpot`, `HidingEvents`, `EHidingSpotType`), un solo asset para todos en `ScriptableObjects/Hiding/SO_HidingData.asset`, prefabs en `Prefabs/HidingSpotFather/` (un padre y las variantes Locker, UnderTable y Container). Hoy sólo hay escondites colocados en `Scenes/Dev/TestIñaki.unity`.

**Del lado del jugador.** Entrar tarda `enterDuration` (0.6 s) con el jugador quieto y **completamente visible**: esa ventana es la que le permite al Nemesis "verte entrar". Adentro, `PlayerHiddenState` respira prendiendo el emisor de ruido: radio 0.8 cada 3 s (unos 2 m de alcance). Mantener **F** (`HoldBreath`) corta la respiración; soltarla larga una exhalación de radio 2.0 (unos 5 m), peor que no haber aguantado. El aire dura `maxHoldSeconds` 8 (asset y código) y se recupera en `breathRecoverySeconds` (5 s). **Aguantando, el Nemesis no te detecta ni por proximidad ni por las rendijas**: llega a donde oyó algo, mira a los lados y se va. Si te quedás sin aire con él cerca, la exhalación lo trae de vuelta. Por eso el tope ya no es opcional: sin él, aguantar sería inmunidad. En el container los dos radios van ×0.5. El emisor es el mismo de los estados de movimiento, y al salir se devuelve como estaba.

**Lo que ve el Nemesis de alguien escondido** (`FieldOfView.HiddenViewRange`):

| Tipo | Hasta dónde te distingue | Condición |
|---|---|---|
| Locker | `viewRange` × `lockerVisionExposure` (0.35 → 2.45 m) | Sólo desde el lado de la puerta, por las rendijas |
| UnderTable | `viewRange` × `underTableVisionMultiplier` (0.5 → 3.5 m) | La mesa acorta la vista, no ciega |
| Container | nada | Sellado |

Eso **nunca** es detección inmediata: pasa por el acumulador de la periferia. Mientras aguantás la respiración, no acumula. Pasado `awarenessTriggerThreshold` el escondite queda *sospechado* y va a mirar (`Investigating`); con el medidor lleno queda *conocido*.

**Lo que sabe** (`NemesisHidingAwareness`, se agrega solo al Nemesis):

- **Conocido**: al terminar tu subida, si te estaba viendo o te vio en los últimos `seenEnteringWindow` (0.75 s), con el escondite dentro de `viewRange` y su puerta en línea de vista ("lo vio entrar"). También si el medidor se llenó mirando a través del escondite, o por proximidad extrema.
- **Sospechado**: te tenía de reojo, con el medidor pasado el umbral, cuando te metiste.
- **Se olvida** cuando lo revisa y está vacío, cuando te ve afuera, con una captura o un respawn, y como red de seguridad cuando nada lo confirma por más de `searchTimeOut` (conocido) o `investigationTimeOut` (sospechado).
- Salir sin que te vea no se le informa: va, encuentra vacío y se olvida. Puede equivocarse; no es omnisciente.

**Cómo te saca.** "sabe en qué escondite está" lo manda a `Searching`, que camina al `ApproachPoint` del escondite. El alcance del agarre se mide contra ese punto, y sólo para un escondite que conoce: pasar por delante de un locker que no sabe ocupado no saca a nadie. Un escondite que **sospecha o conoce**, en cambio, lo **abre** al llegar (en `Searching` y en `Investigating`): si estás adentro, te saca aunque aguantes la respiración. Antes de llamar a `OnCaptured()` se queda `hiddenPullOutTime` (0.8 s) parado en la puerta — todavía no hay animación propia, se reproduce la del agarre. No es una ventana para escapar: salís al lado de la puerta, al alcance.

**Armado**: el header de `HidingSpot.cs` tiene la jerarquía (`InteriorPose`, `ApproachPoint`, `ExitPose`, cámara interior) y el plan §14.4 dónde va cada pieza. `Tools > Player > Validate Hiding Spots` reporta:

- un `SpotId` vacío o repetido;
- un `ApproachPoint` fuera del NavMesh o a más de `catchMaxReach` del interior;
- desde el 28/09, una `ExitPose` a más de 0.75 m del `ApproachPoint`: el alcance del agarre menos lo que frena el Nemesis en la puerta. Más lejos, salir mientras te saca sería un escape gratis (D16);
- una `ExitPose` adentro de un collider sólido o sin piso abajo.

---

## Señuelos

Código en `Scripts/Decoys/`, datos en `ScriptableObjects/Decoys/`, prefabs en `Prefabs/Decoys/` (`Decoy_Radio`, `Decoy_FireAlarm`, `Decoy_Chains`). En la testbed los pone *Tools/Nemesis/Build Decoy Stations (NemesisTestbed)*: la radio en SALA_LATERAL, la alarma en PASILLO_CARGA y las cadenas en PASILLO_OESTE, con carteles. En Zona1 todavía no hay ninguno.

Un señuelo es un `DecoyNoiseSource` más el componente que decide cuándo suena. No es una esfera en la capa de escucha: `FieldOfListening` los lee de un registro propio (`DecoyNoiseSource.Active`), porque un señuelo dice en metros reales hasta dónde se oye —sin `noiseRangeScale` y sin el tope de `listenRange`— o que se oye desde cualquier lado. Paredes y pisos lo atenúan igual que al jugador. Uno que se oye en todos lados compite con margen 0: un ruido de verdad cerca le gana. El Nemesis va al `investigatePoint` proyectado al NavMesh; ponelo en el piso, del lado desde el que tiene que llegar.

| Señuelo | Usos | Se oye | Tuning (asset) |
|---|---|---|---|
| `RadioDecoy` | 1 | a 12 m | Suena hasta que la rompen (`maxPlayTime` 0). La rompe a 2.5 m: 1.2 s de corte enojado hasta el golpe, 1 s quieto después |
| `FireAlarmDecoy` | 1 | desde cualquier lado | Suena 10 s después de activarla, durante 30 s, y abre los rociadores |
| `ChainDecoy` | infinitos | a 8 m | 1.5 s de ruido, 2 s de enfriamiento |

La rotura la hace `NemesisDecoyBreaker`, que **se agrega solo** a la raíz del Nemesis desde el 28/09 (antes no estaba en ningún lado y la radio no se rompía nunca). Sólo rompe el señuelo que es su **foco**, no cualquiera por el que pase; si te ve durante el corte, lo abandona y la radio sigue sonando. Los tres assets tienen los ids de sonido vacíos.

### A qué le presta atención (plan §17.4, Fase 2B parte 4)

`NemesisChoice` (se agrega solo) elige un **foco**: vos, una pista (un señuelo, un pulso del Director) o un vistazo de reojo. Cada vez que llega algo nuevo —nunca por tener un sentido prendido— le pregunta a `FocusArbiter` si vale más que lo que está persiguiendo:

- **Valor** = base × confianza (el radio de la evidencia) × frescura (se reduce a la mitad cada 6 s) × costo de llegar × habituación. Las bases: vos 1, alarma 0.7, radio 0.6, cadenas 0.45, otro ruido 0.4 y vistazo 0.5. Todo está en `SO_NemesisData` › *Elección*.
- **Verte gana siempre y al instante.** Mientras rompe la radio o abre un escondite no cambia, salvo que te vea.
- **Lo mismo no es cambio**: el mismo señuelo, vos otra vez, o algo del mismo tipo a menos de 3 m actualizan el foco sin cortar lo que hacía.
- **Lo nuevo tiene que ganar por un margen** (0.05). Lo recién elegido tiene una ventaja de 0.3 que se va en 3 s, y lo que tiene a menos de 4 m, 0.15 más. Además, dos pistas seguidas no lo hacen cambiar de una a otra en menos de 2 s.
- **Habituación**: cada vez que un señuelo lo hace ir sin encontrar nada, vale ×0.6 para el resto de la sesión. Debajo de 0.12 no le presta atención. Por eso unas cadenas del otro lado del nivel lo mueven dos veces y a la tercera ya no.
- **Un señuelo donde ya te está buscando suma**: sigue barriendo ahí.
- **Un escondite que sospecha o conoce le gana a cualquier señuelo.**

Cuando cambia de idea se frena ~0.4 s girando hacia lo nuevo antes de caminar. F9, fila *foco*: qué es, cuánto vale, hace cuánto y la última decisión con su pregunta (`cambió: radio 0.36 > vos 0.28 [11]`).

**Sospecha compartida.** Un ruido **suave** tuyo (agachado) sube el mismo medidor que un vistazo: juntos lo ponen en sospecha más rápido. Un ruido solo nunca llega a ser un avistamiento.

**Escondites que usaste.** Cuando busca o investiga una zona, sortea los escondites que usaste **ahí**: el más usado primero, con la chance del medidor de la Fase 3 (×0.25, tope 0.85). Si sale uno, lo sospecha y va a abrirlo. Nunca cruza el nivel por uno. La primera vez sólo pasa si estás a ≤ 12 m, para que lo veas u oigas. Y un segundo ruido desde el mismo escondite (una exhalación, un suspiro) lo vuelve sospechoso: va y lo abre.

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

Fuera del Nemesis también los leen el tope de emisión del regulador de presión fuera del Hub
(`SocketEmissionShift.dimOutsideSafeZone`) y la alerta `|| Safe Zone ||` que aparece cada vez que el
jugador entra (`SafeZoneAlert`, objeto `Safe Zone Alert` bajo `---- SISTEMA ----`). Sacar un marker
también los apaga para ese volumen.

Ojo con la capa del volumen: `NavMeshSurface` filtra los modifier volumes por sus Include Layers, así que uno en una capa que el surface no recolecta se descarta sin avisar y el Nemesis entra. `Validate Navigation Setup` lo reporta.

---

## Persecución

**Te pierde de vista → va a donde te vio.** Sin la vista no predice ni flanquea: corre al último punto donde te **vio** —no al último ruido: el que corta la línea de visión y sigue corriendo se oye hasta el locker— y recién al llegar pasa a `Searching`. Tope de 10 s (peldaño "va a donde lo vio por última vez"). La búsqueda arranca ahí mismo, parado `searchPauseTime` (1.2 s) mirando alrededor, salvo que después te haya oído más allá: entonces va directo ahí.

**Cómo busca (desde el 27/09, plan §18, Fase 2B parte 2).** Barre puntos del NavMesh alrededor de la creencia, no waypoints:
- **Primero el punto.** Va al punto de la evidencia —donde te vio o te oyó por última vez— y recién después barre alrededor. También cuando te oye en un lugar del disco donde todavía no miró. No con un ruido desde un escondite (D22): ese punto es la puerta del locker. F9: "yendo al último punto".
- **El disco.** El radio sale de la precisión de la última evidencia: `searchSweepMinRadius` (3 m) para un avistamiento; más ancho para un paso a través de una pared; ×2 (`beliefNoiseHidingSpotFactor`) para una respiración que sale de un escondite. Se le suma `searchSweepEvidenceMargin` (1 m) y el tope es `roomSweepRadius` (8).
- **Cuando lo cubre**, se abre de a 2.5 m hasta el tope.
- **Te oye adentro del disco:** corre el centro sin cortar lo que está haciendo.
- **Te oye afuera:** re-centra el disco ahí, sin olvidar lo que ya barrió.
- **Los señuelos y el ruido del Director no mueven el barrido.**
- **Sin creencia** barre `searchSweepRadius` (5 m) alrededor de donde está.
- **La habitación.** Si te vio entrar a un cuarto, lo barre primero mientras le queden puntos sin mirar. Qué es "el cuarto" lo decide `NemesisRooms` por el collider del piso: en Zona1, el nombre `<CUARTO>_Floor_<n>`.

La intercepción (cortarte el paso en un waypoint) se sacó (D24).

**Cuánto busca (desde el 27/09, plan §18.5 B, Fase 2B parte 3): se enfría, no se vence.** Ya no son 15 s fijos. Sigue buscando mientras:
- **Mínimo:** lleva menos de `searchMinTime` (6 s). Siempre mira un poco, y queda por debajo del aire que aguantás (8 s).
- **Silencio:** o, si no, mientras el silencio es menor que `searchQuietWindow` (8 s) × la calidad de esa evidencia: ×1.25 si te vio (`searchQualitySight`) y ×0.75 si te oyó a través de una pared, un piso o un escondite (`searchQualityMuffled`). El silencio cuenta desde tu última evidencia o desde que llegó a ese punto, lo que sea más tarde, y no corre mientras camina hasta ahí (F9: "tibia (sin contar)").
- **Tope:** nunca más de `searchHardCap` (30 s), aunque te siga oyendo. Al tope, si te oye, va a investigar.
- **Revisó todo:** termina antes si ya barrió todo lo que alcanza con el disco en su tamaño máximo.
- **Qué lo renueva:** cada paso o exhalación tuya pone el silencio en cero. Los señuelos y lo que oye desde adentro del Hub, no (C5).
- **Búsqueda corta (D26):** si investigó un ruido tuyo y no te encontró, pero el silencio (contado desde que llegó al ruido) es menor que la ventana, pasa a una búsqueda corta alrededor de la creencia, con el tope ×0.5 (`searchEscalatedCapScale`). Si lo que investigó era sólo un señuelo, no escala.
- **El Director** estira o acorta la ventana y el tope según el ritmo (persistencia).
- `searchTimeOut` sigue existiendo: es cuánto recuerda un escondite conocido.

**Donde no llega, no insiste.** Si tu posición no tiene camino completo (`IsBeliefUnreachable`, WIR-018), ni "lo está viendo" ni "va a donde lo vio" lo sostienen en `Chasing`: no se queda mirándote desde el borde del NavMesh.

**El loop de la mesa.** Corriendo, el jugador (4.5 m/s) siempre le gana al Nemesis (3.0), así que dar vueltas a un obstáculo no termina nunca. `NemesisChaseProgress` mide, por NavMesh, si acorta distancia: si en `chaseProgressWindow` (4 s) no bajó `chaseMinProgress` (1.5 m), la persecución queda estancada (`ChaseStalled`, en F9). Mientras tanto `NemesisPursuit` castiga los waypoints de desvío que están sobre el rastro por donde vino el jugador (×`chaseTrailPenalty` 0.2 dentro de `chaseTrailPenaltyRadius`, 3 m) y acepta desvíos más largos (`chaseStagnantDetourTolerance` 2.5), para que la ruta salga por el otro lado. **Nunca lo hace más rápido.** Si no hay waypoints cerca del obstáculo no hay otro lado que elegir: eso se arregla con waypoints, no con tuning.

---

## Bajadas entre pisos

El Nemesis puede **bajar** de un piso a otro por puntos que elige diseño: un hueco en el piso, una baranda rota, el borde de una pasarela. Es de un solo sentido: para subir sigue usando la escalera o el montacargas. Hay dos en la testbed, en el *Drop Lab* al sur de ENTRADA: una Hop de 2 m y una Hang de 3.6 m, con rampas de vuelta. En Zona1 no hay ninguna. Diseño completo: plan §15.

**Cómo se arma** (el header de `NemesisDropLink` tiene la receta):

```
Drop_<lugar>        ← NemesisDropLink. Agrega solo el NavMeshLink y los dos hijos. Estático, escala 1.
|-- TopEdge         ← sobre el NavMesh de arriba, a 0.3–0.5 m del borde
\-- BottomLanding   ← sobre el NavMesh de abajo, a 0.8–1.5 m de la vertical del borde
```

- **El link se configura solo en `Awake`** (un solo sentido, área `NemesisDrop`), y lo que se cargue a mano en el `NavMeshLink` se pisa.
- **El alto decide el tipo:** hasta `floorHeightThreshold` (2.5 m) salta (**Hop**); más alto, se descuelga (**Hang**). Va de 1.5 a 5 m.
- **Mira hacia donde está el aterrizaje**, así que la rotación de los hijos no importa.
- **Si el jugador no la tiene que usar**, una baranda en el borde en la capa `Ignore Raycast`. Choca con el jugador, y no la ven ni el horneado, ni los sentidos, ni el chequeo del arco. No en `Props`, que le tapa la vista al Nemesis, ni en `Player`, porque la máscara de objetivos de su vista la tomaría por el jugador.
- **Desde abajo tiene que haber vuelta**, por escalera o montacargas. *Validate Navigation Setup* revisa esto y lo del plan §15.6.

**Qué hace** (F9, fila `bajada`):

1. Llega al borde caminando y gira hacia el hueco (`dropAlignTurnSpeed`, 360°/s).
2. **Se asoma y gruñe** (`dropLookTime`, 0.6 s). Es el aviso: desde abajo se lo ve y se lo oye antes de que caiga.
3. *Hop:* flexiona (`hopTakeoffTime`, 0.35 s) y salta, subiendo `hopApexHeight` (0.3 m) para no rozar el canto. *Hang:* se da vuelta de espaldas al hueco con un golpe de manos (`hangTurnTime`, 0.75 s), se cuelga `hangDepth` (1.9 m) bajo el borde (`hangReleaseTime`, 0.4 s) y se suelta.
4. Cae en arco: `dropGravity` 12 m/s², y como mínimo `dropMinAirTime` (0.35 s) en el aire.
5. Aterriza con un impacto y **se queda `dropRecoveryTime` (0.75 s) sin poder agarrar a nadie**. Es la ventana del jugador: no agarra en el aire ni al aterrizar, aunque caiga al lado tuyo.

Mientras sigue en el piso (asomarse, flexionar, darse vuelta), una captura corta la bajada; desde que se tira o se descuelga, nada la corta. Si mientras se asoma **te ve en su mismo piso**, se echa atrás y te persigue arriba (plan D30). Si te ve abajo, por el hueco, se tira igual.

**Cuándo la usa:**

- **Costo según lo que hace** (plan D11). El área `NemesisDrop` cuesta `dropCostWhileHunting` (2) cazando, más barato que el montacargas, y `dropCostWhilePatrolling` (20) patrullando: patrullando sólo la usa si no hay otra ruta.
- **Cuenta como otro piso**, así que `Traversing` sostiene el camino hasta el borde como con el montacargas.
- **Al aterrizar suelta ese compromiso** y persigue (plan D29).
- **Después descansa:** la bajada queda `dropLinkCooldown` (8 s, en `SO_NemesisData`) fuera de las rutas, así no se tira en loop si das vueltas entre pisos.

**Animaciones y sonido provisorios:**

- **El controller no tiene los estados de la bajada:** `Drop Look`, `Hop Takeoff`, `Hang Turn`, `Hang Release`, `Fall Loop` y `Land Heavy`, con los nombres en `SO_NemesisMovement`. Cada fase sin estado se hace igual, sin animación, y la consola avisa una vez.
- **Sonidos** (`NemesisAudio`, sección *Bajadas*): `voice_chase` como gruñido y `pasos_chase_05` con pitch 0.85 como impacto. El golpe de manos está vacío.

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
4. **Sentidos**: una copia en runtime de `SO_NemesisData` con oído y vista ×`sensoryBoost` (1.25). El oído alarga tus ruidos y su tope juntos (`Noise Range Scale` y `Listen Range`), así que caminando te oye a ~12.5 m en vez de 10 (desde el 28/09; antes sólo subía el tope y caminando no se notaba). Nunca modifica el asset.

Aparte, la **entrada tipo Mr. X**: aparece a 10–22 m por NavMesh, fuera de tu vista, y se queda `entranceStareSeconds` (2.5 s) mirándote antes de moverse. Sale sin hacer nada si el Nemesis no está activo.

Las zonas son `NemesisPressureZone` (un id y un radio, 12 por defecto). Piden presión los `Puzzle Triggers` del Director, la API estática (`RequestPressure`, `ReleasePressure`, `RequestEntrance`), el ritmo y F10. Un pedido nuevo reemplaza al anterior, y los disparadores por puzzle siempre le ganan al ritmo.

**Ritmo** (`SO_DirectorPacing`, asignado en el Director): `NemesisTension` (se agrega solo junto al Director) lleva un medidor de 0 a 1 que sube con la proximidad del Nemesis, la persecución, que vos lo veas a él y estar escondido con el Nemesis buscando cerca; baja 0.03/s después de 4 s sin estímulos, y nunca en `Chasing` ni `Catch`. En 0.85 pasa a `SustainPeak` (3–5 s), después a `PeakFade` (suelta la presión que había puesto el propio ritmo y espera a que el encuentro termine solo) y después a `Relax` (30–45 s: la patrulla se inclina hacia la zona más lejana a vos, sólo con ancla y pesos). En `BuildUp`, tras `quietTimeout` (90 s) sin contacto —sin contar el tiempo en el Hub—, presiona la zona donde estás: 0.3, +0.15 cada 20 s, hasta 1.

**En Zona1 está armado pero inerte**: 6 zonas (`montacargas`, `panel electrico`, `valvulas`, `fondo norte`, `ala oeste`, `centro este`) y dos disparadores (`sp2_contenedores` → `fondo norte` 0.5 / 45 s; `sp3_valvulas` → `valvulas` 0.7 / 60 s + entrada), pero con el Nemesis dormido no hay a quién mover, y el ritmo está suspendido (ver *En el escape de Zona1*). Receta de armado, estado en la escena y errores comunes: plan §14.1–14.3.

---

## Hábitos del jugador (Fase 3 del plan)

`PlayerHabitTracker`, en la escena `Data`, cuenta lo que el jugador repite para escaparse y recuerda qué escondites usa. **Por ahora sólo cuenta: nada del juego reacciona todavía** (las contra-jugadas son la Fase 6). Esta etapa se juega con F9 abierto, para calibrar los umbrales con datos y no a ojo.

Es del lado del Director: sabe dónde está el jugador de verdad, pero sólo decide *qué* comportamientos existen, nunca *adónde* va el Nemesis. Sobrevive a la captura y al checkpoint, y se borra con New Game.

**Qué cuenta:**

| Qué | Cuándo |
|---|---|
| Escapó escondido (`EscapedWhileHidden`) | Una búsqueda termina sin encontrarlo, a ≤ 8 m por NavMesh de donde está escondido. Cuenta recién cuando la estadía se vuelve escape (ver abajo), una por cacería aguantada. Una cacería dura hasta que el Nemesis vuelve a patrullar: buscar, investigar un suspiro y volver a buscar es una sola. Con el *Hide* de F10 (sin escondite), cuenta en el acto. |
| Repitió escondite (`SameSpotReused`) | Un escape de un escondite del que ya se había escapado antes. |
| Persecución estancada (`ChaseStalled`) | Cada ventana de 4 s en la que el Nemesis persigue y no acorta distancia (el loop alrededor de una columna). |
| Escapó al Hub (`SafeZoneEscape`) | Una persecución termina con el jugador adentro del Hub, sin captura de por medio. |

**Cuándo una escondida es un escape.** Hacen falta tres cosas:

1. Mientras estaba adentro, el Nemesis cazó cerca: investigó, persiguió o buscó a ≤ 8 m por NavMesh, o terminó una búsqueda cerca.
2. Salió él. Si lo sacaron, si lo tomó una cinemática o si se descargó el nivel, no cuenta.
3. Pasaron 5 s sin que lo agarraran y sin persecución andando.

Salir justo cuando el Nemesis abre la puerta, o que te vea salir y te agarre en la persecución, es que te agarraron, no un escape. Esconderse "por las dudas" no se castiga.

**Medidor de cada escondite.** Esconderse en uno le suma 1 al entrar y 1 más cuando esa escondida se vuelve escape. Baja 0.1 por minuto. Con 2 el escondite se revisaría primero y con 4 se podría romper. Ojo al calibrar: una sola escapada con el Nemesis cerca ya lo deja en 2, y dos lo dejan en 4.

Los contadores de hábitos, en cambio, aguantan 5 minutos sin bajar y después bajan 0.1 por minuto. Así, apenas un contador llega a su umbral, el desbloqueo no se vuelve a cerrar solo.

**Qué desbloquearía** (`SO_CounterplayRules`, en `ScriptableObjects/Nemesis/`): 3 escapes escondido → emboscada a la salida; 1 persecución estancada → flanqueo desde el arranque; 2 estancadas o 2 escapes al Hub → defensa de salidas. Cada una arranca con 35 % de chance, suma 10 % por uso extra y nunca pasa de 85 %.

**Dónde se ve:**

- F9: filas `hábitos` (los cuatro contadores), `desbloquea` (qué se desbloquearía —`emboscada`, `flanqueo`, `defensa`— con qué chance, y hace cuánto fue el último registro) y `escondites` (los más usados con su medidor; `adentro, cazado` si salir ahora dejaría un escape por confirmar, `N búsq.` con las cacerías que ya aguantó adentro, y `escape pendiente` durante los 5 s de confirmación).
- F10, sección HABITS: *Log ledger* vuelca todo a la consola y *Clear habits* lo borra sin New Game.
- La consola escribe una línea por cada escondida y por cada registro. Se apaga con `logRegistrations` en el SO.

**Para que cuente bien, cada escondite necesita un `SpotId` único.** Sin id, se lo recuerda por el nombre del GameObject (con un warning), y dos escondites con el mismo nombre se mezclan. `Tools > Player > Validate Hiding Spots` los lista.

---

## Audio

Tres caminos independientes al mixer. **Ninguno de los tres es intercambiable con los otros** — la regla es: *loops continuos que comunican estado* van por `NemesisAudio`; *one-shots posicionales* van por el pool del `AudioManager`; *señales de score* van por el bus Music.

| Sonido | Componente | Bus | Estado |
|---|---|---|---|
| Pasos | `FootstepEmitter` en la raíz del prefab | Nemesis | Funciona |
| Respiración por estado | `NemesisAudio` (en el prefab; se agrega solo si falta) | Nemesis | Funciona |
| Voz: aviso de "sabe tu escondite" | `NemesisAudio` (sección *Voz y avisos*) | Nemesis | Clip provisorio: `voice_chase` a pitch 0.8 |
| Voz: "te perdí" al volver a patrullar | `NemesisAudio` | Nemesis | Funciona (`voice_lost_01/02`) |
| Música de persecución | `NemesisChaseMusic`, objeto suelto en la escena | **Music** | Funciona |
| Puertas que abre | `DoorInteractable.AnimateOpen/Close` | SFX | Funciona |
| Bajadas: gruñido, golpe de manos, impacto | `NemesisAudio.PlayDropCue`, uno por fase | Nemesis | Clips provisorios (ver *Bajadas entre pisos*) |
| Stinger de captura | — | — | **No existe** |
| Cue de activación | Cuando lo despierta un puzzle, `NemesisAudio` (`activationSoundId`). En el escape, cuando arranca a correr (`SO_EscapeSequenceConfig.revealSoundId`) | Nemesis | **Falta el clip**: los dos piden `sfx_nemesis_activacion`, que está vacío |

La música de persecución no se corta cuando te pierde de vista: sigue durante la búsqueda que viene después (también en `Traversing`) y termina cuando termina la búsqueda, así el silencio quiere decir "dejó de buscar" y no "dejó de verte" (decisión D5 del plan). `searchTailTimeout` (25 s) es la red de seguridad.

Cuando la partida tiene resultado (`GameResultManager.OnGameResult`), los loops de `NemesisAudio` y la música se desvanecen en tiempo unscaled, para no quedar sonando congelados sobre la pantalla de resultado.

### Pasos

El Nemesis usa el mismo `FootstepEmitter` que el jugador, con `bus = Nemesis` y `cadenceSource = AnimationEvent`. Los pasos los dispara un `Step` puesto en el frame de apoyo de la animación, que llega por `FootstepAnimationRelay` (está en el hijo con el Animator, no en la raíz).

> **El Animator del Nemesis usa las animaciones del jugador** (`Walking`, `Running`, `Idle` de `Player Animations/`, en `NemesisController.controller`). Son las únicas del proyecto que tienen el evento `Step`. Consecuencia: el `strideLength` del prefab está inerte, y la cadencia son los dos ritmos del jugador conmutados por los bools `isWalking`/`isRunning` — no sigue la velocidad real de `SO_NemesisMovement`.

**Los clips también son los del jugador**: el emisor del prefab usa `SO_FootstepBank_Player` (resuelve por superficie, no por estado) con `pitchMultiplier` 0.72, más grave para distinguirlo de oído, rolloff logarítmico entre 1.5 y 24 m, y oclusión por `Wall` que atenúa a 0.5. `SO_FootstepBank_Nemesis`, con los `pasos_chase_*` propios, sigue en el proyecto pero no lo usa nadie. Los pasos no dicen en qué estado está; eso lo dicen la respiración y la música.

### `NemesisAudio`

Se agrega solo a cualquier Nemesis que no lo tenga, pero el contenido —el array `stateLoops`, una entrada por estado con clip y volumen— se autora en el prefab. Un estado sin entrada hace crossfade a silencio; si el array está vacío avisa una vez por consola al arrancar.

Hoy el prefab tiene respiración: `breathing_patrol` en `Patrolling`, `breathing_search` en `Investigating` y `Searching`, `breathing_chase` en `Chasing` y `Catch`; `Traversing` no tiene entrada.

También tiene one-shots, que no son loops: van por el pool del `AudioManager` al bus Nemesis, en 3D y sin oclusión.

- **Bajadas** (sección *Bajadas*): gruñido, golpe de manos e impacto, más el volumen y el pitch del impacto. Sin oclusión porque se tienen que oír a través del piso.
- **Voz y avisos** (plan §16.2), con un enfriamiento común (`voiceCooldown`, 3 s) para que no hable encima de sí mismo:
  - **"Sabe tu escondite"** (`knownSpotStings`). Suena cuando pasa a saber en qué escondite estás (te vio entrar, o te distinguió por las rendijas) y todavía está a más de `knownSpotStingMinDistance` (2 m) de la puerta. Desde adentro es lo único que separa "sabe" de "adivina": el margen para salir antes de que llegue (D1, D16, D34). Si sólo sospecha no suena, y tampoco en la puerta, donde el golpe es la música al abrir (D13). Clip provisorio: `voice_chase` a pitch 0.8.
  - **"Te perdí"** (`lostVoices`: `voice_lost_01/02`). Suena al volver a patrullar después de una búsqueda que terminó sin encontrarte, y confirma lo que ya dice el silencio de la música (D5). Si en el medio se fue a investigar un ruido, espera a que vuelva a patrullar. Si te encontró, no suena.
  - **Cue de activación** (`activationSoundId`, `sfx_nemesis_activacion`). Suena cuando lo despierta un puzzle, no un script: el escape toca el suyo. El SO todavía no tiene clip.

Crossfade de 0.4 s entre estados, `spatialBlend` 1 (3D puro), y oclusión que **atenúa, nunca corta** (`occludedVolumeMultiplier`: 0.35 en el código, 0.5 en el prefab): que el monstruo desaparezca del audio apenas se mete detrás de una columna es peor información que que se escuche de más.

---

## Escalada de dificultad

A medida que se completan puzzles, el Nemesis ve y oye más lejos y patrulla de forma menos previsible (spec §7.2, plan Fase 7, 28/09). **Nunca se vuelve más rápido.** Lo hace `NemesisEscalation`, en la escena `Data`; los niveles están en `SO_NemesisEscalation`.

| Desde | Vista | Oído | Búsqueda | Variación de ruta |
|---|---|---|---|---|
| 0 puzzles | ×1 | ×1 | ×1 | como está (0.15) |
| 2 puzzles | ×1.1 | ×1 | ×1 | al menos 0.25 |
| 3 o más | ×1.15 | ×1.1 | ×1 | al menos 0.40 |

- **Cuenta puzzles, no módulos.** `ModuleManager` son los timers de los dispositivos y nunca avanza la historia. El primer puzzle es el que despierta al Nemesis, así que el "módulo 1" del spec son 0–1 puzzles.
- **Lee la cuenta, no suma eventos.** Si un checkpoint deshace un puzzle, el nivel baja con él. New Game vuelve a empezar de 0.
- **Vista** alarga `View Range`, y con él las rendijas del locker y lo que ve bajo la mesa. **Oído** alarga todos los ruidos y su tope juntos: caminando, en el nivel 3 te oye a 11 m en vez de 10.
- **Búsqueda** queda ×1: el spec la acortaba, pero con la búsqueda que se enfría eso premiaría esconderse y esperar (D31). **Variación de ruta** es un piso para las chances de invertir la ronda y de saltear un waypoint: nunca las baja.
- **Convive con el Director.** La escalada es permanente, y el préstamo de sentidos o de persistencia del Director es temporal y se suma encima. Si el nivel cambia en medio de un préstamo, el Director lo rearma sobre el nivel nuevo.
- **Dónde se ve:** F9 fila `escalada`; F10 sección *ESCALATION* (*Tier +* / *Tier -* / *Auto*), porque la testbed no tiene puzzles. En Zona1 el Nemesis sólo aparece en el escape, ya con todos los puzzles hechos: donde se va a notar es la Zona 2.

---

## Los ScriptableObjects

En `ScriptableObjects/Nemesis/`:

| Asset | Qué contiene |
|---|---|
| `SO_NemesisData` | Todo lo que no es velocidad: rangos, tiempos, umbrales, sesgos de ruta, cúmulos, captura, persecución estancada, investigación, escondites. |
| `SO_NemesisMovement` | Velocidades por estado + tuning del `NavMeshAgent` (angular 160, aceleración 14, stopping 1) + el movimiento a mano cuando el agente está apagado (links 2.5, subir y bajar del montacargas 1.5, giro 180) + las bajadas: costo por estado, tiempo de cada fase, arco y nombres de los estados del Animator (ver *Bajadas entre pisos*). El enfriamiento de cada bajada (`dropLinkCooldown`) está en `SO_NemesisData`. |
| `SO_NemesisPriorities` | La escalera de prioridades. Reordenable. Incluye `minimumStateDwell` (0.35 s): la histéresis que evita que dos peldaños se lo pasen ida y vuelta cada frame. |
| `SO_DirectorPacing` | El ritmo del Director (ver *Director y ritmo*). Lo lee `NemesisDirector`, no el Nemesis. |
| `SO_CounterplayRules` | Qué desbloquean los hábitos y cómo se puntúa cada escondite (ver *Hábitos del jugador*). Lo lee `PlayerHabitTracker`, en la escena `Data`. |
| `SO_NemesisEscalation` | Los niveles de la escalada por puzzles (ver *Escalada de dificultad*). Lo lee `NemesisEscalation`, en la escena `Data`. |

Fuera de esa carpeta pero leídos del lado del Nemesis: `SO_HidingData` (`ScriptableObjects/Hiding/`) y los tres de señuelos (`ScriptableObjects/Decoys/`).

Los `LayerMask` **no** están en los SO: viven en los componentes, porque son cableado de escena y no valores de diseño.

> **Campos nuevos y el asset.** Un campo agregado a `SO_NemesisData` no figura en el `.asset` hasta que alguien lo guarda desde el editor, y mientras tanto toma el valor inicial del código. El 22/09 se escribieron en el asset, con esos mismos valores iniciales, los nueve que faltaban: `stuckRepathGrace`, `spawnMinPlayerDistance`, `spawnSafeHalfAngle`, los tres `investigation*`, `underTableVisionMultiplier`, `seenEnteringWindow` y `hiddenPullOutTime`. Al agregar un campo, guardá el asset (o escribilo) para que el valor de diseño quede a la vista en el diff.

---

## Cómo verificar

**Escena de pruebas**: abrí `Scenes/Dev/NemesisTestbed.unity` (lista de chequeo en `docs/Checklist-NemesisTestbed.md`). `Tools > Nemesis > Build Bug Lab (NemesisTestbed)` le arma estaciones que reproducen los bugs de QA que necesitan geometría (escalera con puertas, pilares, balcón inalcanzable, trigger en una puerta). Para escondites, `Scenes/Dev/TestIñaki.unity` tiene la *Hiding Test Area*: los tres tipos, con su propio Nemesis.

**En Play** (las dos teclas funcionan sólo en el editor):

| Tecla | Qué abre |
|---|---|
| `F9` | HUD de debug (`NemesisDebugHUD`, está en el prefab): estado y la regla que ganó, sospecha, escondite conocido, creencia, distancia recta y por NavMesh, progreso de la persecución (`ChaseStalled`), búsqueda, cúmulo, agente, trabas, bajada (tipo, alto, fase y si puede agarrar; entre bajadas, cuántas están en enfriamiento), ritmo y presión del Director, hábitos (ver *Hábitos del jugador*), y "seguro en": segundos desde la última detección hasta volver a patrullar. **Mientras está abierto, lo que muestra se guarda** en `Logs/NemesisF9/`, un .txt por cada vez que lo abrís: segundo de juego (el mismo reloj que el CSV de `Logs/NemesisTrace/`), hora y las filas que cambiaron, con una foto completa cada 10 s. |
| `F7` | Con F9 abierto: deja una marca numerada en el .txt de F9, con una foto completa del panel. Para el momento en que algo se ve mal. |
| `F10` | Consola de test (`NemesisTestConsole`, hoy en la testbed y en `TestIñaki`; en otra escena se agrega a mano al Nemesis): armar situaciones (Nemesis detrás o delante tuyo, vos encima de él, escondido, captura), la sección del Director (un botón por zona, *Release*, *Staged entrance*, pico de tensión, saltar el silencio) y la de hábitos (*Log ledger*, *Clear habits*). |
| `1`–`6` / `0` | Con la consola en la escena, aunque esté cerrada: fija el estado que responde la escalera (Patrol, Investig, Chase, Search, Traverse, Catch); `0` o la misma tecla lo suelta. |

**Registro**: `NemesisTraceRecorder` (se agrega solo; editor y development build) escribe un CSV por sesión en `Logs/NemesisTrace/` (en un development build, en `persistentDataPath/NemesisTrace`): regla ganadora, sentidos, estado del camino y velocidad real, cada 0.25 s y en cada cambio de estado. La ruta sale una vez por consola. Se apaga con `record` en el componente.

**Gizmos** (`NemesisGizmos`): se dibujan siempre, no sólo con el Nemesis seleccionado, con un toggle por bloque y un interruptor maestro `drawGizmos` que también apaga las rutas. Conos de visión a escala (normal, agachado, bajo mesa, foco), proximidad, oído, los tres radios de ruido del jugador por paso, alcance de captura, el barrido de la búsqueda (disco, centro, punto al que va y puntos ya barridos), lo que sabe de escondites, el rastro de la persecución, el punto predicho y el de flanqueo. En Zona1, el `GizmoManager` de la escena (`Scripts/Managers/GizmoManager.cs`) oculta gizmos por familia; un script nuevo que dibuje gizmos va en su `Families()`, no con un bool propio.

**Validación de nivel**:

- `Tools > Nemesis > Validate Navigation Setup` reporta geometría que se quedó afuera del bake, máscaras mal puestas, waypoints sin tag o fuera del NavMesh, modifier volumes que el bake descarta, y filas de `SO_CounterplayRules` con umbral 0 o chances fuera de 0..1. En las bajadas revisa:
  - que las dos puntas estén en el NavMesh;
  - que el alto esté entre 1.5 y 5 m y que haya avance horizontal;
  - que haya vuelta desde abajo;
  - que el arco no atraviese geometría;
  - que el aterrizaje tenga 1 m libre y quede lejos del Hub;
  - los links a mano en el área `NemesisDrop`.

  Como nota, avisa *Generate Links* prendido y los estados de animación que faltan.
- `Tests/EditMode` (Window > General > Test Runner, pestaña EditMode): los tests de la aritmética de los hábitos (`HabitLedgerTests`) y del arco de las bajadas (`DropPathTests`).
- `Tools > Player > Validate Hiding Spots`, ver *Escondites*.

El `NavMeshSurface` de Zona1 hornea Default + Ground + Wall + Props; el de la testbed, Ground + Wall + Props (a propósito).

---

## Errores comunes

**"El Nemesis nunca aparece."** En Zona1 es lo esperado: `Wake Only From Script` está prendido y sólo lo despierta el escape. En otra escena, ningún spawn point pasa los tres tests. Mirá la consola, dice cuál falla. Casi siempre están todos demasiado cerca de donde termina el puzzle de activación.

**"Se queda trabado contra una esquina."** Hay un watchdog (`NemesisStuckEscape`) que escala: primero recalcula el camino y le da `stuckRepathGrace` (1.5 s), después lo teletransporta a un waypoint fuera de la vista del jugador, desde el que pueda seguir hacia donde iba y a 3 m o más de donde se trabó. Si pasa seguido en un lugar concreto, es geometría, no IA — corré el validador.

**"Ignora un NavMeshLink que puse."** Si está en el área `Jump` es a propósito: ahí caen los links que genera el bake solo, y el Nemesis no la usa (`NemesisLifecycle` la saca de su `areaMask`; atravesaba columnas por esos links, WIR-028). Un link autorado para él va en otra área. Si es una bajada, poné un `NemesisDropLink` en vez de un `NavMeshLink` suelto: sin él, la cruza como un link cualquiera, en línea recta y sin aviso.

**"No usa la bajada."** Mirá, en orden:

1. Si está en enfriamiento: F9, fila `bajada`, `N en enfriamiento`. Dura 8 s después de usarla o de echarse atrás.
2. Si patrulla: cuesta 20 y prefiere la escalera.
3. Si las puntas no están sobre el NavMesh: lo dice la consola al arrancar, y el validador.
4. Si el aterrizaje está justo abajo del borde.

**"La consola dice que un collider SÓLIDO está en una capa que escucha el Nemesis."** Es geometría en la capa de ruido (`DetectableAudio`). Se ignora, pero así también queda afuera del bake y de la máscara de visión: pasala a Wall, Props o Default.

**"No hace ningún ruido."** O `stateLoops` quedó vacío (te lo avisa por consola), o estás en una escena abierta sola — todo el audio del proyecto depende de que esté cargada la escena `Data`. Dale Play desde `Bootstrap`.

**"Me encontró escondido sin haberme visto entrar."** Revisá, en orden: si te acercó a 1.5 m (proximidad extrema), si estabas en un locker con él del lado de la puerta a menos de 2.45 m o bajo una mesa a menos de 3.5 m el tiempo suficiente para llenar el medidor, y si lo trajiste con ruido (la respiración se oye a unos 2 m, la exhalación a unos 6): el ruido lo lleva hasta ahí y la proximidad hace el resto.

**"Toqué el enum de estados y se rompió todo."** Insertaste en el medio en vez de al final. Ver arriba.

**"Cambié un número en el SO durante Play y quedó guardado."** Sí, Unity hace eso. Los cambios a ScriptableObjects en Play mode persisten al asset.

---

## Pendiente

| Qué | Quién |
|---|---|
| Voz: el aviso de "sabe tu escondite" usa `voice_chase` provisorio. Falta un clip propio, que no se confunda con el gruñido de las bajadas | Audio |
| Decidir qué hacer con `SO_FootstepBank_Nemesis` y los `pasos_chase_*`: quedaron sin uso desde que los pasos usan el banco del jugador | Audio |
| **Conseguir dos clips**: el stinger de captura y el cue de activación — ver abajo | Audio |
| Marcar superficies con `FootstepSurface` en el blockout | Nivel |
| Definir qué es la cinemática de captura del spec §5.6, y la animación de sacar al jugador de un escondite | Diseño |
| Poner `NemesisDecoyBreaker` en el prefab antes de colocar radios en un nivel (ver *Señuelos*), y los ids de sonido de los tres señuelos | Nivel + audio |
| Revisar los umbrales de la escalada (0 / 2 / 3 puzzles) cuando exista la Zona 2 | Diseño |
| Bajadas: las animaciones del plan §15.5 y el setup del Animator, clips propios de golpe de manos e impacto, apagar *Generate Links* y rebakear (D10), y jugarlas en el *Drop Lab* de la testbed (casos 12–16 y 55) | Arte + audio + nivel |

### Dos `SO_SoundData` esperando clip

Están creados y ya enganchados al `AudioManager`, pero con el campo `clip` vacío. **Reservan el id** para que el código que los dispara se pueda escribir contra ellos; hasta que alguien arrastre un clip suenan silencio, sin error y sin warning.

| Asset | id | Para qué |
|---|---|---|
| `SO_sfx_nemesis_captura_stinger` | `sfx_nemesis_captura_stinger` | El impacto sonoro que cierra el intento (spec §5.5) |
| `SO_sfx_nemesis_activacion` | `sfx_nemesis_activacion` | El momento en que el Nemesis entra al juego (spec §7.1). Ya tiene quien lo dispare: el escape, cuando el Nemesis arranca a correr |

Llenarlos es arrastrar el clip al campo `Clip`, nada más. El id, la categoría (Nemesis) y el rango 3D ya están puestos.

Los de la voz de «te perdí» (`SO_sfx_nemesis_voice_lost_01` y `_02`) sí tienen clip y están registrados, pero todavía no los pide ningún código.
