# Diseño de combate y fórmulas

Todas las fórmulas viven en `PixelRealms.Game/Combat/CombatCalculator.cs` y `StatCalculator.cs`, y tienen tests con
valores exactos. **Toda constante viene de `content/rules.json`** (`rules.combat`, `rules.classScaling`, `rules.affinity`);
aquí se citan los valores por defecto para poder leer las fórmulas. Cambio de fórmula ⇒ tests + esta página en el mismo commit.

## Modelo de combate: híbrido (ADR-015)
- **Un objetivo ⇒ tab-target:** ataque básico y hechizos con `targeting` `self`, `enemy` o `ally` (daño, cura, control,
  `taunt`, `dash`). Requieren objetivo seleccionado; se esquivan solo con la tabla de impacto.
- **Área ⇒ se apunta libremente:** `self_aoe_enemies` / `self_aoe_allies` (círculo alrededor del lanzador) y
  `ground_aoe_enemies` / `ground_aoe_allies` / `ground_aoe_all` (círculo de `aoeRadius` en el punto `targetPos` que envía el
  cliente, a ≤ `range` + `castRangeToleranceTiles` del lanzador y con LOS al punto). `ground_aoe_all` aplica los efectos
  positivos (cura, escudo, beneficios) a aliados y los negativos (daño, controles, perjuicios) a enemigos (Pulso sagrado,
  Sendero de luz). `target_aoe_enemies` ya no existe en el contenido ni en el schema.
- Un efecto puede llevar `applyTo: "self"` para aplicarse al lanzador aunque el hechizo sea de área o de salto (velocidad
  propia de Cuchillas arrojadizas, daño extra de Paso sombrío). `heal` admite `bonusBelowHpPct` + `bonusMult` (cura más si el
  objetivo está por debajo de ese % de vida; Oración desesperada). Efecto `interrupt`: corta el casteo del objetivo y aplica
  `rules.combat.interruptLockoutMs`.
- El punto se fija en `CastStarted` (todos ven la marca en el suelo) y el área se resuelve al terminar el casteo con los
  objetivos que estén dentro en ese tick. Salir de la marca esquiva el golpe; los monstruos siguen la misma regla.
- Sin fuego amigo. `maxTargets` elige los más cercanos al centro del área.
- **Formas** (`shape`): `circle` (`aoeRadius`), `cone` (`aoeRadius` + `aoeAngleDeg`) y `line` (`aoeLength` + `aoeWidth`); el
  cono y la línea salen del lanzador en la dirección de `targetPos`. Las tres están en el schema; el círculo se implementa en
  la Fase 1 (HU-086) y el cono y la línea cuando un hechizo los use (hasta entonces, los hechizos que los usan quedan **no disponibles**: ver §Contenido no disponible).
- **Salto a un punto** (efecto `leap`: `maxRange`, `travelMs`; `travelMs = 0` = teletransporte; HU-087): el lanzador va hasta
  `targetPos`, recortado a la última casilla libre con LOS dentro de `maxRange`. Lo mueve el servidor; el cliente no lo
  predice y suaviza la posición recibida (~100 ms). `root` y `stun` impiden saltar. Los efectos del mismo hechizo que van
  después del `leap` se aplican en el punto de llegada (p. ej. un área al caer). Carga (`dash` a un objetivo) sigue igual.
- **Inmunidad tras un control fuerte (ADR-022):** cuando termina un `stun`, `root` o `silence`
  (`rules.combat.hardControlKinds`), el objetivo es inmune a los tres durante `hardControlImmunitySec` (1,5 s). Vale para
  jugadores y monstruos; los jefes ya son inmunes a `stun`, `root` y `slow`. El efecto `interrupt` no es un control y corta
  igual un casteo aunque el objetivo sea inmune.
- **Contenido no disponible (ADR-023):** el validador conoce las funciones del motor ya implementadas. Un hechizo que use
  alguna que falte (forma, targeting, efecto o campo) se carga pero queda no disponible: no se aprende ni se equipa, y el
  validador da un aviso, no un error. Al implementarse la función, el hechizo pasa a estar disponible sin tocar el contenido.

