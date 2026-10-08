# Clases: pentagrama, armas de referencia y grupos de hechizos

> Estructura aprobada el 2026-09-30 (ADR-020). `content/spells.json` y `content/auras.json` tienen estos 32 hechizos y sus
> auras. Los 16 de la Fase 1 (niveles 1–5) y los 8 de la Fase 2 (niveles 7 y 9, HU-106) tienen **números medidos**
> (`"provisional": false`, ver [balance-report.md](balance-report.md)) y sus **mejoras 1-de-2** (HU-107, §Mejoras); los 8 de
> la Fase 3 están escalados a la misma escala pero siguen **provisionales** (`"provisional": true`) hasta su pasada de balance.

## Pentagrama
Cada clase se define por 5 puntas, iguales para todas. En el Sacerdote, sus curas ocupan mono-objetivo y área; un hechizo
que cura aliados y daña enemigos (Pulso sagrado) cuenta en área.

| Clase | Mono-objetivo | Área | Control | Movilidad | Armadura | Total |
|---|---|---|---|---|---|---|
| Pícaro | 85 | 15 | 35 | 85 | 30 | 250 |
| Mago | 45 | 90 | 70 | 20 | 25 | 250 |
| Guerrero | 45 | 45 | 50 | 20 | 90 | 250 |
| Sacerdote | 85 (cura) | 85 (cura) | 25 | 25 | 30 | 250 |

Presupuesto **250** por clase y **100** como máximo por punta (`rules.balanceTargets.pentagram`). Con dos puntas de 85–90
quedan ~75 puntos para las otras tres (~25 cada una): el perfil se lee claro y ninguna clase queda sin herramientas para
subir sola (pilar 4). Una punta baja por perfil (el área del Pícaro) es intencional y no se compensa.
La armadura del Pícaro (30) y del Mago (25) se ajustó a lo medido: bajarla más los dejaba por debajo del piso de
supervivencia en solitario.

## Método de medición
Cada punta se mide con un número concreto al nivel 6 (grupo de la Fase 1) y al 15 (grupo completo), con equipo verde de su
nivel y el **arma de referencia** de la clase. Ese número se convierte a puntos dividiéndolo por un valor de referencia que
se calibra una sola vez y queda fijo en `rules.balanceTargets.pentagram`.

| Punta | Qué se mide |
|---|---|
| Mono-objetivo | Daño (o cura) por segundo sostenido durante 30 s contra un solo objetivo. |
| Área | Daño (o cura) por segundo sobre el objetivo principal y 3 secundarios agrupados (radio 2 casillas). Un hechizo de área cuenta **entero** aquí, también el daño a su objetivo principal. |
| Control | Segundos de control por minuto: aturdir 1, raíz 0,6, ralentizar 0,3 por cada 40 %, interrumpir 0,5 s, provocar 0,5. En área, +50 % por objetivo extra, hasta 3. |
| Movilidad | Casillas extra por minuto frente a caminar (saltos, embestidas, velocidad) más los segundos de control que la habilidad quita, convertidos a distancia. |
| Armadura | Segundos que se aguanta contra un atacante de referencia **físico** (todos los monstruos del Tier 1 pegan físico): vida, mitigación, escudos propios y reducciones de daño. Las curas y los escudos puestos a aliados no cuentan aquí (cuentan en mono-objetivo). |

- **Aporte de una habilidad** = puntos(base + habilidad) − puntos(base).
- **Valor de la clase en una punta** = base + los 4 mejores aportes a esa punta.
- **Base de la clase** = sus estadísticas + el básico de su arma de referencia. La velocidad de movimiento base es igual para
  todas (4 casillas/s), así que aporta 0 a movilidad. En el Sacerdote el básico no cuenta: sus puntas de mono y área son cura.
- **Valores de referencia** (100 puntos, nivel 6; `rules.balanceTargets.pentagram.references`): mono 26,6 de daño por
  segundo (el básico del Pícaro vale 30); área 63,8 (4 objetivos × 0,6 × mono); control 40 s equivalentes por minuto;
  movilidad 120 casillas extra por minuto; armadura 88,2 s (el aguante del Guerrero vale 70).
- **Herramienta:** `tools/balance/` (modelo en Python del `content-designer`) genera la tabla de aportes, comprueba la regla 40/75 en las 70
  combinaciones de cada clase y marca las parejas de habilidades que se potencian (el modelo suma aportes y no las ve).

