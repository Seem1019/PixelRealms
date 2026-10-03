using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PixelRealms.Tools.LoadBot;

/// <summary>
/// HU-089 CA1 (parte de red) y HU-023 CA4: `bots` clientes reales contra un servidor en marcha. Cada bot registra su cuenta,
/// crea un personaje, entra por WebSocket con ticket y juega como un cliente: un MoveInput por tick mientras camina (cambia de
/// dirección cada 1–2 s), Ping cada segundo y básico contra los monstruos que ve. Mide los bytes recibidos por cliente (p95 en
/// KB/s) y el tick p99 del servidor (`/health`). El servidor de prueba necesita límites por IP altos (30 cuentas y conexiones
/// desde una IP): `--Auth:RegisterPerHour=100 --Auth:LoginPerMinute=100 --Net:RateLimits:MaxConnectionsPerIp=100`.
/// </summary>
public static class NetworkScenario
{
    /// <summary>Umbrales de HU-023 CA4.</summary>
    public const double ClientKbPerSecP95Limit = 30, ServerTickP99LimitMs = 10;

    public static async Task<bool> RunAsync(LoadOptions o, TextWriter log)
    {
        var baseUrl = o.Network!.TrimEnd('/');
        var wsUrl = (baseUrl.StartsWith("https://", StringComparison.Ordinal) ? "wss://" + baseUrl["https://".Length..] : "ws://" + baseUrl["http://".Length..]) + "/ws";
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var bots = new List<Bot>();
        for (var i = 0; i < o.Bots; i++) bots.Add(await Bot.CreateAsync(http, wsUrl, i));
        log.WriteLine($"{bots.Count} bots dentro; jugando {o.DurationSec} s…");

        var wall = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(o.DurationSec));
        await Task.WhenAll(bots.Select((b, i) => b.PlayAsync(o.Seed + i, cts.Token)));
        var seconds = wall.Elapsed.TotalSeconds;

