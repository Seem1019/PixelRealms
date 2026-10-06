# Documento de diseño (GDD)

> Regla de oro: **este documento explica el porqué y las fórmulas; `content/` tiene los valores.** Toda constante
> numérica citada aquí vive en `content/rules.json` (se indica entre paréntesis como `rules.x.y`). Si un número de
> este documento y `rules.json` difieren, manda `rules.json` y hay que corregir este documento.

## Pilares
1. **Juntos en 5 minutos:** PixelRealms es para un grupo cerrado de ~20 amigos. Un jugador nuevo pasa de abrir el
   enlace en el navegador a pelear en grupo con un amigo en ≤ 5 minutos. Con amigos de hasta 2 niveles de diferencia,
   jugar en grupo rinde al menos lo mismo que jugar solo.
2. **La subida de 1 a 15 es el juego:** no hay endgame: el contenido es subir de nivel, y tiene que disfrutarse y durar
   (objetivo: 20–30 horas de juego del 1 al 15 con una clase). Los primeros niveles llegan rápido, cada 2–3 niveles algo mejora de forma visible (un rango de hechizo, una zona o
   un jefe) y empezar otra clase nunca es tedioso.
3. **Pocas piezas, mucho juego:** cada sistema usa las mínimas reglas que lo hacen divertido. Cada clase tiene como
   máximo 8 hechizos, lleva 4 equipados a la vez y los mejora por rangos al subir de nivel; la variedad sale de combinar
   hechizos y mejoras (builds), no de sumar mecánicas. Está lo básico de un MMO (daño en área, control, críticos, daño y
   curación en el tiempo, escudos) y no hay elementos, combinaciones elementales ni estados que interactúan entre sí.
4. **Cualquier clase es igual de buena elección:** cada clase tiene un rol de grupo único (tanque: Guerrero · daño
   físico: Pícaro · daño mágico: Mago · sanador: Sacerdote) y su propia forma de subir en solitario. Con cualquier clase
   se sube del 1 al 15 a un ritmo parecido (medido en XP por hora contando descansos, no en tiempo por kill) y en grupo
   cada rol se nota. Cualquier clase equipa cualquier item, pero fuera de rol rinde menos que el especialista. Ningún
   contenido exige una composición concreta. *Márgenes provisionales, a fijar en HU-084: fuera de rol 55–65 % del
   especialista; XP/hora entre clases ±15 %.*
5. **Botín sin discusiones:** cada item que cae tiene dueño desde el primer momento (lo asigna el servidor, al azar
   dentro del grupo) y todo lo que aporta se ve como números en su tooltip, comparado con lo que llevas puesto. Entre
   amigos nunca hay que negociar un reparto; si el item no te sirve, lo intercambias.
6. **Pequeño y terminado, por fases:** el MVP completo son 3 tiers y 15 niveles, sin endgame, construido en 3 fases (una
   por tier). Cada fase termina jugable y probada con amigos antes de empezar la siguiente; la Fase 1 (Tier 1: Aldea,
   Campos, Colinas, Mina Abandonada y Capataz Grask; niveles 1–6) es lo primero que se juega. Una feature entra en una
   fase solo si se usa en ella y sirve a otro pilar; lo que solo prepara fases futuras o el post-MVP no se detalla ni se
   generaliza antes de tiempo. "Terminada" = cada acción del jugador tiene respuesta visible y no hay bugs conocidos que
   corten una sesión.
7. **Duelos entre amigos, sin consecuencias:** el PvP es siempre un duelo 1 vs 1 aceptado por ambos, sin pérdidas de
   ningún tipo y posible en cualquier sitio. Cada clase tiene rivales favorables y desfavorables reconocibles (Mago >
   Guerrero > Pícaro > Mago; el Sacerdote gana por desgaste y pierde contra el control), pero ninguna gana siempre: con
   nivel y equipo iguales, el favorito gana entre el 60 % y el 75 % de los duelos (objetivo provisional, HU-084).

