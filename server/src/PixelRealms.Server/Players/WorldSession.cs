using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Sessions;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Net;

namespace PixelRealms.Server.Players;

/// <summary>
/// Entrada y salida de jugadores del mundo, en el tick (HU-014 CA2/CA5, HU-025): crea el <see cref="Player"/> desde el
/// personaje cargado, lo mete en la instancia de su mapa y envía Welcome. Al perder la conexión el jugador queda linkdead
/// (quieto) y <see cref="SweepLinkdead"/> lo guarda y lo saca según <see cref="LinkdeadPolicy"/>; un cierre normal del
/// cliente fuera de combate guarda de inmediato. Si reconecta a tiempo, la conexión nueva retoma el mismo personaje.
/// </summary>
public sealed class WorldSession(World world, PlayerRegistry players, PlayerMapper mapper, ReloadableContent content, SaveService saver, InterestSystem interest,
    Microsoft.Extensions.Options.IOptions<PersistenceOptions> persistence, ILogger<WorldSession> logger) : IConnectionObserver
{
    /// <summary>Lo fija la composición: grupos/duelos/intercambios reaccionan a entradas y salidas (HU-061 CA5, HU-064 CA4, HU-059 CA4).</summary>
    public Game.Combat.CombatModule? Combat { get; set; }

    private readonly long _autosaveMs = (long)(persistence.Value.AutosaveSec * 1000);

    /// <summary>Motivo de cierre que registra WebSocketSession cuando el cliente envía el frame Close (HU-025 CA4).</summary>
    public const string ClientCloseReason = "client_close";

    public event Action<Player, MapInstanceRef>? PlayerEntered;

    public event Action<Player, MapInstanceRef>? PlayerLeft;

    public int LinkdeadCount => players.All.Count(p => p.IsLinkdead);

    public void OnConnected(int connectionId, HandlerContext ctx) { }

    public void OnPlayerJoin(int connectionId, object? attachment, HandlerContext ctx)
    {
        if (attachment is not CharacterSaveDto dto) { ctx.SendError(ErrorCodes.BadTicket); ctx.Close("bad_ticket"); return; }

        var previous = players.ByAccount(dto.AccountId);
        if (previous is not null && previous.CharacterId == dto.Id && previous.IsLinkdead)
        {
            Reconnect(previous, connectionId, ctx);
            return;
        }

        // HU-014 CA5: la cuenta ya tiene un personaje dentro → la sesión anterior se guarda y se desconecta primero.
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
        player.LastSaveAtMs = ctx.Tick.NowMs;
        player.Dirty = false;
        ctx.Send(mapper.ToWelcome(player, mapId, ctx.Tick.Tick));
        logger.LogInformation("{Name} entró en {Map} (conexión {Conn})", player.Name, mapId, connectionId);
        if (Combat?.Parties.SetOnline(player.CharacterId, true, ctx.Tick.NowMs) is { } party) ctx.Tick.Emit(new Game.Social.PartyChangedEvent(instance.Id, party, "online"));
        PlayerEntered?.Invoke(player, new MapInstanceRef(instance));
        // Welcome no trae stats primarios ni oro: sin esto el cliente los ve a 0. Va al final para no dejar la entrada a medias.
        ctx.Send(mapper.ToStatsUpdate(player));
    }

    /// <summary>HU-025 CA2: la conexión nueva toma el personaje que seguía en el mundo; se reenvía Welcome y la AOI completa.</summary>
    private void Reconnect(Player player, int connectionId, HandlerContext ctx)
    {
        players.Attach(player, connectionId);
        LinkdeadPolicy.MarkReconnected(player, connectionId);
        var instance = world.GetInstance(player.MapInstanceId);
        var mapId = instance?.MapId ?? content.Current.Rules.World.StartMapId;
        ctx.Send(mapper.ToWelcome(player, mapId, ctx.Tick.Tick));
        if (instance is not null) interest.ResetObserver(instance, player);
        ctx.Send(mapper.ToStatsUpdate(player)); // stats y oro tras el Welcome (ver OnPlayerJoin)
        logger.LogInformation("{Name} reconectó (conexión {Conn})", player.Name, connectionId);
    }

    public void OnDisconnected(int connectionId, string reason, HandlerContext ctx)
    {
        var player = players.ByConnection(connectionId);
        if (player is null) return;
        var rules = content.Current.Rules;
        if (reason == ClientCloseReason && !player.IsInCombat(ctx.Tick.NowMs, rules.Combat.InCombatWindowSec))
        {
            // CA4: cierre normal (la X del juego) fuera de combate → guardar y salir ya.
            Leave(player, "closed");
            return;
        }
        players.Detach(connectionId);
        LinkdeadPolicy.MarkLinkdead(player, ctx.Tick.NowMs);
        if (world.GetInstance(player.MapInstanceId) is { } ldMap && Combat is not null)
        {
            Combat.Pvp.Abandon(player, "disconnected", ldMap, ctx.Tick);
            Combat.Trades.CancelBy(player, "disconnected", ldMap, ctx.Tick);
        }
        logger.LogInformation("{Name} quedó linkdead ({Reason}); sale en {Sec} s salvo combate", player.Name, reason, rules.Combat.LinkdeadSec);
    }

    /// <summary>Gancho post-tick (HU-025 CA1 + HU-026 CA3): saca a los linkdead vencidos y encola el autosave de los `Dirty`.</summary>
    private TickContext? _sweepCtx;

    public void SweepLinkdead(TickContext ctx)
    {
        _sweepCtx = ctx;
        // HU-061 CA5: invitaciones caducadas y desconectados fuera tras offlineGraceSec.
        if (Combat is not null)
            foreach (var (party, _, disbanded, last) in Combat.Parties.Tick(ctx.NowMs, ctx.Rules.Group))
            {
                if (party is not null) ctx.Emit(new Game.Social.PartyChangedEvent(0, party, "timeout"));
                if (disbanded && last is { } l) ctx.Emit(new Game.Social.PartyDisbandedEvent(0, l));
            }
        List<Player>? expired = null;
        foreach (var p in players.All)
        {
            if (LinkdeadPolicy.ShouldRemove(p, ctx.NowMs, ctx.Rules)) { (expired ??= new List<Player>()).Add(p); continue; }
            if (p.Dirty && ctx.NowMs - p.LastSaveAtMs >= _autosaveMs) Save(p, ctx.NowMs, "autosave");
        }
        if (expired is null) return;
        foreach (var p in expired) Leave(p, "linkdead");
    }

    /// <summary>Guardado por evento (ADR-018 / HU-026 CA6): salir, cambiar de mapa, subir de nivel, intercambio, cambio de clase, morir.</summary>
    public void Save(Player player, long nowMs, string reason)
    {
        saver.Enqueue(mapper.ToSave(player));
        player.Dirty = false;
        player.LastSaveAtMs = nowMs;
        logger.LogDebug("{Name} guardado ({Reason})", player.Name, reason);
    }

    /// <summary>Saca al jugador del mundo y encola su guardado.</summary>
    public void Leave(Player player, string reason)
    {
        var instance = world.GetInstance(player.MapInstanceId);
        if (instance is not null && Combat is not null && _sweepCtx is not null)
        {
            Combat.Pvp.Abandon(player, reason, instance, _sweepCtx);
            Combat.Trades.CancelBy(player, reason, instance, _sweepCtx);
            if (Combat.Parties.SetOnline(player.CharacterId, false, _sweepCtx.NowMs) is { } party) _sweepCtx.Emit(new Game.Social.PartyChangedEvent(instance.Id, party, "offline"));
        }
        saver.Enqueue(mapper.ToSave(player));
        player.Dirty = false;
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
