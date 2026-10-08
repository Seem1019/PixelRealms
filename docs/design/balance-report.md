# Informe de balance de la Fase 1 (2026-09-30)

Primera pasada de números de los 16 hechizos de la Fase 1 (niveles 1–5), hecha por el `content-designer` con el método
de [class-kits.md](class-kits.md) (ADR-020). Son valores **esperados** de un modelo en Python (`tools/balance/`), sin azar
ni movimiento; HU-084 los contrasta con partidas reales.

Para repetirlo: `python tools/balance/check.py` (pentagrama y solitario) y `python tools/balance/boss.py` (jefe). Ambos
leen `content/` directamente.

## Qué simula el modelo
- 30 s de combate a nivel 6 con el equipo verde de su nivel y el arma de referencia de cada clase.
- Recursos (energía, ira, maná con el maná por golpe), cooldown global de 1 s, bloqueo de animación de 250 ms, pausa del
  básico al castear.
- Contra un objetivo de nivel 6 con 35 de armadura (entre el Kóbold y el Gólem).

## Decisiones de medición (aprobadas)
1. **Un hechizo de área cuenta entero en la punta de área**, también el daño a su objetivo principal. Si no, cada área
   sumaba de rebote ~60 % de su valor en mono-objetivo.
2. **Valores de referencia fijos** (`rules.balanceTargets.pentagram.references`), 100 puntos = :

   | Punta | Referencia a nivel 6 |
   |---|---|
   | Mono-objetivo | 26,6 de daño por segundo (el básico del Pícaro vale 30) |
   | Área | 63,8 (4 objetivos × 0,6 × mono) |
   | Control | 40 s equivalentes de control por minuto |
   | Movilidad | 120 casillas extra por minuto |
   | Armadura | 88,2 s de aguante (el del Guerrero vale 70) |

3. **El atacante de referencia pega físico**, porque todos los monstruos del Tier 1 pegan físico.
4. En el Sacerdote, las curas y los escudos a aliados cuentan en mono-objetivo (o en área si son de área), no en armadura.

## Números de la Fase 1
Con el pentagrama, los hechizos aportan tanto como el básico del arma. Por eso el daño y la curación bajaron a ~⅓ de los
valores provisionales y el maná, en proporción.

| Clase | Hechizo | Números |
|---|---|---|
| Pícaro | Golpe siniestro | 3 + 0,2 AP + 0,15 arma, cooldown 3 s |
| | Gubia | 10 de daño, aturde 2 s, cooldown 25 s |
| | Paso sombrío | +60 % de daño 3 s, ralentiza 0,5 s, cooldown 10 s |
| | Carrera | +50 % de velocidad 6 s, cooldown 40 s |
| Mago | Bola de fuego | 7 + 0,3 SP, casteo 2 s, 8 de maná |
| | Descarga de escarcha | 5 + 0,3 SP, ralentiza, cooldown 12 s, 8 de maná |
| | Nova de escarcha | 4 + 0,15 SP, raíz 3 s, cooldown 18 s, 15 de maná |
| | Estallido de llamas | 20 + 0,55 SP, casteo 1,5 s, 25 de maná |
| Guerrero | Golpe heroico | 2 + 0,1 AP + 0,1 arma, cooldown 3 s |
| | Provocar | 2 s, cooldown 10 s |
| | Carga | aturde 1,5 s, cooldown 16 s |
| | Torbellino | 8 + 0,4 AP + 0,6 arma |
| Sacerdote | Sanar | 5 + 0,2 SP (~12 por casteo), 6 de maná |
| | Castigo | 7 + 0,3 SP, sin ralentización, 6 de maná |
| | Palabra de poder: Escudo | +20 % de velocidad 4 s, cooldown 15 s, 20 de maná |
| | Pulso sagrado | cura 20 + 0,65 SP, ralentiza 1,5 s, 20 de maná |

- **Castigo pierde la ralentización:** como se lanza sin parar, dejaba al objetivo ralentizado todo el tiempo y valía 22
  puntos de control. Su control pasa a Pulso sagrado.
- **Sanar** quedó en 5 + 0,2 SP y 6 de maná. Con 7 + 0,28 y 8 de maná (lo que se propuso primero) ya no se quedaba sin
  maná en 30 s y valía 44 puntos, por encima del máximo de 40 por habilidad.
- **Fases 2 y 3:** los 16 hechizos se escalaron a la misma escala (daño ×0,35, cura ×0,3, maná ×0,4 con mínimo 5) y siguen
  con `"provisional": true` hasta su pasada al nivel 15.

## Resultado: pentagrama de la Fase 1 (nivel 6)
Entre paréntesis, el objetivo de la Fase 1.

| Clase | Mono | Área | Control | Movilidad | Armadura |
|---|---|---|---|---|---|
| Pícaro | 57 (60) | 0 (0) | 20 (20) | 50 (50) | 32 (30) |
| Mago | 41 (45) | 34 (35) | 45 (45) | 0 (0) | 23 (25) |
| Guerrero | 35 (35) | 21 (20) | 29 (30) | 16 (15) | 70 (70) |
| Sacerdote | 51 (50) | 29 (30) | 10 (15) | 11 (10) | 29 (30) |

- **Regla 40/75:** se cumple. La habilidad que más suma es Pulso sagrado (39) y la combinación más cargada, la del
  Guerrero (170 de 187).
- **Armadura del Pícaro y del Mago:** no bajaba a su objetivo original (25 y 15) sin dejarlos por debajo del piso de
  supervivencia en solitario; el pentagrama se ajustó a Pícaro armadura 30 / movilidad 85 y Mago armadura 25 / control 70.

## Solitario: nivel 5 contra Kóbold minero
160 de vida, 30 de armadura, 6–10 de daño cada 2,2 s; con 10 s de caminata entre monstruos.

| Clase | Mata en | Vida perdida | Descanso | Ciclo por monstruo | XP por hora | Solo con básicos |
|---|---|---|---|---|---|---|
| Pícaro | 12,5 s | 17 % | 10,8 s | 33,3 s | referencia | 21 s, 29 % de vida |
| Guerrero | 15 s | 9 % | 9,8 s | 34,8 s | −4 % | 26 s, 16 % |
| Sacerdote | 16,5 s | 24 % | 9,5 s | 36 s | −8 % | 34 s, 50 % |
| Mago | 11,5 s | 22 % | 15,6 s | 37,1 s | −10 % | 34 s, **65 %** |

- **XP por hora:** todas las clases dentro del ±15 % (`soloXpPerHourSpreadPct`).
- **Ciclo real ~36 s** (33–37 s), no 30: `killCycleSecTarget` pasó a 36 para que la curva siga en ~25 h.
- **Mago solo con básicos:** bajar el daño del Kóbold de 7–11 a 6–10 lo dejó en 65 % de vida perdida (antes 73 %). Sigue
  por encima del 50 %; el modelo no cuenta que el Mago se aleje mientras castea. Vigilar en HU-084; la siguiente palanca
  es +10 de vida base del Mago.

## Jefe: Capataz Grask
Con 2 000 de vida los grupos tardaban 117–137 s; la vida bajó a **1 400**. Resultados en
[balance-notes.md](balance-notes.md) §4: 85 s con 3 de nivel 4 y sanador, 54 s sin sanador (con pociones), 57 s con
Pícaro + Mago de nivel 6 y 101 s con Guerrero + Sacerdote de nivel 6.

## Pasada de HU-084 (2026-10-03)
Hecha con el mismo modelo (`tools/balance/model.py`, sin modificar) más un simulador de duelos con movimiento. Los scripts
de esta pasada no están en el repo: reutilizan `model.Char`, `simulate` y `basic_hit`. Valores esperados, no partidas reales:
HU-084 CA3 y CA4 siguen pidiendo contrastarlos jugando.

### 1. Stats por clase y nivel
Equipo inicial al nivel 1 y equipo verde esperado (`REF_GEAR` del modelo, filtrado por `levelReq`) al 5 y al 6. DPS de 30 s
contra el monstruo normal de su nivel (al 6, el objetivo de referencia: armadura 35). Vida efectiva = vida ÷ (1 − mitigación)
÷ (1 − esquiva), contra golpes físicos de su nivel. El nivel 10 no se mide: no hay equipo ni hechizos medidos de la Fase 2.

| Clase | Nv | Vida | Recurso | AP | SP | Armadura | Mitig. | Esquiva | Crit F/M | Swing | DPS rotación | DPS básico | Vida efectiva |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Pícaro | 1 | 140 | 100 energía | 35 | 10 | 20 | 14 % | 5,6 % | 9 / 6 % | 1,39 s | 7,7 | 4,4 | 173 |
| Pícaro | 5 | 190 | 100 energía | 71 | 19 | 43 | 18 % | 8,6 % | 13 / 6 % | 1,39 s | 13,2 | 7,5 | 253 |
| Pícaro | 6 | 200 | 100 energía | 76 | 20 | 45 | 17 % | 9,0 % | 14 / 6 % | 1,39 s | 13,7 | 7,8 | 265 |
| Mago | 1 | 120 | 180 maná | 25 | 22 | 7 | 6 % | 4,0 % | 6 / 10 % | 3,33 s | 6,5 | 2,4 | 133 |
| Mago | 5 | 160 | 276 maná | 43 | 44 | 13 | 6 % | 4,8 % | 8 / 13 % | 3,33 s | 12,7 | 4,5 | 179 |
| Mago | 6 | 170 | 292 maná | 46 | 48 | 14 | 6 % | 5,0 % | 8 / 14 % | 3,33 s | 13,3 | 4,7 | 190 |
| Guerrero | 1 | 204 | 100 ira | 28 | 10 | 41 | 25 % | 4,4 % | 7 / 6 % | 2,40 s | 5,3 | 3,6 | 286 |
| Guerrero | 5 | 384 | 100 ira | 61 | 18 | 77 | 28 % | 5,6 % | 9 / 6 % | 2,40 s | 11,8 | 6,4 | 563 |
| Guerrero | 6 | 408 | 100 ira | 66 | 20 | 78 | 26 % | 5,8 % | 9 / 6 % | 2,40 s | 12,2 | 6,6 | 587 |
| Sacerdote | 1 | 135 | 151 maná | 21 | 16 | 7 | 5 % | 3,8 % | 6 / 9 % | 2,22 s | 2,4 | 2,4 | 148 |
| Sacerdote | 5 | 205 | 247 maná | 39 | 34 | 18 | 8 % | 3,8 % | 6 / 12 % | 2,22 s | 10,9 | 4,8 | 233 |
| Sacerdote | 6 | 215 | 263 maná | 42 | 37 | 18 | 8 % | 3,8 % | 6 / 13 % | 2,22 s | 11,4 | 5,0 | 242 |

HPS del Sacerdote lanzando Sanar sin parar (con Escudo desde el nivel 3): 5,4 al nivel 1 (4,9 de media en 60 s, se queda
sin maná), 12,7 al 5 y 13,2 al 6 (igual en 60 s).

### 2. Solitario contra el monstruo normal de su nivel
Rotación / solo básicos: tiempo en matarlo · vida perdida. En negrita, por encima del piso del 50 %.

| Monstruo | Nv | Pícaro | Mago | Guerrero | Sacerdote |
|---|---|---|---|---|---|
| Slime | 1 | 6 s · 5 % / 10 s · 8 % | 6 s · 7 % / 17 s · 18 % | 9 s · 4 % / 12 s · 6 % | 18 s · 17 % / 18 s · 17 % |
| Jabalí | 2 | 7 s · 7 % / 16 s · 16 % | 10 s · 16 % / 27 s · 40 % | 14 s · 10 % / 20 s · 13 % | 10 s · 13 % / 27 s · 37 % |
| Bandido | 3 | 10 s · 14 % / 21 s · 29 % | 14 s · 29 % / 30 s · **60 %** | 20 s · 18 % / 26 s · 24 % | 12 s · 23 % / 34 s · **61 %** |
| Lobo de las colinas | 4 | 10 s · 18 % / 18 s · 32 % | 14 s · 37 % / 30 s · **77 %** | 17 s · 19 % / 24 s · 27 % | 12 s · 29 % / 29 s · **68 %** |
| Goblin arquero (`hard`) | 5 | 10 s · 17 % / 18 s · 32 % | 12 s · 28 % / 30 s · **75 %** | 12 s · 10 % / 22 s · 17 % | 13 s · 25 % / 29 s · **55 %** |
| Kóbold minero | 5 | 12 s · 17 % / 21 s · 29 % | 12 s · 22 % / 34 s · **65 %** | 15 s · 9 % / 26 s · 16 % | 16 s · 24 % / 34 s · 50 % |

- **Piso de viabilidad:** con la rotación nadie pasa del 37 %. Solo con básicos, el Mago pasa del 50 % del nivel 3 al 5
  (60–77 %) y el Sacerdote en los niveles 3 y 4 (61–68 %). Es el pendiente "Mago solo con básicos" de CA4, más amplio de lo
  que decía el Kóbold: **no se cambia** hasta jugarlo. El modelo no cuenta que ambos pegan a distancia (bastón 5, varita 7)
  y pueden alejarse; +10 de vida base del Mago (la primera palanca) solo baja el peor caso de 77 % a ~72 %.
- **Ciclo por monstruo** (pelea + descanso + 10 s de caminata): 24–43 s; en los niveles 1–2 entre 24 y 35 s, más rápido que
  el objetivo de 36 s (los primeros niveles llegan antes, como pide el pilar 2). Con el Kóbold, 33–37 s: `killCycleSecTarget`
  sigue en 36 (pendiente de CA4).

### 2b. Afinidad (piso de viabilidad, `rules.balanceTargets`)
Nivel 5, equipo verde, solo básicos contra el Goblin arquero (nv 5). "Con Disparo" suma el hechizo del goblin (9 de daño cada
~4 s); la tabla de [combat.md](combat.md) §Referencia lo ignoraba. Daño fuera de rol en % del Pícaro con daga; aguante en %
del Guerrero con espada, escudo y malla.

| Combinación | Vida | DPS básico | % del Pícaro | Mata en | Le matan en (básico / con Disparo) | % del Guerrero | Vida perdida (básico / con Disparo) |
|---|---|---|---|---|---|---|---|
| Pícaro + daga | 190 | 8,1 | 100 % | 17 s | 84 / 57 s | 45 % | 21 / 30 % |
| Pícaro + espada | 200 | 8,0 | 99 % | 17 s | 87 / 60 s | 47 % | 20 / 29 % |
| Guerrero + espada + escudo + malla | 384 | 6,9 | 86 % | 20 s | 186 / 128 s | 100 % | 11 / 16 % |
| Mago + bastón | 160 | 5,1 | 63 % | 27 s | 59 / 41 s | 32 % | 46 / 68 % |
| Sacerdote + varita | 205 | 5,0 | 62 % | 28 s | 77 / 53 s | 41 % | 36 / 53 % |
| Sacerdote + maza | 205 | 4,4 | 55 % | 32 s | 77 / 53 s | 41 % | 41 / 60 % |
| Sacerdote + espada (media) | 214 | 4,7 | **58 %** | 30 s | 80 / 55 s | 43 % | 37 / 54 % |
| Mago + daga (media) | 160 | 4,8 | **59 %** | 29 s | 60 / 41 s | 32 % | 49 / 72 % |
| Guerrero + bastón (baja) | 372 | 2,9 | **37 %** | 48 s | 168 / 115 s | 90 % | 28 / 41 % |
| Sacerdote + espada + placas/malla/escudo | 234 | 4,6 | 57 % | 31 s | 100 / 69 s | **54 %** | 31 / 45 % |
| Pícaro + espada + placas/malla/escudo | 237 | 7,7 | 96 % | 18 s | 112 / 77 s | **60 %** | 16 / 24 % |
| Mago + espada + placas/malla/escudo (baja) | 209 | 4,3 | **54 %** | 32 s | 88 / 60 s | **47 %** | 37 / 53 % |

- **Daño fuera de rol (55–65 %):** Sacerdote con espada 58 % y Mago con daga 59 %, dentro. Mago con espada y placas 54 %
  (doble penalización, ya aceptada en [balance-notes.md](balance-notes.md) §3; palanca: `affinity.multipliers.baja` 0,75).
  **Nuevo:** el Guerrero (y el Pícaro) con armas de `int` rinde el 37–46 %, porque su `sp` sale de stats que casi no tiene.
  Sobrevive (41 % de vida perdida), así que no rompe el piso; ver la propuesta 3 de §Desviaciones.
- **Aguante fuera de rol (50–60 %):** Sacerdote pesado 54 %, Pícaro pesado 60 %. **Mago pesado 47 %** (antes 49 %: el
  Guerrero de referencia lleva ahora yelmo y calzas). Es el pendiente de CA4 (+10 de vida base del Mago): no se cambia.
