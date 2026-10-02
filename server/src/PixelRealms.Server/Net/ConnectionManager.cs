using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;

namespace PixelRealms.Server.Net;

/// <summary>Sesiones vivas por id de conexión. El tick la consulta para enviar; las sesiones se registran desde el endpoint /ws.</summary>
public sealed class ConnectionManager(ILogger<ConnectionManager> logger, IHelloGate helloGate, Microsoft.Extensions.Options.IOptions<NetOptions> netOptions, NetMetrics metrics)
{
    private readonly ConcurrentDictionary<int, WebSocketSession> _sessions = new();
    private readonly ConcurrentDictionary<string, int> _perIp = new();
    private int _nextId;

    /// <summary>HU-071 CA3: reserva una conexión para la IP; false si ya tiene `MaxConnectionsPerIp`.</summary>
    public bool TryReserveIp(string ip)
    {
        var max = netOptions.Value.RateLimits.MaxConnectionsPerIp;
        while (true)
        {
            var current = _perIp.GetValueOrDefault(ip);
            if (current >= max) return false;
            if (_perIp.TryGetValue(ip, out var existing) ? _perIp.TryUpdate(ip, current + 1, existing) : _perIp.TryAdd(ip, 1)) return true;
        }
    }

    private void ReleaseIp(string ip)
    {
        if (_perIp.AddOrUpdate(ip, 0, (_, v) => Math.Max(0, v - 1)) == 0) _perIp.TryRemove(ip, out _);
    }

    public Channel<InboundMessage> Inbound { get; } = InboundChannel.Create();

    public int Count => _sessions.Count;

    public IEnumerable<WebSocketSession> Sessions => _sessions.Values;

    public WebSocketSession? Get(int connectionId) => _sessions.GetValueOrDefault(connectionId);

    public async Task HandleAsync(WebSocket socket, TimeSpan idleTimeout, CancellationToken serverStopping, string remoteIp = "")
    {
        var id = Interlocked.Increment(ref _nextId);
        using var session = new WebSocketSession(id, socket, Inbound.Writer, idleTimeout, logger, helloGate, netOptions.Value.RateLimits) { RemoteIp = remoteIp, Metrics = metrics };
        _sessions[id] = session;
        logger.LogDebug("Conexión {Conn} abierta desde {Ip}", id, remoteIp);
        try
        {
            await session.RunAsync(serverStopping);
        }
        finally
        {
            _sessions.TryRemove(id, out _);
            if (remoteIp.Length > 0) ReleaseIp(remoteIp);
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

    /// <summary>Cierra la conexión después de enviar lo ya encolado.</summary>
    public void CloseAfterFlush(int connectionId, string reason)
    {
        if (_sessions.TryGetValue(connectionId, out var s)) s.CloseAfterFlush(reason);
    }
}
