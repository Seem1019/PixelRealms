---
name: combat-system
description: Implementación del combate híbrido (tab-target para un objetivo, áreas apuntadas; ADR-015) en el servidor — pipeline de casteo, resolución de efectos, fórmulas de daño/curación, auras, amenaza, IA de monstruos, muerte y respawn — y su reflejo en el cliente (cast bar, textos flotantes, auras). Úsala al tocar cualquier cosa de combate.
---

# Sistema de combate

Diseño y fórmulas: **`docs/design/combat.md`** (léelo primero; es la especificación). Esta skill explica *cómo*
está construido y cómo extenderlo sin romperlo.

## Piezas (PixelRealms.Game/Combat; `StatCalculator` en `Progression/`, `PvpService` en `Social/`)
| Clase | Responsabilidad |
|---|---|
| `StatCalculator` | stats primarios (clase + nivel + equipo **× afinidad** + auras) → derivados con la matriz `rules.classScaling` (`maxHp`, `attackPower`, `spellPower`, `crit`, `armor`, `haste`…). Cachea y se invalida con `actor.MarkStatsDirty()` |
| `rules.Affinity.MultiplierFor` | `(classId, itemType) → mult` desde `rules.affinity` (`AffinityRules`, no hay clase aparte); lo usan `StatCalculator` (stats/armor/spellPower) y `CombatServices` (arma del básico de `AutoAttackSystem`) |
| `PvpService` | `CanAttack(a, b) → PvpRuleset?`; `DuelSession`s por `MapInstance`; fin de duelo al `endAtHpPct` (ADR-011) |
| `CombatCalculator` | funciones **puras**: `RollHit`, `RollVariance`, `RollWeapon`, `PhysicalDamage`, `MagicDamage`, `Heal`, `Mitigation(armor, attackerLevel, c)`. Las tiradas reciben `IRng` |
| `CastSystem` | `TryBeginCast(caster, spell, targetId, targetPos, map, ctx) → string?` (código de error o null), avance por tick, interrupciones, GCD y cooldowns |
| `EffectResolver` | aplica `EffectDef[]` sobre la lista de objetivos resuelta por `TargetResolver` |
| `AuraSystem` | aplicar/refrescar/stack, ticks, expiración, `shield` absorbe, `stat_mod` invalida stats. ADR-021/022: instancia = (aura, lanzador); topes 16 beneficiosas / 16 perjudiciales sin contar controles; renovar no reinicia el ritmo de ticks; modificadores y controles del mismo tipo no se suman (manda el más fuerte / el más largo); inmunidad `hardControlImmunitySec` tras `stun|root|silence` |
| `ThreatTable` | por monstruo: `Add(actor, amount)`, `Top()`, reglas 110 %/130 %, `taunt` |
| `AutoAttackSystem` | swing timer `weapon.speedMs / haste` (o `attackSpeedMs` del monstruo) si `autoAttackOn` y en rango; escuela y poder según `weapon.scaling` (str/agi → físico; int → mágico); alcance, animación y proyectil de `rules.weapons` (ADR-019). Un hechizo instantáneo no reinicia el swing, pero abre `abilityLockMs` (250) sin básico. Sin coste de recurso; al impactar devuelve maná a quien lo tenga (`maxMana · rules.combat.manaPerBasicHitPctPerSec · swingMs/1000`, cualquier arma). El swing se pausa mientras el actor castea |
| `DeathSystem` | hp ≤ 0 → muerto, limpia auras y casteo, evento `ActorDiedEvent` (de él leen `LootSystem`, que crea la `LootBag`, y `ProgressionSystem`, que da la XP); jugadores: esperan `Respawn`; la reaparición de monstruos la programa `SpawnSystem` |

## Pipeline de un hechizo
```
CastSpell(msg) ─► CastSystem.TryBeginCast
   ├─ validar: conoce, equipado en la barra (handler: `not_equipped`), levelReq, !dead, !stunned, !silenced y !locked_out (salvo objetos: pociones sí), CD, GCD, (si ya castea: cancelar el casteo actual, ADR-019)
   │           recurso ≥ coste, objetivo válido para targeting (jugador enemigo solo si PvpService.CanAttack), rango, LOS
   │           `ground_*`, cono, línea y `leap`: targetPos obligatorio, rango y LOS al punto; queda fijo en CastState (ADR-015/016)
   ├─ castMs == 0 ─► Resolve inmediato
   └─ castMs > 0  ─► CastState{spell, target, endsAtMs} + evento CastStarted; GCD arranca YA
tick: si now ≥ endsAtMs ─► revalidar (rango + `castRangeToleranceTiles` (1.5), LOS, objetivo vivo, recurso) ─► Resolve
Resolve: descontar recurso ─► iniciar CD ─► projectile? programar impacto a now + dist/speed ─► EffectResolver
EffectResolver: TargetResolver(targeting) ─► por objetivo: tabla de impacto ─► efectos en orden ─► eventos
            (`dash` y `leap` mueven al lanzador antes del resto de efectos, que se resuelven en el punto de llegada; auras sobre boss filtradas por bossImmuneToAuraKinds)
```
- El impacto programado (proyectil) se guarda en `PendingImpacts` (cola por `atMs`) y se resuelve aunque el lanzador
  muera; si el objetivo murió, se descarta.
- Moverse **no** interrumpe: mientras se castea, `MovementSystem` aplica `rules.combat.castMoveSpeedMult` (0.5). Solo cortan un
  casteo `stun`, `silence` (cualquier habilidad, física o mágica; no los objetos) y el efecto `interrupt` → `interruptLockoutMs`
  sin castear (ADR-019).