## Bucle principal
Explorar → matar monstruos → botín/XP → subir nivel (hechizo nuevo o rango) → mejor equipo → zona más difícil → cueva de
transición con jefe (en grupo) → siguiente tier. Entre medias: duelos con amigos e intercambio de items.

## Mundo
Nivel máximo **15** (`rules.progression.maxLevel`), tres tiers, dos cuevas de transición y una fortaleza final.
**El MVP completo son los tres tiers** y se construye en tres fases, una por tier: cada fase se juega y se prueba antes
de empezar la siguiente. Tope de nivel por fase: 6 / 10 / 15 (`rules.progression.levelCapByPhase`, fase activa en `rules.world.currentPhase`).
Biomas distintos por tier ⇒ **un tileset por tier**, no por zona. Todas las cuevas comparten el tileset "interior"
cambiando la paleta (mina marrón, cripta verde, fortaleza gris).

```
TIER 1 · Pradera (nv 1-5)                        ← Fase 1
   [Aldea Robledal · hub] ── [Campos 1-3] ── [Colinas 3-5]
                                                 │
                                    ⟨Mina Abandonada⟩  jefe: Capataz Grask nv 6
                                                 │ salida
TIER 2 · Bosque (nv 6-10)                        ▼   ← Fase 2 (diseñado)
   [Linde del Bosque 6-8] ── [Pantano 8-10]
          │ atajo: puente roto (se baja desde el Bosque)
          └────► Colinas                    ⟨Cripta de Raíces⟩  jefe: Árbol Podrido nv 11
                                                 │ salida
TIER 3 · Montaña (nv 11-15)                      ▼   ← Fase 3 (diseñado)
   [Paso Nevado 11-13] ── [Ruinas 13-15]
          │ atajo: teleférico (se activa desde la Montaña)
          └────► Aldea                      ⟨Fortaleza⟩  jefe final nv 15 (sin salida)
```

### Zonas abiertas (overworld)
- Todas las zonas de un tier viven en **un solo mapa** (`meadow` para el Tier 1): se recorren sin cambio de mapa,
  delimitadas por cuellos de botella naturales, no por portales.
- Se dimensionan por **tiempo de caminata**: cruzar una zona toma 25–40 s (`rules.world.zoneCrossTimeSecTarget`), ~100×100
  tiles de área útil a 4 tiles/s (`rules.movement.baseSpeedTilesPerSec`). Mapas compactos a propósito: con ~20 amigos, se
  cruzan entre ellos (decisión 2026-10-03; el objetivo anterior de 60–90 s no cuadraba con ese tamaño).
- Cada zona tiene un **punto de referencia visible** (torre, árbol enorme, lago), un **camino principal obvio**
  (sendero) con ramas laterales opcionales que dan recompensa (cofre, mob raro, recurso) y **2–3 campamentos de
  monstruos** con subniveles: los de menor nivel cerca de la entrada, los de mayor hacia la salida.
- **Un punto seguro por zona** (fogata/santuario) donde reaparecer; no se manda a todos al hub.
- El **atajo de vuelta** de cada tier se desbloquea desde el lado superior: la cueva es el camino de ida, volver es gratis.

### Cuevas de transición (mazmorras)
- Son **mapas aparte** (`mapId` propio) a los que se entra por **portal** (ADR-007). En el MVP hay **una sola copia**
  compartida por todos los jugadores: el jefe es común y reaparece (`respawnSec`). Las instancias por grupo son post-MVP y
  solo requieren permitir varias copias de un mapa que ya existe.
- Entre 3 y 5 salas conectadas por pasillos, 5–10 min de recorrido:
  ```
  Entrada → Sala 1 (mobs) → Sala 2 (mobs + trampa/puzle simple)
                               ├──► Sala del jefe (rama lateral)
                               └──► Sala 3 (mobs élite) → Salida al tier siguiente
  ```