## Stats primarios
`str` (fuerza), `agi` (agilidad), `int` (intelecto), `spi` (espíritu), `sta` (aguante).
Fuentes: `baseStats` de clase + `statsPerLevel · (nivel − 1)` + equipo **× afinidad** + auras (`stat_mod`).

## Afinidad y equipo (`rules.affinity`)
- Cada item tiene un tipo (`weaponType` o `armorType`); cada clase tiene afinidad `alta | media | baja` con ese tipo.
- `afinidadMult = multipliers[afinidad]` (alta 1.0, media 0.85, baja 0.7) multiplica **todo lo numérico del item**:
  daño mín/máx del arma, `armor`, `spellPower` y cada `stats`. Un tipo no listado cuenta como baja.
- No hay restricción de equipo por clase. Solo `levelReq` (`level_too_low`) y el `slot` correcto.

## Stats derivados (`rules.classScaling.<clase>`)
| Derivado | Fórmula |
|---|---|
| `maxHp` | `class.baseHp + sta · hpPerSta` (Guerrero 12, resto 10) |
| `maxMana` | `class.baseMana + int · manaPerInt` (8 en Mago/Sacerdote, 0 en el resto) |
| `attackPower` | `str · ap.str + agi · ap.agi + int · ap.int` |
| `spellPower` | `str · sp.str + agi · sp.agi + int · sp.int + Σ item.spellPower · afinidad` |
| `critChance` físico | `critBase 5 % + agi · 0.3 %`; mágico `5 % + int · 0.3 %`; tope 40 % |
| `dodgeChance` | `3 % + agi · 0.2 %`; tope 25 % (solo contra físico) |
| `armor` | `Σ item.armor · afinidad · armorMult + agi · 1` |
| `haste` | multiplicador de velocidad de ataque de la clase (Pícaro 1.15, Guerrero 1.0, Mago/Sacerdote 0.9) |
| `manaRegen` (por 5 s) | `spi · 1 + int · 0.1`; ×0.3 durante 5 s tras gastar maná |
| `hpRegen` fuera de combate | `spi · 0.5 + sta · 0.2` por segundo tras 6 s sin combate |

Tabla de conversión por defecto:

| Clase | ap.str | ap.agi | ap.int | sp.str | sp.agi | sp.int | hpPerSta | armorMult | haste |
|---|---|---|---|---|---|---|---|---|---|
| Guerrero | 2.0 | 0.5 | 0.3 | 0.5 | 0.2 | 0.8 | 12 | 1.00 | 1.00 |
| Pícaro | 1.0 | 2.0 | 0.3 | 0.2 | 0.5 | 0.8 | 10 | 0.85 | 1.15 |
| Mago | 0.6 | 0.4 | 1.4 | 0 | 0 | 1.5 | 10 | 0.75 | 0.90 |
| Sacerdote | 0.7 | 0.4 | 1.4 | 0 | 0 | 1.3 | 10 | 0.85 | 0.90 |

`ap.int` alto en los casters es lo que hace viable su ataque básico con un arma física (regla "equipo libre").

## Escuelas
Solo dos en el MVP: `physical` (mitigado por armadura, puede fallar 5 % y esquivarse) y `magic` (sin mitigación en el
MVP, `resist = 0` salvo auras futuras, falla 4 %, no se esquiva). Bola de fuego y Escarcha son ambas `magic`; la
diferencia es el efecto secundario, no el elemento.

## Ataque básico
El básico lo da **el arma equipada** y **no ocupa ninguna de las 4 casillas de hechizo** (ADR-019). Alcance, animación y
proyectil por tipo en `rules.weapons` (daga 1,25 · espada/maza/hacha 1,5 · varita 7 · bastón 5 casillas); velocidad y daño en
cada item; las armas a distancia tienen un 20 % menos de presupuesto de daño (`rangedDpsMult`). Solo hace daño a un objetivo.
- **Convivencia con los hechizos:** el básico sigue solo mientras haya objetivo en alcance y no se castee. Un hechizo
  instantáneo **no reinicia** el temporizador, pero abre un bloqueo de `abilityLockMs` (250) en el que no sale el básico ni
  otro hechizo; si el básico tocaba dentro del bloqueo, sale al terminar. GCD de `gcdMs` (1000) en los hechizos de clase; el
  básico y los usables no lo activan. Todo hechizo instantáneo de clase tiene `cooldownMs ≥ minInstantSpellCooldownMs` (2000).