- **Escenario del test HU-033 CA3b** (las cuatro clases con `iron_sword` + `recruit_mail_shirt`): Sacerdote 61 % del daño
  del Pícaro, Mago 51 % del aguante del Guerrero. Dentro de margen.
- Con el Disparo, el goblin quita un 45 % más: Mago con bastón 68 % y Sacerdote con maza 60 %. El goblin es `hard`, no
  normal, así que no entra en el piso; pero `combat.md` §Referencia infravalora su daño.

### 2b2. Pentagrama
Sin cambios: esta pasada no toca hechizos ni auras. `check.py` repite la tabla de arriba (Pícaro 57/0/20/50/32, Mago
41/34/45/0/23, Guerrero 35/21/29/16/70, Sacerdote 51/29/10/11/29); regla 40/75 cumplida (Pulso sagrado 39, combinación
máxima 170 de 187). Parejas que se potencian y el modelo no ve:
- **Nova de escarcha + Estallido de llamas:** con la raíz de 3 s el área (casteo 1,5 s) acierta siempre; sin raíz, un
  objetivo que ve la marca la esquiva. En el simulador de duelos es la diferencia entre acertarla ~50 % y siempre.
- **Gubia + Paso sombrío:** 2 s de aturdimiento dentro de los 3 s de +60 % de daño.
- **Carga + Torbellino:** el aturdimiento de 1,5 s deja el Torbellino entero sobre un objetivo quieto.
- **Descarga de escarcha + Estallido de llamas:** con un 40 % menos de velocidad cuesta más salir del área.

### 2c. XP por hora en solitario (nivel 5, Kóbold minero, con descansos)
| Clase | Ciclo | XP por hora | Diferencia con la mejor |
|---|---|---|---|
| Pícaro | 33,3 s | 2 813 | referencia |
| Guerrero | 34,8 s | 2 693 | −4 % |
| Sacerdote | 36,0 s | 2 599 | −8 % |
| Mago | 37,1 s | 2 520 | −10 % |

Dentro del ±15 % (`soloXpPerHourSpreadPct`). Sin cambios respecto al 2026-09-30.

### 3. Jefe: Capataz Grask (1 400 de vida, armadura 90), HU-083 CA3
Modelo de `boss.py` (mismo daño del jefe: golpe con ¡A trabajar! la mitad del combate y medio Latigazo), con la duración
iterada en lugar de fijarla en 80 s, todas las parejas de nivel 6 y el maná del Mago y del Sacerdote al final.

| Grupo | DPS del grupo | Duración | Tanque | HPS necesario / máximo del Sacerdote | El tanque aguanta | Maná al final |
|---|---|---|---|---|---|---|
| 3 × nv 4: Guerrero, Mago, Sacerdote | 16,3 | **86 s** | Guerrero 288, recibe 7,6/s | 9,8 / 10,4 | todo | Mago 16 de 260 (se queda sin maná a los 83 s); Sacerdote 126 de 231 |
| 3 × nv 4: Guerrero, Pícaro, Sacerdote | 17,2 | **81 s** | Guerrero 288, 7,6/s | 9,8 / 10,8 | todo | Sacerdote 150 de 231 |
| 3 × nv 4 sin sanador: Guerrero, Pícaro, Mago | 25,9 | **54 s** | Guerrero 288, 7,6/s | 9,8 / — | 33 s sin consumibles | Mago 101 de 260 |
| 2 × nv 6: Guerrero, Pícaro | 23,0 | **61 s** | Guerrero 408, 6,8/s | 8,9 / — | 52 s | — |
| 2 × nv 6: Guerrero, Mago | 23,6 | **59 s** | Guerrero 408, 6,8/s | 8,9 / — | 52 s | Mago 12 de 292 |
| 2 × nv 6: Guerrero, Sacerdote | 13,3 | **105 s** | Guerrero 408, 6,8/s | 8,9 / 12,1 | todo | Sacerdote 261 de 263 |
| 2 × nv 6: Pícaro, Mago | 26,5 | **53 s** | Pícaro 200, 7,4/s | 9,5 / — | 24 s | Mago 30 de 292 |
| 2 × nv 6: Pícaro, Sacerdote | 15,6 | **90 s** | Pícaro 200, 7,4/s | 9,5 / 13,0 | todo | Sacerdote 255 de 263 |

- **Grupo de referencia (3 de nivel B−2 con sanador): 81–86 s**, dentro de 60–100 s. El Sacerdote cubre el daño
  casteando el 61 % del tiempo (con la varita el resto, que le devuelve maná) y acaba con más de la mitad del maná.
- **Mago:** con la rotación más codiciosa (Estallido, Descarga, Bola de fuego; básicos solo sin maná) se queda sin maná 3 s
  antes del final. Alternando más básicos o con una de las 3 pociones de maná de su equipo inicial, llega.
- **Sin sanador (54 s):** más rápido que el objetivo, y el Guerrero solo aguanta 33 s. Ganan con una poción (60) **y** pan
  (el pan se puede comer en combate: ~3,3 de vida por segundo) o cediendo el aggro un rato; con una sola poción no llega.
- **2 de nivel 6:** todas las parejas lo ganan (CA3), en 53–105 s. Las parejas sin sanador necesitan una poción; Pícaro +
  Mago, alternar el aggro.
- `boss.py` tal cual (80 s fijos) da 85 / 54 / 57 / 101 s para sus cuatro grupos. Con los rangos de hechizo (+15 % de
  `base` desde el nivel 4, que el servidor aplica y el modelo no) el grupo de referencia baja a 79 s y Guerrero + Sacerdote
  a 97 s.

**Propuesta (no aplicada; CA4 pide jugarlo antes):** dejar la vida en 1 400. No hay vida ni armadura que meta a todas las
parejas de nivel 6 en 60–100 s: probé 1 300–1 600 de vida con armadura 45–90 y Guerrero + Sacerdote tarda siempre ~2 veces
lo que Pícaro + Mago (95–117 s frente a 50–57 s). El grupo de referencia queda centrado (86 s). Si jugando Guerrero +
Sacerdote resulta lento, la palanca no es la vida del jefe (hundiría a Pícaro + Mago por debajo de 50 s) sino el daño del
Sacerdote cuando no cura.

### 3b. Triángulo PvP (duelos 1 vs 1, nivel 6)
**Cómo se simula.** Monte Carlo de 1 000 duelos por pareja, ticks de 50 ms, distancia en una dimensión con movimiento a 4
casillas/s. Reglas del servidor: tabla de impacto, crítico, varianza, tirada del arma, rangos de hechizo (+15 % de `base`),
GCD, bloqueo de 250 ms, el casteo pausa el básico y deja moverse al 50 %, aturdir corta casteos (+1,5 s sin castear), inmunidad
de 1,5 s tras aturdir o enraizar, escudos, tope de ralentización del 40 %, Carrera, Provocar sin efecto en jugadores (solo da
ira) y fin del duelo al 1 % de vida. `classAdvantage` multiplica básico, hechizos y ticks de DoT (en la Fase 1 ningún kit de
jugador tiene DoT, así que a nivel 6 no influye por ahí). Equipo verde de referencia de cada clase, sin pociones ni pan.

**Supuestos de referencia:** los cuerpo a cuerpo persiguen; quien pega a distancia retrocede a 0,75 de la velocidad del
perseguidor (obstáculos, reacción) y elige la mejor de dos tácticas (castear siempre o alejarse con básicos);
distancia inicial 4–10 casillas; cada duelista rinde ×N(1; 0,10) en ese duelo (calidad de ejecución). El Sacerdote se cura
a sí mismo (los hechizos de aliado caen sobre él en un duelo).

| Duelo (favorito primero) | Con `classAdvantage` en 1,0 | Con la matriz nueva | Duración media (nueva) |
|---|---|---|---|
| Mago > Guerrero | **0 %** | 68 % (66 % en 3 semillas) | 36 s |
| Guerrero > Pícaro | **99 %** | 63 % (64 %) | 29 s |
| Pícaro > Mago | **100 %** | 67 % (67 %) | 18 s |
| Sacerdote > Guerrero (desgaste) | **56 %** | 67 % (67 %) | 49 s |
| Pícaro > Sacerdote (control) | **78 %** | 66 % (66 %) | 30 s |
| Sacerdote = Mago (parejo) | 96 % para el Sacerdote | 56 % (54 %) | 21 s |

Por qué estaban así: el Guerrero tiene 2,4 veces la vida del Mago (408 frente a 170) y la Nova (3 s cada 18 s) y la
Descarga no lo mantienen lejos el tiempo suficiente; el Pícaro mata al Mago en 10 s; el Sacerdote cura más (Sanar + Escudo
+ Pulso, ~19 por segundo en ráfaga) de lo que el Mago le hace, y Castigo (1,5 s) le pega más por segundo que la Bola de
fuego (2 s).

**Matriz aplicada** (fila = atacante, columna = defensor; el resto en 1,0): Guerrero→Pícaro 0,63, Guerrero→Mago 0,38,
Pícaro→Mago 0,54, Pícaro→Sacerdote 0,93, Mago→Sacerdote 1,45, Sacerdote→Guerrero 1,12. Regla seguida: bajar el daño del
que domina (los duelos de 10–20 s pasan a 18–36 s) salvo donde quien pierde no llega a matar (Mago contra Sacerdote) o el
favorito se queda corto (Sacerdote contra Guerrero).

Sensibilidad de la matriz nueva (% del favorito):

| Duelo | Retrocede a 0,5 | Retrocede a 1,0 | Sin variación de ejecución | Variación 0,2 | Empiezan a 8–14 casillas |
|---|---|---|---|---|---|
| Mago > Guerrero | 60 % | 93 % | 72 % | 61 % | 68 % |
| Guerrero > Pícaro | 63 % | 63 % | 65 % | 64 % | 68 % |
| Pícaro > Mago | 66 % | 90 % | 77 % | 63 % | 64 % |
| Sacerdote > Guerrero | 63 % | 71 % | 81 % | 62 % | 64 % |
| Pícaro > Sacerdote | 65 % | 33 % | 75 % | 63 % | 67 % |
| Sacerdote = Mago | 56 % | 56 % | 51 % | 58 % | 57 % |

- **Cuidado con el tamaño de los factores:** 0,38 o 1,45 no son una "palanca fina". Dicen que el triángulo no sale del kit
  sino de la vida de cada rol; la matriz lo corrige solo en PvP y no toca nada de PvE. Las curvas son empinadas (Guerrero→
  Pícaro 0,60 da 57 % y 0,70 da 80 %): se ajustan con duelos reales en HU-084 CA3.
- **El kiteo decide los duelos con el Mago y el Sacerdote:** un Mago que retrocede perfecto gana al Guerrero el 93 % y un
  Sacerdote, al Pícaro el 67 %. Es la "habilidad que revierte el triángulo" del pilar 7.
- **Sacerdote contra Sacerdote** no termina: el 86 % de los duelos llega a 3 minutos sin ganador. El duelo no tiene tope de
  tiempo.

### 4. Curva de XP
- Kills por nivel = minutos × 60 / 36; total **126 934 XP, ~2 517 kills**. Con el ciclo medido al nivel 5: Pícaro 23,3 h,
  Guerrero 24,3 h, Sacerdote 25,2 h, Mago 26,0 h. Objetivo 20–30 h cumplido.
- Reparto en grupo (`rules.group`), XP por kill de cada miembro (entre paréntesis, la de matarlo solo):

| Grupo | Monstruo | Pool | Reparto |
|---|---|---|---|
| 10 / 8 / 5 | normal nv 9 (46 XP) | 64,2 | 26,5 / 26,5 / 11,2 (41 / 51 / 64) |
| 6 / 6 / 6 | normal nv 6 (31 XP) | 48,1 | 16,0 cada uno (31) |
| 6 / 5 / 4 | normal nv 5 (26 XP) | 36,3 | 12,1 cada uno (23 / 26 / 29) |

  Con 3 jugadores cada uno gana ~52 % por kill. Si pelea y descanso bajan a un tercio y la caminata sigue en 10 s, el ciclo
  pasa de ~36 s a ~18 s y la XP por hora queda igual que en solitario (pilar 1); es una estimación, no una simulación.

### 5. Economía al nivel 5
Contra el Kóbold minero (oro 12–28 + mena y botín vendidos): **40–45 platas por hora** según la clase (97–108 kills por
hora). Marta vende la poción de vida y la de maná a 20 cobres y el pan a 4: una poción es el 0,5 % de lo que se gana en una
hora. El límite de las pociones es su cooldown de 60 s, no el precio.

### Élites de rama lateral (HU-080 CA1)
Guardianes de las dos ramas laterales que ya existen en la pradera, cada uno con su campamento alrededor. XP calculada del
tipo `elite` (×3). Referencia: el Gólem de escombros (élite nv 6) mata solo al 106–121 % de la vida de un Pícaro, Mago o
Sacerdote de su nivel y al 54 % de un Guerrero; en pareja cae en 24–37 s.

| | Jabalí de guerra (`boar_alpha`) | Huargo de la atalaya (`wolf_alpha`) |
|---|---|---|
| Sitio | escondite de bandidos (Campos), 4 bandidos nv 3 | atalaya goblin (Colinas), 4 goblins arqueros nv 5 |
| Nivel, vida, armadura | 3, 330, 20 | 5, 500, 30 |
| Daño | 7–11 cada 2 s (vida ×3,3 y daño ×1,4 del Bandido) | 8–12 cada 1,8 s (vida ×3,1 y daño ×1,5 del Kóbold) |
| XP | 48 (bandido: 16) | 78 (kóbold: 26) |
| IA | aggro 4 (bandidos 5), correa 12, velocidad 3,8 | aggro 5 (goblins 7), correa 12, velocidad 4,5 |
| Reaparece | 600 s | 600 s |
| Botín garantizado (1 de) | Botas de piel de jabalí (×2), Guantes de bandido, Capucha de aprendiz | Daga de colmillo, Collar de dientes de lobo, Espada de hierro, Varita de sauce |
| Además | 15–35 de oro, colmillo, bolsa robada 50 %, pan 30 %, poción 20 % | 30–70 de oro, 2–3 pieles, pociones 20 %, Anillo de hueso 5 % |
| Solo a su nivel (vida perdida) | Pícaro 67 %, Guerrero 76 %, Sacerdote 95 %, Mago 116 % | Guerrero 41 %, Pícaro 79 %, Sacerdote 104 %, Mago 114 % |
| Solo un nivel por encima | Pícaro y Guerrero 44 %, Sacerdote 73 %, Mago 86 % | Guerrero 36 %, Pícaro 71 %, Sacerdote 95 %, Mago 100 % |
| Pareja de su nivel | 19–33 s; el que tanquea pierde 31–45 % | 19–28 s; 19–56 % |
| Pareja con un refuerzo del campamento | 54–78 % | 34–102 % (Pícaro + Sacerdote, con curas) |
| Trío de su nivel | 17–18 s; 22–23 % (37–40 % con refuerzo) | 16 s; 15 % (28 % con refuerzo) |

- Su `aggroRange` es menor que el del campamento: se puede limpiar a los guardias desde fuera y luego tirar del élite. Los
  guardias reaparecen a los 40–45 s; un élite en pareja dura 20–33 s, así que lo normal es pelear con un refuerzo como
  mucho. El Huargo es algo más blando que lo que daría la proporción del Gólem (530 de vida y 8–13) porque los goblins
  disparan desde 6 casillas.
- La correa corta (12) lo mantiene en su rama: se puede sacar unas casillas del campamento, pero no arrastrarlo al sendero.
- Probabilidad del botín garantizado: las Botas tienen peso 2 porque son lo propio del jabalí (50 %); el resto, 25 % cada
  uno. Cada item cubre una clase distinta y se puede intercambiar.

### Desviaciones y cambios
1. **Triángulo PvP fuera de 60–75 % → corregido** en `rules.classAdvantage` (§3b). Mensaje propuesto:
   `content(balance): tune PvP classAdvantage so duel favourites win 60-75% (HU-084)`.
2. **Élites de rama lateral → añadidos** (`monsters.json`, `loot_tables.json`; las tablas nuevas van al final del array
   porque dos tests de contenido apuntan a tablas por índice). Mensaje propuesto:
   `content(monsters): add war boar and watchtower warg side-branch elites (HU-080)`.
3. **Propuesta, no aplicada:** Guerrero y Pícaro con bastón o varita rinden el 37–46 % del Pícaro (piso 55 %). Su `sp`
   solo se usa en ese básico, así que se puede subir sin tocar nada más. Con estos valores quedan en 57–63 %:
   ```json
   "warrior": { "sp": { "str": 1.1, "agi": 0.45, "int": 1.75 } },
   "rogue":   { "sp": { "str": 0.4, "agi": 1.0, "int": 1.6 } }
   ```
   No lo aplico: no es un escenario de la tabla de referencia y algún test de stats puede fijar el `sp` del Guerrero.
