---
name: game-content
description: Crear, editar y balancear contenido data-driven del juego (clases, hechizos, auras, items, monstruos, tablas de botín, vendedores) en content/*.json con sus JSON Schemas y el validador. Úsala para cualquier cambio en content/ o cuando el usuario pida "agrega un hechizo/item/monstruo/clase".
---

# Contenido del juego (data-driven)

## Archivos
| Archivo | Schema | Qué define |
|---|---|---|
| `content/classes.json` | `schemas/classes.schema.json` | clases, stats base y por nivel, armas/armaduras permitidas, equipo inicial |
| `content/spells.json` | `schemas/spells.schema.json` | hechizos de clase, de items (consumibles) y de monstruos |
| `content/auras.json` | `schemas/auras.schema.json` | DoT, HoT, buffs/debuffs, stun, root, silence, shield, slow |
| `content/items.json` | `schemas/items.schema.json` | armas, armaduras, consumibles, materiales, basura |
| `content/monsters.json` | `schemas/monsters.schema.json` | stats, IA (aggro/leash), XP, botín, hechizos |
| `content/loot_tables.json` | `schemas/loot_tables.schema.json` | oro y probabilidades por item |
| `content/vendors.json` | `schemas/vendors.schema.json` | NPCs vendedores y qué venden |
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
Tipos de aura: `dot`, `hot`, `stat_mod` (`mods.stats`, `damageTakenPct`, `damageDonePct`, `speedPct`), `stun`, `root`, `silence`, `shield`, `slow`.
Targeting: `self`, `enemy`, `ally`, `self_aoe_enemies`, `self_aoe_allies`, `target_aoe_enemies` (+ `aoeRadius`, `maxTargets`).

**Si una idea no cabe en estos efectos**, no la fuerces con hacks: propone al usuario un nuevo tipo de efecto
(requiere código en `EffectResolver`, schema, tests y esta tabla) y crea una HU para ello.

## Procedimiento
1. Lee el JSON actual del tipo y 2–3 entradas parecidas como referencia de balance.
2. Edita el JSON respetando el orden (agrupado por clase / por nivel).
3. Valida: `dotnet run --project server/tools/ContentValidator -- content/`. Debe verificar:
   schema, ids únicos, referencias (`auraId`, `useSpellId`, `lootTableId`, `itemId`, `startingItems`, vendors),
   `damageMin ≤ damageMax`, `min ≤ max`, clase de `startingItems` puede equiparlos, `levelReq` de hechizos 1..10,
   hechizos de clase con `cost.resource` = recurso de la clase.
4. Si añades `icon`/`sprite` nuevos, crea un placeholder (skill `pixel-art-assets`) o lista los assets faltantes.
5. Balance: compara con la guía de abajo; si te sales, justifícalo en el commit.

## Guía de balance (MVP, niveles 1–10)
- **Tiempo para matar** un monstruo de su nivel en solitario: 8–15 s. Para el jefe con 5 jugadores nivel 10: 90–150 s.
- **DPS de hechizo** ≈ `(base + coef·poder) / max(castMs, 1000 GCD)`. Un hechizo de 2 s debe hacer ~1.8× uno instantáneo sin CD.
- Coeficientes: instantáneo sin CD `spCoef` 0.4–0.5; 2 s `0.7–0.8`; 3 s `1.0`. AoE ×0.5–0.6 del single-target.
- Curación por maná ≈ 1.0–1.3 vida por punto de maná; daño por maná ≈ 0.9–1.1.
- Stats de item por nivel y rareza (presupuesto de puntos): `nivel × {common 0.5, uncommon 1, rare 1.5, epic 2}` redondeado.
- Probabilidades: uncommon 3–5 %, rare 1–2 %, epic de jefe ~33 % cada uno (1 garantizado recomendable).
- XP de monstruo ≈ `10 + 12·nivel` (normal), ×8–10 para jefe.
Pide al subagente `content-designer` una revisión de balance cuando agregues más de 3 entradas.

## Ejemplo: nuevo hechizo con DoT
```json
// spells.json
{ "id": "priest_shadow_word_pain", "name": "Palabra de las sombras: Dolor", "source": "class", "classId": "priest",
  "levelReq": 6, "school": "shadow", "castMs": 0, "cooldownMs": 0, "cost": { "resource": "mana", "amount": 25 },
  "range": 8, "targeting": "enemy", "effects": [{ "type": "apply_aura", "auraId": "priest_swp_dot" }],
  "icon": "spells/shadow_word_pain", "description": "Daño de sombras durante 18 s." }
// auras.json
{ "id": "priest_swp_dot", "name": "Dolor", "kind": "dot", "isDebuff": true, "school": "shadow",
  "durationMs": 18000, "tickMs": 3000, "base": 6, "spCoef": 0.18, "icon": "spells/shadow_word_pain" }
```
