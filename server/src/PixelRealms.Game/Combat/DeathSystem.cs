using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Map;
using PixelRealms.Game.Progression;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Paso 8 del tick (HU-037): hp ≤ 0 → muerto (auras fuera, casteo fuera, los monstruos lo olvidan, evento `Died`); los
/// jugadores esperan `Respawn` (punto seguro más cercano, respawnHpPct / respawnResourcePct); los cadáveres de monstruo
/// duran `corpseLifetimeSec` (o hasta saquearse) y luego desaparecen; el respawn lo programa <see cref="SpawnSystem"/>.
/// </summary>
public sealed class DeathSystem(CombatServices services, AuraSystem auras, InterestSystem interest) : IMapSystem
{
    public string Name => "death";

    /// <summary>Lo rellena SpawnSystem: al morir se programa la reaparición (`respawnSec` desde la muerte, independiente del cadáver).</summary>
    public Action<Monster, MapInstance, TickContext>? OnMonsterKilled { get; set; }

    /// <summary>¿El cadáver ya fue saqueado del todo? (HU-050). Por defecto nunca: solo expira por tiempo.</summary>
    public Func<Monster, bool> IsLooted { get; set; } = static _ => false;

    /// <summary>El cadáver desapareció (LootSystem olvida su bolsa).</summary>
    public Action<Monster, MapInstance>? OnCorpseRemoved { get; set; }

    /// <summary>Alguien murió (CastSystem quita sus áreas duraderas, HU-100).</summary>
    public Action<Actor, MapInstance, TickContext>? OnActorKilled { get; set; }

    public void Kill(Actor victim, Actor? killer, MapInstance map, TickContext ctx)
    {
        if (victim.Combat.DiedAtMs != long.MinValue) return; // ya muerto
        victim.Hp = 0;
        victim.Combat.DiedAtMs = ctx.NowMs;
        victim.Combat.KilledBy = killer?.Id;
        if (victim.Combat.Cast is { } cast) ctx.Emit(new CastEndedEvent(map.Id, victim, cast.Spell, CastResults.Cancelled, null));
        victim.Combat.ResetTransient();
        if (victim is Player p) { p.MoveDx = 0; p.MoveDy = 0; p.Dirty = true; }
        auras.ClearAll(victim, map, ctx);
        foreach (var m in map.Monsters.Values) m.Threat.Remove(victim.Id);
        if (victim is Monster mon) { mon.Threat.Clear(); OnMonsterKilled?.Invoke(mon, map, ctx); }
        OnActorKilled?.Invoke(victim, map, ctx);
        ctx.Emit(new ActorDiedEvent(map.Id, victim, killer));
    }

    /// <summary>HU-037 CA2: reaparece en el cementerio más cercano con el % de vida y recurso de las reglas.</summary>
    public bool Respawn(Player p, MapInstance map, TickContext ctx)
    {
        if (!p.IsDead) return false;
        var rules = ctx.Rules.Combat;
        var gy = map.Data.NearestGraveyard(p.Position);
        p.Position = gy.Position;
        p.Combat.DiedAtMs = long.MinValue;
        p.Combat.KilledBy = null;
        var d = services.Recalculate(p);
        p.Hp = Math.Max(1, (int)Math.Round(d.MaxHp * rules.RespawnHpPct));
        p.Resource = (int)Math.Round(p.MaxResource * rules.RespawnResourcePct);
        p.LastCombatAtMs = long.MinValue;
        p.Dirty = true;
        ctx.Emit(new RespawnedEvent(map.Id, p));
        return true;
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        var corpseMs = (long)(ctx.Rules.Combat.CorpseLifetimeSec * 1000);
        List<Monster>? gone = null;
        foreach (var m in map.Monsters.Values)
        {
            if (!m.IsDead) continue;
            if (ctx.NowMs - m.Combat.DiedAtMs >= corpseMs || IsLooted(m)) (gone ??= new List<Monster>()).Add(m);
        }
        if (gone is null) return;
        foreach (var m in gone)
        {
            interest.ForgetEntity(map, m.Id, InterestSystem.ReasonDespawn, ctx);
            map.Remove(m.Id);
            OnCorpseRemoved?.Invoke(m, map);
        }
    }
}