- Dentro de la cueva **no hay más cambios de mapa**: las salas se delimitan con monstruos, puertas y puzles.

### Tier 1 (Fase 1) en detalle
| Zona | Mapa | Niveles | Monstruos |
|---|---|---|---|
| Aldea Robledal (hub, `safe`) | `meadow` | — | Marta la tendera, cementerio, duelos permitidos |
| Campos | `meadow` | 1–3 | Slime (1), Jabalí (2), Bandido (3) |
| Colinas | `meadow` | 3–5 | Lobo de las colinas (4), Goblin arquero (5, `hard`) |
| Mina Abandonada | `mine` | 4–6 | Kóbold minero (5), Gólem de escombros (6, `elite`), **Capataz Grask (6, `boss`)** |

## Clases
| Clase | Recurso | Rol | Equipo con afinidad **alta** | Identidad en PvP |
|---|---|---|---|---|
| Guerrero | Ira (sube por golpe dado/recibido, baja fuera de combate) | Tanque / melee | Espada, Hacha, Maza · Placas, Malla, Escudo | Aguanta y alcanza (Carga, Bloqueo) |
| Pícaro | Energía (regenera sola) | Daño melee burst | Daga, Espada · Cuero | Controla y rompe control (Gubia, Carrera) |
| Mago | Maná | Daño mágico a distancia, control | Bastón, Varita · Tela | Mantiene la distancia (Escarcha, Nova) |
| Sacerdote | Maná | Curación, escudos | Maza, Bastón, Varita · Tela, Malla | Desgaste (curas + Castigo) |

### Modelo de combate: híbrido (ADR-015)
Mezcla de tab-target y combate de acción, al estilo de Albion Online:
- **Un objetivo = tab-target.** Ataque básico, hechizos de daño o cura a un objetivo, control a un objetivo, Provocar y
  Carga: se selecciona el objetivo (clic o Tab) y el servidor resuelve el impacto. No se esquivan moviéndose; solo con la
  tabla de impacto (fallo, esquiva).
- **Áreas = se apuntan libremente.** Un hechizo de área se lanza sobre el punto del suelo que marca el cursor, dentro de
  su alcance (`ground`), o alrededor del lanzador (`self`), sin necesidad de objetivo seleccionado. Formas: círculo, cono y
  línea (`shape`, ya en el schema); el cono y la línea salen del lanzador hacia el punto apuntado. El círculo se implementa
  en la Fase 1 y el cono y la línea cuando un hechizo los necesite. Las áreas de daño llevan casteo para poder esquivarlas,
  salvo los conos cuerpo a cuerpo (radio ≤ 3 tiles), que se esquivan saliendo del frente del lanzador (ADR-027).
- **Saltos y embestidas.** Carga (embestida a un objetivo) es tab-target; los saltos a un punto (`leap`) se apuntan como
  las áreas. El servidor mueve al personaje (valida alcance, casilla libre y línea de visión) y el cliente no lo predice:
  solo suaviza el desplazamiento al dibujarlo.
- **Las áreas se ven y se esquivan.** El punto se fija al empezar el casteo, todos ven la marca en el suelo durante el
  casteo y el área se resuelve al terminar, con quien esté dentro en ese momento. Salir de la marca esquiva el golpe.
  Vale igual para monstruos: el Golpe de pico del Capataz es un área marcada. Por eso las áreas apuntadas de daño deben
  tener casteo o retardo visible (regla para el rediseño de kits).