### Armas de referencia y básico del arma
| Clase | Arma de referencia |
|---|---|
| Pícaro | daga |
| Mago | bastón |
| Guerrero | espada + escudo |
| Sacerdote | varita (no cuenta en su pentagrama; sí en su XP por hora) |

Para que un arma no rompa el perfil del rol:
1. El básico solo aporta a **mono-objetivo** (el escudo y las armaduras, a armadura). Ningún básico tiene área, control ni movilidad.
2. Todos los tipos de arma tienen el mismo presupuesto de daño por segundo para un mismo nivel y rareza; las de distancia
   rinden un 20 % menos (`rules.weapons.rangedDpsMult`). Lo comprueba el validador.
3. Con cualquier otra arma, la afinidad hace que la clase rinda igual o menos que con la de referencia: el pentagrama de la
   clase sigue siendo el techo.

## Regla 40/75
1. Ninguna habilidad suma más de **40 puntos** entre todas sus puntas.
2. Ninguna combinación de 4 habilidades, más la base, pasa del **75 %** del presupuesto (187 puntos).
3. Ninguna combinación supera el valor de la clase en ninguna punta.

Medido en la Fase 1: la habilidad que más suma es Pulso sagrado (39) y la combinación más cargada la del Guerrero (170 de
187). No hay límites en tiempo real (etiquetas, cooldowns compartidos): el freno contra cadenas de control entre varios
jugadores es la inmunidad de 1,5 s tras un control (ADR-022).

## Grupos de hechizos
8 por clase; se equipan 4 libremente. Desbloqueo en los niveles **1, 2, 3, 5, 7, 9, 11 y 13**
(`rules.progression.spellUnlockLevels`); con los rangos en 4, 8 y 12 (+15 % por rango, ADR-024) hay algo nuevo en 11 de los 15 niveles.
En la Fase 1 cada clase tiene 4 hechizos (todos equipados); la elección libre empieza en el nivel 7.

Formas: [obj] a un objetivo · [propio] sobre uno mismo · [suelo] área apuntada · [alrededor] área alrededor del lanzador ·
[cono] · [línea] · [salto]. Aportes en puntos del pentagrama (objetivo para el `content-designer`).

### Pícaro (base: mono 30, armadura 25)
| Hechizo | Nv | Forma | Qué hace | Aporta |
|---|---|---|---|---|
| Golpe siniestro | 1 | obj | golpe rápido, cooldown ~3 s | Mono 20 |
| Gubia | 2 | obj | aturde 2 s e interrumpe | Control 15, Mono 5 |
| Paso sombrío | 3 | salto | salto corto; +60 % de daño durante 3 s; al caer ralentiza 0,5 s | Mov 25, Mono 5, Control 5 |
| Carrera | 5 | propio | +50 % de velocidad; rompe raíz y ralentización | Mov 25 |
| Eviscerar | 7 | obj | golpe fuerte, cooldown largo | Mono 20 |
| Cuchillas arrojadizas | 9 | cono | daño en cono y velocidad propia | Área 10, Mov 15 |
| Tajo mortal | 11 | salto | salta a un punto, daña un área pequeña y enraíza 1 s | Mov 25, Área 5, Mono 5, Control 5 |
| Veneno debilitante | 13 | obj | daño en el tiempo y ralentiza 30 % | Mono 10, Control 10 |

### Mago (base: mono 15, armadura 15)
| Hechizo | Nv | Forma | Qué hace | Aporta |
|---|---|---|---|---|
| Bola de fuego | 1 | obj | casteo 2 s | Mono 20 |
| Descarga de escarcha | 2 | obj | daño y ralentiza 40 %, cooldown 12 s | Control 15, Mono 5 |
| Nova de escarcha | 3 | alrededor | enraíza 3 s | Control 30, Área 5 |
| Estallido de llamas | 5 | suelo | área, casteo 1,5 s | Área 30, Mono 5 |
| Campo ardiente | 7 | suelo | área pequeña, casteo 0,5 s, cooldown corto | Área 20 |
| Parpadeo | 9 | salto | teletransporte corto | Mov 20 |
| Cono de frío | 11 | cono | daño y ralentiza | Área 15, Control 20 |
| Meteoro | 13 | suelo | área grande, **casteo 2,5 s**, aturde 1 s | Área 25, Control 15 |

