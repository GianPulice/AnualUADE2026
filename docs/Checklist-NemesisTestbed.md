# Checklist: Nemesis + Director en `NemesisTestbed`

> Relevado el 21/09/2026 contra el working tree (Fase 2 construida, sin commitear). Para usar en Play
> con F9 (HUD) y F10 (consola). Cada prueba: **qué hacer → qué mirar → qué esperar**, y qué sería un
> bug. Los casos de escondites van en el *Hiding Lab* de la testbed (desde el 29/09; también siguen en
> `TestIñaki.unity`).
> Contexto de diseño: `docs/Plan-IA-Stalker.md` (§3 escondites, §13 casos, §16 revisión).

**El Nemesis del testbed ya no arranca dormido.** Heredaba `activatedByPuzzleId = sp2_contenedores`
de `Nemesis.prefab` y en el testbed nadie completa ese puzzle. El 21/09 se le puso el override vacío
en la instancia del Nemesis (`Testbed/Actors/Nemesis` → `NemesisController` → *Activated By Puzzle
Id* vacío, en negrita). A1 lo verifica.

## Mapa rápido (x, z en metros; norte = +z)

- **ENTRADA** (x −6..6, z −5..5): arrancás en (0, −2) mirando al norte. `Column_Loop` (1.2 m) en
  (3.2, 1.8). El `Locker` en (−4, 2) es **decoración, no escondite**. DoorWood al norte, en z 5.
- **SALA_SEGURA** (este, x 9..19): volumen Not Walkable bien armado. **SALA_ROTA** (oeste,
  x −19..−9): volumen en Default, **rota a propósito**.
- **PASILLO** (x −2..2, z 5..35): columna `Cover_Pasillo` en z 20. Por las bocas de z≈14 y z≈26 se
  pasa a **PASILLO_OESTE** (x −10..−6); juntos forman un loop.
- **SALA_LATERAL** (x −22..−10, z 15..25): 3 columnas y `Crate (ignoreFromBuild)`. Se entra desde
  PASILLO_OESTE por z 18.5–21.5. Al oeste, una puerta de 3 m lleva al Bug Lab.
- **PASILLO_CARGA** (z 35..45, DoorWood en z 35). Al norte, Link_Alcove → **ALCOVE** (sin salida).
  Al este, **SALA_MONTACARGAS** con el montacargas en (13, 38).
- **Piso de arriba** (y 5.3, sólo por montacargas): PLANTA_ALTA (x 7..19) y PASARELA_ALTA (x −7..7).
- **Bug Lab** (x −40..−22):
  - **Hall** (z 12..28): pilares de **S2** en pares en z 16.7 y 23.3. Balcón de **S3** en la esquina
    noroeste (x −40..−36.9, z 21..28, 2.6 m de alto), con rampa pegada a la pared oeste desde z≈12.6.
  - **S1** al norte: vestíbulo z 28..34, puerta de abajo en x≈−31.6, de arriba en x≈−34.7, cuarto de
    arriba a 4.98 m.
  - **S4** al sur, entrando por el vano de z 12: Room A (z 5.5..12) → vano con línea amarilla en
    z 5.5 → Room B (z −1.5..5.5, adentro del trigger).
- **Drop Lab** (desde el 27/09; x −9..9, z −29..−9): se entra desde ENTRADA por una puerta de 3 m en
  su pared sur, detrás del spawn. Al fondo hay dos entrepisos, cada uno con una rampa contra la pared
  lateral para volver a subir:
  - al oeste, el de **3.6 m**, con `Drop_Hang` en su borde norte (x −4.5);
  - al este, el de **2 m**, con `Drop_Hop` (x 4.5).

  Las barandas del borde son invisibles y sólo te frenan a vos: el Nemesis se tira a través de ellas.
  Lo arma *Tools/Nemesis/Build Drop Lab (NemesisTestbed)* (builder borrado el 29/09, en git).
- **Hiding Lab** (desde el 29/09; x 2..28, z 6..34): se entra desde PASILLO por dos aberturas de 4 m en
  su pared este, en z 6..10 (**Pasillo Sur**) y z 30..34 (**Pasillo Norte**). Los dos pasillos y el
  **Pasillo Este** (x 20..24) rodean tres salas encadenadas: **Sala de mesas** (x 2..11, z 10..19),
  **Vestuario** (z 19..30) y **Depósito** (x 11..20). Al este, el **Cuartito** sin salida (x 24..28).
  Escondites (id `tb_*`), cada uno con su cartel:
  - **A** `tb_locker_vista`, pared sur del Pasillo Sur (x 13): el pasillo recto para H-1.
  - **B** `tb_locker_vuelta`, pared este del Pasillo Este, 2.5 m pasada la esquina (z 12.5): H-2 y H-3.
  - **C** `tb_locker_callejon`, al fondo del Cuartito (z 20): acorralado, aviso D34.
  - **D** `tb_mesa_1` (pared oeste, z 14.5, marcas 1–6 m) y `tb_mesa_2` (pared norte): H-4.
  - **E** `tb_fila_1..3`, pared norte del Vestuario (x 4.2 / 5.6 / 7.0), con el Pasillo Norte detrás: H-8
    (¿abre el correcto?) y H-10.
  - **F** `tb_container`, contra la pared norte del Depósito, puerta en z 23.4 (marcas 1–6 m), y dos
    cajas de 1.2 m para cortar la vista: H-5 y H-9.

  `PASILLO/Wall_E` quedó desactivada (la reemplazan tres tramos del lab). Lo arma *Tools/Nemesis/Build
  Hiding Lab (NemesisTestbed)*, que se puede volver a correr.
