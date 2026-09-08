# Sistema Nemesis

El perseguidor. No se lo puede matar, atrapar ni frenar: todo lo que el jugador tiene es evasión, distracción y sigilo. Aparece cuando se resuelve el primer puzzle y a partir de ahí patrulla las zonas que el jugador ya recorrió, que es lo que convierte el backtracking en una decisión de riesgo.

```
DORMIDO         invisible, sin navegación, sin sentidos
   ↓ (se completa el puzzle de activación)
PATRULLA        recorre cúmulos de waypoints, lento
   ↓ (ruido)              ↓ (te ve)
INVESTIGA  ──────────→  PERSIGUE  ──────────→  CAPTURA
   ↑                       ↓ (te pierde)
   └────  BUSCA  ←─────────┘
```

Código en `Assets/_Project/Scripts/Nemesis/`, ScriptableObjects en `Assets/_Project/ScriptableObjects/Nemesis/`, prefab en `Assets/_Project/Prefabs/Nemesis.prefab`, escena de pruebas en `Assets/_Project/Scenes/Dev/NemesisTestbed.unity`.

> Este documento es para tunear y armar niveles. La arquitectura interna —por qué cada cosa está donde está— vive en `docs/CLAUDE.md`, en inglés.

---

## Activación

El Nemesis **no existe** al empezar la partida. Está en la escena desde el principio pero dormido: sin agente, sin sentidos, invisible.

En el prefab, `NemesisController > Activation > Activated By Puzzle Id`. Ese id tiene que coincidir con el `PuzzleId` del `SO_PuzzleData` correspondiente. Vacío = activo desde que le das Play, que es lo que querés en la testbed y **no** lo que querés en el nivel.

Cuando el puzzle se completa, elige un spawn point. Un punto sólo sirve si cumple **las tres**:

1. está a más de `SpawnMinPlayerDistance` **medido sobre el NavMesh**, no en línea recta;
2. está fuera del cono de visión del jugador (`SpawnSafeHalfAngle`);
3. está detrás de geometría.

Si ninguno cumple, **el Nemesis no aparece**: se vuelve a dormir y reintenta dos veces por segundo hasta que alguno sirva, cosa que normalmente pasa apenas el jugador camina o se da vuelta. Es a propósito — aparecer a la vista es la única forma de que la entrada se sienta tramposa.

Si nunca aparece, la consola dice cuál de los tres tests falló para todos los puntos. Poné los spawn points separados, detrás de cobertura, y lejos de donde el jugador va a estar parado cuando termine ese puzzle.

---

## Quién decide en qué estado está

**Los estados no deciden las transiciones.** Eso es lo primero que sorprende al leer el código: `NemesisPatrolState` no elige pasar a `Chasing`.

Quien decide es `NemesisDecision`, leyendo una **escalera de prioridades** que vive en `SO_NemesisPriorities.asset`. Cada peldaño es "si se cumplen estas condiciones, el estado pedido es X", y se leen en orden hasta que uno da verdadero. El asset es reordenable desde el inspector, así que **cambiar el orden de la escalera no recompila nada**.

Los seis estados:

| Estado | Qué es | Velocidad |
|---|---|---|
| `Patrolling` | Recorre cúmulos de waypoints. Espera en cada uno. | 2.75 |
| `Investigating` | Escuchó algo y va al origen. | 2.5 |
| `Chasing` | Te ve. Persecución activa con predicción e intercepción. | 3.0 |
| `Searching` | Te perdió pero sabe por dónde andabas. Barre la zona. | 2.75 |
| `Traversing` | "Para llegar necesito el montacargas". | 3.0 |
| `Catch` | Terminal. Te agarró. | — |

`Traversing` existe porque una losa corta la línea de visión durante todo el viaje: sin un estado propio que sostenga la decisión por `ElevatorCommitTime` (12 s), el Nemesis abandonaba el ascensor cada vez.

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

Estar `Hidden` (escondido) sí salta la vista por completo. La única excepción es la proximidad extrema, abajo.

