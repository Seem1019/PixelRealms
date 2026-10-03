using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>HU-027 CA1: `UsePortal{portalId}` explícito; el `PortalSystem` lo resuelve en el tick (rango ≤ 1 casilla, combate, nivel).</summary>
public sealed class UsePortalHandler : IMessageHandler<UsePortal>
{
    public void Handle(UsePortal msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null) return;
        if (string.IsNullOrWhiteSpace(msg.PortalId) || msg.PortalId.Length > 64) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
        player.RequestedPortalId = msg.PortalId;
    }
}

/// <summary>HU-083: `Interact{objectId}` sobre una palanca; el sistema de objetos valida vida y distancia y la activa en el tick.</summary>
public sealed class InteractHandler(CombatHandlerDeps deps) : IMessageHandler<Interact>
{
    public void Handle(Interact msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null || deps.MapOf(player) is not { } map) return;
        if (string.IsNullOrWhiteSpace(msg.ObjectId) || msg.ObjectId.Length > 64) { ctx.SendError(ErrorCodes.InvalidPayload, msg.ReqId); return; }
        var error = deps.Combat.Objects.Pull(player, msg.ObjectId, map, ctx.Tick);
        if (error is not null) ctx.SendError(error, msg.ReqId);
    }
}