### Guerrero (base: mono 15, armadura 70)
| Hechizo | Nv | Forma | Qué hace | Aporta |
|---|---|---|---|---|
| Golpe heroico | 1 | obj | golpe de clase, cooldown ~3 s | Mono 15 |
| Provocar | 2 | obj | obliga al monstruo a atacarte 2 s | Control 15 |
| Carga | 3 | obj (embestida) | te lanza hacia el objetivo y lo aturde 1,5 s | Mov 15, Control 15 |
| Torbellino | 5 | alrededor | daño y mucha amenaza | Área 20, Mono 5 |
| Bloqueo con escudo | 7 | propio | −50 % de daño durante 4 s | Armadura 20 |
| Tajo amplio | 9 | cono | daño frontal | Área 15, Mono 5 |
| Corte de tendón | 11 | obj | ralentiza 40 % | Control 10, Mono 5 |
| Golpe poderoso | 13 | salto | salta a un punto; área pequeña que aturde 1 s | Área 10, Control 10, Mov 5 |

### Sacerdote (base: armadura 25)
| Hechizo | Nv | Forma | Qué hace | Aporta |
|---|---|---|---|---|
| Sanar | 1 | obj aliado | casteo 1–1,5 s | Mono 30 |
| Castigo | 2 | obj | daño a distancia (sin ralentización: se lanza sin parar y la dejaba permanente) | — (su daño lo vigila la XP por hora) |
| Palabra de poder: Escudo | 3 | obj aliado | escudo y +20 % de velocidad | Mono 20, Mov 10, Armadura 5 |
| Pulso sagrado | 5 | suelo | cura a aliados; daña poco y ralentiza a enemigos | Área 30, Control 10 |
| Renovar | 7 | obj aliado | cura en el tiempo | Mono 20 |
| Sendero de luz | 9 | línea | aliados: velocidad y cura pequeña; enemigos: ralentizados | Área 15, Mov 15, Control 10 |
| Himno | 11 | alrededor | cura en el tiempo a aliados cercanos | Área 40 |
| Oración desesperada | 13 | obj aliado | cura más si el aliado tiene menos del 40 % de vida | Mono 15 |

**Perfil en la Fase 1, medido** (mono / área / control / movilidad / armadura): Pícaro 57 / 0 / 20 / 50 / 32 · Mago 41 /
34 / 45 / 0 / 23 · Guerrero 35 / 21 / 29 / 16 / 70 · Sacerdote 51 / 29 / 10 / 11 / 29. Los números de cada hechizo y cómo
se midieron están en [balance-report.md](balance-report.md).

**Perfil en la Fase 2, medido al nivel 10** con los 6 hechizos (HU-106, `tools/balance/phase2.py`): Pícaro 65 / 10 / 20 / 65 / 27 ·
Mago 38 / 56 / 45 / 20 / 19 · Guerrero 34 / 33 / 29 / 16 / 88 · Sacerdote 64 / 40 / 19 / 24 / 25. Se aparta de los aportes de
arriba en dos hechizos: **Eviscerar** aporta mono 11 (no 20) porque más daño dispara la XP por hora del Pícaro, y **Campo
ardiente** aporta área 26 (no 20) para compensar que el Estallido pierde área al nivel 10. **Cuchillas arrojadizas** es un cono de
radio 3 sin casteo (ADR-027 D4).

**Sale del kit anterior:** Desgarrar, Escudo de maná, Rezo de sanación (lo reemplaza Pulso sagrado) y Hoja envenenada
(pasa a ser Veneno debilitante).

## Mejoras 1-de-2 (HU-107)
Cada hechizo de los niveles 1 a 9 tiene dos mejoras (`upgrades` en `content/spells.json`); se elige una desde el nivel 8 y se
cambia gratis fuera de combate (ADR-027 D1). Los de los niveles 11 y 13 las reciben en la Fase 3. Las dos de un hechizo cambian
cosas distintas y ninguna es mejor en todo: o suben puntas distintas del pentagrama, o suben la misma y una gana en lo que el
pentagrama no mide (recurso, alcance, valor de un lanzamiento, área con enemigos dispersos). Medidas y builds en
[balance-report.md](balance-report.md) §HU-107.