```
swingMs  = weapon.speedMs / haste
escuela  = weaponScaling[weapon.weaponType] ∈ {str, agi} → physical con attackPower ; int → magic con spellPower
alcance  = rules.weapons[weapon.weaponType].rangeTiles
raw      = weaponRoll(min..max) · afinidad + poder / basicAttackPowerDivisor(14) · (swingMs / 1000)
```
Sin coste de recurso. Reanuda al volver al rango sin reiniciar el temporizador. No activa GCD.
**El temporizador del básico no avanza mientras se castea** (se reanuda al terminar el casteo).
**Maná por golpe:** todo personaje con maná, con cualquier arma, recupera al impactar
`maná = maxMana · manaPerBasicHitPctPerSec (0.015) · (swingMs / 1000)`. Fallos y esquivas no dan maná. Normalizado por
`swingMs`: un arma rápida no da más maná por segundo que una lenta. Referencia (Mago nv 4, ~240 de maná, bastón): Bola de
fuego sin parar ⇒ sin maná en ~26 s; 2 hechizos por 1 básico ⇒ ~80 s; 1:1 ⇒ más de 6 min.
Genera ira (Guerrero): `ragePerHitDealt` (6) al impactar, `ragePerHitTaken` (4) al recibir cualquier golpe.

## Daño físico (habilidades con `school: physical`)
```
raw      = effect.base + effect.apCoef · attackPower + effect.weaponPct · weaponRollMedio · afinidad
mitig    = armor / (armor + mitigationPerLevel(20) · nivelAtacante + mitigationConstant(100))   // tope 0.75
dmg      = round(raw · (1 − mitig) · critMult · variance · classAdvantage)
critMult = 1.5 si crit; variance ∈ [0.95, 1.05] con IRng; classAdvantage solo en PvP (1.0 por defecto)
```
Los monstruos usan la misma fórmula con `weaponRoll = damageMin..damageMax` y `attackPower = 0`; pueden hacer crítico
(5 % fijo) y ser esquivados.

## Daño mágico (`school: magic`)
```
raw = effect.base + effect.spCoef · spellPower
dmg = round(raw · (1 − resist) · critMult · variance · classAdvantage)     // resist = 0 en MVP
```

## Curación
`heal = round((effect.base + effect.spCoef · spellPower) · critMult · variance)`; no excede `maxHp` (el exceso no genera amenaza). Nunca falla.

## Tabla de impacto (un solo roll por objetivo)
Físico: `miss 5 %` (+1 % por nivel del objetivo sobre el atacante) → `dodge` → `crit` → `hit`. Mágico: `miss 4 %` → `crit` → `hit`.
- Un hechizo que **solo aplica auras** (Veneno debilitante, Corte de tendón, Escarcha en su parte de aura) pasa por la misma
  tabla: si falla, no aplica nada.
- Los **ticks de DoT** no fallan ni critican; los DoT físicos sí se mitigan por armadura (calculada al aplicar, *snapshot*).

## Casteo
- `castMs = 0` ⇒ instantáneo. Durante un casteo el jugador **puede moverse** a `velocidad · castMoveSpeedMult` (0.5; ADR-019).
  Moverse no interrumpe y recibir daño tampoco. **Solo interrumpen** los controles que impiden castear (`stun`; `silence` en
  hechizos `magic`) y el efecto `interrupt` (`result: interrupted`); después, `interruptLockoutMs` (1500) sin poder castear.
  `root` y `slow` no interrumpen.
- **Fin del casteo con objetivo único:** se revalidan alcance (`castRangeToleranceTiles` = 1.5) y LOS; si fallan,
  `CastEnded{result: failed, reason: out_of_range|no_los}`, sin gastar recurso ni cooldown (el GCD ya se gastó).
- **Áreas y saltos:** punto, origen y dirección se fijan en `CastStarted` y **no** se revalida el alcance al terminar (la marca
  ya vista no se cancela porque el lanzador se movió). Solo las cortan las causas de arriba.
- **Otra habilidad o un salto durante un casteo lo cancela** (`result: cancelled`, sin coste) y se usa la nueva si el GCD lo
  permite. Los usables (pociones) no cancelan. `CancelCast` (Esc) cancela.