- **Castear ralentiza, no inmoviliza (ADR-019).** Se puede mover mientras se castea al **50 %** de la velocidad
  (`rules.combat.castMoveSpeedMult`). Moverse no corta el casteo y recibir daño tampoco: **solo lo cortan los controles que
  impiden castear (aturdir y silenciar) y las habilidades de interrumpir**, y tras un corte no se puede castear durante 1,5 s
  (`interruptLockoutMs`). Raíz y ralentización no cortan. Silenciado (o recién interrumpido) no se lanza ninguna habilidad, pero
  sí se beben pociones y se ataca con el arma.
  - Si al terminar un hechizo a un objetivo este quedó fuera de alcance (con tolerancia de 1,5 casillas) o de línea de
    visión, el casteo falla sin gastar recurso ni cooldown (el cooldown global sí).
  - En las áreas, el punto, el origen y la dirección se fijan al empezar y no se vuelve a comprobar el alcance al terminar:
    una marca que todos ya vieron no se cancela porque el lanzador se movió.
  - Usar otra habilidad o un salto durante un casteo lo cancela (sin coste). Las pociones no lo cancelan.
- Sin fuego amigo: las áreas enemigas no dañan a aliados. El servidor valida el punto (alcance + `castRangeToleranceTiles`,
  línea de visión al punto); el cliente solo envía la intención (`CastSpell{targetPos}`).
- De Albion solo se toma este combate híbrido: no hay habilidades por arma ni por pieza de armadura, ni una energía común.
  Las habilidades salen de la clase, que define el rol (ADR-016).

### Hechizos, rangos y builds
- Cada clase tiene **como máximo 8 hechizos** y lleva **4 equipados** a la vez, elegidos libremente de su grupo (sin
  casillas con tipo). La barra tiene además **4 casillas de
  utilizables** (poción de vida y otros consumibles). Teclas: **1–4** hechizos, **5–8** utilizables
  (`rules.loadout`).
- Los hechizos **mejoran por rangos** al subir de nivel, en lugar de aprender uno nuevo cada pocos niveles.
  **Fase 1:** los rangos suben solos. **Desde la Fase 2:** en el nivel 8 (segundo rango) cada hechizo elige **1 de 2
  mejoras**, y el +15 % del rango sigue siendo automático; las builds salen de qué 4 hechizos llevas equipados y qué mejoras
  eliges. La mejora se cambia gratis fuera de combate desde el libro de hechizos, y esa es la forma de **reiniciarla** (ADR-027).
- **Rangos (ADR-024, confirmado):** los hechizos suben de rango en los niveles **4, 8 y 12** (`rules.progression.spellRankLevels`)
  con **+15 %** sobre el valor base por rango (`spellRankBonusPct`), lineal (+15 / +30 / +45 %) y sin escalar los coeficientes
  (ADR-027). En la Fase 1 (tope 6) solo se alcanza la subida del nivel 4.
- **Controles y acumulación (ADR-022):** tras un aturdimiento, raíz o silencio, 1,5 s de inmunidad a los tres; efectos del
  mismo tipo no se suman (manda el más fuerte) y la ralentización máxima es del 40 % (`combat.md` §Auras).
- **Balance por pentagrama y grupos de hechizos:** `docs/design/class-kits.md` (ADR-020). Cada clase tiene 8 hechizos que
  se desbloquean en los niveles 1, 2, 3, 5, 7, 9, 11 y 13 (`rules.progression.spellUnlockLevels`): en la Fase 1 tiene 4
  (todos equipados) y la elección libre empieza en el nivel 7. Pulso sagrado (nv 5) reemplaza a Rezo de sanación.
  Los hechizos equipados se cambian fuera de combate, en cualquier sitio y sin coste (ADR-027).

### Ataque básico (todas las clases)
- **El ataque básico lo da el arma equipada, no la clase, y no ocupa ninguna de las 4 casillas de hechizo.** Cada tipo de
  arma tiene su propio básico (ADR-019, `rules.weapons`). Con el equipamiento libre, cualquier clase pega desde el nivel 1
  con el arma que lleve.