| Clase | Hechizo | Mejora A | Mejora B |
|---|---|---|---|
| Pícaro | Golpe siniestro | Filo afilado: +15 % de daño | Golpe ágil: −10 de energía |
| | Gubia | Gubia profunda: aturde 0,5 s más | Gubia evasiva: +25 % de velocidad 3 s |
| | Paso sombrío | Paso fugaz: recarga −1 s | Emboscada: el +60 % de daño dura 1,5 s más |
| | Carrera | Carrera larga: dura 2 s más | Huida: recarga −20 s, dura 4 s menos |
| | Eviscerar | Remate rápido: recarga −4 s | Corte en seco: además interrumpe |
| | Cuchillas arrojadizas | Abanico: +30° de apertura | Impulso largo: la velocidad dura 1 s más |
| Mago | Bola de fuego | Llama rápida: casteo −0,2 s | Llama intensa: +8 % de daño |
| | Descarga de escarcha | Frío profundo: ralentiza 1 s más | Lanza de hielo: +20 % de daño |
| | Nova de escarcha | Hielo eterno: raíz 0,5 s más | Nova cortante: el doble de daño |
| | Estallido de llamas | Estallido rápido: casteo −0,5 s | Estallido intenso: +20 % de daño, +5 de maná |
| | Campo ardiente | Campo amplio: radio +0,5 | Campo abrasador: +25 % de daño, recarga +1 s |
| | Parpadeo | Parpadeo ligero: −6 de maná | Parpadeo reparador: cura 12 + 20 % del poder de hechizo, recarga +5 s |
| Guerrero | Golpe heroico | Golpe brutal: +25 % de daño | Golpe desafiante: provoca 0,5 s, recarga +3 s |
| | Provocar | Grito lejano: +3 casillas de alcance | Provocación rápida: recarga −1,5 s |
| | Carga | Carga lejana: +2 de alcance, recarga +1 s | Carga aplastante: aturde 0,5 s más, recarga +2 s |
| | Torbellino | Torbellino amplio: radio +0,5 | Torbellino feroz: +15 % de daño |
| | Bloqueo con escudo | Muro de escudo: dura 2 s más, recarga +4 s | Bloqueo ligero: no gasta ira |
| | Tajo amplio | Tajo ancho: radio +0,5 (3) y +30° | Tajo profundo: +15 % de daño |
| Sacerdote | Sanar | Sanación rápida: casteo −0,25 s | Sanación potente: +15 % de cura |
| | Castigo | Castigo ardiente: +20 % de daño | Castigo rápido: casteo −0,5 s, +3 de maná |
| | Palabra de poder: Escudo | Escudo grueso: absorbe un 20 % más | Escudo ligero: −6 de maná, +2 de alcance |
| | Pulso sagrado | Pulso radiante: +15 % de cura, ralentiza 0,5 s menos | Pulso cegador: ralentiza un 40 %, −15 % de cura |
| | Renovar | Renovar duradero: dura 3 s más (un pulso) | Renovar intenso: +20 % por pulso, dura 3 s menos |
| | Sendero de luz | Sendero ancho: +1 de ancho, −15 % de cura | Sendero cegador: ralentiza 0,5 s más, la velocidad 0,5 s menos |

**Lo que limita cada clase al nivel 10** (builds de 4 hechizos con mejoras frente a 187,5 y al valor de la clase):
- **Guerrero:** su combinación más cargada ya sumaba 177 sin mejoras; Provocar, Carga, Torbellino y Bloqueo suben poco o
  cambian una cosa por otra (la armadura llega a 89,6 de 90 con Muro de escudo). Máximo con mejoras: 185.
- **Mago:** Parpadeo ya está en la movilidad de la clase (20 de 20): sus mejoras no dan movilidad (una cambia 5 de movilidad
  por armadura). Bola de fuego y Descarga comparten el mono, que llega a 43 de 45.
- **Pícaro:** la movilidad llega a 84 de 85 con Gubia evasiva, Paso fugaz, Huida e Impulso largo (el modelo suma velocidades
  que en el juego no se suman: manda la más fuerte). El área, a 12 de 15 con Abanico.
- **Sacerdote:** el control llega a 25 de 25 con Pulso cegador y Sendero cegador; Sendero cede velocidad para ralentizar más.

**Ideas que no entraron: necesitan un modificador que HU-104 no tiene** (criterio 4, pilar 6):
- **Provocar +1 s** (ejemplo de la HU): la duración es un campo del efecto `taunt`, y el modificador de efecto solo escala
  `damage`, `heal` y `restore_resource`. Entró la recarga en su lugar.
- **Parpadeo más largo o con dos cargas:** la distancia es `maxRange` del efecto `leap` (cambiar el `range` del hechizo no la
  mueve) y no hay cargas. Con la movilidad del Mago ya en su tope, tampoco cabía en la punta.
- **Cambiar la fuerza de un aura de modificadores** (Bloqueo −60 %, Carrera +70 %, Paso sombrío +80 % de daño, más velocidad
  en Escudo o Sendero): `pct` solo cambia la ralentización (`slow.pct`); `mods.damageTakenPct`, `damageDonePct` y `speedPct` no
  se pueden tocar. Entraron duración y recarga.
