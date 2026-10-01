using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Ai;

/// <summary>
/// HU-031 CA1/CA3: crea los monstruos de cada `SpawnDef` al poblar la instancia (`count` por spawn, con los datos de
/// monsters.json) y los hace reaparecer en su spawn `respawnSec` después de morir (contado desde la muerte).
/// </summary>
public sealed class SpawnSystem(Func<ContentDb> content, World world) : IMapSystem
{
    private sealed record PendingRespawn(SpawnDef Spawn, string MonsterId, Vec2 Position, long AtMs);

    private readonly Dictionary<int, List<PendingRespawn>> _pending = new();

    public string Name => "spawns";

    /// <summary>Puebla una instancia recién creada. Devuelve cuántos monstruos creó.</summary>
    public int Populate(MapInstance map, IRng rng)
    {
        var db = content();
        var created = 0;
        foreach (var spawn in map.Data.Spawns)
        {
            if (!db.TryGetMonster(spawn.MonsterId, out var template) || template is null) continue;
            for (var i = 0; i < spawn.Count; i++)
            {
                var pos = PickPosition(spawn, map.Data.Collision, rng);
                var monster = Create(template, spawn, pos);
                map.Add(monster);
                created++;
            }
        }
        return created;
    }

    private Monster Create(Content.Defs.MonsterTemplate template, SpawnDef spawn, Vec2 pos)
    {
        var m = new Monster(world.EntityIds.Next(), template, pos, spawn.WanderRadius)
        {
            Level = template.Level, Hp = template.Hp, MaxHp = template.Hp, Position = pos, BaseSpeed = (float)template.Speed,
        };
        m.Brain.Spawn = spawn;
        return m;
    }

    /// <summary>HU-070 `/spawn`: crea `count` monstruos alrededor de `pos` sin punto de spawn (no reaparecen). Devuelve cuántos creó (0 si el id no existe).</summary>
    public int SpawnAt(string monsterId, Vec2 pos, int count, MapInstance map, IRng rng)
    {
        if (!content().TryGetMonster(monsterId, out var template) || template is null) return 0;
        var adHoc = new SpawnDef("admin", monsterId, count, 2, pos, new Vec2(0, 0));
        var created = 0;
        for (var i = 0; i < count; i++)
        {
            var monster = Create(template, adHoc, PickPosition(adHoc, map.Data.Collision, rng));
            monster.Brain.Spawn = null; // sin reaparición
            map.Add(monster);
            created++;
        }
        return created;
    }

    /// <summary>Casilla libre dentro del rectángulo del spawn (o alrededor del punto, en `wanderRadius`); centro de la casilla.</summary>
    public static Vec2 PickPosition(SpawnDef spawn, CollisionGrid grid, IRng rng)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            float x, y;
            if (spawn.Size.X > 0 && spawn.Size.Y > 0)
            {
                x = spawn.Position.X + (float)(rng.NextDouble() * spawn.Size.X);
                y = spawn.Position.Y + (float)(rng.NextDouble() * spawn.Size.Y);
            }
            else
            {
                x = spawn.Position.X + (float)((rng.NextDouble() * 2 - 1) * spawn.WanderRadius);
                y = spawn.Position.Y + (float)((rng.NextDouble() * 2 - 1) * spawn.WanderRadius);
            }
            var tx = (int)MathF.Floor(x); var ty = (int)MathF.Floor(y);
            if (!grid.IsSolid(tx, ty)) return new Vec2(tx + 0.5f, ty + 0.5f);
        }
        var fx = (int)MathF.Floor(spawn.Position.X); var fy = (int)MathF.Floor(spawn.Position.Y);
        return new Vec2(fx + 0.5f, fy + 0.5f);
    }

    /// <summary>Lo llama DeathSystem al morir el monstruo: programa la reaparición `respawnSec` después (el cadáver sigue su propio plazo).</summary>
    public void ScheduleRespawn(Monster m, MapInstance map, TickContext ctx)
    {
        if (m.Brain.Spawn is not { } spawn) return;
        var at = m.Combat.DiedAtMs + (long)m.Template.RespawnSec * 1000;
        if (!_pending.TryGetValue(map.Id, out var list)) _pending[map.Id] = list = new List<PendingRespawn>();
        list.Add(new PendingRespawn(spawn, m.TemplateId, m.SpawnPosition, Math.Max(at, ctx.NowMs)));
    }

    public int PendingCount(MapInstance map) => _pending.TryGetValue(map.Id, out var l) ? l.Count : 0;

    public void Tick(MapInstance map, TickContext ctx)
    {
        if (!_pending.TryGetValue(map.Id, out var list) || list.Count == 0) return;
        var db = content();
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var p = list[i];
            if (p.AtMs > ctx.NowMs) continue;
            list.RemoveAt(i);
            if (!db.TryGetMonster(p.MonsterId, out var template) || template is null) continue;
            var pos = map.Data.Collision.IsSolidAt(p.Position.X, p.Position.Y) ? PickPosition(p.Spawn, map.Data.Collision, ctx.Rng) : p.Position;
            map.Add(Create(template, p.Spawn, pos));
        }
    }
}
