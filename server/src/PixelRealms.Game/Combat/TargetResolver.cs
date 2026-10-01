using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Devuelve los objetivos de un hechizo según su `targeting` (combat.md §Modelo de combate, HU-034 CA2, HU-086 CA4):
/// un objetivo, círculo alrededor del lanzador o círculo en `targetPos`. Áreas: solo vivos, dentro de `aoeRadius` (distancia
/// al cuadrado, sin raíces), con LOS desde el centro, más cercanos primero, hasta `maxTargets` (tope `aoeMaxTargetsCap`).
/// Sin fuego amigo: las áreas de enemigos nunca incluyen aliados ni al lanzador.
/// </summary>
public sealed class TargetResolver(CombatServices services)
{
    private readonly List<(Actor Actor, float DistSq)> _scratch = new(32);

    public List<Actor> Resolve(Actor caster, SpellDef spell, EntityId? targetId, Vec2? targetPos, Vec2 origin, MapInstance map, TickContext ctx, List<Actor>? into = null)
    {
        var result = into ?? new List<Actor>(8);
        result.Clear();
        switch (spell.Targeting)
        {
            case Targeting.Self:
                result.Add(caster);
                break;
            case Targeting.Enemy:
            {
                var t = targetId is { } id ? map.Find(id) : null;
                if (t is not null && t.IsAlive && !t.Combat.Evading && services.IsEnemy(caster, t)) result.Add(t);
                break;
            }
            case Targeting.Ally:
            {
                var t = targetId is { } id ? map.Find(id) : null;
                result.Add(t is not null && t.IsAlive && services.IsAlly(caster, t) ? t : caster);
                break;
            }
            case Targeting.SelfAoeEnemies:
                Circle(caster, origin, spell, map, ctx, enemies: true, allies: false, result);
                break;
            case Targeting.SelfAoeAllies:
                Circle(caster, origin, spell, map, ctx, enemies: false, allies: true, result);
                break;
            case Targeting.GroundAoeEnemies:
                Circle(caster, targetPos ?? origin, spell, map, ctx, enemies: true, allies: false, result);
                break;
            case Targeting.GroundAoeAllies:
                Circle(caster, targetPos ?? origin, spell, map, ctx, enemies: false, allies: true, result);
                break;
            case Targeting.GroundAoeAll:
                Circle(caster, targetPos ?? origin, spell, map, ctx, enemies: true, allies: true, result);
                break;
            default:
                break;
        }
        return result;
    }

    private void Circle(Actor caster, Vec2 center, SpellDef spell, MapInstance map, TickContext ctx, bool enemies, bool allies, List<Actor> result)
    {
        var radius = (float)spell.AoeRadius;
        var radiusSq = radius * radius;
        var max = Math.Min(spell.MaxTargets, ctx.Rules.Limits.AoeMaxTargetsCap);
        _scratch.Clear();
        foreach (var a in map.Actors.Values)
        {
            if (a.IsDead || a.Combat.Evading) continue;
            var isEnemy = services.IsEnemy(caster, a);
            var isAlly = !isEnemy && services.IsAlly(caster, a);
            if (!((enemies && isEnemy) || (allies && isAlly))) continue;
            var d = Vec2.DistanceSquared(a.Position, center);
            if (d > radiusSq) continue;
            _scratch.Add((a, d));
        }
        _scratch.Sort(static (x, y) => x.DistSq.CompareTo(y.DistSq));
        foreach (var (actor, _) in _scratch)
        {
            if (result.Count >= max) break;
            if (!LineOfSight.Has(map.Data.Collision, center, actor.Position)) continue;
            result.Add(actor);
        }
    }
}
