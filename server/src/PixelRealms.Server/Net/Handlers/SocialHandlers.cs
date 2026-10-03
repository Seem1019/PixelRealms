using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Social;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Players;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>HU-060: `ChatSend{channel, text, to?}`; comandos `/g`, `/w`, `/p`, `/invite`, `/leave`, `/kick`, `/duel`, `/rendirse`, `/trade`, `/who` los traduce el cliente a mensajes; `/who` llega como ChatSend{channel:"who"}.</summary>
public sealed class ChatSendHandler(CombatHandlerDeps deps, PlayerRegistry players, WorldSession session) : IMessageHandler<ChatSend>
{
    public void Handle(ChatSend msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        if (msg.Channel == "who")
        {
            // HU-063 CA1: lista de conectados (nombre, clase, nivel, zona) como mensaje de sistema.
            var lines = players.All.Where(x => x.ConnectionId >= 0).OrderBy(x => x.Name)
                .Select(x => $"{x.Name} · {x.ClassId} · nv {x.Level} · {ZoneOf(x)}");
            ctx.Send(new ChatMessage("system", "", string.Join("\n", lines), ctx.Tick.NowMs));
            return;
        }
        var error = deps.Combat.Chat.Send(p, msg.Channel, msg.Text, msg.To, map, players.All, id => deps.Combat.Parties.PartyOf(id), ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }

    private string ZoneOf(Player p) => OnlineListHandler.ZoneOf(p, deps, session);
}

/// <summary>HU-063 CA1: `OnlineListRequest` → `OnlineList` con los conectados (nombre, clase, nivel, zona), ordenados por nombre.</summary>
public sealed class OnlineListHandler(CombatHandlerDeps deps, PlayerRegistry players, WorldSession session) : IMessageHandler<OnlineListRequest>
{
    public void Handle(OnlineListRequest msg, HandlerContext ctx)
    {
        if (ctx.Player is null) return;
        var list = players.All.Where(x => x.ConnectionId >= 0).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new OnlinePlayerDto(x.Name, x.ClassId, x.Level, ZoneOf(x, deps, session))).ToList();
        ctx.Send(new OnlineList(list));
    }

    /// <summary>Zona del mapa donde está (nombre de Tiled) o, si no hay, el nombre del mapa.</summary>
    public static string ZoneOf(Player p, CombatHandlerDeps deps, WorldSession session)
    {
        var map = deps.MapOf(p);
        return map?.Data.ZoneAt(p.Position)?.Name ?? map?.Data.DisplayName ?? session.MapIdOf(p);
    }
}

public sealed class PartyInviteHandler(CombatHandlerDeps deps, PlayerRegistry players) : IMessageHandler<PartyInvite>
{
    public void Handle(PartyInvite msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var target = players.ByName(msg.Name ?? "");
        if (target is null || target.ConnectionId < 0) { ctx.SendError(ErrorCodes.NotFound); return; }
        var error = deps.Combat.Parties.Invite(p, target, ctx.Tick.NowMs, deps.Combat.Services.Content.Rules.Group);
        if (error is not null) { ctx.SendError(error); return; }
        ctx.Tick.Emit(new PartyInvitedEvent(map.Id, target, deps.Combat.Parties.PendingInvite(target.CharacterId)!));
    }
}

public sealed class PartyRespondHandler(CombatHandlerDeps deps, PlayerRegistry players) : IMessageHandler<PartyRespond>
{
    public void Handle(PartyRespond msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var (party, error) = deps.Combat.Parties.Respond(p, msg.Accept, players.ByCharacter, ctx.Tick.NowMs, deps.Combat.Services.Content.Rules.Group);
        if (error is not null) { ctx.SendError(error); return; }
        if (party is not null) ctx.Tick.Emit(new PartyChangedEvent(map.Id, party, "joined"));
    }
}

public sealed class PartyLeaveHandler(CombatHandlerDeps deps) : IMessageHandler<PartyLeave>
{
    public void Handle(PartyLeave msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var (party, disbanded, last) = deps.Combat.Parties.Leave(p.CharacterId);
        if (party is not null) ctx.Tick.Emit(new PartyChangedEvent(map.Id, party, "left"));
        if (disbanded && last is { } l) ctx.Tick.Emit(new PartyDisbandedEvent(map.Id, l));
        ctx.Tick.Emit(new PartyDisbandedEvent(map.Id, p.CharacterId)); // el que sale ve su marco vacío
    }
}