- **Señuelos** (desde el 28/09, ya en la escena): radio en
  SALA_LATERAL, sobre una mesa contra la pared norte (x −19); alarma en la pared oeste de PASILLO_CARGA
  (z 38), audible en todo el nivel; cadenas en la pared oeste de PASILLO_OESTE (z 30). Se prenden con E.
- **Rutas** (peso 1, abiertas): Spine, Oeste, Alcove, Alta, BugLab (WP_00..06), DropLab (WP_00..03) y HidingLab (WP_00..09: 01 a ~2 m de A, 02 a ~1.4 m de B, 03 en el Cuartito, 05 a ~5 m del container, 07 a ~2.8 m de la fila, 08 a ~5 m de la mesa 1, 09 detrás de la fila con la pared en medio).
- **Spawns**: Carga (0, 41), Lateral (−16, 20), Alcove (0, 50), Alta (5, y 5.3, 46.4).
- **Director**: `Testbed/Director` con 4 zonas: `entrada` (0, 0) r 8, `sala lateral` (−16, 20) r 10,
  `carga` (0, 40) r 9 y `planta alta` (9, y 5.3, 45.4) r 10. Sin disparadores por puzzle.
- **Números:**
  - **Vista:** 7 m, cono 170°, foco 80°, agachado 3.5 m. **Proximidad 1.5 m, plana desde sus pies y
    sólo en su mismo piso** (antes nunca disparaba en el mismo piso; ver Plan §3.3).
  - **Oído:** caminando 10 m, corriendo 15 m, agachado 2.5 m; ×0.8 por pared y ×0.75 por piso; se
    mide por camino de NavMesh.
  - **Agarre:** 1 m, ±1 m de altura, con línea de vista.
  - **Tiempos:** gracia al perderte 2.5 s, búsqueda 15 s, inspección de un ruido 4 s.
  - **Velocidades:** él 2.75 / 2.5 / 3.0 / 2.75 (patrulla / investiga / persigue / busca); vos 2.5
    caminando, 4.5 corriendo, 1.25 agachado.

## 1. Antes de empezar

- [ ] **A1. El Nemesis despierta.** Play, caminá un paso hacia la pared sur (el **cuerpo** mirando al
  sur; girar sólo la cámara no alcanza) y abrí F10. → `Active: True`; F9 `estado: Patrolling (…)`.
  **Bug si:** `Active: False` + "Dormant…" sin ningún `[NemesisController] No spawn point…` en la
  consola (sería la puerta del puzzle otra vez).
- [ ] **A2. Spawn seguro.** Arrancás mirando al norte, con los 4 spawns adelante. → Espera y tira
  **un** warning `No spawn point… In the player's view: 4`. Al darte vuelta aparece en ≤0.5 s, a
  ≥15 m por NavMesh y detrás de una pared (probablemente Alcove o Alta). **Bug si:** aparece a la
  vista o cerca.
- [ ] **A3. Escena y horneado.** El testbed tiene que ser la escena **activa**; Play pasa por
  `Bootstrap` y carga `Data` + el grupo *TestNemesis* (consola: `[Bootstrapper] 1…3`). **No** uses el
  *Bake* del NavMeshSurface con Zona1 abierta (hornea Zona1 adentro del testbed). Si hace falta, usá
  `Tools/Nemesis/Build Hiding Lab (NemesisTestbed)` (hornea sólo el testbed) o cerrá Zona1 antes.
- [ ] **A4. Validador** (en Edit): `Tools/Nemesis/Validate Navigation Setup`. → Esperado:
  `SafeVolume (BROKEN - on Default…)` (a propósito) y un `NavMeshModifier on 'Hinge' does nothing`
  por puerta (el testbed no hornea Default). Anotá cualquier otra cosa antes de probar.
