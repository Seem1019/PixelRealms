using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;

namespace PixelRealms.Server.Net;

/// <summary>Sesiones vivas por id de conexión. El tick la consulta para enviar; las sesiones se registran desde el endpoint /ws.</summary>
public sealed class ConnectionManager(ILogger<ConnectionManager> logger, IHelloGate helloGate)
{
    private readonly ConcurrentDictionary<int, WebSocketSession> _sessions = new();
    private int _nextId;

    public Channel<InboundMessage> Inbound { get; } = InboundChannel.Create();

    public int Count => _sessions.Count;

    public IEnumerable<WebSocketSession> Sessions => _sessions.Values;

    public WebSocketSession? Get(int connectionId) => _sessions.GetValueOrDefault(connectionId);

    public async Task HandleAsync(WebSocket socket, TimeSpan idleTimeout, CancellationToken serverStopping)
    {
        var id = Interlocked.Increment(ref _nextId);
        using var session = new WebSocketSession(id, socket, Inbound.Writer, idleTimeout, logger, helloGate);
        _sessions[id] = session;
        logger.LogDebug("Conexión {Conn} abierta", id);
        try
        {
            await session.RunAsync(serverStopping);
        }
        finally
        {
            _sessions.TryRemove(id, out _);
            logger.LogDebug("Conexión {Conn} cerrada ({Reason})", id, session.CloseReason);
        }
    }

    /// <summary>Envía a una conexión concreta (lo llama el tick; la serialización ocurre aquí, el envío en la tarea de la sesión).</summary>
    public void Send(int connectionId, IServerMessage msg)
    {
        if (_sessions.TryGetValue(connectionId, out var s)) s.Send(msg);
    }

    public void Close(int connectionId, string reason)
    {
        if (_sessions.TryGetValue(connectionId, out var s)) s.Close(reason);
    }
}
