using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace PixelRealms.Server.Tests.Helpers;

/// <summary>
/// Cliente WebSocket de prueba reutilizable: una tarea lee todo lo que llega a una cola; los tests esperan mensajes por tipo
/// con timeout sin cancelar el socket (cancelar un ReceiveAsync abortaría la conexión).
/// </summary>
public sealed class TestGameClient : IAsyncDisposable
{
    private const string CloseType = "__close__";
    private readonly ClientWebSocket _ws = new();
    private readonly Channel<(string Type, JsonElement Payload, string? Close)> _inbox = Channel.CreateUnbounded<(string, JsonElement, string?)>();
    private readonly List<(string Type, JsonElement Payload)> _buffer = new();
    private Task? _reader;

    public static async Task<TestGameClient> ConnectAsync(string wsUrl)
    {
        var c = new TestGameClient();
        await c._ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        c._reader = Task.Run(c.ReadLoopAsync);
        return c;
    }

    public WebSocketState State => _ws.State;

    public Task SendRawAsync(string json) => _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    public Task SendAsync(string type, string payloadJson = "{}") => SendRawAsync($"{{\"t\":\"{type}\",\"d\":{payloadJson}}}");

    /// <summary>Espera el siguiente mensaje de ese tipo (los demás quedan en un buffer) o lanza por timeout.</summary>
    public async Task<JsonElement> ExpectAsync(string type, int timeoutMs = 3000)
    {
        var idx = _buffer.FindIndex(m => m.Type == type);
        if (idx >= 0) { var m = _buffer[idx]; _buffer.RemoveAt(idx); return m.Payload; }
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"sin {type} en {timeoutMs} ms (buffer: {string.Join(",", _buffer.Select(b => b.Type))})");
            using var cts = new CancellationTokenSource(remaining);
            (string Type, JsonElement Payload, string? Close) item;
            try { item = await _inbox.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { throw new TimeoutException($"sin {type} en {timeoutMs} ms (buffer: {string.Join(",", _buffer.Select(b => b.Type))})"); }
            if (item.Type == CloseType) throw new WebSocketException($"cerrado por el servidor ({item.Close}) esperando {type}");
            if (item.Type == type) return item.Payload;
            _buffer.Add((item.Type, item.Payload));
        }
    }

    /// <summary>Vacía el buffer y lee durante `windowMs`; devuelve el último mensaje de ese tipo (útil para Snapshot).</summary>
    public async Task<JsonElement> LatestAsync(string type, int windowMs = 250)
    {
        JsonElement? last = null;
        foreach (var m in _buffer) if (m.Type == type) last = m.Payload;
        _buffer.RemoveAll(m => m.Type == type);
        var deadline = DateTime.UtcNow.AddMilliseconds(windowMs);
        while (DateTime.UtcNow < deadline)
        {
            try { last = await ExpectAsync(type, (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)); }
            catch (TimeoutException) { break; }
        }
        return last ?? throw new TimeoutException($"ningún {type} en {windowMs} ms");
    }

    /// <summary>Espera a que el servidor cierre; devuelve la descripción del cierre.</summary>
    public async Task<string?> ExpectCloseAsync(int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"el servidor no cerró en {timeoutMs} ms");
            using var cts = new CancellationTokenSource(remaining);
            (string Type, JsonElement Payload, string? Close) item;
            try { item = await _inbox.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { throw new TimeoutException($"el servidor no cerró en {timeoutMs} ms"); }
            if (item.Type == CloseType) return item.Close;
            _buffer.Add((item.Type, item.Payload));
        }
    }

    private async Task ReadLoopAsync()
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (_ws.State == WebSocketState.Open)
            {
                var total = 0;
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf, total, buf.Length - total), CancellationToken.None);
                    total += r.Count;
                }
                while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    _inbox.Writer.TryWrite((CloseType, default, _ws.CloseStatusDescription));
                    try { await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ok", CancellationToken.None); } catch (WebSocketException) { }
                    return;
                }
                using var doc = JsonDocument.Parse(buf.AsMemory(0, total));
                var t = doc.RootElement.GetProperty("t").GetString()!;
                var d = doc.RootElement.TryGetProperty("d", out var dEl) ? dEl.Clone() : default;
                _inbox.Writer.TryWrite((t, d, null));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            _inbox.Writer.TryWrite((CloseType, default, "socket_error"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
        {
            try { using var cts = new CancellationTokenSource(1000); await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token); } catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        }
        if (_reader is not null) await Task.WhenAny(_reader, Task.Delay(500));
        _ws.Dispose();
    }
}
