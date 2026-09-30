---
name: combat-system
description: Implementación del combate híbrido (tab-target para un objetivo, áreas apuntadas; ADR-015) en el servidor — pipeline de casteo, resolución de efectos, fórmulas de daño/curación, auras, amenaza, IA de monstruos, muerte y respawn — y su reflejo en el cliente (cast bar, textos flotantes, auras). Úsala al tocar cualquier cosa de combate.
---

# Sistema de combate

Diseño y fórmulas: **`docs/design/combat.md`** (léelo primero; es la especificación). Esta skill explica *cómo*
está construido y cómo extenderlo sin romperlo.

## Piezas (PixelRealms.Game/Combat)
| Clase | Responsabilidad |
|---|---|
| `StatCalculator` | stats primarios (clase + nivel + equipo **× afinidad** + auras) → derivados con la matriz `rules.classScaling` (`maxHp`, `attackPower`, `spellPower`, `crit`, `armor`, `haste`…). Cachea y se invalida con `actor.MarkStatsDirty()` |
| `AffinityResolver` | `(classId, item) → mult` desde `rules.affinity`; lo usan `StatCalculator` (stats/armor/spellPower) y `AutoAttackSystem` (roll del arma) |
| `PvpService` | `CanAttack(a, b) → PvpRuleset?`; `DuelSession`s por `MapInstance`; fin de duelo al `endAtHpPct` (ADR-011) |
| `CombatCalculator` | funciones **puras**: `RollPhysical`, `RollSpell`, `RollHeal`, `Mitigation(armor, level)`. Reciben `IRng` |
| `CastSystem` | `TryBeginCast(caster, spell, target) → CastResult`, avance por tick, interrupciones, GCD y cooldowns |
| `EffectResolver` | aplica `EffectDef[]` sobre la lista de objetivos resuelta por `TargetResolver` |
| `AuraSystem` | aplicar/refrescar/stack, ticks, expiración, `shield` absorbe, `stat_mod` invalida stats |
| `ThreatTable` | por monstruo: `Add(actor, amount)`, `Top()`, reglas 110 %/130 %, `taunt` |
| `AutoAttackSystem` | swing timer `weapon.speedMs / haste` (o `attackSpeedMs` del monstruo) si `autoAttackOn` y en rango; escuela y poder según `weapon.scaling` (str/agi → físico melee 1.5; int → mágico 6 tiles con proyectil). Sin coste de recurso; al impactar devuelve maná a quien lo tenga (`maxMana · rules.combat.manaPerBasicHitPctPerSec · swingMs/1000`, cualquier arma). El swing se pausa mientras el actor castea |
| `DeathSystem` | hp ≤ 0 → `Dead`, limpia auras, crea `LootBag`, XP, evento `Died`; jugadores: espera `Respawn` |

## Pipeline de un hechizo
```
CastSpell(msg) ─► CastSystem.TryBeginCast
   ├─ validar: conoce, levelReq, !dead, !stunned, !silenced(si no físico), !casting, CD, GCD,
   │           recurso ≥ coste, objetivo válido para targeting (jugador enemigo solo si PvpService.CanAttack), rango, LOS
   │           `ground_*`: targetPos obligatorio, rango y LOS al punto; el punto queda fijo en CastState (ADR-015)
   ├─ castMs == 0 ─► Resolve inmediato
   └─ castMs > 0  ─► CastState{spell, target, endsAtMs} + evento CastStarted; GCD arranca YA
tick: si now ≥ endsAtMs ─► revalidar (rango +1 tile, LOS, objetivo vivo, recurso) ─► Resolve
Resolve: descontar recurso ─► iniciar CD ─► projectile? programar impacto a now + dist/speed ─► EffectResolver
EffectResolver: TargetResolver(targeting) ─► por objetivo: tabla de impacto ─► efectos en orden ─► eventos
            (`dash` mueve al lanzador antes del resto de efectos; auras sobre boss filtradas por bossImmuneToAuraKinds)
```
- El impacto programado (proyectil) se guarda en `PendingImpacts` (cola por `atMs`) y se resuelve aunque el lanzador
  muera; si el objetivo murió, se descarta.
- Interrupción por movimiento: `MovementSystem` notifica `CastSystem.InterruptIfCasting(actor, "interrupted")` cuando llega input ≠ 0.

## Reglas de oro
1. Todo roll usa `IRng` inyectado. Los tests usan `FixedRng(0.5, 0.01, ...)` para forzar hit/crit/miss.
2. **Tests con números exactos** para cada fórmula de `docs/design/combat.md`: si cambias una fórmula, el test
   cambia en el mismo commit y la doc también.
3. Nunca `if (spell.Id == "...")` ni `if (classId == "mage")`. Todo comportamiento especial se expresa como efecto/aura en `content/` o como número en `rules.json`.
3b. **Ningún número mágico**: GCD, crit, mitigación, ira, amenaza… se leen de `IRules` (ADR-008). Los tests de fórmulas cargan el `rules.json` real.
4. Daño se aplica en este orden: `damageTakenPct`/`damageDonePct` → `shield` absorbe → hp. Evento reporta `absorb` aparte.
5. Curar a un objetivo en combate agrega amenaza del sanador a **todos** los monstruos que tienen al objetivo en su tabla.
6. Un actor muerto no castea, no recibe curas (salvo resurrección futura), no genera amenaza.
7. En duelo, el daño que dejaría al rival por debajo de `endAtHpPct` se recorta a ese umbral y dispara `DuelEnded`; nunca se llama a `DeathSystem`.

## IA de monstruos (`Ai/MonsterBrain`)
Máquina de estados: `Idle → Aggro → Chase → Attack → Evade`. Detalles en `docs/design/combat.md` §Monstruos.
- Percepción cada 250 ms (no cada tick) usando el grid AOI para buscar jugadores cercanos.
- `Chase`: A* sobre `CollisionGrid` (8 direcciones, sin cortar esquinas), recalcular cada 500 ms o si el objetivo se
  mueve > 2 tiles. Límite 200 nodos expandidos; si falla → `Evade`.
- Hechizos de monstruo: en `Attack`, por cada `spells[i]` listo (CD y `hpBelowPct`) lo castea en vez del auto-ataque.
- `Evade`: inmune (`flags |= Evading`), velocidad ×1.5, al llegar al spawn: vida completa, limpiar amenaza y auras.

## Cliente
- `CastStarted` → barra de casteo (propia) o mini-barra sobre la entidad (otros). `CastEnded` la oculta (rojo si interrumpido).
- `CombatEvent` → número flotante (blanco daño, amarillo crit con "!", verde cura, gris "Falla"/"Esquiva", azul "Absorbe").
- Hotbar: al enviar `CastSpell`, muestra GCD **predicho** (1 s) de inmediato; `Cooldown` del servidor lo corrige; un
  `Error{on_cooldown|...}` lo revierte.
- Auras: íconos sobre el marco de unidad con barrido de duración.
- Áreas apuntadas: al pulsar la tecla se muestra el círculo de `aoeRadius` bajo el cursor y el clic envía `targetPos`;
  `CastStarted{targetPos, radius}` dibuja la marca en el suelo para todos hasta que el casteo termina.

## Tests mínimos por cambio de combate
- Fórmula: valores exactos con `FixedRng`, para las 4 clases y al menos una combinación fuera de rol (Mago + espada + placas).
- Validaciones: un test por código de error (`out_of_range`, `no_los`, `on_cooldown`, `on_gcd`, `not_enough_resource`, `invalid_target`, `stunned`, `silenced`).
- Integración de tick: castear bola de fuego (2 s) → tras 39 ticks no hay daño, tras 40 sí.
