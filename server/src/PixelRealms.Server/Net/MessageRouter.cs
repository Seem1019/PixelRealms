using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelRealms.Game.Core;
using PixelRealms.Protocol;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;

namespace PixelRealms.Server.Net;

/// <summary>Contexto que recibe un handler: conexión, tick y utilidades para responder.</summary>
public sealed class HandlerContext(ConnectionManager connections, TickContext tick, int connectionId)
{
    public ConnectionManager Connections { get; } = connections;

    public TickContext Tick { get; } = tick;

    public int ConnectionId { get; } = connectionId;

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

    void OnDisconnected(int connectionId, HandlerContext ctx);
}

/// <summary>
/// Drena la cola de entrada en el tick (máx. 500 mensajes por tick) y despacha cada sobre a su handler por nombre `t`, sin
/// reflexión por mensaje (diccionario nombre → delegado). Un mensaje sin handler recibe `invalid_payload`.
/// </summary>
public sealed class MessageRouter(ConnectionManager connections, ILogger<MessageRouter> logger)
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
            var ctx = new HandlerContext(connections, tick, inbound.ConnectionId);
            try
            {
                switch (inbound.Kind)
                {
                    case InboundKind.Connected:
                        foreach (var o in _observers) o.OnConnected(inbound.ConnectionId, ctx);
                        break;
                    case InboundKind.Disconnected:
                        foreach (var o in _observers) o.OnDisconnected(inbound.ConnectionId, ctx);
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
