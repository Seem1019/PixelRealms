using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Ai;

/// <summary>
/// HU-116: invocaciones de monstruos (efecto `summon`). Aparecen en casillas libres junto al invocador, con su tabla de amenaza
/// (entran persiguiendo), sin punto de spawn (no reaparecen) y dentro de los topes de `rules.limits`. Se van, sin cadáver ni
/// botín, al morir o evadir, o si su invocador muere, evade, se queda sin amenaza (se reinició: p. ej. murieron todos) o deja el
/// mapa. No dan XP ni botín (ProgressionSystem, LootSystem).
/// </summary>
public sealed class SummonSystem(Func<ContentDb> content, World world, InterestSystem interest) : IMapSystem
{
    /// <summary>Huecos alrededor del invocador, del más cercano al más lejano (casillas).</summary>
    private static readonly Vec2[] Offsets =
    [
        new(1.5f, 0), new(-1.5f, 0), new(0, 1.5f), new(0, -1.5f), new(1.1f, 1.1f), new(-1.1f, 1.1f), new(1.1f, -1.1f), new(-1.1f, -1.1f),
        new(2.5f, 0), new(-2.5f, 0), new(0, 2.5f), new(0, -2.5f), new(1.8f, 1.8f), new(-1.8f, 1.8f), new(1.8f, -1.8f), new(-1.8f, -1.8f),
    ];

    private readonly List<Monster> _gone = new(8);

    public string Name => "summons";

    /// <summary>Antes de quitar una invocación en vida: la composición olvida sus proyectiles en vuelo y le quita las auras (que
    /// vuelven a la reserva), como a quien sale del mundo.</summary>
    public Action<Monster, MapInstance, TickContext>? OnRemoving { get; set; }

    /// <summary>Lo fija la composición (<see cref="MonsterAiSystem.IsValidTarget"/>): la invocación hereda solo la amenaza de quien
    /// podría tener de objetivo (ni duelistas, ni ausentes del mapa, ni señuelos fuera de su correa; revisión de autoridad, HU-117).</summary>
    public Func<Monster, EntityId, MapInstance, bool>? IsValidTarget { get; set; }

    /// <summary>Crea las invocaciones del efecto (menos si no hay hueco o se llega a un tope). Devuelve cuántas creó.</summary>
    public int Summon(Monster invoker, EffectDef effect, MapInstance map, TickContext ctx)
    {
        if (effect.MonsterId is null || !content().TryGetMonster(effect.MonsterId, out var template) || template is null) return 0;
        var limits = ctx.Rules.Limits;
        var own = 0;
        var total = 0;
        foreach (var m in map.Monsters.Values)
        {
            if (m.SummonedBy is not { } by || m.IsDead) continue;
            total++;
            if (by == invoker.Id) own++;
        }
        var created = 0;
        var start = ctx.Rng.Next(0, 8); // que no salgan siempre por el mismo lado
        for (var i = 0; i < Offsets.Length && created < effect.Count; i++)
        {
            if (own + created >= limits.MaxSummonsPerCaster || total + created >= limits.MaxSummonsPerInstance) break;
            var pos = invoker.Position + Offsets[i < 8 ? (i + start) % 8 : 8 + (i - 8 + start) % 8];
            if (map.Collision.IsSolidAt(pos.X, pos.Y) || !LineOfSight.Has(map.Collision, invoker.Position, pos)) continue;
            var summon = new Monster(world.EntityIds.Next(), template, pos, wanderRadius: 0)
            {
                Level = template.Level, Hp = template.Hp, MaxHp = template.Hp, Position = pos, BaseSpeed = (float)template.Speed,
                SummonedBy = invoker.Id,
            };
            foreach (var raw in invoker.Threat.Ids)
            {
                var id = new EntityId(raw);
                if (IsValidTarget is null || IsValidTarget(summon, id, map)) summon.Threat.Add(id, invoker.Threat.Of(id));
            }
            map.Add(summon);
            created++;
        }
        return created;
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        _gone.Clear();
        foreach (var m in map.Monsters.Values)
        {
            if (m.SummonedBy is not { } by) continue;
            var invoker = map.Find(by);
            if (m.IsDead || m.Combat.Evading || invoker is null || invoker.IsDead || invoker.Combat.Evading || invoker is Monster { Threat.Count: 0 })
                _gone.Add(m);
        }
        foreach (var m in _gone)
        {
            if (!m.IsDead) OnRemoving?.Invoke(m, map, ctx);
            interest.ForgetEntity(map, m.Id, InterestSystem.ReasonDespawn, ctx);
            map.Remove(m.Id);
        }
    }
}