- [ ] **A5. Consola al arrancar.** **Bug si** aparece: `[NemesisStateManager] … could not resolve`,
  `[NemesisDirector] Noise Layer … not in the Nemesis's Listen Mask`,
  `[NemesisPressureZone] … has no id`, `[NemesisElevatorLink] … incomplete / does not land on the
  NavMesh`, o `[NemesisRouteGraph] … NavMesh islands` (piso de arriba desconectado).
- [ ] **A6. Herramientas.** F9 = HUD (arriba a la izquierda, apagado por defecto); F10 = consola. Las
  teclas 1–6 fijan un estado (Patrol/Investig/Chase/Search/Traverse/Catch) y 0 lo suelta; el **1**
  sirve de "modo turista". «Player onto the Nemesis» te lleva hasta él. Fijar 6 sin tenerlo al lado
  vuelve a Searching con `Entered Catch without a target` (a propósito). **No toques F8** (explota un
  módulo) **ni Y** (congela al jugador).
- [ ] **A7. Gizmos.** Prendé *Gizmos* en la Game view o usá la Scene view. En `NemesisGizmos` de la
  instancia tildá *Vision Cone, Focus Cone, Crouched Vision Cone, Under Table Vision Cone, Proximity
  Detection, Hiding Knowledge* y *Labels* (en el prefab vienen apagados; lo de Play se revierte). **No
  edites los SO en Play:** queda guardado en el asset.
- [ ] **A8. Director registrado.** F10 → DIRECTOR con 4 botones (`entrada`, `sala lateral`, `carga`,
  `planta alta`). **Bug si:** "No pressure zones in the scene".

## 2. Pruebas generales

**Patrulla y rutas**
- [ ] **G1. Tour de cúmulo.** Seguilo 2–3 min; mirá F9 `cúmulo #n i/b` y el gizmo ámbar numerado. →
  Barre 3–5 paradas cercanas (incluidos los puntos generados, esferitas), espera 0.9–2.1 s barriendo
  la mirada ±50° y se muda a un cúmulo **vecino**. **Bug si:** salta de punta a punta, repite siempre
  la misma pausa o no mueve la mirada.
- [ ] **G2. Cobertura.** Dejalo ~5 min. → Recorre las salas y entra al Bug Lab; a veces sube a
  PLANTA_ALTA (F9 `Traversing`, «esta cruzando el montacargas»). **Bug si:** se repite
  `[NemesisPatrolState] No path to waypoint…` (salvo Route_Oeste/WP_01, ver Huecos).
- [ ] **G3. Se arrima hacia vos.** Quieto 1–2 min en SALA_LATERAL. → Cada ~12 s re-sortea y tiende a
  tu lado. Es un sorteo. **Bug si:** va siempre derecho a vos, o nunca se acerca.
- [ ] **G4. Puertas.** Mirá cómo cruza las DoorWood (z 5 y z 35) patrullando y persiguiendo. → Las
  abre antes de llegar. **Bug si:** atraviesa la hoja cerrada o corre en el lugar frente a la puerta.

**Vista**
- [ ] **G5. Foco.** Quieto en el medio del PASILLO, que venga de frente. → A ≤7 m y ±40°: `lo ve` al
  instante y `Chasing` con «lo está viendo».
- [ ] **G6. Periferia.** Quieto y en silencio a 4–6 m, a 45–80° de hacia dónde camina (p. ej. la boca
  de Link_Loop mientras recorre el PASILLO). → `sospecha` sube (más rápido cuanto más cerca); a 0.40
  entra en `Investigating` («vio algo de reojo»); a 1 pasa a `Chasing`; fuera de su vista baja
  ~0.5/s. **Fijate adónde camina:** `Investigating` sólo fija destino con el último **ruido**; si
  sigue su patrulla o va a un ruido viejo en vez de acercarse a mirarte, anotalo (posible hueco).
- [ ] **G7. Agachado.** G5 agachado y quieto. → Te ve recién a ≤3.5 m.
- [ ] **G8. Espalda, paredes y proximidad.** Quieto a 2–3 m a su espalda → nada. Si pasa a ≤1.5 m en
  tu piso → te detecta aunque no mire. A 1 m con una pared en medio (S4-d) → nada. **Bug si:** te ve
  a través de una pared o puerta cerrada.

**Oído** (quieto = silencio total)
- [ ] **G9. Rangos en abierto.** A su espalda en una sala grande (PASILLO_CARGA o el Hall). → Mirá
  «escucha un ruido» y `creencia`: caminando ≤10 m (a 11 no), corriendo ≤15 m, agachado ≤2.5 m.
- [ ] **G10. Por camino, no en línea recta.** Él en el PASILLO; vos corriendo por PASILLO_OESTE a su
  altura (8 m en línea recta, ~17 m por Link_Loop y dos paredes). → No te oye; cerca de la boca de
  Link_Loop sí. **Bug si:** te oye desde el medio de PASILLO_OESTE.