4. **Pendientes de CA4, sin tocar:** Mago solo con básicos (60–77 % del nivel 3 al 5, y el Sacerdote 61–68 % en los
   niveles 3 y 4), Mago pesado al 47 % de aguante, Capataz con Guerrero + Sacerdote de nivel 6 (105 s) y sin sanador (54 s),
   y `killCycleSecTarget` = 36.

### Observaciones de reglas (no son números)
- **Carga se puede usar enraizado:** el servidor solo bloquea `leap` con raíz, no `dash`. En el simulador apenas cambia el
  duelo Mago contra Guerrero (0 % igual), pero contradice "raíz: no mueve".
- **El pan se come en combate:** ~3,3 de vida por segundo renovándolo, por 4 cobres. Sostiene al tanque sin sanador en el
  jefe y vale en duelos. Si no se quiere, hace falta una regla (solo fuera de combate).
- **Los duelos no tienen tope de tiempo** (Sacerdote contra Sacerdote no termina).

## Sin medir
- **Triángulo con duelos reales** (HU-084 CA3): el simulador es de una dimensión, sin obstáculos ni latencia.
- **Pentagrama al nivel 15** (Fases 2 y 3).

# Fase 2 · Monstruos del Tier 2 (HU-109, 2026-10-06)

Los 12 monstruos del Bosque y de la Cripta: 3 de campamento y un élite en el Linde y en el Pantano, y en la Cripta 2 de nivel
10, las plantas trampa y un élite de nivel 11. Medidos con `python tools/balance/tier2.py` (nuevo; `--tier1` repite el
solitario sin equipo nuevo y `--quick` se salta los élites). Valores esperados, sin azar ni movimiento.

**Supuestos** (los de la Fase 1 salvo donde se dice):
- **Equipo aproximado** (no hay equipo de nivel 7 a 9 hasta HU-110): el verde de referencia del Tier 1 (`REF_GEAR`) con todo lo
  numérico (stats, armadura, daño del arma, poder de hechizo) × `1 + 0,2 · (nivel − 6)`: ×1,0 al 6, ×1,4 al 8, ×1,8 al 10. Es
  el presupuesto lineal por nivel de la skill `game-content`, con el equipo de nivel N − 1 de media al nivel N.
- **Rangos de hechizo** como el servidor: +15 % del `base` del nivel 4 al 7 y +30 % del 8 al 11 (`check.py` no los cuenta).
- **Hechizos:** los 16 medidos de la Fase 1. Los de nivel 7 y 9 siguen provisionales (HU-106) y no se cuentan: los tiempos
  para matar son conservadores.
- **Misma escala que la Fase 1:** rotación con los hechizos de daño, maná gastado y descanso como `check.py`. Con esos supuestos
  (y sin rangos) el modelo nuevo repite el Kóbold minero de la Fase 1: ciclos de 33 / 37 / 34 / 36 s y 11 % de diferencia de
  XP por hora (`check.py`: 33,3 / 37,1 / 34,8 / 36,0 s y 10 %).
- **El monstruo** pega con su básico y sus hechizos (cooldown, casteo que pausa su básico, `hpBelowPct`, DoT, aturdir y
  silenciar). Las áreas marcadas no se esquivan (peor caso), salvo en la columna de los élites que lo dice; esquivar le cuesta
  al cuerpo a cuerpo el casteo + 0,5 s sin pegar. El cuerpo a cuerpo tarda en llegar a un monstruo a distancia.
- **Élites:** pociones menores de vida (60, cada 60 s, bajo el 40 %); el Sacerdote cura (Escudo + Sanar, su capacidad medida en
  60 s) a quien baje del 50 % solo y del 60 % en grupo, sin pegar mientras cura, y sus curas gastan maná.

### Bestiario
XP calculada con `rules.progression` (`round((5 · nivel + 1) · tipo)`). Cada mecánica se ve antes de doler: casteo (barra sobre
el monstruo) o área marcada en el suelo. Solo efectos, formas (círculo) y auras que ya existen.

| Zona | Monstruo (`id`) | Nv | Tipo | Vida | Armadura | Daño | Alcance · velocidad | Mecánica | XP |
|---|---|---|---|---|---|---|---|---|---|
| Linde | Lobo del bosque (`forest_wolf`) | 6 | normal | 175 | 28 | 4–7 cada 1,7 s | 1,2 · **5** | Aullido bajo el 50 % (casteo 1 s): +25 % de daño 8 s. Más rápido que el jugador: no se le huye | 31 |
| Linde | Leñador bandido (`bandit_woodcutter`) | 7 | normal | 190 | 40 | 6–10 cada 2,8 s | 1,5 · 3,8 | Hachazo: área marcada (radio 1,25) en su objetivo, casteo 1,5 s, 12 de daño, cada 10 s | 36 |
| Linde | Araña tejedora (`weaver_spider`) | 8 | hard | 220 | 25 | 7–11 cada 2,2 s | **5** · 3,8 | Telaraña: área marcada (1,5), casteo 1,2 s, 6 de daño y raíz 2,5 s, cada 12 s | 49 |
| Linde | **Oso viejo** (`old_bear`) | 8 | élite | 950 | 60 | 36–48 cada 2,5 s | 1,5 · 4,5 | Zarpazo: área marcada (1,5), casteo 1,2 s, 30, cada 10 s. Rugido: a su objetivo, casteo 1 s, 10 y aturde 2 s, cada 12 s | 123 |
| Pantano | Sapo gigante (`giant_toad`) | 8 | normal | 230 | 15 | 6–10 cada 2,6 s | 1,5 · 3 | Salpicón de lodo: área marcada (2), casteo 1,5 s, 8 y ralentiza 40 % 4 s, cada 12 s | 41 |
| Pantano | Hombre lagarto (`lizardman`) | 9 | normal | 230 | 40 | 6–9 cada 2,4 s | 1,8 · 4,2 | Lanza envenenada: a su objetivo, casteo 1 s, 8 y veneno de 3 cada 3 s durante 9 s, cada 10 s | 46 |
| Pantano | Fuego fatuo (`will_o_wisp`) | 10 | hard | 240 | 10 | 8–11 **mágico** cada 2,4 s | **6** · 4,5 | Destello: área marcada (1,5), casteo 1,5 s, 12 mágico y silencio 2,5 s, cada 12 s | 61 |
| Pantano | **Bruja del pantano** (`swamp_witch`) | 10 | élite | 1 050 | 30 | 30–40 **mágico** cada 2,4 s | **6** · 4 | Ciénaga: área marcada (2,5), casteo 2 s, 20 y raíz 3 s, cada 14 s. Maldición: a quien no es el tanque, casteo 1,5 s, 8 cada 3 s durante 12 s. Brebaje bajo el 50 %: casteo 2,5 s, se cura 150, cada 25 s | 153 |
| Cripta | Esqueleto de raíces (`root_skeleton`) | 10 | normal | 235 | **90** | 6–10 cada 2,4 s | 1,5 · 3,8 | Tajo de espinas: área marcada (1,25), casteo 1,2 s, 8 y sangrado físico de 5 cada 3 s durante 9 s, cada 10 s | 51 |
| Cripta | Espíritu del musgo (`moss_spirit`) | 10 | hard | 230 | 20 | 8–12 **mágico** cada 2,4 s | **5** · 3,5 | Savia: casteo 2 s, cura 40 a sí mismo y a los monstruos a 5 casillas, cada 12 s (interrúmpelo o mátalo primero) | 61 |
| Cripta | Planta trampa (`trap_plant`) | 10 | hard | 200 | 40 | 8–11 cada 2,4 s | **8** · **0** | Inmóvil; despierta a 4 casillas. Raíces trampa: área marcada (2) en quien pasa, casteo 1,2 s, 8 y raíz 3 s, cada 8 s | 61 |
| Cripta | **Guardián de la cripta** (`crypt_guardian`) | 11 | élite | 1 150 | **110** | 40–56 cada 2,6 s | 1,5 · 4 | Golpe de losa: área marcada (1,5), casteo 1,8 s, 40, cada 10 s. Coraza de raíces: casteo 1,5 s, escudo de 160 durante 10 s, cada 18 s | 168 |

- **Un normal por nivel, sin huecos:** lobo (6), leñador (7), sapo (8), lagarto (9) y esqueleto (10); el 10 del Pantano es el
  Fuego fatuo (`hard`).
- **Élites:** `aggroRange` 4, menor que el de los monstruos de campamento de su zona (5–7; las plantas trampa, también 4, tienen
  su propia sala), y correa 12, como los de la pradera. Pegan, en proporción a la vida del tanque, casi lo que el Capataz a su grupo de referencia (el Guerrero de su nivel
  pierde ~2–2,6 % de vida por segundo; con el Capataz, 2,6 %), pero mueren antes: el combate en trío dura 20–34 s (el Capataz,
  81–86 s).
- **Botín:** tablas mínimas (cobre y la chatarra que ya existe); HU-110 añade el equipo y el verde garantizado de los élites.

### 1. Stats por clase y nivel (equipo aproximado y rangos)
| Nv | Equipo × | Pícaro: vida · AP/SP · armadura | Mago | Guerrero | Sacerdote |
|---|---|---|---|---|---|
| 6 | 1,0 | 200 · 76/20 · 45 | 170 · 46/48 · 14 | 408 · 66/20 · 78 | 215 · 42/37 · 18 |
| 7 | 1,2 | 212 · 84/22 · 52 | 180 · 51/53 · 16 | 449 · 73/22 · 92 | 231 · 46/41 · 21 |
| 8 | 1,4 | 224 · 92/24 · 58 | 190 · 56/58 · 17 | 490 · 81/24 · 106 | 247 · 51/45 · 24 |
| 9 | 1,6 | 236 · 101/26 · 65 | 200 · 60/63 · 19 | 530 · 88/26 · 121 | 263 · 55/50 · 27 |
| 10 | 1,8 | 248 · 109/28 · 71 | 210 · 65/69 · 21 | 571 · 96/28 · 135 | 279 · 59/54 · 30 |
| 11 | 2,0 | 260 · 117/30 · 78 | 220 · 69/74 · 22 | 612 · 103/30 · 149 | 295 · 63/58 · 33 |

Cura del Sacerdote (Escudo + Sanar, 60 s): 16,8 por segundo al 8, 18,4 al 10 y 19,2 al 11.

### 2. Solitario contra cada monstruo de campamento de su nivel
Rotación / solo básicos: tiempo en matarlo · vida perdida. En negrita, por encima del piso del 50 %.

| Monstruo | Nv | Tipo | Pícaro | Mago | Guerrero | Sacerdote |
|---|---|---|---|---|---|---|
| *Kóbold minero (Fase 1, referencia)* | 5 | normal | 12 s · 15 % / 21 s · 28 % | 11 s · 22 % / 33 s · **65 %** | 15 s · 8 % / 26 s · 17 % | 16 s · 23 % / 33 s · **50 %** |
| Lobo del bosque | 6 | normal | 12 s · 14 % / 22 s · 27 % | 11 s · 15 % / 33 s · **57 %** | 15 s · 8 % / 26 s · 14 % | 15 s · 17 % / 36 s · 47 % |
| Leñador bandido | 7 | normal | 12 s · 12 % / 21 s · 25 % | 11 s · 17 % / 33 s · **56 %** | 15 s · 8 % / 26 s · 14 % | 16 s · 21 % / 33 s · 43 % |
| Araña tejedora | 8 | hard | 13 s · 16 % / 22 s · 27 % | 11 s · 19 % / 33 s · **61 %** | 15 s · 8 % / 25 s · 13 % | 16 s · 23 % / 33 s · 47 % |
| Sapo gigante | 8 | normal | 12 s · 13 % / 21 s · 20 % | 13 s · 18 % / 37 s · **55 %** | 14 s · 5 % / 24 s · 11 % | 16 s · 17 % / 36 s · 39 % |
| Hombre lagarto | 9 | normal | 11 s · 15 % / 20 s · 27 % | 11 s · 20 % / 30 s · **58 %** | 15 s · 9 % / 24 s · 15 % | 16 s · 24 % / 31 s · 44 % |
| Fuego fatuo | 10 | hard | 11 s · 16 % / 18 s · 32 % | 11 s · 19 % / 30 s · **61 %** | 13 s · 9 % / 23 s · 17 % | 16 s · 25 % / 29 s · 46 % |
| Esqueleto de raíces | 10 | normal | 12 s · 15 % / 21 s · 28 % | 11 s · 22 % / 30 s · **60 %** | 15 s · 9 % / 24 s · 14 % | 15 s · 24 % / 29 s · 45 % |
| Espíritu del musgo | 10 | hard | 12 s · 16 % / 20 s · 24 % | 13 s · 19 % / 37 s · **56 %** | 13 s · 7 % / 25 s · 14 % | 18 s · 18 % / 38 s · 46 % |
| Planta trampa | 10 | hard | 11 s · 12 % / 17 s · 20 % | 11 s · 18 % / 27 s · 45 % | 13 s · 6 % / 21 s · 10 % | 13 s · 16 % / 24 s · 31 % |

- **Con la rotación** nadie pasa del 25 % de vida perdida y todos los matan en 11–18 s (Fase 1: 10–16 s).
- **Solo con básicos:** el Pícaro, el Guerrero y el Sacerdote quedan bajo el 50 %; el **Mago, en 55–61 %**: por encima del piso
  pero por debajo del 65 % del Kóbold que la Fase 1 dejó pendiente (ver §Desviaciones).

### 2c. Ciclo por monstruo y XP por hora en solitario
Ciclo = pelea + descanso + 10 s de caminata · XP por hora.

| Monstruo | Nv | Pícaro | Mago | Guerrero | Sacerdote | Diferencia | Ciclo máx. |
|---|---|---|---|---|---|---|---|
| Lobo del bosque | 6 | 32 s · 3 500 | 36 s · 3 124 | 34 s · 3 287 | 32 s · 3 519 | 11 % | 36 s |
| Leñador bandido | 7 | 32 s · 4 113 | 35 s · 3 751 | 34 s · 3 820 | 33 s · 3 900 | 9 % | 35 s |
| Araña tejedora | 8 | 33 s · 5 305 | 34 s · 5 260 | 34 s · 5 148 | 32 s · 5 472 | 6 % | 34 s |
| Sapo gigante | 8 | 32 s · 4 682 | 35 s · 4 180 | 32 s · 4 542 | 31 s · 4 718 | 11 % | 35 s |
| Hombre lagarto | 9 | 31 s · 5 371 | 33 s · 5 071 | 34 s · 4 848 | 32 s · 5 167 | 10 % | 34 s |
| Fuego fatuo | 10 | 31 s · 7 139 | 32 s · 6 886 | 32 s · 6 762 | 32 s · 6 949 | 5 % | 32 s |
| Esqueleto de raíces | 10 | 32 s · 5 741 | 32 s · 5 757 | 34 s · 5 361 | 31 s · 6 020 | 11 % | 34 s |
| Espíritu del musgo | 10 | 32 s · 6 892 | 34 s · 6 545 | 32 s · 6 956 | 32 s · 6 841 | 6 % | 34 s |
| Planta trampa | 10 | 30 s · 7 355 | 32 s · 6 886 | 32 s · 6 961 | 27 s · 8 000 | 14 % | 32 s |

- **Cualquier clase mata a cualquier monstruo de campamento de su nivel con un ciclo de 27–36 s**, dentro de
  `killCycleSecTarget` (36 s).
- **XP por hora:** diferencia máxima entre clases del 14 % (Planta trampa); en los normales, 9–11 %. Dentro del ±15 %.
- **Horas del 6 al 10** contra el normal de cada nivel: Pícaro 6,2 h, Mago 6,9 h, Guerrero 6,7 h, Sacerdote 6,4 h (la curva da
  7,2 h a 36 s). Los hechizos de nivel 7 y 9 (HU-106) las acortarán más: lo mide HU-119.
- Los `hard` dan un 20 % más de XP con tiempos parecidos; su riesgo (pegan a distancia, enraízan, silencian, se curan) es lo que
  el modelo no ve.

### Élites: solo y en grupo de su nivel
Columnas del solitario: como en la Fase 1 (rotación, sin curas ni pociones); con pociones y, el Sacerdote, curándose; y lo mismo
esquivando todas las áreas marcadas (el mejor caso para quien juega solo). En los grupos, lo más bajo que llega la vida de cada uno.