- **Predicción:** el cliente aplica la velocidad reducida al recibir su propio `CastStarted` y la quita con `CastEnded`;
  `Snapshot.self.speed` incluye el multiplicador y la reconciliación corrige el desfase (50–100 ms). `movement.json` incluye
  casos a velocidad reducida. La barra de casteo sigue llenándose en movimiento: "Interrumpido" (rojo), "Fuera de alcance"
  (gris), nada si se cancela.
- GCD `gcdMs` (1000) en todo hechizo con `triggersGcd: true` (por defecto). El ataque básico no activa GCD.
- Recurso se descuenta **al terminar** el casteo; se valida al iniciar y al terminar.
- Rango y LOS se validan al iniciar y, en hechizos a un objetivo, también al terminar (ver arriba).
- LOS: Bresenham sobre la grilla de colisión (tiles con `blocksSight`).
- Efecto `dash` (Carga): el lanzador se coloca adyacente al objetivo en ≤ 3 ticks; requiere LOS y `distancia ≥ minRange`.
- Hechizos de área `ground_*`: rango y LOS se validan contra `targetPos` al iniciar; el punto no se mueve durante el casteo
  y al terminar se toman los objetivos dentro de `aoeRadius`.
- Mientras se castea, el ataque básico se pausa (ver §Ataque básico).

## Rendimiento (ADR-018)
Áreas: se buscan con la rejilla AOI de la instancia y pruebas de forma sin raíces ni trigonometría; las instantáneas se
evalúan una vez al resolverse; solo interactúan con entidades. Límites en `rules.limits`; eventos de combate agrupados por
tick (`CombatEvents`). Detalle y umbrales de verificación en ADR-018.

## Auras (`content/auras.json`)
Campos: `kind: dot|hot|stat_mod|stun|root|silence|shield|slow`, `durationMs`, `tickMs`, `maxStacks`, `base`, `apCoef`,
`spCoef`, `pct`, `mods`, `removesKinds`, `immuneKinds`.
- La cantidad (`base + coef · poder`) se calcula **al aplicarse** (snapshot) y no cambia aunque el lanzador cambie de equipo.
- **Acumulación (ADR-022).** Cada aura activa se identifica por (aura, lanzador).
  - **Mismo lanzador, misma aura:** se renueva a la duración completa (no se suma tiempo). Solo suma cargas un aura con
    `maxStacks` > 1 (hoy, Veneno), hasta su máximo. **Al renovar no se reinicia el ritmo de ticks.**
  - **Lanzadores distintos:** daño en el tiempo, cura en el tiempo y escudos **conviven**, cada uno con su icono y sus cargas.
    Los escudos se gastan primero el que caduca antes.
  - **Modificadores numéricos** (velocidad, daño hecho, daño recibido, ralentización) de auras distintas o de lanzadores
    distintos **no se suman: manda el más fuerte**; los demás siguen visibles en gris y toman el relevo si el que manda
    termina. La velocidad resultante es `base · (1 + mayorBonus) · (1 − mayorRalentización)`.
  - **Ralentización máxima:** `rules.combat.maxSlowPct` (0.4); no hay inmunidad a ralentizar (la contrarresta Carrera).
  - Cada instancia cuenta 1 en su tope aunque tenga cargas o esté en gris; los controles no cuentan.
  - No hay quemaduras ni efectos elementales (ADR-010).
- Un DoT/HoT de `durationMs = 12000, tickMs = 3000` recién aplicado produce **exactamente 4 ticks** (a 3, 6, 9 y 12 s); el último tick
  ocurre en el mismo tick de servidor que la expiración y **sí cuenta**.
- `shield` absorbe daño hasta `amount` y se consume. Orden al recibir daño: `damageTakenPct/damageDonePct` → `shield` → hp.
- **Topes (ADR-021):** una entidad tiene como mucho `rules.limits.maxBuffsPerEntity` (16) auras beneficiosas y
  `maxDebuffsPerEntity` (16) perjudiciales. **Un aura nueva siempre se aplica:** si su grupo está lleno, sale la de ese mismo
  grupo con menos tiempo restante. Los **controles** (`rules.combat.controlAuraKinds`: stun, root, silence, slow) no cuentan
  para ningún tope; solo pueden no aplicarse por inmunidad de jefe o por la inmunidad de 1,5 s tras un control fuerte (ADR-022).
