# Documento de diseño (GDD)

> Regla de oro: **este documento explica el porqué y las fórmulas; `content/` tiene los valores.** Toda constante
> numérica citada aquí vive en `content/rules.json` (se indica entre paréntesis como `rules.x.y`). Si un número de
> este documento y `rules.json` difieren, manda `rules.json` y hay que corregir este documento.

## Pilares
1. **Jugar con amigos en 5 minutos:** entrar por navegador, crear personaje, formar grupo, pelear.
2. **Roles claros, equipo libre:** tanque (Guerrero), daño físico (Pícaro), daño mágico (Mago), sanador (Sacerdote).
   Cualquier clase puede llevar cualquier equipo; el equipo de su rol rinde claramente mejor (afinidad).
3. **Botín que se siente:** rarezas por color, equipo que cambia números visibles, cada item cae para alguien concreto.
4. **Pequeño pero pulido:** el MVP es el Tier 1 completo (Aldea, Campos, Colinas, Mina Abandonada y su jefe).
5. **PvP amistoso:** duelos por consentimiento mutuo, sin penalizaciones, con un triángulo de ventajas entre clases.
   La arquitectura permite crecer a PvP grupal y abierto sin reescribir (`rules.pvp.rulesets`).

## Bucle principal
Explorar → matar monstruos → botín/XP → subir nivel (nuevo hechizo) → mejor equipo → zona más difícil → cueva de
transición con jefe (en grupo) → siguiente tier. Entre medias: duelos con amigos e intercambio de items.

## Mundo
Nivel máximo **15** (`rules.progression.maxLevel`), tres tiers, dos cuevas de transición y una fortaleza final.
Biomas distintos por tier ⇒ **un tileset por tier**, no por zona. Todas las cuevas comparten el tileset "interior"
cambiando la paleta (mina marrón, cripta verde, fortaleza gris).

```
TIER 1 · Pradera (nv 1-5)                        ← MVP
   [Aldea Robledal · hub] ── [Campos 1-3] ── [Colinas 3-5]
                                                 │
                                    ⟨Mina Abandonada⟩  jefe: Capataz Grask nv 6
                                                 │ salida
TIER 2 · Bosque (nv 6-10)                        ▼   ← diseñado, backlog
   [Linde del Bosque 6-8] ── [Pantano 8-10]
          │ atajo: puente roto (se baja desde el Bosque)
          └────► Colinas                    ⟨Cripta de Raíces⟩  jefe: Árbol Podrido nv 11
                                                 │ salida
TIER 3 · Montaña (nv 11-15)                      ▼   ← diseñado, backlog
   [Paso Nevado 11-13] ── [Ruinas 13-15]
          │ atajo: teleférico (se activa desde la Montaña)
          └────► Aldea                      ⟨Fortaleza⟩  jefe final nv 15 (sin salida)
```

### Zonas abiertas (overworld)
- Todas las zonas de un tier viven en **un solo mapa** (`meadow` para el Tier 1): se recorren sin cambio de mapa,
  delimitadas por cuellos de botella naturales, no por portales.
- Se dimensionan por **tiempo de caminata**: cruzar una zona toma 60–90 s (`rules.world.zoneCrossTimeSecTarget`).
  A 4 tiles/s (`rules.movement.baseSpeedTilesPerSec`) son ~100×100 tiles de área útil por zona.
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

### Tier 1 (MVP) en detalle
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

Cada clase tiene **5 hechizos** que se desbloquean en los niveles `1, 1, 3, 6, 10` (`rules.progression.spellUnlockLevels`).
El detalle de cada uno está en `content/spells.json`. Con el tope en 15 hay espacio para más hechizos después.

### Ataque básico (todas las clases)
- Toda arma tiene un ataque básico que **no consume recurso**. Su único límite es la velocidad de ataque:
  `swingMs = arma.speedMs / clase.haste` (`rules.classScaling.<clase>.haste`).
- El stat de escalado del arma (`items[].scaling`) decide la escuela: espada/hacha/maza (`str`) y daga (`agi`) hacen
  daño **físico** con `attackPower`; bastón y varita (`int`) hacen daño **mágico** con `spellPower` a 6 tiles.
- Un Sacerdote o un Mago puede farmear solo con varita sin gastar maná; también puede usar espada, con menor rendimiento.

### Equipamiento libre con afinidad
- **Cualquier clase equipa cualquier item.** No existen errores `wrong_class` ni `cannot_equip` por tipo; solo `level_too_low`.
- Cada clase tiene una afinidad **alta / media / baja** con cada tipo de arma y armadura (`rules.affinity.byClass`).
  La afinidad multiplica **toda** la contribución numérica del item: daño del arma, armadura, `spellPower` y stats
  (`rules.affinity.multipliers`: alta ×1.0, media ×0.85, baja ×0.7).