#### Oso viejo (nv 8, 950 de vida, armadura 60)
| Solo a nivel 8 | Como la Fase 1 | Con pociones y curas | Esquivando las áreas |
|---|---|---|---|
| Pícaro | 20 s · **106 %** | muere a los 24 s (le queda el 70 %) | muere a los 29 s (68 %) |
| Mago | 14 s · **108 %** | muere a los 17 s (73 %) | muere a los 22 s (64 %) |
| Guerrero | 46 s · **103 %** | muere a los 50 s (30 %) | muere a los 63 s (29 %) |
| Sacerdote | 17 s · **101 %** | sin maná para curarse a los 156 s, muere a los 175 s (81 %) | sin maná a los 217 s, muere a los 240 s (42 %) |

| Grupo | Duración | Vida perdida (lo más bajo) | Resultado |
|---|---|---|---|
| Guerrero + Pícaro | 34 s | Guerrero 60 % | lo matan |
| Guerrero + Mago | 31 s | Guerrero 60 % | lo matan |
| Guerrero + Sacerdote | 46 s | Guerrero 44 % | lo matan |
| Pícaro + Mago | 40 s | Pícaro muere, Mago 82 % | lo matan con una baja |
| Pícaro + Sacerdote | 52 s | Pícaro 53 % | lo matan |
| Mago + Sacerdote | 50 s | Mago 56 % | lo matan |
| Tríos de nivel 8 (los 4) | 21–27 s | tanque 44–53 % | lo matan |
| Tríos con Sacerdote de nivel 7 | 27–28 s | Guerrero 44 % | lo matan |

#### Bruja del pantano (nv 10, 1 050 de vida, armadura 30)
| Solo a nivel 10 | Como la Fase 1 | Con pociones y curas | Esquivando las áreas |
|---|---|---|---|
| Pícaro | 20 s · **101 %** | muere a los 24 s (le queda el 51 %) | muere a los 26 s (55 %) |
| Mago | 19 s · **115 %** | muere a los 22 s (58 %) | muere a los 24 s (55 %) |
| Guerrero | 46 s · **105 %** | muere a los 49 s (23 %) | muere a los 53 s (32 %) |
| Sacerdote | 22 s · **102 %** | sin maná a los 280 s, muere a los 317 s (62 %) | sin maná a los 396 s, muere a los 456 s (59 %) |

| Grupo | Duración | Vida perdida (lo más bajo) | Resultado |
|---|---|---|---|
| Guerrero + Pícaro | 29 s | Guerrero 55 %, Pícaro 22 % | lo matan |
| Guerrero + Mago | 30 s | Guerrero 55 %, Mago 29 % | lo matan |
| Guerrero + Sacerdote | 42 s | Guerrero 43 %, Sacerdote 30 % | lo matan |
| Pícaro + Mago | 29 s | Pícaro muere, Mago 26 % | lo matan con una baja |
| Pícaro + Sacerdote | 41 s | Pícaro 50 %, Sacerdote 30 % | lo matan |
| Mago + Sacerdote | 43 s | Mago 53 %, Sacerdote 33 % | lo matan |
| Tríos de nivel 10 (los 4) | 20–23 s | tanque 31–50 % | lo matan |
| Tríos con Sacerdote de nivel 9 | 24 s | Guerrero 40 % | lo matan |

#### Guardián de la cripta (nv 11, 1 150 de vida, armadura 110)
| Solo a nivel 11 | Como la Fase 1 | Con pociones y curas | Esquivando las áreas |
|---|---|---|---|
| Pícaro | 25 s · **108 %** | muere a los 29 s (le queda el 74 %) | muere a los 35 s (79 %) |
| Mago | 16 s · **106 %** | muere a los 21 s (73 %) | muere a los 27 s (71 %) |
| Guerrero | 58 s · **102 %** | muere a los 64 s (46 %) | muere a los 79 s (48 %) |
| Sacerdote | 21 s · **106 %** | sin maná a los 278 s, muere a los 323 s (79 %) | no lo mata en 600 s (le queda el 19 %) |

| Grupo | Duración | Vida perdida (lo más bajo) | Resultado |
|---|---|---|---|
| Guerrero + Pícaro | 39 s | Guerrero 60 % | lo matan |
| Guerrero + Mago | 38 s | Guerrero 60 % | lo matan |
| Guerrero + Sacerdote | 57 s | Guerrero 43 % | lo matan |
| Pícaro + Mago | 42 s | Pícaro muere, Mago 72 % | lo matan con una baja |
| Pícaro + Sacerdote | 58 s | Pícaro 49 % | lo matan |
| Mago + Sacerdote | 69 s | Mago 60 % | lo matan |
| Tríos de nivel 11 (los 4) | 22–30 s | tanque 37–49 % | lo matan |
| Tríos con Sacerdote de nivel 10 (el tope de la Fase 2) | 31–34 s | Guerrero 41 % | lo matan |

- **Nadie los mata solo a nivel equivalente**, ni con pociones, ni esquivando todas las áreas. El que más se acerca es el
  Guerrero (deja al élite con el 23–48 %). El Sacerdote se cura más de lo que le pegan durante minutos, pero no le hace daño
  suficiente y se queda sin maná; contra el Guardián, esquivándolo todo, sigue vivo a los 10 minutos con el élite al 19 %.
- **Piden un grupo de 2–3:** las parejas con Guerrero o Sacerdote los matan en 29–69 s; Pícaro + Mago (sin tanque ni sanador)
  pierde a uno. Los tríos, en 20–34 s, también con un nivel menos.
- **Rugido del Oso** (aturde 2 s a su objetivo, con casteo: no se esquiva andando, sí se interrumpe) es lo que impide que el
  Sacerdote y el Guerrero lo maten solos esquivando: con un área alrededor del oso en su lugar, los dos lo mataban.

### Sensibilidad: sin equipo nuevo (`tier2.py --tier1`)
Con solo el verde del Tier 1 (sin HU-110), el ciclo sube a 29–40 s, la diferencia de XP por hora llega al 19 % (Planta trampa;
16 % el Hombre lagarto) y el Mago solo con básicos pierde hasta el 89 % (Espíritu del musgo). Del nivel 6 al 10: 6,5–7,2 h.
**HU-110 tiene que hacer crecer los números del equipo ~20 % por nivel de item** (lo que supone `gear_factor`); si se queda corto,
la palanca es la vida de los normales de nivel 9 y 10, no la de los del Linde.

### Desviaciones y propuestas
1. **Mago solo con básicos, 55–61 % de vida perdida** (piso 50 %). Es el pendiente de la Fase 1 (65 % contra el Kóbold), que
   aquí no empeora. Propuesta, **no aplicada** (cambia los duelos del Mago y su punta de armadura, que hay que volver a medir):
   ```json
   "classScaling": { "mage": { "hpPerSta": 12 } }
   ```
   Deja al Mago en 47–53 % contra los monstruos del Tier 2 y en 57 % contra el Kóbold. Con `hpPerSta` 11: 51–57 %.
2. **La Fase 2 sale algo más corta que la curva** (6,2–6,9 h frente a 7,2 h) y se acortará con los hechizos de nivel 7 y 9. No se
   toca aquí: HU-119 mide con el equipo y los hechizos reales y decide entre subir la vida de los monstruos o `minutesPerLevel`.
3. **Maná del Mago:** con la aproximación de `check.py` (alterna básicos), su ciclo contra el Kóbold es 37 s; con el maná exacto
   de la rotación más codiciosa sería 49 s. Se mantiene la escala de la Fase 1; HU-119 debería cronometrar cómo descansa un Mago
   de verdad.

### Lo que el modelo no ve
- **Kiteo:** el Mago, con Nova (raíz 3 s) y Descarga (−40 %), puede alejarse del Oso (velocidad 4,5) y del Guardián (4); el
  Lobo (5) no se deja kitear sin raíz. Las cifras de los élites son, por eso, pesimistas para un Mago que juegue bien.
- **Interrupciones y controles del jugador:** Gubia corta Brebaje, Savia, Coraza de raíces y Rugido; la Carga aturde 1,5 s. No
  están en el modelo, así que los grupos con Pícaro o Guerrero matan a los élites algo antes.
- **Parejas que se potencian contra estos monstruos:** Nova de escarcha + Estallido de llamas contra un campamento de lobos
  (la raíz es lo único que frena su velocidad 5); Gubia + Paso sombrío contra el Espíritu del musgo (cortar Savia y rematarlo).

### Cliente y motor
- **Sprites:** los 12 usan `monsters/<id>` y no hay hoja todavía (HU-114). El cliente dibuja el rectángulo de color con la
  inicial (`PlaceholderSprite`, `client/scripts/world/entity_visual.gd`), sin errores. Dos tests GUT que exigen hoja y
  animaciones a todo monstruo de `monsters.json` fallan hasta HU-114 (`test_visual_redesign.gd`,
  `test_combat_animations.gd`).
- **Íconos y efectos:** los hechizos y auras nuevos reutilizan íconos existentes; los mágicos sin proyectil se dibujan en arcano y
  los físicos en acero (`VfxCatalog`), hasta que HU-114 les dé elemento (veneno, naturaleza).
- **Planta inmóvil (`speed: 0`):** el schema lo permite desde esta HU (antes el mínimo era 0,5) y la IA lo soporta sin código
  (`docs/design/combat.md` §Monstruos). Su spawn en la Cripta debe ir con `wanderRadius` 0 (HU-115).

# Fase 2 · Hechizos de nivel 7 y 9 (HU-106, 2026-10-07)

Los 8 hechizos nuevos de la Fase 2 pasan a `"provisional": false`. Medidos con `python tools/balance/phase2.py` (nuevo;
`--rank 0.2` repite todo con otro `spellRankBonusPct` en memoria, sin tocar `rules.json`). Valores esperados, sin azar.

**Decisiones de medición nuevas:**
1. **Nivel 10** (tope de la Fase 2) con el equipo aproximado de `tier2.py` (×1,8; no hay equipo de nivel 7 a 9 cerrado: HU-110 está
   en curso) y el **segundo rango**: +30 % del `base` de los efectos y de las auras numéricas, sin escalar los coeficientes (ADR-027
   D3), lo mismo que hace el servidor.
2. **Referencias del nivel 10 con la misma definición que las fijas del nivel 6**: mono = básico del Pícaro / 0,30 (43,0), área =
   4 × 0,6 × mono (103,1), control 40, movilidad 120, armadura = aguante del Guerrero / 0,70 (133,7 s). Con las fijas del nivel 6, el
   equipo del 10 subía todas las puntas a la vez (el básico del Pícaro valdría 49) y la regla 40/75 dejaba de medir el kit.
   Objetivo de referencia de nivel 10 con la misma mitigación que el del 6 (armadura 48).
3. **Valor de la clase en una punta** = el del pentagrama completo (`rules.balanceTargets.pentagram.classes`); con 6 hechizos,
   "base + 4 mejores" ya no limita a ninguna combinación. **Objetivo de la Fase 2** = base + los 4 mejores aportes objetivo de
   [class-kits.md](class-kits.md) entre los hechizos hasta el nivel 10.
4. **Una línea que sale del lanzador también le da lo beneficioso** (HU-102: el Sacerdote está en el origen de Sendero de luz), así
   que su velocidad cuenta en su movilidad (`model.mob_value`).

### Números
| Clase | Hechizo | Antes (provisional) | Ahora |
|---|---|---|---|
| Pícaro | Eviscerar | 10,5 + 0,42 AP, recarga 20 s | **8 + 0,4 AP, recarga 14 s**, 35 de energía |
| | Cuchillas arrojadizas | cono 4 · 50°, 2,1 + 0,12 AP, +30 % de velocidad 3 s | **cono 3 · 50° sin casteo**, alcance 3, **4 + 0,23 AP**, **+25 %** de velocidad 3 s, recarga 10 s, 25 de energía |
| Mago | Campo ardiente | 2,8 + 0,1 SP, 6 de maná | **9 + 0,23 SP**, **8 de maná**, casteo 0,5 s, recarga 4 s, radio 1,5 |
| | Parpadeo | — | sin cambios: 6 casillas, recarga 15 s, 8 de maná |
| Guerrero | Bloqueo con escudo | −50 % 4 s, recarga 20 s | −50 % **5 s**, recarga **12 s**, 10 de ira |
| | Tajo amplio | 1,75 + 0,1 AP + 0,21 arma | **4 + 0,17 AP + 0,35 arma**; cono 2,5 · 90°, recarga 6 s, 15 de ira |
| Sacerdote | Renovar | 2,4 + 0,07 SP por tick, 8 de maná | **13 + 0,22 SP** por tick (4 en 12 s), **15 de maná**, recarga 3 s |
| | Sendero de luz | cura 1,8 + 0,06 SP, +30 % 4 s, −30 % 3 s, 10 de maná | cura **20 + 0,5 SP**, **+25 %** de velocidad 4 s, −30 % **2 s**, **15 de maná**; línea 8 × 1,5, recarga 15 s |

- **Renovar** cura en un objetivo lo que dan sus ticks (uno cada 3 s): su recarga de 3 s sirve para mantenerlo en varios aliados.
  Con 15 de maná cura ~6,4 por punto de maná al nivel 7 (Escudo 4,5; Sanar 2,5).
- **Campo ardiente** cuesta 8 de maná para que no rinda más por maná que la Bola de fuego sobre un solo objetivo (3,6 frente a 3,9
  de daño por maná al nivel 10).

### Pentagrama al nivel 10
Aporte de cada hechizo (puntos; entre paréntesis, el objetivo de class-kits.md). Las áreas cuentan enteras en área (decisión 1).

| Clase | Hechizo | Mono | Área | Control | Movilidad | Armadura | Suma |
|---|---|---|---|---|---|---|---|
| Pícaro (base 29 / — / — / — / 27) | Golpe siniestro | 18 (20) | | | | | 18 |
| | Gubia | 2 (5) | | 15 (15) | | | 17 |
| | Paso sombrío | 5 (5) | | 4 (5) | 25 (25) | | 35 |
| | Carrera | | | | 25 (25) | | 25 |
| | **Eviscerar** | **11 (20)** | | | | | 11 |
| | **Cuchillas arrojadizas** | | **10 (10)** | | **15 (15)** | | 25 |
| Mago (base 18 / — / — / — / 19) | Bola de fuego | 16 (20) | | | | | 16 |
| | Descarga de escarcha | 4 (5) | | 15 (15) | | | 19 |
| | Nova de escarcha | | 4 (5) | 30 (30) | | | 34 |
| | Estallido de llamas | −2 (5) | 26 (30) | | | | 24 |
| | **Campo ardiente** | −2 | **26 (20)** | | | | 24 |
| | **Parpadeo** | | | | **20 (20)** | | 20 |
| Guerrero (base 25 / — / — / — / 70) | Golpe heroico | 9 (15) | | | | | 9 |
| | Provocar | | | 15 (15) | | | 15 |
| | Carga | | | 14 (15) | 16 (15) | | 30 |
| | Torbellino | (5) | 19 (20) | | | | 19 |
| | **Bloqueo con escudo** | | | | | **18 (20)** | 18 |
| | **Tajo amplio** | (5) | **15 (15)** | | | | 15 |
| Sacerdote (base — / — / — / — / 25) | Sanar | 27 (30) | | | | | 27 |
| | Castigo | | | | | | 0 |
| | Palabra de poder: Escudo | 17 (20) | | | 11 (10) | (5) | 27 |
| | Pulso sagrado | | 26 (30) | 10 (10) | | | 36 |
| | **Renovar** | **20 (20)** | | | | | 20 |
| | **Sendero de luz** | | **15 (15)** | **9 (10)** | **13 (15)** | | 37 |

Valor de cada clase (base + 4 mejores aportes por punta) frente al objetivo de la Fase 2 y al pentagrama completo:

| Clase | Mono | Área | Control | Movilidad | Armadura | Total |
|---|---|---|---|---|---|---|
| Pícaro | 65 (80 · 85) | 10 (10 · 15) | 20 (20 · 35) | 65 (65 · 85) | 27 (30 · 30) | 187 (205) |
| Mago | 38 (45 · 45) | **56 (55 · 90)** | 45 (45 · 70) | 20 (20 · 20) | 19 (25 · 25) | 178 (190) |
| Guerrero | 34 (40 · 45) | 33 (35 · 45) | 29 (30 · 50) | 16 (15 · 20) | 88 (90 · 90) | 200 (210) |
| Sacerdote | 64 (70 · 85) | 40 (45 · 85) | 19 (20 · 25) | 24 (25 · 25) | 25 (30 · 30) | 173 (190) |

- **Regla 40/75 (HU-106 CA1): se cumple.** En las 15 combinaciones de 4 de cada clase ninguna pasa de 187,5 (las más cargadas:
  Guerrero 177 con Provocar + Carga + Torbellino + Bloqueo; Pícaro 159; Sacerdote 153; Mago 138) ni supera el valor de la clase en
  una punta. La habilidad que más suma sigue siendo Pulso sagrado (36 al 10; 39 al 7 y al 8). También se cumple del nivel 7 al 10 con
  los hechizos aprendidos en cada nivel; la única punta por encima es la base del Pícaro en armadura al 7 (30,2 de 30), sin hechizos.