- [ ] **G11. A través del piso.** Vos en el cuarto de arriba de S1, él justo abajo. → Casi no te oye
  (×0.75 y ~20 m por la escalera); corriendo cerca de la puerta de arriba, sí. **Bug si:** te oye
  caminando desde justo abajo.

**Persecución y pérdida de vista**
- [ ] **G12. Persecución.** Hacete ver (G5 o F10 «Nemesis in front of the player»; no lo gira). →
  F9 `Chasing · free roam`, viñeta roja, gizmos «predicho»/«flanqueo». Caminando te alcanza;
  corriendo te le escapás. **Bug si:** acelera o se frena teniéndote a la vista.
- [ ] **G13. Gracia.** Cortale la vista y quedate quieto. → ~2.5 s de «lo perdió de vista recién» (el
  ruido la renueva) y después `Searching` («venía persiguiendo y todavía cree algo»). **Bug si:**
  suelta al instante o te persigue >5 s sin sentirte.

**Búsqueda y vuelta a patrulla**
- [ ] **G14. Búsqueda.** Escapate y quedate quieto. → F9 `búsqueda` alterna «interceptando a X m»
  (línea violeta), «barriendo habitación» y «buscando a X m»; en cada punto se para ~1.2 s mirando
  alrededor; a los ~15 s «nada que atender» y `seguro en ≈ 17–18 s`.
- [ ] **G15. Merodea.** → El primer cúmulo después de buscar es el de la zona donde te perdió; los
  re-sorteos siguientes (cada 12 s) ya son libres.

**Captura**
- [ ] **G16. Secuencia.** Dejate agarrar caminando. → `Catch` (franja roja) con «una captura en curso
  no se vuelve a decidir»; a los 1.5 s aparecés tirado en ENTRADA (no hay checkpoints); él quieto 4 s,
  se teletransporta a un spawn a ≥15 m, se levanta el fade y te parás. **Bug si:** te re-agarra al
  reaparecer o queda trabado en `Catch`.
- [ ] **G17. Después.** → `creencia: nunca lo sintió`, patrulla normal sin volver al lugar de la
  captura; 2 s sin poder volver a `Catch`.

**Trabas**
- [ ] **G18. Watchdog.** Subite al balcón de S3 con él viéndote y fijá `Chasing` (3). → A los ~3 s
  `[NemesisStuckEscape] No progress … Repathing` (`trabas: 1 recalculo`); ~1.5 s después `Still stuck
  after a repath … Warping out` (`1 warp`) a un waypoint fuera de tu vista. Soltalo con 0.
- [ ] **G19. Sesión larga.** Al terminar, mirá `trabas`. → Algún recalculo suelto y 0 warps sin haber
  fijado estados. **Bug si:** warps repetidos en el mismo rincón (bake o waypoint, no tuning).

**Zonas seguras**
- [ ] **G20. SALA_SEGURA** mientras te persigue. → No pasa del vano; a <1 m del vano te puede agarrar
  igual (limitación documentada), a ≥2 m adentro no; puede quedarse plantado en la puerta (C5).
  **Bug si:** entra.
- [ ] **G21. SALA_ROTA.** → **Sí entra** (roto a propósito). Si no entra, alguien cambió el bake.

**Montacargas** (arreglado el 27/09: el hueco ya no es caminable en el NavMesh estático, la cabina
llega al ras del piso alto, y al llegar suelta `Traversing`). En la consola **no** tiene que aparecer
`could not WALK aboard` ni `the boarding link at 'End' … joins NOTHING`.
- [ ] **G22. Que te siga.** Subí (E en la cabina) mientras te siente. → «para llegar hay que tomar el
  montacargas» → `Traversing` (verde); espera la cabina **quieto**; en el viaje `agente: apagado…`;
  al bajar sigue. Sin loop en `Traversing`. No hay panel de llamada y la cabina baja sola a los 12 s.
  **Bug si:** corre en el lugar esperando o abandona a mitad de camino.
- [ ] **G23. Viajar con él / bajar mientras espera.** En la cabina con él te puede agarrar; si le
  aparecés en su piso mientras espera, suelta el viaje y te persigue.
- [ ] **G26. Estado fijado durante el cruce.** Fijá un estado en F10 (p. ej. 4, Search) mientras
  cruza. → Termina el cruce y recién después obedece el pin. **Bug si:** va y viene entre la parada y
  la puerta de la cabina, o aparece del otro lado del hueco.
- [ ] **G27. Arriba, te oye abajo.** Que llegue arriba y te oiga abajo por el hueco. → Al llegar
  suelta `Traversing` y decide de nuevo: vuelve a bajar sólo si la ruta hacia vos sigue pasando por el
  montacargas. **Bug si:** queda 12 s en `Traversing` ("ya se comprometió con el montacargas").

