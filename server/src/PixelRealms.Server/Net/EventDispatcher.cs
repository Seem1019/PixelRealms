using PixelRealms.Content;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Items;
using PixelRealms.Game.Map;
using PixelRealms.Game.Progression;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Players;

namespace PixelRealms.Server.Net;

/// <summary>
/// Traduce los eventos del tick a mensajes para los observadores (skill dotnet-server: la emisión S→C sale de aquí, nunca de
/// los sistemas). Los resultados de combate se agrupan en un `CombatEvents{tick, e}` por observador y tick (máx. 64
/// entradas por mensaje, ADR-018) y solo van a quien ve al atacante o al objetivo.
/// </summary>
public sealed class EventDispatcher(ConnectionManager connections, World world, InterestSystem interest, PlayerMapper mapper, WorldSession session, LootSystem loot)
{
    public const int MaxCombatEntries = 64;

    private readonly Dictionary<int, List<CombatEventDto>> _batches = new();
    private readonly HashSet<int> _recipients = new();

    public void OnPostTick(TickContext ctx)
    {
        _batches.Clear();
        var count = ctx.Events.Count;
        for (var i = 0; i < count; i++)
        {
            var e = ctx.Events[i];
            switch (e)
            {
                case EntityEnteredView v when v.Observer.ConnectionId >= 0:
                {
                    var lootable = v.Entity is Monster { IsDead: true } dead && world.GetInstance(v.MapInstanceId) is { } inst
                                   && loot.Get(inst, dead.Id) is { } bag && bag.HasLootFor(v.Observer.CharacterId, ctx.NowMs);
                    connections.Send(v.Observer.ConnectionId, SnapshotBuilder.ToSpawn(v.Entity, lootable ? SnapshotBuilder.FlagLootable : 0));
                    break;
                }
                case LootAvailableEvent la:
                    // El cadáver brilla solo para quienes ganaron algo: EntitySpawn renovado con el bit lootable.
                    if (world.GetInstance(la.MapInstanceId) is { } lootMap && lootMap.Find(la.Bag.LootId) is { } corpse)
                        foreach (var winner in la.Winners)
                            if (winner.ConnectionId >= 0) connections.Send(winner.ConnectionId, SnapshotBuilder.ToSpawn(corpse, SnapshotBuilder.FlagLootable));
                    break;
                case InventoryChangedEvent inv when inv.Player.ConnectionId >= 0:
                    connections.Send(inv.Player.ConnectionId, mapper.ToInventoryUpdate(inv.Player, inv.ReqId));
                    break;
                case EntityLeftView l when l.Observer.ConnectionId >= 0:
                    connections.Send(l.Observer.ConnectionId, new EntityDespawn(l.EntityId.Value, l.Reason));
                    break;
                case CastStartedEvent cs:
                    Broadcast(cs.MapInstanceId, cs.Caster, new CastStarted(cs.Caster.Id.Value, cs.Spell.Id, cs.TargetId?.Value, ToPx(cs.TargetPos), null, null, cs.DurationMs));
                    break;
                case CastEndedEvent ce:
                    Broadcast(ce.MapInstanceId, ce.Caster, new CastEnded(ce.Caster.Id.Value, ce.Spell.Id, ce.Result, ce.Reason));
                    break;
                case CombatHitEvent hit:
                    Batch(hit);
                    break;
                case AuraAppliedEvent aa:
                    Broadcast(aa.MapInstanceId, aa.Target, new AuraApplied(aa.Target.Id.Value, aa.Aura.AuraId, aa.Aura.CasterId?.Value, aa.Aura.Stacks, aa.Aura.RemainingMs(ctx.NowMs)));
                    break;
                case AuraRemovedEvent ar:
                    Broadcast(ar.MapInstanceId, ar.Target, new AuraRemoved(ar.Target.Id.Value, ar.AuraId, ar.CasterId?.Value));
                    break;
                case ActorDiedEvent died when died.Victim is Player { ConnectionId: >= 0 } victim:
                    connections.Send(victim.ConnectionId, new Died(died.Killer?.Id.Value, 0));
                    break;
                case CooldownEvent cd when cd.Caster is Player { ConnectionId: >= 0 } caster:
                    connections.Send(caster.ConnectionId, new Cooldown(cd.SpellId, cd.RemainingMs, cd.GcdMs));
                    break;
                case CombatErrorEvent err when err.Player.ConnectionId >= 0:
                    connections.Send(err.Player.ConnectionId, new Error(err.Code, err.Message, err.ReqId));
                    break;
                case XpGainedEvent xp when xp.Player.ConnectionId >= 0:
                    connections.Send(xp.Player.ConnectionId, new XpGain(xp.Amount, xp.SourceId?.Value));
                    break;
                case LevelUpEvent lu:
                    if (lu.Player.ConnectionId >= 0)
                        connections.Send(lu.Player.ConnectionId, new LevelUp(lu.Level, lu.NewSpells.ToList(), lu.RankUps.Count == 0 ? null : lu.RankUps.Select(r => new RankUpDto(r.SpellId, r.Rank)).ToList()));
                    // HU-041 CA3: los demás ven el nivel nuevo (EntitySpawn renovado) y HU-026 CA6: guardado al subir.
                    Broadcast(lu.MapInstanceId, lu.Player, SnapshotBuilder.ToSpawn(lu.Player));
                    session.Save(lu.Player, ctx.NowMs, "level_up");
                    break;
                case StatsChangedEvent sc when sc.Player.ConnectionId >= 0:
                    connections.Send(sc.Player.ConnectionId, mapper.ToStatsUpdate(sc.Player));
                    break;
                default:
                    break;
            }
        }
        FlushBatches(ctx.Tick);
    }

    private static Vec2Dto? ToPx(Vec2? p) => p is { } v ? new Vec2Dto(SnapshotBuilder.Px(v.X), SnapshotBuilder.Px(v.Y)) : null;

    /// <summary>Envía a todos los jugadores que ven a la entidad (y a ella misma si es un jugador).</summary>
    private void Broadcast(int mapInstanceId, Actor subject, IServerMessage msg)
    {
        var map = world.GetInstance(mapInstanceId);
        if (map is null) return;
        foreach (var p in interest.ObserversOf(map, subject.Id))
            if (p.ConnectionId >= 0) connections.Send(p.ConnectionId, msg);
    }

    private void Batch(CombatHitEvent hit)
    {
        var map = world.GetInstance(hit.MapInstanceId);
        if (map is null) return;
        var dto = new CombatEventDto(hit.Source.Id.Value, hit.Target.Id.Value, hit.SpellId, hit.Kind, hit.Amount, hit.Crit, ContentJson.EnumName(hit.School));
        _recipients.Clear();
        foreach (var p in interest.ObserversOf(map, hit.Source.Id)) if (p.ConnectionId >= 0) _recipients.Add(p.ConnectionId);
        foreach (var p in interest.ObserversOf(map, hit.Target.Id)) if (p.ConnectionId >= 0) _recipients.Add(p.ConnectionId);
        foreach (var conn in _recipients)
        {
            if (!_batches.TryGetValue(conn, out var list)) _batches[conn] = list = new List<CombatEventDto>(16);
            list.Add(dto);
        }
    }

    private void FlushBatches(long tick)
    {
        foreach (var (conn, list) in _batches)
        {
            for (var i = 0; i < list.Count; i += MaxCombatEntries)
                connections.Send(conn, new CombatEvents(tick, list.GetRange(i, Math.Min(MaxCombatEntries, list.Count - i))));
        }
    }
}