| Tipo | Alcance | Velocidad típica | Daño | Animación |
|---|---|---|---|---|
| Daga | 1,25 casillas | rápida (1,6 s) | físico, bajo por golpe | estocada |
| Espada | 1,5 | media (2,4 s) | físico | tajo |
| Maza | 1,5 | media (2,6 s) | físico | golpe |
| Hacha | 1,5 | lenta (3,0–3,4 s) | físico, alto por golpe | tajo pesado |
| Varita | 7 | rápida (2,0 s) | mágico, bajo | proyectil |
| Bastón | 5 | lenta (3,0 s) | mágico, alto; da poder de hechizo | proyectil pesado |

- La velocidad y el daño concretos están en cada item. Todos los tipos tienen el mismo presupuesto de daño por segundo por
  nivel y rareza; las armas a distancia rinden un 20 % menos (`rules.weapons.rangedDpsMult`). El básico solo hace daño a un
  objetivo: ningún arma da área, control ni movilidad.
- **Convivencia con los hechizos:** el básico sigue solo mientras haya objetivo en alcance y no se esté casteando. Un
  hechizo instantáneo no reinicia su temporizador, pero tiene un bloqueo de animación de 250 ms (`abilityLockMs`) en el que
  no sale el básico ni otro hechizo (si tocaba, sale al terminar). Los hechizos de clase tienen cooldown global de 1 s salvo los
  marcados `triggersGcd: false` (Provocar, Carga, Carrera, Bloqueo con escudo, Parpadeo); el básico y los usables no. Ningún hechizo instantáneo de clase tiene menos de 2 s de cooldown
  (`minInstantSpellCooldownMs`): Golpe siniestro y Golpe heroico quedan en ~3 s y no llegan a ser un segundo básico.
- Toda arma tiene un ataque básico que **no consume recurso**. Su único límite es la velocidad de ataque:
  `swingMs = arma.speedMs / clase.haste` (`rules.classScaling.<clase>.haste`).
- El stat de escalado del arma (`items[].scaling`) decide la escuela: espada/hacha/maza (`str`) y daga (`agi`) hacen
  daño **físico** con `attackPower`; bastón y varita (`int`) hacen daño **mágico** con `spellPower` a su alcance (varita 7, bastón 5).
- Un Sacerdote o un Mago puede farmear solo con varita sin gastar maná; también puede usar espada, con menor rendimiento.
- **Maná por golpe:** todo personaje con maná recupera maná con cada ataque básico que impacta, sea cual sea el arma:
  `maná = maxMana · manaPerBasicHitPctPerSec · (swingMs / 1000)`, valor base **1,5 %** (`rules.combat.manaPerBasicHitPctPerSec = 0.015`).
  Se normaliza por `swingMs` para que un arma rápida no dé más maná por segundo que una lenta. Fallos y esquivas no dan
  maná, y el temporizador del ataque básico no avanza mientras se castea (si no, castear sin parar también recuperaría
  maná). Referencia (Mago nv 4, ~240 de maná, bastón): lanzando Bola de fuego sin parar se queda sin maná en ~26 s;
  alternando 2 hechizos por 1 básico aguanta ~80 s (un combate de jefe); alternando 1:1 aguanta más de 6 minutos.

### Equipamiento libre con afinidad
- **Cualquier clase equipa cualquier item.** No existen errores `wrong_class` ni `cannot_equip` por tipo; solo `level_too_low`.
- Cada clase tiene una afinidad **alta / media / baja** con cada tipo de arma y armadura (`rules.affinity.byClass`).
  La afinidad multiplica **toda** la contribución numérica del item: daño del arma, armadura, `spellPower` y stats
  (`rules.affinity.multipliers`: alta ×1.0, media ×0.85, baja ×0.7).
- Además, cada clase convierte los stats primarios en derivados con su propia tabla (`rules.classScaling`): un Mago
  saca `attackPower` sobre todo de `int` (×1.4) y poco de `str` (×0.6); un Pícaro al revés. Por eso un Mago con espada
  pega, pero menos que un Pícaro con la misma espada, y un Pícaro con placas aguanta, pero menos que un Guerrero.