**Audio y pausa**
- [ ] **G24. Audio.** → Cada estado con su loop y crossfade (Investigating comparte con Searching,
  Chasing con Catch); detrás de una pared ~×0.5 sin cortarse; pasos sólo si se mueve de verdad. En el
  testbed **no hay música de persecución** (sólo en Zona1).
- [ ] **G25. Pausa.** Esc en plena persecución. → Todo congelado; al volver sigue igual. **Bug si:**
  te detecta con el menú abierto.

## 3. Pruebas específicas

**S1 — WIR-028, escalera con puertas**
- [ ] **S1-a. Sube y baja solo** (WP_05 ↔ WP_06). → Abre la puerta de abajo, gira en el descanso
  alrededor del `Stair_Divider`, abre la de arriba y baja igual. **Bug si:** atraviesa el divisor,
  una pared o una hoja cerrada; se traba en el giro; o corre en el lugar frente a una puerta.
- [ ] **S1-b. Persecución por la escalera.** Hacete ver en el vestíbulo y subí corriendo. → Te sigue
  por las dos puertas **sin salir de `Chasing`** (una escalera no es otro piso). **Bug si:**
  `Traversing`.
- [ ] **S1-c. Con la losa de por medio.** Quieto arriba, justo encima de él. → No te ve, no te agarra
  (±1 m de altura), casi no te oye.

**S2 — WIR-028, pilares**
- [ ] **S2-a.** Varias pasadas de WP_01 ↔ WP_04. → Rodea cada par; nunca atraviesa un truss ni pasa
  por el hueco de ~1 m de un par (vale para los que tienen NavMeshObstacle y para SW). En la Scene
  view, sin NavMesh adentro de los trusses.
- [ ] **S2-b.** Mientras te persigue, pasá por el hueco de un par. → Él lo rodea. **Bug si:** lo
  atraviesa o corre pegado al truss.

**S3 — WIR-018 / WIR-024, balcón**
- [ ] **S3-a.** Que te persiga, subí la rampa y quedate quieto a la vista. → En ≤0.5 s deja de ser
  «lo está viendo» y `distancia` muestra `sin camino`; va al punto más cercano abajo, las piernas
  paran, `Searching` ~15 s y vuelve a `Patrolling`. **Bug si:** se queda en `Chasing` mirándote o
  sube la rampa.
- [ ] **S3-b.** Quieto arriba mientras patrulla el Hall. → A lo sumo un parpadeo de `Chasing` (el
  veredicto de camino se refresca cada 0.4 s), después `Searching` y `Patrolling`. **Bug si:** loop
  `Chasing` ↔ `Searching`.
- [ ] **S3-c. WIR-024 solo.** Con `Chasing` fijado (3), caminá de punta a punta del balcón; mirá el
  Animator de `Nemesis/mrZ` (`isRunning`). → Empujando contra el borde sin avanzar, `isRunning` pasa a
  false en 0.2–0.5 s y paran los pasos. **Bug si:** corre en el lugar.

**S4 — WIR-020, trigger en un vano**
- [ ] **S4-a.** Vos quieto en Room B (adentro del trigger) mirando al vano; él llega a Room A. → Te ve
  a través de la línea amarilla y te agarra en el borde.
- [ ] **S4-b.** Al revés (vos en A, él en B / WP_03). → Igual.
- [ ] **S4-c.** Caminá en Room B fuera de su vista, él a ≤8 m por camino. → Te oye como sin trigger.
- [ ] **S4-d. Pared fina.** Pegate detrás de un tramo de `Wall A|B` con él a ~1 m del otro lado. → No
  te detecta ni te agarra; da la vuelta por el vano.

**Señuelos y la elección (plan Fase 2B parte 4, casos 22–33 y 37)**
F9 fila `foco`: qué persigue (vos, una pista o un vistazo), cuánto vale, y la última decisión con su
pregunta, p. ej. `cambió: radio 0.36 > vos 0.28 [11]`. Antes, poner los señuelos (ver el mapa).
- [ ] **E1. Alarma sin que te sienta** (caso 25). → Va a revisarla y vuelve a patrullar; no te
  persigue. **Bug si:** arranca una persecución.
- [ ] **E2. Te ve y prendés la radio o la alarma** (casos 22 y 30). → Te sigue a vos. Al perderte
  busca primero donde te vio; si la alarma sigue sonando, a los ~7 s el foco pasa a ella
  (`cambió: alarma … > vos …`). **Bug si:** con la alarma sonando, termina la búsqueda y vuelve a
  patrullar sin ir nunca.
- [ ] **E3. Radio lejos con la creencia vieja** (caso 31). Hacete perder, quedate quieto ~10 s y que
  suene la radio. → Va y la rompe. Con sólo ~3 s de perderte, no va.