- **Área del Mago (CA5): 56, objetivo 55.** Campo ardiente aporta 26 (objetivo 20) porque al nivel 10 Estallido baja de 30 a 26: el
  rango solo sube el `base` y el equipo sube el básico del Pícaro, que es la referencia (ver §Rangos). Con Campo ardiente el área
  pasa a ser la punta más alta del Mago, como pide class-kits.md §Riesgos.
- **Todo lo de la Fase 1 pierde 1–4 puntos del nivel 6 al 10** (Bola de fuego 20 → 16, Sanar 31 → 27, Estallido 30 → 26) por la
  misma razón. Los perfiles quedan por debajo del objetivo de la Fase 2 en mono y armadura (Pícaro, Mago, Sacerdote).
- **Eviscerar se queda en 11 de 20** por la XP por hora (ver §Solitario).
- **Movilidad del Sacerdote:** Escudo (11) + Sendero (13) = 24 de 25. Con +30 % de velocidad (16) la combinación pasaba de 25: por
  eso Sendero da +25 %.

**Parejas que se potencian (el modelo suma y no las ve):**
- **Paso sombrío + Eviscerar:** el +60 % de daño de 3 s sobre el golpe grande (59 en vez de 37 al nivel 7). Es la apertura del
  Pícaro en solitario y en duelo.
- **Nova de escarcha + Campo ardiente:** 3 s de raíz dentro de un área de radio 1,5 que se repite cada 4 s; sin raíz, salir de 1,5
  casillas es fácil.
- **Carga + Tajo amplio:** el aturdimiento de 1,5 s deja a los objetivos quietos dentro del cono.
- **Sendero de luz + Pulso sagrado:** la línea ralentiza a los enemigos que luego caen en el Pulso; en grupo, la velocidad de la
  línea junta a los aliados para el Pulso.
- **Escudo + Renovar sobre el tanque:** se suman (al 10, un escudo de ~110 y 29 de cura cada 3 s durante 12 s). Lo vigila HU-117
  con el jefe.
- **Al revés, Cuchillas + Carrera:** las velocidades no se suman (ADR-022, manda la más fuerte): el modelo suma 40 de movilidad y
  mientras dura Carrera (+50 %) el +25 % de las Cuchillas no aporta.

### Áreas sin casteo (ADR-027 D4, CA4)
**Cuchillas arrojadizas baja a radio 3 y sigue sin casteo.** Contra el grupo de referencia (3 secundarios en un radio de 2 alrededor
del principal) un Pícaro pegado al objetivo toca lo mismo con radio 3 que con 4: lo que limita es la apertura de 50°, no el radio.
Solo ganaría el radio 4 apartándose 2 casillas (2,5 de 3 frente a 2,2), y un casteo le costaba al Pícaro su básico y moverse a la
mitad justo cuando la habilidad le da velocidad. El alcance baja a 3 (en un cono el punto solo da la dirección, como en Tajo amplio).

| Área | Lanzador a | Secundarios tocados |
|---|---|---|
| Tajo amplio, cono 2,5 · 90° | 1,5 | 2,4 de 3 |
| Torbellino, alrededor 2 | 1,5 | 2,2 de 3 |
| Cuchillas, cono 3 · 50° (elegido) / 4 · 50° (antes) | 1,25 | 2,0 / 2,0 de 3 |
| Cuchillas, cono 3 · 50° / 4 · 50° | 2 | 2,2 / 2,5 de 3 |
| Campo ardiente, suelo 1,5 | — | 2,8 de 3 |
| Sendero de luz, línea 8 × 1,5 | 4 | 2,2 de 3 |

El validador ya no avisa de Tajo amplio (2,5) ni de las Cuchillas (3); queda el aviso de Cono de frío (nivel 11, Fase 3). Con
`world.currentPhase` en 2 (copia temporal del contenido) no hay errores.

### Rangos (ADR-027 D3, CA3)
Lo que sube un lanzamiento con el rango, con el equipo aproximado (y, como contraste, con el verde de nivel 9 que hay hoy en
`items.json` por HU-110, en curso, en su estado del 2026-10-07: da un 12–24 % más de AP y SP que la aproximación).

| Clase | Hechizo | Rango 0 → 1 (equipo del 4) | Rango 1 → 2 (equipo del 8) | Rango 1 → 2 (equipo del 9) | Ídem, verde de nivel 9 de items.json | Equipo del 8 → 9 |
|---|---|---|---|---|---|---|
| Pícaro | Golpe siniestro | 2,6 % | 2,0 % | 1,8 % | 1,5 % | 7,9 % |
| | Gubia | 15 % | 13 % | 13 % | 13 % | 0,2 % |
| | Eviscerar | — | 2,6 % | 2,4 % | 2,0 % | 7,2 % |
| | Cuchillas arrojadizas | — | 2,3 % | 2,2 % | 1,8 % | 7,4 % |
| Mago | Bola de fuego | 5,4 % | 4,1 % | 3,9 % | 3,6 % | 6,4 % |
| | Descarga de escarcha | 4,3 % | 3,2 % | 3,0 % | 2,8 % | 7,1 % |
| | Nova de escarcha | 5,9 % | 4,5 % | 4,3 % | 3,9 % | 6,2 % |
| | Estallido de llamas | 7,0 % | 5,5 % | 5,2 % | 4,8 % | 5,5 % |
| | Campo ardiente | — | 5,7 % | 5,4 % | 5,0 % | 5,3 % |
| Guerrero | Golpe heroico | 3,9 % | 2,6 % | 2,4 % | 2,1 % | 7,7 % |
| | Torbellino | 3,7 % | 2,5 % | 2,3 % | 2,0 % | 8,0 % |
| | Tajo amplio | — | 2,7 % | 2,5 % | 2,2 % | 8,0 % |
| Sacerdote | Sanar | 6,6 % | 5,1 % | 4,8 % | 4,3 % | 5,9 % |
| | Castigo | 6,4 % | 4,9 % | 4,6 % | 4,1 % | 6,0 % |
| | Palabra de poder: Escudo | 10 % | 8,0 % | 7,7 % | 7,2 % | 3,4 % |
| | Pulso sagrado | 7,3 % | 5,6 % | 5,4 % | 4,9 % | 5,4 % |
| | Renovar | — | 7,8 % | 7,5 % | 7,0 % | 3,5 % |
| | Sendero de luz | — | 6,6 % | 6,3 % | 5,8 % | 4,8 % |

**Con el equipo del nivel 9, el segundo rango no se nota:** mediana 4,4 % (4,0 % con el verde de `items.json`), menos que el primer
rango en la Fase 1 (6,1 %), menos que un solo nivel de equipo (6,1 %) y por debajo de la variación de una tirada (10,5 %,
`varianceMax / varianceMin − 1`) en 17 de 18 hechizos (solo Gubia, que no tiene coeficiente). Al subir el `base` y no los
coeficientes, el rango pesa menos cuanto mejor es el equipo.

| `spellRankBonusPct` | Rango 0 → 1 al 4 | Rango 1 → 2 al 9 | Superan la variación al 9 | Rango 3 (nv 12) | Hechizo que más suma (nv 7–10) |
|---|---|---|---|---|---|
| 0,15 (actual) | 6,1 % | 4,4 % | 1 de 18 | +45 % | Pulso sagrado 39,0 |
| **0,20** | 8,2 % | 5,8 % | 1 de 18 | +60 % | Pulso sagrado 39,7 |
| 0,25 | 10,2 % | 7,2 % | 3 de 18 | +75 % | Pulso sagrado **40,7** |
| 0,30 | 12,2 % | 8,5 % | 4 de 18 | +90 % | Pulso sagrado **41,7** |
| 0,40 | 16,3 % | 11,0 % | 10 de 18 | +120 % | Pulso sagrado **43,8** |

**Propuesta (no aplicada):** `spellRankBonusPct` 0,15 → **0,20** (+20 / +40 / +60 %). El segundo rango sube un 5,8 % con el equipo del
9, lo mismo que el primero en la Fase 1 y que un nivel de equipo; es el valor más alto que deja a Pulso sagrado en 40 o menos (con
0,25 llega a 40,7 y habría que bajarlo). Con 0,20 se cumple la regla 40/75 en los niveles 7 a 10 (`phase2.py --rank 0.2`).
```json
"progression": { "spellRankBonusPct": 0.2 }
```
Que se note como una tirada distinta pide ~0,40 y rompe la regla 40/75 por Pulso sagrado. Los hechizos que viven del coeficiente
(Golpe siniestro, Torbellino, Golpe heroico: 2–3 %) no lo notan con ningún valor razonable; eso solo lo arreglaría escalar también
los coeficientes, la alternativa que ADR-027 D3 descartó. Antes de aplicarlo hay que repetir con +20 % lo que en la Fase 1 ya
cuenta el rango del nivel 4: el Capataz (79 s con +15 %) y los duelos.

### Solitario y XP por hora con los hechizos nuevos
`phase2.py` repite el solitario de `tier2.py` contra el normal de cada nivel con varios equipos de 4 para farmear y se queda con el
mejor de cada clase. Bloqueo con escudo baja el daño recibido en su fracción de tiempo activo; Renovar va sobre el propio Sacerdote
(cura mientras pelea, le cuesta un GCD y maná). "Suavizado" = media con la vida del monstruo ×0,9–1,1: el modelo no tiene azar y el
tiempo para matar va a saltos (en el Sapo, el Mago lo mata a 13,2 s porque le faltan 3 de vida a los 13 s).

| Monstruo | Nv | Pícaro | Mago | Guerrero | Sacerdote | Diferencia | Con los de la Fase 1 |
|---|---|---|---|---|---|---|---|
| Leñador bandido | 7 | 4 491 (Eviscerar) | 3 713 | 3 815 (Bloqueo) | 3 975 | **17 %** | 11 % |
| Sapo gigante | 8 | 5 247 | 4 268 | 4 517 | 4 680 | **19 %** | 12 % |
| Hombre lagarto | 9 | 5 971 (+ Cuchillas) | 4 970 | 5 231 (Tajo) | 5 224 | **17 %** | 8 % |
| Esqueleto de raíces | 10 | 6 485 | 5 681 | 5 734 | 5 903 | 12 % | 11 % |

Sin suavizar (como `tier2.py`): 15 / 20 / 16 / 11 %.

- **Solo el Pícaro farmea más rápido con lo nuevo** (Eviscerar desde el 7 y Cuchillas, que también pega a un solo objetivo, desde el
  9): mata en 10 s en lugar de 12. El Mago y el Sacerdote farmean mejor con su equipo de la Fase 1: Campo ardiente y Renovar gastan
  maná y su descanso ya depende del maná. El Guerrero gana un 2–9 % (Bloqueo al 7, Tajo al 9).
- **Eviscerar es el que decide** (suavizado):

  | Eviscerar | Golpe al 7 | Mono al 10 | Dif. nv 7 | nv 8 | nv 9 | nv 10 |
  |---|---|---|---|---|---|---|
  | 6 + 0,3 AP, 14 s | 27 | 8 | 15 % | 18 % | 15 % | 10 % |
  | **8 + 0,4 AP, 14 s (elegido)** | 37 | 11 | 17 % | 19 % | 17 % | 12 % |
  | 10 + 0,5 AP, 14 s | 46 | 14 | 20 % | 21 % | 19 % | 13 % |
  | 14 + 0,7 AP, 14 s (mono 20) | 64 | 19 | 25 % | 25 % | 23 % | 18 % |
  | 10,5 + 0,42 AP, 20 s (provisional) | 41 | 8 | 18 % | 21 % | 18 % | 13 % |

  Ningún Eviscerar deja la diferencia en el 15 % al nivel 8 (el Mago contra el Sapo ya estaba en el 12 %). Elegí el menor que
  sigue siendo un golpe fuerte (el doble que Golpe siniestro al 7, 1,05 de daño por energía frente a 0,6); con 6 + 0,3 (27) apenas
  supera a Golpe siniestro (18) y gasta más energía. La recarga de 14 s le da 3 golpes en los 30 s del pentagrama y uno por pelea en
  solitario (las peleas duran menos de 14 s): es la que más mono da por cada punto de XP por hora.
- **Palancas de `rules.json` probadas, ninguna lo cierra** (suavizado): `classScaling.rogue.haste` 1,10 (−0/−1 punto),
  `classScaling.mage.sp.int` 1,6 (−1), `classScaling.warrior.ap.str` 2,1 con `mage.sp.int` 1,65 (−1/−2) y, sin suavizar,
  `combat.manaRegenPerSpiPer5s` 1,2 (−0/−3). El Pícaro descansa por vida y el Mago por maná: subir el daño del Mago no le acorta
  el descanso.
- **Para HU-119 (no aplicado):** medir con el equipo real de HU-110 (el verde de nivel 9 de `items.json` da al Pícaro un 24 % más de
  AP que la aproximación). Si la diferencia se confirma, la primera palanca es Eviscerar a 6 + 0,3 AP (−2 puntos); la segunda sale de
  cronometrar el descanso real del Mago (§Fase 2, Desviaciones 3), el más lento de los niveles 7 a 9: si el modelo lo exagera, la
  diferencia baja sin tocar nada.

### Lo que no se midió
- **Duelos con los kits nuevos** (HU-119 CA3): el simulador de HU-084 no está en el repo. Riesgo principal: Bloqueo con escudo (−50 %
  el 42 % del tiempo) contra Mago > Guerrero (68 % al nivel 6) y Eviscerar con Paso sombrío en Pícaro > Mago (67 %).
- **El jefe y los élites con Renovar y Sendero de luz** (el modelo cura con Escudo + Sanar): HU-117.
- **El equipo real** (HU-110 en curso): todo usa `gear_factor`; el verde de nivel 9 de `items.json` solo entra en la tabla de rangos.

# Fase 2 · Equipo, botín y vendedor (HU-110, 2026-10-07)

Medido con `python tools/balance/gear.py` (nuevo): el comparador del tooltip, `tier2.py` con el equipo real de `items.json` en lugar
de `gear_factor`, la economía del 6 al 10 y los élites. `--elite-dmg 1.3` y `--regen-delay 5` prueban las propuestas en memoria.
Mismos supuestos que §Fase 2 (HU-109): hechizos de la Fase 1, rangos del servidor, sin azar; los de HU-106 no entran.

### Equipo nuevo
120 objetos de nivel 7 (Linde) y de nivel 9 (Pantano y Cripta), un blanco y un verde por nivel para cada casilla y cada tipo que ya
existe: las 6 armas, el escudo, cabeza, pecho, manos, piernas y pies en tela, cuero, malla y placas, y cuello y anillo (el verde, en
dos versiones: física y de lanzador). Además, un raro por élite y 8 chatarras.

| Arma | Blanco nv 7 | Verde nv 7 | Blanco nv 9 | Verde nv 9 |
|---|---|---|---|---|
| Espada | 7–13 c/2,4 s (DPS 4,2) · Fue 2 | 8–15 (4,8) · Fue 3 Agu 2 | 9–17 (5,4) · Fue 2 Agu 1 | 11–20 (6,5) · Fue 4 Agu 3 |
| Daga | 5–8 c/1,6 s (4,1) · Agi 2 | 6–9 (4,7) · Agi 4 Agu 1 | 7–10 (5,3) · Agi 2 Agu 1 | 8–12 (6,2) · Agi 5 Agu 2 |
| Hacha | 10–17 c/3,2 s (4,2) · Fue 2 | 12–19 (4,8) · Fue 3 Agu 2 | 14–21 (5,5) · Fue 2 Agu 1 | 16–25 (6,4) · Fue 4 Agu 3 |
| Maza | 6–11 c/2,6 s (3,3) · PH 2 · Esp 2 | 7–13 (3,8) · PH 4 · Esp 3 Int 2 | 8–14 (4,2) · PH 3 · Esp 2 Int 1 | 9–16 (4,8) · PH 5 · Esp 4 Int 2 Agu 1 |
| Bastón | 5–10 c/3,0 s (2,5) · PH 3 · Int 2 | 6–11 (2,8) · PH 6 · Int 4 Esp 1 | 6–12 (3,0) · PH 4 · Int 2 Esp 1 | 7–14 (3,5) · PH 7 · Int 5 Esp 2 |
| Varita | 5–8 c/2,0 s (3,2) · PH 2 · Int 2 | 6–9 (3,8) · PH 4 · Int 4 Esp 1 | 6–11 (4,2) · PH 3 · Int 2 Esp 1 | 7–13 (5,0) · PH 5 · Int 5 Esp 2 |