public sealed class PartyKickHandler(CombatHandlerDeps deps) : IMessageHandler<PartyKick>
{
    public void Handle(PartyKick msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Parties.Kick(p.CharacterId, msg.Name ?? "", out var party, out var kicked);
        if (error is not null) { ctx.SendError(error); return; }
        if (party is not null) ctx.Tick.Emit(new PartyChangedEvent(map.Id, party, "kicked"));
        if (kicked is { } k) ctx.Tick.Emit(new PartyDisbandedEvent(map.Id, k));
        if (party is null) ctx.Tick.Emit(new PartyDisbandedEvent(map.Id, p.CharacterId));
    }
}

public sealed class DuelRequestHandler(CombatHandlerDeps deps, PlayerRegistry players) : IMessageHandler<DuelRequest>
{
    public void Handle(DuelRequest msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var target = players.ByName(msg.Name ?? "");
        if (target is null || target.ConnectionId < 0) { ctx.SendError(ErrorCodes.NotFound); return; }
        var error = deps.Combat.Pvp.Request(p, target, map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class DuelRespondHandler(CombatHandlerDeps deps) : IMessageHandler<DuelRespond>
{
    public void Handle(DuelRespond msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Pvp.Respond(p, msg.Accept, map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class DuelForfeitHandler(CombatHandlerDeps deps) : IMessageHandler<DuelForfeit>
{
    public void Handle(DuelForfeit msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Pvp.Forfeit(p, map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class TradeRequestHandler(CombatHandlerDeps deps, PlayerRegistry players) : IMessageHandler<TradeRequest>
{
    public void Handle(TradeRequest msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var target = players.ByName(msg.Name ?? "");
        if (target is null || target.ConnectionId < 0) { ctx.SendError(ErrorCodes.NotFound); return; }
        var error = deps.Combat.Trades.Request(p, target, map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class TradeRespondHandler(CombatHandlerDeps deps) : IMessageHandler<TradeRespond>
{
    public void Handle(TradeRespond msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Trades.Respond(p, msg.Accept, map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class TradeOfferHandler(CombatHandlerDeps deps) : IMessageHandler<TradeOffer>
{
    public void Handle(TradeOffer msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var items = new List<(Guid, int)>();
        foreach (var it in msg.Items ?? [])
        {
            if (!Guid.TryParse(it.ItemId, out var id)) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
            items.Add((id, it.Qty));
        }
        var error = deps.Combat.Trades.Offer(p, items, msg.Gold, map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class TradeConfirmHandler(CombatHandlerDeps deps) : IMessageHandler<TradeConfirm>
{
    public void Handle(TradeConfirm msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Trades.Confirm(p, msg.Version, map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class TradeCancelHandler(CombatHandlerDeps deps) : IMessageHandler<TradeCancel>
{
    public void Handle(TradeCancel msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Trades.CancelBy(p, "cancelled", map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
    }
}

/// <summary>HU-044: `ChangeClass{npcId, classId, reqId}`.</summary>
public sealed class ChangeClassHandler(CombatHandlerDeps deps, WorldSession session, ILogger<ChangeClassHandler> logger) : IMessageHandler<ChangeClass>
{
    public void Handle(ChangeClass msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var oldClass = p.ClassId;
        var error = deps.Combat.ClassChange.Change(p, new EntityId(msg.NpcId), msg.ClassId ?? "", map, ctx.Tick);
        if (error is not null) { ctx.SendError(error, msg.ReqId); return; }
        logger.LogInformation("{Name} cambió de clase: {OldClass} → {NewClass}", p.Name, oldClass, p.ClassId); // HU-044 CA5
        session.Save(p, ctx.Tick.NowMs, "class_change");
        ctx.Tick.Emit(new Game.Items.InventoryChangedEvent(map.Id, p, msg.ReqId));
    }
}

/// <summary>
/// HU-015: `Logout{reqId?}`. En combate → `Error{in_combat}` y sigue dentro (no sirve para escapar de una pelea). Si no,
/// WorldSession guarda y lo saca del mundo; se confirma con `LoggedOut` y se cierra la conexión cuando ese mensaje ya salió.
/// </summary>
public sealed class LogoutHandler(WorldSession session) : IMessageHandler<Logout>
{
    public void Handle(Logout msg, HandlerContext ctx)
    {
        // Sin jugador (antes del Welcome o ya fuera) no hay nada que guardar: se confirma igual para que el cliente no espere.
        if (ctx.Player is { } p && session.Logout(p, ctx.Tick) is { } error) { ctx.SendError(error, msg.ReqId); return; }
        ctx.Send(new LoggedOut());
        ctx.CloseAfterFlush(WorldSession.LogoutReason);
    }
}