- `classes[].recommendedWeapons/recommendedArmor` son solo informativos: el tooltip muestra "Afinidad: alta/media/baja".
- **Piso de viabilidad** (márgenes provisionales, se fijan en HU-084)**:** fuera de rol se rinde entre el 55 % y el
  65 % del especialista en daño con básicos, y entre el 50 % y el 60 % en aguante.
  Cualquier clase con cualquier equipo completa el contenido en solitario (ver `combat.md` §Afinidad).

### Triángulo de ventajas (PvP 1 vs 1, nivel y equipo equivalentes)
**Mago > Guerrero > Pícaro > Mago.** Significativa, no absoluta: la habilidad y el equipo pueden revertirla.
Objetivo provisional (HU-084): con nivel y equipo iguales, el favorito gana entre el 60 % y el 75 % de los duelos.
> *Desactualizado:* los cooldowns y duraciones citados abajo son anteriores al balance de la Fase 1 (hoy Carga 16 s, Nova 18 s,
> Gubia 2 s; `content/spells.json` manda). El triángulo se mide jugando en HU-084 (`balance-notes.md` §6).
- *Mago > Guerrero:* Escarcha (ralentiza) y Nova (raíz) mantienen al Guerrero lejos; Carga tiene 15 s de CD frente a
  Nova 20 s, así que el Guerrero llega una vez y luego vuelve a quedarse atrás.
- *Guerrero > Pícaro:* más vida, armadura y Bloqueo; el Pícaro no puede alejarse sin dejar de pegar, y su Gubia se
  gasta rompiendo… nada, porque el Guerrero no castea.
- *Pícaro > Mago:* Gubia aturde e interrumpe el casteo; Carrera rompe raíz y ralentización y le da alcance. El Mago
  solo se salva si acierta la Nova antes de la Gubia.
- El **Sacerdote** queda fuera del triángulo: gana al Guerrero por desgaste (lo cura más de lo que él daña), pierde con
  el Pícaro (aturdimiento + burst antes de que la cura salga) y va parejo con el Mago (burst vs. curas, guerra de maná).
- Palanca: `rules.classAdvantage` (multiplicador de daño atacante → defensor, en básicos, hechizos y DoT). Desde HU-084
  (2026-10-03) no es una palanca fina: con todo en 1.0 los duelos salían 0–100 %, y la matriz actual (Guerrero→Pícaro 0,63,
  Guerrero→Mago 0,38, Pícaro→Mago 0,54, Pícaro→Sacerdote 0,93, Mago→Sacerdote 1,45, Sacerdote→Guerrero 1,12) deja al favorito
  en 63–68 % en duelos simulados. Hay que confirmarlo jugando (`balance-report.md`, pasada de HU-084).

## Progresión
Todas las constantes en `rules.progression` y `rules.group`.
- **XP para subir: curva por tiempo** (ADR-017). Se diseña en minutos de juego por nivel y la XP se calcula:
  `xpParaSubir(L) = round(minutesPerLevel[L] · (60 / killCycleSecTarget) · xpMonstruoNormal(L))`, con
  `killCycleSecTarget = 36` (segundos entre kills contando pelea, descanso, botín y caminar; 36 s es lo que da el modelo
  de balance de la Fase 1, ver `balance-report.md`; se confirma jugando en HU-084).
  `xpRate` (1.0) multiplica toda la XP ganada (playtests, eventos).

| Nivel | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Minutos | 10 | 20 | 35 | 50 | 65 | 85 | 100 | 115 | 130 | 150 | 165 | 180 | 195 | 210 |
| XP para subir | 100 | 367 | 933 | 1 750 | 2 817 | 4 392 | 6 000 | 7 858 | 9 967 | 12 750 | 15 400 | 18 300 | 21 450 | 24 850 |

