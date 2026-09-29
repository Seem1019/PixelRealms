# Diseño de combate y fórmulas

Todas las fórmulas viven en `PixelRealms.Game/Combat/CombatCalculator.cs` y tienen tests con valores exactos.
Cualquier cambio aquí ⇒ actualizar tests y esta página en el mismo commit.

## Stats primarios
`str` (fuerza), `agi` (agilidad), `int` (intelecto), `spi` (espíritu), `sta` (aguante). Fuentes: base de clase
+ `statsPerLevel · (nivel − 1)` + equipo + auras (`stat_mod`).

## Stats derivados
| Derivado | Fórmula |
|---|---|
| `maxHp` | `class.baseHp + sta · 10` |
| `maxMana` | `class.baseMana + int · 8` (solo clases con maná) |
| `attackPower` | Guerrero: `str·2` · Pícaro: `agi·2 + str` · otros: `str` |
| `spellPower` | `int · 1.5` + bonus de equipo `spellPower` |
| `critChance` | `5% + agi · 0.05%` (físico) · `5% + int · 0.04%` (mágico); tope 40 % |
| `dodgeChance` | `3% + agi · 0.03%`; tope 25 % (solo contra físico, solo si hay línea frontal: no se exige en MVP) |
| `armor` | suma de equipo + `agi · 1` |
| `manaRegen` (por 5 s) | `spi · 1 + int · 0.1`; ×0.3 si lanzó hechizo en los últimos 5 s |
| `hpRegen` fuera de combate | `spi · 0.5 + sta · 0.2` por segundo tras 6 s sin combate |

## Daño físico (auto-ataque y habilidades con `school: "physical"`)
```
raw      = weaponRoll(min..max) + attackPower / 14 · weaponSpeed   // auto-ataque
raw      = effect.base + effect.apCoef · attackPower                // habilidad
mitig    = armor / (armor + 40 · attackerLevel + 200)               // 0..0.75 (tope)
dmg      = round(raw · (1 − mitig) · critMult · variance)
critMult = 1.5 si crit, variance ∈ [0.95, 1.05] con IRng
```
## Daño mágico (`fire`, `frost`, `shadow`, `holy`, `nature`)
```
raw = effect.base + effect.spCoef · spellPower
dmg = round(raw · (1 − resist) · critMult · variance)     // resist = 0 en MVP salvo auras
```
## Curación
`heal = round((effect.base + effect.spCoef · spellPower) · critMult · variance)`; no excede `maxHp` (el exceso no genera amenaza).

## Tabla de impacto (un solo roll)
Físico: `miss 5%` (+1% por nivel del objetivo sobre el atacante) → `dodge` → `crit` → `hit`. Mágico: `miss 4%` → `crit` → `hit`.
Curas nunca fallan.

## Casteo
- `castMs = 0` ⇒ instantáneo. Durante un casteo el jugador **no puede moverse**: si envía `MoveInput` con dirección
  ≠ 0 se interrumpe (`result: "interrupted"`). Recibir daño no interrumpe en el MVP.
- GCD 1000 ms en todo hechizo con `triggersGcd: true` (por defecto). Auto-ataque no activa GCD.
- Recurso se descuenta **al terminar** el casteo; se valida al iniciar y al terminar.
- Rango y LOS se validan al iniciar y al terminar (tolerancia +1 tile al terminar).
- LOS: Bresenham sobre la grilla de colisión (tiles con `blocksSight`).

## Auras
`{ id, kind: dot|hot|stat_mod|stun|root|silence|shield|slow, durationMs, tickMs?, stacks, maxStacks, amountPerTick?, mods? }`
- Reaplicar misma aura del mismo lanzador → refresca duración y suma stack hasta `maxStacks`.
- `shield` absorbe daño hasta `amount` y se consume.
- `stun`: no mueve, no castea, no auto-ataca. `root`: no mueve. `silence`: no castea hechizos (`school != physical`). `slow`: `speed × (1 − pct)`.

## Amenaza (monstruos)
- Daño: 1 amenaza por punto. Curación: 0.5 por punto, repartida entre todos los monstruos en combate con el sanado.
- `taunt`: fija al lanzador como objetivo 3 s y le pone `amenaza = max + 10 %`.
- El monstruo cambia de objetivo si otro supera 110 % (melee) / 130 % (a distancia) de la amenaza del actual.

## Monstruos (IA)
Estados: `Idle` (patrulla aleatoria en radio 3 tiles del spawn) → `Aggro` (jugador a ≤ `aggroRange`, línea de visión)
→ `Chase` (camino A* sobre grilla, recalcula cada 500 ms) → `Attack` (en rango) → `Evade` si se aleja > `leashRange`
del spawn: vuelve corriendo, inmune, se cura al 100 % y resetea amenaza.
Respawn tras `respawnSec`. Botín asignado a quien hizo el primer daño (o su grupo).

## Muerte y recursos
- Ira: +`dmgDealt/ (level·2) ` al golpear, +`dmgTaken / (level·1.5)` al recibir, −2/s fuera de combate. Tope 100.
- Energía: +10/s siempre. Tope 100.
- "En combate": hizo o recibió daño en los últimos 6 s.