- [ ] **E4. Cadenas tres veces desde lejos** (caso 32). → La primera y la segunda viene; la tercera ya
  no (`ignoró cadenas … [7]`, "2 visitas vacías"). Después de cada visita vacía el foco vuelve a
  `vos` (`soltó cadenas: revisó: nada → vos`), no a `nada`.
- [ ] **E5. Cambia de idea** (caso 28). Mientras va a las cadenas, un paso suave del otro lado. → Se
  frena ~0.4 s girando hacia vos y va hacia el paso.
- [ ] **E6. Dos ruidos alternados** (caso 29). → No va y viene; termina lo que eligió.
- [ ] **E7. Señuelo donde te está buscando** (caso 33). → Sigue barriendo ahí (`siguió con vos … [5]`).
- [ ] **E8. Rompiendo la radio, te ve** (caso 37). → Corta y te persigue.
- [ ] **E9. Paso suave + vistazo** (caso 26). → La `sospecha` sube más rápido que con uno solo; un
  ruido solo nunca llega a 1, y tampoco la baja si ya estaba más arriba. Un vistazo por debajo del
  umbral no cambia el `foco`.

**Bajadas — Drop Lab (plan Fase 8, casos 12–16 y 55)**
F9 fila `bajada`. Con gizmos, el arco de cada bajada: verde la `Hop` y ámbar la `Hang`.
- [ ] **D1. Te persigue arriba y bajás por la rampa** (caso 12). Hacete ver en el entrepiso de 3.6 m y
  bajá por la rampa. → Va al borde, se asoma y gruñe, se da vuelta y se descuelga, aterriza y queda un
  momento abajo, y sigue persiguiendo. Igual con la `Hop` (salta, sin descolgarse). **Bug si:** baja
  por la rampa detrás tuyo mientras la bajada es más corta, o se queda en `Traversing` al aterrizar.
- [ ] **D2. Los dos abajo** (caso 13). → Nunca intenta subir por una bajada; sube por las rampas.
- [ ] **D3. Captura o respawn con él en el aire** (caso 14; forzalo con F10). → La bajada termina igual.
- [ ] **D4. Parado justo debajo del aterrizaje** (caso 15). → No te agarra en el aire.
- [ ] **D5. Patrullando, sin creencia** (caso 16). Seguilo por `Route_DropLab`. → Baja de los
  entrepisos por las rampas, no por las bajadas. **Bug si:** se tira patrullando.
- [ ] **D6. Llega al borde con vos arriba y a la vista** (caso 55). → Se echa atrás y te persigue
  arriba. F9 "— · 1 en enfriamiento"; por 8 s usa la rampa.
- [ ] **D7. Barandas.** Caminá por el borde de los entrepisos y de las rampas. → Una baranda invisible
  te frena; a él no. **Bug si:** te caés o él choca contra algo en el borde.

**WIR-020, collider sólido en la capa de ruido**
- [ ] **W20.** En Play, creá un Cube en la capa `DetectableAudio` sobre su camino en el PASILLO. →
  Tira **un** `[FieldOfListening] 'Cube' is a SOLID collider…` y no va a investigarlo. **Bug si:**
  entra en `Investigating` hacia el cubo.

**WIR-006 / DIS-002, investigar un ruido**
- [ ] **I-a. Va al punto, no te sigue.** Caminá 1–2 s fuera de su vista a ≤8 m por camino y seguí
  despacio cerca. → «escucha un ruido» y camina (sin viñeta) al origen; el destino sólo salta si el
  ruido nuevo cae a ≥3 m y pasaron ≥1.5 s. **Bug si:** te sigue pegado como en una persecución.
- [ ] **I-b. Inspecciona.** Un ruido corto y quieto. → Al llegar, «revisa donde escuchó el ruido»
  ~4 s barriendo la mirada, después «nada que atender». **Bug si:** se va apenas llega o se queda >8 s.
- [ ] **I-c. Ruido pegado.** Agachado a 1–2 m detrás de él y pará. → Igual inspecciona ~4 s, no ~1 s.

**Fase 4, persecución estancada**
- [ ] **E-a. Loop en `Column_Loop`.** Corré en círculos sin salir de su vista. → A los ~4 s F9
  `ESTANCADO … · N ChaseStalled`, consola `[NemesisChaseProgress] Chase stalled: closed … of the 1.50 m
  it needed in 4.0 s`, gizmo «persecución estancada»; la velocidad sigue en 3. **Bug si:** nunca lo
  marca o acelera.
- [ ] **E-b. ¿Corta por el otro lado?** Mirá `rastro: N waypoints penalizados` y el cubo «flanqueo».
  En ENTRADA sólo hay WP_00 cerca, así que `0 penalizados` es falta de waypoints, no bug. Repetilo
  alrededor de `Cover_Pasillo`.
