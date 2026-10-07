using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Devuelve los objetivos de un hechizo según su `targeting` (combat.md §Modelo de combate, HU-034 CA2, HU-086 CA4):
/// un objetivo, círculo alrededor del lanzador o círculo en `targetPos`. Áreas: solo vivos cuyo cuadro del cuerpo toca el
/// área (el mismo cuadro que dibuja el cliente; sin raíces por candidato), con LOS desde el centro, el vértice o el origen,
/// más cercanos primero, hasta `maxTargets` (tope `aoeMaxTargetsCap`). HU-102: el cono y la línea salen del origen del lanzador
/// hacia `targetPos` (o hacia donde mira) y la línea se corta en la primera pared (`AreaShape`).
/// Sin fuego amigo: las áreas de enemigos nunca incluyen aliados ni al lanzador.
/// </summary>
public sealed class TargetResolver(CombatServices services)
{
    private readonly List<(Actor Actor, float DistSq, float FeetDistSq)> _scratch = new(32);

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
                Area(caster, ShapeOf(caster, spell, origin, origin, targetPos, map), spell, map, ctx, enemies: true, allies: false, result);
                break;
            case Targeting.SelfAoeAllies:
                Area(caster, ShapeOf(caster, spell, origin, origin, targetPos, map), spell, map, ctx, enemies: false, allies: true, result);
                break;
            case Targeting.GroundAoeEnemies:
                Area(caster, ShapeOf(caster, spell, targetPos ?? origin, origin, targetPos, map), spell, map, ctx, enemies: true, allies: false, result);
                break;
            case Targeting.GroundAoeAllies:
                Area(caster, ShapeOf(caster, spell, targetPos ?? origin, origin, targetPos, map), spell, map, ctx, enemies: false, allies: true, result);
                break;
            case Targeting.GroundAoeAll:
                Area(caster, ShapeOf(caster, spell, targetPos ?? origin, origin, targetPos, map), spell, map, ctx, enemies: true, allies: true, result);
                break;
            default:
                break;
        }
        return result;
    }

    /// <summary>
    /// Forma del área: el círculo va en `center`; el cono y la línea salen de `origin` (el lanzador al empezar el casteo) hacia
    /// `targetPos`, o hacia donde mira si no hay punto o coincide con sus pies.
    /// </summary>
    public static AreaShape ShapeOf(Actor caster, SpellDef spell, Vec2 center, Vec2 origin, Vec2? targetPos, MapInstance map)
    {
        if (spell.Shape == Shape.Circle) return AreaShape.Circle(center, (float)spell.AoeRadius);
        // CastSystem ya fija el punto a origen + dirección · alcance; lo demás (efectos llamados a mano) cae en el facing.
        var aim = targetPos is { } t ? t - origin : Vec2.Zero;
        var dir = aim.LengthSquared > 1e-6f && float.IsFinite(aim.LengthSquared) ? aim.Normalized() : AreaShape.FacingVector(caster.Facing);
        if (spell.Shape == Shape.Cone) return AreaShape.Cone(origin, dir, (float)spell.AoeRadius, (float)spell.AoeAngleDeg);
        var length = LineOfSight.ClearDistance(map.Collision, origin, dir, (float)spell.AoeLength);
        return AreaShape.Line(origin, dir, length, (float)spell.AoeWidth);
    }

    private void Area(Actor caster, AreaShape shape, SpellDef spell, MapInstance map, TickContext ctx, bool enemies, bool allies, List<Actor> result)
    {
        var center = shape.Origin;
        var max = Math.Min(spell.MaxTargets, ctx.Rules.Limits.AoeMaxTargetsCap);
        var body = BodyBox.From(ctx.Rules.Combat); // una vez por área, no por candidato
        _scratch.Clear();
        foreach (var a in map.Actors.Values)
        {
            if (a.IsDead || a.Combat.Evading) continue;
            var isEnemy = services.IsEnemy(caster, a);
            var isAlly = !isEnemy && services.IsAlly(caster, a);
            if (!((enemies && isEnemy) || (allies && isAlly))) continue;
            if (!shape.Touches(a.Position, body, out var d)) continue;
            _scratch.Add((a, d, Vec2.DistanceSquared(a.Position, center)));
        }
        // Varios cuadros pueden contener el centro (distancia 0): desempate por los pies y luego por id, para que `maxTargets` no
        // dependa del orden del diccionario.
        _scratch.Sort(static (x, y) => x.DistSq != y.DistSq ? x.DistSq.CompareTo(y.DistSq)
            : x.FeetDistSq != y.FeetDistSq ? x.FeetDistSq.CompareTo(y.FeetDistSq) : x.Actor.Id.Value.CompareTo(y.Actor.Id.Value));
        foreach (var (actor, _, _) in _scratch)
        {
            if (result.Count >= max) break;
            // LOS del centro (o del vértice u origen) a los pies, no al punto del cuadro más cercano: la cabeza de quien está pegado
            // a un muro entra en la casilla del muro, y LineOfSight no mira la casilla de destino.
            if (!LineOfSight.Has(map.Collision, center, actor.Position)) continue;
            result.Add(actor);
        }
    }

    /// <summary>Distancia al cuadrado de `center` al punto más cercano del cuadro del cuerpo de quien tiene los pies en `feet`
    /// (rules.combat.body*). 0 si el centro cae dentro del cuadro.</summary>
    public static float DistanceSquaredToBody(Vec2 center, Vec2 feet, CombatRules rules) => DistanceSquaredToBody(center, feet, BodyBox.From(rules));

    /// <summary>Lo mismo con las medidas del cuerpo ya leídas: es la prueba de forma de cada candidato (ADR-018: sin raíces).</summary>
    public static float DistanceSquaredToBody(Vec2 center, Vec2 feet, BodyBox body)
    {
        // Sin ramas (max): con candidatos repartidos al azar, los if fallan la predicción y la prueba sale varias veces más lenta.
        var dx = MathF.Max(MathF.Max(feet.X - body.HalfWidth - center.X, center.X - feet.X - body.HalfWidth), 0f);
        var dy = MathF.Max(MathF.Max(feet.Y - body.Above - center.Y, center.Y - feet.Y - body.Below), 0f);
        return dx * dx + dy * dy;
    }

    /// <summary>Cuadro del cuerpo (rules.combat.body*) en casillas, relativo a los pies.</summary>
    public readonly record struct BodyBox(float HalfWidth, float Above, float Below)
    {
        public static BodyBox From(CombatRules rules) => new((float)rules.BodyHalfWidthTiles, (float)rules.BodyHeightAboveFeetTiles, (float)rules.BodyDepthBelowFeetTiles);
    }
}