- Fin de casteo a un objetivo fuera de alcance (tolerancia 1.5) o sin LOS → `CastEnded{failed}` sin coste. Áreas y saltos: punto
  fijo, sin revalidar alcance. Un `CastSpell` nuevo o un salto durante un casteo lo cancelan (`cancelled`, sin coste).

## Reglas de oro
1. Todo roll usa `IRng` inyectado. Los tests usan `FixedRng(0.5, 0.01, ...)` para forzar hit/crit/miss.
2. **Tests con números exactos** para cada fórmula de `docs/design/combat.md`: si cambias una fórmula, el test
   cambia en el mismo commit y la doc también.
3. Nunca `if (spell.Id == "...")` ni `if (classId == "mage")`. Todo comportamiento especial se expresa como efecto/aura en `content/` o como número en `rules.json`.
3b. **Ningún número mágico**: GCD, crit, mitigación, ira, amenaza… se leen de `IRules` (ADR-008). Los tests de fórmulas cargan el `rules.json` real.
4. Daño se aplica en este orden: `damageTakenPct`/`damageDonePct` → `shield` absorbe → hp. Evento reporta `absorb` aparte.
5. Curar a un objetivo en combate agrega amenaza del sanador a **todos** los monstruos que tienen al objetivo en su tabla.
6. Un actor muerto no castea, no recibe curas (salvo resurrección futura), no genera amenaza.
7. En duelo, el daño que dejaría al rival por debajo de `endAtHpPct` se recorta a ese umbral y termina el duelo (`DuelUpdate{state: "ended"}`); nunca se llama a `DeathSystem`. Quien pasa más de `zoneGraceSec` fuera de la zona del duelo pierde (HU-101).

## IA de monstruos (`Ai/MonsterBrain`, `Ai/MonsterAiSystem`)
Máquina de estados (`AiState`): `Idle → Chase → Attack → Evade`; el aggro es la transición `Idle → Chase` y el leash, la
entrada en `Evade`. Detalles en `docs/design/combat.md` §Monstruos.
- Percepción cada 250 ms (no cada tick) usando el grid AOI para buscar jugadores cercanos.
- `Chase`: A* sobre `CollisionGrid` (8 direcciones, sin cortar esquinas), recalcular cada 500 ms o si el objetivo se
  mueve > 2 tiles. Límite 200 nodos expandidos; si falla → `Evade`.
- Hechizos de monstruo: en `Attack`, por cada `spells[i]` listo (CD y `hpBelowPct`) lo castea en vez del auto-ataque.
- `Evade`: inmune (`Combat.Evading = true`), velocidad × `rules.combat.evadeSpeedMult` (1.5), al llegar al spawn: vida completa, limpiar amenaza y auras.

## Cliente
- `CastStarted` → barra de casteo (propia) o mini-barra sobre la entidad (otros). `CastEnded` la oculta (rojo si interrumpido).
- `CombatEvents` (lote por tick, ADR-018) → número flotante (blanco daño, amarillo crit con "!", verde cura, gris "Falla"/"Esquiva", azul "Absorbe").
- Hotbar: al enviar `CastSpell`, muestra GCD **predicho** (1 s) de inmediato; `Cooldown` del servidor lo corrige; un
  `Error{on_cooldown|...}` lo revierte.
- Auras: íconos sobre el marco de unidad con barrido de duración.
- Saltos (`leap`) y Carga: el cliente no los predice; suaviza la posición del servidor en ~100 ms.
- Áreas apuntadas: al pulsar la tecla se muestra el círculo de `aoeRadius` bajo el cursor y el clic envía `targetPos`;
  `CastStarted{targetPos}` (y `dir` en cono/línea) dibuja la marca en el suelo para todos hasta que el casteo termina; la forma y el
  tamaño salen del contenido del cliente y `radius` solo viaja si algo lo modifica (ADR-018).

## Rendimiento (ADR-018)
- Áreas: rejilla AOI + pruebas de forma sin raíces ni trigonometría; LOS desde el centro solo para candidatos; topes en `rules.limits`.
- Reservas de capacidad fija para impactos (structs) y auras (reserva de instancias, se reutilizan desde el tick siguiente); sin
  LINQ ni closures en sistemas; recorrer `map.Actors.Values` no asigna (`EntityTable`). Los eventos del tick siguen siendo
  records (HU-088 CA1 enmendado): `TickAllocationTests` exige 0 bytes por tick sin combate y el escenario ≤ 16 KB por tick.
- Presupuesto: combate ≤ 4 ms p99 por instancia; verificación con el escenario de HU-089.

## Tests mínimos por cambio de combate
- Fórmula: valores exactos con `FixedRng`, para las 4 clases y al menos una combinación fuera de rol (Mago + espada + placas).
- Validaciones: un test por código de error (`out_of_range`, `no_los`, `on_cooldown`, `on_gcd`, `not_enough_resource`, `invalid_target`, `is_dead`, `stunned`, `rooted` (saltos y cargas), `silenced`, `in_combat` (hechizos `outOfCombatOnly`, como comer), `locked_out`, `area_limit`).
- Integración de tick: castear Bola de fuego (2 s) → tras 39 ticks no hay `CastEnded`, tras 40 sí; el daño llega `distancia / projectile.speed` después (con un hechizo sin `projectile`, en el mismo tick).
