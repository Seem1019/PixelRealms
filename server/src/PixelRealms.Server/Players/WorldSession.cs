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

    /// <summary>
    /// Último guardado de cada personaje que salió del mundo, con su generación, hasta que vuelve a entrar (estado del tick).
    /// Un Hello que leyó la BD antes de ese guardado entra con este DTO y no con el leído: así salir y volver a entrar enseguida
    /// no devuelve items ya entregados ni pisa el guardado bueno (HU-015 CA5). No se borra al quedar escrito: un Hello con una
    /// lectura anterior puede seguir en camino al tick. Tras salir nadie más escribe el personaje, así que este DTO es la verdad;
    /// el tamaño lo acota el número de personajes que han salido desde el arranque.
    /// </summary>
    private readonly Dictionary<Guid, (CharacterSaveDto Dto, long Gen)> _departed = new();

    public int DepartedCount => _departed.Count;

    public void OnPlayerJoin(int connectionId, object? attachment, HandlerContext ctx)
    {
        var (dto, writtenGen) = attachment switch
        {
            LoadedCharacter lc => (lc.Dto, lc.WrittenGen),
            CharacterSaveDto plain => (plain, 0L),
            _ => (null, 0L),
        };
        if (dto is null) { ctx.SendError(ErrorCodes.BadTicket); ctx.CloseAfterFlush("bad_ticket"); return; }

        var previous = players.ByAccount(dto.AccountId);
        if (previous is not null && previous.CharacterId == dto.Id)
        {
            // El mismo personaje sigue en el mundo (linkdead o en otra conexión): la conexión nueva toma ese Player. Nunca se
            // recarga de la BD con él dentro: lo leído puede ser anterior a un intercambio o una venta ya hechos.
            var prevConn = previous.ConnectionId;
            if (!previous.IsLinkdead && prevConn >= 0 && prevConn != connectionId)
            {
                // El cliente nuevo no conoce el intercambio ni el duelo de la conexión anterior: se cancelan como al desconectarse.
                if (world.GetInstance(previous.MapInstanceId) is { } prevMap && Combat is not null)
                {
                    Combat.Pvp.Abandon(previous, "replaced", prevMap, ctx.Tick);
                    Combat.Trades.CancelBy(previous, "replaced", prevMap, ctx.Tick);
                }
                Save(previous, ctx.Tick.NowMs, "replaced"); // HU-014 CA5: la sesión anterior se guarda y se desconecta
                ctx.Connections.Close(prevConn, "replaced");
            }
            Reconnect(previous, connectionId, ctx);
            return;
        }
        var hadDeparted = _departed.TryGetValue(dto.Id, out var departed);
        if (hadDeparted && departed.Gen > writtenGen) dto = departed.Dto;

        // HU-014 CA5: la cuenta ya tiene otro personaje dentro → la sesión anterior se guarda y se desconecta primero.
        if (previous is not null)
        {
            // Salvo en combate: sacarlo sería escapar de la pelea con vida (ADR-018: un desconectado en combate sigue dentro
            // hasta salir de combate). Se rechaza la entrada; el cliente reintenta con un ticket nuevo.
            if (previous.IsInCombat(ctx.Tick.NowMs, content.Current.Rules.Combat.InCombatWindowSec))
            {
                ctx.SendError(ErrorCodes.InCombat, message: $"{previous.Name} sigue en combate");
                ctx.CloseAfterFlush(ErrorCodes.InCombat); // con Close, el cierre podía salir antes que el Error
                return;
            }
            var prevConn = previous.ConnectionId;
            Leave(previous, "replaced");
            if (prevConn >= 0) ctx.Connections.Close(prevConn, "replaced");
        }

        var mapId = world.Maps.ContainsKey(dto.MapId) ? dto.MapId : content.Current.Rules.World.StartMapId;
        var instance = world.InstanceOf(mapId);
        if (instance is null) { ctx.SendError(ErrorCodes.NotFound, message: "Mapa no disponible"); ctx.CloseAfterFlush("no_map"); return; }

        var player = mapper.ToPlayer(dto, world.EntityIds.Next());
        if (mapId != dto.MapId || player.Position == Vec2.Zero) player.Position = instance.Data.DefaultGraveyard.Position;
        if (instance.Collision.IsSolidAt(player.Position.X, player.Position.Y)) player.Position = instance.Data.DefaultGraveyard.Position;
        instance.Add(player);
        players.Add(player, connectionId);
        if (hadDeparted) _departed.Remove(dto.Id); // ya está dentro: lo siguiente que salga lo vuelve a apuntar
        player.LastSaveAtMs = ctx.Tick.NowMs;
        player.Dirty = false;
        ctx.Send(mapper.ToWelcome(player, mapId, ctx.Tick.Tick));
        logger.LogInformation("{Name} entró en {Map} (conexión {Conn})", player.Name, mapId, connectionId);
        if (Combat?.Parties.SetOnline(player.CharacterId, true, ctx.Tick.NowMs) is { } party) ctx.Tick.Emit(new Game.Social.PartyChangedEvent(instance.Id, party, "online"));
        PlayerEntered?.Invoke(player, new MapInstanceRef(instance));
        // Welcome no trae stats primarios ni oro: sin esto el cliente los ve a 0. Va al final para no dejar la entrada a medias.
        ctx.Send(mapper.ToStatsUpdate(player));
        foreach (var cd in mapper.ToCooldowns(player)) ctx.Send(cd); // recargas que siguieron corriendo fuera (HU-015)
        if (EventDispatcher.ToMapObjects(instance) is { } objects) ctx.Send(objects); // palancas y puertas (HU-083)
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
        foreach (var cd in mapper.ToCooldowns(player)) ctx.Send(cd);
        // Las auras propias siguieron corriendo mientras estaba linkdead: el cliente nuevo no las conoce (HU-098 CA2).
        foreach (var aura in player.Auras.All) ctx.Send(EventDispatcher.ToAuraApplied(player, aura, ctx.Tick.NowMs));
        if (instance is not null && EventDispatcher.ToMapObjects(instance) is { } objects) ctx.Send(objects);
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


    /// <summary>Guardado por evento (ADR-018 / HU-026 CA6): salir, cambiar de mapa, subir de nivel, intercambio, cambio de clase, morir.
    /// Si acaba de completar un intercambio sin guardar, se guarda junto con el otro (ver <see cref="Player.TradeSavePartner"/>).</summary>
    public void Save(Player player, long nowMs, string reason)
    {
        if (player.TradeSavePartner is { } partner)
        {
            SaveTogether(player, partner, nowMs, reason);
            return;
        }
        saver.Enqueue(mapper.ToSave(player));
        player.Dirty = false;
        player.LastSaveAtMs = nowMs;
        logger.LogDebug("{Name} guardado ({Reason})", player.Name, reason);
    }

    /// <summary>Guarda a los dos en la misma transacción (intercambio completado, HU-059): o quedan ambos o ninguno.</summary>
    public void SaveTogether(Player a, Player b, long nowMs, string reason)
    {
        saver.EnqueueTogether(mapper.ToSave(a), mapper.ToSave(b));
        foreach (var p in new[] { a, b })
        {
            p.Dirty = false;
            p.LastSaveAtMs = nowMs;
            p.TradeSavePartner = null;
        }
        logger.LogDebug("{A} y {B} guardados juntos ({Reason})", a.Name, b.Name, reason);
    }

    /// <summary>Motivo con el que `Logout` saca al jugador (HU-015).</summary>
    public const string LogoutReason = "logout";

    /// <summary>
    /// HU-015: `Logout` del cliente. En combate devuelve `in_combat` y no cambia nada; si no, cancela el casteo, guarda, saca al
    /// jugador como un cierre normal (duelo e intercambio se cancelan) y devuelve null. El handler responde y cierra.
    /// </summary>
    public string? Logout(Player player, TickContext tick)
    {
        var rules = content.Current.Rules;
        if (player.IsInCombat(tick.NowMs, rules.Combat.InCombatWindowSec)) return ErrorCodes.InCombat;
        if (world.GetInstance(player.MapInstanceId) is { } map) Combat?.Casts.Cancel(player, map, tick);
        Leave(player, LogoutReason, tick);
        return null;
    }

    /// <summary>Saca al jugador del mundo y encola su guardado.</summary>
    public void Leave(Player player, string reason) => Leave(player, reason, _sweepCtx);

    private void Leave(Player player, string reason, TickContext? tick)
    {
        var instance = world.GetInstance(player.MapInstanceId);
        if (instance is not null && Combat is not null && tick is not null)
        {
            Combat.Pvp.Abandon(player, reason, instance, tick);
            Combat.Trades.CancelBy(player, reason, instance, tick);
            if (Combat.Parties.SetOnline(player.CharacterId, false, tick.NowMs) is { } party) tick.Emit(new Game.Social.PartyChangedEvent(instance.Id, party, "offline"));
        }
        var save = mapper.ToSave(player);
        if (player.TradeSavePartner is { } partner)
        {
            // Sale en el mismo tick en que completó un intercambio: los dos en la misma transacción (HU-059).
            _departed[player.CharacterId] = (save, saver.EnqueueTogether(save, mapper.ToSave(partner))[0]);
            partner.Dirty = false;
            partner.TradeSavePartner = null;
            player.TradeSavePartner = null;
        }
        else _departed[player.CharacterId] = (save, saver.Enqueue(save));
        player.Dirty = false;
        players.Remove(player);
        if (instance is not null)
        {
            // Lo que dejó en marcha no sigue a su nombre: proyectiles en vuelo, amenaza y monstruos marcados (sin XP ni botín
            // para un ausente).
            Combat?.Casts.ForgetCaster(player, instance);
            foreach (var m in instance.Monsters.Values)
            {
                m.Threat.Remove(player.Id);
                if (m.TaggedBy == player.Id) m.TaggedBy = null;
            }
            instance.Remove(player.Id);
            PlayerLeft?.Invoke(player, new MapInstanceRef(instance));
        }
        logger.LogInformation("{Name} salió del mundo ({Reason})", player.Name, reason);
    }

    public string MapIdOf(Player p) => world.GetInstance(p.MapInstanceId)?.MapId ?? content.Current.Rules.World.StartMapId;
}

/// <summary>Referencia ligera a la instancia para los eventos de entrada/salida.</summary>
public readonly record struct MapInstanceRef(PixelRealms.Game.Map.MapInstance Instance);