- **Ajustar un aura que añade la mejora** (p. ej. un Golpe bajo que ralentice un 30 % con la ralentización de Paso sombrío):
  el validador solo deja modificar auras que el hechizo ya aplica; un aura añadida llega con sus números.
- **Cambiar la forma o los objetivos:** Bola de fuego que salpica a los cercanos, Provocar en área, Golpe heroico a dos
  objetivos, Nova de escarcha lanzada a distancia. Necesitan un modificador de `targeting` o de forma.

**Descartadas por números** (cabían en HU-104, pero rompían un límite o pesaban mucho más que la otra mejora):
- **Renovar cura al instante un 30 %** (ejemplo de la HU): con recarga de 3 s es una cura directa que se repite; Renovar
  pasaba de 20 a 49 puntos (máximo 40) y el mono del Sacerdote a 102 de 85.
- **Bola de fuego −0,5 s de casteo / +20 % de daño** (ejemplo de la HU): +11,5 y +7 de mono; el Mago llegaba a 51 de 45.
- **Castigo −0,5 s de casteo sin más coste:** +13–19 % de XP por hora del Sacerdote; con +3 de maná se queda en +3,5–10 %.
- **Golpe heroico que provoca 1 s:** +12,5 de control frente a +2 de Golpe brutal; con 0,5 s las dos pesan parecido.
- **Ideas con auras nuevas** (contenido, no motor; fuera de esta HU): escudo de maná propio en Parpadeo, quemadura en el tiempo
  en Bola de fuego o Campo ardiente, sangrado en Golpe siniestro.

## Auras del kit (ADR-021)
- **Reutilizadas (9):** Cargado, Gubia, Veneno, Carrera, Helado (Descarga de escarcha y Cono de frío), Congelado (Nova),
  Bloqueo con escudo, Renovar y Escudo sagrado.
- **Nuevas (13):** daño extra y ralentización de Paso sombrío; velocidad de Cuchillas arrojadizas; raíz de Tajo mortal;
  ralentización de Veneno debilitante; aturdimiento de Meteoro; ralentización de Corte de tendón; aturdimiento de Golpe
  poderoso; velocidad de Palabra de poder: Escudo; ralentización de Pulso sagrado; velocidad y
  ralentización de Sendero de luz; cura en el tiempo de Himno.
- **Se quedan para items y monstruos:** Comiendo (Pan), Latigazo y Enfurecido (Capataz), Congelado (Rey Liche, Tier 3).
- **Mejoras (HU-107):** sin auras nuevas. Gubia evasiva reutiliza Impulso (Cuchillas arrojadizas); las demás cambian la duración,
  la fuerza o el porcentaje de las auras de su propio hechizo (una copia por mejora, con el mismo id).
- **Eliminadas:** Desgarro, Escudo de maná y la ralentización de Castigo (su control pasa a Pulso sagrado).
- Paso sombrío da "+daño durante 3 s" en lugar de "el siguiente golpe" para no añadir una mecánica de consumir la carga.
- Torbellino no tiene todavía un mecanismo de "mucha amenaza": hoy genera amenaza solo por su daño.

## Riesgos a vigilar (HU-084)
- El daño del Sacerdote no está en su pentagrama: su farmeo lo vigila la XP por hora (±15 %). Pega desde el nivel 1 con la
  varita (sin maná y recuperando maná), descansa menos porque se cura, y suma Castigo (nv 2) y Pulso sagrado (nv 5).
- El modelo suma aportes y no ve las parejas que se potencian (ralentizar + área, aturdir + golpe fuerte).
- La identidad del Pícaro en la Fase 1 depende del salto (HU-087).
- En la Fase 1 el Mago tiene mono y control igual de altos (objetivo 45 / 45, medido 41 / 45) y menos área (objetivo 35, medido 34); su área madura en la Fase 2 (al nivel 10, con Campo ardiente: 56, objetivo 55).
- Al subir solo el `base`, los rangos se notan poco con el equipo del nivel 9 (+4 % de mediana con el segundo rango): ver la
  propuesta de `spellRankBonusPct` en [balance-report.md](balance-report.md) §Rangos.
- 16 hechizos nuevos necesitan íconos, efectos visuales y marcas de área (3 de ellos en la Fase 1).
- Los valores de referencia se calibran una sola vez: cambiarlos después reescala todas las clases.
- Castear moviéndose da a los casteos largos un valor (alejarse, esquivar) que el pentagrama no mide; primera palanca si
  sobra: bajar `rules.combat.castMoveSpeedMult`.
