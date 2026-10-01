using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PixelRealms.Server.Tests.Helpers;

/// <summary>Cliente WebSocket de prueba reutilizable: envía sobres y recibe mensajes por tipo con timeout.</summary>
public sealed class TestGameClient : IAsyncDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly List<(string Type, JsonElement Payload)> _buffer = new();

    public static async Task<TestGameClient> ConnectAsync(string wsUrl)
    {
        var c = new TestGameClient();
        await c._ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        return c;
    }

    public WebSocketState State => _ws.State;

    public Task SendRawAsync(string json) => _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    public Task SendAsync(string type, string payloadJson = "{}") => SendRawAsync($"{{\"t\":\"{type}\",\"d\":{payloadJson}}}");

    /// <summary>Espera el siguiente mensaje de ese tipo (descarta los demás en un buffer) o lanza por timeout.</summary>
    public async Task<JsonElement> ExpectAsync(string type, int timeoutMs = 3000)
    {
        var idx = _buffer.FindIndex(m => m.Type == type);
        if (idx >= 0) { var m = _buffer[idx]; _buffer.RemoveAt(idx); return m.Payload; }
        using var cts = new CancellationTokenSource(timeoutMs);
        var buf = new byte[64 * 1024];
        while (true)
        {
            var total = 0; WebSocketReceiveResult r;
            do { r = await _ws.ReceiveAsync(buf.AsMemory(total).Length > 0 ? new ArraySegment<byte>(buf, total, buf.Length - total) : throw new InvalidOperationException("buffer"), cts.Token); total += r.Count; }
            while (!r.EndOfMessage);
            if (r.MessageType == WebSocketMessageType.Close) throw new WebSocketException($"cerrado por el servidor ({_ws.CloseStatusDescription}) esperando {type}");
            using var doc = JsonDocument.Parse(buf.AsMemory(0, total));
            var t = doc.RootElement.GetProperty("t").GetString()!;
            var d = doc.RootElement.TryGetProperty("d", out var dEl) ? dEl.Clone() : default;
            if (t == type) return d;
            _buffer.Add((t, d));
        }
    }

    /// <summary>Espera a que el servidor cierre; devuelve la descripción del cierre.</summary>
    public async Task<string?> ExpectCloseAsync(int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var buf = new byte[4096];
        while (true)
        {
            var r = await _ws.ReceiveAsync(buf, cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) return _ws.CloseStatusDescription;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
        {
            try { using var cts = new CancellationTokenSource(1000); await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token); } catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        }
        _ws.Dispose();
    }
}
