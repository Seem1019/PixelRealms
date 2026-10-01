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
