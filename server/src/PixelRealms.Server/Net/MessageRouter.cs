using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelRealms.Game.Core;
using PixelRealms.Protocol;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;

namespace PixelRealms.Server.Net;

/// <summary>Contexto que recibe un handler: conexión, tick y utilidades para responder.</summary>
public sealed class HandlerContext(ConnectionManager connections, TickContext tick, int connectionId, PixelRealms.Server.Players.PlayerRegistry players)
{
    public ConnectionManager Connections { get; } = connections;

    public TickContext Tick { get; } = tick;

    public int ConnectionId { get; } = connectionId;

    public PixelRealms.Server.Players.PlayerRegistry Players { get; } = players;

    /// <summary>Jugador de esta conexión, o null si aún no entró al mundo.</summary>
    public PixelRealms.Game.Entities.Player? Player => Players.ByConnection(ConnectionId);

    public void Send(IServerMessage msg) => Connections.Send(ConnectionId, msg);

    public void SendError(string code, int? reqId = null, string? message = null) => Send(new Error(code, message, reqId));

    public void Close(string reason) => Connections.Close(ConnectionId, reason);
}

/// <summary>Handler de un mensaje del cliente; se ejecuta en el hilo del tick (paso 2 de docs/architecture.md §3).</summary>
public interface IMessageHandler<in T> where T : IClientMessage
{
    void Handle(T msg, HandlerContext ctx);
}

/// <summary>Eventos de conexión/desconexión que el tick también procesa en orden con los mensajes.</summary>
public interface IConnectionObserver
{
    void OnConnected(int connectionId, HandlerContext ctx);

    /// <summary>La conexión terminó; `reason` es el motivo de cierre ("client_close" = cierre normal del cliente).</summary>
    void OnDisconnected(int connectionId, string reason, HandlerContext ctx);

    /// <summary>Hello aceptado: el adjunto es el personaje ya leído de BD (HU-014).</summary>
    void OnPlayerJoin(int connectionId, object? attachment, HandlerContext ctx);
}

/// <summary>
/// Drena la cola de entrada en el tick (máx. 500 mensajes por tick) y despacha cada sobre a su handler por nombre `t`, sin
/// reflexión por mensaje (diccionario nombre → delegado). Un mensaje sin handler recibe `invalid_payload`.
/// </summary>
public sealed class MessageRouter(ConnectionManager connections, PixelRealms.Server.Players.PlayerRegistry players, ILogger<MessageRouter> logger)
{
    public const int MaxMessagesPerTick = 500;

    private readonly Dictionary<string, Action<JsonElement, HandlerContext>> _handlers = new(StringComparer.Ordinal);
    private readonly List<IConnectionObserver> _observers = new();

    public void Register<T>(IMessageHandler<T> handler) where T : class, IClientMessage
    {
        var name = typeof(T).Name;
        var info = (JsonTypeInfo<T>)ProtocolJsonContext.Default.GetTypeInfo(typeof(T))!;
        _handlers[name] = (payload, ctx) =>
        {
            T? msg;
            try { msg = payload.ValueKind == JsonValueKind.Object ? payload.Deserialize(info) : null; }
            catch (JsonException) { msg = null; }
            if (msg is null) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
            handler.Handle(msg, ctx);
        };
    }

    public void AddObserver(IConnectionObserver observer) => _observers.Add(observer);

    public IReadOnlyCollection<string> RegisteredTypes => _handlers.Keys;

    /// <summary>Gancho pre-tick: vacía la cola y aplica las intenciones.</summary>
    public void Drain(TickContext tick)
    {
        var reader = connections.Inbound.Reader;
        var n = 0;
        while (n < MaxMessagesPerTick && reader.TryRead(out var inbound))
        {
            n++;
            var ctx = new HandlerContext(connections, tick, inbound.ConnectionId, players);
            // HU-072 CA2: ConnId / CharacterName / AccountId como propiedades del log mientras se procesa el mensaje.
            var scopePlayer = players.ByConnection(inbound.ConnectionId);
            using var scope = logger.BeginScope(new LogScope(inbound.ConnectionId, scopePlayer?.Name, scopePlayer?.AccountId));
            try
            {
                switch (inbound.Kind)
                {
                    case InboundKind.Connected:
                        foreach (var o in _observers) o.OnConnected(inbound.ConnectionId, ctx);
                        break;
                    case InboundKind.Disconnected:
                        foreach (var o in _observers) o.OnDisconnected(inbound.ConnectionId, inbound.Attachment as string ?? "", ctx);
                        break;
                    case InboundKind.PlayerJoin:
                        foreach (var o in _observers) o.OnPlayerJoin(inbound.ConnectionId, inbound.Attachment, ctx);
                        break;
                    default:
                        if (_handlers.TryGetValue(inbound.Type, out var h)) h(inbound.Payload, ctx);
                        else ctx.SendError(ErrorCodes.InvalidPayload);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Un handler nunca debe tumbar el tick (server-authority-reviewer §2).
                logger.LogError(ex, "Error en el handler de {Type} (conexión {Conn})", inbound.Type, inbound.ConnectionId);
                ctx.SendError(ErrorCodes.InvalidPayload);
            }
        }
    }
}

/// <summary>Scope de log sin diccionario: se enumera como pares clave/valor (lo entienden JsonConsole y Serilog).</summary>
internal sealed class LogScope(int connId, string? characterName, Guid? accountId) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public int Count => 3;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new("ConnId", connId),
        1 => new("CharacterName", characterName),
        2 => new("AccountId", accountId),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"conn {connId} {characterName}";
}
