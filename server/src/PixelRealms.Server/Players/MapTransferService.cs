using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Map;
using PixelRealms.Game.Portals;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Net;

namespace PixelRealms.Server.Players;

/// <summary>
/// Cambio de mapa (HU-027, ADR-007): con <see cref="PortalUsed"/> saca al jugador de su instancia (EntityDespawn{left} a quienes
/// lo veían), lo mete en la instancia de destino en (targetX, targetY), envía ChangeMap y guarda (HU-026 CA6); los EntitySpawn
/// de la nueva AOI salen en el siguiente tick. Con <see cref="PortalRejected"/> envía el Error correspondiente.
/// Gancho post-tick, antes del EventDispatcher.
/// </summary>
public sealed class MapTransferService(World world, InterestSystem interest, ConnectionManager connections, WorldSession session, ILogger<MapTransferService> logger)
{
    public void OnPostTick(TickContext ctx)
    {
        // Los eventos nuevos (EntityLeftView) se añaden al final; recorremos por índice solo los ya existentes.
        var count = ctx.Events.Count;
        for (var i = 0; i < count; i++)
        {
            switch (ctx.Events[i])
            {
                case PortalUsed used:
                    Transfer(used.Player, used.Portal.TargetMapId, new Vec2(used.Portal.TargetX, used.Portal.TargetY), ctx, $"portal {used.Portal.PortalId}");
                    break;
                case PortalRejected rejected when rejected.Player.ConnectionId >= 0:
                    var message = rejected.ErrorCode == PortalPolicy.LevelTooLow && rejected.Portal.MinLevel is { } min ? $"Necesitas nivel {min}" : null;
                    connections.Send(rejected.Player.ConnectionId, new Error(rejected.ErrorCode, message, null));
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Mueve al jugador a otra instancia (lo usan portales y, más adelante, respawn/admin).</summary>
    public bool Transfer(Player player, string targetMapId, Vec2 position, TickContext ctx, string reason)
    {
        var from = world.GetInstance(player.MapInstanceId);
        var to = world.InstanceOf(targetMapId);
        if (to is null || from is null)
        {
            logger.LogWarning("Cambio de mapa imposible para {Name}: destino '{Map}' sin instancia", player.Name, targetMapId);
            if (player.ConnectionId >= 0) connections.Send(player.ConnectionId, new Error(ErrorCodes.NotFound, "Mapa no disponible", null));
            return false;
        }
        if (to.Data.Collision.IsSolidAt(position.X, position.Y)) position = to.Data.DefaultGraveyard.Position;

        // Un casteo o un salto llevan objetivo y coordenadas del mapa de origen: no pueden terminar en el de destino.
        if (player.Combat.Cast is { } cast)
        {
            player.Combat.Cast = null;
            if (player.ConnectionId >= 0) connections.Send(player.ConnectionId, new CastEnded(player.Id.Value, cast.Spell.Id, CastResults.Cancelled, null));
        }
        player.Combat.Flight = null;
        interest.ForgetEntity(from, player.Id, InterestSystem.ReasonLeft, ctx);
        from.Remove(player.Id);
        player.Position = position;
        player.MoveDx = 0;
        player.MoveDy = 0;
        player.RejectedPortalId = null;
        to.Add(player);
        player.Dirty = true;
        if (player.ConnectionId >= 0)
            connections.Send(player.ConnectionId, new ChangeMap(to.MapId, SnapshotBuilder.Px(position.X), SnapshotBuilder.Px(position.Y)));
        session.Save(player, ctx.NowMs, "change_map");
        logger.LogInformation("{Name}: {From} → {To} ({Reason})", player.Name, from.MapId, to.MapId, reason);
        return true;
    }
}