### Oído — `FieldOfListening`

**El ruido en este proyecto es una esfera, no un evento.** No existe ningún `OnNoiseGenerated`. El jugador lleva un `SphereCollider` (`PlayerStateManager.AudioEmitingZone`) cuyo radio setean los estados de movimiento según el paso:

| Paso | Radio del emisor |
|---|---|
| Agachado | 1 |
| Caminando | 2 |
| Corriendo | 6 |
| Quieto | el GameObject se apaga entero — **quieto es silencioso** |

`FieldOfListening` barre cada 0.1 s, lee el radio real del collider y lo escala por `noiseRangeScale` (2.5) antes de atenuar por paredes (`wallOcclusionMultiplier` 0.8) y por pisos (`floorOcclusionMultiplier` 0.75). `listenRange` es 15 y se mide **por camino de NavMesh**, no en línea recta.

Que los pisos atenúen en vez de cortar es deliberado: es el único canal que tiene el Nemesis hacia el piso de arriba.

> **Si algo tiene que "hacer ruido", tiene que durar.** Prendé el emisor al radio que quieras y dejalo más de 0.1 s. Un pulso de un frame puede caer entre dos barridos y no lo escucha nadie. Y devolvé el radio a donde estaba: el emisor lo comparten los estados de movimiento, y un radio olvidado deja al jugador permanentemente ruidoso sin que nada tire error.

### Proximidad extrema

`proximityDetectionRange` = 1.5. A esa distancia te detecta **aunque estés escondido**. Es la única cosa que rompe `Hidden`, y existe para que un escondite pegado al Nemesis no sea un exploit.

`proximityRadius` (12) es otra cosa: alimenta la viñeta de proximidad del HUD, no la detección.

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

El Hub **no** es un `NavMeshObstacle` ni tiene código. Es un volumen **Not Walkable** pintado en el NavMesh. El Nemesis no puede calcular un camino que lo atraviese, punto. Si el jugador se mete ahí durante una persecución, el Nemesis espera afuera y vuelve a patrullar cuando se le vence el `searchTimeOut`.

No hay nada que agregar en C# para que esto funcione, y no hay que agregarlo.

---

## Captura

```
NemesisCatchState
   → PlayerStateManager.OnCaptured()
      → PlayerEvents.OnPlayerCaptured
         → CheckpointManager  (carga el checkpoint)
         → CaptureFadeView    (fade a negro)
   → espera captureGracePeriod (4 s)
   → NemesisStateManager.RepositionAfterCapture()
   → NemesisEvents.OnCaptureResolved  (recién ahí se levanta el negro)
```

El Nemesis **nunca** llama al guardado ni a la UI. Sólo llama a `OnCaptured()` y levanta eventos.

Dos cosas que importan para el diseño:

- **Período de gracia.** Después del checkpoint el Nemesis espera 4 s antes de volver a razonar, para que no te agarre en el frame en que reapareciste.
- **Se reposiciona.** Vuelve a un spawn point o waypoint al azar a más de `repositionMinPlayerDistance` (15), nunca al lugar donde te agarró. Si no, el checkpoint y la captura quedan pegados y es el mismo punto de tensión en loop.

`catchMaxReach` es 1 m y `catchRequiresLineOfSight` está prendido: no te agarra a través de una pared aunque el agente esté al lado.

---

## Audio

Tres caminos independientes al mixer. **Ninguno de los tres es intercambiable con los otros** — la regla es: *loops continuos que comunican estado* van por `NemesisAudio`; *one-shots posicionales* van por el pool del `AudioManager`; *señales de score* van por el bus Music.

| Sonido | Componente | Bus | Estado |
|---|---|---|---|
| Pasos | `FootstepEmitter` en la raíz del prefab | Nemesis | Funciona |
| Respiración y voz por estado | `NemesisAudio` (se agrega solo) | Nemesis | **Falta el contenido** |
| Música de persecución | `NemesisChaseMusic`, objeto suelto en la escena | **Music** | Funciona |
| Puertas que abre | `DoorInteractable.AnimateOpen/Close` | SFX | Funciona |
| Stinger de captura | — | — | **No existe** |
| Cue de activación | — | — | **No existe** |