| Armadura (blanco 7 · verde 7 · blanco 9 · verde 9) | Cabeza | Pecho | Manos | Piernas | Pies | Stats del verde 7 · verde 9 (blanco: 2 · 3) |
|---|---|---|---|---|---|---|
| Tela | 3 · 3 · 4 · 4 | 4 · 4 · 5 · 5 | 2 · 2 · 3 · 3 | 3 · 4 · 4 · 5 | 2 · 3 · 3 · 4 | Int 3 Esp 2 Agu 1 · Int 4 Esp 2 Agu 2 |
| Cuero | 8 · 9 · 10 · 12 | 10 · 11 · 13 · 14 | 6 · 6 · 7 · 8 | 9 · 10 · 11 · 12 | 7 · 7 · 8 · 9 | Agi 3 Agu 2 Fue 1 · Agi 4 Agu 2 Fue 2 |
| Malla | 14 · 16 · 18 · 20 | 18 · 20 · 23 · 25 | 10 · 11 · 12 · 14 | 15 · 17 · 19 · 21 | 11 · 13 · 15 · 16 | Agu 3 Fue 2 Esp 1 · Agu 4 Fue 2 Esp 2 |
| Placas | 22 · 25 · 29 · 32 | 28 · 31 · 36 · 40 | 15 · 17 · 20 · 22 | 24 · 26 · 30 · 34 | 18 · 20 · 23 · 26 | Agu 3 Fue 3 · Agu 4 Fue 4 |
| Escudo | | | | | | 25 · 28 · 32 · 36 de armadura; Agu 3 Fue 3 · Agu 4 Fue 4 |
| Joyería (verde) | | | | | | Cuello: Agi 3 Fue 2 Agu 1 o Int 3 Esp 2 Agu 1 · Agi 4 Fue 2 Agu 2 o Int 4 Esp 2 Agu 2; anillo: Agu 3 + 3 (físico o lanzador) · Agu 4 + 4 |

- **Reglas:** el daño de las armas verdes es el del verde del Tier 1 ×1,4 (nivel 7) y ×1,8 (nivel 9), la misma velocidad; el hacha
  sigue el presupuesto de la espada a 3,2 s; los blancos, ~0,87 del verde de su nivel. La armadura es la del pecho blanco del Tier 1
  por tipo (tela 3, cuero 8, malla 14, placas 22) × casilla (pecho 1, piernas 0,85, cabeza 0,8, pies 0,65, manos 0,55) × nivel / 5,
  y el blanco ×0,9; el escudo, 20 × nivel / 5. Stats por tipo: tela Int > Esp > Agu; cuero Agi > Agu > Fue; malla Agu > Fue > Esp
  (sirve al Guerrero y al Sacerdote); placas Agu = Fue.
- **Por debajo de la guía de la skill** (`nivel × {0,5; 1}`): verdes de armadura y joyería con 6 y 8 puntos (no 7 y 9), armas con 5
  y 7, y blancos con 2 y 3 (no 3–4 y 4–5). Ahora hay pieza para las 9 casillas (el Tier 1 daba 3–6 por clase): con la guía, el
  equipo esperado queda muy por encima de `gear_factor` y el Guerrero mata solo a los élites (ver §Élites).
- **Raros de élite** (10 %): Collar de garras de oso (nv 8, cuello, Agi 5 Fue 4 Agu 3), Varita de la bruja (nv 10, 8–14, PH 7,
  Int 6 Esp 5 Agu 4) y Escudo de losa (nv 10, 48 de armadura, Agu 7 Fue 5 Agi 3). Precio de venta 30 · 50 · 35 (blancos 7), 130 ·
  210 · 140 (verdes 7), 40 · 65 · 45 y 165 · 270 · 180 (armadura · arma · joya, nivel 9); raros 450–500.
- **Comparador del tooltip (CA1):** las 94 comparaciones de cada objeto nuevo con los del Tier 1 de su casilla, tipo y rareza (y del
  nivel 9 con el 7) salen «mejor», sin ninguna flecha roja. Un blanco contra el verde del Tier 1 de su casilla cambia stats por
  armadura o DPS (7 de 28 sin flechas rojas). Contra los raros del Capataz (nivel 6) los verdes ganan en DPS o armadura y pierden 1–3
  puntos de algún stat: el Pico sigue siendo competitivo en stats hasta el Hacha de raíz negra (Fue −1, Agu −1, DPS +2,4).

### Botín (CA2)
- **Normales:** el cobre de HU-109, su chatarra (60–65 %, de 16 a 22 cobres: seda, glándula, escama, polvo de fuego fatuo, musgo,
  semilla; piel, bolsa y hueso del Tier 1), una poción un 6–8 % de las veces, 9–10 blancos al 1,2 % (~11 % por kill) y 10–11 verdes
  al 0,7 % (**~7 % por kill**; el Tier 1 daba 4–13 %). La probabilidad por objeto queda por debajo del 3–5 % de la guía porque cada
  tramo tiene 31 verdes: lo que se iguala con el Tier 1 es la de sacar un verde por kill.
- **Reparto:** el Linde suelta el nivel 7 y el Pantano y la Cripta el 9. En cada zona, cada objeto de su tramo está en exactamente un
  monstruo y cada monstruo lleva algo de cada clase (el Leñador, más armas y placas; la Araña y el Espíritu del musgo, más tela).
- **Élites:** un verde garantizado (`groups`, 1 de los 31 de su tramo con el mismo peso), su raro al 10 %, chatarra segura y poción
  un 20–25 %. Por kill, a repartir: 142–150 cobres en oro y chatarra, 1 verde y 0,1 raros.
- **Lo que le llega a una clase:** unos 300 kills por tramo dan ~21 verdes, ~5 de su afinidad alta, más los élites y lo que le pasan
  los amigos: a final de tramo, la mitad de las casillas en verde es lo razonable (el equipo «esperado» del modelo).

### Vendedor y consumibles (CA3)
- `forest_camp`, **Brena la trampera** (`npcs/shopkeeper`; la coloca HU-111 en el punto seguro del Linde). Compra cualquier cosa: el
  servidor compra todo lo que tiene `sellPrice > 0`. Marta no cambia.
- Hoy vende la poción menor de vida, la de maná y el pan. **Falta** la poción mayor de vida (100 de vida, recarga 60 s, 60 cobres) y
  el Venado ahumado (120 de vida en 15 s, solo fuera de combate y se corta con daño, 12 cobres): necesitan `item_greater_heal` y
  `item_eat_smoked_venison` en `spells.json` y `smoked_venison_hot` en `auras.json`, fuera del alcance de esta HU. Con ellos, el
  vendedor pasa a vender poción mayor, venado y poción menor de maná, y las tablas del Tier 2 sueltan poción mayor y venado.
- **Poción de 100, no de 150:** con 150, el Sacerdote mata solo al Oso viejo incluso con `gear_factor` (215 s esquivando).

### Modelo con el equipo real
Equipo de todas las casillas de afinidad alta de cada clase (el Guerrero, placas y escudo). **Esperado:** arma, pecho, piernas y
cuello en verde y el resto en blanco del tramo (nivel 7 a los niveles 7 y 8; nivel 9 del 9 al 11). **Recién llegado:** el esperado
del tramo anterior (Tier 1 al 7; nivel 7 al 8 y al 9; nivel 9 al 10). **Completo:** todo en verde, la cota superior.

| Clase: puntos de stats · armadura · PH · DPS del arma | Tier 1 (nv 6) | `gear_factor` ×1,4 (nv 8) | Nv 7 en las casillas de `REF_GEAR` | Nv 7 completo | ×1,8 (nv 10) | Nv 9 en las casillas de `REF_GEAR` | Nv 9 completo |
|---|---|---|---|---|---|---|---|
| Pícaro | 10 · 18 · 0 · 3,4 | 14 · 25 · 0 · 4,8 | 25 · 23 · 0 · 4,7 | 47 · 43 · 0 · 4,7 | 18 · 32 · 0 · 6,2 | 34 · 30 · 0 · 6,2 | 63 · 55 · 0 · 6,2 |
| Mago | 6 · 5 · 4 · 2,0 | 8 · 7 · 6 · 2,8 | 13 · 7 · 6 · 2,8 | 47 · 16 · 6 · 2,8 | 11 · 9 · 7 · 3,6 | 18 · 9 · 7 · 3,5 | 63 · 21 · 7 · 3,5 |
| Guerrero | 16 · 64 · 0 · 3,5 | 22 · 90 · 0 · 5,0 | 27 · 104 · 0 · 4,8 | 53 · 147 · 0 · 4,8 | 29 · 115 · 0 · 6,4 | 37 · 134 · 0 · 6,5 | 71 · 190 · 0 · 6,5 |
| Sacerdote | 10 · 17 · 3 · 2,8 | 14 · 24 · 4 · 3,8 | 19 · 24 · 4 · 3,8 | 47 · 29 · 4 · 3,8 | 18 · 31 · 5 · 5,0 | 26 · 30 · 5 · 5,0 | 63 · 37 · 5 · 5,0 |

El daño y la armadura de las armas siguen a `gear_factor`; los stats, no: `REF_GEAR` tenía piezas de nivel 1 a 5 y la aproximación
las trataba como de nivel 5. Vida · AP/SP · armadura al nivel 8, `gear_factor` frente a esperado: Pícaro 224 · 92/24 · 58 → 310 ·
106/28 · 79; Mago 190 · 56/58 · 17 → 230 · 68/72 · 23; Guerrero 490 · 81/24 · 106 → 540 · 91/26 · 154; Sacerdote 247 · 51/45 · 24 →
265 · 60/55 · 28 (al 10: 350, 310, 684 y 345 de vida).

| Equipo | Pícaro: rotación / básicos | Mago | Guerrero | Sacerdote | Ciclo máx. | Dif. XP/h máx. |
|---|---|---|---|---|---|---|
| `gear_factor` (HU-109) | 11–13 s · ≤ 16 % / ≤ 32 % | 11–13 s · ≤ 22 % / ≤ **61 %** | 13–15 s · ≤ 9 % / ≤ 17 % | 13–18 s · ≤ 25 % / ≤ 47 % | 35,7 s | 13,9 % |
| Recién llegado | 10–13 s · ≤ 14 % / ≤ 28 % | 10–11 s · ≤ 17 % / ≤ **62 %** | 11–17 s · ≤ 9 % / ≤ 16 % | 12–16 s · ≤ 23 % / ≤ 47 % | **36,3 s** | 14,3 % |
| Esperado | 9–12 s · ≤ 14 % / ≤ 27 % | 10–11 s · ≤ 15 % / ≤ **57 %** | 11–15 s · ≤ 8 % / ≤ 14 % | 12–15 s · ≤ 19 % / ≤ 47 % | 35,7 s | **15,4 %** |
| Completo | 8–12 s · ≤ 14 % / ≤ 27 % | 8–11 s · ≤ 15 % / ≤ **57 %** | 10–15 s · ≤ 8 % / ≤ 14 % | 10–15 s · ≤ 17 % / ≤ 47 % | 35,7 s | **19,1 %** |

Ciclo y XP por hora con el equipo esperado (pelea + descanso + 10 s):

| Monstruo | Nv | Pícaro | Mago | Guerrero | Sacerdote | Diferencia |
|---|---|---|---|---|---|---|
| Lobo del bosque | 6 | 32 s · 3 500 | 36 s · 3 124 | 34 s · 3 287 | 32 s · 3 519 | 11 % |
| Leñador bandido | 7 | 28 s · 4 696 | 31 s · 4 210 | 30 s · 4 381 | 26 s · 4 980 | **15,4 %** |
| Araña tejedora | 8 | 29 s · 6 046 | 30 s · 5 797 | 34 s · 5 237 | 29 s · 6 083 | 14 % |
| Sapo gigante | 8 | 28 s · 5 321 | 30 s · 4 851 | 32 s · 4 600 | 28 s · 5 367 | 14 % |
| Hombre lagarto | 9 | 28 s · 6 013 | 29 s · 5 617 | 31 s · 5 412 | 27 s · 6 033 | 10 % |
| Fuego fatuo | 10 | 29 s · 7 453 | 29 s · 7 518 | 32 s · 6 943 | 29 s · 7 572 | 8 % |
| Esqueleto de raíces | 10 | 30 s · 6 130 | 29 s · 6 258 | 33 s · 5 585 | 29 s · 6 342 | 12 % |
| Espíritu del musgo | 10 | 29 s · 7 580 | 29 s · 7 518 | 31 s · 7 127 | 28 s · 7 985 | 11 % |
| Planta trampa | 10 | 28 s · 7 884 | 28 s · 7 837 | 29 s · 7 653 | 25 s · 8 819 | 13 % |

- **Ciclo:** ≤ `killCycleSecTarget` (36 s) con el equipo esperado y el completo; recién llegado al 7 con el equipo del Tier 1, el
  Guerrero tarda 36,3 s contra el Leñador (HU-109 ya medía hasta 40 s sin equipo nuevo).
- **XP por hora:** 15,4 % con el esperado (Leñador: el Sacerdote se cura en un lanzamiento y el Mago espera su maná) y 19,1 % con el
  completo (Araña: el Guerrero, 31 s frente a 25 s del Sacerdote). Ver §Desviaciones 1.
- **Mago solo con básicos:** 57 % (era 55–61 %); sigue la propuesta `hpPerSta` 12 de HU-109.
- **Horas del 6 al 10** (la curva da 7,2 h): `gear_factor` 6,2–6,9 h; esperado Pícaro 5,7, Mago 6,2, Guerrero 6,3 y Sacerdote 5,6 h;
  completo 5,3–5,9 h. El equipo acorta la Fase 2 otro 6–13 %: lo decide HU-119 (vida de los normales o `killCycleSecTarget`).

### Economía del 6 al 10 (CA4)
Contra el normal de cada nivel con el equipo esperado; ingresos = cobre + chatarra + blancos vendidos (los verdes no se cuentan).

| Nv | Monstruo | Cobres por kill (oro + chatarra + blancos) | Verdes por 100 kills | Platas por hora (Pícaro · Mago · Guerrero · Sacerdote) |
|---|---|---|---|---|
| 6 | Lobo del bosque | 33,6 (22 + 7,8 + 3,8) | 7,0 | 37,9 · 33,8 · 35,6 · 38,1 |
| 7 | Leñador bandido | 33,6 (24 + 5,0 + 4,1) | 7,7 | 43,8 · 39,3 · 40,9 · 46,4 |
| 8 | Sapo gigante | 42,1 (27 + 9,6 + 5,5) | 7,0 | 54,6 · 49,8 · 47,2 · 55,1 |
| 9 | Hombre lagarto | 53,5 (30 + 18,0 + 5,5) | 7,7 | 69,9 · 65,3 · 62,9 · 70,1 |
| 10 | Esqueleto de raíces | 52,3 (33 + 13,5 + 5,8) | 7,0 | 62,8 · 64,1 · 57,2 · 65,0 |

| Del 6 al 10 (717 kills) | Ingresos | Poción mayor 1 cada 10 kills | Poción mayor en cada recarga mientras pelea (peor caso) | Venado en cada kill | Sobra en el peor caso |
|---|---|---|---|---|---|
| Pícaro (5,7 h) | 300 platas | 43 (14 %) | 73 (24 %) | 86 (29 %) | 141 platas |
| Mago (6,2 h) | 300 platas | 43 (14 %) | 80 (27 %) | 86 (29 %) | 134 platas |
| Guerrero (6,3 h) | 300 platas | 43 (14 %) | 95 (32 %) | 86 (29 %) | 119 platas |
| Sacerdote (5,6 h) | 300 platas | 43 (14 %) | 98 (33 %) | 86 (29 %) | 116 platas |

**Alcanza sin farmear aparte:** aun bebiendo una poción mayor en cada recarga mientras pelea y comiendo después de cada kill, sobra
más de un tercio de lo ganado, sin contar élites ni verdes vendidos (130–270 cobres cada uno).

### Élites con el equipo real
Solo, a su nivel, con pociones mayores (100) bajo el 40 % y el Sacerdote curándose: sin esquivar / esquivando todas las áreas.

