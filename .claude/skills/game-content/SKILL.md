---
name: game-content
description: Crear, editar y balancear contenido data-driven del juego (clases, hechizos, auras, items, monstruos, tablas de botín, vendedores) en content/*.json con sus JSON Schemas y el validador. Úsala para cualquier cambio en content/ o cuando el usuario pida "agrega un hechizo/item/monstruo/clase".
---

# Contenido del juego (data-driven)

## Archivos
| Archivo | Schema | Qué define |
|---|---|---|
| `content/classes.json` | `schemas/classes.schema.json` | clases, stats base y por nivel, armas/armaduras recomendadas (solo informativo, ADR-009), equipo inicial |
| `content/spells.json` | `schemas/spells.schema.json` | hechizos de clase, de items (consumibles) y de monstruos |
| `content/auras.json` | `schemas/auras.schema.json` | DoT, HoT, buffs/debuffs, stun, root, silence, shield, slow |
| `content/items.json` | `schemas/items.schema.json` | armas, armaduras, consumibles, materiales, basura |
| `content/monsters.json` | `schemas/monsters.schema.json` | stats, IA (aggro/leash), XP, botín, hechizos |
| `content/loot_tables.json` | `schemas/loot_tables.schema.json` | oro y probabilidades por item |
| `content/vendors.json` | `schemas/vendors.schema.json` | NPCs vendedores y qué venden |
| `content/rules.json` | `schemas/rules.schema.json` | **todas las constantes**: XP, grupo, combate, conversión por clase, afinidad, PvP, botín, jefes (ADR-008) |
Tipos compartidos (ids, enums, stats): `schemas/common.schema.json`.

## Convenciones de ID
- `snake_case`, único **por archivo**, inmutable una vez en uso (hay personajes con items guardados por `template_id`).
  Para "renombrar", cambia `name`, nunca `id`.
- Prefijos: hechizos de clase `<clase>_<nombre>`; de item `item_<nombre>`; de monstruo `<monstruo>_<nombre>`;
  auras `<hechizo>_<efecto>`; tablas `lt_<monstruo>`.
- Íconos/sprites: ruta relativa bajo `client/assets/` sin extensión (`spells/fireball` → `assets/icons/spells/fireball.png`).

## Efectos disponibles (los ÚNICOS que entiende `EffectResolver`)
| `type` | Campos | Notas |
|---|---|---|
| `damage` | `base`, `apCoef?`, `spCoef?`, `weaponPct?` | escuela = `spell.school`; físico usa armadura |
| `heal` | `base`, `spCoef?` | no supera maxHp; 0.5 amenaza/punto |
| `restore_resource` | `resource`, `amount` | |
| `apply_aura` | `auraId` | la aura calcula su `base + coef·poder` **al aplicarse** (snapshot) |
| `taunt` | `durationMs` | |
| `interrupt` | — | corta el casteo del objetivo; aplica `rules.combat.interruptLockoutMs` (ADR-019) |
| `dash` | `minRange?` | el lanzador se coloca adyacente al objetivo (Carga); exige LOS |
| `leap` | `maxRange`, `travelMs?` | salto a `targetPos` (0 ms = teletransporte); lo mueve el servidor; los efectos siguientes se aplican al caer (HU-087) |
Campos comunes: `applyTo: "self"` aplica el efecto al lanzador aunque el hechizo sea de área o de salto; `heal` admite
`bonusBelowHpPct` + `bonusMult` (cura más si el objetivo está por debajo de ese % de vida).
Tipos de aura: `dot`, `hot`, `stat_mod` (`mods.stats`, `damageTakenPct`, `damageDonePct`, `speedPct`), `stun`, `root`, `silence`, `shield`, `slow`.
Campos extra de aura: `removesKinds` (quita esas auras al aplicarse), `immuneKinds` (bloquea esas auras mientras dura). Los `boss: true` ignoran `rules.combat.bossImmuneToAuraKinds`.
Escuelas: solo `physical` y `magic` (ADR-010).
Targeting (combate híbrido, ADR-015): un objetivo (tab-target) `self`, `enemy`, `ally`; área `self_aoe_enemies`,
`self_aoe_allies` y, desde HU-086, `ground_aoe_enemies`, `ground_aoe_allies`, `ground_aoe_all` (punto apuntado; `ground_aoe_all`
cura aliados y daña enemigos). `target_aoe_enemies` ya no existe. Campos `aoeRadius`, `maxTargets`. Las áreas
apuntadas de daño llevan `castMs > 0` para que se puedan esquivar.
Formas (`shape`, ADR-016): `circle` (`aoeRadius`), `cone` (`aoeRadius`, `aoeAngleDeg`), `line` (`aoeLength`, `aoeWidth`); cono y
línea salen del lanzador hacia `targetPos`. Solo `circle` está implementado; los hechizos que usan algo del motor aún no implementado quedan no disponibles (ADR-023).

**Si una idea no cabe en estos efectos**, no la fuerces con hacks: propone al usuario un nuevo tipo de efecto
(requiere código en `EffectResolver`, schema, tests y esta tabla) y crea una HU para ello.

## Procedimiento
1. Lee el JSON actual del tipo y 2–3 entradas parecidas como referencia de balance.
2. Edita el JSON respetando el orden (agrupado por clase / por nivel).
3. Valida: `dotnet run --project server/tools/ContentValidator -- content/`. Debe verificar:
   schema, ids únicos, referencias (`auraId`, `useSpellId`, `lootTableId`, `itemId`, `startingItems`, vendors),
   `damageMin ≤ damageMax`, `min ≤ max`, `startingItems` con afinidad alta para su clase, `levelReq` 1..maxLevel,
   hechizos de clase con `cost.resource` = recurso de la clase, `items[].scaling` = `rules.affinity.weaponScaling[weaponType]`,
   `monsters[].type == boss` ⇔ `boss: true`, `groups[].rolls ≤ entradas`, y todo lo de `rules.json` (HU-003 CA 4b).
4. Si añades `icon`/`sprite` nuevos, crea un placeholder (skill `pixel-art-assets`) o lista los assets faltantes.
5. Balance: compara con la guía de abajo; si te sales, justifícalo en el commit.

## Guía de balance (nivel máximo 15; Fase 1 = Tier 1, niveles 1–6; márgenes en `rules.balanceTargets`)
- **Equipo libre:** ningún item lleva `classes`. El rendimiento por clase sale de `rules.affinity` (×1.0/0.85/0.7) y
  `rules.classScaling`. Al diseñar un item piensa en su clase de afinidad alta; las demás lo usarán peor automáticamente.
- **Piso de viabilidad:** cualquier clase con cualquier equipo debe matar un monstruo normal de su nivel perdiendo < 50 % de vida
  solo con ataque básico (tabla en `docs/design/combat.md` §Referencia). Si un cambio lo rompe, ajusta `classScaling`, no el item.
- **XP/hora en solitario** contando descansos: diferencia entre clases ≤ 15 % (`soloXpPerHourSpreadPct`). Esa es la medida de
  "cualquier clase es igual de buena elección", no el tiempo por kill.
- **Hechizos por clase:** máximo 8 (`rules.loadout.maxSpellsPerClass`), 4 equipados libremente (sin casillas con tipo). Antes de añadir un hechizo, mejora uno por rangos (pilar 3).
- **Pentagrama (ADR-020):** los grupos de hechizos y los aportes objetivo están en `docs/design/class-kits.md`; se miden con
  `tools/balance/` (`docs/design/balance-report.md`). Un hechizo medido lleva `"provisional": false`. Regla 40/75: una
  habilidad ≤ 40 puntos; una combinación de 4 + base ≤ 75 % del presupuesto; ninguna supera el valor de la clase en ninguna punta.
- **Básico y armas (ADR-019):** el básico lo da el arma (`rules.weapons`), solo hace daño a un objetivo; mismo presupuesto de daño por
  nivel y rareza para todos los tipos, a distancia ×`rangedDpsMult` (0.8). Hechizos instantáneos de clase: `cooldownMs ≥ 2000`.
- **Duración:** 20–30 h del 1 al 15 con una clase (`hoursToMaxLevel`).
- **Tiempo para matar** un monstruo normal de su nivel en solitario con rotación completa: 8–15 s (solo básicos: 18–30 s).
- **Jefes** (`rules.boss`): un jefe de nivel B lo matan 3 jugadores de nivel B−2 en 60–100 s; 2 de nivel B; 1 de B+1 + 1 de B−1.
  Vida del jefe ≈ `DPS(3 jugadores nivel B−2, contando el maná por golpe) × 80 s`; ningún caster debe quedarse sin maná antes del final si alterna básicos (pilar 4). Daño del jefe: el tanque de nivel
  B−2 debe morir en ~30 s sin curas (obliga a llevar sanador o pociones) y un dps de nivel B en ~25 s.
- **DPS de hechizo** ≈ `(base + coef·poder) / max(castMs, 1000 GCD)`. Un hechizo de 2 s debe hacer ~1.8× uno instantáneo sin CD.
- Coeficientes: instantáneo sin CD `spCoef` 0.4–0.5; 2 s `0.7–0.8`; 3 s `1.0`. AoE ×0.5–0.6 del single-target.
- Curación por maná ≈ 1.0–1.3 vida por punto de maná; daño por maná ≈ 0.9–1.1.
- Stats de item por nivel y rareza (presupuesto de puntos): `nivel × {common 0.5, uncommon 1, rare 1.5, epic 2}` redondeado.
- Probabilidades: uncommon 3–5 %, rare 1–2 %, epic de jefe ~33 % cada uno (1 garantizado recomendable).
- **XP de monstruo no se escribe:** `round((5·nivel + 1) · tipo)` con `type` normal 1.0 / hard 1.2 (a distancia o con mecánica) / elite 3 / boss 10.
- **Monstruos por nivel:** al menos un monstruo normal por cada nivel de la zona (sin huecos), o el jugador se atasca.
- **PvP:** la clase favorecida del triángulo (Mago > Guerrero > Pícaro > Mago) gana el 60–75 % de duelos simulados con equipo igual (`duelFavoriteWinRate`); si supera el 75 % hay que bajar la palanca (hechizo o `classAdvantage`).
Pide al subagente `content-designer` una revisión de balance cuando agregues más de 3 entradas.

## Ejemplo: nuevo hechizo con DoT (solo ilustra el formato; el grupo de 8 del Sacerdote ya está completo, ADR-020)
```json
// spells.json
{ "id": "priest_shadow_word_pain", "name": "Palabra de las sombras: Dolor", "source": "class", "classId": "priest",
  "levelReq": 7, "school": "magic", "castMs": 0, "cooldownMs": 6000, "cost": { "resource": "mana", "amount": 8 },
  "range": 8, "targeting": "enemy", "effects": [{ "type": "apply_aura", "auraId": "priest_swp_dot" }],
  "icon": "spells/shadow_word_pain", "description": "Daño de sombras durante 18 s." }
// auras.json
{ "id": "priest_swp_dot", "name": "Dolor", "kind": "dot", "isDebuff": true, "school": "magic",
  "durationMs": 18000, "tickMs": 3000, "base": 6, "spCoef": 0.18, "icon": "spells/shadow_word_pain" }
```