### Pasos

El Nemesis usa el mismo `FootstepEmitter` que el jugador, con `bus = Nemesis` y `cadenceSource = AnimationEvent`. Los pasos los dispara un `Step` puesto en el frame de apoyo de la animación, que llega por `FootstepAnimationRelay` (está en el hijo con el Animator, no en la raíz).

> **El Animator del Nemesis usa las animaciones del jugador** (`Walking`, `Running`, `Idle` de `Player Animations/`). Son las únicas del proyecto que tienen el evento `Step`. Consecuencia: el `strideLength` del prefab está inerte, y la cadencia son los dos ritmos del jugador conmutados por los bools `Walking`/`Running` — no sigue la velocidad real de `SO_NemesisMovement`.

Los clips salen de `SO_FootstepBank_Nemesis`, que resuelve **por superficie**, no por estado:

| Superficie | Clips |
|---|---|
| Concrete | los del jugador, pitcheados a 0.78–0.86 |
| **Metal** (y es el fallback) | los 9 propios: `pasos_chase_01..07`, `investigar_01`, `patrulla_01` |
| Wood / Gravel / Water / Oil | los del jugador, pitcheados |

Como Metal es el fallback, en cualquier piso sin marcador `FootstepSurface` el Nemesis baraja al azar pasos de persecución, patrulla e investigación sin importar qué esté haciendo. **Esto está pendiente de decisión** — ver abajo.

### `NemesisAudio`

Se agrega solo a cualquier Nemesis que no lo tenga, así que no hace falta abrir el prefab para que exista. Lo que sí hace falta es **autorar el array `stateLoops`**: una entrada por estado, con clip y volumen. Un estado sin entrada hace crossfade a silencio.

Si el array está vacío avisa una vez por consola al arrancar. Los clips ya están en `Audio/SFX/Nemesis/` (`breathing_patrol`, `breathing_chase`, `breathing_search`, `voice_chase`, `voice_lost_01/02`).

Crossfade de 0.4 s entre estados, `spatialBlend` 1 (3D puro), y oclusión que **atenúa al 0.35, nunca corta**: que el monstruo desaparezca del audio apenas se mete detrás de una columna es peor información que que se escuche de más.

---

## Escalada de dificultad — no implementada

El spec §7.2 pide que el Nemesis se ponga más agresivo a medida que se completan módulos. **No está construido**, y el propio spec lo marca como pulido diferido: pide mantener los valores base constantes en la primera iteración.

Dos cosas quedan decididas de antemano para cuando se construya:

- **Cuenta puzzles, no módulos.** `ModuleManager` son los timers de los dispositivos y nunca avanza la historia. La espina de progresión de este proyecto es completar puzzles: es lo que desbloquea rutas, despierta al Nemesis y arma los checkpoints.
- **Tiene que leer un contador, no sumar eventos.** `PuzzleStateManager.RestoreSnapshot` rellena los puzzles resueltos **sin** emitir `OnPuzzleCompleted`, así que algo que cuente eventos volvería de una partida guardada creyendo que el jugador recién empieza.

Y una trampa que ya está desarmada: cuando esto se implemente, va a cambiar la sintonía del Nemesis de forma **permanente**, mientras que el boost sensorial de `NemesisDirector` la cambia de forma **temporal** y la devuelve. El Director ya lee su punto de retorno desde `NemesisStateManager.BaselineData` en vez de cachearlo, justamente para que las dos cosas se compongan en lugar de pisarse.

---

## Los tres ScriptableObjects

| Asset | Qué contiene |
|---|---|
| `SO_NemesisData` | Todo lo que no es velocidad: rangos, tiempos, umbrales, sesgos de ruta, cúmulos, captura. |
| `SO_NemesisMovement` | Velocidades por estado + tuning del `NavMeshAgent` (angular 160, aceleración 14, stopping 1). |
| `SO_NemesisPriorities` | La escalera de prioridades. Reordenable. |

