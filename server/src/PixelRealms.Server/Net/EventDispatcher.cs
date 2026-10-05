using PixelRealms.Content;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Items;
using PixelRealms.Game.Map;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Social;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Players;

namespace PixelRealms.Server.Net;

/// <summary>
/// Traduce los eventos del tick a mensajes para los observadores (skill dotnet-server: la emisión S→C sale de aquí, nunca de
/// los sistemas). Los resultados de combate se agrupan en un `CombatEvents{tick, e}` por observador y tick (máx. 64
/// entradas por mensaje, ADR-018) y solo van a quien ve al atacante o al objetivo.
/// </summary>
public sealed class EventDispatcher(ConnectionManager connections, World world, InterestSystem interest, PlayerMapper mapper, WorldSession session, LootSystem loot, PlayerRegistry players, PartyService parties)
{
    public const int MaxCombatEntries = 64;

    /// <summary>HU-062 CA1: marcos de grupo ≥ 2 veces/s aunque el compañero esté fuera de la AOI.</summary>
    public const int PartyFrameEveryTicks = 10;

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
                    // HU-035 CA6 / HU-098 CA2: quien entra en la AOI con auras ya puestas las trae consigo (si no, no se verían hasta
                    // el siguiente AuraApplied).
                    foreach (var aura in v.Entity.Auras.All)
                        connections.Send(v.Observer.ConnectionId, ToAuraApplied(v.Entity, aura, ctx.NowMs));
                    break;
                }
                case LootAvailableEvent la:
                    // El cadáver brilla solo para quienes ganaron algo: EntitySpawn renovado con el bit lootable.
                    if (world.GetInstance(la.MapInstanceId) is { } lootMap && lootMap.Find(la.Bag.LootId) is { } corpse)
                        foreach (var winner in la.Winners)
                            if (winner.ConnectionId >= 0) connections.Send(winner.ConnectionId, SnapshotBuilder.ToSpawn(corpse, SnapshotBuilder.FlagLootable));
                    break;
                case LootAnnouncedEvent ann:
                {
                    // HU-062 CA4: uncommon+ al chat de grupo del ganador; HU-083 CA4: lo del jefe, al global.
                    var itemName = mapper.ItemName(ann.TemplateId);
                    var text = $"{ann.Winner.Name} ha conseguido [{itemName}] ({RarityName(ann.Rarity)})";
                    if (ann.Global)
                    {
                        var msg = new ChatMessage("global", "", text, ctx.NowMs);
                        foreach (var p in players.All) if (p.ConnectionId >= 0) connections.Send(p.ConnectionId, msg);
                    }
                    else if (parties.PartyOf(ann.Winner.CharacterId) is { } party)
                    {
                        var msg = new ChatMessage("party", "", text, ctx.NowMs);
                        foreach (var m in party.Members)
                            if (players.ByCharacter(m.CharacterId) is { ConnectionId: >= 0 } member) connections.Send(member.ConnectionId, msg);
                    }
                    break;
                }
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
                    Broadcast(aa.MapInstanceId, aa.Target, ToAuraApplied(aa.Target, aa.Aura, ctx.NowMs));
                    break;
                case Game.Map.MapObjectChangedEvent oc when world.GetInstance(oc.MapInstanceId) is { } objMap:
                {
                    // Una palanca o una puerta se ven desde todo el mapa (las salas son pequeñas): a todos los de la instancia.
                    var msg = new MapObjects([new MapObjectDto(oc.ObjectId, oc.State)]);
                    foreach (var p in objMap.Players.Values) if (p.ConnectionId >= 0) connections.Send(p.ConnectionId, msg);
                    break;
                }
                case AuraRemovedEvent ar:
                    Broadcast(ar.MapInstanceId, ar.Target, new AuraRemoved(ar.Target.Id.Value, ar.AuraId, ar.CasterId?.Value));
                    break;
                case ActorDiedEvent died when died.Victim is Player victim:
                    if (victim.ConnectionId >= 0) connections.Send(victim.ConnectionId, new Died(died.Killer?.Id.Value, 0));
                    session.Save(victim, ctx.NowMs, "death"); // HU-026 CA6 / ADR-018
                    break;
                case CooldownEvent cd when cd.Caster is Player { ConnectionId: >= 0 } caster:
                    connections.Send(caster.ConnectionId, new Cooldown(cd.SpellId, cd.RemainingMs, cd.GcdMs, cd.TemplateId));
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
                case ChatDeliveredEvent chat:
                {
                    var msg = new ChatMessage(chat.Channel, chat.From, chat.Text, ctx.NowMs);
                    foreach (var r in chat.Recipients) if (r.ConnectionId >= 0) connections.Send(r.ConnectionId, msg);
                    break;
                }
                case PartyInvitedEvent inv when inv.Target.ConnectionId >= 0:
                    // Sin mensaje propio en el protocolo: la invitación viaja como PartyUpdate{leader: quien invita, members: []} (el cliente muestra Aceptar/Rechazar).
                    connections.Send(inv.Target.ConnectionId, new PartyUpdate(inv.Invite.FromName, []));
                    break;
                case PartyChangedEvent pc:
                    SendPartyUpdate(pc.Party);
                    break;
                case PartyDisbandedEvent pd when players.ByCharacter(pd.LastMember) is { ConnectionId: >= 0 } lastP:
                    connections.Send(lastP.ConnectionId, new PartyUpdate("", []));
                    break;
                case DuelChangedEvent duel:
                {
                    foreach (var p in new[] { duel.Duel.A, duel.Duel.B })
                    {
                        if (p.ConnectionId < 0) continue;
                        var startsIn = duel.State == "countdown" ? (int?)Math.Max(0, duel.Duel.StartsAtMs - ctx.NowMs) : null;
                        var zone = duel.State is "countdown" or "active" ? ToDuelZone(duel.Duel) : null;
                        connections.Send(p.ConnectionId, new DuelUpdate(duel.State, duel.Duel.Opponent(p).Id.Value, duel.Duel.Winner?.Id.Value, startsIn, zone,
                            Reason: duel.Reason));
                    }
                    if (duel.State == "ended" && duel.Duel.Winner is { } winner && world.GetInstance(duel.MapInstanceId) is { } dmap)
                    {
                        // HU-064 CA3: se anuncia en `say` (alcance sayRangeTiles alrededor del ganador).
                        var text = $"{winner.Name} ha ganado el duelo contra {duel.Duel.Opponent(winner).Name}";
                        var range = ctx.Rules.Movement.SayRangeTiles;
                        foreach (var p in dmap.Players.Values)
                            if (p.ConnectionId >= 0 && Vec2.Distance(p.Position, winner.Position) <= range) connections.Send(p.ConnectionId, new ChatMessage("system", "", text, ctx.NowMs));
                    }
                    break;
                }
                case TradeChangedEvent tr:
                {
                    foreach (var p in new[] { tr.Trade.A, tr.Trade.B })
                    {
                        if (p.ConnectionId < 0) continue;
                        var mine = tr.Trade.OfferOf(p); var theirs = tr.Trade.OfferOf(tr.Trade.Partner(p));
                        connections.Send(p.ConnectionId, new TradeUpdate(tr.State, tr.Trade.Partner(p).Id.Value, tr.Trade.Version, ToOffer(mine, p), ToOffer(theirs, tr.Trade.Partner(p)),
                            tr.Trade.ConfirmedBy(p), tr.Trade.ConfirmedBy(tr.Trade.Partner(p)), tr.Reason));
                    }
                    // HU-026 CA6 / ADR-018: los dos en el mismo tick y en una sola transacción (antes esperaban al autosave, hasta
                    // 60 s, y luego eran dos escrituras sueltas: si el proceso moría entre ellas, lo intercambiado quedaba en ambos o en
                    // ninguno). Si otro guardado de este tick (cambio de mapa, autosave, salida) ya los escribió juntos, la marca de
                    // `TradeSavePartner` ya no está. Un intercambio vacío no la pone y no se guarda.
                    if (tr.State == "completed" && tr.Trade.A.TradeSavePartner is not null) session.Save(tr.Trade.A, ctx.NowMs, "trade");
                    break;
                }
                case DuelZoneEvent dz when dz.Player.ConnectionId >= 0:
                {
                    // HU-101: solo a quien salió o volvió; el aviso lo cuenta el cliente desde `outsideMs`.
                    var outsideMs = dz.LosesAtMs is { } at ? (int?)Math.Max(0, at - ctx.NowMs) : null;
                    connections.Send(dz.Player.ConnectionId, new DuelUpdate("active", dz.Duel.Opponent(dz.Player).Id.Value, null, null, ToDuelZone(dz.Duel), outsideMs));
                    break;
                }
                case KnownSpellsResetEvent ks when ks.Player.ConnectionId >= 0:
                    // `/level` hacia abajo: hechizos, barra y nivel otra vez, como al cambiar de clase.
                    SendRenewedWelcome(ks.Player, ks.MapInstanceId, ctx);
                    break;
                case ClassChangedEvent cc when cc.Player.ConnectionId >= 0:
                    // El cliente necesita hechizos y barra nuevos: Welcome renovado es lo más simple y completo.
                    SendRenewedWelcome(cc.Player, cc.MapInstanceId, ctx);
                    break;
                default:
                    break;
            }
        }
        FlushBatches(ctx.Tick);
        if (ctx.Tick % PartyFrameEveryTicks == 0) foreach (var party in parties.All) SendPartyUpdate(party);
    }

    /// <summary>
    /// Welcome por la misma conexión (cambio de clase, `/level` hacia abajo). El cliente lo toma como una actualización: borra
    /// sus recargas y el estado de palancas y puertas, así que van detrás, como al entrar; los demás ven el nivel y la clase nuevos.
    /// </summary>
    private void SendRenewedWelcome(Player player, int mapInstanceId, TickContext ctx)
    {
        connections.Send(player.ConnectionId, mapper.ToWelcome(player, session.MapIdOf(player), ctx.Tick));
        foreach (var cd in mapper.ToCooldowns(player)) connections.Send(player.ConnectionId, cd);
        if (world.GetInstance(mapInstanceId) is { } map && ToMapObjects(map) is { } objects) connections.Send(player.ConnectionId, objects);
        Broadcast(mapInstanceId, player, SnapshotBuilder.ToSpawn(player));
    }

    private static DuelZoneDto ToDuelZone(DuelSession duel) =>
        new(SnapshotBuilder.Px(duel.Center.X), SnapshotBuilder.Px(duel.Center.Y), SnapshotBuilder.Px((float)duel.ZoneRadiusTiles));

    /// <summary>HU-083: estado de todos los objetos del mapa (palancas y puertas) para quien entra; null si el mapa no tiene.</summary>
    public static MapObjects? ToMapObjects(Game.Map.MapInstance map)
    {
        if (map.Data.Levers.Count + map.Data.Doors.Count == 0) return null;
        var states = Game.Map.MapObjectSystem.States(map);
        var list = new List<MapObjectDto>(states.Count);
        foreach (var (id, state) in states) list.Add(new MapObjectDto(id, state));
        return new MapObjects(list);
    }

    /// <summary>AuraApplied de un aura ya puesta (al aplicarse, al entrar alguien en la AOI o al reconectar).</summary>
    public static AuraApplied ToAuraApplied(Actor target, AuraInstance aura, long nowMs) =>
        new(target.Id.Value, aura.AuraId, aura.CasterId?.Value, aura.Stacks, aura.RemainingMs(nowMs));

    /// <summary>La plantilla sale del inventario de quien ofrece: el otro no tiene el objeto y no sabría qué recibe.</summary>
    private static OfferDto ToOffer(TradeOfferState o, Game.Entities.Player owner) =>
        new(o.Items.Select(i => new OfferedItemDto(i.ItemId.ToString(), Game.Items.InventoryOps.Find(owner.Inventory, i.ItemId)?.TemplateId ?? "", i.Qty)).ToList(), o.Gold);

    private void SendPartyUpdate(Party party)
    {
        var leaderName = players.ByCharacter(party.Leader)?.Name ?? party.Members.FirstOrDefault(m => m.CharacterId == party.Leader)?.Name ?? "";
        var members = new List<PartyMemberDto>(party.Members.Count);
        foreach (var m in party.Members)
        {
            var p = players.ByCharacter(m.CharacterId);
            var online = p is { ConnectionId: >= 0 };
            var hpPct = p is null || p.MaxHp <= 0 ? 0 : (int)Math.Round(100.0 * p.Hp / p.MaxHp);
            int? resPct = p is null || p.MaxResource <= 0 ? null : (int)Math.Round(100.0 * p.Resource / p.MaxResource);
            members.Add(new PartyMemberDto(m.Name, p?.Id.Value, p?.ClassId ?? m.ClassId, p?.Level ?? 0, hpPct, online, p is null ? null : session.MapIdOf(p), resPct));
        }
        var msg = new PartyUpdate(leaderName, members);
        foreach (var m in party.Members)
            if (players.ByCharacter(m.CharacterId) is { ConnectionId: >= 0 } p) connections.Send(p.ConnectionId, msg);
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

    private static string RarityName(Content.Defs.Rarity r) => r switch
    {
        Content.Defs.Rarity.Uncommon => "poco común",
        Content.Defs.Rarity.Rare => "raro",
        Content.Defs.Rarity.Epic => "épico",
        _ => r.ToString().ToLowerInvariant(),
    };
}