- [ ] **E-c. Se destraba.** Caminá derecho. → `acortó +x / 1.5 m` y desaparece ESTANCADO; si te pierde
  >2.5 s, «sin medir».

**Prioridad de habitación**
- [ ] **R-a.** Que te persiga por PASILLO_OESTE; metete en SALA_LATERAL y **pará en seco** detrás de la
  pared (si seguís corriendo, la creencia pasa a ser de oído y no se compromete). → `barriendo
  habitación r 8 m…` y los primeros cubitos violetas **adentro** de SALA_LATERAL. **Bug si:** sigue
  de largo o barre el pasillo primero.
- [ ] **R-b.** Un ruido corto adentro de la sala mientras barre. → Sigue barriendo la misma sala.

**Director** (todo desde F10, presión 1.0 durante 90 s)
- [ ] **D-a. Pedido.** `Pressure: carga`. → `[NemesisDirector] Pressure on 'carga' at 1.00 for 90s.`,
  botón `■ carga (1.00)`, esfera de la zona de gris a roja.
- [ ] **D-b. Palanca 4, sentidos.** → Labels de `view 7 m` a `8.8 m` y `hearing cap 15 m` a `18.8 m`;
  con `Release` vuelven. El asset `SO_NemesisData` queda en 7/15 siempre. **Bug si:** cambió el asset.
- [ ] **D-c. Palanca 1, ancla.** Al fondo de SALA_SEGURA (tu posición no lo atrae), mirá sus cúmulos
  1 min; después `planta alta` o `carga`. → En 1–2 re-sorteos (12 s) se inclina hacia la zona; con
  `planta alta` toma el montacargas. Es un sesgo.
- [ ] **D-d. Palanca 3, ruido.** Él cerca de la zona, vos lejos y quieto. → Cada 9 s (el primero a los
  ~9–12 s) aparece `DirectorNoise (carga)` 0.8 s; si él está a ≤10 m por camino, «escucha un ruido»,
  va e inspecciona. Si vos caminás a ~6 m de él al mismo tiempo, te oye **siempre** (WIR-020). **Bug
  si:** nunca reacciona estando cerca, o a igual distancia a veces te oye y a veces no.
- [ ] **D-e. Reemplazo y fin.** `carga` y después `entrada`. → Una sola zona roja; `Release` limpia al
  toque; sin release se corta a los ~90–93 s; desactivar el GameObject `Director` en Play también
  restaura todo.
- [ ] **D-f. Entrada en escena.** En el medio del PASILLO, `Staged entrance`. → Aparece fuera de tu
  vista, a 10–22 m por NavMesh, mirándote; quieto 2.5 s (`agente: listo · watchdog suprimido`) y
  recién ahí se mueve. **Bug si:** lo ves aparecer, aparece a <10 m o se mueve antes.
- [ ] **D-g. Casos límite.** Desde el fondo de SALA_SEGURA: `No spot to make an entrance…` o aparece
  afuera, **nunca** adentro. Con él dormido o arriba del montacargas: no hace nada y no loguea.

**Escondites (Fase 2)**
- [ ] **H-0 (en el testbed). Proximidad plana con F10 `Hide`.** Parate en su ruta, p. ej. (0, 12) en
  el PASILLO, y apretá `Hide` (inmóvil y sin ruido). → Pasa a >1.5 m sin verte aunque te mire de
  frente; a ≤1.5 m, en tu piso y sin pared, te detecta al toque (sin escondite, cuenta como que te ve).
  **Bug si:** te ve a 3 m o no te detecta pegado.

Lo que sigue va en el **Hiding Lab** de la testbed (los ids entre paréntesis son los de ahí; en
`TestIñaki.unity` → "Hiding Test Area" siguen los `test_*` de antes).
E entra y sale; mantener F aguanta la respiración.
- [ ] **H-1 (caso 1, `tb_locker_vista`; en TestIñaki `test_locker_a`).** Con él persiguiéndote, metete mientras te ve. → F9
  `escondite: sabe tb_locker_vista (lo vio entrar) · yendo → revisando`, regla «sabe en qué escondite
  está»; va al ApproachPoint, `Catch`, ~0.8 s quieto en la puerta y te saca: **aparecés afuera, en la
  ExitPose**. **Bug si:** se queda plantado frente al locker en `Chasing`, o va a donde te vio por
  última vez.
- [ ] **H-2 (caso 2, `tb_locker_vuelta`; en TestIñaki `test_locker_b`).** Cortale la vista y metete cuando pasaron >0.75 s sin que te
  vea. → `escondite: —` (tampoco «sospecha», aunque el medidor siga bajando); barre y se va a los
  ~15 s. Ojo: WP_02 (en TestIñaki, WP 05/06) pasa a ~1.4 m del locker; si después te detecta por proximidad es el caso 3.