- Total: 126 934 XP ≈ 2 517 kills ≈ **25,2 h** (Fase 1: 3 h · Fase 2: 7,2 h · Fase 3: 15 h). Subir a nivel 2 son ~17 slimes.
  Objetivo 20–30 h (`rules.balanceTargets.hoursToMaxLevel`). Si el ciclo real por kill no es 36 s, se cambia
  `killCycleSecTarget` y toda la curva se corrige sola.
- **XP de contenido futuro** (misiones, mazmorras…): se define en minutos equivalentes,
  `xp = minutos · (60 / killCycleSecTarget) · xpMonstruoNormal(nivel)`, así el contenido nuevo no desajusta la curva. Un tier
  nuevo añade filas a `minutesPerLevel`.
- **XP por monstruo** (ya no se escribe en `monsters.json`, se calcula): `round((5 · nivel + 1) · tipo)` con
  `tipo`: normal 1.0 · hard (a distancia / con mecánica) 1.2 · élite 3 · jefe 10.
- **Modificador por diferencia de nivel:** `diff = nivelMonstruo − nivelReferencia`; si `diff ≤ −5` → 0 XP (gris);
  si no `mod = 1 + 0.1 · clamp(diff, −4, +4)`.
- **Solo:** `nivelReferencia = nivelJugador`; `xp = xpMonstruo · mod`.
- **En grupo:**
  ```
  activos = miembros vivos, a ≤ 40 tiles, que en los últimos 90 s hicieron daño, curaron, recibieron daño o usaron una habilidad
  N = |activos| ; nivelMax = max nivel entre activos ; nivelReferencia = nivelMax
  peso_i = max(0.10, 0.75 ^ max(0, (nivelMax − nivel_i) − 2))
  pool   = xpMonstruo · mod · bonoGrupo(N)          // bonoGrupo: 1.00, 1.30, 1.55, 1.80, 2.00
  xp_i   = pool · peso_i / Σ peso
  ```
  Ejemplo: niveles 10/8/5 matan un monstruo normal de nivel 9 (46 XP): mod 0.9, bono 1.55, pesos 1/1/0.42 → 26.5 / 26.5 / 11.2.
  No hay piso: la penalización por brecha es intencional para evitar el *carry*.
- Al subir de nivel: stats `+statsPerLevel`, vida y recurso llenos, hechizos nuevos o rangos según `levelReq`.

## Cambio de clase
Cambiar de clase nunca debe ser tedioso.
- **Fases 1 y 2:** un NPC en la Aldea cambia la clase del personaje conservando nivel, items y oro (con equipo libre, el
  equipo sigue sirviendo, con la afinidad de la nueva clase).
- **Fase 3:** se retira el NPC. Para probar otra clase se crea un personaje nuevo, que gana **XP ×2** mientras esté por
  debajo del nivel más alto de la cuenta; la cuenta tiene un **almacén compartido** entre sus personajes para pasarle equipo.

## Jefes
- Objetivo de diseño de un jefe de nivel **B** (`rules.boss`): **3 jugadores de nivel B−2** con equipo de su nivel lo
  matan en **60–100 s**; **2 jugadores de nivel B** también; **1 de nivel B+1 con 1 de nivel B−1** también, más fácil.
  Nadie lo mata solo a nivel equivalente (`soloKillable: false`). En la Fase 1 (tope 6) el caso B+1 con B−1 del Capataz no
  existe: se valida al abrir la Fase 2.
- Los jefes son inmunes a aturdir, raíz y ralentizar (`rules.combat.bossImmuneToAuraKinds`).
- Todo el contenido que no es jefe o minijefe (`type: boss|elite`) lo completa cualquier clase en solitario.

## Botín
- Cada entrada de la tabla se tira **independientemente** (`chance`). Los jefes además tienen `groups`: de cada grupo
  caen exactamente `rolls` items elegidos por peso (botín garantizado).
