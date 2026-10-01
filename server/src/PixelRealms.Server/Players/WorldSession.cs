using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Net;

namespace PixelRealms.Server.Players;

/// <summary>
/// Entrada y salida de jugadores del mundo, en el tick (HU-014 CA2/CA5): crea el <see cref="Player"/> desde el personaje
/// cargado, lo mete en la instancia de su mapa y envía Welcome; al desconectarse lo guarda y lo saca (el linkdead de
/// HU-025 refina la salida).
/// </summary>
public sealed class WorldSession(World world, PlayerRegistry players, PlayerMapper mapper, ReloadableContent content, SaveService saver, ILogger<WorldSession> logger) : IConnectionObserver
{
    public event Action<Player, MapInstanceRef>? PlayerEntered;

    public event Action<Player, MapInstanceRef>? PlayerLeft;

    public void OnConnected(int connectionId, HandlerContext ctx) { }

    public void OnPlayerJoin(int connectionId, object? attachment, HandlerContext ctx)
    {
        if (attachment is not CharacterSaveDto dto) { ctx.SendError(ErrorCodes.BadTicket); ctx.Close("bad_ticket"); return; }

        // HU-014 CA5: la cuenta ya tiene un personaje dentro → la sesión anterior se guarda y se desconecta primero.
        var previous = players.ByAccount(dto.AccountId);
        if (previous is not null)
        {
            var prevConn = previous.ConnectionId;
            Leave(previous, "replaced");
            if (prevConn >= 0) ctx.Connections.Close(prevConn, "replaced");
        }

        var mapId = world.Maps.ContainsKey(dto.MapId) ? dto.MapId : content.Current.Rules.World.StartMapId;
        var instance = world.InstanceOf(mapId);
        if (instance is null) { ctx.SendError(ErrorCodes.NotFound, message: "Mapa no disponible"); ctx.Close("no_map"); return; }

        var player = mapper.ToPlayer(dto, world.EntityIds.Next());
        if (mapId != dto.MapId || player.Position == Vec2.Zero) player.Position = instance.Data.DefaultGraveyard.Position;
        if (instance.Data.Collision.IsSolidAt(player.Position.X, player.Position.Y)) player.Position = instance.Data.DefaultGraveyard.Position;
        instance.Add(player);
        players.Add(player, connectionId);
        ctx.Send(mapper.ToWelcome(player, mapId, ctx.Tick.Tick));
        logger.LogInformation("{Name} entró en {Map} (conexión {Conn})", player.Name, mapId, connectionId);
        PlayerEntered?.Invoke(player, new MapInstanceRef(instance));
    }

    public void OnDisconnected(int connectionId, HandlerContext ctx)
    {
        var player = players.ByConnection(connectionId);
        if (player is null) return;
        Leave(player, "disconnected");
    }

    /// <summary>Saca al jugador del mundo y encola su guardado.</summary>
    public void Leave(Player player, string reason)
    {
        var instance = world.GetInstance(player.MapInstanceId);
        saver.Enqueue(mapper.ToSave(player));
        players.Remove(player);
        if (instance is not null)
        {
            instance.Remove(player.Id);
            PlayerLeft?.Invoke(player, new MapInstanceRef(instance));
        }
        logger.LogInformation("{Name} salió del mundo ({Reason})", player.Name, reason);
    }

    public string MapIdOf(Player p) => world.GetInstance(p.MapInstanceId)?.MapId ?? content.Current.Rules.World.StartMapId;
}

/// <summary>Referencia ligera a la instancia para los eventos de entrada/salida.</summary>
public readonly record struct MapInstanceRef(PixelRealms.Game.Map.MapInstance Instance);
