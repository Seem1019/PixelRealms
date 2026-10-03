using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Ataque básico (HU-032, ADR-019): temporizador `weapon.speedMs / haste` (o `attackSpeedMs` del monstruo) que solo avanza con
/// `autoAttackOn`, objetivo enemigo vivo en alcance, sin castear y sin aturdimiento; durante `abilityLockMs` no sale pero el
/// temporizador sigue (sale al terminar el bloqueo). Escuela y poder según `weapon.scaling`; maná por golpe que impacta.
/// </summary>
public sealed class AutoAttackSystem(CombatServices services, DamagePipeline damage) : IMapSystem
{
    private readonly List<Actor> _attackers = new(64);

    public string Name => "auto_attack";

    /// <summary>Velocidad de swing en ms del actor (null si no puede atacar: sin arma).</summary>
    public double? SwingMs(Actor actor)
    {
        if (actor is Monster m) return m.Template.AttackSpeedMs;
        if (actor is Player p && services.WeaponOf(p) is { } w) return w.SpeedMs / services.StatsOf(p).Haste;
        return null;
    }

    public double? RangeOf(Actor actor)
    {
        if (actor is Monster m) return m.Template.AttackRange;
        if (actor is Player p && services.WeaponOf(p) is { } w) return w.RangeTiles;
        return null;
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        _attackers.Clear();
        foreach (var a in map.Actors.Values) if (a.Combat.AutoAttackOn && a.IsAlive) _attackers.Add(a);
        foreach (var attacker in _attackers)
        {
            var combat = attacker.Combat;
            var target = combat.TargetId is { } id ? map.Find(id) : null;
            if (target is null || target.IsDead || target.Combat.Evading || !services.IsEnemy(attacker, target))
            {
                if (attacker is Player) combat.AutoAttackOn = false; // el objetivo murió o dejó de ser válido
                continue;
            }
            var swingMs = SwingMs(attacker);
            var range = RangeOf(attacker);
            if (swingMs is null || range is null) continue;
            // Pausas: fuera de alcance (CA2), casteando (CA6), aturdido.
            if (combat.IsCasting || attacker.Auras.IsStunned || combat.Evading) continue;
            if (Vec2.Distance(attacker.Position, target.Position) > range.Value) continue;
            combat.SwingProgressMs = Math.Min(swingMs.Value, combat.SwingProgressMs + ctx.DeltaMs);
            if (combat.SwingProgressMs < swingMs.Value) continue;
            if (combat.IsAbilityLocked(ctx.NowMs)) continue; // CA7: sale al terminar el bloqueo
            if (!LineOfSight.Has(map.Data.Collision, attacker.Position, target.Position)) continue;
            combat.SwingProgressMs = 0;
            Swing(attacker, target, swingMs.Value, map, ctx);
        }
    }

    /// <summary>Un golpe básico: tabla de impacto → raw (combat.md §Ataque básico) → mitigación/crit/variance → DamagePipeline.</summary>
    public HitOutcome Swing(Actor attacker, Actor target, double swingMs, MapInstance map, TickContext ctx)
    {
        var rules = ctx.Rules.Combat;
        var attackerStats = services.StatsOf(attacker);
        var targetStats = services.StatsOf(target);
        School school;
        int roll;
        double affinity, power;
        if (attacker is Player p && services.WeaponOf(p) is { } w)
        {
            school = w.School;
            roll = CombatCalculator.RollWeapon(ctx.Rng, w.Template.DamageMin, w.Template.DamageMax);
            affinity = w.Affinity;
            power = school == School.Magic ? attackerStats.SpellPower : attackerStats.AttackPower;
        }
        else if (attacker is Monster m)
        {
            school = m.Template.School;
            roll = CombatCalculator.RollWeapon(ctx.Rng, m.Template.DamageMin, m.Template.DamageMax);
            affinity = 1.0;
            power = 0;
        }
        else return HitOutcome.Miss;

        var critChance = school == School.Physical ? attackerStats.CritChancePhysical : attackerStats.CritChanceMagic;
        var outcome = CombatCalculator.RollHit(ctx.Rng, school, attacker.Level, target.Level, targetStats.DodgeChance, critChance, rules);
        if (outcome == HitOutcome.Miss) { ctx.Emit(new CombatHitEvent(map.Id, attacker, target, null, HitKinds.Miss, 0, false, school)); return outcome; }
        if (outcome == HitOutcome.Dodge) { ctx.Emit(new CombatHitEvent(map.Id, attacker, target, null, HitKinds.Dodge, 0, false, school)); return outcome; }

        var crit = outcome == HitOutcome.Crit;
        var variance = CombatCalculator.RollVariance(ctx.Rng, rules);
        var raw = CombatCalculator.BasicAttackRaw(roll, affinity, power, swingMs, rules);
        var dmg = school == School.Physical
            ? CombatCalculator.PhysicalDamage(raw, CombatCalculator.Mitigation(targetStats.Armor, attacker.Level, rules), crit, variance, EffectResolver.ClassAdvantage(attacker, target, ctx), rules)
            : CombatCalculator.MagicDamage(raw, crit, variance, EffectResolver.ClassAdvantage(attacker, target, ctx), rules);
        damage.Deal(attacker, target, dmg, school, crit, null, map, ctx);

        // Maná por básico que impacta (ADR-014): cualquier arma, normalizado por swingMs.
        if (attacker is Player mp && services.ResourceOf(mp) == Resource.Mana)
            damage.AddResource(mp, CombatCalculator.ManaPerBasicHit(mp.MaxResource, swingMs, rules), ctx);
        return outcome;
    }
}