| Élite | Equipo | Pícaro | Mago | Guerrero | Sacerdote | Parejas · tríos de su nivel |
|---|---|---|---|---|---|---|
| Oso viejo (8) | `gear_factor` | muere (59 %) / muere (62 %) | muere (64 %) / muere (58 %) | muere (27 %) / muere (26 %) | muere (66 %) / muere (29 %) | 30–52 s (Pícaro + Mago, una baja) · 21–27 s |
| Oso viejo (8) | esperado | muere (39 %) / muere (48 %) | muere (55 %) / muere (48 %) | muere (**3 %**) / muere (**2 %**) | muere (21 %) / **lo mata** (170 s) | 28–40 s · 18–21 s |
| Oso viejo (8) | completo | muere (25 %) / muere (24 %) | muere (43 %) / muere (27 %) | **lo mata** (61 s) / **lo mata** (75 s) | **lo mata** (313 s) / **lo mata** (171 s) | 25–36 s · 17–19 s |
| Bruja del pantano (10) | `gear_factor` | muere (52 %) / muere (56 %) | muere (55 %) / muere (52 %) | muere (15 %) / muere (28 %) | muere (53 %) / **lo mata** (451 s) | 29–43 s · 20–23 s |
| Bruja del pantano (10) | esperado | muere (27 %) / muere (40 %) | muere (39 %) / muere (32 %) | muere (**3 %**) / muere (9 %) | **lo mata** (257 s) / **lo mata** (219 s) | 25–35 s · 18–19 s |
| Bruja del pantano (10) | completo | muere (8 %) / muere (10 %) | muere (21 %) / muere (4 %) | **lo mata** (48 s) / **lo mata** (58 s) | **lo mata** (150 s) / **lo mata** (120 s) | 22–30 s · 16–18 s |
| Guardián de la cripta (11) | `gear_factor` | muere (71 %) / muere (76 %) | muere (71 %) / muere (71 %) | muere (46 %) / muere (43 %) | muere (58 %) / **lo mata** (494 s) | 38–69 s (Pícaro + Mago, una baja) · 22–30 s |
| Guardián de la cripta (11) | esperado | muere (50 %) / muere (68 %) | muere (64 %) / muere (48 %) | muere (25 %) / muere (28 %) | no lo mata (60 %) / **lo mata** (316 s) | 32–52 s · 21–22 s |
| Guardián de la cripta (11) | completo | muere (35 %) / muere (43 %) | muere (44 %) / muere (25 %) | **lo mata** (91 s) / **lo mata** (118 s) | no lo mata (14 %) / **lo mata** (190 s) | 29–38 s · 18–19 s |

- Con el equipo esperado, el Guerrero deja al Oso y a la Bruja al 2–9 %: el margen de HU-109 (15–46 %) desaparece. El Sacerdote los
  mata solo en 170–316 s curándose sin parar; con 3 normales por cada élite en ese tiempo, no le compensa en XP.
- Con todo en verde, el Guerrero mata al Oso en 61 s, a la Bruja en 48 s y al Guardián en 91 s: mejor XP por hora que los normales.
- Las parejas y los tríos los matan antes que con `gear_factor` y sin bajas (Pícaro + Mago ya no pierde a nadie).

### Desviaciones y propuestas (no aplicadas)
1. **XP por hora 15,4 % con el equipo esperado y 19,1 % con el completo** (objetivo ≤ 15 %). Con menos daño recibido, el descanso de
   los demás se queda en los 6 s de `hpRegenDelaySec` mientras el Sacerdote se cura en un lanzamiento; el equipo no tiene palanca
   propia del Mago (tela, bastón y varita son también del Sacerdote). Propuesta en `rules.json`:
   ```json
   "combat": { "hpRegenDelaySec": 5 }
   ```
   Con 5 s: esperado 14,0 %, recién llegado 14,0 %, `gear_factor` 11,1 %, completo 16,4 %; Kóbold de la Fase 1 11,7 % (hoy 11,4 %);
   ciclos ≤ 35,3 s y 5,5–6,1 h del 6 al 10. Con 4 s el completo baja a 13,5 %, pero el Kóbold sube a 14,1 %. Los hechizos de HU-106
   cambian quién va primero (el Pícaro con Eviscerar): decidirlo en HU-119 con los dos.
2. **Élites con el equipo real.** Propuesta en `monsters.json`, básico de los tres élites ×1,3:
   ```json
   { "id": "old_bear", "damageMin": 47, "damageMax": 62 },
   { "id": "swamp_witch", "damageMin": 39, "damageMax": 52 },
   { "id": "crypt_guardian", "damageMin": 52, "damageMax": 73 }
   ```
   Con el equipo esperado nadie los mata solo salvo el Sacerdote esquivándolo todo (Bruja en 433 s, Guardián en 219 s), el Guerrero
   se queda en el 16–44 % y las parejas (25–56 s) y los tríos (18–23 s) los siguen matando sin bajas (el tanque, hasta el 95 % en
   pareja). Con todo en verde, el Guerrero aún mata al Oso (61 s) y a la Bruja (48 s), como el Gólem en la Fase 1: o se acepta, o
   HU-119 les da una mecánica que el tanque solo no aguante.
3. **La Fase 2 sale más corta** (5,6–6,3 h frente a 7,2 h): HU-119.

### Íconos
Los 131 objetos nuevos usan íconos que ya existen y piden uno propio en HU-114: las 24 armas (`sword_*`, `dagger_*`, `axe_*`,
`mace_*`, `staff_*`, `wand_*` del Tier 1), las 80 piezas de armadura (capucha, túnica, guantes, calzas y botas de cada tipo, con
`hood_apprentice`, `robe_novice`, `vest_leather`, `mail_recruit`, `plate_foreman`, `helm_iron`, `gloves_bandit`, `legs_miner`,
`boots_boar` y `boots_shadowstep`), los 4 escudos (`shield_wood`), las 12 joyas (`necklace_wolf`, `amulet_lantern`, `ring_bone`), los
3 raros y las 8 chatarras (`goo`, `tusk`, `bone`). La poción mayor y el venado usarán `potion_red` y `bread`.

# Fase 2 · Mejoras de los hechizos (HU-107, 2026-10-07)

48 mejoras, dos por cada hechizo de clase de los niveles 1 a 9 (`upgrades` en `content/spells.json`; la lista y lo que no entró,
en [class-kits.md](class-kits.md) §Mejoras). Medidas con `python tools/balance/upgrades.py` (nuevo; `--draft f.json` mide un
borrador sin tocar el contenido) con las reglas actuales: sin las propuestas pendientes (`spellRankBonusPct` 0,20, `hpPerSta` 12,
`hpRegenDelaySec` 5, élites ×1,3). Valores esperados, sin azar.

**Decisiones de medición nuevas** (siguen las de §HU-106: nivel 10 con el equipo aproximado de `tier2.py`, segundo rango,
referencias del nivel 10 y valor de la clase = pentagrama completo):
1. **La mejora se aplica como en el servidor** (`SpellUpgrades.Apply`: valor · `mult` + `add`; `effect` escala base, coeficientes
   y porcentaje de arma; `aura` cambia una copia del aura; `addEffect` añade el efecto al final; los campos enteros se redondean
   alejándose de cero) y el rango multiplica el `base` ya mejorado. `upgrades.py` lo comprueba con los casos de
   `shared/test-vectors/spell_upgrades.json`: 7 de 7.
2. **Lo que suma una mejora en mono y área es la diferencia media con ventanas de 24, 27, 30, 33 y 36 s.** Sin azar, en 30 s
   exactos una recarga o un casteo algo más corto da 0 o un lanzamiento entero más: Eviscerar con 4 s menos de recarga cabe 3
   veces, como sin mejora (0 puntos), y Estallido de llamas con 0,5 s menos de casteo cabe 4 veces en vez de 3 (+8,6 de área). Con
   las ventanas, +3,0 y +1,7 (más 1,4 de mono). Los hechizos sin mejora conservan su valor de 30 s: `phase2.py` da lo mismo que en
   §HU-106.
3. **Lo que el modelo no miraba porque ningún hechizo lo cambiaba** (no mueve a ningún hechizo sin mejora): el radio, la apertura
   o el ancho de un área escalan la parte de los secundarios, en área y en control, con la cobertura del grupo de referencia
   (`phase2.coverage`); la Carga recorre (alcance mínimo + alcance) / 2 casillas (5 con el alcance de 8, lo que ya contaba); una
   ralentización cuenta como mucho `rules.combat.maxSlowPct` (0,4), lo que aplica el servidor; las auras propias de un hechizo que
   no es sobre uno mismo y las curas propias cuentan en armadura, como un escudo.
4. **Que ninguna de las dos sea mejor en todo.** Se comparan en las 5 puntas (empate: menos de 0,5 puntos) y en lo que el
   pentagrama no ve: recurso por minuto lanzándolo en cuanto está listo, valor de un lanzamiento (daño, cura, todo el DoT/HoT o el
   escudo sobre un objetivo), alcance, secundarios tocados con un grupo disperso (3 en un radio de 3; el de referencia, de radio 2,
   ya cabe entero en casi todas las áreas) y, en el Sacerdote, el daño por segundo, que no está en su pentagrama (empate: menos de
   un 2 %).
5. **Builds:** 4 de los 6 hechizos × (sin mejora, A o B) = 1 215 por clase. La regla 40/75 se mide al nivel 10 (el de la Fase 2,
   como en §HU-106) y, aparte, al 8 y al 9 con los hechizos aprendidos.

### Cada mejora sobre el pentagrama (nivel 10)
Aporte del hechizo sin mejora y, debajo, lo que cambia cada mejora (puntos; «·» = sin cambio). Las cinco últimas columnas son lo
de fuera del pentagrama.

| Clase | Hechizo | Mejora | Mono | Área | Control | Movilidad | Armadura | Suma (total) | Recurso/min | Por lanzamiento | Alcance | Disperso | Daño/s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Pícaro | Golpe siniestro | sin mejora | 18 | 0 | 0 | 0 | 0 | 18 | 600 | 23 | 1,5 | — | — |
|  |  | Filo afilado | +2,7 | · | · | · | · | +2,7 (21) | 600 | 27 | 1,5 | — | — |
|  |  | Golpe ágil | · | · | · | · | · | +0,0 (18) | 400 | 23 | 1,5 | — | — |
|  | Gubia | sin mejora | 2 | 0 | 15 | 0 | 0 | 17 | 60 | 11 | 1,5 | — | — |
|  |  | Gubia profunda | · | · | +3,0 | · | · | +3,0 (20) | 60 | 11 | 1,5 | — | — |
|  |  | Gubia evasiva | · | · | · | +6,0 | · | +6,0 (23) | 60 | 11 | 1,5 | — | — |
|  | Paso sombrío | sin mejora | 5 | 0 | 4 | 25 | 0 | 35 | 120 | 0 | 5,0 | 1,4 | — |
|  |  | Paso fugaz | +0,3 | · | +0,5 | +2,8 | · | +3,6 (38) | 133 | 0 | 5,0 | 1,4 | — |
|  |  | Emboscada | +2,7 | · | · | · | · | +2,7 (37) | 120 | 0 | 5,0 | 1,4 | — |
|  | Carrera | sin mejora | 0 | 0 | 0 | 25 | 0 | 25 | 0 | 0 | 0,0 | — | — |
|  |  | Carrera larga | · | · | · | +5,0 | · | +5,0 (30) | 0 | 0 | 0,0 | — | — |
|  |  | Huida | · | · | · | +5,0 | · | +5,0 (30) | 0 | 0 | 0,0 | — | — |
|  | Eviscerar | sin mejora | 11 | 0 | 0 | 0 | 0 | 11 | 150 | 47 | 1,5 | — | — |
|  |  | Remate rápido | +3,0 | · | · | · | · | +3,0 (14) | 210 | 47 | 1,5 | — | — |
|  |  | Corte en seco | · | · | +5,4 | · | · | +5,4 (16) | 150 | 47 | 1,5 | — | — |
|  | Cuchillas arrojadizas | sin mejora | 0 | 10 | 0 | 15 | 0 | 25 | 150 | 26 | 3,0 | 1,0 | — |
|  |  | Abanico | · | +2,1 | · | · | · | +2,1 (27) | 150 | 26 | 3,0 | 1,4 | — |
|  |  | Impulso largo | · | · | · | +5,0 | · | +5,0 (30) | 150 | 26 | 3,0 | 1,0 | — |
| Mago | Bola de fuego | sin mejora | 16 | 0 | 0 | 0 | 0 | 16 | 240 | 31 | 8,0 | — | — |
|  |  | Llama rápida | +3,3 | · | · | · | · | +3,3 (19) | 267 | 31 | 8,0 | — | — |
|  |  | Llama intensa | +2,7 | · | · | · | · | +2,7 (19) | 240 | 33 | 8,0 | — | — |
|  | Descarga de escarcha | sin mejora | 4 | 0 | 15 | 0 | 0 | 19 | 40 | 28 | 8,0 | — | — |
|  |  | Frío profundo | · | · | +3,8 | · | · | +3,8 (23) | 40 | 28 | 8,0 | — | — |
|  |  | Lanza de hielo | +1,1 | · | · | · | · | +1,1 (21) | 40 | 34 | 8,0 | — | — |
|  | Nova de escarcha | sin mejora | 0 | 4 | 30 | 0 | 0 | 34 | 50 | 16 | 0,0 | 2,5 | — |
|  |  | Hielo eterno | · | · | +5,0 | · | · | +5,0 (39) | 50 | 16 | 0,0 | 2,5 | — |
|  |  | Nova cortante | · | +4,3 | · | · | · | +4,3 (38) | 50 | 32 | 0,0 | 2,5 | — |
|  | Estallido de llamas | sin mejora | -2 | 26 | 0 | 0 | 0 | 24 | 188 | 67 | 8,0 | 2,9 | — |
|  |  | Estallido rápido | +1,4 | +1,7 | · | · | · | +3,1 (27) | 188 | 67 | 8,0 | 2,9 | — |
|  |  | Estallido intenso | · | +5,9 | · | · | · | +5,9 (29) | 225 | 80 | 8,0 | 2,9 | — |
|  | Campo ardiente | sin mejora | -2 | 26 | 0 | 0 | 0 | 24 | 120 | 29 | 8,0 | 1,4 | — |
|  |  | Campo amplio | · | +1,3 | · | · | · | +1,3 (25) | 120 | 29 | 8,0 | 2,2 | — |
|  |  | Campo abrasador | +0,4 | +0,9 | · | · | · | +1,4 (25) | 96 | 36 | 8,0 | 1,4 | — |
|  | Parpadeo | sin mejora | 0 | 0 | 0 | 20 | 0 | 20 | 32 | 0 | 6,0 | — | — |
|  |  | Parpadeo ligero | · | · | · | · | · | +0,0 (20) | 8 | 0 | 6,0 | — | — |
|  |  | Parpadeo reparador | · | · | · | -5,0 | +4,4 | -0,6 (19) | 24 | 32 | 6,0 | — | — |
| Guerrero | Golpe heroico | sin mejora | 9 | 0 | 0 | 0 | 0 | 9 | 200 | 11 | 1,5 | — | — |
|  |  | Golpe brutal | +2,2 | · | · | · | · | +2,2 (11) | 200 | 14 | 1,5 | — | — |
|  |  | Golpe desafiante | -4,1 | · | +6,2 | · | · | +2,1 (11) | 100 | 11 | 1,5 | — | — |
|  | Provocar | sin mejora | 0 | 0 | 15 | 0 | 0 | 15 | 0 | 0 | 6,0 | — | — |
|  |  | Grito lejano | · | · | · | · | · | +0,0 (15) | 0 | 0 | 9,0 | — | — |
|  |  | Provocación rápida | · | · | +2,6 | · | · | +2,6 (18) | 0 | 0 | 6,0 | — | — |
|  | Carga | sin mejora | 0 | 0 | 14 | 16 | 0 | 30 | 0 | 0 | 8,0 | — | — |
|  |  | Carga lejana | · | · | -0,8 | +2,0 | · | +1,2 (31) | 0 | 0 | 10,0 | — | — |
|  |  | Carga aplastante | · | · | +2,6 | -1,7 | · | +0,9 (31) | 0 | 0 | 8,0 | — | — |
|  | Torbellino | sin mejora | 0 | 19 | 0 | 0 | 0 | 19 | 120 | 49 | 0,0 | 1,7 | — |
|  |  | Torbellino amplio | · | +3,3 | · | · | · | +3,3 (22) | 120 | 49 | 0,0 | 2,1 | — |
|  |  | Torbellino feroz | · | +2,8 | · | · | · | +2,8 (22) | 120 | 56 | 0,0 | 1,7 | — |
|  | Bloqueo con escudo | sin mejora | 0 | 0 | 0 | 0 | 18 | 18 | 50 | 0 | 0,0 | — | — |
|  |  | Muro de escudo | · | · | · | · | +1,2 | +1,2 (20) | 38 | 0 | 0,0 | — | — |
|  |  | Bloqueo ligero | · | · | · | · | · | +0,0 (18) | 0 | 0 | 0,0 | — | — |
|  | Tajo amplio | sin mejora | 0 | 15 | 0 | 0 | 0 | 15 | 150 | 22 | 2,5 | 1,2 | — |
|  |  | Tajo ancho | · | +2,0 | · | · | · | +2,0 (17) | 150 | 22 | 2,5 | 1,8 | — |
|  |  | Tajo profundo | · | +2,1 | · | · | · | +2,1 (17) | 150 | 26 | 2,5 | 1,2 | — |
| Sacerdote | Sanar | sin mejora | 27 | 0 | 0 | 0 | 0 | 27 | 240 | 19 | 8,0 | — | -8,3 |
|  |  | Sanación rápida | +5,2 | · | · | · | · | +5,2 (33) | 288 | 19 | 8,0 | — | -8,3 |
|  |  | Sanación potente | +4,1 | · | · | · | · | +4,1 (32) | 240 | 21 | 8,0 | — | -8,3 |
|  | Castigo | sin mejora | 0 | 0 | 0 | 0 | 0 | 0 | 240 | 26 | 8,0 | — | 8,3 |
|  |  | Castigo ardiente | · | · | · | · | · | +0,0 (0) | 240 | 31 | 8,0 | — | 11,6 |
|  |  | Castigo rápido | · | · | · | · | · | +0,0 (0) | 540 | 26 | 8,0 | — | 16,1 |
|  | Palabra de poder: Escudo | sin mejora | 17 | 0 | 0 | 11 | 0 | 27 | 80 | 108 | 8,0 | — | 0,0 |
|  |  | Escudo grueso | +4,0 | · | · | · | · | +4,0 (31) | 80 | 130 | 8,0 | — | 0,0 |
|  |  | Escudo ligero | · | · | · | · | · | +0,0 (27) | 56 | 108 | 10,0 | — | 0,0 |
|  | Pulso sagrado | sin mejora | 0 | 26 | 10 | 0 | 0 | 36 | 120 | 80 | 8,0 | 3,0 | 5,0 |
|  |  | Pulso radiante | · | +4,1 | -3,4 | · | · | +0,8 (36) | 120 | 90 | 8,0 | 3,0 | 5,0 |
|  |  | Pulso cegador | · | -4,1 | +3,4 | · | · | -0,8 (35) | 120 | 70 | 8,0 | 3,0 | 5,0 |
|  | Renovar | sin mejora | 20 | 0 | 0 | 0 | 0 | 20 | 300 | 115 | 8,0 | — | 0,0 |
|  |  | Renovar duradero | · | · | · | · | · | +0,0 (20) | 300 | 144 | 8,0 | — | 0,0 |
|  |  | Renovar intenso | +4,0 | · | · | · | · | +4,0 (24) | 300 | 103 | 8,0 | — | 0,0 |
|  | Sendero de luz | sin mejora | 0 | 15 | 9 | 13 | 0 | 37 | 60 | 57 | 8,0 | 1,6 | 0,0 |
|  |  | Sendero ancho | · | +0,3 | +1,2 | · | · | +1,4 (39) | 60 | 49 | 8,0 | 2,1 | 0,0 |
|  |  | Sendero cegador | · | · | +2,2 | -1,7 | · | +0,6 (38) | 60 | 57 | 8,0 | 1,6 | 0,0 |