- Además, cada clase convierte los stats primarios en derivados con su propia tabla (`rules.classScaling`): un Mago
  saca `attackPower` sobre todo de `int` (×1.4) y poco de `str` (×0.6); un Pícaro al revés. Por eso un Mago con espada
  pega, pero menos que un Pícaro con la misma espada, y un Pícaro con placas aguanta, pero menos que un Guerrero.
- `classes[].recommendedWeapons/recommendedArmor` son solo informativos: el tooltip muestra "Afinidad: alta/media/baja".
- **Piso de viabilidad:** fuera de rol se rinde entre el 55 % y el 65 % del especialista en daño con básicos, y entre el
  50 % y el 60 % en aguante. Cualquier clase con cualquier equipo completa el contenido en solitario (ver `combat.md` §Afinidad).

### Triángulo de ventajas (PvP 1 vs 1, nivel y equipo equivalentes)
**Mago > Guerrero > Pícaro > Mago.** Significativa, no absoluta: la habilidad y el equipo pueden revertirla.
- *Mago > Guerrero:* Escarcha (ralentiza) y Nova (raíz) mantienen al Guerrero lejos; Carga tiene 15 s de CD frente a
  Nova 20 s, así que el Guerrero llega una vez y luego vuelve a quedarse atrás.
- *Guerrero > Pícaro:* más vida, armadura y Bloqueo; el Pícaro no puede alejarse sin dejar de pegar, y su Gubia se
  gasta rompiendo… nada, porque el Guerrero no castea.
- *Pícaro > Mago:* Gubia aturde e interrumpe el casteo; Carrera rompe raíz y ralentización y le da alcance. El Mago
  solo se salva si acierta la Nova antes de la Gubia.
- El **Sacerdote** queda fuera del triángulo: gana al Guerrero por desgaste (lo cura más de lo que él daña), pierde con
  el Pícaro (aturdimiento + burst antes de que la cura salga) y va parejo con el Mago (burst vs. curas, guerra de maná).
- Palanca fina: `rules.classAdvantage` (multiplicador de daño atacante → defensor, hoy todo en 1.0).

## Progresión
Todas las constantes en `rules.progression` y `rules.group`.
- **XP para subir** de `L` a `L+1`: `round(K · L^1.6)` con `K = 200`, `L = 1..14`.
  200, 606, 1 160, 1 838, 2 627, 3 516, 4 500, 5 572, 6 727, 7 962, 9 274, 10 659, 12 115, 13 641 (total 80 397 ≈ 1 700 kills).
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
- Al subir de nivel: stats `+statsPerLevel`, vida y recurso llenos, hechizos nuevos según `levelReq`.

## Jefes
- Objetivo de diseño de un jefe de nivel **B** (`rules.boss`): **3 jugadores de nivel B−2** con equipo de su nivel lo
  matan en **60–100 s**; **2 jugadores de nivel B** también; **1 de nivel B+1 con 1 de nivel B−1** también, más fácil.
  Nadie lo mata solo a nivel equivalente (`soloKillable: false`).
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
- Termina cuando un participante baja al 1 % de vida (`endAtHpPct`), se aleja más de 30 tiles, se desconecta o se rinde
  (`/rendirse`). Al terminar ambos recuperan vida y recurso completos. Sin pérdida de XP, oro, items ni durabilidad.
- Permitido en la aldea y en cualquier zona. Los monstruos ignoran a los duelistas y viceversa (no se puede usar un mob de escudo).
- Arquitectura: un `PvpRuleset` (`rules.pvp.rulesets`) define quién puede atacar a quién, cómo termina y qué se pierde.
  El MVP solo activa `duel`; PvP grupal y abierto son rulesets nuevos, no código nuevo.

## Social
- Chat: `say` (radio 20 tiles), `global`, `party`, susurro `/w Nombre`.
- Grupo hasta 5 (`rules.group.maxMembers`), XP y botín según las reglas de arriba.

## Configuración
Toda variable numérica está en `content/rules.json`, validada por `content/schemas/rules.schema.json` y cargada como
`RulesDb` inmutable. El admin puede recargarla en caliente con `/reload rules`. Nada de esto se hardcodea.

## Fuera del MVP (backlog "después")
Tiers 2 y 3 (mapas, monstruos, dos jefes), instancias por grupo, PvP grupal y abierto, oficios, gremios, subasta,
monturas, talentos, misiones, más hechizos por clase, sonido espacial.
