# Plan — IA stalker y anti-cheese del Nemesis

> Compara el análisis *"IA de enemigos stalker"* (Alien: Isolation, Mr. X, Nemesis, Dimitrescu,
> Requiem, GDC) contra lo que el Nemesis de WIRED ya tiene hoy, y propone qué construir, en qué
> orden y dónde. **Asume que el sistema de escondites se construye** (spec *Hiding System v1.0*), y
> por eso el eje del plan es el anti-cheese alrededor de esconderse.
>
> Arquitectura vigente: `docs/CLAUDE.md` (inglés). Tuning del Nemesis: `docs/Nemesis-System.md`.
> Todo el código nuevo va en inglés, como el resto de `Assets/_Project/Scripts/`.

---

## Pendiente

Al 05/10/2026. **Plan de ejecución a tres semanas (05/10 → 25/10)**, en orden de dependencia.
Reemplaza la lista del 28/09: lo que seguía vigente de ella está en las etapas F a H, en
[Falta jugar](#falta-jugar-de-las-fases-ya-construidas) y en [Más adelante](#más-adelante). El
diagnóstico detrás de cada ítem (causa, archivo y línea, arreglo) está en el
[§19](#19-rediagnóstico-del-05102026). Lo construido está en [✅ Hecho](#-hecho), y el detalle de
cada fase, en el [§9](#9-fases-de-implementación).

> **De dónde sale.** `Plan-Busqueda-Nemesis.md`, el docx del 30/09 y `Logs/fase3-descartada/` se
> perdieron ([§19.1](#191-qué-se-perdió-y-qué-quedó)). Búsqueda, montacargas y titileo se
> rediagnosticaron del código el 05/10. WIR-057, WIR-058 y WIR-062 se planifican como rotos, sin
> esperar a jugar el commit `145eaf5e` (decisión de Iñaki, 05/10).

Tamaños: **S** menos de 2 h · **M** medio día · **L** un día o más.

Cada etapa se cierra igual:

1. Código y tests EditMode: compila en batch y pasan los tests.
2. Sus docs: `CLAUDE.md`, `Nemesis-System.md` y sus casos en el [§13](#13-casos-de-prueba).
3. Una pasada de juego de Iñaki con los casos que lista la etapa. Lo que salga ahí cierra o reabre
   el ítem.

Todo peldaño nuevo va en `SO_NemesisPriorities.asset` **y** en `BuildDefaultLadder()`, y los enums
sólo crecen por el final ([§10](#10-reglas-del-proyecto-que-este-plan-no-puede-romper)).

### Semana 1 (05–11/10) — Que busque bien y que no titile

**Etapa A — Preparación** — ✅ hecha el 05/10 (sin commitear; el detalle está en [✅ Hecho](#-hecho))

- Falta sólo mirar una traza nueva después de jugar: las columnas `view_range`, `sight_miss` y
  `lost_sight_why`, y las filas `sight`.
- *Tools > Nemesis > Validate Ladder* es desde ahora la primera verificación de las etapas B y D:
  reproducía el titileo (T1 y T2, arreglados en la etapa B) y reproduce la búsqueda con creencia
  vieja al bajar del montacargas (M1), que sigue marcada como conocida hasta la etapa D.

**Etapa B — Titileo entre `Chasing` y `Searching`** — ✅ construida y jugada el 05/10 (sin commitear)
→ [§19.4](#194-titileo-entre-chasing-y-searching)

- **B1, hecho: "todavía sabe dónde está".** Parado en el último punto visto, sigue en `Chasing`
  mientras el radio de su creencia esté por debajo de `Chase Hold Radius` (3 m) y te haya visto hace
  menos de `Chase Hold Max Time` (1,2 s). El radio ya es la mezcla que pidió Iñaki (D45): verte lo
  deja en 0,5 m, un ruido tuyo lo deja según qué tan bien te oyó, y sin nada nuevo crece a 4,5 m/s.
  Sin oírte aguanta 0,56 s.
- **B2, hecho: el veredicto de ruta tiene que sostenerse.** `IsBeliefUnreachable` cambia recién
  cuando la respuesta nueva se mantuvo `Route Verdict Settle Time` (0,75 s, dos consultas seguidas),
  en los dos sentidos. La primera respuesta después de un rato sin preguntar vale como viene.
- **B3, cerrado sin tocar nada.** En la sesión de Iñaki del 05/10 (`trace_20261005_143612.csv`, 6,8
  min) la persecución perdió la vista 27 veces y las 27 fueron `occluded`: se las ganó el jugador
  tapándose. Ninguna fue `cone` ni `range`, así que la decisión del 04/10 (romper la línea de visión
  tiene que valer) queda como está.
- **Jugada el 05/10:** en esa traza no hay ninguna estadía en `Searching` de menos de un segundo, y
  las dos estadías cortas en `Chasing` son una captura y un estado fijado con F10. Corriendo por el
  borde del NavMesh lo persigue sin cortar. Lo que salió mal no era titileo: al doblar la esquina
  salía a buscar para otro lado. Eso pasó a la etapa C.
- **Respondido por Iñaki (D46):** el ruido no dirige la persecución. Parado en el último punto
  visto y oyéndote correr, pasa a buscar; no te sigue por el oído.

**Etapa C — Búsqueda** — 🟡 C0–C2 construidas el 05/10 (commit `417ce019`); C3, C5, C6 y C7 el
07/10 (sin commitear); falta jugarla → [§19.2](#192-búsqueda) y [§19.9](#199-análisis-de-causas-del-0710-wir-057-058-y-062)

- **C0, hecho: al pasar a buscar tira hacia donde ibas** (playtest del 05/10: "cuando doblás la
  esquina no predice que vas a seguir para adelante"). En la traza, de 7 pases de la persecución a
  la búsqueda, 3 salieron para un lado que no era el del jugador; en uno estuvo 2,5 s a 6–9 m, sin
  nada en el medio, fuera de su cono. El primer sorteo dividía por la caminata y los lugares de al
  lado le ganaban al de adelante. Ahora ese sorteo, y sólo ése, multiplica cada lugar por
  `Search Map Chase Heading Boost` (4) según qué tan en tu rumbo queda: ×4 adelante, ×1 al costado,
  ×0,25 atrás. Sigue siendo un sorteo.
- **C1, hecho: WIR-057, va derecho a tu escondite.** El uso de un escondite se cuenta al **salir**,
  no al entrar (`PlayerHabitTracker`). Antes, en tu primer escondite el único "usado" era el que
  estabas ocupando, y la búsqueda lo sorteaba con 25 % por tirada. Es lo que pedía R4 ("no el
  escondite donde estás"); D23 sigue igual para los escondites que ya usaste.
- **C2, hecho: búsqueda quieta.** Si te ve y no puede llegar, va hasta lo más cerca que le deja el
  NavMesh, se queda ahí y te sostiene la mirada (`NemesisSearchingState.TickWatch`,
  `NemesisLookAround`). Mientras te ve, la búsqueda no se enfría: ya no vuelve a patrullar con vos
  a la vista. Cumple el [§18.5](#185-modelo-propuesto) A. Cuando te pierde, vuelve a elegir sobre
  el mapa.
- **C3, hecho el 07/10 salvo un punto: WIR-062, busca hacia atrás.** Lo principal era C0. Lo que
  todavía lo podía mandar para atrás:
  - ✅ un ruido después de perderte sembraba un disco en planta que cruzaba paredes. Ahora el ruido y
    el avistamiento se siembran por distancia caminando sobre el grafo (`PossibilityMap.CollectAlongEdges`),
    desde un nodo de arranque que el componente valida con un `NavMesh.Raycast`;
  - ✅ el mapa limpiaba hasta 7 m aunque la vista sostuviera 14: ahora limpia hasta
    `EffectiveViewRange` × `Search Map Clear Range Scale`;
  - sin mapa o sin creencia, reparte puntos al azar: queda. Pasa sólo entrando desde una captura o con
    un escondite conocido y sin creencia, no al perderte.
- **C5, hecho el 07/10: el rastro, "la salida que él mismo estaba tapando" (WIR-062).** Mientras
  persigue, busca o investiga, los nodos a `Search Map Trail Radius` (2 m) de por donde caminó en los
  últimos `Search Map Trail Memory` (4 s) quedan tapados: el valor no entra y un ruido no los
  atraviesa. Lo que te oye o te ve dentro de la duda de esa evidencia borra ese tramo. Es nuevo, no
  estaba en el plan; suma una memoria corta de dónde estuvo, al lado de "mirar es la memoria".
- **C6, hecho el 07/10: WIR-058 sin montacargas.** Buscando en la pasarela, un ruido tuyo abajo no
  pasa por `Investigating` ("la búsqueda sigue tibia" va antes que "escucha un ruido"), y los lugares
  de abajo, lejos por la escalera, no llegaban al umbral de valor ÷ caminata: "revisó todo" en el
  acto, 6 s parado y a patrullar. Ahora la elección que responde a evidencia nueva, y la primera al
  salir de `Chasing` o `Traversing`, es una visita debida: alcanza con la parte del valor
  (`SearchPickRules.TakesPart`), y la caminata pesa sólo en el sorteo.
- **C7, hecho el 07/10: "lo vio entrar" pide el cono (WIR-057).** Contaba como visto si te había
  visto hace menos de 0,75 s y tenía línea a la puerta, aunque la puerta quedara al costado o
  detrás. Ahora la puerta también tiene que estar dentro de `View Angle`.
- **C4, DIS-002: sin reproducir.** No hay ningún término de distancia en la duración de la
  investigación. En la traza del 05/10, de 6 investigaciones por ruido, 5 terminaron en menos de
  2,5 s porque **te vio**, y la que no, duró 10,7 s con sus 4 s de mirar alrededor. Ninguna fue a
  menos de 4 m (la más cercana, 4,8 m): falta jugar el I-c del checklist para cerrarlo.
- **Las cuatro reglas de búsqueda**, hoy:
  - (a) te ve o te oye → va a ese punto: con C6, también lejos y en otro piso a pie; falta el caso
    con montacargas o bajada (D7); el inalcanzable, con C2. Un ruido vago va a su área, no a su
    punto (Fase 1, WIR-057): si el reporte pide "el punto" literal, choca con eso;
  - (b) llega y no te ve → investiga alrededor: se cumple;
  - (c) te perdió persiguiendo → busca cerca del último punto visto, hacia donde ibas: con C0;
  - (d) no busca al azar ni hacia atrás: con C0, C3 y C5. "Por donde no pasaste" ahora sí se
    modela, en corto: su propio rastro de los últimos segundos tapa.
- **Para decidir (Iñaki):** las causas de WIR-057 que quedan son decisiones, no bugs. Están en el
  [§19.9](#199-análisis-de-causas-del-0710-wir-057-058-y-062).
- **Jugar:** casos 77 a 84 del §13, y 62, 63, 65 y 67, con `Draw Possibility Map` y
  `Draw Search Pick`. F9 dice "tirando hacia donde ibas" en la fila de la búsqueda cuando el sorteo
  usó tu rumbo, "se lo debe a lo que sintió" cuando fue una visita debida, y "rastro tapa N" en la
  fila del mapa.

### Semana 2 (12–18/10) — Pisos, montacargas y escondites por el mapa

**Etapa D — Pisos y montacargas** (≈ 3 días) → [§19.3](#193-pisos-y-montacargas)

En el plan perdido esto era su "Fase 3", que no es la [Fase 3](#fase-3--contar-sin-reaccionar) de
este plan.

- **D1 (S) Llega y busca sobre una creencia vieja.** El peldaño "venía hacia el montacargas y
  todavía cree algo" sólo pregunta si hay creencia, y la creencia no vence. Se le suma
  `BeliefAgeUnder(ElevatorCommitTime)`.
- **D2 (M) Sólo cruza quien tiene motivo.** Cualquier estado puede pisar el hueco y arrancar un
  cruce; sólo `NemesisSearchPicker` descarta las rutas con montacargas. Una misma prueba "a pie"
  para la revisión de escondites y para `Investigating`. Si la patrulla puede viajar es la
  [decisión 2](#decisiones-de-iñaki-del-0510).
- **D3 (S) Abandona el viaje por un veredicto que oscila.** Hoy abandona si te ve y "la ruta no
  cruza pisos", que también da falso cuando la consulta no pudo correr (vos en la cabina en
  movimiento, o a más de 2 m del NavMesh). Pasa a abandonar sólo con una ruta completa que no
  cruza, o con la prueba de altura que D30 ya usa en las bajadas.
- **D4 (M) Se rinde, espera 10 s y vuelve a comprometerse** mientras la causa sigue ahí (vos parado
  en la cabina). Enfriamiento por hueco que crece y se reinicia con un viaje completo, y la bandera
  por link en vez de la global.
- **D5 (M) Ida y vuelta sin tope** (cheese C7). Presupuesto de viajes y después espera en el
  descanso ([decisión 3](#decisiones-de-iñaki-del-0510)). Un link común sólo suelta el
  compromiso si el viaje no era de piso. No toca la liberación de D29.
- **D6 (S–M) Montacargas sin energía.** El Nemesis ignora `ElevatorPower` y lo usa igual
  ([decisión 4](#decisiones-de-iñaki-del-0510)). `NemesisElevatorLink` pasa a ser dueño de
  "activo = con energía y no suspendido". Negarlo dentro del cruce crearía el loop de D4.
- **D7 (M) WIR-058, no va adonde escuchó.** El caso a pie lo cerró C6. Queda cuando la ruta al ruido
  usa el montacargas o una bajada: al llegar, la única salida de `Traversing` es `Searching`, sobre un mapa que se dispersó
  durante todo el viaje, y el punto escuchado nunca se visita. Predicado nuevo "venía por un ruido"
  y peldaño a `Investigating`, y resembrar el mapa al aterrizar. Con escalera sola la regla ya se
  cumple.
- **Con el editor:** en la testbed, el link de abordaje de arriba (`End`) sigue sin unir nada.
- **Sin jugar:** *Validate Ladder* tiene que dejar de marcar M1 (con D1).
- **Jugar:** G22 a G27 del checklist. Para WIR-058, F9 fila `decisión`.
- **Queda afuera:** el "asomarse" antes de cambiar de piso (caso 66).

**Etapa E — Escondites por el mapa y patrulla después de la caza** (≈ 2 días) →
[§19.5](#195-escondites-por-el-mapa-y-patrulla-después-de-la-caza)

Es la tanda 2 del plan perdido, que no llegó a entrar. Reemplaza el sorteo que arregla C1.

- **E1 (S) Fórmula de D38** ([decisión 1](#decisiones-de-iñaki-del-0510)).
- **E2 Escondites por el mapa:**
  - la regla pura `Logic/SpotOpenRules.cs`, con sus tests (S);
  - `NemesisPossibilityMap.ShareInSpot` (S);
  - el umbral y la escala del hábito en `SO_NemesisData` y su editor (S);
  - `ConsiderSpotsByMap` en `NemesisHidingAwareness`, en lugar de los tres llamados a
    `ConsiderUsedSpots`, y afuera `usedSpotsRolled` (M);
  - F9 y gizmo por escondite (S);
  - reescribir los casos 6, 39 y 40 (S).
- **E3 (M) Patrulla después de la caza.** Hoy nada de la patrulla lee el mapa. Valor por cluster
  (`ShareNear` sobre el centroide), un multiplicador en `NemesisClusterPatrol.PickCluster` sólo
  durante la caza y su gracia de 45 s, un tunable, una fila en F9 y el test del peso. El retiro de
  `Relax` del Director tiene que seguir ganando.
- **E4 (M) Un ruido que no alcanza para investigar reordena la patrulla** (D46, Iñaki, 05/10: "iba a
  ir por izquierda pero escuchó algo por derecha, entonces va a la derecha"). Hoy cualquier ruido
  tuyo que oye lo manda a `Investigating` ("escucha un ruido" sólo pregunta `HearsPlayer`): no
  hay ruido "que no alcanza". Falta decidir cuál es. Toca el mismo `PickCluster` que E3.
- **Jugar:** 65, 39 y 40 en el *Hiding Lab*, con la fila del mapa en F9. Y una búsqueda que termina
  vacía: la primera vuelta de patrulla pasa cerca de donde quedó valor.

### Semana 3 (19–25/10) — Deuda del plan, contra-jugadas y documentación

Es el colchón: si las pasadas de juego de las semanas 1 y 2 abren trabajo, se cae primero la etapa
H, después la G y después la F.

**Etapa F — [Fase 2B parte 5](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas):
limpieza y tests** (≈ 2 días) → [§18.6](#186-orden-dentro-de-la-2b)

- Sacar el parche del "punto visto" de `NemesisPursuit` (su motivo de orden,
  `IsStandingWhereLost`, ya no existe) y el filtro de `Investigating` (S–M).
- El gizmo del radio de la creencia (S).
- Tests de la creencia y de la escalera (los del árbitro, `FocusArbiterTests`, ya están).
  `NemesisBelief` es un `MonoBehaviour` fuera de `WIRED.Nemesis.Logic`: hay que extraer su lógica
  (L). El replay de la escalera ya está (etapa A, `NemesisLadderValidator`): acá se le suman
  escenarios.

**Etapa G — [Fase 6](#fase-6--contra-jugadas-desbloqueables): contra-jugadas** (≈ 1 día de lo que
no está bloqueado) → [§19.7](#197-fase-6-ganchos-y-bloqueos)

- **G1 (S) `ChaseFlank`.** Necesita 2 o 3 waypoints alrededor de `Column_Loop` en la testbed.
- **G2 (M) `ExitAmbush`.** Después de la etapa E.
- **Bloqueado por nivel:** `ZoneDefense` y el "soltar y emboscar" que quedó de la
  [Fase 4](#fase-4--persecución-estancada-independiente---construida-commit-9eba9b46-salvo-soltar-y-emboscar)
  (M–L) necesitan puntos de emboscada puestos a mano. `NemesisAmbushPoint` no existe todavía, y la
  Zona 2 tampoco.
- **Bloqueado por arte:** `BurnHidingSpot` necesita el locker roto, su animación y su sonido. El
  llamado en sí es S. ([decisión 6](#decisiones-de-iñaki-del-0510))
- `CheckHidingSpots` y `PrioritizeSuspiciousSpots` los reemplaza la 2D, y ahora la etapa E. Casos
  6 y 8.

**Etapa H — [Fase 8](#fase-8--bajadas-entre-pisos-independiente): bajadas entre pisos** →
[§15](#15-bajadas-entre-pisos)

- **H1 (M–L) Rehacer el *Drop Lab* como un agujero en el piso** (pedido en el playtest del 28/09):
  un agujero por el que el Nemesis sólo baja, nunca sube. Una losa con un agujero y una sala abajo,
  en vez de los bloques macizos. Entre pisos hay 2.5–5 m, así que es una `Hang`; la `Hop` sólo
  sirve para bordes bajos. El código de la bajada sirve tal cual. El builder del lab actual se borró
  el 29/09; el viejo está en git (`a9980073`) y sirve de base.
  ([decisión 5](#decisiones-de-iñaki-del-0510))
- **H2, con el editor:** apagar *Generate Links* y rebakear (D10).
- **H3, bloqueado por animación:** los clips del §15.5, y clips propios de golpe de manos e impacto
  (hoy son provisorios). El controller ya tiene los seis estados (`Drop Look`, `Hop Takeoff`,
  `Hang Turn`, `Hang Release`, `Fall Loop`, `Land Heavy`), vacíos.
- **Jugar:** casos 12 a 16 y 55, y después poner bajadas en la Zona 2.

**Etapa I — Documentación y limpieza** (≈ 1 día) → [§19.6](#196-el-plan-contra-el-código)

- `CLAUDE.md` y `Nemesis-System.md` todavía describen el barrido borrado y dan por "siguiente" lo
  que ya está hecho. La búsqueda por mapa del 04/10 no está documentada en ningún lado.
- §13: sacar los casos 41 y 42, y corregir los que nombran el barrido o números viejos (1, 2, 6, 7,
  23, 33, 43, 45 y 52). Los 62 a 71 ya están, reconstruidos del handout.
- §11: anotar D38, D39 y D40, que hoy sólo están en `CLAUDE.md` y en comentarios del código. D37
  no aparece en ningún lado.
- §16.2: la lista de tests nombra uno borrado y le faltan los nuevos.
- Los comentarios que nombran tipos borrados, listados en el
  [§19.8](#198-diagnóstico-y-comentarios-viejos).
- Retirar `Handout-Busqueda-Nemesis.md` cuando todo lo suyo esté acá.

### Decisiones de Iñaki del 05/10

Iñaki tomó las seis recomendaciones. Las cuatro que son de diseño quedaron en el
[§11](#11-decisiones-abiertas) como D41 a D44.

1. **D41 — D38 con un escondite nunca usado:** el mapa solo puede abrirlo, con un umbral alto. Y
   `Investigating` deja de sortear escondites.
2. **D42 — La patrulla puede tomar el montacargas** cuando su ruta lo pide. No se cruza por un
   escondite sospechado ni por una creencia vieja en el otro piso.
3. **D43 — Ida y vuelta por el montacargas:** después de dos viajes siguiéndote, se planta a
   esperarte en el descanso.
4. **D44 — Montacargas sin energía:** el Nemesis tampoco puede usarlo.
5. **Agujero en el piso:** se mantiene D9, el jugador no cae.
6. **Fase 6:** entran sólo `ChaseFlank` y `ExitAmbush`. El resto espera al locker roto y a los
   puntos de emboscada.

### A decidir jugando

Valores que el trabajo del 04/10 dejó por defecto, más las tres decisiones del docx perdido. No
bloquean ninguna etapa.

| Qué | Cómo quedó | Con qué se cambia |
|---|---|---|
| Vista sin tope | Sostener ×2 y cazar ×2 se apilan con la escalada y el Director: el rango puede pasar los 20 m. | `View Hold Scale`, `View Hunt Scale` y `View Hunt Grow Time` (en 1 se apagan). Un tope sería un tunable nuevo. |
| "Lo vio entrar" | Vale hasta el rango con el que te estaba viendo (14 m en persecución): esconderte a la vista en un pasillo largo deja de servir. Desde el 07/10 la puerta tiene que estar dentro de su cono (C7). | No tiene tunable. |
| Rastro (C5) | Lo que caminó en los últimos 4 s, 2 m a cada lado, queda tapado. | `Search Map Trail Memory` y `Search Map Trail Radius` (0 lo apaga). |
| Visita debida (C6) | Recién oído o visto, un lugar lejos con al menos 1,5 % del valor entra al sorteo. | `Search Map Worth Threshold` (el mismo umbral, pedido a la parte del valor). |
| Espacios abiertos | Casi no se detiene a mirar, porque descarta lugares de lejos. | `Search Map Repick Share` (en 0 vuelve al ritmo de antes). |
| "Revisé todo" | Umbral 0,015: la búsqueda termina a los 6–9 s en un hall abierto y a los 10–13 s en pasillo o sala. | `Search Map Worth Threshold`. |
| "Estanca" | Cuenta en la primera ventana sin progreso, aunque sea una corrida recta y no un loop. | No tiene tunable. |
| Foco de cerca | A menos de 2,3 m de frente te ve al instante. | Revertir la llamada en `FieldOfView` y `LineOfSight.CheckConeSampled`. |
| D38 | Decidido: ver la decisión 1 (D41). | Tunables de la etapa E. |
| Aguante sin verte (etapa B) | Sin oírte, 0,56 s; oyéndote cerca, hasta 1,2 s. Después busca. | `Chase Hold Radius` y `Chase Hold Max Time`. El inspector de `SO_NemesisData` lo dibuja en *Perderlo de vista*. |
| Veredicto de ruta (etapa B) | Tarda 0,75 s en darse cuenta de que te subiste a un lugar al que no llega, y lo mismo en volver a perseguirte cuando bajás. | `Route Verdict Settle Time` (0 lo apaga). |
| Vista sostenida (B3) | Cerrado el 05/10: en la traza, las 27 pérdidas de vista en persecución fueron por tapar la línea de visión. | — |
| Rumbo al pasar a buscar (etapa C) | El primer lugar pesa ×4 hacia donde ibas y ×0,25 hacia atrás. | `Search Map Chase Heading Boost` (1 lo apaga). |
| Corrida abierta | Hoy siempre te escapás, pero tarda unos 10 s en vez de 2 o 3. | Del docx perdido: sólo queda esta línea. |
| `PeakFade` | 47 s. | Del docx perdido: sólo queda esta línea. |
| Hábito desde el primer escape | Sin detalle. | Del docx perdido: sólo queda el nombre. |

### Falta jugar de las fases ya construidas

Con sus casos del [§13](#13-casos-de-prueba). Lo marcado *(cambió)* se juega con la salvedad que
dice.

- **Búsqueda por mapa (03–04/10):** casos 62, 63, 65 y 67 a 71. Además, en la testbed con F9:
  - la recta: dejate ver a 6 m y corré. Sigue en `Chasing` hasta unos 14 m y no se frena cuando F9
    dice ESTANCADO;
  - la esquina: doblá a 4 o 5 m y seguí corriendo. Llega y sale sin quedarse quieto, y vuelve a
    verte si seguís en el pasillo;
  - el sigilo: patrullando te nota a 7 m parado y a 3,5 m agachado;
  - "estanca": una persecución larga alrededor de una mesa suma 1.
- **Etapa C, búsqueda (05/10 y 07/10):** casos 77 a 84, y otra vez 62, 63, 65 y 67.
- **Etapa B, titileo (05/10):** jugada. De los casos 72 a 76 quedó sin probar el 76.
- [Fase 0](#fase-0--ajustes-sin-código): esperas distintas por waypoint en F9, y las seis zonas
  del Director en F10.
- [Fase 2](#fase-2--el-nemesis-sabe-de-escondites---construida-2109-sin-commitear-falta-jugarla):
  casos 1–5, 11 y 17–21, en el *Hiding Lab* de la testbed (checklist H-1..H-10) o en `TestIñaki`.
  *(Cambió: en el caso 1, "lo vio entrar" ahora llega a 14 m.)*
- [Fase 2C](#fase-2c--aguantar-la-respiración-tiene-que-servir-independiente-primero---construida-2709-sin-commitear-falta-jugarla):
  casos 34–36 y 38.
- [Fase 2B parte 1](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas):
  no tiene casos propios; entra en "todos los casos de la 2B" de la etapa F.
- [Fase 2B parte 2](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas):
  casos 23 y 27 en la testbed, y el 36 en `TestIñaki`. *(Cambió: el barrido se borró. Los casos 41
  y 42 y el gizmo del barrido ya no aplican; el 23 ahora también vuelve a elegir cuando el lugar
  pierde valor.)*
- [Fase 2B parte 3](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas):
  casos 2, 9, 25, 34 y 43–48. F9: fila `búsqueda` ("tibia s/ventana", "se enfrió") y fila
  `presión` (acecho, persistencia, "vuelta en N s"). El 45 (el Hub) en una escena con Hub.
  *(Cambió: el tope de 30 s está apagado, `searchHardCap = 0`; el 43 y el 45 no valen como están.)*
- [Arreglos del playtest del 27/09](#189-playtest-del-2709--ajustes): perderlo lejos (fuera del
  oído) y ver que va al último punto, "tibia (sin contar)" en F9, y las puertas (una al costado del
  pasillo, cerrársela en la cara, y las selladas del escape). *(Cambió: F9 ya no dice "yendo al
  último punto"; ahora es `ancla:`.)*
- [Fase 3](#fase-3--contar-sin-reaccionar): casos 10 y 49–54, con F9 abierto (filas `hábitos`,
  `desbloquea` y `escondites`). Anotar cuántos de cada uno salen por partida, para ajustar los
  umbrales de `SO_CounterplayRules` con datos. Escondites en `TestIñaki`; la persecución y el *Hide*
  de F10, en la testbed. *(Cambió: "estanca" suma 1 por persecución; afecta el 52.)*
- [Fase 4](#fase-4--persecución-estancada-independiente---construida-commit-9eba9b46-salvo-soltar-y-emboscar):
  caso 7 *(mismo cambio)*. En la testbed, `Column_Loop` no tiene waypoints alrededor: sumar 2–3 o
  probar en `Cover_Pasillo`.
- [Fase 5](#fase-5--tensión-y-ritmo---construida-2209-sin-commitear-falta-jugarla): caso 9, y
  *Pico de tensión* y *Saltar silencio* en F10.
- [Fase 7](#fase-7--escalada-por-puzzles) (28/09): casos 56 y 57 en la testbed, con la sección
  *ESCALATION* de F10 (*Tier +* / *Tier -* / *Auto*) y la fila `escalada` de F9. En una escena con
  puzzles, resolver uno tiene que subir el nivel sin tocar F10.
- [Fase 8 parte 2](#fase-8--bajadas-entre-pisos-independiente): casos 12–16 y 55, con F9 (fila
  `bajada`), en el *Drop Lab* de la testbed: al sur de ENTRADA, por la puerta nueva de su pared
  sur. Hasta que H1 lo rehaga.
- [Voz y avisos](#162-lo-que-quedó-abierto) (28/09): caso 58 en el *Hiding Lab* (escondites) y 59
  en la testbed. *Validate Hiding Spots* ya pasa en la testbed (9 escondites, 29/09); falta en
  `TestIñaki`.
- [Fase 2B parte 4](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas)
  (28/09): casos 22, 24–26, 28–33 y 37 en la testbed, con los señuelos puestos (ya están en la
  escena), y el 60 (F9 fila `foco`: qué, cuánto vale y la pregunta que decidió). El 61 (el Director
  te oye más lejos caminando) con una presión de F10.
- [Fase 2D, la mitad del Nemesis](#fase-2d--memoria-de-escondites-después-de-la-2b) (28/09):
  casos 35, 39 y 40 en el *Hiding Lab* de la testbed o en `TestIñaki` (escondites). Para el 39,
  esconderse dos o tres veces en la misma mesa con él cerca, y después hacer ruido en esa zona.
  Valen hasta que la etapa E cambie el sorteo.

### Más adelante

- **El "asomarse" antes de cambiar de piso** (caso 66 del plan perdido): sólo queda el nombre.
- **La cabeza del modelo no gira con la mirada**, así que no se ve hacia dónde mira en la esquina.
  Es trabajo de rig.
- **Lo que quedó abierto del consejo** → [§16.2](#162-lo-que-quedó-abierto)
  - La animación `Pull Out` con el SFX de la puerta (con ella se decide si 0.8 s alcanza). El aviso
    audible al saber el escondite ya está (28/09, D34), con clip provisorio.
  - Container en escenas que no hornean `Default`: pasarlo a `Props` o sumar un
    `NavMeshModifierVolume` ([§14.4](#144-dónde-va-cada-pieza-nueva-del-plan)).
  - Tests de Play mode: no hay ninguno. EditMode sí: 14 archivos del Nemesis en
    `Assets/_Project/Tests/EditMode`. Los de la escalera y la creencia van en la etapa F.
- **Decisiones abiertas** → [§11](#11-decisiones-abiertas)
  - **D1** (captura o persecución al encontrarte escondido): a revisar con playtest y con la Fase 3.
  - **D14** (detección por la espalda del locker, decidida 2 a 1) y **D17** (clavar la mirada en vez
    de ir a mirar): a probar en playtest.
  - **D15** (container dominante): no se toca por ahora; se revisa con los datos de la Fase 3.
  - **D2** (romper escondites): necesita el arte del locker roto; va con la Fase 6.
  - **D3** y **D4** (lo aprendido sobrevive a la captura y decae lento): recomendaciones para el
    tracker de la Fase 3.
  - **D6** (espiar con la cámara) y **D8** (dificultad seleccionable): recomendaciones sin decidir.
  - **D9** y **D10** (bajadas): recomendaciones para las partes 1 y 3 de la Fase 8 (D11 y las
    nuevas D29 y D30 ya están aplicadas). **D12** (entrada por una bajada): más adelante, fuera de
    la Fase 8.
- **Zona 2 con su Director** ([D25](#11-decisiones-abiertas)): el Nemesis se activa ahí y, en
  Zona1, sólo en la cinemática final. La Zona 2 tiene que nacer con su Director armado
  ([§14.2](#142-activar-el-director-en-zona1-fase-0-sin-código)) y sus rutas validadas; hasta
  entonces, los §17 y §18 se prueban en la testbed y en `TestIñaki`.
- **Sueltos**
  - Zona1 no tiene escondites puestos ni el blend en su `CinemachineBrain`
    ([Fase 1](#fase-1--escondites-lado-jugador-prerrequisito---construida-commit-9eba9b46)).
  - El cue de activación está enganchado (28/09), pero el SO `sfx_nemesis_activacion` sigue sin
    clip. Lo usan el despertar por puzzle y el escape. Falta un clip propio para el aviso de
    escondite, que hoy es `voice_chase` provisorio, igual que el gruñido de las bajadas
    ([§1](#1-los-12-principios-contra-el-código), principio 7).

---

## ✅ Hecho

Lo construido, con fecha y commit. Lo que falta jugar de cada fase está en [Pendiente](#pendiente),
y los bloques de las fases terminadas, al final del [§9](#-hecho-fases-terminadas).

- ✅ **[Fase 0](#fase-0--ajustes-sin-código) — Ajustes sin código** (19/09, `359081fd`; el Director,
  rehecho el 22/09): `patrolWaitVariance` 0.6, `NemesisAudio` en el prefab, D5 y el Director
  activado en Zona1. Falta jugarla.
- ✅ **[Fase 1](#fase-1--escondites-lado-jugador-prerrequisito---construida-commit-9eba9b46) —
  Escondites, lado jugador** (21/09, `9eba9b46`): `HidingSpot`, `SO_HidingData`, `PlayerHiddenState`
  real, respiración por pulsos y `F` para aguantar.
- ✅ **[Fase 2](#fase-2--el-nemesis-sabe-de-escondites---construida-2109-sin-commitear-falta-jugarla)
  — El Nemesis sabe de escondites** (21/09, `eadbb497`), con los arreglos del consejo. Falta
  jugarla.
- ✅ **[Fase 2C](#fase-2c--aguantar-la-respiración-tiene-que-servir-independiente-primero---construida-2709-sin-commitear-falta-jugarla)
  — Aguantar la respiración tiene que servir** (27/09, sin commitear). Falta jugarla.
- ✅ **[Fase 2B parte 1](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas)
  — Creencia** (27/09, sin commitear): `NemesisBelief`, pistas separadas, `HearsLead` y
  `HasFreshLead`. Falta jugarla.
- ✅ **[Fase 2B parte 2](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas)
  — Dónde busca** (27/09, sin commitear): `Searching` barre puntos del NavMesh alrededor de la
  creencia (`SearchSweepRules` + `NemesisFreeRoam`). Incluye:
  - la intercepción y la ruleta de waypoints, afuera (D24);
  - el D22;
  - la pausa al llegar;
  - el filtro `|Δy|` del §16.2;
  - la fila `búsqueda` en F9.

  Compila y pasan los tests EditMode (36/36). Falta jugarla.
- ✅ **[Fase 2B parte 3](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas)
  — Cuánto busca, y el Director** (27/09, sin commitear). Incluye:
  - la búsqueda que se enfría (`IsSearchWarm` + `SearchCooling`) en vez de 15 s fijos, sin renovarse
    con lo que se oye desde el Hub;
  - la búsqueda corta después de investigar un ruido tuyo (D26);
  - en el Director: persistencia prestada por el ritmo, "vuelve a pasar", silencio por encuentros,
    `IsEncounterOver` desde la creencia y la presión limpiada al suspenderse;
  - los pesos de ruta vivos en la patrulla por cúmulos, y el reinicio de la patrulla cuando el grafo
    se rearma;
  - la cola de la música a 50 s;
  - los arreglos de la revisión de la parte 2.

  Compila en Unity y pasan los tests EditMode (71/71, `SearchCoolingTests` incluidos). Falta jugarla.
- ✅ **[Fase 2B parte 4](#fase-2b--creencia-fusionada-y-búsqueda-antes-de-la-3---partes-1-a-4-de-5-construidas)
  — La elección completa** (28/09, sin commitear). Incluye:
  - `FocusArbiter` (puro, en `WIRED.Nemesis.Logic`) con las preguntas del §17.4, y `NemesisChoice`,
    que guarda el **foco**: vos, una pista o un vistazo;
  - el peldaño "su atención está en una pista" (`FocusIsLead`), arriba del presupuesto de búsqueda
    (D35);
  - `Investigating` siguiendo el foco, con una pausa corta al cambiar de idea;
  - `NemesisDecoyBreaker` leyendo el foco e instalándose solo, y los señuelos con tipo;
  - la sospecha compartida: un ruido suave tuyo suma al medidor del vistazo (D36);
  - el builder de señuelos de la testbed (sin correr: el editor estaba abierto);
  - la fila `foco` en F9.

  Una revisión de código del mismo día encontró siete fallas, ya arregladas. La más grave: una alarma
  que perdía una vez quedaba ignorada mientras sonara. El detalle está en el bloque de la parte 4 del
  §9. Compila (Roslyn contra los `.rsp` de Unity, Editor incluido) y pasan los tests EditMode fuera
  de Unity (100/100, `FocusArbiterTests` incluidos). Falta jugarla.
- ✅ **[Arreglos del playtest del 27/09](#189-playtest-del-2709--ajustes)** (sin commitear). Incluye:
  - el prefab ya no duerme al Nemesis fuera de Zona1;
  - la búsqueda va primero al último punto y cuenta el silencio desde que llega;
  - `Investigating` alcanza tu último ruido, y va a lo que lo trajo;
  - la D26 con `IsInvestigationWarm`;
  - las puertas: la hoja que ya toca, la que se le cierra en la cara, y las que no puede abrir cortan
    el NavMesh;
  - el montacargas de la testbed (ver abajo).

  Compila (Roslyn contra los `.rsp` de Unity). Falta jugarlo.
- ✅ **Montacargas de la testbed** (27/09, sin commitear). Los logs y trazas del playtest mostraban
  tres fallas:
  - **Nunca caminaba a la cabina**: se deslizaba en línea recta a través de la barrera, en los dos
    pisos. El piso de la sala seguía debajo de la cabina, así que el NavMesh estático cubría el hueco
    y el link de abordaje no unía nada. Ahora un volumen Not Walkable (`Props`) saca el hueco del
    NavMesh estático.
  - **La cabina quedaba 22 cm bajo el piso alto**: la marca `End` del prefab está a la altura de Zona1.
    Ahora tiene un override a 5.37 m sólo en la instancia de la testbed.
  - **Subía y volvía a bajar**: al llegar arriba, "ya se comprometió con el montacargas" lo retenía
    en `Traversing` y un ruido de abajo lo mandaba de nuevo a la cabina. Ahora un viaje terminado
    suelta el compromiso (`HasJustEndedRide`), como la bajada.

  Además, un estado fijado con F10 ya no le saca el cuerpo al montacargas: con `Searching` fijado,
  el cruce se deshacía y lo teletransportaba. Aplicado y horneado en batchmode: el hueco quedó sin
  NavMesh estático y las dos marcas, sobre el piso. Falta jugarlo (G22 y G23 del checklist).
- ✅ **Montacargas: atravesaba paredes y quedaba 15–30 s en `Traversing`** (playtest del 28/09,
  arreglado esa noche, sin commitear). No venía del trabajo del día: las trazas de Zona1 y de la
  testbed lo muestran desde el código del 04–06/09.
  - **Causa:** para bajarse del link del hueco antes de caminar a la cabina, `LeaveCurrentLink`
    llamaba a `ResetPath`. Sobre un link, Unity lo completa: el Nemesis aparecía en el otro piso.
    Desde ahí caminaba 12 s hacia una puerta que no alcanzaba, y el plan B lo llevaba en línea recta
    a través de la losa y las paredes.
  - **Arreglo** (`NemesisElevatorUser`):
    - se baja del link con un `Warp` al extremo de su piso, y recién después limpia el camino;
    - si igual quedó en el otro piso, vuelve al descanso antes de subir (`WrongFloorAfterStepOff`);
    - un link común cruzado a mano (la puerta de la cabina) ya no lo deja 12 s comprometido;
    - en F10, los warps se apagan mientras cruza: uno a mitad del viaje dejaba al cuerpo a 40 m
      con la cabina todavía moviéndolo.
  - La misma función sirve para abandonar el montacargas y para echarse atrás en una bajada (D30),
    así que esos casos tenían el mismo error.
  - **Pendiente, necesita el editor:** en la testbed, el link de abordaje de arriba (`End`) sigue
    sin unir nada ("joins NOTHING"). La losa de `PLANTA_ALTA` no entra al hueco, así que no es el
    bloqueo de abajo repetido: hay que mirar la cabina arriba con el gizmo de la puerta. Mientras
    tanto, arriba sube y baja por el plan B, en línea recta a través de la barrera.
  - Falta jugarlo: G22–G27.
- ✅ **[Fase 3](#fase-3--contar-sin-reaccionar) — Contar sin reaccionar** (27/09, `9bdd3c90`).
  Incluye:
  - `PlayerHabitTracker`, en la escena `Data`;
  - `SO_CounterplayRules`, `EExploitKind` y `ECounterplay`;
  - el registro de los cuatro exploits del §5.3, sin tocar el FSM (con `OnSearchEnded` y
    `OnChaseStalled`, nuevos);
  - las filas `hábitos`, `desbloquea` y `escondites` en F9, la sección HABITS en F10 y el chequeo en
    el validador;
  - los primeros tests EditMode (`HabitLedgerTests` y `HidingStayBookTests`), en el asmdef nuevo
    `WIRED.Nemesis.Logic`;
  - los arreglos de una revisión de código del mismo día: un escape se confirma 5 s después de
    salir, se cuenta una búsqueda por cacería, y una captura sin persecución previa ya no cuenta
    como escape al Hub.

  Todavía no hay contra-jugadas. Compila y pasan los tests en el Test Runner de Unity. Falta jugarla.
- ✅ **[Fase 2D](#fase-2d--memoria-de-escondites-después-de-la-2b), la mitad de datos** (27/09,
  `9bdd3c90`): el medidor de uso por escondite (D23) y la API que va a leer la elección
  (`GetSpotUsage`, `OpenChance`, `IsPrioritySpot`, `IsBurnable` y `CollectUsedSpots`).
- ✅ **[Fase 2D](#fase-2d--memoria-de-escondites-después-de-la-2b), la mitad del Nemesis** (28/09,
  sin commitear): `Investigating` y `Searching` sortean los escondites usados de la zona que revisan
  (pregunta 7 del §17.4) y van a abrir el que sale. La primera vez sólo cuenta un escondite que el
  jugador pueda ver u oír abrir (R3). De paso, un segundo ruido desde el mismo escondite lo vuelve
  sospechoso (D22, la otra mitad). Compila (Roslyn). Falta jugarla.
- ✅ **[Fase 4](#fase-4--persecución-estancada-independiente---construida-commit-9eba9b46-salvo-soltar-y-emboscar)
  — Persecución estancada** (21/09, `9eba9b46`), salvo "soltar y emboscar", que va con la Fase 6.
- ✅ **[Fase 5](#fase-5--tensión-y-ritmo---construida-2209-sin-commitear-falta-jugarla) — Tensión
  y ritmo** (22/09, `4a163d72`): `NemesisTension`, `SO_DirectorPacing`, la retirada y la
  sensibilidad creciente. Falta jugarla.
- ✅ **[Fase 7](#fase-7--escalada-por-puzzles) — Escalada por puzzles** (28/09, sin commitear).
  Incluye:
  - `NemesisEscalation`, en la escena `Data`. Instala como *baseline* del Nemesis una copia de
    `SO_NemesisData` con el nivel que corresponde a los puzzles completos.
  - `SO_NemesisEscalation`, con tres niveles: base, 2 puzzles y 3 o más. Suben la vista, el oído y
    la variación de ruta; nunca la velocidad.
  - `InstallBaseline` y `AuthoredData` en `NemesisStateManager`.
  - `OnBaselineChanged`, para que el Director rearme su préstamo sobre el nivel nuevo.
  - La fila `escalada` en F9, la sección *ESCALATION* en F10 y el chequeo en el validador.
  - `EscalationRulesTests`.

  Compila (Roslyn) y pasan los tests EditMode fuera de Unity. Revisada el 28/09 sin problemas
  graves. Se arreglaron tres cosas: la primera ronda de patrulla salía sin el nivel, cada Retry
  dejaba una copia suelta y la consola F10 cortaba la sección de escalada en pantallas bajas.
  La verificación en Unity quedó pendiente porque el editor estaba abierto. Al dar Play, F9 tiene
  que mostrar `escalada: nivel 0 (0 puzzles)`, y la consola no tiene que avisar que falta
  `SO_NemesisEscalation`. Falta jugarla.
- ✅ **[Fase 8](#fase-8--bajadas-entre-pisos-independiente) parte 2 — Bajadas entre pisos, el
  código** (27/09, sin commitear). Incluye:
  - `NemesisDropLink`, y la bajada como tercera rama de `NemesisElevatorUser`: se asoma y gruñe,
    salta o se descuelga, aterriza y no agarra a nadie mientras se recupera;
  - `CrossedDrop` en la ruta, así `Traversing` también ve las bajadas;
  - el costo por estado (D11), D29 y D30;
  - la fila `bajada` en F9, el gizmo y el validador;
  - el área `NemesisDrop` nombrada;
  - `DropPathTests` en `WIRED.Nemesis.Logic`.

  Compila y pasan los tests EditMode en Unity (59/59). Faltan las partes 1 y 3 y jugarla.
- ✅ **Drop Lab en la testbed** (27/09, sin commitear; §15.6 paso 5). Lo arma
  *Tools/Nemesis/Build Drop Lab (NemesisTestbed)* (`NemesisTestbedDropLabBuilder`; ya corrido, el builder
  se borró el 29/09 y está en git). Incluye:
  - una sala al sur de ENTRADA con un entrepiso de 3.6 m (`Drop_Hang`) y otro de 2 m (`Drop_Hop`),
    cada uno con su rampa de vuelta;
  - barandas invisibles para el jugador (D9) en la capa `Ignore Raycast`;
  - `Route_DropLab`, que sube y baja los dos entrepisos (caso 16);
  - el horneado compartido con el Bug Lab (`TestbedNavMeshBake`).

  Se armó en batchmode. Pasan todos sus chequeos: las dos puntas sobre el NavMesh, la vuelta por la
  rampa y la ruta completa. El validador no marca nada de las bajadas; sólo avisa, como nota, que
  faltan los estados de animación (parte 3).
- ✅ **Hiding Lab en la testbed** (29/09, sin commitear). Los escondites de la *Hiding Test Area*
  de `TestIñaki`, repartidos en pasillos y salas cerradas al este de PASILLO (x 2..28, z 6..34), para
  jugar H-1..H-10 y los casos de la Fase 2, la 2D y el 58 en la testbed. Lo arma
  *Tools/Nemesis/Build Hiding Lab (NemesisTestbed)* (`NemesisTestbedHidingLabBuilder`, se borra
  una vez commiteada la escena). Incluye:
  - 9 escondites con id `tb_*`: locker a la vista, a la vuelta de la esquina y en un callejón sin
    salida, dos mesas, una fila de tres lockers con un pasillo detrás, y el container;
  - un piso por sala (`HL_<SALA>_Floor`), así `NemesisRooms` las distingue;
  - un `NavMeshModifierVolume` Not Walkable por escondite, en `Props` (la testbed no hornea
    `Default`, ver §14.4);
  - `Route_HidingLab` (WP_00..09) con las paradas que mide el checklist, carteles y marcas de
    distancia en el piso.

  Se armó en batchmode: pasan todos sus chequeos de NavMesh y *Validate Hiding Spots* (9/9).
- ✅ **Voz y avisos del Nemesis, y la `ExitPose` en el validador** (28/09, sin commitear; §16.2 y
  principio 7). En `NemesisAudio`:
  - el aviso de que **sabe** en qué escondite estás, a más de 2 m de la puerta (D34, caso 58);
  - "te perdí" al volver a patrullar después de una búsqueda vacía (caso 59);
  - el cue de activación cuando lo despierta un puzzle.

  Clips provisorios en el prefab: `voice_chase` a pitch 0.8 para el aviso y `voice_lost_01/02` para
  "te perdí"; el SO de activación sigue sin clip. *Validate Hiding Spots* revisa la `ExitPose`: a su
  alcance desde la puerta (D16), fuera de colliders sólidos y con piso abajo. Compila (Roslyn); falta
  jugarlo.
- ✅ **Director de Zona1 rehecho** (22/09, `4a163d72`): seis zonas, dos disparadores y ninguna
  palanca a menos de 6 m del Hub (C5). Queda inerte mientras el Nemesis duerma en Zona1 (D25). →
  [§14.1](#141-el-director-hoy-estado-en-zona1)
- ✅ **Mejoras de editor** (22/09): `[PressureZoneId]` y los chequeos del Director en *Validate
  Navigation Setup*. → [§14.5](#145-mejoras-chicas-de-editor-para-hacer-en-el-camino)
- ✅ **Búsqueda por el mapa de posibilidades** (03–04/10, `00d97c61` y `145eaf5e`). Era el
  `Plan-Busqueda-Nemesis.md`, que se perdió ([§19.1](#191-qué-se-perdió-y-qué-quedó)); lo que sigue
  sale del handout y del código. Falta jugarla y documentarla. Incluye:
  - el oído ya no es un GPS (`HearingLocalization`, D39), y sólo camina al punto de la evidencia
    cuando el punto significa algo;
  - el mapa de posibilidades (`PossibilityGraph`, `PossibilityMap`, `NemesisPossibilityMap`) y la
    búsqueda que elige entre hasta 8 lugares por valor ÷ tiempo de llegada (`NemesisSearchPicker`,
    `SearchPickRules`). Reemplaza al barrido: `SearchSweepRules`, `NemesisFreeRoam` y `NemesisRooms`
    se borraron;
  - la vista adaptativa (`AdaptiveViewRange`): te nota a 7 m, te sostiene hasta 14 m y, si te
    pierde cazándote, el rango para volver a verte crece de 7 a 14 m en 2 s. Y el sentido de
    espaldas (`VisionZones`);
  - la mirada en las esquinas (`ChaseGaze`): al perderte mira 6 m más allá en el rumbo que llevabas;
  - "estanca" suma 1 por persecución, y un salto de más de 10 m por NavMesh no cuenta
    (`ChaseStallCounter`, `ChaseGapWindow`);
  - el foco de cerca: a menos de 2,3 m de frente te ve al instante;
  - del mismo `00d97c61`, sin documentar: el agarre de la captura, `NemesisArmWallGuard`,
    `NemesisArmThud` y `ElevatorPower`.

  247 atributos de test EditMode. No entró su tanda 2 (escondites por el mapa y patrulla después de
  la caza): es la etapa E de [Pendiente](#pendiente).
- ✅ **Etapa A — Preparación** (05/10, sin commitear). Compila en batch y pasan los 254 tests EditMode
  (7 nuevos, `SightMissTests`). Falta ver una traza nueva después de jugar. Incluye:
  - `SO_NemesisData.asset` reserializado: 57 campos nuevos con su valor por defecto y 10 campos
    muertos menos (los del barrido y la intercepción, ya sin código). Ningún valor existente cambió;
  - `Draw Possibility Map` prendido en el prefab `Nemesis`;
  - la traza (`NemesisTraceRecorder`): una fila `sight` cada vez que gana o pierde la vista, y las
    columnas `view_range` (hasta dónde llegó el barrido, con el agachado), `view_scale`, `view_why`,
    `crouch`, `hidden`, `breath`, `sight_miss` (por qué no lo ve: `range`, `cone`, `occluded`,
    `periphery`, `hidden`), `lost_sight_why` y `since_lost_sight`. El motivo lo arma `FieldOfView`
    con lo que el barrido ya calculó (`SightMiss`, en `WIRED.Nemesis.Logic`), sin rayos extra:
    `LineOfSight.CheckConeSampled` devuelve además si alguna muestra estaba dentro del cono;
  - *Tools > Nemesis > Validate Ladder* (`NemesisLadderValidator`, también dentro de *Validate
    Navigation Setup*): la paridad entre el asset y `BuildDefaultLadder()` (hoy, 22 reglas iguales)
    y un replay de la escalera real sobre situaciones escritas. Hoy reproduce el titileo T2 (20
    cambios de estado en 4 s) y T1 (14), y la búsqueda con creencia vieja de M1; van marcados como
    conocidos hasta las etapas B y D. No hizo falta extraer `NemesisDecision`: expone
    `IsInsideDwell`, `IsHeldBack` y `ResolveThreshold`, y el replay usa esa misma histéresis.
- ✅ **Etapa B — Titileo entre `Chasing` y `Searching`** (05/10, sin commitear). Compila en batch,
  pasan los 263 tests EditMode (9 nuevos, `SettledVerdictTests`) y *Validate Ladder* no reporta
  problemas. Falta jugarla. Incluye:
  - el peldaño "todavía sabe dónde está" (el 23.º, en el asset y en `BuildDefaultLadder()`), con dos
    preguntas nuevas al final de `ENemesisPredicate`: `SightAgeUnder` (hace cuánto lo vio) y
    `BeliefRadiusUnder` (qué tan seguro está de dónde está, en metros);
  - `Chase Hold Radius` (3 m) y `Chase Hold Max Time` (1,2 s) en `SO_NemesisData`, como umbrales
    nuevos de `ENemesisThreshold`;
  - `SettledVerdict` (`WIRED.Nemesis.Logic`) y `NemesisPathOracle.IsUnreachableSettled`: el veredicto
    de ruta que lee `IsBeliefUnreachable` tiene que sostenerse `Route Verdict Settle Time` (0,75 s);
  - la sección *Perderlo de vista* del inspector de `SO_NemesisData`: una línea de tiempo con cuánto
    aguanta sin verte (sin oírte y oyéndote), el tope, y el veredicto de ruta contra sus consultas;
    y tres chequeos nuevos;
  - en el replay del validador: la edad de la última vista, el radio de la creencia y el veredicto
    de ruta asentado, y cinco escenarios (dos que eran `KnownIssue` y tres de control).
- ✅ **Etapa C — Búsqueda, primera parte** (05/10, sin commitear). Compila en batch y pasan los 272
  tests EditMode (9 nuevos en `SearchPickRulesTests`). Falta jugarla. Incluye:
  - el primer sorteo de la búsqueda después de una persecución tira hacia donde ibas
    (`SearchPickRules.HeadingAlignment` / `HeadingWeight`, `NemesisSearchPicker.HeadingHint`,
    `Search Map Chase Heading Boost` en `SO_NemesisData`, con su chequeo en el inspector);
  - el uso de un escondite se cuenta al salir (WIR-057);
  - si te ve y no puede llegar, se acerca lo más que puede y te mira (`TickWatch`), y la búsqueda no
    se enfría mientras te ve.

  Commiteada en `417ce019`.
- ✅ **Etapa C — Búsqueda, segunda parte** (07/10, sin commitear) → [§19.9](#199-análisis-de-causas-del-0710-wir-057-058-y-062).
  Compila (los cuatro ensamblados, con el Roslyn de Unity sobre los `.rsp` de Bee, con el editor
  abierto) y pasan 280 de 285 tests EditMode corridos fuera de Unity (13 nuevos: 9 en
  `PossibilityMapTests`, 4 en `SearchPickRulesTests`). Los 5 de `ChaseGazeTests` llaman a
  `Quaternion.AngleAxis`, que fuera de Unity no corre: fallan igual con la versión anterior. Falta
  correrlos en el Test Runner y jugarla (casos 80 a 84). Incluye:
  - C3: el ruido y el avistamiento se siembran por distancia caminando (`PossibilityMap.CollectAlongEdges`,
    nodo de arranque validado con `NavMesh.Raycast` en `NemesisPossibilityMap.StartNodeFor`), y el
    "acá no está" limpia hasta `EffectiveViewRange` × `Search Map Clear Range Scale`;
  - C5: el rastro (`PossibilityMap.Block`, `NemesisPossibilityMap.RecordTrail` / `BlockTrail`,
    `Search Map Trail Memory` y `Search Map Trail Radius` en `SO_NemesisData` con dos chequeos en el
    inspector, cuadrados grises en el gizmo del mapa y "rastro tapa N" en F9);
  - C6: la visita debida (`SearchPickRules.TakesPart`, `NemesisSearchPicker.TryPick(owedVisit)`,
    `NemesisSearchingState.IsOwedVisit`, "se lo debe a lo que sintió" en F9);
  - C7: "lo vio entrar" pide el cono (`NemesisHidingAwareness.HandleEntered`).

  Falta de la etapa: jugar el I-c para DIS-002, y las decisiones del §19.9.
- ✅ **Revisión del consejo** (21/09): cuatro modelos revisaron la Fase 2 y encontraron un bug
  bloqueante, ya corregido. → [§16](#16-revisión-del-consejo-21092026)
- ✅ **Análisis de percepción y creencia** (27/09). → [§17](#17-percepción-y-creencia)
- ✅ **Análisis de búsqueda y del Director** (27/09). →
  [§18](#18-búsqueda-dónde-y-cuánto-y-el-director)
- ✅ **Chequeos en Unity** (batchmode, 27/09): el working tree con la 2C, las partes 1 a 3 de la 2B,
  la Fase 3 y la Fase 8 parte 2 compila entero, Editor incluido, y pasan los 71 tests EditMode. Se
  midió también el NavMesh de Zona1 y de la testbed ([§18.3](#183-el-director-qué-hace-de-verdad)).
- ✅ **Decisiones tomadas** en el [§11](#11-decisiones-abiertas): D5, D13, D14, D20, D21, D22 (en el
  §17.6), D23, D24, D25, D26, D27, D28, D29, D30, D31, D32, D33, D34, D35, D36 y, del 05/10, D41 a D46. Ya aplicadas, aunque la tabla no las marque: D7 y
  D17 (Fase 2), D18 y D19 (2B parte 1), D11 (Fase 8); D16 la respondió el consejo.

### Historial

Las notas de estado que antes abrían el plan, de la más vieja a la más nueva.

> Relevado contra el código el 14/09/2026 (rama `iña`, commit `79c2652`).
>
> La guía de armado ([§14](#14-cómo-se-arma-en-unity)) se relevó el 15/09/2026 leyendo escena,
> prefabs y assets como YAML, sin conector MCP de Unity: lo marcado *verificar en el editor* no se
> pudo abrir.
>
> **Revisado el 19/09/2026 contra `6703f9d`** (48 commits después). El Nemesis casi no cambió y nada
> del plan se construyó. Se corrigieron tres datos que ya estaban mal (`patrolWaitVariance`,
> `stateLoops`, radios de ruido) y se incorporaron cambios de diseño posteriores: M2 es módulo fatal,
> la captura pausa el timer, se borró `NemesisTestSceneBuilder`, hay estados nuevos del jugador
> (levantarse, empujar la caja). Se agregó el [§15](#15-bajadas-entre-pisos) (bajadas entre pisos).
>
> **Estado al 22/09/2026:** construidas las Fases 0, 1, 2, 4 y 5 (la 2 y la 5 falta jugarlas);
> pendientes la 3, 6, 7 y 8. La Fase 2 pasó por una revisión de cuatro modelos ([§16](#16-revisión-del-consejo-21092026))
> que encontró un bug bloqueante, ya corregido, y ajustó el modelo de los §3.3–§3.5. El Director de
> Zona1, que se había perdido en el merge `16b1962c` (20/09), se rehízo el 22/09 con seis zonas que
> cubren los 39 waypoints, y ahora ninguna palanca puede actuar a menos de 6 m del Hub (C5)
> ([§14.1](#141-el-director-hoy-estado-en-zona1)).
>
> **27/09/2026:** se agregó el [§17](#17-percepción-y-creencia) (percepción y creencia) y la
> Fase 2B. Un análisis del código mostró que vista, oído y creencia **no suman: compiten** — lo que se
> sintió en el último playtest — y que el plan daba ese punto por resuelto (§1, principio 1). Va antes
> de la Fase 3.
>
> **27/09/2026, más tarde:** se agregó el [§18](#18-búsqueda-dónde-y-cuánto-y-el-director) después de
> jugar la 2C:
>
> - Al perderte, `Searching` elige **waypoints** en vez de puntos del NavMesh, y deja de buscar con un
>   reloj fijo de 15 s.
> - El Director, relevado contra el código y medido en Unity, no se ve nunca en Zona1 (el Nemesis
>   duerme todo el gameplay), y en la testbed su sensibilidad creciente no puede arrancar.
> - Los arreglos entran en la Fase 2B, que queda en cinco partes (las 2 y 3 son las nuevas).
> - El working tree con la 2C y la 2B parte 1 compila en Unity (batchmode, 27/09).
>
> **27/09/2026, noche:** construida la 2B parte 2 (dónde busca). Decididos D20 (gana la evidencia
> del jugador), D24 (se saca la intercepción), D25 (el Nemesis es de la Zona 2; en Zona1, sólo la
> cinemática final) y D26 (investigar un ruido tuyo escala a una búsqueda corta). El plan se
> reorganizó: lo pendiente arriba, lo hecho en esta sección. **Corrección** a la nota anterior: en la
> testbed la sensibilidad creciente **sí** puede arrancar. Que el 94 % del NavMesh quede a menos de
> 12 m de alguna ruta es cobertura en el espacio; en el tiempo, simulando la patrulla, el Nemesis está
> a menos de 12 m del jugador un 15–20 % del tiempo. Lo que la frena en un playtest son los encuentros
> ([§18.3](#183-el-director-qué-hace-de-verdad)).
>
> **27/09/2026, más tarde todavía:** construida la 2B parte 3 (cuánto busca, y el Director). La
> búsqueda se enfría en vez de vencerse; el Director presta persistencia, vuelve a pasar después de
> una búsqueda vacía y suelta la presión al suspenderse, y los pesos de ruta volvieron a llegar a la
> patrulla por cúmulos. El proyecto entero compila en Unity y pasan los 71 tests EditMode. Lo que
> sigue es la parte 4 (la elección completa).
>
> **27/09/2026, playtest de las partes 2 y 3** en el blockout de Zona1 (§18.9). Identifica mejor,
> pero con distancias grandes no iba al último punto, la búsqueda duraba poco, se quedaba quieto entre
> estados y atravesaba algunas puertas. Además el prefab dormía al Nemesis en la testbed y en
> `TestIñaki`. Todo arreglado ese mismo día; falta volver a jugarlo.
>
> **28/09/2026:** construidas la 2B parte 4 (la elección completa: el Nemesis tiene un **foco** y
> cambia de idea sólo por algo que vale claramente más) y la mitad del Nemesis de la 2D (abre los
> escondites que usaste en la zona que revisa). Decididos D35 (una pista le gana al presupuesto de
> búsqueda) y D36 (un ruido suave comparte el medidor del vistazo, sin llegar solo a avistamiento).
> Arreglado el D32: el préstamo de sentidos del Director escala también el radio de tus ruidos. Lo que
> sigue es la parte 5 (limpieza y tests).

---

## Índice

- [Pendiente](#pendiente)
- [✅ Hecho](#-hecho) · [Historial](#historial)

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
17. [Percepción y creencia](#17-percepción-y-creencia)
18. [Búsqueda: dónde y cuánto, y el Director](#18-búsqueda-dónde-y-cuánto-y-el-director)
19. [Rediagnóstico del 05/10/2026](#19-rediagnóstico-del-05102026)

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
| 1 | **Escondites.** El lado jugador no existe. El lado Nemesis es binario (escondido = ciego) y, con los lockers del proyecto, **inmunidad total** — ver [§3.3](#33-hallazgo-con-los-lockers-actuales-esconderse-es-inmunidad-total). | ✅ Construido (Fases 1 y 2); falta jugarlo |
| 2 | **Nadie cuenta los hábitos del jugador.** No hay ninguna contra-jugada. Es el anti-cheese entero. | Alta |
| 3 | **No se detecta la persecución estancada.** El jugador corriendo (4.5 m/s) es más rápido que el Nemesis persiguiendo (3.0 m/s): **un loop alrededor de una columna es un exploit hoy**, sin escondites. Sigue siéndolo con M1 (3.6 m/s), y M2 ya no lo acorta porque es el módulo fatal ([C4](#c4--el-loop-alrededor-de-un-obstáculo-el-bug-de-la-mesa-de-dimitrescu)). | ✅ Construido (Fase 4) |
| 4 | **El Director no mide tensión ni administra ritmo.** Sólo reacciona a pedidos (puzzles, API). No hay Relax ni retirada. | ✅ Construido (Fase 5, 22/09); falta jugarlo |
| 5 | **Escalada por progreso** (spec Nemesis §7.2) sin hacer. | Media, diferida por diseño |
| 6 | **La creencia no fusiona los sentidos.** Vista y oído no suman: `TryGetBelief` se queda con el más fresco y descarta el otro, el oído no distingue al jugador de un señuelo o del Director, y cada estado parcha esa desconfianza a su manera. Se siente como sentidos que se contradicen — ver [§17](#17-percepción-y-creencia). | **Alta.** Va antes de la Fase 3: hábitos y contra-jugadas leen la creencia |

Y hay cosas que el análisis recomienda y que **acá no conviene hacer**: un `NoiseBus`, Unity
Behavior, cuatro conos nuevos, ductos/backstage, santuarios de luz, roles de escuadra y LOD de IA.
El porqué, en [§2.3](#23-lo-que-no-conviene-copiar).

---

## 1. Los 12 principios contra el código

✅ hecho · 🟡 parcial · ❌ falta

| # | Principio del análisis | Estado | Dónde está hoy | Brecha |
|---|---|---|---|---|
| 1 | Separar percepción, conocimiento y decisión | 🟡 | `FieldOfView` / `FieldOfListening` → `NemesisStateManager.TryGetBelief` / `BeliefAge` → `NemesisDecision` + `SO_NemesisPriorities`. La **decisión** sí está separada: los estados sólo ejecutan. | El **conocimiento** no: `TryGetBelief` no es un modelo sino un selector (el sensor más fresco gana, el otro se descarta), y los estados vuelven a leer los sensores directo para esquivarlo. Ver [§17](#17-percepción-y-creencia). Lo nuevo sigue entrando igual: **un predicado y un peldaño, nunca un estado que decide**. |
| 2 | El agente no hace trampa; el director sí | ✅ | Persecución y búsqueda leen la creencia y la velocidad **observada** (`FieldOfView.LastKnownVelocity`). Sólo dos lecturas del jugador real, ambas deliberadas: `ZoneBiasUsesRealPlayer` (elige zona, no waypoint) y `CanReachPlayerNow` (el agarre). | Las contra-jugadas nuevas tienen que pasar el mismo filtro (ver regla R4, §5). |
| 3 | La detección es un acumulador | 🟡 | Banda periférica de 170° que llena `Awareness`; foco de 80° instantáneo a propósito (es un peldaño *interrupt*); agachado ×0.5 de alcance; escondido, visibilidad residual por tipo (Nivel B, Fase 2). | **El oído no acumula:** un solo barrido audible ya es `HearsPlayer` y manda a `Investigating`. **Los sentidos no se cruzan:** un vistazo y un paso en el mismo lugar no suman certeza. Sin término de luz. Ver [§17](#17-percepción-y-creencia). |
| 4 | Administrar la tensión, no maximizarla | 🟡 | `NemesisTension` (medidor + BuildUp/SustainPeak/PeakFade/Relax) y `NemesisDirector` (retirada en Relax, sensibilidad creciente tras 90 s de silencio), con `SO_DirectorPacing`. | Construido el 22/09, sin jugar: los números del §12 son puntos de partida. |
| 5 | Nada guionado para el "cuándo" y el "dónde" | 🟡 | Patrulla por ruleta, cúmulos, satélites; spawn y entrada muestreados. | Los disparadores del Director son siempre "al completar el puzzle". Aceptable: el *qué* puede ser fijo. |
| 6 | Incertidumbre estructurada | 🟡 | 15 % de invertir la ronda, 15 % de saltear un waypoint; `patrolWaitVariance` 0.6 (Fase 0): la espera en cada waypoint varía 0.9–2.1 s. | La creencia no tiene incertidumbre propia: es un punto, no una zona, y la búsqueda no puede achicar un radio que no existe ([§17](#17-percepción-y-creencia)). |
| 7 | Anticipación dramática | 🟡 | Pasos reales, ocluidos por pared; puertas que suenan al abrirlas; música de persecución que se sostiene durante la búsqueda (D5); `NemesisAudio.stateLoops` en el prefab con los cinco estados (Fase 0). Desde el 28/09: aviso cuando **sabe** en qué escondite estás (D34), "te perdí" al volver a patrullar (`voice_lost_*`) y cue de activación enganchado a `OnActivated`. Las bajadas se anuncian con un gruñido (Fase 8). | El aviso de escondite y el gruñido usan `voice_chase` provisorio, y al cue de activación le falta el clip. No muestra la duda: no gira hacia un ruido antes de ir ([§17](#17-percepción-y-creencia)). |
| 8 | Legibilidad por encima de inteligencia | ✅ | `SearchPauseTime` + `NemesisLookAround`; el HUD F9 muestra el peldaño ganador. | Las contra-jugadas nuevas tienen que **verse** (regla R3). |
| 9 | Anti-cheese con comportamiento | ❌ | Nada cuenta hábitos. | Todo [§4](#4-catálogo-de-cheeses-de-wired) y [§5](#5-hábitos-del-jugador-y-contra-jugadas). |
| 10 | Detectar el estancamiento | 🟡 | `NemesisStuckEscape` (cuerpo trabado: repath → warp). `NemesisPursuit` predice e intercepta. `NemesisChaseProgress` mide "persigo pero no acorto" y penaliza el rastro (Fase 4). | Ningún peldaño lee `IsChaseStagnant` todavía: "soltar y emboscar" va con la Fase 6. |
| 11 | El NavMesh expresa personalidad | 🟡 | Hub `Not Walkable`; las puertas que no puede abrir (selladas, o *Nemesis Can Open* apagado) cortan el NavMesh mientras dura, y las que abre no (desde el 27/09, §18.9); montacargas con links. Bajadas de un solo sentido (`NemesisDropLink`, área `NemesisDrop`, Fase 8 parte 2). | Área 3 `NemesisAvoid` sin uso. Sin rutas de flanqueo. Bajadas sólo en el *Drop Lab* de la testbed. |
| 12 | Herramientas de debug primero | ✅ | F9 HUD (con filas de escondite, ritmo y presión), F10 consola, `NemesisGizmos`, validadores, `SO_NemesisDataEditor`. | Falta el panel de hábitos (Fase 3) y una fila de creencia con radio, confianza y procedencia ([§17](#17-percepción-y-creencia)). Sin tests automáticos. |

---

## 2. Mapeo: lo que propone el análisis → lo que ya existe

### 2.1 Ya existe — se reusa, no se reescribe

| El análisis propone | En WIRED es | Diferencias que importan |
|---|---|---|
| `NoiseEmitter` + `HearingSensor` | `PlayerStateManager.AudioEmitingZone` (esfera en la capa `DetectableAudio`, radios agachado 1 / caminando 4 / corriendo 10 en `SO_PlayerMovement`, apagada en quieto; 1 / 2 / 6 son los defaults del código, no lo que corre) + `FieldOfListening` | El ruido **dura**, no es un evento: tiene que vivir más de 0.1 s o cae entre dos barridos. Alcance = radio × `NoiseRangeScale` (2.5), tope `ListenRange` (15): agachado 2.5 m, caminando 10 m, corriendo 15 m (tope). ×0.8 por pared, ×0.75 por piso, distancia medida sobre el NavMesh. |
| `StalkerBlackboard` | La creencia de `NemesisStateManager`: `TryGetBelief(out pos, out fromSight)`, `BeliefAge`; más `FieldOfView.Awareness` y `LastKnownVelocity` | Usa el sensor **más fresco** y dice si la creencia viene de la vista o del oído. La procedencia sirve (el barrido de habitación sólo se compromete con una creencia de vista), pero **quedarse con uno y descartar el otro** es lo que hace que los sentidos compitan en vez de sumar. Se reemplaza por `NemesisBelief` ([§17](#17-percepción-y-creencia)). |
| `VisionSensor`, 4 conos | `FieldOfView`: foco 80° (Normal/Focused), periferia 170° con acumulador (Peripheral), `minDistance` 1 m (Close), proximidad extrema 1.5 m (rompe `Hidden`) | Equivalente funcional. Además muestrea pies, centro y cabeza. |
| `PlayerVisibilityState` | `PlayerStateManager.IsCrouch` / `IsHidden` | `IsHidden` es un bool suelto. Hace falta saber **en qué** escondite (§3.1). |
| `StalkerAgent` (FSM) | `NemesisStateManager` + 6 estados + `NemesisDecision` (escalera en `SO_NemesisPriorities`) | Una sola voz decide. Los estados no transicionan. |
| Stalk en dona | `NemesisSearchingState` + `NemesisFreeRoam` (barrido de habitación, radio 8) + `PickSearchTarget` (ruleta por última posición, predicción, no barrido) | El radio no se achica por pasada: el cerco no "se cierra". **27/09:** fuera del barrido, `PickSearchTarget` sólo devuelve waypoints, y ese es parte del "va a un nodo cercano" del playtest. Se reemplaza por un barrido alrededor de la creencia ([§18.5](#185-modelo-propuesto), Fase 2B parte 2). |
| Intercept | `NemesisPursuit.PredictAhead` + desvíos por waypoints con línea de visión; `TryGetInterceptPoint` al entrar a Searching | Nada lo dispara por estancamiento. **27/09:** la de `Searching` elige el waypoint "adelante" al que llega antes, que suele ser uno pegado a él, y se recalcula con cada ruido. Sale del flujo normal ([§18](#18-búsqueda-dónde-y-cuánto-y-el-director), D24). |
| `StalkerDirector` (hint zones) | `NemesisDirector`: ancla de patrulla, pesos de ruta, ruido sintético, boost de sentidos, entrada Mr. X; más `NemesisPressureZone` | Sin medidor de tensión y sin estados de ritmo (hechos en la Fase 5). **27/09:** sólo mueve la patrulla; no toca la búsqueda ni sabe cómo terminó ([§18.3](#183-el-director-qué-hace-de-verdad)). |
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
- La creencia fusionada (`NemesisBelief`) y la sospecha compartida entre sentidos ([§17](#17-percepción-y-creencia)).

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
| `NemesisSearchingState` (`TickSpotCheck`) | ✅ Prioridad: escondite **conocido** → **sospechoso** → barrido de habitación (con prioridad de la habitación donde te vio entrar) → grafo. Va al `ApproachPoint`, se queda `SearchPauseTime` y, si en ese rato no pasó nada, lo marca revisado (`MarkChecked`) y sigue. **Al llegar lo abre** (`NemesisHidingAwareness.Open`, desde el 27/09): si estás adentro te saca aunque aguantes la respiración. Antes no lo abría y dependía de la proximidad, que aguantar ahora frena (D21). |
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

**Desde el 27/09:** los usos son un medidor continuo por escondite, y además de revisarlo primero y
romperlo, el Nemesis abre los escondites usados que caen en la zona que investiga (§17.6, Fase 2D).

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
- **Desde el 27/09 ([§18](#18-búsqueda-dónde-y-cuánto-y-el-director), 2B parte 3):**
  - La búsqueda deja de durar 15 s fijos: se enfría con el silencio. Esconderse y aguantar sigue
    sirviendo (D21), pero cada ruido la renueva.
  - La contra-jugada (1) arranca como la **persistencia** del Director (búsquedas más largas en
    `BuildUp` con la sensibilidad creciente).
  - El Director la hace **volver a pasar** por la zona después de una búsqueda vacía. La Fase 6 la
    vuelve emboscada.

### C2 — Siempre el mismo escondite
- **Señal:** usos por `SpotId` estando cazado.
- **Contra-jugada:** 2 usos → sospechoso (lo revisa primero); 4 → quemado (lo rompe).
- **Desde el 27/09:** medidor continuo por escondite, que además lo hace revisar escondites usados mientras investiga (§17.6, Fase 2D).

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

### C6 — Ruido de cebo (señuelos)
- **Ya existe, y el plan lo tenía como futuro.** Tres señuelos en `Prefabs/Decoys/`, que
  `FieldOfListening` escucha por un canal propio (`DecoyNoiseSource.Active`):

| Señuelo | Se oye | Dura | Usos |
|---|---|---|---|
| `Decoy_Radio` | 12 m | hasta que el Nemesis la rompe (`NemesisDecoyBreaker`) | 1 |
| `Decoy_Chains` | 8 m | 1.5 s por sacudida | infinitos |
| `Decoy_FireAlarm` | **en todo el nivel** | 30 s (`ringDuration`) | 1 |

- **Por qué hoy es un cheese.** El señuelo entra por el mismo lugar que tus pasos: `HearsPlayer` es
  "oye cualquier cosa", y `TryGetBelief` toma el ruido más fresco como tu posición. Mientras la
  alarma suena, la creencia sobre *vos* apunta a la alarma y su edad no pasa de 0.1 s durante 30 s:
  los peldaños que sostienen persecución y búsqueda por edad de creencia no vencen. Si te ve mientras
  suena, la creencia salta entre vos y la alarma según qué sentido barrió último. Y las cadenas, con
  usos infinitos, son un botón de "mandalo allá" repetible.
- **Contra-jugada (con el [§17](#17-percepción-y-creencia)):** un señuelo es una **pista**, no el
  jugador (D18). Se investiga (y la radio se rompe), pero no renueva la creencia sobre el jugador ni
  mantiene viva una persecución. Sigue valiendo lo de antes: `SightCommitTime` (6 s) impide escaparse
  de una habitación comprometida con un ruido de afuera. A futuro (Fase 6): contar los señuelos que
  usa el jugador y, al N-ésimo, que vaya pero busque en dona alrededor de la pista y no sólo en ella.
- **Cómo pesa cada señuelo en la elección:** §17.5. No son todos iguales: la radio lo compromete
  hasta romperla, la alarma compite desde cualquier punto del nivel y las cadenas se gastan.

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

**Construido (27/09, [Fase 3](#fase-3--contar-sin-reaccionar)) con tres diferencias.** Registra todo
`PlayerHabitTracker`, desde eventos: `OnSearchEnded`, `OnChaseStalled` (los dos nuevos),
`OnChaseEnded` y `HidingEvents`. `EscapedWhileHidden` y `SameSpotReused` se cuentan cuando la
estadía se vuelve escape, no al terminar la búsqueda ni al entrar (D27, D28). El Hub se lee con
`NemesisSafeZones`, sin trigger aparte.

### 5.4 Contra-jugadas desbloqueables

| `ECounterplay` | Se desbloquea con | Qué hace | Dónde vive |
|---|---|---|---|
| `CheckHidingSpots` | 3 × `EscapedWhileHidden` | Nivel C: `Searching` incluye los escondites dentro de su barrido. | `NemesisSearchingState` + `NemesisHidingAwareness` |
| `PrioritizeSuspiciousSpots` | 2 × `SameSpotReused` (por escondite) | Ese escondite se revisa primero. | ídem |
| `BurnHidingSpot` | 4 × `SameSpotReused` (por escondite) | Lo rompe (§3.6). | `HidingSpot` + `NemesisHidingAwareness` |
| `ExitAmbush` | 3 × `EscapedWhileHidden` | Al terminar la búsqueda, espera mirando la salida (C1). | `NemesisSearchingState` (fase final) |
| `ChaseFlank` | 1 × `ChaseStalled` | Penalización del rastro en `NemesisPursuit` desde el arranque (C4). | `NemesisPursuit` |
| `ZoneDefense` | 2 × `ChaseStalled` o 2 × `SafeZoneEscape` | Emboscada en salidas probables (C4, C5). | Puntos `NemesisAmbushPoint` autorados + `NemesisDirector` |

**Desde el 27/09:** `CheckHidingSpots` y `PrioritizeSuspiciousSpots` se reemplazan por el medidor continuo de escondites de la Fase 2D (§17.6): en vez de desbloquearse de golpe con N escapes, la revisión crece con el uso.

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

**El Director sigue sin tocar el FSM.** El ritmo sólo mueve ancla, pesos, ruido y sentidos, y
desde la 2B parte 3 también la **persistencia** de la búsqueda: números prestados sobre
`SO_NemesisData`, como los sentidos ([§18.5](#185-modelo-propuesto) C). Un `PeakFade` que no puede
sacar al Nemesis de `Chasing` es exactamente lo que tiene que pasar: el Relax arranca cuando el
encuentro termina solo.

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

**Construido (28/09, [Fase 7](#fase-7--escalada-por-puzzles)).** `NemesisEscalation` vive en la
escena `Data` e instala el nivel con `InstallBaseline`. Tiene tres niveles, que suben vista, oído y
variación de ruta. Los tiempos de búsqueda quedaron ×1 (D31): con el enfriamiento de la 2B, acortarlos
como pide el spec premiaría esconderse y esperar, y alargarlos ya es la palanca de persistencia del
Director.

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
│ FieldOfView ─(★ nivel B)────┼─▶ ★ NemesisBelief (§17) ───────▶ NemesisDecision                  │
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
| `NemesisBelief` | Fusionar la evidencia de los sentidos en posición + radio + confianza + procedencia; separar las pistas del jugador; llevar la sospecha compartida | Sentir (eso siguen siendo `FieldOfView` y `FieldOfListening`); decidir (eso es `NemesisDecision`) |
| `NemesisChoice` + `FocusArbiter` | Elegir el foco: hacerse las preguntas del §17.4 y registrar cuál decidió | Saber (eso es `NemesisBelief`); decidir el estado (eso es `NemesisDecision`) |

`NemesisHidingAwareness`, `NemesisChaseProgress`, `NemesisBelief` y `NemesisChoice` son hermanos del facade, como `NemesisPathOracle`:
se agregan solos y el estado los consulta a través de `NemesisStateManager`.

---

## 9. Fases de implementación

Orden recomendado. La Fase 4 no depende de los escondites y arregla un cheese que ya existe, así que
puede ir en paralelo con la 1.

### Fase 2B — Creencia fusionada y búsqueda *(antes de la 3)* — 🟡 partes 1 a 4 de 5 construidas
> **27/09, reordenada en cinco partes** ([§18.6](#186-orden-dentro-de-la-2b)). Después de jugar la 2C
> se sumaron dónde busca y cuánto busca ([§18](#18-búsqueda-dónde-y-cuánto-y-el-director)), y el cruce
> del resto del plan mostró dos dependencias. El barrido nuevo necesita la pregunta 4 del árbitro
> (`Sequence` sube ~10 veces por segundo) y el D22, así que van juntos en la parte 2. El enfriamiento
> no necesita el foco, porque `BeliefAge` ya no cuenta pistas: va en la parte 3, antes de la elección
> completa. Lo que antes era "parte 2" es ahora la 4 (con su árbitro mínimo adelantado a la 2), y la
> vieja "parte 3" es la 5.

- **Parte 1 ✅ Creencia (27/09, sin commitear), falta jugarla.** Compila en Unity (batchmode sobre el
  working tree, 27/09). Qué entró:
  - `NemesisBelief` con la creencia fusionada y las pistas separadas. `FieldOfListening` distingue
    jugador de pista.
  - `TryGetBelief` / `BeliefAge` leen la creencia (sólo el jugador). `HearsPlayer` es el jugador.
  - Entran `HearsLead` (18) y `HasFreshLead` (19), con los peldaños "oye un señuelo u otro ruido" y
    "sigue yendo hacia la pista" (asset y `BuildDefaultLadder()`: 21 peldaños).
  - `NemesisController` lee la creencia en vez de su copia.
  - F9 muestra procedencia, radio y pista. Los números están en `SO_NemesisData` › Creencia.

  Diferencias con lo planeado:
  - La "confianza" no es un campo: se deriva del radio.
  - Hay una sola pista a la vez (la más fuerte de cada barrido).
  - La evidencia del jugador que "no es el mismo lugar" **reemplaza** la creencia en vez de volverse
    pista (ver D20).
  - `Sequence` sube con cada evidencia plegada, unas 10 veces por segundo (§18.4).
- **Parte 2 ✅ Dónde busca (27/09, sin commitear), falta jugarla** ([§18.5](#185-modelo-propuesto)
  A). Compila en Unity y pasan los tests EditMode (36/36, `SearchSweepRulesTests` incluidos).
  **Cómo quedó:**
  - **`SearchSweepRules`** (nuevo, en `WIRED.Nemesis.Logic`) contesta las preguntas 1 y 4 y
    dimensiona el disco. `Sequence` sola no alcanza: evidencia adentro del disco corre el centro;
    afuera, lo re-centra.
  - **`NemesisSearchingState`, reescrito:**
    - Arranca el barrido sobre la creencia (o sobre sí mismo, sin creencia).
    - La sigue con `TrackEvidence`.
    - Arma la pausa al llegar.
    - Abre el disco de a `WidenStep` (2.5 m) cuando lo cubrió.
    - `IsStandingWhereLost` lee `NemesisBelief.TryGetLastSeen`, no `NemesisPursuit`.
  - **`NemesisFreeRoam`:**
    - Puntos del NavMesh primero, más dos waypoints como candidatos extra.
    - Filtro de piso de 1.5 m.
    - `Recenter` / `MoveAnchor` / `Widen` conservan lo barrido, y `MarkSwept` se llama al llegar.
    - `IsFullySwept` ≠ inalcanzable.
    - La habitación preferida suelta cuando ya no tiene puntos sin barrer.
  - **`NemesisBelief`:** `EvidenceRadius`, `TryGetLastSeen` y `ObservedVelocity`. `HeardNoise.FromHidingSpot`
    (D22) multiplica el radio por `beliefNoiseHidingSpotFactor`.
  - **`SO_NemesisData`:**
    - Salen los campos sin uso: la ruleta de waypoints, la intercepción, `searchLeadTime`,
      `roomCommitRange` y `sightCommitTime`.
    - Entran `beliefNoiseHidingSpotFactor`, `searchSweepMinRadius` y `searchSweepEvidenceMargin`.
  - **Telemetría, F9 y gizmos:** sale `SearchInterceptPoint`. F9 muestra "mirando alrededor / yendo"
    más el barrido (radio, centro, puntos, "cubierto", habitación), y el gizmo dibuja el disco, la
    línea al punto actual y el rastro.
  - **`docs/CLAUDE.md`** y **`docs/Nemesis-System.md`**, actualizados.

  Lo que se pidió:
  - Árbitro mínimo: las preguntas 1 (¿hay algo nuevo?) y 4 (¿es lo mismo que ya persigo?) del §17.4,
    como lógica pura.
  - `Searching` barre puntos del NavMesh alrededor de la creencia:
    - Radio: el de la última evidencia, congelado al elegir y con tope.
    - Candidatos: puntos sorteados en el piso de la creencia. Los waypoints dejan de ser los primeros.
    - Dentro del disco actualiza sin reiniciar; fuera, re-centra conservando lo barrido.
  - La creencia expone el radio de la evidencia, el punto visto y la velocidad observada, para que
    los estados dejen de leer sensores (§18.4).
  - `NemesisFreeRoam`:
    - `Recenter` sin vaciar lo barrido.
    - Marca "barrido" al llegar.
    - Distingue agotado de inalcanzable.
  - La pausa de mirar se arma al llegar (hoy se arma al salir: §18.1, punto 5).
  - D22: un ruido que sale de un escondite viene con radio ×2.
  - Salen el reapuntado por `HasAudioTarget` (`ShouldNoiseRetarget`, el fallback al oído) y el ancla de
    vista forzada al entrar. `TryGetInterceptPoint` y `PickSearchTarget` salen del flujo normal (D24).
  - Fila `búsqueda` en F9 y gizmo del disco de barrido.
  - **Verificación:** casos 23, 27, 36, 41 y 42.
- **Parte 3 ✅ Cuánto busca, y el Director (27/09, sin commitear), falta jugarla** (§18.5 B y C).
  Compila en Unity (proyecto entero, Editor incluido) y pasan los tests EditMode (71/71).
  **Cómo quedó:**
  - **Enfriamiento.**
    - `SearchCooling` (nuevo, en `WIRED.Nemesis.Logic`, con `SearchCoolingTests`) define las reglas:
      mínimo, tope, "revisó todo" y el silencio contra la ventana × la calidad.
    - `NemesisSearchingState.IsWarm` las aplica con su propio reloj de renovación (`TrackRenewal`),
      que corre también durante la revisión de un escondite y no se renueva con evidencia desde el Hub.
    - La calidad sale de `NemesisBelief.IsAnchoredBySight` / `LastEvidenceMuffled` (nuevo).
    - Los números van al final de `SO_NemesisData`: `searchMinTime` 6, `searchQuietWindow` 8,
      `searchHardCap` 30, `searchQualitySight` 1.25, `searchQualityMuffled` 0.75 y
      `searchEscalatedCapScale` 0.5.
  - **Escalera** (asset y `BuildDefaultLadder()`, 22 peldaños):
    - El peldaño del presupuesto lee `IsSearchWarm` (predicado 20).
    - Peldaño nuevo del D26, "investigó un ruido tuyo y sigue tibio": `Investigating` →
      `Searching` con `BeliefAgeUnder(SearchQuietWindow)`, un umbral nuevo al final de
      `ENemesisThreshold`.
    - `Searching` sabe que es la búsqueda corta por `StateManager.PreviousStateKey` (nuevo, en la
      base).
  - **Director** (`NemesisDirector`, `NemesisTension`, `SO_DirectorPacing`):
    - Un solo préstamo (`RefreshLoan`) combina los sentidos con la persistencia: `BuildUp` ×1 (hasta
      ×1.5 con la sensibilidad creciente), `SustainPeak` ×1, `PeakFade` ×0.75 y `Relax` ×0.5.
    - "Vuelve a pasar": toma el último `OnSearchEnded` vacío, espera 20–40 s y aplica sólo en
      `Patrolling`; si todavía está cazando, lo reprograma para 10 s después.
    - `QuietTime` por encuentros: proximidad > 0.75 y creencia fresca < 10 s mientras busca o
      investiga.
    - `IsEncounterOver` lee la creencia.
    - Al suspenderse (dormido, cinemática, escape) suelta toda presión, scripted incluida (`StandDown`).
    - F9 `presión` muestra el acecho de la patrulla, la persistencia y la vuelta pendiente.
  - **Pesos de ruta con cúmulos:** `NemesisRouteGraph.ClusterWeight` lee el peso vivo de las rutas de
    cada cúmulo, y se borró `Cluster.Weight`. De paso: `RebuildGraph` reinicia la patrulla por
    cúmulos cuando el grafo de verdad se rearmó (`BuildVersion`), y se borró `TryGetSensedTrail`,
    que ya no tenía uso.
  - **Música:** `searchTailTimeout` 50 s por defecto. Zona1 lo pisa con 25 en la escena; sin efecto
    mientras el Nemesis no busque ahí (D25).
  - **Arreglos de la revisión de la parte 2:**
    - Evidencia dentro del disco lo sigue con `Follow` (el radio crece, la habitación se relee y
      re-elige si el punto quedó afuera).
    - El disco se abre en la misma elección que lo descubre cubierto.
    - La fusión no achica un ruido de escondite por debajo de su propio radio.
    - Hay una sola banda de piso.
    - No elige punto de entrada si hay un escondite que revisar.
    - El fallback sólo acepta puntos alcanzables.
    - `IsStandingWhereLost` mira la altura.

  Lo que se pidió:
  - Predicado `IsSearchWarm` (20, al final del enum) en el peldaño `"le queda presupuesto de
    búsqueda"`, en el asset **y** en `BuildDefaultLadder()`. Implementa el enfriamiento: mínimo,
    ventana de silencio × calidad, tope, "revisé todo".
  - La evidencia desde el Hub no renueva (C5).
  - `Investigating` escala a una búsqueda corta después de un ruido tuyo (D26).
  - Números nuevos al final de `SO_NemesisData`. `SearchTimeOut` queda para la memoria del escondite
    conocido. `searchTailTimeout` de la música sube a 50 s.
  - Director:
    - Persistencia como préstamo de números, en una sola copia con el boost de sentidos.
    - "Vuelve a pasar" con `OnSearchEnded` (el evento lo crea la Fase 3).
    - `QuietTime` medido por encuentros y no por proximidad.
    - `IsEncounterOver` lee la creencia.
    - Una presión de puzzle limpia sus sentidos al suspenderse.
    - La palanca de pesos de ruta vuelve a funcionar con cúmulos: el peso de cada cúmulo se congela al
      armar el grafo, y la huella no incluye los pesos (§18.3).
    - F9 muestra el acecho de la patrulla y la persistencia.
  - **Verificación:** casos 2, 9, 25, 34 y 43–48.
- **Parte 4 ✅ La elección completa (28/09, sin commitear), falta jugarla.** Compila entero (Roslyn
  contra los `.rsp` de Unity, Editor incluido) y pasan los tests EditMode fuera de Unity (100/100
  después de la revisión, `FocusArbiterTests` incluidos). **Cómo quedó:**
  - **`FocusArbiter`** (nuevo, puro, en `WIRED.Nemesis.Logic`): las preguntas del §17.4 en orden, con
    la que decidió. Valor = base × confianza (radio de la evidencia) × frescura (media vida 6 s) ×
    costo de llegar (camino) × habituación. Ventaja de compromiso 0.3 que decae en 3 s (sólo contra algo
    de su rango o menor: un paso tuyo no espera a que se le pase lo de las cadenas), "casi llego"
    +0.15 a menos de 4 m (nunca para la creencia: una búsqueda la rodea todo el tiempo), margen 0.05,
    anti-titubeo 2 s, piso de atención 0.12.
  - **`NemesisChoice`** (nuevo, hermano del facade, se agrega solo): el **foco** (vos, una pista o un
    vistazo). Pregunta sólo con evidencia nueva (`Sequence`, `LeadSequence`, un vistazo fresco con la
    sospecha pasada el umbral), más una pista que sigue sonando, cada 1 s. Al soltar una pista o un
    vistazo vuelve a `vos` si hay creencia.
    Habituación por señuelo ×0.6 por visita vacía, por sesión; un señuelo que ya revisó mientras sigue
    sonando se descarta hasta que se calle. Un escondite sospechado o conocido le gana a cualquier
    pista (suelta el foco en la pista). "Suma" (§17.5) sólo mientras está buscando esa zona.
  - **Escalera** (asset y `BuildDefaultLadder()`, 22 peldaños): `FocusIsLead` (predicado 22).
    "oye un señuelo u otro ruido" (`HearsLead`) pasó a ser **"su atención está en una pista"**
    (`FocusIsLead`) y subió **arriba del presupuesto de búsqueda** (D35).
  - **`Investigating` sigue el foco:** al cambiar de idea se frena ~0.4 s girando hacia lo nuevo y
    recién ahí camina (§17.3, "se ve el cambio"); una pista la sigue sin intervalo; al terminar de mirar
    una pista o un vistazo avisa (`MarkFocusChecked`). Nada lo reapunta por `HasAudioTarget`.
  - **`NemesisDecoyBreaker`** lee el foco (`FocusDecoy`) y **se instala solo**: no estaba en ningún
    lado y la radio no se rompía nunca. `DecoyNoiseSource` tiene tipo (radio, alarma, cadenas, otro,
    leído del componente de al lado) e id.
  - **Sospecha compartida:** un ruido suave tuyo (agachado, `softNoiseLoudness` 1.5) sube el mismo
    medidor que un vistazo. Un ruido solo nunca llega a avistamiento: lo sube hasta 0.9 y no lo baja
    (D36).
  - **Señuelos en la testbed:** *Tools/Nemesis/Build Decoy Stations (NemesisTestbed)* pone la radio
    (SALA_LATERAL), la alarma (PASILLO_CARGA) y las cadenas (PASILLO_OESTE) con carteles. Ya
    corrido (están en la escena); el builder se borró el 29/09 y está en git.
  - **F9:** fila `foco` (qué, cuánto vale, hace cuánto, y la última decisión con su pregunta).
  - Los números van al final de `SO_NemesisData` (*Elección* y *Sospecha compartida*).

  **Diferencias con lo planeado:**
  - La "lista corta de pistas" es implícita: la mejor pista de cada barrido compite contra el foco
    cada vez que es nueva, y otra vez cada 1 s mientras siga sonando sin ser el foco. No hace falta
    guardar más de una.
  - "Mismo lugar" (pregunta 4): dos señuelos distintos nunca son lo mismo, por cerca que estén (cada
    uno se rompe, se habitúa y se descarta por su cuenta). Un ruido anónimo a menos de 3 m de un
    señuelo, sí: el foco pasa a ser ese señuelo.
  - `Searching` no cambia: un foco en una pista lo saca por la escalera (el peldaño nuevo), y con el
    foco en vos barre la creencia como antes.
  - La habituación vive en `NemesisChoice`, no en `PlayerHabitTracker` (§17.5 lo permitía mientras
    tanto); no sobrevive a recargar la escena.

  **Arreglos de una revisión de código (28/09, el mismo día):**
  - **Una pista que perdía una vez quedaba ignorada mientras sonara.** Sólo se preguntaba con
    `LeadSequence` nuevo, y un señuelo que no para no lo mueve: la alarma perdía contra una creencia
    fresca y ya no volvía, aunque la creencia se enfriara (§17.5 pide lo contrario). Ahora una pista
    que sigue sonando y no es el foco se vuelve a preguntar cada 1 s.
  - **Soltar una pista o un vistazo dejaba el foco en "nada"** con la creencia viva: cualquier pulso
    del Director le ganaba a "0 + margen" y sacaba a una búsqueda tibia. Ahora vuelve a `vos`, sin
    ventaja de compromiso.
  - **Un vistazo por debajo del umbral cambiaba el foco**, y ningún peldaño actúa sobre un vistazo.
    Ahora se ofrece sólo con la sospecha pasada el umbral.
  - **Dos señuelos a menos de 3 m eran "lo mismo"**, y el foco se quedaba con el id del primero: la
    radio al lado de un pulso del Director no se rompía nunca. Ver arriba.
  - **Una pista que seguía sonando envejecía como si se hubiera callado** (su valor se partía a la
    mitad cada 6 s en plena caminata). Ahora su edad es la de la última vez que se oyó.
  - **El peldaño "para llegar hay que tomar el montacargas" iba hacia la creencia que el árbitro
    acababa de descartar**, y después "su atención está en una pista" lo bajaba de vuelta. Ahora
    pide `Not FocusIsLead` (D35).
  - **`Investigating` yendo a un escondite sospechado:** los cambios de foco del camino se aplicaban
    tarde, al terminar, con la pausa de giro; y el escondite contaba como "vistazo", así que al
    terminar soltaba un vistazo que no tenía nada que ver. El escondite tiene ahora su propio origen.
  - Menores: una captura suelta también una pista o un vistazo en foco; la distancia a una pista se
    recuerda 1 s (dos señuelos alternándose pedían un camino cada 0.1 s); el señuelo se pasa a mano
    al foco (buscarlo por id perdía uno que se calló en el mismo barrido); el foco en una pista mide
    por camino, como las pistas con las que compite.

  Lo que se pidió:
  - `NemesisChoice` (hermano del facade) + `FocusArbiter` (clase pura, en un asmdef que referencien los
    tests): todas las preguntas del §17.4, con señuelos (§17.5) y escondites (§17.6).
  - Re-elección con foco único (§17.3): `Searching` e `Investigating` siguen el foco. Ninguno reacciona
    a `HasAudioTarget`.
  - Una lista corta de pistas (hoy hay una sola), con valor base y habituación por señuelo.
    `DecoyNoiseSource` necesita un tipo.
  - `NemesisDecoyBreaker` lee el foco en vez de `LastHeardDecoy` **y se instala**: hoy no está en el
    prefab ni se agrega solo, así que la radio nunca se rompe.
  - Poner los señuelos en la testbed: hoy no hay ninguno en ninguna escena.
  - Sospecha compartida: vistazo y ruido suave suben el mismo medidor. `Investigating` va a la posición
    de la creencia o del vistazo, no a la del oído.
  - F9 muestra el foco y la pregunta que decidió.
  - **Pistas y `BeliefAge` van en el mismo paso** (ya se cumplió en la parte 1: `HasFreshLead` sostiene
    la caminata hacia una pista).
  - **Verificación:** casos 22, 24–26, 28–33 y 37, y el 60 (la fila `foco`).
- **Parte 5 — Limpieza y tests** (lo que antes era "parte 3"):
  - Se sacan los parches que esto vuelve innecesarios, de a uno y con su caso de prueba: el "punto
    visto y no la creencia" de `NemesisPursuit` (§16.4, **después** del de `Searching`, porque
    `IsStandingWhereLost` depende de él) y el filtro de intervalo y distancia de `Investigating`.
  - Gizmo del radio de la creencia.
  - Tests EditMode de la fusión y de la escalera: son lógica pura, sin escena (§16.2). Los del árbitro
    (`FocusArbiterTests`, parte 4) y del enfriamiento (`SearchCoolingTests`, parte 3) ya están.
    `WIRED.Tests.EditMode.asmdef` no puede ver Assembly-CSharp. La lógica va en el asmdef de
    lógica pura que arma la Fase 3 (`WIRED.Nemesis.Logic`, en `Scripts/Nemesis/Logic/`), con datos
    planos: sin `HidingSpot` ni `DecoyNoiseSource`.
  - **Verificación:** todos los casos de la 2B.

### Fase 2D — Memoria de escondites *(después de la 2B)*
- ✅ **Los datos, construidos con la Fase 3 (27/09, sin commitear).** El medidor de uso por escondite
  (D23, [§17.6](#176-los-escondites-en-la-elección)) vive en `PlayerHabitTracker`:
  - +1 al **salir** (desde el 05/10, WIR-057; antes era al entrar, y el escondite ocupado ya contaba
    como usado), y +1 más cuando esa estadía se vuelve escape (R1, D28; ver la
    [Fase 3](#fase-3--contar-sin-reaccionar)). "Cazó cerca" es investigar, perseguir o buscar a
    ≤ 8 m por NavMesh, medido cada 0.5 s.
  - −0.1/min desde el primer minuto; sobrevive a la captura (R5).
  - La clave es el `SpotId`. Un escondite sin id se recuerda por el nombre del GameObject, con un
    warning.
  - **A calibrar:** con +1 por uso y +1 por escape, una sola escapada con el Nemesis cerca ya deja
    el escondite en 2 (revisarlo primero), y dos lo dejan en 4 (romperlo). Es el doble de rápido que
    los "2 / 4 usos estando cazado" del C2 de antes, aunque sea literalmente el D23 con los umbrales
    del §12. Se decide con los datos de la Fase 3.
- ✅ **API para la pregunta 7 del §17.4:**
  - `GetSpotUsage`.
  - `OpenChance`: medidor × 0.25, tope 0.85.
  - `IsPrioritySpot` (≥ 2) e `IsBurnable` (≥ 4).
  - `CollectUsedSpots(centro, radio, lista)`: por NavMesh, el más usado primero, nunca un escondite
    fuera del radio (caso 40).
  - Para R3: `HasRun` y `MarkRun`, que marcan la primera vez de cada contra-jugada.
- ✅ Fila `escondites` en F9: los más usados con su medidor; `adentro, cazado, N búsq.` mientras el
  jugador está escondido, y `escape pendiente` durante los 5 s de confirmación.
- Reemplaza `CheckHidingSpots` y `PrioritizeSuspiciousSpots` de la Fase 6: en vez de desbloquearse
  de golpe con N escapes, la revisión crece con el uso. `BurnHidingSpot` queda, al tope del medidor.
  No tienen fila en `SO_CounterplayRules`: se contestan por escondite.
- ✅ **La mitad del Nemesis, con la 2B parte 4 (28/09, sin commitear), falta jugarla.** Compila
  (Roslyn). En `NemesisHidingAwareness.ConsiderUsedSpots`:
  - `Investigating`, al llegar a un punto que no es un escondite, mira los escondites usados a 6 m;
    `Searching`, los del disco de barrido, al entrar, al re-centrar y al ensanchar.
  - Van del más usado al menos usado y cada uno se sortea una vez por búsqueda o por investigación
    contra su `OpenChance`. El primero que sale pasa a **sospechado** ("lo usaste antes (N)" en F9),
    y el estado va a abrirlo como cualquier sospecha. Nunca fuera de la zona (caso 40).
  - **R3:** mientras `CheckHidingSpots` no corrió nunca (`HasRun`), sólo cuenta un escondite a
    menos de 12 m del jugador y en su mismo piso, que lo puede ver u oír abrir; uno que no cumple no
    gasta su sorteo. `MarkRun` recién **al abrirlo**: si algo lo corta antes, la lección no se dio.
  - La otra mitad del D22: un segundo ruido desde el mismo lugar (a menos de 1.5 m, dentro de 60 s,
    y después de más de 1 s de silencio) vuelve sospechoso al escondite más cercano, a 2 m como
    mucho ("volvió a sonar ahí"). Cuenta cualquier ruido de adentro, respirar incluido: con él a
    menos de ~2.3 m, dos respiraciones (3 s) lo mandan a abrir. Es la franja donde aguantar decide
    (D21). La memoria del primero se borra al revisarlo vacío, con una captura, un respawn, o al
    verte afuera.

  **Diferencias con lo planeado:** los escondites usados no compiten en el árbitro del foco. Son la
  pregunta 7, que ya contesta el estado: un escondite sospechado le gana a cualquier pista (la
  `NemesisChoice` suelta una pista en cuanto hay uno).
- **Verificación:** casos 35, 39 y 40; el medidor, casos 49–51.

### Fase 6 — Contra-jugadas desbloqueables
- `CheckHidingSpots`, `PrioritizeSuspiciousSpots`, `ExitAmbush`, `BurnHidingSpot` (necesita el
  modelo del locker roto), `ChaseFlank`, `ZoneDefense` + `NemesisAmbushPoint`.
- La regla R3 (la primera vez se ve) implementada, no sólo pedida.
- **Verificación:** casos 6 y 8.

### Fase 8 — Bajadas entre pisos *(independiente)*
- Detalle completo en el [§15](#15-bajadas-entre-pisos). Tiene tres partes, y cada una se puede
  mergear sola:
  1. **Sin código:** apagar *Generate Links*, rebakear y medir qué links automáticos se pierden
     (§15.2). Pendiente: necesita el editor.
  2. ✅ **Código** (27/09, sin commitear): `NemesisDropLink`, la rama de bajada en
     `NemesisElevatorUser`, `CrossedDrop` en `NemesisNav.NavRoute`, validador y gizmos (§15.4).
     Funciona sin animaciones.
  3. **Arte:** las animaciones del §15.5 y el setup del Animator. Pendiente.
- **Verificación:** casos 12–16 y 55 del [§13](#13-casos-de-prueba). Antes hay que poner una
  bajada `Hop` y otra `Hang` en la testbed, con su escalera de vuelta (§15.6, paso 5): hoy no hay
  ninguna en ninguna escena.
- **Cómo quedó la parte 2:**
  - `NemesisDropLink` (nuevo, espejo de `NemesisElevatorLink`): configura su `NavMeshLink` en
    `Awake`.
    - Un solo sentido, área `NemesisDrop` y sin override de costo: un costo positivo en el link
      pisaría el de área, que es lo que cambia por estado.
    - Lista estática `Active` para `NemesisNav`.
    - Arco en gizmo (verde `Hop`, ámbar `Hang`, gris en enfriamiento), bajo el interruptor maestro
      de `NemesisGizmos` y en la familia de debug del `GizmoManager`.
    - Al agregarlo en el editor crea los hijos `TopEdge` y `BottomLanding`.
  - La geometría es lógica pura, en `WIRED.Nemesis.Logic` (`DropPath.cs`): el arco balístico, la
    elección `Hop`/`Hang` por alto y los tramos de la `Hang`. La usan la bajada, el gizmo y el
    validador, así que los tres dibujan el mismo camino. 11 tests EditMode (`DropPathTests`).
  - `TraverseDropAsync`, en `NemesisElevatorUser`, con las fases del §15.3:
    - Alinear, asomarse con el gruñido (el aviso) y despegar (`Hop`), o darse vuelta con el golpe
      de manos (`Hang`).
    - Colgarse, caer, aterrizar con impacto y recuperarse 0.75 s sin poder agarrar.
    - El agente queda sobre el link todo el tiempo: el aterrizaje cierra el link con
      `CompleteOffMeshLink`, como uno simple.
    - Cortada en el aire (un respawn), la bajada pone al Nemesis en el aterrizaje con `WarpTo`.
    - Después se suspende el link por `dropLinkCooldown` (8 s).
  - La `Hang` no es un arco de 4 m: camina al borde de espaldas al hueco, se cuelga
    `hangDepth` (1.9 m) y cae el resto. La `Hop` sube `hopApexHeight` (0.3 m) para no rozar el
    canto.
  - `NemesisStateManager`:
    - `CanReachPlayerNow` da `false` durante toda la bajada y la recuperación.
    - El costo del área se fija por estado (D11: 2 cazando, 20 en patrulla), desde `SetGait`.
    - `PlayTraversal(EDropPhase)` con `HasState` y fallback.
  - `NavRoute.CrossedDrop` exige las dos puntas **seguidas y en orden** (arriba y después abajo):
    las puntas de una bajada están a 1–2 m en planta, y un camino que pasa cerca de las dos por la
    escalera no la usó.
  - Números: al final de `SO_NemesisMovement` (costos, tiempos de cada fase, arco, nombres de los
    estados del Animator) y `dropLinkCooldown` al final de `SO_NemesisData`.
  - F9: fila `bajada`. Validador: toda la lista del §15.6.
  - Área 5 nombrada `NemesisDrop` en `ProjectSettings/NavMeshAreas.asset` (costo 1).
  - Clips provisorios en `NemesisAudio` del prefab: `sfx_nemesis_voice_chase` como gruñido y
    `sfx_nemesis_pasos_chase_05` con pitch 0.85 como impacto. Sin golpe de manos.

  Diferencias con lo planeado:
  - **D29, nueva:** terminar la bajada libera el compromiso de `Traversing`. Sin eso el caso 12
    no se cumplía.
  - **D30, nueva:** se echa atrás si ve al jugador en su propio piso.
  - Mira hacia donde está el aterrizaje, no hacia el eje Z de `TopEdge`: así no hay una rotación que
    alguien pueda poner mal. El eje Z sólo cuenta si el aterrizaje está justo abajo.
  - El costo es por estado y el enfriamiento es global. Por bajada sólo se configura el ancho
    (el §14.4 decía "costo; enfriamiento").
  - Los sonidos los dispara el código al empezar cada fase, no eventos de animación: las fases las
    cronometra el código, y con el placeholder no hay clips que lleven eventos.
  - Sin `Land Roll` (A7, opcional).
  - *Generate Links* prendido es una nota del validador, no un problema: el Nemesis ya no usa
    esos links desde WIR-028 (`NemesisLifecycle` saca `Jump` de su máscara).
- Compila y pasan los tests EditMode en Unity (batchmode, 27/09: 59/59). Falta jugarla.

**Por qué en este orden:** la 1 es prerrequisito. La 2 cierra el agujero de inmunidad, sin el cual
esconderse rompe el juego. La 3 va antes que la 6 para que los umbrales salgan de datos. La 5 va
antes que la 6 porque contra-jugadas sin Relax frustran (y cada captura de más le cuesta 30 s de
módulo al jugador). La 4 es independiente y arregla algo que hoy ya se puede explotar. La 8 también
es independiente: puede ir apenas termine la Fase 0, y conviene que llegue antes de la 6, porque
`ZoneDefense` y la emboscada de C7 la pueden aprovechar. La 2B va antes de la 3 y de
la 6: la 3 cuenta escapes con el barrido anclado en la creencia, y la 6 aplica las contra-jugadas
"donde lo sintió" (R4). Construidas sobre la creencia de hoy, heredan el conflicto. La 2C va antes que
todo: es chica, no depende de la creencia y arregla lo que más se sintió en el playtest del 27/09. La 2D va después
de la 2B, porque el medidor entra como una pregunta más de la elección.

### ✅ Hecho (fases terminadas)

Las fases construidas, en orden numérico. Lo que les falta jugar está en [Pendiente](#pendiente).

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
  (valor del prefab, a propósito: las puertas del montacargas se abren con `sp1`). **Desde el 22/09
  a la tarde en Zona1 no se despierta en el gameplay:** sólo en la cinemática final (§14.1).
- **Pendiente de jugar:** F9 tiene que mostrar esperas distintas en cada waypoint; F10 tiene que
  listar las seis zonas y un botón de presión tiene que inclinar la patrulla hacia esa zona en uno
  o dos ciclos de ruta (12 s). Nada de esto se puede verificar sin entrar a Play.

### Fase 1 — Escondites, lado jugador *(prerrequisito)* — ✅ construida (commit `9eba9b46`)
- Área de prueba: `TestIñaki.unity` → *Hiding Test Area* (la armó `HidingTestAreaBuilder`, borrado el 2026-09-25; está en `3d262aef`),
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
  - ✅ **Aviso audible al saber el escondite** (28/09, D34): suena cuando el escondite pasa a
    conocido y el Nemesis todavía está a más de 2 m de la puerta. Clip provisorio. Caso 58.
  - Alternativa a probar (D17): al pasar el umbral por las rendijas, clavar la mirada en el
    escondite (`NemesisLookAround`) en vez de ir directo a la puerta.
  - En escenas que no hornean `Default` (la testbed hornea `Ground|Wall|Props`), el collider sólido
    del container no hace hueco en el NavMesh: pasarlo a `Props` o sumarle un
    `NavMeshModifierVolume` en una capa horneada antes de poner containers ahí.

### Fase 2C — Aguantar la respiración tiene que servir *(independiente, primero)* — ✅ construida (27/09, sin commitear), falta jugarla
- Del playtest del 27/09: "casi siempre me vio y me agarró aunque estuviera aguantando la
  respiración". Diagnóstico y regla en el [§17.6](#176-los-escondites-en-la-elección).
- `FieldOfView` pasa a leer `IsHoldingBreath`: aguantando, no hay proximidad a través de la carcasa
  ni acumulación por las rendijas en un escondite que no sospecha (D21).
- La revisión de un escondite sospechoso o conocido **lo abre**: si el jugador está adentro, lo saca,
  aguante o no. Ya no depende de la proximidad.
- Llegar a donde oyó algo y no ver nada: mira a la izquierda y a la derecha y se va
  (`investigationDwellTime` + `NemesisLookAround`, que ya existen).
- `maxHoldSeconds` 6 → 8 y `exhaleNoiseRadius` 2.5 → 2.0 en `SO_HidingData` (§12).
- No depende del §17: se puede hacer ya, y es lo que más se sintió en el playtest.
- **Cómo quedó:** `FieldOfView.CheckExtremeProximity` y `SenseThroughSpot` leen `IsHoldingBreath`:
  aguantando dentro de un escondite no hay proximidad ni rendijas. La vista no necesita distinguir
  si el escondite es sospechoso, porque uno sospechoso se abre igual. `NemesisHidingAwareness.Open`
  abre el escondite al llegar, desde `Searching.TickSpotCheck` y desde `Investigating`.
  `SO_HidingData`: `maxHoldSeconds` 8 (asset y código; el default del código era 0 = sin tope, que
  con esta regla sería inmunidad) y `exhaleNoiseRadius` 2.0. Compila con el Roslyn de Unity sobre
  el `.rsp` del proyecto, con el editor abierto.
- **Verificación:** casos 34–36 y 38.

### Fase 3 — Contar sin reaccionar
- ✅ **Construida el 27/09 (sin commitear); falta jugarla.**
- ✅ `PlayerHabitTracker`, en la escena `Data` (`Singleton` + `ISessionResettable`).
  - Sobrevive a la captura y al checkpoint: queda afuera del rollback, que sólo restaura
    `PuzzleStateManager`.
  - Se borra con New Game.
- ✅ `SO_CounterplayRules` (`ScriptableObjects/Nemesis/`, asignado en el tracker), `EExploitKind` y
  `ECounterplay`. Los enums sólo se agregan al final.
- ✅ **Registro, todo del lado del Director (R4), sin tocar el FSM ni la escalera:**
  - `EscapedWhileHidden` sale de `NemesisEvents.OnSearchEnded(area, found)`, un evento nuevo. Lo
    levanta `NemesisTelemetry` cuando el Nemesis sale de `Searching` hacia algo que no sea
    `Traversing`: una búsqueda que sigue en otro piso queda abierta. El área es la creencia, y found
    quiere decir que salió a `Chasing` o `Catch`.
    - Cuenta si la búsqueda no lo encontró y el escondite está a ≤ `nearbyRadius` (8 m) por NavMesh
      del área.
    - Cuenta **recién cuando la estadía se vuelve escape** (ver abajo), una por **cacería**
      aguantada. Una cacería dura hasta que el Nemesis vuelve a `Patrolling`: buscar, investigar un
      suspiro y volver a buscar es una sola.
    - Con el *Hide* de F10 (sin escondite) cuenta en el acto, así se puede probar en la testbed.
    - Un Nemesis apagado en medio de una búsqueda (la cinemática del escape) la deja abierta: se
      reporta cuando vuelve a salir de `Searching`.
  - `SameSpotReused`: un escape de un escondite del que ya se había escapado antes.
  - `ChaseStalled` sale de `NemesisEvents.OnChaseStalled`, nuevo, levantado desde
    `NemesisChaseProgress.Stall`: uno por ventana. Nunca durante el escape.
  - `SafeZoneEscape`: `OnChaseEnded` con el jugador dentro del Hub (`NemesisSafeZones`), salvo que la
    persecución haya terminado en una captura (el respawn puede caer en el Hub). La marca de captura
    se consume en `ChaseEnded` y no se borra en `ChaseStarted`: un agarre desde la patrulla levanta
    la captura y el inicio de la persecución en el mismo frame.
  - Las diferencias con el §5.3 están en D27 y D28.
- ✅ **Cuándo una estadía es un escape** (`HidingStayBook`, lógica pura con tests; D28). Hacen falta
  tres cosas:
  - El Nemesis cazó cerca: investigó, persiguió o buscó a ≤ 8 m por NavMesh (medido cada 0.5 s), o
    terminó una búsqueda cerca.
  - El jugador salió por su cuenta: no una captura, no una cinemática, no el nivel descargándose.
  - Después pasaron 5 s sin captura y sin persecución andando.

  Salir mientras te saca, o que te vea salir y te agarre, es que te agarraron. La captura cancela el
  escape pendiente, y volver a esconderse lo confirma. Lo encontró la revisión de código del 27/09.
- ✅ **Filas del §5.4 que siguen contando.** Las de escondites pasaron al medidor de la 2D.
  - `EscapedWhileHidden` × 3 → `ExitAmbush`.
  - `ChaseStalled` × 1 → `ChaseFlank`.
  - `ChaseStalled` × 2 o `SafeZoneEscape` × 2 → `ZoneDefense`.
  - Cada una arranca con 0.35 al desbloquear, suma +0.1 por uso extra y tiene tope 0.85.
- ✅ **Decaimiento (R7, D4):** un contador aguanta 5 min sin bajar desde el último registro y después
  baja 0.1/min. Sin esa espera, un contador que llegaba al umbral volvía a quedar abajo al segundo.
- ✅ **Debug:**
  - F9: filas `hábitos` (los cuatro contadores), `desbloquea` (qué se desbloquearía, con qué chance, y
    hace cuánto fue el último registro) y `escondites`.
  - F10: *Log ledger* y *Clear habits*.
  - Una línea de consola por registro y por escondida (`logRegistrations`).
  - *Validate Navigation Setup* revisa las filas: umbral 0, chances fuera de 0..1, o romper antes de
    revisar primero.
- ✅ **Primeros tests EditMode del proyecto:** `HabitLedgerTests` (22) y `HidingStayBookTests` (12).
  - Viven en `Scripts/Nemesis/Logic/`, con el asmdef `WIRED.Nemesis.Logic`: lógica pura, sin
    Assembly-CSharp. Ahí van también el `FocusArbiter` y las reglas de búsqueda de la 2B.
  - Pasan en el Test Runner de Unity (batchmode), y todo el árbol compila.
  - La escena `Data` y el asset se verificaron cargándolos en Unity.
- **Sin contra-jugadas todavía:** nada lee `IsUnlocked`.
- **Falta jugarla** con F9 abierto y anotar cuántos de cada uno salen por partida, para ajustar los
  umbrales. Hoy Zona1 no tiene escondites y el Nemesis sólo se despierta en el escape: se juega en
  `TestIñaki` (escondites) y en la testbed (persecución, *Hide* de F10).
- **Verificación:** casos 10 y 49–54.

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

### Fase 7 — Escalada por puzzles
- ✅ **Construida el 28/09 (sin commitear); falta jugarla.** Es el spec §7.2 con el mecanismo del
  [§7](#7-escalada-por-progreso).
- ✅ **`NemesisEscalation`** (`Scripts/Nemesis/`), en la escena `Data`, al lado de
  `PlayerHabitTracker` (`Singleton` + `ISessionResettable`).
  - Toma el nivel de `PuzzleStateManager.CompletedPuzzleCount` (nuevo). Lee la cuenta, no suma
    eventos: un checkpoint que deshace un puzzle lo baja.
  - Lo vuelve a mirar al cargar el nivel y en cada puzzle completo, cada respawn, cada despertar y
    cada cambio de estado del Nemesis. Si el Nemesis ya tiene el nivel, no hace nada. La carga del
    nivel es la que importa para la ruta: llega antes del `Start` del Nemesis, que entra a patrullar
    (y sortea su primera ronda) antes de avisar que despertó.
  - New Game y Retry tiran la copia. En un Retry el nivel viejo sigue cargado: si su Nemesis sigue
    vivo, primero le devuelve el asset autorado.
  - Construye una copia del asset **como está autorado** (`AuthoredData`, nunca una copia sobre otra)
    y la instala con `InstallBaseline`: la escalada es permanente y pasa por el `BaselineData` (§7).
  - Sin préstamo del Director, instala la copia en el acto. Con préstamo, `OnBaselineChanged` le hace
    rearmar el préstamo encima del nivel nuevo, en vez de esperar a que termine.
  - Nunca escribe el asset: los cambios en Play mode persistirían en el editor.
- ✅ **`SO_NemesisEscalation`** (`ScriptableObjects/Nemesis/`). Tres niveles (D33):

  | Desde | Vista | Oído | Búsqueda | Variación de ruta |
  |---|---|---|---|---|
  | 0 puzzles | ×1 | ×1 | ×1 | como está (0.15) |
  | 2 puzzles | ×1.1 | ×1 | ×1 | al menos 0.25 |
  | 3 o más | ×1.15 | ×1.1 | ×1 | al menos 0.40 |

  - La vista multiplica `ViewRange`, y con él lo que se mide en fracción de él: las rendijas del
    locker y bajo la mesa.
  - El oído multiplica `NoiseRangeScale` y `ListenRange` juntos: cada ruido se oye más lejos (D32).
  - La variación de ruta es un piso para las chances de invertir la ronda y de saltear un waypoint:
    nunca las baja.
  - **Nunca la velocidad** (§7, análisis §12.2), aunque el spec la sube. La búsqueda queda ×1 (D31).
- ✅ **Debug:**
  - F9: fila `escalada` (nivel, de cuántos puzzles o de F10, y los multiplicadores).
  - F10: sección *ESCALATION*, con *Tier -*, *Tier +* y *Auto*. Las testbeds no tienen puzzles, así
    que el nivel se prueba desde ahí.
  - Una línea de consola cada vez que instala un nivel.
  - *Validate Navigation Setup* avisa de dos niveles con el mismo umbral y de un multiplicador de
    sentidos menor a 1.
- ✅ **Tests:** `EscalationRulesTests`, en `WIRED.Nemesis.Logic`.
- **Falta jugarla.** En Zona1 el Nemesis sólo aparece en el escape (D25), así que ahí la escalada
  sólo se ve con todos los puzzles hechos. Donde se va a notar es la Zona 2.
- **Verificación:** casos 56 y 57.

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
- **Los estados leen la creencia, no los sensores.** Si un estado necesita algo que la creencia no
  dice (el punto visto, una pista), se agrega a la creencia; no se vuelve a leer `FieldOfView` o
  `FieldOfListening` por atrás. Así se llegó a tres parches distintos al mismo problema (§17.2).
- **Una pista no es el jugador.** Señuelos y ruido del Director se investigan, pero no renuevan la
  creencia sobre el jugador ni mantienen viva una persecución.

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
| D18 | ¿Un señuelo renueva la creencia sobre el jugador? | **No.** Es una pista: se investiga (y la radio se rompe), pero no mueve ni rejuvenece la creencia sobre el jugador. Si no, la alarma —audible en todo el nivel durante 30 s— mantiene viva cualquier persecución. |
| D19 | El ruido sintético del Director, ¿es pista o evidencia? | **Pista.** El Director "no toca el FSM" y tampoco tendría que tocar la creencia: hoy su ruido, al ser el más fresco, se vuelve la posición del jugador. Como pista sigue empujando a `Investigating`, igual que antes. |
| D20 | Evidencia que contradice la creencia (un ruido lejos de donde te vio hace un segundo): ¿qué gana? | **Decidido (27/09): la evidencia del jugador gana.** La procedencia sale de quién hizo el ruido: un ruido del emisor del jugador *es* el jugador, y un señuelo o un pulso del Director es una pista (D18, D19). <br>• Si la evidencia cae dentro de lo alcanzable, se fusiona con la creencia y el radio se achica. <br>• Si cae afuera, **reemplaza** la creencia (`NemesisBelief.cs:240-244`). Sólo puede pasar después de un respawn o un warp, y ahí el jugador está donde sonó. <br>• La plausibilidad queda para evidencia sin procedencia, que hoy no existe. <br>*Antes decía:* "ninguna de las dos de golpe; si el jugador no pudo llegar ahí, es una pista aparte". Eso servía cuando no se sabía quién hacía el ruido. |
| D21 | ¿Aguantar la respiración dentro de un escondite protege de la proximidad? | **Sí (decidido el 27/09, revisa D13 y D14).** Respirando, la proximidad a través de la carcasa sigue instantánea; aguantando, no te detecta por proximidad ni por las rendijas en un escondite que no sospecha: llega, mira a los lados y se va. Si aguantás de más, la exhalación lo trae de vuelta. Un escondite **sospechoso** se abre igual: ahí aguantar ya no salva. |
| D22 | ¿Un ruido que sale de un escondite ubica el escondite? | **No de entrada: marca la zona.** El primero manda a investigar el área (radio grande, §17.6); si vuelve a sonar desde el mismo escondite, lo sospecha, y recién ahí va a la puerta. **Implementado:** el primero, con la 2B parte 2 (radio ×2, sin ir a la puerta); el segundo, el 28/09 con la 2D (a menos de 1.5 m del primero, dentro de 60 s y después de más de 1 s de silencio). |
| D23 | ¿Revisa escondites que el jugador ya usó? | **Sí, con un medidor de uso por escondite (decidido el 27/09).** Mientras investiga o busca, abre los escondites usados que caen en la zona que revisa, con más chance cuanto más usados. Nunca cruza el nivel para revisar uno lejano (R4). Genérico para mesa, locker y container. |
| D24 | ¿Se conserva la intercepción de `Searching` (`TryGetInterceptPoint`)? | **Decidido (27/09): se saca** (§18.1: elige el waypoint "adelante" al que llega antes, que suele ser uno pegado a él, y se recalcula con cada ruido). La búsqueda barre alrededor de la creencia. Si el playtest pide cortar el paso, que sea un punto del NavMesh sobre el rumbo observado (`NavMesh.Raycast` desde la creencia) y sólo con velocidad observada alta, nunca un waypoint. El flanqueo de la persecución (`NemesisPursuit`) no se toca: ése es a propósito. |
| D25 | ¿El Nemesis aparece en el gameplay de Zona1? | **Resuelto por diseño (27/09): no.** El Nemesis se activa en la **Zona 2** y, en Zona1, sólo en la cinemática final. Que duerma todo el gameplay de Zona1 (`wakeOnlyFromScript`, §14.1) es lo buscado, no un problema. Consecuencias: <br>• Todo lo de los §17 y §18 se prueba en la testbed y en `TestIñaki` hasta que exista la Zona 2. <br>• Las zonas, los disparadores y las rutas del Director en Zona1 son configuración sin efecto en el juego. <br>• Los seis tramos de ruta sin camino de la planta baja ([§18.3](#183-el-director-qué-hace-de-verdad)) no afectan a nadie mientras esas rutas no se reusen. <br>• La Zona 2 tiene que nacer con su Director armado (§14.2) y sus rutas validadas. |
| D26 | Investigar un ruido **tuyo** y no encontrar nada, ¿vuelve a patrullar o busca? | **Decidido (27/09): busca un poco** (§18.5 B): una búsqueda corta alrededor de la creencia (tope ×0.5) mientras el silencio siga corto. Una pista (señuelo, pulso del Director) no escala: se mira y se sigue. Es el "el oído es binario" del §17.2. |
| D27 | ¿Quién registra `EscapedWhileHidden`, y qué es "dentro del radio de barrido"? | **Implementado (27/09, Fase 3): el tracker, no `NemesisHidingAwareness`** (el §5.3 decía lo contrario). El tracker es del Director y puede mirar al jugador real; la conciencia del Nemesis no tiene por qué saber de hábitos. "Dentro del barrido" es a ≤ `nearbyRadius` (8 m, del orden de `roomSweepRadius`) por NavMesh del área donde terminó la búsqueda, que es la creencia: con la 2B, el barrido deja de tener un radio fijo. El mismo radio define "lo cazaba cerca" para el medidor (D23). A revisar con los datos de la Fase 3. |
| D28 | ¿`EscapedWhileHidden` y `SameSpotReused` se cuentan al terminar la búsqueda y al entrar (§5.3), o al salir? | **Implementado (27/09, Fase 3): cuando la estadía se vuelve escape** (R1: escapes, no intentos). Eso pide tres cosas: que el Nemesis haya cazado cerca, que el jugador salga por su cuenta y que después pasen 5 s sin captura ni persecución. Salir mientras te saca, o que te vea salir y te agarre, no es escape. Una búsqueda puede terminar porque oyó la exhalación y después volver a abrir la puerta. Se cuenta una búsqueda aguantada por cacería (hasta que vuelve a patrullar). El +1 extra del medidor (D23) sigue la misma regla. Con el *Hide* de F10 no hay salida que esperar y cuenta en el acto. A revisar con playtest si 5 s es poco o mucho. |
| D29 | Después de una bajada, ¿sigue comprometido con `Traversing`? | **Decidido (27/09, Fase 8): no.** El peldaño "ya se comprometió con el montacargas" sostiene `Traversing` mientras el estado dure menos de `ElevatorCommitTime` (12 s), y una bajada se camina y se cruza en 2–3 s. Así, aterrizaba y seguía el resto de la ventana en `Traversing`, corriendo hacia una creencia que deja de actualizarse apenas no te ve y sin nada de la persecución: lo mismo que WIR-028 vio con los links generados. El caso 12 pide lo contrario (aterriza y persigue). Al terminar una bajada (aterrizada, cortada en el aire o abandonada, D30), `NemesisElevatorUser.HasJustEndedDrop` queda en verdadero 0.5 s, y la fachada lo suma a `HasGivenUpOnElevator`, que es lo que ese peldaño ya pregunta. No cambia la escalera: ni peldaños ni predicados nuevos. El montacargas tenía el mismo problema. Se creía que no se notaba, porque el viaje se come casi toda la ventana, pero en el playtest del 27/09 sí se notó: arriba seguía en `Traversing` los segundos que quedaban, y un ruido de abajo lo mandaba de vuelta a la cabina. Desde entonces un viaje completo lo suelta igual (`HasJustEndedRide`, 0.5 s). |
| D30 | Asomado al borde, ¿se tira aunque vea al jugador arriba, a su lado? | **Decidido (27/09, Fase 8): no, se echa atrás.** Mientras se asoma o flexiona, si lo ve y la creencia está más cerca en altura del piso donde está parado que del de abajo, sale del link y deja la bajada en enfriamiento. Es el "abandonar si el jugador está acá" del montacargas (`ShouldAbandonForPlayer`), pero sin el veredicto de ruta: medido desde un punto parado sobre un link, ese veredicto es justo el que oscila. Al jugador que ve abajo, por el hueco, nunca lo frena: es el caso para el que existe la bajada. Ya descolgándose, no se echa atrás. |
| D31 | ¿La escalada acorta la búsqueda, como pide el spec (12 → 10 → 8 s)? | **Implementado (28/09, Fase 7): no; la búsqueda queda ×1 en los tres niveles.** El spec pensó un timeout fijo. Con el enfriamiento de la 2B parte 3, acortar la búsqueda premia esconderse y esperar (C1), y alargarla ya es la persistencia que presta el Director. El multiplicador está por nivel en `SO_NemesisEscalation`, para calibrarlo en la Zona 2. |
| D32 | ¿Qué es "oído" en la escalada? | **Implementado (28/09, Fase 7): `NoiseRangeScale` y `ListenRange` juntos.** Así cada ruido se oye un 10 % más lejos y el tope se corre con él. `ListenRange` solo es el tope de 15 m: caminando se te oye a 10 m, así que sólo cambiaría para quien corre. El préstamo de sentidos del Director tenía esa misma limitación (multiplicaba sólo `ListenRange`, y su ×1.25 de oído no se notaba caminando): arreglado en el préstamo del Director (28/09), que ahora escala los dos (caso 61). |
| D33 | ¿Cómo se pasan los "módulos" del spec a puzzles, y qué pasa si un checkpoint deshace uno? | **Implementado (28/09, Fase 7): niveles a 0, 2 y 3 o más puzzles.** El primer puzzle es el que despierta al Nemesis (spec §7.1), así que el "módulo 1" del spec son 0–1 puzzles. La cuenta es de la sesión (New Game la pone en 0) y se lee cada vez: si un respawn deshace un puzzle, el nivel baja con él. Con la Zona 2 hay que revisar los umbrales: va a llegar con los 4 puzzles de Zona1 hechos, en el nivel más alto. |
| D34 | ¿Cuándo suena el aviso de que **sabe** en qué escondite estás (§16.2, D1, D16)? | **Decidido (28/09): cuando el escondite pasa a conocido y el Nemesis está a más de 2 m de su puerta.** Es lo único que, desde adentro, separa "sabe" de "adivina", y el margen de D1 para salir antes de que llegue. Casos: <br>• Te vio entrar, o te distinguió por las rendijas: suena. <br>• Sólo sospecha: no suena. Es justamente la duda que el aviso tiene que distinguir. <br>• Lo sabe ya en la puerta ("lo tiene encima", "lo abrió"): no suena. Abre en el acto, y el golpe es la música al abrir (D13: llega en silencio). <br>Se lee de `KnownHidingSpot` en `NemesisAudio`, sin tocar `NemesisHidingAwareness`. Comparte un enfriamiento de 3 s con "te perdí". Clip provisorio: `voice_chase` a pitch 0.8. |
| D35 | Una pista (un señuelo) que llega mientras la búsqueda sigue tibia, ¿la saca de buscar? | **Decidido (28/09, 2B parte 4): sí, si el foco pasó a la pista.** El peldaño de la pista ("su atención está en una pista", `FocusIsLead`) va **arriba** de "le queda presupuesto de búsqueda". Antes, `HearsLead` estaba abajo, y con la búsqueda tibia ninguna radio lo sacaba: el caso 31 no podía pasar. No es cualquier pista: antes el árbitro la compara con la creencia, con compromiso, margen y anti-titubeo. Una que no le gana a una búsqueda fresca no cambia el foco, y una que suena donde ya busca "suma" (§17.5) y tampoco. Abajo de "está revisando un escondite": un escondite sospechado le gana a cualquier pista. Y el peldaño "para llegar hay que tomar el montacargas" pide que el foco **no** esté en una pista (revisión del 28/09): si no, tomaba el montacargas hacia la creencia que el árbitro acababa de descartar, y del otro lado la pista lo hacía volver. Un viaje ya empezado se termina igual. |
| D36 | ¿Cómo suman el vistazo y un ruido suave tuyo (§17.3, caso 26)? | **Decidido (28/09, 2B parte 4): en el mismo medidor, con tope.** Un ruido tuyo suave (sonoridad ≤ `softNoiseLoudness`, 1.5: agachado) que no sale de un escondite sube la sospecha del vistazo a `softNoiseSuspicionRate` (0.6 por tiempo de detección). Sin contacto de reojo, el ruido lo sube sólo hasta `noiseOnlySuspicionCap` (0.9), y nunca lo baja: lo que ya puso el ojo se sostiene mientras sigan los pasos. **Un ruido solo nunca es un avistamiento**, porque el oído no ve. Sí puede pasar el umbral de sospecha y mandarlo a mirar. Con un vistazo en el mismo momento, se llena antes que con cualquiera de los dos solo. |
| D37–D40 | Del `Plan-Busqueda-Nemesis.md`, que se perdió (§19.1). | D38 (escondites por el mapa), D39 (el oído no es un GPS) y D40 (el ancla de zona ignora al jugador real durante la caza) están en `CLAUDE.md` y en comentarios del código; se pasan a esta tabla en la etapa I. D37 no aparece en ningún lado. |
| D41 | D38 abre un escondite por "valor del mapa × cuánto lo usaste": ¿y uno que nunca usaste? | **Decidido (05/10): el mapa solo puede abrirlo, con un umbral alto.** `Investigating` deja de sortear escondites. Etapa E. |
| D42 | ¿La patrulla puede tomar el montacargas sin estar siguiendo nada? | **Decidido (05/10): sí, cuando su ruta lo pide.** No se cruza por un escondite sospechado ni por una creencia vieja en el otro piso. Etapa D. |
| D43 | Ida y vuelta por el montacargas (cheese C7): ¿hay tope? | **Decidido (05/10): después de dos viajes siguiéndote, se planta a esperar en el descanso.** Etapa D. |
| D44 | Montacargas sin energía: ¿el Nemesis lo usa igual? | **Decidido (05/10): no puede usarlo, igual que el jugador.** Etapa D. |
| D45 | ¿De qué depende que la persecución aguante cuando te pierde de vista un instante? | **Decidido (05/10, Iñaki): de una mezcla de la última posición vista, lo último escuchado y la creencia de dónde podrías estar, con el oído pesando menos.** Esa mezcla es el radio de `NemesisBelief`; el peldaño "todavía sabe dónde está" lo compara con `Chase Hold Radius`, con un tope de tiempo sin vista (`Chase Hold Max Time`) porque la persecución sin vista se queda en el último punto visto (§16.4). Etapa B. |
| D46 | ¿Para qué sirve el ruido del jugador cuando no alcanza para más? | **Decidido (05/10, Iñaki): el ruido no dirige la persecución, y uno que no alcanza para investigar sólo reordena la patrulla** ("iba a ir por izquierda pero escuchó algo por derecha, entonces va a la derecha"). Lo primero ya es así: perdida la vista va al último punto visto y pasa a buscar. Lo segundo es la etapa E4, y falta decidir qué ruido es el que no alcanza: hoy todo ruido oído manda a `Investigating`. |

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
| Bajadas: recuperación al aterrizar | 0.6–0.9 s (la duración del clip); 0.75 en `SO_NemesisMovement` | La ventana del jugador; menos se siente injusto |
| Bajadas: enfriamiento por link | 8 s | Que no suba por la escalera y vuelva a tirarse en loop |
| Creencia: radio de la vista | 0.5 m | Un avistamiento es una posición |
| Creencia: radio del oído | 1 m + 15 % de la distancia; ×1.5 por pared, ×1.3 por piso | Un ruido es "por ahí", y crece con lo que lo tapa |
| Creencia: crecimiento del radio sin evidencia | 4.5 m/s | Velocidad del jugador corriendo: donde pudo haber ido |
| "El mismo lugar" (fusión) | Dentro del radio + 4.5 m/s × tiempo transcurrido + el radio de la evidencia | Adentro se fusiona; afuera, la evidencia del jugador reemplaza la creencia (D20, decidido el 27/09) |
| Sospecha compartida: umbral | 0.4 | El `awarenessTriggerThreshold` de hoy: mismo comportamiento de la periferia |
| Sospecha compartida: aporte de un ruido | Un ruido suave tuyo (sonoridad ≤ `softNoiseLoudness` 1.5: agachado; no desde un escondite) suma `softNoiseSuspicionRate` 0.6 / `awarenessBuildTime` por segundo mientras suena. Solo, lo sube hasta `noiseOnlySuspicionCap` 0.9 y no lo baja | D36 (28/09): un ruido solo nunca es un avistamiento. Un paso suave y un vistazo juntos pasan el umbral antes (caso 26). *Planeado:* 0.25 por ruido nuevo |
| Re-elección: margen para cambiar de foco | El nuevo tiene que valer 0.05 más que el actual, con el compromiso y el "casi llego" ya sumados | La histéresis la hace el compromiso; el margen sólo corta los empates. *Planeado:* ≥ 25 % más |
| Re-elección: ventaja de compromiso | +0.3 al elegir, decae a 0 en 3 s. Sólo contra algo del mismo rango o menor (vistazo < pista < vos) | Lo recién elegido no se abandona por algo apenas mejor; lo viejo sí. Un paso tuyo no espera a que se le pase lo de las cadenas. *Planeado:* +50 % |
| Re-elección: tiempo mínimo entre cambios del mismo tipo | 2 s (`focusAntiDither`) | Anti-titubeo (caso 29). *Planeado:* 1.5 s, el `InvestigationRetargetInterval` |
| Re-elección: mismo lugar | Mismo señuelo, o < 3 m del foco y del mismo tipo = actualización, no cambio. Dos señuelos distintos nunca son lo mismo; un ruido anónimo a < 3 m de un señuelo, sí. Vos siempre sos el mismo | Pregunta 4 del §17.4. Cada señuelo se rompe, se habitúa y se descarta por su cuenta (revisión del 28/09) |
| Re-elección: pausa al cambiar | 0.4 s mirando hacia lo nuevo | Que se lea como una decisión |
| Elección: valor de la creencia sobre el jugador | 1 × confianza 1 / (1 + radio / 4) (0.5 m → 0.89; 8 m → 0.33) × frescura 0.5^(edad / 6 s) | Una creencia vieja e imprecisa pierde contra un señuelo fresco (caso 31). *Planeado:* 0.8 × confianza × frescura a 0 en `BeliefMemoryTime` |
| Elección: frescura | Media vida 6 s (`focusFreshnessHalfLife`), para vos y para los vistazos | Del orden de la ventana de silencio de la búsqueda (8 s) |
| Elección: valor base de los señuelos | Alarma 0.7 · radio 0.6 · cadenas 0.45 · ruido sin identificar 0.4 (`leadValue*`) | §17.5 |
| Elección: valor de un vistazo | 0.5 (`glimpseValue`), radio 1.5 m; se ofrece sólo con la sospecha pasada el umbral; se olvida a los 8 s | Menos que vos, más que las cadenas: vio algo |
| Elección: piso de atención | 0.12 (`focusAttentionFloor`): una pista o un vistazo que vale menos se ignora | Lo que deja a las cadenas lejanas sin efecto a la tercera (caso 32) |
| Elección: habituación por señuelo | ×0.6 (`leadHabituation`) cada vez que ese señuelo lo hizo ir sin encontrar nada, sin piso: el piso de atención lo corta. Por sesión. Uno que ya revisó mientras sigue sonando se descarta hasta que se calle | Las cadenas dejan de ser un botón. *Planeado:* piso 0.2 |
| Elección: compatibilidad | Señuelo dentro del radio de la creencia + 3 m (`leadSumsMargin`), sólo mientras busca → suma a la creencia, no compite | Ir ahí sirve para las dos cosas (caso 33) |
| Elección: costo de llegar | × 1 / (1 + camino / 60 m) (`focusCostDistance`); sin camino ×0.4; en otro piso ×0.7. Las pistas se miden por NavMesh; vos y los vistazos, en línea recta | La alarma se oye en todo el nivel; llegar no es gratis. *Planeado:* × (1 − d / 60), piso 0.3 |
| Elección: "casi llego" | +0.15 al foco actual a menos de 4 m; nunca a la creencia | No abandona algo que tiene al lado. Una búsqueda rodea la creencia todo el tiempo: con el bono no la dejaba nunca. *Planeado:* +20 % |
| Proximidad a través de la carcasa | Respirando: instantánea (como hoy). Aguantando: no detecta | D21 |
| Rendijas mientras aguanta | No acumulan en un escondite que no sospecha | D21: el clásico "mira y se va" |
| `maxHoldSeconds` | 8 s (hoy 6) | Alcanza para una revisión de puerta (4 s) más la llegada |
| `exhaleNoiseRadius` | 2.0 (hoy 2.5): ~5 m, ~4 m con la carcasa | Sigue siendo el costo de aguantar, sin delatar desde media sala |
| Ruido desde un escondite: radio | ×2 sobre el radio normal del oído | Marca la zona, no la puerta (D22) |
| Medidor de uso por escondite | +1 por uso, +1 extra si lo cazaba cerca; −0.1 por minuto; sobrevive a la captura | D23; R1, R5, R7 |
| Chance de abrir un escondite usado al investigar la zona | medidor × 0.25, tope 0.85 | Que siga siendo apuesta (§5.2) |
| Medidor para revisarlo primero / para romperlo | 2 / 4 | Los umbrales de C2 de antes |
| Escondites usados: zona que revisa | `Investigating`: 6 m alrededor del punto al que llegó · `Searching`: el disco de barrido | R4: nunca cruza el nivel por uno (caso 40) |
| R3: la primera revisión por hábito | Sólo un escondite a menos de 12 m del jugador y en su piso, mientras `CheckHidingSpots` no corrió nunca. Cuenta como dada al abrirlo | Del orden del oído: la primera vez se ve o se oye |
| Segundo ruido desde el mismo escondite (D22) | A menos de 1.5 m del primero, dentro de 60 s y después de más de 1 s de silencio; sospecha el escondite más cercano, a 2 m como mucho | Un suspiro largo es un solo ruido, no dos |
| Barrido de búsqueda: radio | El de la última evidencia + 1 m, entre 3 m y `RoomSweepRadius` (8), congelado hasta re-centrar | El de la creencia crece a 4.5 m/s y llega a 8 m en ~1.7 s, antes de que entre `Searching`: sirve para la plausibilidad, no para barrer (§18.4) |
| Barrido: re-centrar | Evidencia nueva fuera del disco actual; adentro, corre el centro sin reiniciar | Pregunta 4 del §17.4: `Sequence` sube ~10 veces por segundo |
| `searchMinTime` | 6 s | Siempre mira un poco. Por debajo de `maxHoldSeconds` (8), o la exhalación forzada caza siempre (D21) |
| `searchQuietWindow` | 8 s de silencio desde la última evidencia del jugador | ~Un `investigationTimeOut`: lo que hoy tolera `Investigating` |
| Calidad de la última evidencia | Vista ×1.25 · ruido ×1 · a través de pared o piso ×0.75 | Te vio: insiste; te oyó por una pared: no sabe tanto |
| `searchHardCap` | 30 s | Que un jugador ruidoso que no se deja ver no lo tenga buscando para siempre |
| Persistencia del Director | `BuildUp` 1.0 → 1.5 con la sensibilidad creciente · `SustainPeak` 1.0 · `PeakFade` 0.75 · `Relax` 0.5 | Multiplica ventana y tope; préstamo de números, como los sentidos |
| Escalada de `Investigating` (D26) | Búsqueda con tope ×0.5 si lo investigado fue un ruido tuyo | Una pista no escala |
| "Vuelve a pasar" | 20–40 s después de la última búsqueda vacía, ancla y pesos 0.5 durante 30 s, sólo en `BuildUp` y con el Nemesis en `Patrolling`; si todavía caza, reintenta a los 10 s | C1 sin hábitos; que sea sesgo, no cita. Una cacería puede terminar varias búsquedas (Searching → Investigating también es un fin): cuenta la última |
| Silencio del Director (`QuietTime`) | Lo cortan persecución, "el jugador lo ve", escondido con búsqueda cerca, captura, la proximidad sólo por encima de 0.75 (~3 m), y buscar o investigar con la creencia de menos de 10 s (`encounterBeliefFreshness`) | §18.3: con la regla vieja, en la simulación de la testbed arrancaba en el 40–85 % de las corridas de 10 min; con proximidad > 0.5, en el 73–95 % |
| `searchTailTimeout` (música) | 50 s (hoy 25) | Por encima del tope máximo: 30 s × 1.5 |
| "Cerca" para los hábitos (`nearbyRadius`) | 8 m por NavMesh | Del orden de `roomSweepRadius`. Los 12 m de la proximidad del vignette cubren el 93,6 % del NavMesh de la testbed (medido el 27/09): casi siempre sería "cerca" |
| Decaimiento de los hábitos | 5 min sin bajar desde el último registro, después −0.1/min | R7, D4. Sin la espera, un contador en el umbral volvía a quedar abajo al segundo |
| Bajadas: fases (`SO_NemesisMovement`) | Giro 360°/s · asomarse 0.6 s · despegar 0.35 s (`Hop`) · darse vuelta 0.75 s y colgarse 0.4 s (`Hang`) · recuperación 0.75 s | La mitad de los rangos del §15.5: se ajustan cuando estén las animaciones, y la recuperación tiene que durar lo mismo que `Land Heavy` |
| Bajadas: arco | Gravedad 12 m/s² · la `Hop` sube 0.3 m antes de caer · la `Hang` se cuelga 1.9 m bajo el borde · mínimo 0.35 s en el aire | Algo más pesada que la real (9.8). Sin los 0.3 m, la `Hop` roza el canto del piso; colgada, una `Hang` de 4 m es una caída de 2 m |
| Bajadas: compromiso liberado al terminar (D29) | 0.5 s | Alcanza para que la escalera lo suelte una vez, y no impide que se forme otro si la ruta vuelve a cruzar pisos |
| Aviso de escondite conocido: distancia mínima a la puerta (D34) | 2 m en planta | Por encima de la proximidad extrema (1.5 m) y del frenado en la puerta (0.25 m): lo que queda adentro es "ya está abriendo" |
| Voces del Nemesis: enfriamiento común | 3 s | Que el aviso y "te perdí" no se pisen, y que un escondite que se olvida y se vuelve a saber no repita el aviso de inmediato |
| `ExitPose` ↔ `ApproachPoint` (validador) | ≤ 0.75 m en planta: `catchMaxReach` 1 − frenado 0.25 | D16: salir mientras te saca te deja a su alcance. Los tres prefabs quedan a 0.1–0.25 m |
| Escalada: niveles | Desde 0, 2 y 3 puzzles completos | Spec §7.2 (módulos 1, 2 y 3+; el primer puzzle lo despierta). D33 |
| Escalada: vista / oído | ×1 · ×1.1 · ×1.15 / ×1 · ×1 · ×1.1 | Spec §7.2. El oído multiplica el ruido y el tope juntos (D32) |
| Escalada: búsqueda / velocidad | ×1 en los tres niveles / nunca | D31; §7 y análisis §12.2 |
| Escalada: variación de ruta | Como está (0.15) · al menos 0.25 · al menos 0.40 | Spec §7.2 (10 / 25 / 40 %), como piso de las chances de invertir la ronda y de saltear un waypoint |

Referencias del proyecto para calibrar: jugador 2.5 m/s (agachado 1.25, corriendo 4.5; con M1
2.0 / 3.6); ruido del jugador agachado 2.5 m / caminando 10 m / corriendo 15 m (tope); Nemesis
patrulla 2.75 / investiga 2.5 / persigue 3.0 / busca 2.75; vista 7 m, foco 80°, periferia 170°;
oído 15 m de tope; proximidad extrema 1.5 m; alcance de captura 1 m; búsqueda 15 s (fija hasta la
2B parte 3, §18); gracia de persecución 2.5 s.

---

## 13. Casos de prueba

En `Scenes/Dev/NemesisTestbed` (F9 HUD, F10 consola) y después en `WIRED_Zona1_Blockout` desde
`Bootstrap`. Los de escondites (1–5, 11, 17–21) van en `TestIñaki.unity` → *Hiding Test Area*
mientras la testbed no tenga escondites. El checklist completo de la testbed, paso a paso, está en
`docs/Checklist-NemesisTestbed.md`.

| # | Situación | Esperado |
|---|---|---|
| 1 | Te persigue, entrás al locker a la vista. | Nivel A: va directo al `ApproachPoint`, lo abre y te captura. F9 muestra `"sabe en qué escondite está"` y `escondite: sabe … (lo vio entrar) · yendo → revisando`. No se queda clavado en la puerta. |
| 2 | Te persigue, doblás una esquina, entrás al locker fuera de su vista. | Barre la habitación y no revisa el locker (sin desbloqueo). Se va a los 15 s (desde la 2B parte 3: cuando se enfría, §18.5 B). Cuenta un `EscapedWhileHidden`. Tampoco **sospecha** aunque el medidor siga bajando de la persecución. |
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
| 22 | Suena la alarma de incendio mientras te persigue y te ve. | Te sigue persiguiendo a vos. F9: creencia de *vista*, la alarma como *pista*. Al perderte, busca primero donde te vio, no en la alarma. Si la alarma sigue sonando, a los ~7 s de búsqueda sin evidencia tuya el foco pasa a ella (F9 `cambió: alarma … > vos …`) y va (§17.5: la alarma le gana a una creencia vieja). |
| 23 | Buscándote, con las cadenas sonando cerca (o cualquier ruido continuo). | La búsqueda mantiene sus pausas de mirar (`SearchPauseTime`); el destino cambia sólo cuando llega evidencia nueva. |
| 24 | Te ve de reojo en campo abierto (sin escondite), sin haber hecho ruido antes. | Va al punto donde te vio de reojo; no a un ruido viejo, y no se queda quieto. |
| 25 | Activás la alarma lejos y te quedás quieto en otra zona, sin que te haya sentido. | Va a revisar la alarma (pista). No arranca ni sostiene una persecución; la búsqueda vence a su tiempo. |
| 26 | Un paso suave y un vistazo de reojo en el mismo lugar, casi juntos. | La sospecha pasa el umbral más rápido que con cualquiera de los dos solo. |
| 27 | Te pierde de vista y seguís corriendo detrás de una pared. | La creencia se corre con tus pasos, pero su radio crece: no lo lleva de la mano al escondite (el bug del §16.4, sin el parche de `NemesisPursuit`). |
| 28 | Investigando las cadenas, hacés un paso suave al otro lado de la sala. | Cambia de idea: gira hacia vos, pausa corta, y va hacia el paso (el jugador vale más que una pista). F9 muestra el foco nuevo y por qué cambió. |
| 29 | Dos ruidos parecidos que se alternan a los dos lados (cadenas y radio). | No va y viene: elige uno y lo termina. Cambia sólo si el otro vale claramente más o si el primero se enfría. |
| 30 | Te ve, y activás la radio. | No va a la radio: te sigue persiguiendo. |
| 31 | Te perdió hace 10 s (creencia vieja) y suena la radio lejos. | Va y la rompe. F9: "radio 0.6 > creencia 0.3". |
| 32 | Sacudís las cadenas tres veces seguidas desde lejos. | Cada vez le da menos bola; a la tercera ya no cruza el nivel por ellas. |
| 33 | El señuelo suena justo donde cree que estás. | Va y barre alrededor: lo toma a favor, no como distracción. |
| 34 | Escondido, él llega a la zona por un ruido tuyo y vos aguantás la respiración. | Mira a la izquierda y a la derecha y se va: no te detecta por proximidad ni por las rendijas. Si te quedás sin aire con él cerca, la exhalación lo trae de vuelta, y ahora sospecha de ese escondite. |
| 35 | Escondido, un suspiro con él a 3 m. | Investiga la zona, no va derecho a la puerta. Un segundo suspiro desde el mismo escondite: lo sospecha y va a mirar. |
| 36 | Cortás la línea de vista, caminás hasta un locker y te metés. | Llega a la zona de tus últimos pasos y mira. Si aguantás, no te encuentra por pasar cerca de la puerta. |
| 37 | Rompiendo la radio, te ve. | Corta y te persigue. |
| 38 | Sospecha de tu escondite (segundo ruido, o te distinguió por las rendijas) y vos aguantás. | Lo abre y te saca: sospechar te vuelve presa. |
| 39 | Te escondiste dos veces en la misma mesa; más tarde investiga un ruido en esa zona. | A veces (sorteado, más chance cuanto más la usaste) va a revisar la mesa. La primera vez pasa donde lo podés ver u oír. Si algo lo corta antes de abrirla, esa todavía no fue la primera. |
| 40 | Usaste mucho una mesa del otro lado del nivel; él investiga acá. | No cruza el nivel para revisarla: sólo abre escondites de la zona que investiga. |
| 41 | Te pierde de vista a ~5 m y seguís caminando detrás de una pared. | Va al punto donde te vio, mira, y barre **puntos del NavMesh** alrededor de tus pasos, no un waypoint. F9 (fila `búsqueda`) y el gizmo muestran el centro y el radio del barrido. |
| 42 | Buscándote, seguís dando pasos a su alcance sin que te vea. | El centro del barrido se corre con tus pasos sin cancelar las pausas de mirar ni borrar lo barrido. Sólo re-centra si un paso cae fuera del disco. |
| 43 | Te escondés fuera de su vista y aguantás cada vez que se acerca, sin hacer ruido. | La búsqueda se enfría y se va antes del tope (F9: `se enfrió`). Nunca se queda más de 8 s seguidos a menos de ~2.3 m de tu escondite sin sospecharlo. |
| 44 | Durante la búsqueda te quedás sin aire y exhalás. | La búsqueda se renueva y se corre hacia la zona del ruido, con radio ×2 (D22), no a la puerta. Una segunda exhalación desde el mismo escondite lo vuelve sospechoso. |
| 45 | Hacés ruido cerca todo el tiempo sin dejarte ver; después entrás al Hub y seguís caminando adentro. | La búsqueda no pasa del tope (30 s × persistencia); al tope, si te oye, investiga. Los pasos oídos desde adentro del Hub **no** la renuevan: no acampa en la puerta (C5). |
| 46 | F10 *Pico de tensión* justo después de que te pierde. | En `Relax` la búsqueda se enfría a la mitad y la patrulla se va lejos: la retirada se ve. Con la sensibilidad creciente activa, en `BuildUp`, la búsqueda dura más. F9 muestra la persistencia. |
| 47 | Una búsqueda termina vacía en `BuildUp` y te quedás en la zona. | Entre 20 y 40 s después la patrulla vuelve a pasar por ahí (F9 `presión`: `vuelta`). Si terminó en `Relax`, no vuelve. |
| 48 | Testbed: 90 s sin encuentros, con el Nemesis patrullando cerca sin sentirte. | Arranca la sensibilidad creciente sobre tu zona. Hoy arranca tarde: cada pasada a menos de 12 m reinicia el silencio, y después de un encuentro primero hay que esperar el Relax (§18.3). |
| 49 | Te escondés con el Nemesis patrullando lejos y salís. | F9 `escondites`: el escondite con 1 (el uso), sin `cazado`. Ningún contador de `hábitos` sube. |
| 50 | Te escondés; el Nemesis busca a ≤ 8 m, no te encuentra y se va; salís y te alejás. | Adentro, F9 muestra `adentro, cazado, 1 búsq.`; al salir, `escape pendiente`. Pasados 5 s sin captura ni persecución: `esc` +1 y el escondite +1 más (2 → `primero`). La próxima vez que te escapes de ese mismo escondite, `repite` +1. |
| 51 | Como el 50, pero te saca del escondite, o salís en sus manos (mientras te saca, o te ve salir y te agarra en la persecución). | Sólo cuenta el uso (+1): ni `esc`, ni `repite`, ni el +1 extra. |
| 52 | Loop alrededor de una columna durante 12 s. | `estanca` sube una vez por ventana (~3). `desbloquea` muestra `flanqueo` desde la primera y `defensa` desde la segunda. El comportamiento no cambia: nada lo lee todavía. |
| 53 | Una persecución termina con vos adentro del Hub. | `Hub` +1. Si en ese encuentro te capturaron (aunque fuera sin persecución previa) y el checkpoint cae en el Hub, no cuenta. |
| 54 | Escondido, el Nemesis busca, oye tu exhalación, investiga y vuelve a buscar, todo sin volver a patrullar. | Cuenta una sola búsqueda aguantada por cacería: una cacería termina cuando vuelve a patrullar. Después, 5 min sin repetir nada: los contadores de `hábitos` empiezan a bajar; el medidor del escondite baja desde el primer minuto. *Clear habits* (F10) pone todo en cero sin New Game. |
| 55 | Te persigue en `PISO_02` y llega al borde de una bajada; vos seguís arriba, a la vista (por ejemplo, rodeaste el hueco). | Mientras se asoma, se echa atrás y te persigue arriba (D30): nunca se tira con vos a su lado. F9 `bajada` pasa a `— · 1 en enfriamiento`, y en los 8 s siguientes baja por la escalera, no por esa bajada. Si en cambio estás abajo y te ve por el hueco, se tira igual. |
| 56 | Testbed: F10 → *ESCALATION* → *Tier +* hasta el nivel 2. | F9 `escalada`: `nivel 2 (F10) · vista x1.15 · oído x1.1 · búsq. x1`. El cono de visión del gizmo se alarga un 15 %, y caminando te oye a ~11 m en vez de 10. La velocidad no cambia (F9 `agente`). En la consola sale una línea `[NemesisEscalation] Tier 2 …`. *Auto* lo devuelve al nivel de los puzzles: 0 en la testbed. |
| 57 | Con una presión del Director andando (F10, botón de una zona), subí el nivel desde F10. | El préstamo se rearma en el acto sobre el nivel nuevo: vista ×1.15 × 1.25. Al soltar la presión (*Release*) queda el nivel, no la base. En una escena con puzzles, resolver el segundo sube al nivel 1 sin tocar F10, y un respawn que lo deshace lo vuelve a bajar. |
| 58 | Escondido en un locker, te vio entrar desde lejos (o te distinguió por las rendijas a 2–3.5 m). | En el momento en que pasa a "sabe" (F9 `escondite: sabe …`) suena el aviso, una voz grave, antes de que llegue a la puerta: todavía podés salir corriendo (D1, D34). Si sólo sospecha (F9 `sospecha`, va a mirar), no suena. Si te detecta ya en la puerta (≤ 1.5 m, "lo tiene encima"), tampoco: abre en el acto con la música. |
| 59 | Te pierde, busca y la búsqueda se vence sin encontrarte. | Al volver a patrullar dice "te perdí" (`voice_lost_01/02`) una sola vez, junto con el final de la música (D5). Si antes de patrullar se va a investigar un ruido o una radio, espera a que termine y vuelva a patrullar. Si te encuentra en el medio (`Chasing`/`Catch`), no la dice. |
| 60 | Testbed con F9 abierto: te pierde, busca, y sacudís las cadenas lejos; después hacé un paso suave cerca. | Fila `foco`: qué atiende (`vos`, `radio`, `alarma`, `cadenas`, `vistazo` o `nada`), cuánto vale, hace cuánto y, en un señuelo, las visitas vacías. Durante 10 s, la última decisión con la pregunta del §17.4 que la tomó, por ejemplo `cambió: cadenas 0.35 > vos 0.12 [11]`, `siguió con cadenas: … [9]` o `ignoró cadenas (0.08) [7]`. Después de revisar un señuelo sin nada, el foco vuelve a `vos` (`soltó cadenas: revisó: nada → vos`), no a `nada`, mientras haya creencia. |
| 61 | Testbed con una presión del Director sobre tu zona (F10, el botón de la zona) y la escalada en *Auto*: caminá (sin correr) alejándote de él. | Te oye caminando a ~12.5 m en vez de 10: el préstamo de sentidos (×1.25) escala también el radio de tus ruidos, no sólo el tope (D32). Al soltar la presión (*Release*) vuelve a 10 m. |
| 62 | Pasillo con una sola salida: te persigue y lo perdés de vista adentro del pasillo. Diez veces. | Va al último punto donde te vio y sigue hacia la salida. Nunca vuelve por donde vino. Con `Draw Search Pick`, ningún candidato dibujado detrás de él. *(Reconstruido el 05/10 del handout; WIR-062.)* |
| 63 | Una T: ibas hacia la izquierda cuando te perdió. Diez veces. | Izquierda la mayoría de las veces; nunca por donde vino. *(Reconstruido el 05/10 del handout.)* |
| 64 | *(Perdido: estaba en `Plan-Busqueda-Nemesis.md` y el handout no lo nombra. §19.1.)* | — |
| 65 | Corré hasta un locker que quede dentro de su oído y escondete sin que te vea entrar. | Busca la zona; no va a la puerta del locker. *(Reconstruido el 05/10 del handout; WIR-057.)* |
| 66 | El "asomarse" antes de cambiar de piso. *(Sólo queda el nombre: era la fase de pisos del plan perdido. Diferido.)* | — |
| 67 | Metete al Hub en plena búsqueda. | No acampa la puerta. *(Reconstruido el 05/10 del handout.)* |
| 68 | Te persigue y te pierde de vista. | Entra a `Searching` y sale a buscar sin quedarse 1,2 s parado en el lugar. |
| 69 | Búsqueda en un espacio abierto: el lugar que eligió queda a la vista de lejos y está vacío. | Elige otro sin caminar hasta ahí. |
| 70 | Patrullando, sin que te haya visto, hacé un ruido. | El rango de vista no se agranda: un ruido solo no lo sube; hace falta que te haya visto. |
| 71 | En una persecución, su ruta por el NavMesh da un salto de más de 10 m (una puerta que se cierra, un desvío largo). | No suma "estanca". |
| 72 | Te persigue hasta donde te vio por última vez y vos entrás y salís de su vista (el marco de una puerta, una columna) cada medio segundo. | Sigue en `Chasing` todo el tiempo. F9 `decisión`: "todavía sabe dónde está" en los huecos; nunca estadías de menos de un segundo en `Searching`. Etapa B, T2. |
| 73 | Te persigue, doblás una esquina a 4 o 5 m y te quedás quieto y en silencio del otro lado. | Llega a la esquina y pasa a `Searching` enseguida: no se queda parado más de medio segundo. Etapa B. |
| 74 | Lo mismo, pero seguís corriendo (te oye). | Aguanta en `Chasing` a lo sumo `Chase Hold Max Time` (1,2 s) desde que te perdió de vista y pasa a `Searching`, que sí va hacia donde te oye. No se queda parado en la esquina escuchándote. Etapa B. |
| 75 | A la vista y con camino, corré pisando el borde del NavMesh (el filo de una pasarela, pegado a una baranda). | Sigue en `Chasing`: una consulta de camino que falla no corta la persecución. En la traza, `state` no alterna `Chasing`/`Searching` con `sees=1`. Etapa B, T1. |
| 76 | Subite a un lugar al que no llega (el Hub, la escalera de arriba) y dejate ver; después bajá. | Arriba: no te persigue (WIR-018), como antes. Al bajar: tarda unos 0,75 s en volver a `Chasing` (`Route Verdict Settle Time`), no más. Etapa B. |
| 77 | Te persigue por un pasillo, doblás una esquina y seguís derecho por el pasillo nuevo. Diez veces. | Llega a la esquina y la mayoría de las veces sigue por donde fuiste, sin salir primero para el costado ni volver. F9, fila de la búsqueda: "tirando hacia donde ibas" en la primera tirada. Etapa C0. |
| 78 | Subite a un lugar al que no llega (una pasarela, el Hub) y quedate a la vista. | No vuelve a patrullar mientras te ve. Camina hasta lo más cerca que puede, se queda ahí y te mira; no barre la mirada de lado a lado ni corre en el lugar. Al taparte, vuelve a buscar. Etapa C2. |
| 79 | Sin haber usado ningún escondite, hacé que te persiga, cortá la vista y metete en un locker sin que te vea entrar. Diez veces. | Busca la zona y nunca va derecho a la puerta con el motivo "lo usaste antes". F9 `escondites`: el uso del locker aparece recién cuando salís. Etapa C1, WIR-057. |
| 80 | Pasillo angosto con una sola salida y salas del otro lado de sus paredes: que te persiga, perdelo de vista adentro y seguí corriendo hacia la salida (te oye). Diez veces. | Nunca vuelve por donde vino ni da la vuelta para revisar las salas de al lado. Con `Draw Possibility Map`: ningún valor detrás suyo ni del otro lado de las paredes; cuadrados grises sobre su rastro. F9, fila `mapa`: "rastro tapa N". Etapas C3 y C5, WIR-062. |
| 81 | Lo mismo, pero después de perderlo volvé por detrás suyo en silencio y hacé un ruido a unos metros de su espalda. | Se da vuelta y busca detrás: el ruido borra ese tramo del rastro. Etapa C5. |
| 82 | Que te persiga hasta una sala grande y perdelo ahí. | Lo que ya miró de lejos no lo camina: no va hasta el fondo de la sala a revisar algo que vio vacío desde la entrada. F9, fila `mapa`: "limpió … hasta 14 m" mientras te caza (no 7). Etapa C3. |
| 83 | Que te busque en una pasarela (perdelo de vista ahí) y hacé ruido abajo, donde se llega por la escalera. Diez veces. | Baja por la escalera hacia donde te oyó; no se queda arriba 6 s para después irse a patrullar. F9, fila de la búsqueda: "se lo debe a lo que sintió". Etapa C6, WIR-058. |
| 84 | Que te persiga, cortá la vista y metete en un locker con la puerta al costado o detrás de él cuando termina de doblar la esquina. | No lo sabe: no va derecho al locker con el motivo "lo vio entrar". Con la puerta de frente y a la vista, sí. Etapa C7, WIR-057. |

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
>
> **22/09/2026, más tarde: el Nemesis ya no aparece en el gameplay de Zona1**, sólo en la cinemática
> final (pedido del equipo). Ver la fila "Nemesis: activación": el Director, las zonas y los
> disparadores quedan armados pero inertes mientras el Nemesis duerme.

| Qué | En la escena (22/09) | Qué implica |
|---|---|---|
| GameObject `Nemesis Director`, bajo `---- SISTEMA ----` | **Activo**, en el origen (su posición no se usa), con `NemesisDirector` + `NemesisTension` | Corre `Awake` y hay singleton: la API responde y el ancla de presión llega a `NemesisController`. |
| `Puzzle Triggers` | 2: `sp2_contenedores` → `fondo norte` 0.5 / 45 s · `sp3_valvulas` → `valvulas` 0.7 / 60 s **+ entrada Mr. X** | La presión sube con el progreso. La entrada no va en `sp2` porque es el puzzle que despierta al Nemesis (§14.2), ni en el central (cinemática). |
| `NemesisPressureZone` | 6, bajo `Nemesis Director / Pressure Zones`, reubicadas a mano en el editor el 22/09: `montacargas` (-16.6, 0, 9.9) r 10 · `panel electrico` (-23.1, 0, 22) r 10 · `valvulas` (28.6, 0, 25.6) r 12 · `fondo norte` (0.7, -0.8, 27.4) r 15 · `ala oeste` (-9.8, 0, 27.3) r 9 · `centro este` (15.9, 0, 12.3) r 11 | Cubren **38 de 39** waypoints (seleccionando el Director se ve cuál falta). Ningún centro queda a menos de 6 m del Hub: el más cercano es `centro este`, a 7.8 m. El ruido y la entrada igual evitan los 6 m alrededor de la puerta. |
| `SafeZoneMarker` | En `'Safe Area '` y `Safe Area  (1)` | Es cómo el Director sabe dónde está el Hub (C5). Sin él la protección se apaga, y lo avisan el Director y el validador. |
| Volúmenes del Hub | Agrandados 0.2 m por lado el 22/09: `'Safe Area '` 13.22 × 4.27 × 21.37, `Safe Area  (1)` 4.65 × 4.16 × 11.63 (centros sin cambio) | Con el radio del agente en 0.3 el Nemesis se paraba 0.2 m más cerca de las puertas; en la norte (`DoorMetalRed (7)`, la del escape, con la pared al ras del volumen) el agarre llegaba ~0.6 m adentro. Así vuelve a ~0.4 m, lo de antes. Necesita re-hornear Zona1. |
| `SO_DirectorPacing` | Asignado | El ritmo de la Fase 5 está prendido. |
| Quién llama a la API | Los disparadores, el ritmo y `NemesisTestConsole` (F10) | Ni los módulos ni la narrativa piden presión todavía. |
| `noiseLayer` 8 (`DetectableAudio`) contra el `listenMask` del prefab (256) | ✅ Coinciden | El ruido sintético se oye. Si no coincidieran, `Start` lo reporta. |
| El resto de la tuning del componente | Valores por defecto: evaluación cada 3 s, pesos ×3, ruido cada 9 s con radio 4, sentidos ×1.25, entrada a 10–22 m con 2.5 s de pausa | Sirven para arrancar. |
| Rutas | 4 `NemesisRoute` asignadas al `NemesisController`, con pesos 3 / 1 / 1 / 2; la de peso 3 (`ROUTE 2 2F`) se abre con `sp1_panel_electrico` | La palanca 2 tiene con qué trabajar. |
| Nemesis: activación | `wakeOnlyFromScript` prendido (override en la escena, 22/09). `activatedByPuzzleId` sigue en `sp2_contenedores` (prefab) pero se ignora | Duerme toda la partida hasta que el escape lo toma (`NemesisCinematicActor` → `ActivateInPlace`, sin buscar spawn point: la cinemática lo pone detrás de su puerta). Sin anuncio ARC_02. Sólo la entrada Mr. X sale si el Nemesis no está activo. **Corregido el 27/09 ([§18.3](#183-el-director-qué-hace-de-verdad)):** los disparadores de `sp2`/`sp3` **sí** aplican su presión (pesos, clon de sentidos, ruido) sobre el Nemesis dormido. No se nota porque dormido no hace nada, pero si el escape arranca antes de 60 s de `sp3`, despierta con los sentidos del Director puestos. Apagar el flag vuelve al despertar con `sp2`. |

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
| `HidingSpot` | 1 | Raíz de `Locker.prefab` / `Locker2.prefab` (y de los prefabs de mesa y container) | Tipo; `SpotId`; hijo `ApproachPoint`; hijo `InteriorPose` con la cámara Cinemachine interior y los límites del spec; colliders propios (se llenan solos en `OnValidate`). El `BoxCollider` sólido **se queda en `Default`**. Hoy se usan los prefabs de `Prefabs/HidingSpotFather/` (variantes Locker / UnderTable / Container de un padre con un `NavMeshModifier` *Not Walkable* en `Model`, capa `Props`). **Ojo:** en una escena que no hornea `Default` (la testbed: `Ground|Wall|Props`) el collider sólido del container no hace hueco y queda NavMesh adentro; ahí hay que pasarlo a `Props` o sumar un `NavMeshModifierVolume` en una capa horneada. `ApproachPoint` a ≤1 m del interior. La `ExitPose` es también donde aparece el jugador cuando lo sacan, así que va a ≤0.75 m del `ApproachPoint` (D16; corregido el 28/09: decía "a ≤1 m del interior", y la mesa y el container quedan a 1.1–1.15 sin problema). | `SpotId` vacío o repetido; `ApproachPoint` fuera del NavMesh o a más de `catchMaxReach` (1 m) de la pose interior. Desde el 28/09 también: `ExitPose` a más de 0.75 m del `ApproachPoint`, adentro de un collider sólido, o sin piso abajo. |
| `SO_HidingData` | 1 | `ScriptableObjects/Hiding/` | Respiración, radios, multiplicadores por tipo, `lockerVisionExposure`. | — |
| `NemesisHidingAwareness`, `NemesisChaseProgress` | 2 / 4 | Raíz de `Nemesis.prefab`, junto a `NemesisStateManager` | Nada: se enganchan solos, como `NemesisPathOracle`. Sus números van al final de `SO_NemesisData`. | — |
| Peldaños nuevos | 2 / 4 | `SO_NemesisPriorities.asset` **y** `BuildDefaultLadder()` | En la posición que dice el §3.5. | Que el asset y el default no coincidan. |
| `NemesisBelief`, `NemesisChoice`, `NemesisDecoyBreaker` | 2B | Raíz de `Nemesis.prefab`, junto a `NemesisStateManager` | Nada: se agregan solos si faltan (`ResolveSibling`). Sus números van al final de `SO_NemesisData`. | — |
| Señuelos (`Decoy_Radio`, `Decoy_FireAlarm`, `Decoy_Chains`) | 2B parte 4 | En el nivel, donde diseño los quiera. En la testbed ya están (los puso *Build Decoy Stations*, borrado el 29/09) | El tipo sale del componente de al lado (`RadioDecoy`, `FireAlarmDecoy`, `ChainDecoy`); su valor, de `SO_NemesisData`. | — |
| `PlayerHabitTracker` | 3 | Escena `Data`, junto a `PuzzleStateManager`, `ModuleManager` e `InventoryManager` (los otros `ISessionResettable`) | `Singleton` persistente que se registra en `GameSession`: así sobrevive a la captura y al checkpoint y se resetea con New Game (D3). Referencia a `SO_CounterplayRules`. | — |
| `SO_CounterplayRules` | 3 | `ScriptableObjects/Nemesis/` | Las filas del §5.2. | Un umbral en 0, o un `chanceAtUnlock` fuera de 0..1. |
| `NemesisAmbushPoint` | 6 | En el nivel: GameObjects vacíos cerca de las salidas probables (del Hub, de las habitaciones con escondites), mirando hacia la salida | Posición y orientación. | Fuera del NavMesh, dentro del Hub, o con línea de visión directa desde la salida que vigila (tiene que esperar fuera de la vista). |
| `NemesisDropLink` + `NavMeshLink` | 8 | En el nivel: un GameObject estático por bajada, bajo un contenedor `Drop Links` (§15.6) | Hijos `TopEdge` y `BottomLanding` (se crean solos al agregar el componente) y el ancho del link. El tipo sale del alto; el costo (por estado) y el enfriamiento (global) están en los SO del Nemesis, no en cada bajada. | Ver la lista del §15.6. |

### 14.5 Mejoras chicas de editor para hacer en el camino

- ✅ (22/09) Un atributo `[PressureZoneId]` con drawer, igual que `[PuzzleId]`, para
  `PuzzleTrigger.zoneId`, que lista las zonas de la escena abierta.
- ✅ (22/09) *Validate Navigation Setup* reporta: el Director apagado habiendo zonas o disparadores;
  zonas sin id, repetidas, que no tocan ningún waypoint o con el centro a menos de 6 m del Hub;
  disparadores sin puzzle, sin efecto, con un `zoneId` que no existe o con entrada en el puzzle que
  despierta al Nemesis; y, como nota, la cobertura (`N/M waypoints`) y si falta `SO_DirectorPacing`.

---

## 15. Bajadas entre pisos

> Agregado el 19/09/2026. Relevado contra `6703f9d` leyendo código, escena y `ProjectSettings` como
> texto; lo marcado *verificar en el editor* no se pudo abrir.
>
> **27/09:** el código (parte 2) está construido; las diferencias con este diseño están en el
> [§9](#fase-8--bajadas-entre-pisos-independiente), en *Cómo quedó la parte 2*. Faltan apagar
> *Generate Links* (parte 1), las animaciones (parte 3) y poner bajadas en una escena.

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
| Animator | `NemesisController.controller` (ahora junto al modelo `TLLStalker`) tiene **Idle, Patrol, Chase y Catch** (`isCatching` lleva a `Catch` desde cualquier estado), más los seis estados de bajada **vacíos** (sin clip, §15.5) y tres clips sin conectar (`E_Attack`, `E_First_Contact`, `A_POSE`). Root motion apagado en `Nemesis.prefab`. | Todo lo del §15.5 es nuevo. Con root motion apagado, las animaciones van **in place** y el código mueve el cuerpo. |
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
2. ✅ **El área** (27/09). En *Navigation → Areas*, índice 5 → `NemesisDrop`, con costo 1: el costo
   real lo pone el código por estado. Sin el nombre, el código usa el índice 5 igual.
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
   jugador en el borde. En `Ignore Raycast` (27/09), no en `Props`, para no tapar la visión del
   Nemesis hacia abajo. Tampoco en `Player`: la máscara de objetivo del Nemesis leería la baranda
   como el jugador. El validador no la cuenta como geometría sin hornear.
5. ✅ **Testbed** (27/09). El *Drop Lab*, al sur de ENTRADA, lo arma
   *Tools/Nemesis/Build Drop Lab (NemesisTestbed)* (borrado el 29/09, en git): un entrepiso de 3.6 m con `Drop_Hang` y otro de
   2 m con `Drop_Hop`, cada uno con su rampa de vuelta y barandas invisibles. Rampas y no escaleras,
   por la pendiente que acepta el jugador (~17°).

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
  distingue desde adentro "sabe" de "adivina", y el margen de D1 es teórico. **✅ El aviso está
  hecho (28/09, D34, caso 58), con clip provisorio.** Siguen pendientes el SFX y la animación del
  pull-out, que necesitan arte.
- **D14:** detección a través de la carcasa sólo desde la puerta (C3) — playtest.
- **D15:** container a paridad de ruido (C2) — datos de la Fase 3.
- **D17:** clavar la mirada en vez de ir a mirar (C3) — playtest.
- Container en escenas que no hornean `Default` (C3, ronda 2) — §14.4.
- `NemesisFreeRoam.AddSampledPoints` podría rechazar puntos con `|Δy| > 1` para no mezclar pisos en
  el barrido (C2 #3 / C3). Hoy lo mitiga la prioridad por habitación. **✅ Hecho (27/09, 2B parte
  2):** filtro de 1.5 m sobre el centro, en los puntos sorteados y en los waypoints.
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

---

## 17. Percepción y creencia

> Agregado el 27/09/2026. Sale de un análisis del código, sin cambios, después de que en el playtest
> los sentidos se sintieron "no aditivos: se contraponen y generan conflicto". No se reprodujo en
> Play: todo lo que sigue está leído del código, con el archivo y la línea donde pasa.

### 17.1 Qué pasa hoy

Tres elecciones de "un solo ganador", una encima de la otra:

| Capa | Qué hace | Dónde |
|---|---|---|
| Oído | En cada barrido (0.1 s) se queda con **un** ruido, el que mejor se oye, y pisa la memoria anterior. No distingue jugador, señuelo ni pulso del Director. | `FieldOfListening.cs:201-224` |
| Vista | La periferia acumula, pero **no guarda dónde vio algo** hasta que el medidor llega a 1. | `FieldOfView.cs:300-347` |
| Creencia | `TryGetBelief` toma el sensor **más fresco** y descarta el otro. `BeliefAge` toma el mínimo de los dos. | `NemesisStateManager.cs:835-869` |

### 17.2 Cómo se nota

1. **La creencia salta.** Vista y oído barren cada 0.1 s con timers independientes: cuando los dos
   te perciben, cuál es "el más fresco" depende del desfasaje. Con tus pasos no se nota (el emisor
   está en vos), pero con un señuelo o un pulso del Director sonando en otro lado, la creencia —y la
   persecución, que apunta a ella (`NemesisPursuit.cs:131`)— salta entre vos y el ruido.
2. **Cualquier ruido rejuvenece la creencia sobre el jugador.** `BeliefAge` es el mínimo de los dos
   sentidos: la alarma de incendio (C6) la deja en 0.1 s durante 30 s, y los peldaños que sostienen
   persecución y búsqueda por edad de creencia no vencen.
3. **`HearsPlayer` es "oye algo".** El predicado lee `HasAudioTarget` (`NemesisDecision.cs:104`): un
   señuelo o el Director cuentan como el jugador para toda la escalera.
4. **`Searching` reapunta cada frame mientras oye algo.** La condición es "el sensor está prendido",
   no "llegó evidencia nueva" (`NemesisSearchingState.cs:175-183`). Cada reapuntado borra los puntos
   barridos, cancela la pausa de mirar y recalcula la intercepción: con un ruido continuo, busca sin
   frenar nunca. El §16.4 lo dejó así ("como antes").
5. **De reojo en campo abierto, va al último ruido.** "Vio algo de reojo" lleva a `Investigating`,
   que sólo lee el oído (`NemesisInvestigatingState.cs:190-217`). El arreglo de la Fase 2 (D17) cubre
   el escondite sospechoso; sin escondite, va a un ruido viejo o, si nunca oyó nada, se queda quieto
   hasta el timeout.
6. **Los sentidos no suman.** El oído es binario (un barrido audible = `Investigating`), la
   periferia acumula, el foco es instantáneo, y nada cruza entre ellos.
7. **Cada estado desconfía a su manera.** `NemesisPursuit` usa el punto *visto* en vez de la
   creencia (§16.4, `NemesisPursuit.cs:141-154`); `Searching` fuerza el ancla de vista al entrar;
   `Investigating` filtra con intervalo y distancia mínima. Tres parches al mismo problema, y por eso
   el comportamiento cambia según el estado.

### 17.3 Modelo propuesto

**Evidencia.** Cada vez que un sentido capta algo, produce una evidencia: posición, momento,
precisión (un radio) y procedencia.

| Procedencia | Qué es | ¿Mueve la creencia sobre el jugador? |
|---|---|---|
| `Sight` | Foco, periferia llena, proximidad | Sí, con radio chico |
| `Glimpse` | Periferia con contacto, medidor < 1 | No; sube la sospecha y guarda **dónde** |
| `PlayerNoise` | El emisor del jugador (pasos, respiración) | Sí, con radio según distancia y oclusión, si es plausible |
| `Lead` | Señuelo o pulso del Director | **No**: queda como pista aparte |

La procedencia sale de quién emite: el emisor del jugador cuelga de `PlayerStateManager`; los
señuelos ya llegan por su propio canal (`DecoyNoiseSource`); el pulso del Director se marca al
crearlo (el `NoisePulse` del §2.3 es el lugar natural).

**Creencia.** Posición + radio + confianza + procedencia de la última evidencia que la movió.

- Sin evidencia, el radio crece a la velocidad máxima del jugador: es "donde pudo haber ido".
- Una evidencia coherente (cae dentro de lo alcanzable) la corre y **achica** el radio: así suman
  los sentidos.
- **Plausibilidad:** una evidencia de jugador que cae fuera de lo alcanzable (radio + velocidad ×
  tiempo) no es el jugador; se trata como pista. Resuelve los señuelos sin casos especiales.
  **Reemplazado el 27/09 (D20):** los señuelos se resolvieron con la procedencia (quién hizo el ruido).
  La evidencia del jugador fuera de lo alcanzable reemplaza la creencia.
- La confianza sube con evidencia coherente y baja con el tiempo. Reemplaza a `BeliefAge` en la
  escalera, con umbrales equivalentes para no retunear todo de una.

**Pistas.** Una lista corta de cosas para revisar que no son el jugador. No son "lo último que se
atiende": compiten por valor con la creencia sobre el jugador, y un señuelo fresco le gana a una
creencia vieja (§17.5). Lo único que nunca hacen es sacarlo de una persecución con el jugador a la
vista.

**Sospecha compartida.** Un solo medidor, que suben los vistazos (como hoy) y los ruidos suaves del
jugador (nuevo), con decaimiento. Al pasar el umbral, `Investigating` va a la posición de la creencia
o del vistazo, no al último ruido.

**Re-elección: puede cambiar de idea, si vale la pena.** El Nemesis no se casa con su primera
elección: cada evidencia nueva es un momento para reconsiderarla. Pero reconsiderar no es obedecer
al último estímulo (hoy `Searching` reapunta cada frame con cualquier ruido: eso es no tener
elección). El criterio vive en un solo lugar, `NemesisBelief`, como un **foco**: lo que el Nemesis
está persiguiendo ahora (la creencia sobre el jugador, una pista, un vistazo, un escondite
sospechoso). Los estados siguen el foco; no deciden por su cuenta cuándo cambiar.

- **Cuándo reconsidera:** sólo cuando llega evidencia nueva (número de secuencia), nunca por tener
  un sensor prendido.
- **Qué vale cada candidato:** el tipo y, si es un señuelo o un escondite, cuál es (§17.5, §17.6);
  la confianza (un radio chico vale más), la frescura y lo que cuesta llegar (distancia por NavMesh).
  El detalle, pregunta por pregunta, está en el §17.4.
- **Cuándo cambia:** si el candidato nuevo vale más que el foco actual **por un margen**
  (histéresis). El foco recién elegido tiene una ventaja de compromiso que decae con el tiempo: lo
  que acaba de elegir no lo abandona por algo apenas mejor; lo que eligió hace rato, sí. Un tipo
  superior gana siempre y al instante (ver al jugador no espera a nadie, y ya es un peldaño
  *interrupt*).
- **Mismo lugar no es cambio:** un candidato del mismo tipo a pocos metros del foco lo actualiza
  sin reiniciar lo que estaba haciendo (no borra el barrido ni corta la pausa de mirar).
- **Anti-titubeo:** un tiempo mínimo entre cambios del mismo tipo, para que dos ruidos alternados no
  lo hagan ir y venir.
- **Se ve el cambio:** al cambiar de foco gira la cabeza hacia lo nuevo y hace una pausa corta antes
  de caminar. El jugador lee "cambió de idea", no "se trabó".
- Reemplaza los umbrales sueltos que hoy hacen esto en cada estado: `InvestigationRetargetInterval` /
  `InvestigationRetargetDistance` de `Investigating`, el `ShouldNoiseRetarget` de `Searching` (con
  `SightCommitTime`) y el reapuntado por `HasAudioTarget`.

**Legibilidad.** Radio y confianza se pueden mostrar sin UI: girar la cabeza hacia un ruido antes de
caminar (`NemesisLookAround`), barrer un área del tamaño del radio, y el aviso de la Fase 2 cuando
**sabe** como el caso de confianza máxima.

### 17.4 El script de la elección: las preguntas

Un componente propio, separado del que sabe. `NemesisBelief` **sabe** (evidencia, creencia, pistas,
vistazos); `NemesisChoice` **elige** a qué prestarle atención. La elección en sí es una clase C#
pura, `FocusArbiter`, sin MonoBehaviour ni escena: recibe el foco actual, el candidato nuevo y el
contexto, y devuelve un veredicto con **la pregunta que lo decidió**. Eso la hace testeable en
EditMode y legible en F9 ("cambió: paso del jugador 0.8 > cadenas 0.3", "no cambió: mismo lugar").

No decide estados: la escalera sigue siendo la única voz que escribe `NextState`. Decide *qué*
persigue el estado en el que está, y la escalera puede leer el foco por predicados (por ejemplo,
"el foco es una pista").

Las preguntas, en orden. La primera que contesta, decide:

| # | Pregunta | Qué hace según la respuesta |
|---|---|---|
| 1 | ¿Hay algo nuevo? (evidencia con número de secuencia nuevo) | No: **mantiene** el foco. |
| 2 | ¿Es ver al jugador? | Sí: **cambia ya.** Nada lo compite. |
| 3 | ¿Estoy a mitad de algo que no se corta? (rompiendo la radio, abriendo un escondite, sacándote de uno) | Sí: **mantiene**, salvo la pregunta 2. |
| 4 | ¿Es lo mismo que ya persigo? (el mismo señuelo, el mismo escondite, o el mismo tipo a menos de 3 m) | Sí: **actualiza** el foco sin reiniciar nada: no borra el barrido ni corta la pausa de mirar. |
| 5 | ¿Está descartado? (radio rota, escondite revisado vacío hace poco, un señuelo que ya revisó y sigue sonando igual) | Sí: **ignora.** |
| 6 | ¿Lo puedo alcanzar? (ruta completa; otro piso cuesta el montacargas) | No: vale menos. No se descarta: sigue siendo información. |
| 7 | ¿Cuánto vale lo nuevo? | Tipo × confianza × frescura × costo de llegar × habituación (§17.5), más la compatibilidad con la creencia. |
| 8 | ¿Cuánto vale lo actual, hoy? | Lo mismo, más la ventaja de compromiso (decae en 3 s) y "casi llego" (a menos de 4 m). |
| 9 | ¿Lo nuevo supera a lo actual por el margen? | No: **mantiene.** |
| 10 | ¿Cambié hace poco a algo del mismo tipo? | Sí: **mantiene** (anti-titubeo). |
| 11 | — | **Cambia:** gira hacia lo nuevo, pausa corta, y registra el porqué. |

Las preguntas de señuelos (§17.5) y de escondites (§17.6) entran en la 3, 4, 5 y 7; no son una
lista aparte.

### 17.5 Los señuelos en la elección

Un señuelo es una pista (D18: no renueva la creencia sobre el jugador), pero **no es la última
prioridad**. Es la herramienta del jugador para mover al Nemesis, y tiene que funcionar cuando el
Nemesis no lo está sintiendo. Cada uno pesa distinto:

| Señuelo | Valor base | Qué cambia en la elección |
|---|---|---|
| `Decoy_FireAlarm` | 0.7 | Se oye desde cualquier punto del nivel, así que el costo de llegar pesa mucho: una alarma en la otra punta no le gana a una creencia fresca cerca. Mientras suena, repetirse no es información nueva (pregunta 4). |
| `Decoy_Radio` | 0.6 | Si la elige, se compromete hasta romperla (pregunta 3): `NemesisDecoyBreaker` pasa a leer el foco en vez de `LastHeardDecoy`. Rota, vale 0 (pregunta 5). |
| `Decoy_Chains` | 0.45 | Usos infinitos: la habituación la gasta (×0.6 cada vez que lo hizo ir sin encontrar nada). A la tercera, ya no cruza el nivel por ellas. |
| Ruido sin identificar (pulso del Director, otros) | 0.4 | Igual que hoy: empuja a investigar. |

Contra la creencia sobre el jugador, lo que decide es **cuánto vale esa creencia ahora**:

- **Te está viendo:** el señuelo no lo saca. Ver es *interrupt*.
- **Te perdió hace poco y la creencia es precisa** (radio chico): la creencia vale más y el señuelo
  espera.
- **La creencia es vieja o imprecisa:** el señuelo fresco gana. Es exactamente para lo que el
  jugador lo usa: esconderse, esperar y mandarlo a otro lado.
- **El señuelo suena dentro de la zona donde cree que estás** (radio de la creencia + 3 m): no
  compite, **suma**. Va y barre alrededor, porque ir ahí sirve para las dos cosas.

La habituación es por sesión y sobrevive a la captura (R5). Mientras no exista la Fase 3 vive en
`NemesisChoice`; después pasa a `PlayerHabitTracker`.

### 17.6 Los escondites en la elección

**Playtest del 27/09:** "casi siempre me vio y me agarró aunque estuviera aguantando la
respiración". El código lo confirma. Un escondite sólo protege si el Nemesis no se acerca a su
puerta, y todo lo que pasa mientras estás escondido lo manda justo a la puerta:

1. **Los pasos te delatan hasta la puerta.** Caminando se te oye a 10 m y corriendo a 15 m: el
   último ruido antes de esconderte está en la puerta del escondite.
2. **Investigar un ruido es caminar hasta el punto exacto.** `Investigating` y `Searching` van a la
   posición del oído (`NemesisInvestigatingState.cs:190-217`), que es el escondite.
3. **En la puerta, la proximidad es instantánea:** a 1.5 m, a través de la carcasa y desde
   cualquier lado (D14), queda conocido en el acto (`FieldOfView.cs:368-408`).
4. **Aguantar la respiración no cambia nada de eso.** Ningún script del Nemesis lee
   `IsHoldingBreath`: aguantar sólo corta los pulsos de respiración, y ni la proximidad ni la vista
   por las rendijas se enteran.
5. **Y el aire no alcanza.** `maxHoldSeconds` son 6 s, la búsqueda dura 15 s, y al soltar (o al
   quedarte sin aire) viene una exhalación que se oye a ~6 m (`exhaleNoiseRadius` 2.5): tres veces
   más que la respiración normal (~2 m).

Qué tiene que cambiar (decidido el 27/09):

- **Aguantar te saca del agarre instantáneo (D21).** Respirando, la proximidad a través de la
  carcasa sigue como hoy: instantánea. Aguantando, no te detecta por proximidad, y las rendijas no
  acumulan en un escondite que no sospecha. Es el clásico: llega a donde oyó algo, mira a la
  izquierda y a la derecha (`NemesisLookAround`) y se va porque no te vio.
- **Si aguantás de más, vuelve.** Al quedarte sin aire (o al soltar) viene la exhalación, que lo trae
  de nuevo. Y ese segundo ruido desde el mismo escondite ya es sospecha.
- **Sospechar te vuelve presa.** Un escondite sospechoso o conocido **se abre**: va al
  `ApproachPoint`, lo abre y mira, y si estás adentro te saca, aguantes o no. La revisión deja de
  depender de la proximidad (el §3.5 decía que sí: con el aliento frenando la proximidad, un
  escondite revisado quedaría "vacío" con el jugador adentro).
- **Un ruido desde un escondite marca la zona, no la puerta (D22).** El oído lo registra con un
  radio grande (×2). El primero manda a investigar el área; el segundo desde el mismo escondite lo
  vuelve sospechoso.
- **Aire para una revisión de zona.** `maxHoldSeconds` de 6 a 8 s y `exhaleNoiseRadius` de 2.5 a 2.0
  (§12): aguantar tiene que alcanzar para que llegue, mire y se vaya; exhalar sigue siendo el costo.
- **Memoria de escondites usados (D23, Fase 2D).** Cada escondite lleva un medidor de uso, sea
  mesa, locker o container:
  - Sube cada vez que el jugador se esconde ahí, y más si el Nemesis estaba cazando cerca (R1).
    Baja lento (R7) y sobrevive a la captura (R5).
  - Mientras **investiga o busca**, los escondites usados que caen **dentro de la zona que está
    revisando** entran como candidatos: va a abrir alguno, con más chance cuanto más alto el
    medidor (pregunta 7 del §17.4). No cruza el nivel para revisar una mesa del otro lado: el
    conocimiento es de director, la ejecución es de agente (R4).
  - Con el medidor alto lo revisa primero (C2) y, al tope, lo rompe (§3.6, `BurnHidingSpot`).
  - La primera vez que abre un escondite por el medidor tiene que pasar donde el jugador lo pueda
    ver u oír (R3): así se aprende que no conviene repetir escondite.
- **Preguntas de la elección:** ¿es la primera vez que algo suena desde ese escondite, o la segunda?
  ¿hay en esta zona un escondite que el jugador ya usó, y cuánto? ¿lo revisé vacío hace poco? ¿lo
  estoy abriendo (no se corta)? ¿tengo evidencia para *saber* (vista, lo vio entrar) o sólo para
  sospechar?

Lo que no cambia: si te ve entrar, te saca (Nivel A, C3). El escondite protege al que cortó la
línea de vista, no al que se mete delante del monstruo.

### 17.7 Qué no cambia

- **Una sola voz decide.** `NemesisBelief` no escribe estados: es conocimiento, como
  `NemesisHidingAwareness`.
- **El agente no hace trampa.** Todo sale de evidencia que los sentidos ganaron. La plausibilidad
  usa la velocidad máxima del jugador (un dato de diseño), no su posición real.
- **Los sensores siguen siendo los mismos:** cambia qué se hace con lo que devuelven. No es el
  `NoiseBus` descartado en el §2.3: la esfera del emisor sigue siendo el ruido; lo nuevo es anotarle
  la procedencia.
- **`TryGetBelief` mantiene la firma** mientras se migra, para no romper a los llamadores de una vez.

### 17.8 Riesgos

- **Retuneo.** Los tiempos de la escalera (`BeliefAgeUnder`, gracia de persecución, presupuesto de
  búsqueda) se calibraron contra la creencia de hoy. Empezar con umbrales de confianza equivalentes y
  calibrar con los casos 22–27.
- **Sacar los parches antes de tiempo.** Cada parche del §16.4 existe por un bug de playtest. Se saca
  de a uno, con su caso de prueba.
- **Un Nemesis "más tonto".** Si las pistas no se atienden nunca, un señuelo deja de distraer. Tienen
  que seguir funcionando como distracción **cuando no te está sintiendo** (caso 25).
- **Un Nemesis indeciso o terco.** El margen y la ventaja de compromiso son los dos números que lo
  definen: con poco titubea, con mucho no reacciona. Se calibran con los casos 28 y 29.
- **Señuelos demasiado fuertes o demasiado débiles.** Si un señuelo le gana a una creencia fresca,
  el jugador lo usa en plena persecución; si nunca le gana, nadie lo usa. Se calibra con los casos
  30–33.
- **Escondites demasiado seguros.** Con la proximidad frenada por el aliento, un jugador que aguanta
  bien puede sentir que el escondite es inmune. Lo acotan el tope de aire, la exhalación y las
  contra-jugadas de la Fase 6 (`CheckHidingSpots`, `BurnHidingSpot`).

---

## 18. Búsqueda: dónde y cuánto, y el Director

> Agregado el 27/09/2026, después de jugar la 2C. Dos quejas del playtest:
>
> - *"Cuando te vas del rango de visión no va a la última posición que vio sino a una posición como
>   de nodo cercano; a veces sí, a veces no."*
> - *"La forma en que decide dejar de buscar no tiene ningún bias ni se modifica."*
>
> Y un pedido: que el Director haga más cosas solo.
>
> Fuentes:
> - El código, con archivo y línea.
> - Un cruce del resto del plan contra la 2B.
> - Una verificación de cada afirmación contra el working tree.
> - Una medición del NavMesh de las dos escenas hecha en Unity (batchmode, sin Play).
>
> No se reprodujo en Play.

### 18.1 Qué pasa hoy al perderte

La persecución hace lo que dice el §16.4: sin vista, con un avistamiento de menos de 10 s y la ruta
completa, corre por NavMesh al último punto **visto** (`NemesisPursuit.cs:145-154`). El problema
empieza al llegar.

1. **Llega enseguida.** Venía pegado al jugador, así que el punto visto queda a pocos metros.
   `HasArrived` corta el peldaño `"va a donde lo vio por última vez"`, y entra `Searching` por
   `"venía persiguiendo y todavía cree algo"`. Ese peldaño también se corta con `BeliefAge` ≥ 10 s o
   con `IsBeliefUnreachable`.
2. **`Searching` tiene cuatro salidas, en este orden** (`NemesisSearchingState.cs:409-445`). Un
   escondite conocido o sospechado pisa todas.
   1. **El barrido de habitación** (`NemesisFreeRoam`, 8 m). Sólo si la creencia viene de la vista,
      tiene menos de 6 s y está a menos de 12 m por camino.
   2. **La intercepción** (`TryGetInterceptPoint`, `:602`): el waypoint "adelante" de tu rumbo al que
      él llega antes que vos. Casi nunca gana la carrera: exige llegar en ≤ 1.25 veces tu tiempo, y
      con 2.75 contra 4.5 m/s, parado donde te perdió, no llega.
   3. **Si oye algo** (`HasAudioTarget`), el ruido más fuerte, adelantado con tu velocidad
      (`:437-442`). No es un waypoint, pero puede ser un señuelo o un pulso del Director.
   4. **`PickSearchTarget`** (`:736`): una ruleta **sólo de waypoints** (ancla ×4, predicción ×2.5,
      dividido por el tiempo de llegada), que empuja a nodos cerca de la creencia.
3. **Hasta el barrido empieza por waypoints.** `NemesisFreeRoam` arma los candidatos con los waypoints
   de su isla que caen dentro del disco, en el orden en que aparecen (`NemesisFreeRoam.cs:340-376`).
   Sólo completa con puntos sorteados del NavMesh si le faltan: con 8 o más waypoints adentro, no
   sortea ninguno. Además, lo ya barrido sólo pesa ×0.33, así que el barrido casi nunca se agota.
4. **Reapunta cada frame mientras oye algo** (`:175`; el punto 4 del §17.2). `HasAudioTarget` es
   cualquier ruido, pistas incluidas, y queda prendido entre barridos. Sin barrido comprometido, o
   pasados 6 s, cualquier ruido lo reapunta; antes de eso, sólo un ruido dentro del área (medido con el
   ruido más fuerte, no con la creencia). Cada vez:
   - **Si la creencia cae dentro del área, re-compromete el barrido:** borra lo barrido y sortea otro
     punto (`Commit`, `NemesisFreeRoam.cs:155-163`).
   - **Si cae afuera** (o ya no es de vista, o tiene 6 s o más, o el camino supera 12 m), suelta el
     barrido y pasa por las salidas 2 a 4. La intercepción cuesta hasta 16 `CalculatePath` por frame,
     contra lo que dice su propio comentario.
   - **En los dos casos cancela la pausa de mirar.**
5. **La pausa de mirar se arma al salir, no al llegar** (`:220`; verificado en código, no probado en
   Play).
   - El primer punto después del destino diferido no tiene pausa.
   - Mientras camina a los siguientes, `IsPausing` queda en verdadero: F9 dice "mirando alrededor" y
     `NemesisLookAround` barre la mirada hacia una dirección fija mientras corre.
6. **En la persecución hay otro camino a nodos, menos común.** Si el avistamiento tiene más de 10 s o
   la ruta a él está incompleta, apunta a la creencia predicha y, cada 0.66 s, `TryPickWaypoint`
   (`NemesisPursuit.cs:422`) puede elegir un waypoint que la vea (gizmo "flanqueo"). Mientras la
   persecución está estancada (Fase 4) es a propósito.

"A veces sí, a veces no" es la combinación de todo eso:
- si seguís haciendo ruido;
- si la creencia cae dentro o fuera del disco;
- si pasaron los 6 s;
- cuántos waypoints caen dentro del disco.

Hasta el 27/09 el plan no lo cubría. El §2.1 daba la intercepción y `PickSearchTarget` por buenos, y
la 2B hacía que los estados siguieran el foco, pero `Searching` iba a seguir eligiendo waypoints para
ir hacia él.

**Al terminar** (`ExitState` → `RequestNearbyPatrol`), la patrulla elige un cúmulo cercano con una
ruleta. La ruleta pesa la creencia (×5, escalada por frescura) y **la posición real del jugador** (×4,
vía `TryGetZoneAnchor`), y recorre 1 o 2 waypoints parando a mirar. No hereda lo que barrió, así que
puede volver a patrullar la misma sala. Visto desde afuera: termina de buscar y se queda rondando.

### 18.2 Cómo decide dejar de buscar

Con un reloj fijo. El peldaño `"le queda presupuesto de búsqueda"` es
`TimeInStateUnder(SearchTimeOut)`: 15 s desde que entró (`SO_NemesisPriorities.cs:255-259`).

- El reloj sólo se reinicia con una transición, y nadie escribe `SearchTimeOut` en runtime.
- Lo estiran los peldaños de escondites (`"sabe en qué escondite está"`, `"está revisando un
  escondite"`).
- Lo cortan antes la vista, la captura y el montacargas.

Fuera de eso, nada lo mueve:

- **Te oye en el segundo 14 y corta igual en el 15.** Después va a `Investigating` si te oye, si
  sospecha de un escondite, si te vio de reojo o si oye una pista; si no, a patrullar.
- **Sin ninguna evidencia desde que entró, espera igual los 15 s.**
- **No mira** qué tan buena era la última evidencia, cuánto del área barrió ni el ritmo del Director.

`Investigating` tiene el mismo problema del otro lado. Llega, mira 4 s (`InvestigationDwellTime`) y
vuelve a patrullar, aunque lo que oyó fueran tus pasos hace dos segundos (el punto 6 del §17.2: el
oído es binario). Sólo un ruido nuevo, a 3 m o más y 1.5 s después, lo vuelve a mandar.

### 18.3 El Director: qué hace de verdad

Relevado contra `NemesisDirector.cs`, `NemesisTension.cs`, `SO_DirectorPacing.asset` y las dos
escenas, y medido en Unity.

| Qué | Qué pasa de verdad |
|---|---|
| **Zona1** | El Nemesis tiene `wakeOnlyFromScript` en 1 (override de escena, §14.1): duerme todo el gameplay. `NemesisTension` ni siquiera arranca: `IsRunning` exige que haya despertado, y F9 dice "esperando que se despierte". En el escape se suspende ("cinemática", después "escape"). Los disparadores de `sp2` y `sp3` **aplican igual** su presión sobre el Nemesis dormido: pesos, clon de sentidos, ruido cada 9–12 s. No se ve porque dormido no hace nada. Sólo la entrada Mr. X sale (`NemesisDirector.cs:615`). **En el juego real el Director no se ve nunca** (D25). *Borde:* si el escape arranca antes de 60 s de `sp3`, el Nemesis despierta con los sentidos del Director puestos: una presión de puzzle no se limpia al suspenderse (`:843-849`). |
| **Testbed** | Sin disparadores (`puzzleTriggers: []`). Lo único automático es el ritmo. |
| **BuildUp** (casi todo el tiempo) | No hace nada hasta `quietTimeout` (90 s) de silencio. Y "silencio" es que no haya **ningún** estímulo. La proximidad cuenta como contacto desde que el Nemesis está a menos de 12 m por NavMesh (`proximityRadius`), aunque no te sienta. También cuentan "el jugador lo ve" (a 12 m, con línea de vista) y "escondido con búsqueda cerca". En el Hub el silencio se pausa. **Medido en Unity (27/09):** en la testbed el 93.6 % del NavMesh queda a menos de 12 m por camino de alguna ruta (77 % a menos de 6 m). **Corregido:** eso es cobertura en el espacio, no en el tiempo. Simulando la patrulla (cúmulos, replan cada 12 s, recencia; sin detección, así que es una cota superior del silencio), el Nemesis está a menos de 12 m de un jugador quieto el 14–19 % del tiempo. La sensibilidad creciente **sí arranca**: en el 64–85 % de las corridas de 10 min (mediana ~3.5 min), y deambulando en el 40 %. Lo que la frena en un playtest son los encuentros: después de un pico vienen `SustainPeak` y el Relax (30–45 s), y al volver a `BuildUp` el silencio empieza de cero. Entre un encuentro y el disparo pasan al menos 2 minutos. |
| **Pico → Relax** | Es lo único que sí pasa solo. Una captura pone el medidor en 1; una persecución llega a 0.85 en 5 a 11 s, según lo cerca que esté y si lo ves. Después vienen `SustainPeak` (3–5 s), `PeakFade` hasta que termina el encuentro, y `Relax` con la retirada: ancla y pesos de ruta hacia la zona más lejana (0.8), durante 30–45 s. `IsEncounterOver` acepta `Searching` con el avistamiento de 6 s o más, e `Investigating` siempre: el Relax puede arrancar a mitad de una búsqueda. |
| **Qué mueve una presión** | **Ancla y pesos:** sólo la patrulla, y como sesgo (se nota en uno o dos ciclos de 12 s). **Ruido** (cada 9–12 s): es una pista, manda a `Investigating` y, en `Searching`, dispara el reapuntado. **Sentidos** (×1.075 a ×1.25 según la intensidad): valen en todos los estados. Ruido y sentidos sólo aparecen en presión de puzzle o de sensibilidad; la retirada es sólo ancla y pesos. **Nada decide cuánto dura la búsqueda**, y como la retirada mueve sólo la patrulla, se nota recién cuando la búsqueda termina. |
| **Pesos de ruta con cúmulos** | **Verificado en código (27/09):** la palanca de pesos no hace nada con la patrulla por cúmulos. <br>• El peso de cada cúmulo se calcula al armar el grafo (`NemesisRouteGraph.cs:626-638`). <br>• La huella que decide si se rearma no incluye los pesos (`:322-375`). <br>• `InvalidateRouteGraph` (`NemesisController.cs:859`) no tiene llamadores. <br>• `PickCluster` lee el peso congelado (`NemesisClusterPatrol.cs:560`). <br>La retirada y la sensibilidad mueven la patrulla sólo con el ancla. **✅ Arreglado el 27/09 (2B parte 3):** `NemesisRouteGraph.ClusterWeight` promedia el peso vivo de las rutas del cúmulo, y `PickCluster` lo lee. Además, el grafo rearmado reinicia la patrulla por cúmulos (`BuildVersion`). |
| **Sin presión** | `NemesisController.TryGetZoneAnchor` usa la posición **real** del jugador como ancla de la tirada de cúmulos (`zoneBiasUsesRealPlayer` = 1), salvo con el jugador en el Hub. Es el acecho más constante del juego, pero no es el Director ni aparece en su fila de F9. |
| **Rutas de Zona1** | **Medido:** 6 de los 29 tramos entre waypoints consecutivos de la planta baja no tienen camino completo: `ROUTE 2 1F` 4 de 8 y `ROUTE 1 1F` 2 de 9. Todos cortan en la misma línea (z ≈ 7.5–8.5, entre x −25 y −10). Los waypoints de la franja sur (z ≈ 5.8, junto al montacargas) están sobre el NavMesh, pero en una isla sin camino desde el norte. *Verificar en el editor* si es una puerta horneada cerrada o un corte del horneado. En el piso de arriba todos los tramos tienen camino, pero en `ROUTE 2 2F` uno de 16 m en línea recta cuesta 128 m por NavMesh (`Waypoint (22)` → `(15)`). Hoy no se nota porque el Nemesis duerme (D25). |
| **Qué sabe del Nemesis** | Proximidad, persecución, estado, el último avistamiento, "el jugador lo ve" y "escondido con búsqueda cerca". Con eso sube el medidor, frena el decaimiento y decide el paso de `PeakFade` a `Relax`. No sabe si la búsqueda terminó vacía ni dónde fue. `IsEncounterOver` lee `FieldOfView` por atrás (`NemesisTension.cs:212-230`), no la creencia. |

Por eso se siente que no hace nada:
- En Zona1 está apagado de hecho.
- En la testbed, la sensibilidad creciente tarda: al menos 2 minutos después de cada encuentro.
- Lo que sí hace mueve sobre todo la patrulla, que no es lo que el jugador está mirando mientras lo
  buscan. Encima, con cúmulos, sólo la mueve el ancla: los pesos no llegan.

### 18.4 Con qué se cruza: señuelos, escondites y el jugador

Del cruce del resto del plan contra la 2B. Esto es lo que cambia cómo se construye.

- **`Sequence` sube unas 10 veces por segundo, no "con evidencia nueva".** `NemesisBelief.Set` lo
  incrementa en cada avistamiento o ruido plegado (`NemesisBelief.cs:288-296`). El oído sella cada
  barrido (`FieldOfListening.cs:282`), y la proximidad graba un avistamiento por frame
  (`FieldOfView.cs:404-405`). Reapuntar cuando cambia `Sequence` sigue siendo reapuntar casi cada
  frame: hace falta la pregunta 4 del §17.4 ("mismo lugar = actualizar sin reiniciar") **junto con**
  el barrido nuevo, no después.
- **El radio de la creencia no sirve para barrer.** Crece a 4.5 m/s hasta 100 m
  (`NemesisBelief.cs:54, 285-286`; `beliefGrowthSpeed` no está en el asset, así que corre el
  default). Llega a 8 m ~1.7 s después de la evidencia, antes de que entre `Searching`. Sirve para la
  plausibilidad, no para barrer.
- **D22 no está construido.** `HeardNoise` no sabe si salió de un escondite. Con una creencia de oído
  dentro de uno, un barrido centrado en la creencia colapsa en la puerta: es el bug del §16.4 (casos
  27 y 36). El radio ×2 del D22 entra **con** el barrido.
- **Los señuelos todavía no se pueden probar.**
  - `Decoy_Radio`, `Decoy_Chains` y `Decoy_FireAlarm` no están en ninguna escena ni prefab.
  - `NemesisDecoyBreaker` tampoco, y no se agrega solo. Con `maxPlayTime` 0, la radio sonaría para
    siempre.
  - La alarma tiene un `armDelay` de 10 s que el plan no nombraba.
- **Una sola pista a la vez.** `FieldOfListening` pasa la más fuerte de cada barrido (`:381-387`) y
  `NemesisBelief` guarda una (`:300-315`). Con dos señuelos sonando sólo existe uno, y la alarma
  (margen 0, `:328`) pierde contra cualquiera con alcance. El caso 29 todavía no se puede representar.
- **Una pista todavía mueve a `Investigating`.** `NemesisInvestigatingState.cs:205-226` va a
  `ears.LastKnownPosition`: el ruido más fuerte, pista incluida. Con la alarma, llega, mira 4 s y se
  queda parado ahí los 30 s *(inferido)*.
- **Dónde se prueba.** Los escondites están sólo en `TestIñaki.unity`, y en Zona1 el Nemesis duerme:
  la 2B se prueba en la testbed y en TestIñaki.
- **`SearchTimeOut` está atado a cuatro cosas:**
  - el peldaño;
  - la memoria de un escondite conocido (`NemesisHidingAwareness.cs:274-281`);
  - el tooltip de la cola de la música (`searchTailTimeout` 25 s, "más largo que `SearchTimeOut`",
    `NemesisChaseMusic.cs:52-56`);
  - la premisa del C1.
- **El enfriamiento funciona con la 2C.** Aguantando no hay proximidad, ni rendijas, ni pulsos, así
  que no hay evidencia y la búsqueda se enfría (D21). La exhalación sí es evidencia: renueva.
- **Los estados todavía leen sensores crudos** (regla del §10). En `Searching`:
  - `HasAudioTarget`: líneas 175 y 438.
  - `FieldOfListening.LastKnownPosition` (el ruido más fuerte, pista incluida): 392 y 440.
  - `FieldOfView`: 495-501.
  - `LastKnownVelocity`: 565, 698 y 913.
  - La vista, a través de `NemesisPursuit.TryGetRecentSighting`: 233-236.

  Fuera de `Searching`: `Investigating` (205-226) y `Traversing` (78). Para que puedan dejar de
  hacerlo, la creencia tiene que exponer lo que hoy buscan por atrás: el punto visto, la velocidad
  observada y el radio de la última evidencia (`radiusAtStamp`, hoy privado).
- **`Searching` puede entrar sin creencia.** `NemesisCatchState.cs:70` entra directo, y el peldaño
  `"sabe en qué escondite está"` no pide creencia. El barrido nuevo necesita un plan B para ese caso.
- **El orden dentro del frame** (`NemesisStateManager.cs:1264-1302`): sensores → `belief.Tick` →
  rastro → proximidad → estancamiento → escondites → escalera. Después corre la transición o el
  `UpdateState`, nunca los dos. El árbitro y el enfriamiento leen la creencia ya plegada de ese frame.
- **Tests.** `WIRED.Tests.EditMode.asmdef` referencia sólo `WIRED.Utils`, y un asmdef no puede ver
  Assembly-CSharp. El árbitro y la regla de enfriamiento tienen que vivir en un asmdef de lógica pura
  que los tests referencien (`WIRED.Nemesis.Logic`, que arma la Fase 3), con datos planos: sin
  `HidingSpot` ni `DecoyNoiseSource`.
- **D20 no coincidía con el código, y ganó el código** (decidido el 27/09). El plan decía que una
  evidencia del jugador fuera de lo alcanzable es una pista; el código **reemplaza** la creencia
  (`NemesisBelief.cs:240-244`). Con la procedencia sacada del emisor, un ruido del emisor del jugador
  *es* el jugador, y fuera de lo alcanzable sólo cae después de un respawn o un warp.

### 18.5 Modelo propuesto

**A. Dónde busca: alrededor de la creencia, por NavMesh.**

- **Centro:** `NemesisBelief.Position`, ajustado al NavMesh. La fusión promedia posiciones, y el
  promedio puede caer fuera del piso.
- **Radio:** el de la **última evidencia**, no el de la creencia ahora:
  `clamp(radio de la evidencia + 1 m, 3, RoomSweepRadius)`. Se congela al elegir. Queda apretado sobre
  un avistamiento y más abierto sobre un ruido a través de una pared. Con el D22, un ruido que sale de
  un escondite viene con radio ×2: barre la zona, no la puerta.
- **Candidatos:** puntos sorteados del NavMesh en el piso de la creencia (el filtro `|Δy|` del §16.2).
  Un waypoint dentro del disco es un candidato más, no el primero de la lista (hay que tocar
  `NemesisFreeRoam.AddWaypointsInArea`). `IsStandingWhereLost` y la pausa de mirar al entrar se quedan.
- **Mismo lugar = actualizar.**
  - Evidencia nueva **dentro** del disco corre el centro sin borrar lo barrido ni cortar la pausa
    (pregunta 4 del §17.4).
  - **Fuera** del disco, re-centra y congela un radio nuevo, **conservando** lo barrido: son
    posiciones y siguen valiendo.
  - Un sensor prendido nunca reapunta: salen `ShouldNoiseRetarget` y el fallback al oído (`:437-441`).
  - Una pista no mueve el barrido: compite en la elección (§17.5).
- **La pausa, al llegar.** Se arma al llegar a cada punto, incluido el primero. `IsPausing` pasa a
  ser "llegó y está mirando".
- **Sin intercepción.** `TryGetInterceptPoint` y `PickSearchTarget` salen del flujo normal (D24). Sin
  creencia (entrada desde `Catch`, o sólo con el escondite conocido), barre alrededor de donde está.
- **"Revisé todo":** si no le quedan candidatos sin barrer en el disco, termina antes del tope. Tiene
  que distinguir "todo barrido" de "nada alcanzable": lo segundo no cuenta como haber revisado.
- **Cambios chicos en `NemesisFreeRoam`:**
  - `Recenter(centro, radio, room)` sin vaciar lo barrido.
  - Marcar el punto como barrido al llegar, no al elegirlo.
  - Distinguir agotado de inalcanzable.
  - Que `PreferEnteredRoom` suelte una habitación agotada.

**B. Cuánto busca: se enfría, no se vence.**

Un solo número, el **silencio**: los segundos desde la última evidencia del jugador (`BeliefAge`, que
desde la parte 1 no cuenta pistas). Sigue buscando mientras:

- lleva menos de `searchMinTime` en el estado (6 s: siempre mira un poco), **o**
- se cumplen las tres a la vez:
  - el silencio es menor que `searchQuietWindow` × calidad;
  - lleva menos de `searchHardCap`;
  - no barrió todo el área.

| Factor | Valor | Por qué |
|---|---|---|
| Calidad de la última evidencia | Vista ×1.25 · ruido tuyo ×1 · a través de pared o piso ×0.75 | Te vio: insiste. Te oyó a través de una pared: no sabe tanto. |
| Persistencia del Director (C) | Multiplica la ventana y el tope | El ritmo decide cuánto insiste, sin tocar el FSM. |

- **Cada paso o exhalación que oye renueva** (el silencio vuelve a 0) y, si cae fuera del área,
  re-centra (A). Es la 2C cerrada: aguantás, no hay evidencia, se enfría y se va. Exhalás y vuelve.
- **La evidencia desde el Hub no renueva** (C5): tus pasos oídos por la puerta del Hub no lo dejan
  acampando afuera hasta el tope.
- **El tiempo cerca de tu escondite tiene que caber en el aire.** Un escondite que no sospecha no se
  abre; lo que importa es cuánto se queda a menos de ~2.3 m de él, donde se oye la respiración. El
  barrido no repite puntos al lado de la puerta, y `searchMinTime` (6 s) queda por debajo de
  `maxHoldSeconds` (8 s).
- **Un escondite sospechoso o conocido sigue teniendo su peldaño** (`"está revisando un escondite"`),
  arriba de este: el enfriamiento no lo corta.
- **`Investigating` también se enfría, y puede escalar** (D26). Si lo que investigó fue un ruido
  **tuyo** y llega sin encontrarte con el silencio todavía corto, pasa a una búsqueda corta alrededor
  de la creencia (tope ×0.5) en vez de patrullar. Una pista no escala: se mira y se sigue.
- **El peldaño.** `"le queda presupuesto de búsqueda"` pasa a leer un predicado nuevo, `IsSearchWarm`
  (al final del enum, el 20), en el asset **y** en `BuildDefaultLadder()`. `SearchTimeOut` se queda
  como campo: lo sigue leyendo la memoria del escondite conocido.

**C. El Director mete la mano, sin tocar el FSM.**

Todo son números y presión, como hoy. El Director no escribe `NextState` ni elige destinos (§10). El
conocimiento es del Director; la ejecución, del agente (R4).

1. **Palanca 5: persistencia.** Es un préstamo sobre `SO_NemesisData`, igual que el boost de sentidos.
   Construido: `RefreshLoan` / `ReturnLoan` reemplazan a `ApplySensoryBoost` / `RemoveSensoryBoost`.
   - El Director escala `searchQuietWindow` y `searchHardCap` en su copia, y el predicado lee su SO de
     siempre.
   - Se recalcula al cambiar el ritmo, a partir de `BaselineData` leído fresco (§10, la trampa del §7).
   - Se compone con el de sentidos en **una sola** copia prestada.

   | Ritmo | Persistencia | Qué se ve |
   |---|---|---|
   | `BuildUp` | 1.0, hasta 1.5 con la sensibilidad creciente | Si hace rato que no pasa nada, te busca más (Mr. X, C1). |
   | `SustainPeak` | 1.0 | — |
   | `PeakFade` | 0.75 | Ayuda a que el encuentro termine solo, que es lo que espera `PeakFade`. |
   | `Relax` | 0.5 | Corta antes y **se va**: la retirada deja de ser invisible. |

   Con evidencia fresca no cambia nada: sólo acorta o estira el silencio tolerado, nunca hace que
   ignore lo que siente. Sin Director (o en Zona1) no hay préstamo, y la persistencia vale 1.
2. **Vuelve a pasar.**
   - Usa `NemesisEvents.OnSearchEnded(Vector3 area, bool found)`, que crea la Fase 3 para
     `EscapedWhileHidden`: `area` es la creencia al salir de `Searching` y `found` indica que sale a
     `Chasing` o `Catch`. Si pasa por `Traversing`, la búsqueda sigue abierta.
   - Con una búsqueda vacía en `BuildUp`, el Director programa, 20–40 s después, presión de ancla y
     pesos (0.5, 30 s) sobre la zona de esa búsqueda. En `Relax`, no.
   - Es la versión sin hábitos del C1 ("nunca vuelve a propósito"). La Fase 6 la vuelve emboscada
     (`ExitAmbush`).
3. **El silencio se mide por encuentros, no por metros.**
   - `QuietTime` se reinicia con persecución, búsqueda o investigación de evidencia del jugador, "el
     jugador lo ve" y captura. La proximidad sólo cuenta por encima de 0.75 (~3 m).
   - Con la proximidad a menos de 12 m como contacto, el silencio de 90 s llega, pero tarde y poco
     (§18.3). En la simulación, contar la proximidad sólo por encima de 0.5 (~6 m) ya lo lleva del
     40–85 % al 73–95 % de las corridas; 0.75 es más estricto todavía.
   - Pasar cerca sin sentirte ni ser visto deja de ser contacto, aunque la proximidad sigue sumando al
     medidor.
   - `IsEncounterOver` pasa a leer la creencia.
4. **Una presión de puzzle se limpia al suspenderse**, o al menos sus sentidos. Así el Nemesis no
   despierta en el escape con el boost puesto.
5. **Los pesos de ruta vuelven a llegar a la patrulla por cúmulos** (§18.3). Que el grafo recalcule
   el peso de cada cúmulo cuando el Director cambia un multiplicador, o que la tirada lea el peso vivo
   de sus rutas, sin rearmar el grafo entero.
6. **Se ve en F9.**
   - La fila `presión` muestra también el acecho de la patrulla (`zoneBiasUsesRealPlayer`) cuando no
     hay presión del Director, y la persistencia vigente.
   - La fila `búsqueda` muestra centro, radio, silencio / ventana, tope, persistencia y puntos barridos.
7. **Zona1 es una decisión de diseño, no de código (D25).**

### 18.6 Orden dentro de la 2B

Reordenada el 27/09 por dependencias (§18.4). El barrido nuevo no se puede hacer sin la pregunta 4 ni
sin el D22. El enfriamiento no depende del foco, porque `BeliefAge` ya no cuenta pistas.

| Parte | Qué | Casos |
|---|---|---|
| 1 ✅ | `NemesisBelief`, pistas separadas, `HearsPlayer` / `HearsLead` / `HasFreshLead` | — |
| 2 ✅ | **Dónde busca:** árbitro mínimo (preguntas 1 y 4), barrido alrededor de la creencia (A), radio de la evidencia congelado con tope, D22, la pausa al llegar; salen el reapuntado por `HasAudioTarget` y el ancla de vista forzada. Construida el 27/09, falta jugarla | 23, 27, 36, 41, 42 |
| 3 ✅ | **Cuánto busca y el Director:** `IsSearchWarm`, enfriamiento y escalada de `Investigating` (B); persistencia, "vuelve a pasar", silencio por encuentros, limpiar la presión al suspender, los pesos de ruta con cúmulos, F9 (C); desacoplar `SearchTimeOut`, subir la cola de la música, guarda del Hub. Construida el 27/09, falta jugarla | 2, 9, 25, 34, 43–48 |
| 4 ✅ | **La elección completa:** `NemesisChoice` con todas las preguntas, lista de pistas, valores y habituación de señuelos, breaker leyendo el foco **e instalado**, señuelos puestos en la testbed, vistazo → `Investigating`, sospecha compartida. Construida el 28/09, falta jugarla | 22, 24–26, 28–33, 37, 60 |
| 5 | **Limpieza y tests:** el parche de `NemesisPursuit` (después del de `Searching`, porque `IsStandingWhereLost` depende de él), el filtro de `Investigating`, gizmo, tests EditMode en `WIRED.Nemesis.Logic` | 27 y todos los anteriores |

### 18.7 Qué no cambia

- **Una sola voz decide.** `IsSearchWarm` es un predicado; la escalera sigue escribiendo `NextState`.
  El Director presta números y pone presión, como hoy.
- **La persecución** (§16.4: al punto visto, después buscar) no se toca. Cambia lo que pasa al llegar.
- **Los escondites:** `"sabe en qué escondite está"` y `"está revisando un escondite"` siguen arriba de
  la búsqueda. La 2D suma sus candidatos al disco del barrido (A).
- **El escape** (`ChaseFloor`) no pasa por nada de esto: sube `Searching` e `Investigating` a
  `Chasing` y el Director está suspendido.

### 18.8 Riesgos

- **Búsquedas eternas.** Un jugador que hace ruido sin que lo vea renueva sin fin: por eso el tope.
  Al tope, si te oye, la escalera lo manda a investigar como hoy.
- **La música.**
  - `searchTailTimeout` (25 s) cortaría la cola a mitad de una búsqueda de 30 s × 1.5: hay que subirlo
    a 50 s.
  - Hoy no pasa en ninguna escena: en Zona1 la búsqueda no corre y la testbed no tiene
    `NemesisChaseMusic`.
  - Va a pasar en cuanto el Nemesis vuelva al gameplay (D25).
- **Un Nemesis que abandona.** En `Relax` la ventana queda en ~4 s. Si se siente regalado, subir la
  persistencia a 0.75. Se calibra con los casos 46 y 47.
- **"Vuelve a pasar" como trampa.** Si vuelve siempre, se lee como que sabe dónde estás. Por eso va
  sólo en `BuildUp`, con demora sorteada, y como sesgo de patrulla, no como orden.
- **Dos préstamos del SO.** Sentidos y persistencia en copias separadas se pisarían. Va una sola
  copia, recalculada desde `BaselineData` cada vez que cambia cualquiera de los dos.
- **Persistencia y tensión se realimentan.** Buscar sube la tensión (`HiddenNearSearch`), y
  `IsEncounterOver` mira la búsqueda. Es una realimentación negativa: una búsqueda larga en `BuildUp`
  lleva al pico, el pico a `PeakFade` (0.75) y después a `Relax` (0.5), que la acorta. Es el arco
  buscado, pero hay que mirarlo en F9 para que no oscile.
- **`docs/CLAUDE.md` describe la búsqueda vieja** (sección *Nemesis: chase and search*). Se actualiza
  con la parte 2.
- **Sacar parches antes de tiempo** (§17.8). El ancla de vista forzada sin el D22 ni el tope del radio
  reabre el §16.4.

### 18.9 Playtest del 27/09 — ajustes

Iñaki jugó las partes 2 y 3 en el blockout de Zona1. Identifica mejor que antes, pero:

| Lo que se vio | Por qué pasaba | Qué se hizo |
|---|---|---|
| En la testbed y en `TestIñaki` el Nemesis quedaba dormido y no se podía activar. | Al aplicar al prefab los componentes nuevos (`NemesisBelief`, `NemesisHidingAwareness`) desde la instancia de Zona1, se aplicó también su override `wakeOnlyFromScript: 1`. | Sacado del prefab. Zona1 lo sigue teniendo como override propio. |
| **Con distancias grandes te pierde y no va al último punto.** | Tres cosas que se sumaban. (1) El barrido sorteaba puntos del disco con poco peso al centro y mucho a "llegar antes": se quedaba del lado cercano, a metros del punto, y más cuanto más ancho el disco (evidencia lejana). (2) `Investigating` sólo movía el destino con un ruido oído en ese barrido, y el intervalo de 1.5 s se tragaba los últimos pasos: caminaba a donde estabas un segundo y medio antes de salir de su alcance. (3) El silencio de la búsqueda contaba desde la evidencia, así que el camino hasta ella se comía la ventana: llegaba con la mitad gastada y se iba. | (1) El barrido **va primero al punto de la evidencia** (`NemesisFreeRoam.IsAnchorPending`) y después barre alrededor; también cuando te oye en un lugar del disco donde todavía no miró. No con un ruido desde un escondite (D22). (2) `Investigating` alcanza tu **último** ruido aunque ya no te oiga. (3) El silencio cuenta **desde que llega** al punto (`Silence`); mientras camina no corre. F9: "yendo al último punto" y "tibia (sin contar)". |
| La búsqueda duraba muy poco. | El punto (3) de arriba: en la práctica, la búsqueda después de perderte era el mínimo de 6 s. En `Relax` la persistencia la corta a la mitad (a propósito, §18.8). | Con el reloj desde la llegada, después de perderte busca ~10 s en el lugar (vista × 1.25), más cada renovación. La D26 pasa a `IsInvestigationWarm` (predicado 21): caminando hacia tu ruido, el silencio también cuenta desde la llegada, y un ruido lejano ya no llega vencido al final de la inspección. |
| Al pasar de un estado a otro se quedaba quieto, y parecía contradecirse. | `Searching` arrancaba con 1.2 s de mirar alrededor en el punto donde te perdió, aunque te estuviera oyendo alejarte. `Investigating` iba al ruido más fuerte del barrido, fuera tuyo o un pulso del Director; y entrando por un vistazo ("vio algo de reojo") iba al último ruido que había oído, por viejo que fuera, o a ningún lado. | Si te oyó más allá después de perderte, va directo ahí. `Investigating` va a lo que lo trajo, en el orden del ladder: el vistazo, tu ruido, una pista. Un señuelo le gana a tu ruido sólo si hace 1.5 s que no te oye. La competencia de verdad sigue siendo la parte 4. |
| **Sigue atravesando algunas puertas.** | (a) `NemesisDoorUser` barre con un *sphere cast*, que no ve la hoja si ya arranca adentro: una puerta al costado de un pasillo, donde el camino dobla en el marco, recién queda adelante con el cuerpo pegado a la hoja. (b) Salteaba las puertas en movimiento: cerrarle una puerta en la cara la dejaba pasar. (c) Una puerta que no puede abrir (sellada por el escape, o *Nemesis Can Open* apagado) no cortaba el NavMesh: `DoorMetalRed` y sus variantes, casi todas las de Zona1, tienen *Auto Carve Nav Mesh* apagado y ningún obstáculo. | (a) Además del barrido, mira la hoja que ya está tocando, si queda adelante (±60°) y es la hoja, no el marco. (b) Una puerta que se cierra en su cara la vuelve a abrir desde donde esté (`DoorInteractable.IsClosing`, `AnimateReopen`). (c) Mientras no la puede abrir, la puerta corta el NavMesh (`RefreshNemesisBlock`): toma su obstáculo o crea uno del tamaño de la hoja, y lo devuelve como estaba al dessellarse. |

**Visto en el HUD, sin tocar:** la fila `persecución` marcaba 5 `ChaseStalled` y la fila `hábitos` "estanca 5".
`NemesisChaseProgress` cuenta como estancada cualquier ventana de 4 s en la que no acortó 1.5 m, y el
jugador corriendo en línea recta (4.5 m/s contra 3) también la cumple. Eso infla el contador con el que
la Fase 3 desbloquea el flanqueo, y en persecución a la vista activa el desvío por waypoints. Es de la
Fase 3/4: a decidir si una ventana en la que la distancia **creció** es "se escapó" y no "loop".

Compila (Logic, Assembly-CSharp y tests). Falta jugarlo: casos 41, 42, 43, 45 y 46 del §13 más los de
esta tabla. En Zona1, la persecución del escape pasa por puertas selladas: ahora son paredes.

---

## 19. Rediagnóstico del 05/10/2026

Por qué existe: el plan de la búsqueda por mapa (`Plan-Busqueda-Nemesis.md`), el docx del 30/09 con
los cuatro arreglos del montacargas y las tres decisiones abiertas, y el material de
`Logs/fase3-descartada/` se perdieron. Lo que sigue se reconstruyó leyendo el código de `HEAD`
(`0b131bec`) y las trazas de `Logs/NemesisTrace/`, sin abrir Unity ni jugar. Es la base del plan de
[Pendiente](#pendiente).

Cómo leerlo:

- Las rutas son relativas a `Assets/_Project/Scripts/`, y los números de línea, los del 05/10.
- **Verificado** es "leído en el código"; **inferido** es razonado sin jugarlo.
- Las trazas son del 27 y 28/09: anteriores al arreglo del montacargas de esa noche, al cambio de
  la escalera del 03/10 y a la búsqueda nueva del 04/10. Muestran mecanismos, no el comportamiento
  de hoy.
- El asset de la escalera y `BuildDefaultLadder()` coinciden hoy: 22 peldaños.
- Sale de tres análisis de sólo lectura. Se revisaron a mano la causa de WIR-057, los peldaños
  "lo está viendo", "venía persiguiendo y todavía cree algo" y "venía hacia el montacargas y
  todavía cree algo", que ningún peldaño lee `VisionLossGracePeriod`, y `ShouldAbandonForPlayer`.
  El resto no se volvió a comprobar línea por línea.

### 19.1 Qué se perdió y qué quedó

| Material | Estado |
|---|---|
| `docs/Plan-Busqueda-Nemesis.md` (fases 1, 2a–2e y 3; casos 62–67; D37–D40) | Nunca se commiteó y no está en disco. Quedan las menciones de `CLAUDE.md`, los comentarios del código y `Handout-Busqueda-Nemesis.md`. |
| Docx del 30/09 (cuatro arreglos del loop del montacargas y tres decisiones) | No está. Reconstruido en el [§19.3](#193-pisos-y-montacargas). |
| `Logs/fase3-descartada/` (`titileo.patch`, dos patches de la fase de pisos y cinco archivos nuevos) | `Logs/` está fuera de git y la carpeta ya no existe. Reconstruido en el [§19.4](#194-titileo-entre-chasing-y-searching). |
| `Logs/nemesis-tanda1-reportes.json`, `nemesis-batch.log`, `nemesis-batch-tests.xml` | No están. |

Se buscó en el repo (todas las ramas y el stash), en Descargas, Escritorio, Documentos, los
scratchpads de otras sesiones y sus transcripciones.

Lo que el handout daba por hecho o en curso, y cómo está de verdad:

- **La tanda 2 no entró.** `NemesisSearchingState.ConsiderUsedSpots` sigue siendo el sorteo, con el
  comentario "Fase 2e replaces this roll". →
  [§19.5](#195-escondites-por-el-mapa-y-patrulla-después-de-la-caza)
- **La revisión y la documentación no se hicieron.** → [§19.6](#196-el-plan-contra-el-código)
- **`SO_NemesisData.asset` no se guardó.** Su último commit es del 22/09 (`3d262aef`): no tiene
  ninguno de los 12 campos de `145eaf5e` ni los cerca de 15 de `00d97c61`.
- **`Draw Possibility Map` sigue apagado** en `Assets/_Project/Prefabs/Nemesis.prefab`.
- **Coincide:** los seis archivos nuevos existen, los tres borrados están borrados, y hay 247
  atributos de test en 15 archivos.

### 19.2 Búsqueda

**WIR-057 — va derecho a tu escondite. Sigue reproducible (verificado).**

- Causa: el sorteo por hábito cuenta la estadía en curso.
  - `Nemesis/PlayerHabitTracker.cs:182-190` suma un uso al **entrar** al escondite.
  - `Nemesis/NemesisHidingAwareness.cs:260-285` sortea `OpenChance` (0,25 por punto,
    `SO_CounterplayRules.asset:39,45`) sobre los escondites usados cerca de la búsqueda.
  - En el primer escondite de la partida, el único usado es el ocupado: 25 % por tirada de que
    camine a la puerta y la abra. F9 lo muestra como "lo usaste antes (1.0)".
  - Se sortea en cada elección de la búsqueda (`NemesisFSM/NemesisSearchingState.cs:224-231, 598,
    620, 631`, radio 8 m) y al llegar en `Investigating` (`NemesisInvestigatingState.cs:288`, 6 m).
    Cada entrada de estado limpia el conjunto de ya sorteados (`:448`, `:207`), así que reentrar
    vuelve a sortear.
- Arreglo (S): sumar el uso al salir, o excluir la estadía en curso de `OpenChance`.
- Tests: `HabitLedgerTests.cs:234` cubre sólo la fórmula. Agregar "una estadía en curso no sube
  `OpenChance`".
- Choque con el plan: R4 ("no el escondite donde estás") contra la Fase 2D, "+1 al entrar" (D23).
  El arreglo cumple R4.

Lecturas de la posición real del jugador mientras no lo ve:

| Dónde | Qué lee | Qué es |
|---|---|---|
| `PlayerHabitTracker.cs:182-190` + `NemesisHidingAwareness.cs:269, 290-302` | La estadía en curso en el medidor; la posición real habilita la primera apertura por hábito. | Trampa: es WIR-057. |
| `NemesisController.cs:649` + `NemesisDirector.cs:1160-1178` | El ancla de presión del Director sobre la zona real del jugador, devuelta antes de las guardas de D40 (`:664-666`). | Trampa por diseño, gruesa, sólo cuando está "tranquilo". |
| `FieldOfListening.cs:322-326, 532-554` | De la posición real al punto percibido, con error ≤ 0,6 × radio: a menos de ~3,3 m el error es ≤ 0,9 m y además cuenta como "preciso" (`NemesisSearchingState.cs:685-693`). | Legítimo (D39). De cerca es casi un GPS. |
| `FieldOfListening.cs:511-515` + `NemesisHidingAwareness.cs:337-373` | La bandera `IsHidden`; la segunda respiración sospecha del escondite más cercano. | Legítimo por D22, en el límite. |
| `NemesisHidingAwareness.cs:398-428` | El escondite al que entraste, si te vio hace menos de 0,75 s y tiene línea a la puerta (sin prueba de cono), hasta 14 m. | Legítimo. |
| `FieldOfView.cs:550-601, 833-857, 889-917` | Proximidad a 1,5 m, rendijas y sentido de espaldas. | Sentidos legítimos. |
| `NemesisStateManager.cs:922-963`; `NemesisHidingAwareness.cs:194-214` | El alcance del agarre; abrir un escondite. | Legítimo. |
| Tensión, telemetría, HUD, destrabe y ciclo de vida | No dirigen al Nemesis. | Legítimo. |

**WIR-062 — busca hacia atrás. Probablemente arreglado si sólo te vio; no se puede afirmar sin
jugar.**

- Un avistamiento siembra un punto con rumbo, y el cono lo limpia
  (`Nemesis/NemesisPossibilityMap.cs:230-253, 261-300`).
- No hay una exclusión explícita de "atrás de mí" ni de "por donde vine":
  `Nemesis/Logic/SearchPickRules.cs:29-35` puntúa valor ÷ (1 + segundos) y nada más. Depende de que
  el mapa no tenga nada ahí.
- Los tests lo cubren sólo en una dimensión idealizada: `PossibilityMapTests.cs:55, 78` y
  `PossibilityZonesTests.cs:82`.
- Caminos que quedan (verificados):
  - Un ruido después de la pérdida resiembra un disco ciego a las paredes y sin dirección
    (`Logic/PossibilityMap.cs:194-249`, `Logic/PossibilityGraph.cs:197-224`). El valor puede caer
    detrás de él o del otro lado de una pared, y sólo se limpia dentro del cono de 170° o a 1,5 m.
  - El rango de limpieza es el base, 7 m (`ScriptableScripts/Nemesis/SO_NemesisData.cs:1167`),
    aunque los ojos sostengan 14 m.
  - Con el mapa vacío o inalcanzable se queda 6 s parado y pasa a `Patrolling`, que puede volver
    por donde vino (`NemesisSearchingState.cs:609-622`).
  - Sin mapa o sin creencia reparte puntos al azar (`:626-631, 957-990`).
- Arreglo (M): sembrar por distancia en el grafo desde el nodo más cercano, no por radio en planta;
  opcional, una penalización al valor que queda detrás de la mirada.
- Tests: un pasillo de dos columnas con la mirada girada 90°; "`SeedArea` a través de una pared no
  deja nada detrás".
- Juego: caso 62, diez veces, con `Draw Possibility Map` y `Draw Search Pick`, con ruido y en
  silencio. Un candidato dibujado detrás de él en `Searching` es un fallo.

**Búsqueda quieta. Sigue reproducible (verificado).**

- "lo está viendo" exige que la creencia sea alcanzable
  (`ScriptableScripts/Nemesis/SO_NemesisPriorities.cs:199-203`, WIR-018), así que cae a `Searching`.
- Las zonas inalcanzables valen 0 (`Nemesis/NemesisSearchPicker.cs:317-323`): la búsqueda pasa a
  *Standing* y vuelve a preguntar cada ~1,2 s (`NemesisSearchingState.cs:602-622, 772-781`),
  mirando alrededor en vez de mirar al jugador (`NemesisLookAround.cs:188-190`).
- A los 6 s puede caer a `Patrolling` con el jugador todavía a la vista (`:343-354`).
- Arreglo (M): en la rama *Standing* con un objetivo a la vista, caminar al final del camino
  parcial o a un punto con línea de visión (reusar `NemesisPursuit.CanSeeFrom`) y sostener la mirada.
- Tests: `SearchCoolingTests.cs:142` afirma hoy que inalcanzable = "revisó todo". Partirlo en dos
  motivos.
- Con el plan: el [§18.5](#185-modelo-propuesto) A dice que inalcanzable no cuenta como revisado.
  El arreglo lo cumple.

**DIS-002 — un ruido a menos de 4 m acorta la investigación. Probablemente ya no existe.**

- La duración es `IsInspecting` (`NemesisInvestigatingState.cs:105-121`): una permanencia de 4 s
  desde que llega, con el cuadro de llegada contado, sostenida por el peldaño de
  `SO_NemesisPriorities.cs:389-393`; después, la búsqueda de D26 (`:411-415`).
- No hay ningún término de distancia en ese código.
- Lo que hay cerca de los 4 m y puede explicar el reporte (inferido):
  - al entrar gira hacia el ruido (`:539-586`), y un jugador agachado a menos de 7 × 0,5 = 3,5 m
    queda a la vista en el acto → `Chasing`. Es lo esperado;
  - la permanencia limpia toda el área escuchada, así que la búsqueda que sigue es quedarse 6 s
    parado;
  - `FocusArbiter.NearDistance` (4 m) sólo protege una pista.
- Test: extraer la regla de permanencia a `Logic`, con "el cuadro de llegada cuenta".
- Juego: el I-c del checklist.

**Las cuatro reglas de búsqueda, hoy:**

| Regla | Estado | Por qué |
|---|---|---|
| (a) Te ve o te oye → va a ese punto | Parcial | Verte lleva a `Chasing`; un ruido en su piso lleva al punto percibido. Desde `Searching` va a una zona de 2,5 m, y al punto sólo si el radio es ≤ 1,5 m. Rota entre pisos (WIR-058) y cuando no puede llegar (búsqueda quieta). |
| (b) Llega y no te ve → investiga alrededor | Se cumple en el papel | Permanencia y después la búsqueda de D26. En salas abiertas se reduce a quedarse ~6 s parado. |
| (c) Te perdió persiguiendo → busca cerca del último punto visto | Se cumple en el papel | Corre al último punto visto (`NemesisPursuit.cs:161-170`) y busca sobre el mapa. Un ruido posterior mueve la búsqueda al área escuchada, a propósito. Ya no mira alrededor en el punto donde te perdió. |
| (d) No busca al azar: hacia donde pudiste escapar, nunca hacia atrás | Parcial | Es un sorteo ponderado, no determinista. "Nunca hacia atrás" sólo emerge del mapa (WIR-062). "Por donde no pasaste" no se modela: el mapa es alcance a 4,5 m/s, no un rastro. |

### 19.3 Pisos y montacargas

Las líneas sin archivo son de `Nemesis/NemesisElevatorUser.cs`.

**WIR-058 — no va adonde escuchó. No lo tocó `145eaf5e`; reproducible cuando la ruta usa el
montacargas o una bajada (lógica verificada, nivel inferido).**

- El peldaño del montacargas lo manda a `Traversing`
  (`ScriptableScripts/Nemesis/SO_NemesisPriorities.cs:187-192`).
- Después de un viaje o una bajada, el compromiso se suelta
  (`NemesisFSM/NemesisStateManager.cs:207-209`) y la única salida es `Searching`, no
  `Investigating` (`SO_NemesisPriorities.cs:312-316`).
- Esa búsqueda sortea sobre un mapa que se dispersó a 4,5 m/s durante todo el viaje
  (`Nemesis/NemesisPossibilityMap.cs:142-156`): elige una zona cerca del descanso, o se queda
  parado. Al punto escuchado no se le debe ninguna visita.
- Causas secundarias (inferidas):
  - sin un camino completo, el oído cae a distancia en línea recta
    (`FieldOfListening.cs:671-691`) e `Investigating` camina hasta el final de un camino parcial
    (`NemesisInvestigatingState.cs:184-192, 591-600`);
  - un ruido del jugador en otro piso vale ×0,7 frente a una pista
    (`Logic/FocusArbiter.cs:249-252`);
  - la creencia funde niveles cuando Δy ≤ 2,5 m (`NemesisBelief.cs:361-365`).
- Con escalera sola no hay link, `Investigating` va a la creencia y la regla se cumple.
- Arreglo (M): un predicado nuevo, al final del enum, y un peldaño "venía por un ruido →
  `Investigating`" en el asset y en `BuildDefaultLadder`; o que `Searching`, cuando viene de
  `Traversing`, visite primero la creencia. Y resembrar el mapa al aterrizar.
- Con el plan: §10 (peldaño en los dos lados, enums al final), D29 y D35.
- Juego: en F9, fila `decisión`, "para llegar hay que tomar el montacargas" seguido de "venía
  hacia el montacargas…" lo confirma.

**El loop del montacargas: cinco mecanismos, del más probable al menos.**

**M1. Estados que no son de la escalera pisan el hueco; al llegar, busca sobre una creencia vieja.**
Verificado y con traza.

- `NemesisElevatorUser.Update` (363–424) arranca un cruce para cualquier estado. Sólo
  `NemesisSearchPicker.SecondsOnFoot` (317–323) descarta las rutas con montacargas.
  `NemesisPatrolState:114`, `NemesisInvestigatingState:344, 599` y la revisión de escondites
  (`NemesisSearchingState` 904–915) no.
- Al llegar, "venía hacia el montacargas y todavía cree algo" (`SO_NemesisPriorities.cs` 312–316)
  sólo pregunta `HasBelief`, que nunca vence.
- `trace_20260928_182056.csv`, t=11.8 y 151.6: `Patrolling`→`Traversing` con la creencia a edad
  infinita / 31 s, y después `Searching` a los 20 s. `trace_20260928_184032.csv`, t=215–217:
  `Searching`↔`Traversing` cada 0,5 s sobre la puerta de la cabina (un link común).
- Cómo se ve: que te sienta una vez y quedate en silencio; la patrulla viaja, busca en el descanso
  y vuelve. O un locker sospechado en el otro piso.
- Arreglo: (S) sumar `BeliefAgeUnder(ElevatorCommitTime)` a ese peldaño, sin enum nuevo; (M) una
  sola prueba "a pie" compartida por la revisión de escondites e `Investigating`, y decidir si la
  patrulla puede viajar.

**M2. El viaje se abandona por un veredicto que se da vuelta.** Verificado; D30 ya describe la
falla.

- `ShouldAbandonForPlayer` (486–492) es "lo ve && !`RouteToBeliefCrossesFloors`". Eso también es
  falso cuando la consulta no puede correr (`NemesisFSM/NemesisDecision.cs` 222–231): el jugador en
  una cabina en movimiento (el NavMesh de la cabina está apagado,
  `Environment/ElevatorCabinNavMesh.cs` 537–548) o a más de 2 m del NavMesh.
- D30 pasó la bajada a una prueba de altura (`IsPlayerUpHere`, 887–893) porque este veredicto
  oscila sobre un link. El montacargas quedó con el veredicto viejo.
- Cada falso positivo corre `AbandonElevator` (566–580): el hueco apagado 10 s, y después vuelve a
  comprometerse.
- Cómo se ve: G23, viajar en la cabina a la vista mientras él espera en el descanso.
- Arreglo (S): abandonar sólo con una ruta completa que no cruza pisos, o reusar la prueba de
  altura de D30.

**M3. Se rinde, espera 10 s y vuelve a comprometerse, mientras la causa sigue.** Verificado.

- El peldaño de entrada (187–192) no tiene memoria. `HasGivenUpOnElevator` sólo bloquea el peldaño
  de sostén (174–180) y es una bandera global (`NemesisStateManager.cs` 207–209).
- Un jugador parado en la cabina la deja en `WaitingForExit`
  (`Environment/MovingPlatform.cs` 242–257, 511–526): la espera vence a los 20 s (1694–1706),
  abandona, el link vuelve (592–604), y se repite. Valores del asset: espera 20, enfriamiento 10,
  compromiso 12.
- Cómo se ve: subir, quedarse en la cabina haciendo ruido.
- Arreglo (M): enfriamiento por hueco que crece y se reinicia con un viaje completo; bandera por
  link.

**M4. Ida y vuelta sin tope.** Verificado en el código; que haga loop jugando es inferido.

- Después de un viaje hay 0,5 s de `HasJustEndedRide` (1280) y el destino restaurado (1276). Si la
  ruta sigue cruzando, `Traversing` no sale nunca y el peldaño de compromiso lo vuelve a fijar (el
  reloj del estado no se reinicia). El cheese C7 del plan está sólo en "vigilar".
- Los links comunes sueltan el compromiso sin condición (693), también a mitad del acercamiento.
- Cómo se ve: G27, alternar de piso por la escalera.
- Arreglo (M): presupuesto de viajes y después emboscada en el descanso (C7); soltar en un link
  común sólo si el viaje no era de piso.

**M5. Montacargas sin energía.** Verificado: el Nemesis ignora `ElevatorPower` (no aparece en
`NemesisElevatorUser`, `NemesisElevatorLink`, `NemesisNav` ni `RequestRide`), mientras que
`NemesisPossibilityMap.cs` 207–214 sí cierra el montacargas para el mapa. Hoy no es un loop. El
arreglo ingenuo (negarse dentro del cruce) crearía un M3 cada 10 s. Arreglo (S–M):
`NemesisElevatorLink` es dueño de `activated = powered && !suspended`.

**Revisado, sin loop:** la cabina fuera de su piso y el plan B están cubiertos
(`BringCabinHereAsync` 1332–1362, `WrongFloorAfterStepOff`). La búsqueda nueva excluye el hueco en
sus elecciones y en la dispersión, pero no la puerta de la cabina ni la revisión de escondites.

**Reglas del §10 que tocan estos arreglos:**

- Todo entra como peldaños o predicados (un solo votante), en el asset y en `BuildDefaultLadder()`.
- Los miembros nuevos de los enums van después de `FocusIsLead` (22) y de `SearchQuietWindow`.
- Las pruebas "a pie" pasan por `NemesisNav`.
- Ya hay warps que saltean `WarpTo`: `TryWarpNear` (1776–1783) y
  `MovingPlatform.DropLooseRiders` (456–482). No sumar más.
- M2 sigue a D30; M4 tiene que conservar la liberación de D29.

### 19.4 Titileo entre Chasing y Searching

**Las condiciones, hoy (verificado):**

- A `Chasing`: "lo está viendo" (`SO_NemesisPriorities.cs` 199–203), que interrumpe y no tiene
  permanencia mínima. Es la bandera cruda del barrido de 0,1 s (`Nemesis/FieldOfView.cs` 801–809,
  sin antirrebote) && !`IsBeliefUnreachable`.
- A `Searching`: "venía persiguiendo y todavía cree algo" (306–310), en cuanto falla "va a donde
  lo vio" (229–240: !`HasArrived`, edad < 10, !`Unreachable`). Sólo aplica `MinimumStateDwell`,
  0,35 s (`NemesisFSM/NemesisDecision.cs` 416–419, 541–552).
- La única pista del arreglo perdido: `00d97c61` agregó `Not(SeesPlayer)` al compromiso de 0,5 s de
  la búsqueda (115–120), o sea que sacó el freno del lado de la búsqueda.
- `VisionLossGracePeriod` (2,5 s) no lo lee ningún peldaño: sólo `NemesisChaseProgress.cs:443`. El
  texto de `Editor/SO_NemesisDataEditor.cs:948` que dice lo contrario quedó viejo.

**T1. `IsBeliefUnreachable` aletea y corta los dos peldaños de persecución sin margen.** Con traza.

- `trace_20260928_221019.csv`, t=11.23, 14.30 y 33.68: `Chasing` dura exactamente 0,35 s con
  `sees=1` ("esperando la histéresis", y después `Searching`). Lo mismo en el montacargas:
  `trace_20260928_184624.csv`, t=97.75.
- De dónde sale: el oráculo reusa cualquier respuesta a menos de 2,5 m durante 0,4 s
  (`Nemesis/NemesisPathOracle.cs` 83, 141–146); el ajuste de 2 m (`Nemesis/NemesisNav.cs` 167–168);
  y "no pude consultar = inalcanzable" (`NemesisDecision.cs` 253–263). No se determinó por qué la
  creencia daba inalcanzable en esa traza.
- El checklist nombra este loop en S3-b. Cómo se ve: S3-a y S3-b, la puerta de G20, el jugador en
  la cabina.
- Arreglo (S–M): un "inalcanzable" calificado por tiempo (predicado nuevo, al final) o un
  antirrebote dentro del oráculo.

**T2. No hay gracia del lado de la persecución.** Con traza.

- En el último punto visto (`HasArrived`), un barrido perdido termina la persecución en 0,35 s, y
  la reentrada es instantánea.
- `trace_20260928_182056.csv`, t=109–114: le quedaban ≈ 0,59 m en cada salida, con estadías en
  `Searching` de exactamente 0,50 s (el freno que después se sacó).
- Arreglo (S): un peldaño `InState(Chasing) && BeliefAgeUnder(VisionLossGracePeriod) &&
  !Unreachable`, debajo de "sabe en qué escondite está" ([§3.5](#35-cómo-se-expresa-en-el-fsm--sin-estado-nuevo)).

**T3. `AdaptiveViewRange` (131–152) cae de 14 a 7 m con un barrido perdido.** Inferido.

- Entre 7 y 14 m eso son (d/7 − 1) · 2 s de ceguera. Hace titilar la vista, no el estado, pero
  alimenta T1 y T2.
- Arreglo (S): un decaimiento corto del rango sostenido. Cambia el test `LosingSight_DropsToTheBase`.

**Cómo quedó (05/10, etapa B).**

- **T2 no se arregló como decía arriba.** `BeliefAgeUnder(VisionLossGracePeriod)` habría estado mal:
  la edad de la creencia la renuevan tus propios pasos, y una persecución sin vista va al último
  punto donde te **vio** y se queda ahí (`NemesisPursuit`, §16.4). Con esa regla se quedaba parado
  en la esquina todo el tiempo que te oyera correr. Iñaki pidió que el aguante dependa de una mezcla
  de la última posición vista, lo último escuchado y la creencia de dónde podrías estar, con el oído
  pesando menos (D45). Esa mezcla ya existe y tiene un número: el radio de `NemesisBelief`. El
  peldaño "todavía sabe dónde está" lo compara con `Chase Hold Radius`, y `Chase Hold Max Time`,
  contado desde la última **vista**, es el tope que impide que el oído solo sostenga la persecución.
- **T1 se arregló en el oráculo, en los dos sentidos.** `SettledVerdict` cambia de respuesta recién
  cuando la nueva se sostuvo `Route Verdict Settle Time`. La primera después de un rato sin preguntar
  vale como viene: un Nemesis que recién te ve en un lugar inalcanzable no te persigue 0,75 s antes
  de darse cuenta.
- **T3 no se tocó.** Que el rango caiga a la base al perder la vista es una decisión del 04/10
  (romper la línea de visión le da al jugador los 2 s que tarda en volver a crecer). Con la traza
  nueva se ve si las pérdidas en persecución son `cone` u `occluded`/`range`.

**Tests.** Existen los de `AdaptiveViewRange`, `ChaseProgress` (`ChaseGapWindow`), `ChaseGaze`,
`VisionZones`, `SearchCooling`, `SearchPickRules`, `PossibilityMap`/`Zones` y `DropPath`. Ninguno
mira la escalera, el oráculo ni `NemesisElevatorUser`. A agregar: un replay de la escalera sobre
secuencias de predicados (ningún A→B→A dentro de N s), la paridad entre el asset y los valores por
defecto, las reglas puras de abandonar / enfriar / soltar, y el antirrebote del oráculo.

### 19.5 Escondites por el mapa y patrulla después de la caza

Es la tanda 2 del plan perdido (sus fases 2e y 2d). No hay nada de su código en el repo.

**2e — Escondites por el mapa (D38)**

Lo que ya existe (verificado):

- Un nodo y una compuerta por escondite: `Nemesis/NemesisPossibilityGraphBuilder.cs:389`.
- La salida lenta, ×0,1 (`Logic/PossibilityMap.cs:56, 312`). La vista nunca limpia un escondite
  (`:345`); sólo `Open` (`NemesisPossibilityMap.cs:302`, alimentado por
  `NemesisHidingAwareness.SpotOpened`).
- Los escondites no entran en las zonas de búsqueda (`IsSearchable`, `PossibilityMap.cs:502`). F9
  muestra sólo el total, "escondites %".
- El consumidor está completo: `SpotToCheck` → `NemesisSearchingState.TickSpotCheck:856` → `Open`
  → `MarkChecked`.
- El lado del hábito: `PlayerHabitTracker.GetSpotUsage` / `OpenChance:485-491`,
  `CollectUsedSpots:510`, y la compuerta de R3 dentro de
  `NemesisHidingAwareness.ConsiderUsedSpots:260`.
- Sigue siendo el sorteo: `NemesisSearchingState.ConsiderUsedSpots:224` (llamado en 598, 620 y 631)
  y `NemesisInvestigatingState.cs:288`.

Lo que falta (verificado):

- Un acceso al valor por escondite (`nodeOfSpot` es privado, `NemesisPossibilityMap.cs:60`).
- La regla pura y sus tests.
- Tunables: `SO_NemesisData` tiene 11 campos `searchMap*` y ninguno para escondites.
- Una entrada pública para sospechar por el mapa (`Suspect` es privado,
  `NemesisHidingAwareness.cs:453`).
- F9 y gizmo por escondite.

Pasos:

1. La fórmula, decidida el 05/10 (D41): el mapa solo puede abrir un escondite nunca usado, con un
   umbral alto, e `Investigating` deja de sortear.
2. `Logic/SpotOpenRules.cs` y sus tests (S).
3. `NemesisPossibilityMap.ShareInSpot` (S).
4. El umbral y la escala del hábito en el SO, y `SO_NemesisDataEditor` (S).
5. `ConsiderSpotsByMap` en `NemesisHidingAwareness`, reusando el `habitSpot` / `MarkRun` de R3;
   reemplazar los tres llamados y sacar `usedSpotsRolled` (M).
6. F9 y gizmo (S).
7. Reescribir los casos 6, 39 y 40 (S).

**2d — Patrulla después de la caza**

Lo que ya existe (verificado):

- `NemesisSearchingState.ExitState:485` llama a `RequestNearbyPatrol`, de un solo uso, que
  `NemesisController.BeginPatrolCycle:327` consume como `preferNeighbours`.
- `NemesisClusterPatrol.PickCluster:533` pondera: peso de la ruta × sesgo por el punto de la
  creencia (decae con `BeliefFreshness`) × ancla de zona × sesgo de vecinos × recencia.
- El ancla de zona ignora al jugador real durante una caza más `HuntGraceSeconds`, 45 s (D40,
  `NemesisController.cs:255, 660-666`).
- Replanifica cada `RouteReplanInterval`.
- El "vuelve a pasar" del Director (`NemesisDirector.cs:1207-1278`, sólo en *BuildUp*, 20–40 s
  después).
- El mapa sigue corriendo en todos los estados (`NemesisStateManager.cs:1511`); la creencia sólo
  se olvida en la captura y el respawn.

Lo que falta (verificado): nada del controller, de la patrulla por clusters ni del grafo de rutas
lee el mapa.

Pasos (M en total, independiente de la 2e):

1. Valor por cluster con el `NemesisPossibilityMap.ShareNear:327` que ya existe, sobre el centroide.
2. Un multiplicador en `PickCluster:558-599`, y el ancla del prefiltro (`:548`) puesta en
   `TryGetBest`. Se le pasan números sueltos, para que la clase siga sin conocer sensores.
3. Llenarlo en `BuildClusterSettings:605` sólo mientras `IsInHuntOrGrace`, apagándose.
4. La fuerza en el SO y una fila en F9.
5. El test del peso, puro.

Salvedades:

- (Inferido) El mapa se dispersa a 4,5 m/s: a los ~20–30 s está casi plano, salvo dentro de los
  escondites. El sesgo pesa en la primera vuelta o dos, y el residuo ata la 2d a la 2e.
- El retiro de *Relax* del Director tiene que seguir ganando.
- El comentario de `NemesisController.cs:292-294` ("NOT biased towards staying nearby") contradice
  el código de `:327`.

### 19.6 El plan contra el código

**La lista de Pendiente del 28/09, ítem por ítem:**

| Ítem | Estado |
|---|---|
| 1. 2B parte 5 | **Cambió.** El parche del "punto visto" de `NemesisPursuit` sigue (`:149-170`), pero su motivo de orden (`IsStandingWhereLost`) ya no existe. El filtro de `Investigating` sigue (`:459-460`). No hay gizmo del radio de la creencia ni tests de la creencia o de la escalera; `NemesisBelief` es un `MonoBehaviour` fuera de `WIRED.Nemesis.Logic` y hay que extraerlo (L). |
| 2. Fase 6 | Vigente. Ver el [§19.7](#197-fase-6-ganchos-y-bloqueos). |
| 3. Fase 8 | Vigente. Dato nuevo: `Art/Models/Characters/Nemesis/New/NemesisController.controller` ya tiene `Drop Look`, `Hop Takeoff`, `Hang Turn`, `Hang Release`, `Fall Loop` y `Land Heavy`, todos sin clip. |
| 4. Falta jugar | 2B parte 2, **obsoleta** ("gizmo del barrido", casos 41 y 42). Arreglos del 27/09: el texto de F9 "yendo al último punto" ya no existe (ahora es `ancla:`). 2B parte 3: el "tope" está apagado (`searchHardCap = 0`, `SO_NemesisData.cs:848`). Fases 3 y 4: "estanca" ahora suma 1 por persecución. La mitad de la 2D vale hasta la 2e. El resto, vigente. |
| 5. §16.2 | La lista de tests nombra el borrado `SearchSweepRulesTests` y le faltan 9 archivos nuevos; la viñeta de `NemesisFreeRoam.AddSampledPoints` ya no aplica. `Pull Out`, el bake del container y los tests de Play mode siguen vigentes. |
| 6. Decisiones | Vigente. D38 (sin confirmar), D39 y D40 faltan en el §11; D37 no existe en ningún lado. |
| 7 y 8. Zona 2 y sueltos | Vigentes (inferido: no se volvió a mirar en las escenas). |

**Casos del §13 que describen comportamiento borrado:**

- Obsoletos: 41 y 42.
- Con texto o números viejos: 2 y 33 (el barrido); 6 (superado); 23 (ahora también vuelve a elegir
  cuando el lugar pierde valor); 43 y 45 (el tope de 30 s); 7 y 52 (una "estanca" por ventana); 1
  ("lo vio entrar" ahora llega a 14 m).
- Los 62 a 67 no estaban: se agregaron el 05/10, reconstruidos del handout. El 64 se perdió del
  todo y el 66 quedó sólo con el nombre.

**Lo que el plan no menciona en ningún lado** (cero resultados al buscar):

- El mapa de posibilidades entero y `NemesisSearchPicker`.
- `AdaptiveViewRange` (sostener ×2, cazar ×2 en 2 s), `VisionZones` (el sentido de espaldas),
  `ChaseGaze`, `ChaseGapWindow` y `ChaseStallCounter`.
- `HearingLocalization`, `MayVisitEvidence`, `IsInvestigationWarm` y `HuntGraceSeconds`.
- De `00d97c61`: el agarre de la captura, `NemesisArmWallGuard`, `NemesisArmThud` y `ElevatorPower`.

**Los otros docs:**

- `CLAUDE.md` no es la verdad para la búsqueda: las líneas 1475–1501, 1522 y 1558–1621 todavía
  documentan el disco borrado (`SearchSweepRules`, `NemesisFreeRoam`); la 1703 dice "Fase 2a: it
  only watches", y las 1728–1730 listan las fases 2b–2e como "Next". También 365 y 1311–1312.
- `Nemesis-System.md`: 226–233 y 241.
- `Checklist-NemesisTestbed.md`: 71, 148, 157, 227, 311–312, 350–351 y 390.
- Este plan, por sección y no por línea (las líneas se corren con cada edición): dos menciones en
  ✅ Hecho, una en la Fase 2D del §9, tres en el §12 y una en el §16.2. Se encuentran buscando
  `SearchSweepRules`, `NemesisFreeRoam` y "barrido".

### 19.7 Fase 6: ganchos y bloqueos

Ganchos que ya existen (verificado):

- `ECounterplay` (6 valores) y las filas de `SO_CounterplayRules`: `ExitAmbush` a los 3 escapes
  escondido, `ChaseFlank` a 1 "estanca", `ZoneDefense` a 2 "estanca" o 2 escapes por el Hub.
- `PlayerHabitTracker.IsUnlocked` / `CounterplayChance` / `HasRun` / `MarkRun` (`:544-555`). El
  único que lee es `NemesisDebugHUD.cs:1066`; el único que llama a `MarkRun` es
  `NemesisHidingAwareness.cs:206`.
- `IsChaseStagnant` sólo lo lee `NemesisPursuit.Replan:391`. El predicado de índice 12 no lo usa
  ningún peldaño, ni en los valores por defecto ni en `SO_NemesisPriorities.asset`.
- `OnChaseStalled` sólo llega al tracker.
- `HidingSpot.Burn()` (`:508`) no tiene ningún llamador.
- `NemesisAmbushPoint` no existe.

| Contra-jugada | Trabajo | Bloqueada por |
|---|---|---|
| `ChaseFlank` | S: sumar con un OR una bandera armada en `:391`, sorteada al empezar la persecución. | Waypoints alrededor de `Column_Loop` en la testbed (nivel, S). |
| `ExitAmbush` | M: una fase final o un peldaño nuevo, un punto de espera con línea al `ExitPose`, 8–15 s. | Nada de arte. Mejor después de la 2e. |
| Soltar y emboscar + `ZoneDefense` | M–L: componente nuevo, validador, familia en `GizmoManager`, un peldaño sobre `IsChaseStagnant` y una fuente de presión del Director. | Puntos de emboscada puestos a mano (nivel; la Zona 2 no existe). |
| `BurnHidingSpot` | El llamado, S. | El modelo del locker roto, la animación y el SFX; `HidingSpot` no tiene cambio de visual. |

También bloqueado por arte: `Pull Out` (no hay estado en el Animator), los clips de las bajadas, la
mirada en el rig de la cabeza y los clips de voz. La 2B parte 5 es sólo código.

### 19.8 Diagnóstico y comentarios viejos

**`Nemesis/NemesisTraceRecorder.cs`.** Columnas de hoy (`:159-163`): `time`, `kind`, `state`,
`time_in_state`, `rung`, `reason`, `gait`, `sees`, `hears`, `suspicious`, `awareness`,
`belief_age`, `belief_from`, `dist_belief`, `dist_player`, `player_dy`, `agent_ready`,
`path_pending`, `has_path`, `path_status`, `remaining`, `agent_speed_now`, `net_flat_speed`,
`agent_speed_cmd`, `using_lift`, `chase_stagnant`, `repaths`, `warps`, `x`, `y`, `z`.

Lo que falta y dónde vive:

- **Rango efectivo:** `FieldOfView.EffectiveViewRange:121`, `ViewRangeScale:126` y
  `ViewRangeReason:130` son públicos. El valor partido por estar agachado es una variable local
  (`:727-733`).
- **Agachado:** `PlayerStateManager.IsCrouch:82` (también `IsHidden` e `IsHoldingBreath:156`).
- **Causa de la pérdida de vista:** no se guarda en ningún lado. `FindVisibleTargets:690-820`
  descarta candidatos en silencio: escondido (`:705`), rango (`:746`), y cono y oclusión juntos en
  un solo booleano (`:760`, `LineOfSight.CheckConeSampled`). Necesita un enum de motivo, escrito en
  el flanco (S–M). El recorder no tiene que tirar rayos.

**Comentarios que nombran tipos borrados** (`NemesisRooms`, `NemesisFreeRoam`, `SearchSweepRules`,
el barrido):

- `Editor/Nemesis/NemesisTestbedHidingLabBuilder.cs:17, 48, 526`
- `Nemesis/NemesisDebugHUD.cs:267-268, 277-278`
- `Nemesis/NemesisFSM/NemesisStateManager.cs:751, 1757` (el enum vivo `FreeRoam`) y `:1510`
- `Nemesis/NemesisController.cs:292-294, 389`
- `Nemesis/NemesisHidingAwareness.cs:238, 257, 332`
- `Nemesis/PlayerHabitTracker.cs:507`
- `Nemesis/NemesisBelief.cs:53, 136`
- `Nemesis/NemesisChoice.cs:332-333`
- `ScriptableScripts/Nemesis/SO_NemesisPriorities.cs:280, 301`
- `ScriptableScripts/Nemesis/SO_CounterplayRules.cs:77` (dice 8; el valor por defecto es 7)
- `Nemesis/NemesisFSM/NemesisTraversingState.cs:18` y `NemesisChasingState.cs:63`
- `Nemesis/NemesisRouteGraph.cs:138`, `FieldOfListening.cs:527`, `NemesisChaseMusic.cs:194` y
  `NemesisPossibilityMap.cs:22`
- `Hiding/HidingSpot.cs:42`

Historia a propósito, se deja: `NemesisSearchPicker.cs:10`, `SearchPickRules.cs:7` y
`NemesisSearchingState.cs:8, 219, 387, 708`.

### 19.9 Análisis de causas del 07/10 (WIR-057, 058 y 062)

Sobre `HEAD` `8a3e16ba`, que ya tenía C0, C1 y C2 (`417ce019`), leyendo el código y la traza del
05/10 (`trace_20261005_143612.csv`, anterior a la etapa C). Sin jugar. La regla de base del reporte
("la búsqueda arranca de lo que percibió, nunca de tu posición real") ya se cumplía salvo en el
Director (abajo): lo que fallaba era cómo convertía lo percibido en dónde buscar.

**Un dato de la traza que explica casi todo WIR-062.** En 5 de los 7 pases de `Chasing` a
`Searching`, la creencia ya venía de un ruido (`belief_from=noise`), no de la vista.
`NemesisBelief.SightAnchorWindow` es 0,25 s: si te oye correr apenas te pierde, el mapa deja de
sembrar "punto + rumbo" y siembra el disco del ruido. Ese disco era en planta y cruzaba paredes.

**Arreglado el 07/10** (etapa C, en [Pendiente](#pendiente)):

| Bug | Causa | Arreglo |
|---|---|---|
| WIR-062 | El disco del ruido (y el del avistamiento, de 2 m) se medía en planta: en un pasillo angosto caía casi todo en las salas del otro lado de las paredes, a las que sólo se llega volviendo y que el cono nunca limpia. | C3: se siembra caminando por el grafo. |
| WIR-062 | Nada modelaba "la salida que él tapaba": el valor podía volver por donde vino él. | C5: el rastro. |
| WIR-062 | El "acá no está" llegaba a 7 m con la vista en 14. | C3: limpia hasta el rango efectivo. |
| WIR-062 | La renormalización convierte cualquier sobra detrás suyo en "lo más probable" una vez limpio lo de adelante. | Sin cambio propio: con C3 y C5 esas sobras ya no se forman. |
| WIR-058 | Buscando en la pasarela, tu ruido abajo no pasa por `Investigating` (la búsqueda tibia va antes), y con el oído midiendo por camino el área de abajo sale grande y lejos: ningún lugar llegaba a valor ÷ (1 + caminata) ≥ 0,015. "Revisó todo", 6 s parado, patrulla. | C6: visita debida. |
| WIR-057 | "Lo vio entrar" no pedía el cono: una puerta al costado o detrás, con línea a la puerta y una vista de hace menos de 0,75 s, contaba. | C7: pide el cono. |

**Lo que queda.** Ninguna de estas causas es un bug. Las de WIR-057 son decisiones de diseño, y
falta que Iñaki decida si se mantienen.

- **WIR-057 — su última evidencia es la puerta del escondite.** La búsqueda va a donde te percibió
  por última vez, y se para en el punto mismo si es preciso (te vio, o un paso a menos de ~3,3 m sin
  pared, `MayVisitEvidence`). Si te metiste justo ahí, la puerta queda a un paso. Ahí terminan los
  sentidos de cerca, todos por diseño: proximidad a 1,5 m salvo que aguantes la respiración (D21),
  las rendijas (nivel B) y la respiración, que se oye a ~2 m (D22). Para el jugador es "fue derecho".
- **WIR-057 — el hábito (D23).** Un locker que ya usaste y del que saliste se sortea con hasta 85 %
  en cada elección a 8 m, y otra vez cada vez que vuelve a buscar o investigar. En un playtest que
  reusa el mismo locker se ve igual que saber dónde estás. F9 lo distingue: "lo usaste antes (n)".
- **WIR-057 — el Director usa tu zona real aunque estés escondido.** La "sensibilidad creciente"
  (`NemesisDirector.ApplyRisingSensitivity`) elige la zona con `player.position`, y
  `TryGetPressureAnchor` se devuelve en `NemesisController.TryGetZoneAnchor` antes de las guardas de
  D40 (escondido, o caza reciente). Trae la patrulla a tu zona, emite ruidos sintéticos ahí y le
  sube los sentidos. Sólo después de 90 s de calma (`quietTimeout`): explica "me quedé escondido y
  vino", no "me perdió y vino derecho". **A decidir:** aplicarle las guardas de D40 o dejarlo como
  el anti-estancamiento de Mr. X.
- **WIR-057 — el valor queda encerrado en los escondites** (inferido). Un escondite pierde valor
  10 veces más lento y mirarlo no lo limpia; limpio el piso, casi todo queda adentro, la búsqueda no
  tiene adónde caminar y a los 6 s patrulla cerca (`RequestNearbyPatrol`). En la traza, las dos
  búsquedas con el jugador escondido duraron 6 s justos. No va derecho, pero vuelve a pasar por la
  puerta. Lo cambia la etapa E (escondites por el mapa).
- **WIR-058 — con montacargas o bajada:** D7, etapa D.
- **WIR-058 — una pista le gana:** tu ruido en otro piso vale ×0,7 frente a una pista
  (`FocusArbiter`), y además se castiga por distancia de camino; un ruido del Director o un señuelo
  en su piso gana "su atención está en una pista". Por diseño (D18, D35).
- **WIR-058 — choque de reglas.** El reporte pide "va al punto donde escuchó"; la Fase 1 dejó a
  propósito de ir al punto de un ruido vago, porque ese punto suele ser la puerta del locker
  (WIR-057). Hoy va al área del ruido, y al punto sólo si fue preciso. **A decidir.**
- **WIR-062 — si la búsqueda termina, la patrulla puede volver** (E3, etapa E). La patrulla no lee
  el mapa. F9 lo distingue: volver en `Searching` es el mapa; en `Patrolling` "nada que atender", es
  el fin de la búsqueda.
- **WIR-062 — el mapa se queda atrás de un jugador que corre** (inferido). El valor se esparce
  como una difusión: el grueso avanza ~3·√t m (≈ 6 m a los 4 s) mientras el jugador corre 18. La
  salida lejana tiene poco valor y cae bajo el umbral antes de que la busque. C6 lo alivia sólo en
  la primera elección.