- [ ] **H-3 (caso 3).** Adentro de `tb_locker_vuelta`, que pase por WP_02 (TestIñaki: `test_locker_b`, WP 05/06). → `sabe … (lo tiene
  encima)` **sin** `Chasing`, va a la puerta y te saca.
- [ ] **H-4 (caso 4, mesa).** Con él mirando de frente: a ~5 m **no pasa nada** (bajo la mesa ve
  7 × 0.5 = 3.5 m); a ≤3.5 m sube `lo distingue por tb_mesa_1` **sin** `lo ve` ni `Chasing`; al pasar
  0.4, «sospecha de un escondite» y va a mirar; a 1, `sabe…` y te saca. **Bug si:** persecución
  instantánea desde la mesa.
- [ ] **H-5 (caso 5).** Escondido, mantené F y soltala con él a ~4 m. → «escucha un ruido» →
  `Investigating` hacia el escondite, **no** `Chasing`.
- [ ] **H-6 (caso 11).** Dejate capturar adentro o F10 `Capture the player`. → Después de reaparecer:
  F10 `Hidden: False`, pasos normales, cámara normal, te parás, `escondite: —`.
- [ ] **H-7 (olvido).** → `escondite` se limpia al revisar y encontrar vacío, al verte afuera, o sin
  confirmar por 15 s si lo sabía (8 s si sospechaba). Mientras te sigue distinguiendo por las rendijas
  **no** lo olvida.
- [ ] **H-8 (caso 17, de reojo).** Que te tenga en la periferia (medidor subiendo, sin llegar a 1)
  mientras subís al locker. → `sospecha … (lo vio de reojo al entrar)` → `Investigating` camina a la
  puerta y mira ~4 s; si seguís adentro te detecta al llegar y te saca; si saliste antes, lo da por
  revisado.
- [ ] **H-9 (caso 18, rendijas).** En `tb_locker_vista` (WP_01, ~2 m) o la fila del Vestuario (WP_07, ~2.8 m), que pase de frente a 2–3.5 m. → `lo distingue
  por …` → sospecha → va a mirar → te saca. Que pase **por detrás** del locker a 2–3 m: nada. En el
  container: nada salvo que pase a ≤1.5 m.
- [ ] **H-10 (caso 19–20).** Al sacarte, aparecés en la ExitPose, no adentro del mueble ni despedido
  por la física. Si pasa a ≤1 m de un locker ocupado **con una pared en medio**, nada: metete en
  `tb_fila_2` y esperá a que WP_09 lo pase por el Pasillo Norte, del otro lado de la pared.

## Huecos del testbed

- ~~**No hay ningún `HidingSpot`.**~~ **Resuelto el 29/09** con el *Hiding Lab* (ver el mapa). El
  collider del container y de los lockers queda en `Default`, que el testbed no hornea: cada escondite
  lleva un `NavMeshModifierVolume` Not Walkable en `Props` del tamaño del mueble.
- **Director por puzzles:** los disparadores no se pueden probar en ningún lado. En el testbed están
  vacíos y no hay puzzles; **en Zona1 el Director de la Fase 0 se perdió en el merge `16b1962c`**
  (hoy apagado, 0 zonas, 0 disparadores; Plan §14.1). La Fase 5 (tensión, ritmo, retirada) no existe.
- ~~**La palanca 2 del Director (pesos de ruta) no se ve** con la patrulla por cúmulos: el sorteo usa
  `Cluster.Weight`, que se congela al armar el grafo.~~ **Arreglado el 27/09 (plan Fase 2B parte
  3):** el sorteo lee el peso vivo de las rutas de cada cúmulo (`NemesisRouteGraph.ClusterWeight`).
  El peso se ve cambiar en la etiqueta del cúmulo activo.
- No hay música de persecución; el montacargas no tiene `ElevatorCallPanel` y baja solo a los 12 s
  (si quedás varado arriba, F10).
- `NemesisRooms` toma Room A y Room B de S4 como una sola habitación (y el vestíbulo y el cuarto de
  arriba de S1): la prioridad de habitación se prueba en las salas del testbed principal.
- `Column_Loop` no tiene waypoints alrededor: sumá 2–3 o usá `Cover_Pasillo` para E-b.
- WIR-028: el modifier de las puertas (en `Hinge`) y el divisor en `Wall` sólo importan donde se
  hornea `Default` (Zona1).
- `Route_Oeste/WP_01` y `Spawn_Lateral` están en (−16, 0, 20), **adentro de `Column_Lateral_1`**.
- `Crate (ignoreFromBuild)` lo atraviesa a propósito: no es regresión.
- F9 no muestra la velocidad neta: para WIR-024 mirá el Animator.
- Bajadas entre pisos (casos 12–16) y hábitos (6–10) no existen todavía.