- **Controles del mismo tipo no se suman:** con varias ralentizaciones activas manda la más fuerte (`pct` mayor); con varios
  aturdimientos o raíces manda la que termina más tarde. Siguen visibles como iconos, pero solo una tiene efecto.
- `stun`: no mueve, no castea (interrumpe), no ataca. `root`: no mueve. `silence`: no castea `magic`. `slow`: `speed × (1 − pct)`.
- `removesKinds`: al aplicarse quita esas auras del objetivo. `immuneKinds`: mientras dura, ignora auras nuevas de esos tipos (Carrera: root, slow).
- Monstruos con `boss: true` ignoran `bossImmuneToAuraKinds` (stun, root, slow).

## Amenaza (monstruos)
- Daño: `threatPerDamage` (1) por punto. Curación: `threatPerHeal` (0.5) por punto, repartida entre todos los monstruos en combate con el sanado.
- `taunt`: fija al lanzador como objetivo `durationMs` y le pone `amenaza = max · (1 + tauntThreatBonus)`.
- Cambio de objetivo si otro supera `threatSwitchMelee` 110 % (melee) / `threatSwitchRanged` 130 % (a distancia) de la amenaza del actual.

## Monstruos (IA)
`Idle` (patrulla en `wanderRadius` del spawn) → `Aggro` (jugador a ≤ `aggroRange` con LOS) → `Chase` (A*, recalcula
cada 500 ms) → `Attack` (en rango; usa `spells[]` listos según su `cooldownMs` en `spells.json` y `hpBelowPct`) →
`Evade` si se aleja > `leashRange`: vuelve inmune, se cura al 100 %, resetea amenaza. Respawn tras `respawnSec`.
Los duelistas no generan aggro ni amenaza mientras dura el duelo.

## Recursos
- Ira: `+ragePerHitDealt` por golpe/habilidad propia que impacta, `+ragePerHitTaken` por golpe recibido,
  `−rageDecayPerSecOutOfCombat` fuera de combate. Tope `resourceCap` (100). Empieza en 0.
- Energía: `+energyPerSec` (10) siempre. Tope 100.
- Maná: regeneración por `spi`/`int` (tabla de derivados) **más** maná por cada ataque básico que impacta
  (`manaPerBasicHitPctPerSec`, ver §Ataque básico). Única otra fuente en combate: pociones (CD compartido de 60 s).
- "En combate": hizo o recibió daño en los últimos `inCombatWindowSec` (6).

## Muerte y reaparición
`hp ≤ 0` → `Dead`, se limpian auras, los monstruos lo olvidan, `Died`. Reaparece en el punto seguro más cercano con
`respawnHpPct`/`respawnResourcePct` (50 %). En duelo no se muere: al llegar a `endAtHpPct` el duelo termina y ambos se restauran.

## Referencia de balance (calculado con estas fórmulas, ver `docs/design/balance-notes.md`)
Nivel 5 con equipo verde contra Goblin arquero (nv 5), **solo ataque básico**:

| Combinación | Afinidad arma | DPS básico | Tiempo en matarlo | Vida perdida |
|---|---|---|---|---|
| Pícaro + daga | alta | 8.0 | 18 s | 21 % |
| Pícaro + espada | alta | 7.9 | 18 s | 21 % |
| Guerrero + espada + escudo + malla | alta | 6.6 | 21 s | 13 % |
| Mago + bastón | alta | 5.1 | 28 s | 46 % |
| Sacerdote + varita | alta | 5.0 | 28 s | 45 % |
| Sacerdote + espada | media | 4.7 (60 % del Pícaro) | 30 s | 45 % |
| Sacerdote + espada + placas/malla | media/baja | 4.6 | 30 s | 35 % |
| Mago + espada + placas/malla | baja | 4.4 (56 % del Pícaro) | 32 s | 40 % |

Aguante (tiempo que tarda el goblin en matarlos): Guerrero pesado 163 s · Pícaro pesado 100 s (61 %) · Sacerdote pesado
87 s (53 %) · Mago pesado 80 s (49 %). Ninguna combinación pierde más del 50 % de vida por kill ⇒ todas son viables en solitario.