        var kbps = bots.Select(b => b.BytesReceived / 1024.0 / seconds).OrderBy(x => x).ToList();
        var p95 = kbps[Math.Max(0, (int)Math.Ceiling(kbps.Count * 0.95) - 1)];
        foreach (var b in bots) b.Dispose();
        var health = await http.GetFromJsonAsync<JsonElement>("/health");
        var tickP99 = health.GetProperty("tickP99Ms").GetDouble();
        var ci = CultureInfo.InvariantCulture;
        var passed = p95 < ClientKbPerSecP95Limit && tickP99 < ServerTickP99LimitMs && bots.All(b => b.Error is null);
        log.WriteLine($"recibido por cliente: media {kbps.Average().ToString("F1", ci)} KB/s · p95 {p95.ToString("F1", ci)} KB/s (< {ClientKbPerSecP95Limit}) · máx {kbps[^1].ToString("F1", ci)} KB/s");
        log.WriteLine($"servidor: tick p99 {tickP99.ToString("F2", ci)} ms (< {ServerTickP99LimitMs}) · jugadores {health.GetProperty("players").GetInt32()}");
        log.WriteLine($"mensajes enviados por bot: media {bots.Average(b => b.MessagesSent).ToString("F0", ci)} · errores del servidor recibidos: {bots.Sum(b => b.ServerErrors)}");
        foreach (var b in bots.Where(b => b.Error is not null)) log.WriteLine($"  {b.Name}: {b.Error}");
        log.WriteLine(passed ? "RESULTADO: OK" : "RESULTADO: FALLA (ver umbrales)");
        return passed;
    }

    private sealed class Bot : IDisposable
    {
        private static readonly string[] Classes = ["warrior", "rogue", "mage", "priest"];
        private readonly ClientWebSocket _ws = new();
        private readonly HashSet<int> _monsters = new();
        private readonly object _gate = new();

        public required string Name { get; init; }

        public long BytesReceived;
        public int MessagesSent;
        public int ServerErrors;
        public string? Error;

        public static async Task<Bot> CreateAsync(HttpClient http, string wsUrl, int index)
        {
            var user = $"loadbot{index}";
            var password = $"loadbot-pass-{index}";
            var name = "Bot" + Letters(index);
            await http.PostAsJsonAsync("/api/auth/register", new { username = user, password }); // 201 o 409 si ya existía
            var login = await http.PostAsJsonAsync("/api/auth/login", new { username = user, password });
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/characters") { Content = JsonContent.Create(new { name, classId = Classes[index % Classes.Length] }) };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var created = await http.SendAsync(req);
            Guid characterId;
            if (created.IsSuccessStatusCode) characterId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            else
            {
                using var list = new HttpRequestMessage(HttpMethod.Get, "/api/characters");
                list.Headers.Authorization = req.Headers.Authorization;
                var chars = await (await http.SendAsync(list)).Content.ReadFromJsonAsync<JsonElement>();
                characterId = chars.EnumerateArray().First(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
            }
            using var ticketReq = new HttpRequestMessage(HttpMethod.Post, "/api/game/ticket") { Content = JsonContent.Create(new { characterId }) };
            ticketReq.Headers.Authorization = req.Headers.Authorization;
            var ticket = (await (await http.SendAsync(ticketReq)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
            var bot = new Bot { Name = name };
            await bot._ws.ConnectAsync(new Uri($"{wsUrl}?ticket={Uri.EscapeDataString(ticket)}"), CancellationToken.None);
            await bot.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""", CancellationToken.None);
            return bot;
        }

        /// <summary>Nombre válido (empieza por letra, sin guiones): 0 → "aa", 1 → "ab"…</summary>
        private static string Letters(int i) => $"{(char)('a' + i / 26 % 26)}{(char)('a' + i % 26)}";

        public async Task PlayAsync(int seed, CancellationToken ct)
        {
            var rng = new Random(seed);
            var receive = ReceiveLoopAsync(ct);
            try
            {
                var seq = 0;
                var (dx, dy) = (0, 0);
                var nextTurn = 0L; var nextPing = 0L; var nextAttack = 2000L;
                var clock = Stopwatch.StartNew();
                while (!ct.IsCancellationRequested)
                {
                    var now = clock.ElapsedMilliseconds;
                    if (now >= nextTurn)
                    {
                        (dx, dy) = (rng.Next(-1, 2), rng.Next(-1, 2));
                        nextTurn = now + rng.Next(1000, 2001);
                        if (dx == 0 && dy == 0) await SendAsync("MoveInput", $$"""{"seq":{{++seq}},"dx":0,"dy":0}""", ct);
                    }
                    if (dx != 0 || dy != 0) await SendAsync("MoveInput", $$"""{"seq":{{++seq}},"dx":{{dx}},"dy":{{dy}}}""", ct);
                    if (now >= nextPing) { await SendAsync("Ping", """{"clientTime":0}""", ct); nextPing = now + 1000; }
                    if (now >= nextAttack)
                    {
                        int target;
                        lock (_gate) target = _monsters.Count == 0 ? 0 : _monsters.ElementAt(rng.Next(_monsters.Count));
                        if (target > 0)
                        {
                            await SendAsync("SelectTarget", $$"""{"targetId":{{target}}}""", ct);
                            await SendAsync("AutoAttack", """{"on":true}""", ct);
                        }
                        nextAttack = now + 3000;
                    }
                    await Task.Delay(50, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { Error = ex.Message; }
            try { await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (WebSocketException) { }
            try { await receive; } catch (Exception) when (Error is not null || ct.IsCancellationRequested) { }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (_ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult r;
                try { r = await _ws.ReceiveAsync(buffer, CancellationToken.None); }
                catch (WebSocketException) { return; }
                if (r.MessageType == WebSocketMessageType.Close) return;
                Interlocked.Add(ref BytesReceived, r.Count);
                message.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;
                Inspect(message.GetBuffer().AsSpan(0, (int)message.Length));
                message.SetLength(0);
                if (ct.IsCancellationRequested && _ws.State != WebSocketState.Open) return;
            }
        }

        /// <summary>Lleva la cuenta de los monstruos a la vista (para atacar) y de los errores del servidor.</summary>
        private void Inspect(ReadOnlySpan<byte> json)
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            var t = doc.RootElement.GetProperty("t").GetString();
            var d = doc.RootElement.GetProperty("d");
            switch (t)
            {
                case "EntitySpawn" when d.GetProperty("kind").GetString() == "monster":
                    lock (_gate) _monsters.Add(d.GetProperty("id").GetInt32());
                    break;
                case "EntityDespawn":
                    lock (_gate) _monsters.Remove(d.GetProperty("id").GetInt32());
                    break;
                case "Error":
                    Interlocked.Increment(ref ServerErrors);
                    break;
            }
        }

        public void Dispose() => _ws.Dispose();

        private async Task SendAsync(string type, string payload, CancellationToken ct)
        {
            await _ws.SendAsync(Encoding.UTF8.GetBytes($$"""{"t":"{{type}}","d":{{payload}}}"""), WebSocketMessageType.Text, true, ct);
            MessagesSent++;
        }
    }
}