**Ninguna pareja tiene una mejora mejor en todo** contando lo de fuera del pentagrama. Por tipo:
- **Suben puntas distintas (12):** Gubia (control / movilidad), Paso sombrío (movilidad / mono), Eviscerar (mono / control),
  Cuchillas (área / movilidad), Descarga (control / mono), Nova (control / área), Estallido (mono y algo de área / área), Parpadeo
  (movilidad / armadura), Golpe heroico (mono / control), Carga (movilidad / control), Pulso sagrado (área / control) y Sendero de
  luz (área y control / control a cambio de movilidad).
- **Empatan en la misma punta y las separa lo de fuera (5):** Torbellino y Tajo amplio (más radio: más secundarios con el grupo
  disperso / más daño: más por lanzamiento), Campo ardiente (radio / más daño por lanzamiento y menos maná por minuto, con más
  recarga), Castigo (más por lanzamiento y menos maná / más daño por segundo) y Carrera, igual en todo lo medido: la larga (8 s
  cada 40 s) sirve para viajar y perseguir; Huida (2 s cada 20 s) rompe una raíz el doble de veces.
- **Una sube más la punta y la otra gana fuera (7):** Golpe siniestro (+2,7 de mono / 10 de energía menos), Bola de fuego
  (+3,3 / +2,7 de mono, la intensa con menos maná por minuto y más por lanzamiento), Provocar (+2,6 de control / +3 de alcance),
  Bloqueo con escudo (+1,2 de armadura / sin ira), Sanar (+5,2 / +4,1 de mono, la potente con menos maná y más por lanzamiento),
  Escudo (+4,0 de mono / 6 de maná menos y +2 de alcance) y Renovar (el intenso cura más por segundo, +4,0; el duradero, un 40 %
  más por lanzamiento con el mismo maná).

### Builds al nivel 10: regla 40/75 y valor de la clase (CA3)

| Clase | Builds | Máx. total (de 187,5) | Mín. total | Máx. mono | Máx. área | Máx. control | Máx. movilidad | Máx. armadura | Incumplen |
|---|---|---|---|---|---|---|---|---|---|
| Pícaro | 1 215 | 177,6 (159 sin mejoras) | 127,4 | 73 (de 85) | 12 (de 15) | 28 (de 35) | 84 (de 85) | 27 (de 30) | 0 |
| Mago | 1 215 | 153,3 (138) | 114,8 | 43 (de 45) | 67 (de 90) | 54 (de 70) | 20 (de 20) | 23 (de 25) | 0 |
| Guerrero | 1 215 | 185,0 (177) | 151,6 | 36 (de 45) | 39 (de 45) | 41 (de 50) | 18 (de 20) | 90 (de 90) | 0 |
| Sacerdote | 1 215 | 164,2 (153) | 100,1 | 78 (de 85) | 45 (de 85) | 25 (de 25) | 24 (de 25) | 25 (de 30) | 0 |

Ningún hechizo con mejora pasa de 40 (el que más: Nova de escarcha con Hielo eterno, 39,2) y ninguna build pasa de 187,5 ni del
valor de la clase en una punta. Las puntas que tocan techo: movilidad del Mago (Parpadeo ya daba 20 de 20: sus mejoras no dan
movilidad), armadura del Guerrero (89,6 de 90 con Muro de escudo), control del Sacerdote (24,7 de 25 con Pulso y Sendero
cegadores) y movilidad del Pícaro (83,8 de 85).

| Clase | Build más fuerte | Build más débil | Más débil con una mejora en cada hechizo |
|---|---|---|---|
| Pícaro | 177,6: Gubia evasiva + Paso fugaz + Huida + Impulso largo (36 / 10 / 20 / 84 / 27) | 127,4: Golpe siniestro + Gubia + Carrera + Eviscerar, sin mejoras (60 / 0 / 15 / 25 / 27) | 135,6: Golpe ágil + Gubia profunda + Remate rápido + Abanico (63 / 12 / 18 / 15 / 27) |
| Mago | 153,3: Frío profundo + Hielo eterno + Estallido intenso + Campo abrasador (18 / 63 / 54 / 0 / 19) | 114,8: Bola de fuego + Descarga + Estallido + Parpadeo reparador (36 / 26 / 15 / 15 / 23) | 120,1: Llama intensa + Lanza de hielo + Campo amplio + Parpadeo reparador (40 / 27 / 15 / 15 / 23) |
| Guerrero | 185,0: Provocación rápida + Carga lejana + Torbellino amplio + Muro de escudo (25 / 22 / 31 / 18 / 90) | 151,6: Golpe heroico + Provocar + Bloqueo + Tajo amplio, sin mejoras (34 / 15 / 15 / 0 / 88) | 155,8: Golpe desafiante + Grito lejano + Bloqueo ligero + Tajo ancho (30 / 17 / 21 / 0 / 88) |
| Sacerdote | 164,2: Sanación rápida + Escudo grueso + Pulso radiante + Sendero ancho (53 / 45 / 17 / 24 / 25) | 100,1: Sanar + Castigo + Escudo + Renovar, sin mejoras (64 / 0 / 0 / 11 / 25) | 104,2: Sanación potente + Castigo ardiente + Escudo ligero + Renovar duradero (68 / 0 / 0 / 11 / 25) |

(Puntas: mono / área / control / movilidad / armadura.) Las más débiles con mejoras eligen las que el pentagrama no ve (Golpe
ágil, Grito lejano, Bloqueo ligero, Escudo ligero, Renovar duradero) o las que cambian una punta por otra (Parpadeo reparador,
Golpe desafiante): son las que más valor dejan fuera de la cuenta, no malas elecciones. Con las mejoras, la build más cargada de
cada clase sube 8–19 puntos sobre la de §HU-106 y se queda a 2,5 puntos o más de 187,5.

### Del nivel 8 al 10 (hechizos aprendidos a cada nivel)

| Nv | Pícaro: hechizo máx. · build máx. · puntas por encima | Mago | Guerrero | Sacerdote |
|---|---|---|---|---|
| 8 | 38,1 (Paso fugaz) · 171,8 · ninguna | 39,6 (Hielo eterno) · 162,6 · **mono 45,8 de 45** | 30,9 (Carga lejana) · 186,4 · ninguna | 39,9 (Pulso radiante) · 165,2 · **mono 88,0 de 85** |
| 9 | 38,1 (Paso fugaz) · 178,9 · ninguna | 39,4 (Hielo eterno) · 157,6 · ninguna | 30,9 (Carga lejana) · 185,6 · ninguna | 39,4 (Sendero ancho) · 170,5 · ninguna |
| 10 | 38,1 (Paso fugaz) · 177,6 · ninguna | 39,2 (Hielo eterno) · 153,3 · ninguna | 30,9 (Carga lejana) · 185,0 · ninguna | 38,5 (Sendero ancho) · 164,2 · ninguna |

Al nivel 8 el segundo rango llega con el equipo del 7 y los hechizos de `base` alto valen más que al 10 (§HU-106, Rangos): sin
mejoras, la build con más mono ya llega a 39,6 de 45 en el Mago y a 73,0 de 85 en el Sacerdote. Con las mejoras que suben el mono
pasan de su valor de la clase al 8 (Mago con Llama rápida, Lanza de hielo y Estallido rápido; Sacerdote con Sanación rápida,
Escudo grueso y Renovar intenso); al 9 y al 10 no. Ver §Desviaciones 1.

### XP por hora en solitario (suavizado de `phase2.py`, normal de cada nivel)
Cada clase con su mejor equipo de `phase2.FARM_KITS` y, en cada hechizo, la mejora que más XP por hora le da.

| Nv | Monstruo | Pícaro | Mago | Guerrero | Sacerdote | Diferencia con mejoras | Sin mejoras (§HU-106) |
|---|---|---|---|---|---|---|---|
| 8 | Sapo gigante | 5 402 (+3,0 %) | 4 425 (+3,7 %) | 4 748 (+5,1 %) | 5 084 (+8,7 %) | 18 % | 19 % |
| 9 | Hombre lagarto | 6 129 (+2,6 %) | 5 151 (+3,7 %) | 5 231 (+0,0 %) | 5 802 (+11,0 %) | 16 % | 17 % |
| 10 | Esqueleto de raíces | 6 574 (+1,4 %) | 5 907 (+4,0 %) | 5 972 (+4,2 %) | 6 523 (+10,5 %) | 10 % | 12 % |

- **Las mejoras no agravan la diferencia** (baja 1–2 puntos): el Pícaro, el que más farmea, gana lo menos (+1–3 %: Remate rápido
  solo cuenta en peleas de más de 10 s y Filo afilado es +15 % de un golpe). El Guerrero al 9 da +0,0 % porque con Tajo amplio el
  tiempo para matar no cambia con +15 % de daño (el modelo va a saltos); al 8 y al 10, +4–5 %.
- **Castigo es lo que más mueve la XP por hora del Sacerdote** (+9–11 % con Castigo ardiente). Sin el coste de maná, Castigo
  rápido daba +13–19 % y dejaba al Sacerdote primero al 9 y al 10; con +3 de maná, +3,5–10 %.

### Validación y formas
- `ContentValidator`: 0 errores (el aviso de Cono de frío, nivel 11, ya estaba); con `world.currentPhase` en 2, en una copia del
  contenido, igual. Ninguna mejora deja un instantáneo con menos de 2 s de recarga (las que la acortan la dejan en 8,5 s o más),
  un área de daño sin casteo que no se pueda esquivar (Tajo ancho queda en un cono de radio 3, el tope de
  `instantConeMaxRadiusTiles`, y 120°; Abanico, 3 × 80°) ni un aura beneficiosa añadida más larga que la recarga (Gubia evasiva:
  Impulso, 3 s, en una recarga de 25 s).
- **Áreas más grandes** (secundarios tocados de 3 con el grupo disperso, sin mejora → con ella): Tajo ancho 1,2 → 1,8; Torbellino
  amplio 1,7 → 2,1; Abanico 1,0 → 1,4; Campo amplio 1,4 → 2,2; Sendero ancho 1,6 → 2,1. Estallido y Pulso ya tocan casi todo
  (2,9 y 3,0 de 3): por eso ninguna mejora suya es de radio.

### Parejas que se potencian o no se suman (el modelo suma y no las ve)
- **Velocidades:** Gubia evasiva, Impulso largo y Carrera no se suman (ADR-022, manda la más fuerte); el modelo cuenta 84 de
  movilidad para el Pícaro y en partida es menos.
- **Emboscada + Remate rápido:** el +60 % de daño dura 4,5 s y cubre Eviscerar y un Golpe siniestro más: la apertura del Pícaro en
  duelo (HU-119).
- **Hielo eterno (3,5 s de raíz) con Campo ardiente o Estallido rápido:** más tiempo dentro del área.
- **Carga aplastante (2 s) con Torbellino feroz o Tajo profundo.**
- **Golpe ágil y Bloqueo ligero liberan recurso** (200 de energía o 50 de ira por minuto) para los otros tres hechizos: el modelo
  mide cada hechizo solo y no lo ve.
- **Sanación rápida + Escudo grueso + Renovar intenso sobre el tanque:** el Sacerdote cura más por segundo (HU-117, jefe).
- **Pulso cegador + Sendero cegador:** el control del Sacerdote, en su tope (25).

### Lo que no se midió
- **Duelos** (HU-119 CA3): las mejoras de control mueven el triángulo. Gubia profunda (2,5 s) y Corte en seco (otra interrupción)
  van a favor del favorito en Pícaro > Mago (67 % al nivel 6); Frío profundo (5 s de ralentización) y Hielo eterno (3,5 s de raíz),
  en Mago > Guerrero (68 %); Carga aplastante (2 s), del Guerrero contra el Mago; Pulso y Sendero cegadores, del Sacerdote contra
  el control.
- **El jefe y los élites** con las mejoras de cura (HU-117).
- **El equipo real de HU-110:** todo usa `gear_factor`, como §HU-106.

### Desviaciones y propuestas (no aplicadas)
1. **Al nivel 8, el mono del Mago (45,8 de 45) y del Sacerdote (88,0 de 85) pasan de su valor de la clase** con las mejoras que
   suben el mono de Bola de fuego, Descarga y Estallido, y de Sanar, Escudo y Renovar; al 9 y al 10 se cumple. La causa es el rango
   del nivel 8 con el equipo del 7 (sin mejoras ya están al 88 % y al 86 %). Propongo dejarlo: el nivel de la Fase 2 es el 10 y al
   9 ya cumple.
   Si la regla tiene que cumplirse también al 8, esto lo deja en 44,6 y 83,4 (y el 10 en 41,4 y 73,5), a costa de dos mejoras que
   pasan a sumar +2:
   ```json
   { "id": "mage_frostbolt", "upgrades": [{ "id": "frostbolt_reach", "name": "Descarga lejana", "description": "+3 casillas de alcance.",
       "mods": [{ "stat": "range", "add": 3 }] }] },
   { "id": "priest_power_shield", "upgrades": [{ "id": "shield_thick", "description": "Absorbe un 10 % más.",
       "mods": [{ "aura": "priest_power_shield_aura", "stat": "amount", "mult": 1.1 }] }] },
   { "id": "priest_renew", "upgrades": [{ "id": "renew_strong", "description": "+10 % de cura por pulso; dura 3 s menos.",
       "mods": [{ "aura": "priest_renew_hot", "stat": "amount", "mult": 1.1 }, { "aura": "priest_renew_hot", "stat": "durationMs", "add": -3000 }] }] }
   ```
   (sustituyen a Lanza de hielo, Escudo grueso y Renovar intenso; las otras mejoras de esos hechizos no cambian).
2. **XP por hora:** sigue la desviación de §HU-106 al 8 y al 9 (18 % y 16 %, antes 19 % y 17 %; objetivo ≤ 15 %). Las mejoras no
   la agravan; sin propuesta nueva (HU-119).
3. **Observación de método:** el pentagrama de 30 s exactos va a saltos con las recargas y los casteos (decisión 2). Para la pasada
   de HU-119 conviene medir también los hechizos sin mejora con las ventanas de 24–36 s.
