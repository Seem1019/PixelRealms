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
