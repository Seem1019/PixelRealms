using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Aplica los `effects[]` de un hechizo sobre los objetivos que devuelve <see cref="TargetResolver"/> (HU-034 CA1): `leap` y
/// `dash` mueven antes al lanzador; una sola tirada de impacto por objetivo enemigo (los que solo aplican auras también
/// fallan); `applyTo: self` va al lanzador una vez; en `ground_aoe_all` los efectos positivos van a aliados y los negativos a
/// enemigos (HU-085 CA1). Los números salen de CombatCalculator y las constantes de rules.
/// </summary>
public sealed class EffectResolver(CombatServices services, DamagePipeline damage, AuraSystem auras, TargetResolver targets)
{
    private readonly List<Actor> _targets = new(16);

    /// <summary>Lo fija la composición: el efecto `interrupt` corta casteos.</summary>
    public CastSystem? Casts { get; set; }

    public void Apply(Actor caster, SpellDef spell, EntityId? targetId, Vec2? targetPos, Vec2 origin, MapInstance map, TickContext ctx)
    {
        var rules = ctx.Rules.Combat;
        var grid = map.Collision;

        // Desplazamientos primero: lo demás se resuelve en el punto de llegada (ADR-016).
        foreach (var e in spell.Effects)
        {
            if (e.Type == EffectType.Leap && targetPos is { } tp)
            {
                var dest = ForcedMovement.LeapDestination(caster.Position, tp, e.MaxRange > 0 ? e.MaxRange : spell.Range, grid);
                if (dest != caster.Position) { ctx.Emit(new ForcedMoveEvent(map.Id, caster, caster.Position, dest)); caster.Position = dest; }
                origin = dest;
                targetPos = dest; // el área y los demás efectos se resuelven en el punto de llegada (HU-087 CA3)
                if (caster is Player lp) lp.Dirty = true;
            }
            else if (e.Type == EffectType.Dash && targetId is { } tid && map.Find(tid) is { } dashTarget)
            {
                var dest = ForcedMovement.DashDestination(caster.Position, dashTarget.Position, grid);
                if (dest != caster.Position) { ctx.Emit(new ForcedMoveEvent(map.Id, caster, caster.Position, dest)); caster.Position = dest; origin = dest; }
                if (caster is Player dp) dp.Dirty = true;
            }
        }

        var list = targets.Resolve(caster, spell, targetId, targetPos, origin, map, ctx, _targets);
        var casterStats = services.StatsOf(caster);
        var hasDamage = false;
        var hasNegative = false;
        foreach (var e in spell.Effects)
        {
            if (e.ApplyTo == ApplyTo.Self) continue;
            if (e.Type == EffectType.Damage) hasDamage = true;
            if (IsNegative(e)) hasNegative = true;
        }

        // Efectos sobre uno mismo: una vez por lanzamiento, sin tirada.
        foreach (var e in spell.Effects)
            if (e.ApplyTo == ApplyTo.Self) ApplyEffect(e, caster, caster, spell, casterStats, crit: false, map, ctx);

        var hitAny = false;
        foreach (var target in list)
        {
            var isEnemy = services.IsEnemy(caster, target);
            var crit = false;
            if (isEnemy && hasNegative)
            {
                var targetStats = services.StatsOf(target);
                var critChance = spell.School == School.Physical ? casterStats.CritChancePhysical : casterStats.CritChanceMagic;
                var outcome = CombatCalculator.RollHit(ctx.Rng, spell.School, caster.Level, target.Level, targetStats.DodgeChance, critChance, rules);
                if (outcome == HitOutcome.Miss) { ctx.Emit(new CombatHitEvent(map.Id, caster, target, spell.Id, HitKinds.Miss, 0, false, spell.School)); continue; }
                if (outcome == HitOutcome.Dodge) { ctx.Emit(new CombatHitEvent(map.Id, caster, target, spell.Id, HitKinds.Dodge, 0, false, spell.School)); continue; }
                crit = outcome == HitOutcome.Crit;
                hitAny = true;
            }
            foreach (var e in spell.Effects)
            {
                if (e.ApplyTo == ApplyTo.Self) continue;
                if (e.Type is EffectType.Leap or EffectType.Dash) continue;
                // ground_aoe_all: positivos a aliados, negativos a enemigos; nadie recibe ambos.
                if (spell.Targeting == Targeting.GroundAoeAll && IsNegative(e) != isEnemy) continue;
                if (target.IsDead) break;
                // Un efecto anterior puede haber terminado un duelo: el resto de perjuicios ya no alcanza al ex-rival (un aturdimiento
                // o un DoT sin el recorte del duelo podrían matarlo de verdad).
                if (IsNegative(e) && isEnemy && !services.IsEnemy(caster, target)) break;
                ApplyEffect(e, caster, target, spell, casterStats, crit, map, ctx);
            }
        }

        // Una habilidad que impacta sin hacer daño (Provocar, el aturdimiento de Carga) también da ira (HU-039 CA2).
        if (hitAny && !hasDamage) damage.GrantRage(caster, rules.RagePerHitDealt, ctx);
    }