Los `LayerMask` **no** están en los SO: viven en los componentes, porque son cableado de escena y no valores de diseño.

---

## Cómo verificar

**Escena de pruebas**: `Tools > Nemesis > Build Nemesis Test Scene` arma una desde cero, o abrí `Scenes/Dev/NemesisTestbed.unity`.

**En Play:**

| Tecla | Qué abre |
|---|---|
| `F9` | HUD de debug: estado actual, creencia, verdicto de ruta, contadores de atasco. |
| `F10` | Consola de test: forzar estados, teletransportar, togglear `Hidden`, disparar la captura. |

**Gizmos** (`NemesisGizmos`, con el Nemesis seleccionado): dibuja los dos conos de visión a escala, el radio de oído, los tres radios de ruido del jugador por paso, el punto predicho, el de flanqueo y el de intercepción.

**Validación de nivel**: `Tools > Nemesis > Validate Navigation Setup` reporta geometría que se quedó afuera del bake. `Tools > Nemesis > Repair Layer Masks` arregla máscaras mal puestas.

---

## Errores comunes

**"El Nemesis nunca aparece."** Ningún spawn point pasa los tres tests. Mirá la consola, dice cuál falla. Casi siempre están todos demasiado cerca de donde termina el puzzle de activación.

**"Se queda trabado contra una esquina."** Hay un watchdog (`NemesisStuckEscape`) que escala: primero recalcula el camino, después lo teletransporta a un waypoint fuera de la vista del jugador. Si pasa seguido en un lugar concreto, es geometría, no IA — corré el validador.

**"Suena igual esté persiguiendo o patrullando."** Es lo del banco de pasos: todos los clips propios están en la misma superficie. Ver *Audio*.

**"No hace ningún ruido."** O `stateLoops` está vacío (te lo avisa por consola), o estás en una escena abierta sola — todo el audio del proyecto depende de que esté cargada la escena `Data`. Dale Play desde `Bootstrap`.

**"Toqué el enum de estados y se rompió todo."** Insertaste en el medio en vez de al final. Ver arriba.

**"Cambié un número en el SO durante Play y quedó guardado."** Sí, Unity hace eso. Los cambios a ScriptableObjects en Play mode persisten al asset.

---

## Pendiente

| Qué | Quién |
|---|---|
| Autorar `stateLoops` en el prefab (respiración y voz por estado) | Audio |
| Decidir si los `pasos_chase_*` son "corriendo" (banco aparte) o "sobre metal" (están bien) | Audio + diseño |
| **Conseguir dos clips**: el stinger de captura y el cue de activación — ver abajo | Audio |
| Marcar superficies con `FootstepSurface` en el blockout | Nivel |
| Definir qué es la cinemática de captura del spec §5.6 | Diseño |
| Escalada de dificultad del spec §7.2 — diferida a propósito, ver arriba | Diseño + código |

### Dos `SO_SoundData` esperando clip

Están creados y ya enganchados al `AudioManager`, pero con el campo `clip` vacío. **Reservan el id** para que el código que los dispara se pueda escribir contra ellos; hasta que alguien arrastre un clip suenan silencio, sin error y sin warning.

| Asset | id | Para qué |
|---|---|---|
| `SO_sfx_nemesis_captura_stinger` | `sfx_nemesis_captura_stinger` | El impacto sonoro que cierra el intento (spec §5.5) |
| `SO_sfx_nemesis_activacion` | `sfx_nemesis_activacion` | El momento en que el Nemesis entra al juego (spec §7.1) |

Llenarlos es arrastrar el clip al campo `Clip`, nada más. El id, la categoría (Nemesis) y el rango 3D ya están puestos.

Los de la voz de «te perdí» (`SO_sfx_nemesis_voice_lost_01` y `_02`) sí tienen clip y están listos.