- **Cada item que cae se asigna al azar, de forma uniforme, a un miembro elegible** del grupo (vivo, a ≤ 40 tiles;
  `rules.loot.ownerMode = random_per_item`). El sorteo es por item, no por el total: con 4 jugadores cada uno tiene
  25 % por item, y que uno se lleve todo es posible pero raro.
- El cadáver **brilla solo para quien ganó algo**; todos pueden abrirlo y ver qué cayó, pero solo el dueño de cada item
  puede tomarlo. Tras 30 s (`exclusiveSec`) lo no reclamado queda libre; el cadáver dura 60 s.
- El oro se reparte a partes iguales (`goldSplit`). Items `uncommon+` se anuncian en el chat de grupo.
- **Rareza por color** en el nombre, el tooltip y la ventana de botín: junk gris, common blanco, uncommon verde, rare azul, epic morado.
- **Intercambio entre jugadores** (ventana de trade con doble confirmación) forma parte del MVP.

## Economía
- Monedas en cobre (`1 oro = 100 plata = 10 000 cobre`). Monstruos sueltan cobre (`gold` en tabla de botín).
- Vendedor compra a `sellPrice` y vende a `sellPrice × 4` (`rules.economy.vendorBuyMultiplier`); `vendorPrice` solo para excepciones.
- Marta vende consumibles y las armas básicas de nivel 1 (para que cualquiera pueda probar otra arma).

## Muerte
- Al morir: pantalla "Has muerto" → *Reaparecer* → punto seguro más cercano con 50 % de vida/recurso. Sin pérdida de items ni XP.
- En duelo no se muere: ver PvP.

## PvP amistoso (duelos)
- `/duel Nombre` o clic derecho → "Retar". El otro acepta o rechaza (expira en 30 s). Cuenta atrás de 3 s y empieza.
- Termina cuando un participante baja al 1 % de vida (`endAtHpPct`), pasa más de 5 s fuera de la zona del duelo, se desconecta o se rinde
  (`/rendirse`). Al terminar nadie se cura: cada uno se queda con la vida con que acabó y pierde las auras del rival. Como en
  Albion, quien pierde por vida se recupera un poco más rápido (× 2 y sin esperar a salir de combate) hasta la vida con que
  empezó el duelo o hasta volver a pelear; rendirse no da recuperación y el ganador se cura como siempre. Sin pérdida de XP, oro, items ni durabilidad. No se puede retar ni aceptar en
  combate, y un duelista en duelo activo no es aliado de nadie más (ni cura ni lo curan).
- **Zona del duelo (HU-101):** al aceptar aparece un círculo de 12 casillas alrededor de los dos, que solo ven ellos. Quien sale
  ve un aviso y tiene 5 s para volver (el plazo se recupera estando dentro, no por volver un instante); si lo agota, pierde. Así
  nadie gana huyendo ni usa el duelo para cruzar el mapa.
- Permitido en la aldea y en cualquier zona. Los monstruos ignoran a los duelistas y viceversa (no se puede usar un mob de escudo).
- Nota técnica (ADR-011): un `PvpRuleset` (`rules.pvp.rulesets`) define quién puede atacar a quién, cómo termina y qué
  se pierde. El MVP solo activa `duel`; PvP grupal y abierto son post-MVP.

## Social
- Chat: `say` (radio 20 tiles), `global`, `party`, susurro `/w Nombre`.
- Grupo hasta 5 (`rules.group.maxMembers`), XP y botín según las reglas de arriba.

## Configuración
Toda variable numérica está en `content/rules.json`, validada por `content/schemas/rules.schema.json` y cargada como
`RulesDb` inmutable. El admin puede recargarla en caliente con `/reload rules`. Nada de esto se hardcodea.

## Fuera del MVP (backlog "después")
Instancias por grupo, PvP grupal y abierto, oficios, gremios, subasta, monturas, árboles de talentos (las builds salen
de los hechizos equipados y sus mejoras), misiones, más de 8 hechizos por clase, sonido espacial.