    /// <summary>Efecto perjudicial: daño, control/perjuicio, interrupción, provocación. Positivo: cura, recurso, beneficio.</summary>
    public bool IsNegative(EffectDef e) => e.Type switch
    {
        EffectType.Damage or EffectType.Interrupt or EffectType.Taunt => true,
        EffectType.ApplyAura => e.AuraId is not null && services.Content.Aura(e.AuraId).IsDebuff,
        _ => false,
    };

    private void ApplyEffect(EffectDef e, Actor caster, Actor target, SpellDef spell, Progression.DerivedStats casterStats, bool crit, MapInstance map, TickContext ctx)
    {
        var rules = ctx.Rules.Combat;
        // Rangos (ADR-024): +spellRankBonusPct por rango sobre el `base` de los efectos numéricos de los hechizos de clase.
        if (caster is Player rankPlayer && spell.Source == SpellSource.Class && e.Base != 0)
            e = e with { Base = e.Base * Progression.SpellRanks.BaseMultiplier(ctx.Rules.Progression, rankPlayer.Level) };
        switch (e.Type)
        {
            case EffectType.Damage:
            {
                var variance = CombatCalculator.RollVariance(ctx.Rng, rules);
                int dmg;
                if (spell.School == School.Physical)
                {
                    var weapon = caster is Player p ? services.WeaponOf(p) : null;
                    var raw = CombatCalculator.PhysicalRaw(e, casterStats.AttackPower, weapon?.AverageRoll ?? 0, weapon?.Affinity ?? 1.0);
                    var mitigation = CombatCalculator.Mitigation(services.StatsOf(target).Armor, caster.Level, rules);
                    dmg = CombatCalculator.PhysicalDamage(raw, mitigation, crit, variance, ClassAdvantage(caster, target, ctx), rules);
                }
                else
                {
                    dmg = CombatCalculator.MagicDamage(CombatCalculator.MagicRaw(e, casterStats.SpellPower), crit, variance, ClassAdvantage(caster, target, ctx), rules);
                }
                damage.Deal(caster, target, dmg, spell.School, crit, spell.Id, map, ctx);
                break;
            }
            case EffectType.Heal:
            {
                var raw = CombatCalculator.MagicRaw(e, casterStats.SpellPower);
                if (e.BonusBelowHpPct > 0 && target.MaxHp > 0 && (double)target.Hp / target.MaxHp < e.BonusBelowHpPct) raw *= e.BonusMult;
                var healCrit = ctx.Rng.NextDouble() < casterStats.CritChanceMagic;
                var variance = CombatCalculator.RollVariance(ctx.Rng, rules);
                damage.Heal(caster, target, CombatCalculator.Heal(raw, healCrit, variance, rules), healCrit, spell.Id, map, ctx);
                break;
            }
            case EffectType.RestoreResource:
                if (target is Player rp && (e.Resource is null || services.ResourceOf(rp) == e.Resource)) damage.AddResource(rp, e.Amount, ctx);
                break;
            case EffectType.ApplyAura:
                // HU-104: una mejora puede traer el aura con otra duración o potencia (mismo id).
                if (e.AuraId is not null) auras.Apply(target, e.AuraOverride ?? services.Content.Aura(e.AuraId), caster, map, ctx, spell.Id);
                break;
            case EffectType.Taunt:
                if (target is Monster m && !m.Combat.Evading) m.Threat.Taunt(caster.Id, ctx.NowMs, e.DurationMs, rules.TauntThreatBonus);
                break;
            case EffectType.Interrupt:
                Casts?.Interrupt(target, map, ctx);
                break;
            default:
                break;
        }
    }

    /// <summary>`rules.classAdvantage` solo entre jugadores (PvP); 1.0 en el resto. Lo usan también el básico y los DoT (HU-064 CA2).</summary>
    public static double ClassAdvantage(Actor caster, Actor target, TickContext ctx)
    {
        if (caster is Player a && target is Player b && !ReferenceEquals(a, b) && ctx.Rules.ClassAdvantage.TryGetValue(a.ClassId, out var row) && row.TryGetValue(b.ClassId, out var mult)) return mult;
        return 1.0;
    }
}
