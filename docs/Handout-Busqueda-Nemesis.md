# Handout — Búsqueda del Nemesis

> 04/10/2026, 12:05. Estado del trabajo sobre `Plan-Busqueda-Nemesis.md`.
> Nada está commiteado ni jugado. La tanda 2, la revisión y la documentación todavía están corriendo:
> este archivo se actualiza cuando terminen.

---

## Estado

| Parte | Estado |
|---|---|
| Fase 1 y 2a | Commiteadas desde antes (`00d97c61`) |
| Búsqueda por mapa (2b + 2c) | Escrita hoy, sin jugar |
| Vista adaptativa y esquinas | Escrita hoy, sin jugar |
| Contador de "estanca" | Escrito hoy, sin jugar |
| Escondites en el mapa y patrulla después de la caza (2d + 2e) | Corriendo |
| Revisión del cambio y documentación | Después de lo anterior |
| Fase 3 (montacargas, pisos) y titileo Chasing ↔ Searching | Fuera: es de otro integrante |

Las tres partes escritas hoy compilaron en Unity batch a las 12:01 sin errores, y pasan los 247 tests
EditMode.

---

## Qué cambió en el juego

- **Búsqueda.** Ya no barre un disco. Va adonde el mapa de posibilidades todavía tiene valor, sorteando
  entre hasta 8 lugares por valor ÷ tiempo de llegada. Al entrar desde una persecución no se queda
  1,2 s parado. Si ve de lejos que el lugar está vacío, elige otro sin caminar hasta ahí.
- **Vista.** Te nota a 7 m como antes. Una vez que te vio, te sostiene hasta 14 m. Si te pierde
  cazándote, el rango para volver a verte crece de 7 a 14 m en 2 s. Un ruido solo no lo agranda.
- **Esquinas.** Al perderte mira 6 m más allá en el rumbo que te vio llevar, así llega mirando el
  pasillo y no la pared.
- **Bug en rectas (arreglado).** Con la persecución "estancada", el desvío anti-loop lo mandaba al
  waypoint que tenía bajo los pies y se quedaba parado viéndote irte.
- **Bug del sensor (arreglado).** A menos de 2,3 m de frente te tomaba como "de reojo" porque medía
  los pies. Ahora te ve al instante, y eso cambia el sigilo de cerca.
- **"Estanca".** Suma 1 por persecución, no 1 cada 4 s. Un salto de más de 10 m por NavMesh no cuenta.

---

## Lo que tenés que hacer vos

1. No abrir Unity hasta que termine el workflow: compila en batch y necesita el editor cerrado.
2. Abrir `SO_NemesisData` y guardarlo una vez, para que los campos nuevos queden en el asset. Son 4 de
   mapa de búsqueda, 6 de visión y 2 de persecución, más los que agregue la tanda 2.
3. En el prefab `Nemesis`, prender `Draw Possibility Map`; `Draw Search Pick` ya queda prendido.
4. Jugar en la testbed con F9:
   - **Recta:** dejate ver a 6 m y corré. Tiene que seguir en Chasing hasta unos 14 m y no frenarse
     cuando F9 diga ESTANCADO.
   - **Esquina:** doblá a 4 o 5 m y seguí corriendo. Tiene que llegar y salir sin quedarse quieto, y
     volver a verte si seguís en el pasillo.
   - **Caso 62:** pasillo con una sola salida, 10 veces. Nunca vuelve para atrás.
   - **Caso 63:** T, ibas hacia la izquierda, 10 veces. Izquierda la mayoría, nunca por donde vino.
   - **Caso 65:** correr hasta un locker dentro de su oído. Busca la zona, no va a la puerta.
   - **Caso 67:** meterte al Hub en plena búsqueda. No acampa la puerta.
   - **Sigilo:** patrullando te sigue notando a 7 m parado y 3,5 m agachado.
   - **"Estanca":** una persecución larga alrededor de una mesa suma 1.
5. Revisar `git status` antes de commitear: fuentes TMP, dos materiales de niebla, una nota y scripts
   de UI estaban modificados y no son de este trabajo.

---

## Decisiones que quedaron tomadas por defecto

| Decisión | Lo que quedó | Cómo cambiarlo |
|---|---|---|
| D38: escondites por el mapa | Abre un escondite cuando el mapa junta suficiente valor ahí, multiplicado por cuánto lo usaste. Es la recomendación del plan; falta confirmarla | Tunables de la tanda 2 |
| Números de vista | Sostener ×2, cazar ×2 en 2 s. Sin tope al apilarse con la escalada y el Director: puede pasar los 20 m | `View Hold Scale`, `View Hunt Scale`, `View Hunt Grow Time` (en 1 se apagan) |
| "Lo vio entrar" | Vale hasta el rango con el que te estaba viendo (14 m en persecución). Esconderte a la vista en un pasillo largo deja de servir | — |
| Búsqueda en espacios abiertos | Casi no se detiene a mirar, porque descarta lugares de lejos | `Search Map Repick Share` en 0 vuelve al ritmo de antes |
| "Revisé todo" | Umbral 0,015: en un hall abierto la búsqueda termina a los 6–9 s; en pasillo y sala, 10–13 s | `Search Map Worth Threshold` |
| "Estanca" | Cuenta en la primera ventana sin progreso, aunque sea una corrida recta y no un loop | — |
| Foco de cerca | A menos de 2,3 m de frente te ve al instante | Revertir la llamada en `FieldOfView` y `LineOfSight.CheckConeSampled` |
| Titileo | Sacado | Aplicar `Logs/fase3-descartada/titileo.patch` |

---

## Queda por hacer

### De otro integrante

- Los cuatro arreglos del loop del montacargas (docx del 30/09).
- El "asomarse" antes de cambiar de piso (Fase 3, caso 66).
- El titileo Chasing ↔ Searching.

Material en `Logs/fase3-descartada/` (fuera de git): `fase3-archivos-propios.patch`,
`fase3-archivos-mezclados.patch`, `titileo.patch` y cinco archivos en `nuevos/`. Son referencia; no
compilan solos.

### Abierto, sin dueño

- Las tres decisiones del docx: si la corrida abierta siempre se escapa (hoy sí, pero tarda unos 10 s
  en vez de 2 o 3), el PeakFade de 47 s y el hábito desde el primer escape.
- La cabeza del modelo no gira con la mirada, así que no se ve hacia dónde mira en la esquina. Es
  trabajo de rig.
- `NemesisTraceRecorder` no graba rango efectivo, agachado ni causa de la pérdida de vista.
- Con el jugador a la vista pero inalcanzable, la búsqueda se queda quieta mirando.
- Comentarios desactualizados en archivos ajenos; por ejemplo, `NemesisTestbedHidingLabBuilder`
  nombra `NemesisRooms`, que ya no existe.

---

## Dónde está cada cosa

- Reportes completos de los agentes, con todas sus preguntas: `Logs/nemesis-tanda1-reportes.json`.
- Resultado de la compilación: `Logs/nemesis-batch.log` y `Logs/nemesis-batch-tests.xml`.
- Código nuevo principal: `NemesisSearchPicker.cs`, `Logic/SearchPickRules.cs`,
  `Logic/AdaptiveViewRange.cs`, `Logic/ChaseGaze.cs`, `Logic/ChaseStallCounter.cs`,
  `Logic/ChaseGapWindow.cs`.
- Borrados: `NemesisFreeRoam.cs`, `NemesisRooms.cs`, `Logic/SearchSweepRules.cs` y sus tests.
