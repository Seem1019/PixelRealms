using System.Net.WebSockets;
using System.Threading.Channels;
using PixelRealms.Protocol;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Hosting;

namespace PixelRealms.Server.Net;

/// <summary>
/// Una conexión WebSocket (HU-006): bucle de lectura que acumula frames hasta EndOfMessage (máx. 4 KB), decodifica el sobre y
/// encola la intención hacia el tick; tarea de escritura que vacía su propio canal (bounded 256: cliente lento → desconectar);
/// 3 mensajes inválidos → cierre; sin tráfico en `idleTimeout` → cierre.
/// </summary>
public sealed class WebSocketSession : IDisposable
{
    public const int OutboundCapacity = 256;
    public const int MaxInvalidMessages = 3;

    private readonly WebSocket _socket;
    private readonly ChannelWriter<InboundMessage> _inbound;
    private readonly Channel<ReadOnlyMemory<byte>> _outbound = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(OutboundCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private readonly TimeSpan _idleTimeout;
    private readonly TaskCompletionSource _closeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IHelloGate? _helloGate;
    private int _invalidCount;

    /// <summary>Ya envió un Hello aceptado (PlayerJoin encolado).</summary>
    public bool Joined { get; private set; }
    private long _lastTrafficTicks;

    private readonly MessageRateLimiter _rateLimiter;

    public string RemoteIp { get; init; } = "";

    public WebSocketSession(int id, WebSocket socket, ChannelWriter<InboundMessage> inbound, TimeSpan idleTimeout, ILogger logger, IHelloGate? helloGate = null, RateLimitOptions? rateLimits = null)
    {
        _rateLimiter = new MessageRateLimiter(rateLimits ?? new RateLimitOptions());
        _helloGate = helloGate;
        Id = id;
        _socket = socket;
        _inbound = inbound;
        _idleTimeout = idleTimeout;
        _logger = logger;
        _lastTrafficTicks = Environment.TickCount64;
    }

    public int Id { get; }

    /// <summary>Id de personaje asociado tras Hello (HU-014); null hasta entonces.</summary>
    public Guid? CharacterId { get; set; }

    public Guid? AccountId { get; set; }

    /// <summary>`accounts.is_admin` del dueño del ticket (HU-070); lo fija HelloGate.</summary>
    public bool IsAdmin { get; set; }

    public bool IsOpen => _socket.State == WebSocketState.Open;

    public string CloseReason { get; private set; } = "";

    /// <summary>Encola un mensaje ya serializado (lo llama el tick). Si el canal está lleno, el cliente es lento: se cierra.</summary>
    public void Enqueue(ReadOnlyMemory<byte> bytes)
    {
        if (!_outbound.Writer.TryWrite(bytes))
        {
            CloseReason = "slow_client";
            _closeSignal.TrySetResult();
        }
    }

    public void Send(IServerMessage msg) => Enqueue(MessageRegistry.Encode(msg));

    public void Close(string reason)
    {
        CloseReason = reason;
        _closeSignal.TrySetResult();
    }

    /// <summary>Ejecuta lectura, escritura y vigilancia de inactividad hasta que la conexión termina.</summary>
    public async Task RunAsync(CancellationToken serverStopping)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, serverStopping);
        var ct = linked.Token;
        _inbound.TryWrite(new InboundMessage(Id, "", default, InboundKind.Connected));
        var read = ReadLoopAsync(ct);
        var write = WriteLoopAsync(ct);
        var idle = IdleWatchAsync(ct);
        var closeRequested = CloseRequested(ct);
        var first = await Task.WhenAny(read, write, idle, closeRequested);
        if (first != read)
        {
            // Cierre iniciado por el servidor: enviamos el frame de cierre y dejamos que el bucle de lectura reciba la respuesta
            // (cancelar un ReceiveAsync abortaría el socket sin handshake).
            await CloseOutputAsync();
            await Task.WhenAny(read, Task.Delay(2000, CancellationToken.None));
        }
        await _cts.CancelAsync();
        try { await Task.WhenAll(read, write, idle); } catch (OperationCanceledException) { }
        _inbound.TryWrite(new InboundMessage(Id, "", default, InboundKind.Disconnected, CloseReason));
        if (_socket.State == WebSocketState.CloseReceived)
        {
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, CloseReason, closeCts.Token); } catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private async Task CloseRequested(CancellationToken ct)
    {
        await using var reg = ct.Register(() => _closeSignal.TrySetResult());
        await _closeSignal.Task;
    }

    private async Task CloseOutputAsync()
    {
        if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, CloseReason, closeCts.Token); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[MessageRegistry.MaxInboundBytes + 1];
        try
        {
            while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var total = 0;
                var tooLarge = false;
                ValueWebSocketReceiveResult result;
                do
                {
                    var slice = total < buffer.Length ? buffer.AsMemory(total) : buffer.AsMemory(buffer.Length - 1, 1);
                    result = await _socket.ReceiveAsync(slice, ct);
                    if (result.MessageType == WebSocketMessageType.Close) { if (CloseReason.Length == 0) CloseReason = "client_close"; return; }
                    total += result.Count;
                    if (total > MessageRegistry.MaxInboundBytes) { tooLarge = true; total = MessageRegistry.MaxInboundBytes; }
                }
                while (!result.EndOfMessage);

                _lastTrafficTicks = Environment.TickCount64;
                if (tooLarge || result.MessageType != WebSocketMessageType.Text) { if (await RejectAsync(ct)) return; continue; }

                var decoded = MessageRegistry.Decode(buffer.AsSpan(0, total));
                if (decoded.Status != MessageRegistry.DecodeStatus.Ok) { if (await RejectAsync(ct)) return; continue; }

                if (decoded.Message is Hello hello)
                {
                    // HU-014: versión, ticket y lectura de BD fuera del tick; después se encola PlayerJoin con el personaje cargado.
                    if (Joined || _helloGate is null) { if (await RejectAsync(ct)) return; continue; }
                    var gate = await _helloGate.ProcessAsync(hello, this, ct);
                    if (gate.ErrorCode is not null)
                    {
                        await _outbound.Writer.WriteAsync(MessageRegistry.Encode(new Error(gate.ErrorCode, null, null)), ct);
                        CloseReason = gate.ErrorCode;
                        await Task.Delay(20, ct);
                        await CloseOutputAsync();
                        continue;
                    }
                    Joined = true;
                    _inbound.TryWrite(new InboundMessage(Id, "Hello", default, InboundKind.PlayerJoin, gate.JoinAttachment));
                    continue;
                }

                // HU-071: token bucket por tipo; 3 excesos en 10 s → desconexión con rate_limited (log con IP y cuenta).
                switch (_rateLimiter.Check(decoded.Type!, Environment.TickCount64))
                {
                    case RateDecision.Limited:
                        await _outbound.Writer.WriteAsync(MessageRegistry.Encode(new Error(ErrorCodes.RateLimited, null, null)), ct);
                        continue;
                    case RateDecision.Disconnect:
                        _logger.LogWarning("Conexión {Conn} desconectada por rate limit (IP {Ip}, cuenta {Account})", Id, RemoteIp, AccountId);
                        await _outbound.Writer.WriteAsync(MessageRegistry.Encode(new Error(ErrorCodes.RateLimited, null, null)), ct);
                        CloseReason = ErrorCodes.RateLimited;
                        await Task.Delay(20, ct);
                        await CloseOutputAsync();
                        continue;
                    default:
                        break;
                }

                // El payload viaja como JsonElement clonado: el tick lo deserializa al tipo concreto (MessageRouter).
                using var doc = System.Text.Json.JsonDocument.Parse(buffer.AsMemory(0, total));
                var d = doc.RootElement.TryGetProperty("d", out var dEl) ? dEl.Clone() : default;
                if (!_inbound.TryWrite(new InboundMessage(Id, decoded.Type!, d)))
                    _logger.LogWarning("Cola de entrada llena: se descarta {Type} de la conexión {Conn}", decoded.Type, Id);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) { CloseReason = "socket_error"; _logger.LogDebug(ex, "Conexión {Conn} cerrada por error de socket", Id); }
    }

    /// <summary>Error{invalid_payload}; a la 3.ª vez se cierra (devuelve true si hay que terminar).</summary>
    private async Task<bool> RejectAsync(CancellationToken ct)
    {
        _invalidCount++;
        await _outbound.Writer.WriteAsync(MessageRegistry.Encode(new Error(ErrorCodes.InvalidPayload, null, null)), ct);
        if (_invalidCount < MaxInvalidMessages) return false;
        CloseReason = "invalid_payload";
        await Task.Delay(20, ct); // deja salir el último Error antes del frame de cierre
        await CloseOutputAsync();
        return false; // el bucle de lectura sigue hasta recibir el Close del cliente
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var bytes in _outbound.Reader.ReadAllAsync(ct))
            {
                if (_socket.State != WebSocketState.Open) return;
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { CloseReason = "socket_error"; }
    }

    private async Task IdleWatchAsync(CancellationToken ct)
    {
        var step = TimeSpan.FromMilliseconds(Math.Max(100, _idleTimeout.TotalMilliseconds / 4));
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(step, ct);
            if (Environment.TickCount64 - _lastTrafficTicks > _idleTimeout.TotalMilliseconds)
            {
                CloseReason = "idle_timeout";
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
    }
}
