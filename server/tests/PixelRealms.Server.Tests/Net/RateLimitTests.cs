using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using PixelRealms.Server.Net;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-071: token bucket por conexión y tipo, desconexión por excesos, tope por IP y fuzzing.</summary>
[Collection(nameof(TickTimingIsolation))]
public sealed class RateLimitTests
{
    [Fact]
    public void Bucket_AllowsBurst_ThenLimits_AndRefills() // CA1
    {
        var limiter = new MessageRateLimiter(new RateLimitOptions { MoveInputPerSec = 30, MoveInputBurst = 30 });
        for (var i = 0; i < 30; i++) limiter.Check("MoveInput", 1000).ShouldBe(RateDecision.Allowed);
        limiter.Check("MoveInput", 1000).ShouldBe(RateDecision.Limited);
        // 100 ms después hay 3 fichas nuevas (30/s).
        for (var i = 0; i < 3; i++) limiter.Check("MoveInput", 1100).ShouldBe(RateDecision.Allowed);
        limiter.Check("MoveInput", 1100).ShouldBe(RateDecision.Limited);
    }

    [Fact]
    public void MoveInputs_DeliveredInOneBurstAfterANetworkStall_AreNotLimited()
    {
        var limiter = new MessageRateLimiter(new RateLimitOptions());
        // El cliente manda un MoveInput por tick (20/s) mientras camina...
        for (var t = 0; t < 5000; t += 50) limiter.Check("MoveInput", t).ShouldBe(RateDecision.Allowed);
        // ...y un corte de 2 s que TCP entrega de golpe: 40 inputs en el mismo instante no deben desconectar.
        for (var i = 0; i < 40; i++) limiter.Check("MoveInput", 7000).ShouldBe(RateDecision.Allowed);
    }

    [Fact]
    public void Buckets_AreIndependentPerType() // CA1: límites distintos por tipo (chat 5/5 s, resto 20/s)
    {
        var limiter = new MessageRateLimiter(new RateLimitOptions());
        for (var i = 0; i < 5; i++) limiter.Check("ChatSend", 0).ShouldBe(RateDecision.Allowed);
        limiter.Check("ChatSend", 0).ShouldBe(RateDecision.Limited);
        for (var i = 0; i < 20; i++) limiter.Check("Ping", 0).ShouldBe(RateDecision.Allowed); // el chat no agota al resto
        limiter.Check("Ping", 0).ShouldBe(RateDecision.Limited);
        for (var i = 0; i < 10; i++) limiter.Check("CastSpell", 0).ShouldBe(RateDecision.Allowed);
        limiter.Check("CastSpell", 0).ShouldBe(RateDecision.Disconnect); // los excesos se cuentan por conexión, no por tipo: 3.º → fuera
    }

    [Fact]
    public void ThreeExcessesInTenSeconds_Disconnect_ButOldOnesExpire() // CA2
    {
        var limiter = new MessageRateLimiter(new RateLimitOptions { ChatPerSec = 1, ChatBurst = 1 });
        limiter.Check("ChatSend", 0).ShouldBe(RateDecision.Allowed);
        limiter.Check("ChatSend", 0).ShouldBe(RateDecision.Limited);   // exceso 1 (t=0)
        limiter.Check("ChatSend", 0).ShouldBe(RateDecision.Limited);   // exceso 2
        limiter.Check("ChatSend", 11_000).ShouldBe(RateDecision.Allowed); // refill
        limiter.Check("ChatSend", 11_000).ShouldBe(RateDecision.Limited); // los dos primeros ya caducaron → cuenta 1
        limiter.ExcessCount.ShouldBe(1);
        limiter.Check("ChatSend", 11_500).ShouldBe(RateDecision.Limited);    // 2 (aún sin ficha: 1/s)
        limiter.Check("ChatSend", 11_900).ShouldBe(RateDecision.Disconnect); // 3 en 10 s
    }

    [Fact]
    public async Task Flood_IsCutWithRateLimited_AndClosed() // CA2 (integración): Error{rate_limited} y cierre
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestGameClient.ConnectAsync(server.WsUrl);
        for (var i = 0; i < 40; i++)
        {
            if (client.State != WebSocketState.Open) break;
            try { await client.SendRawAsync("""{"t":"Ping","d":{"clientTime":1}}"""); }
            catch (WebSocketException) { break; }
        }
        (await client.ExpectAsync("Error", e => e.GetProperty("code").GetString() == "rate_limited")).GetProperty("code").GetString().ShouldBe("rate_limited");
        (await client.ExpectCloseAsync(4000)).ShouldBe("rate_limited");
    }

    [Fact]
    public async Task EleventhConnectionFromSameIp_IsRejected() // CA3
    {
        await using var server = await TestServer.StartAsync();
        var clients = new List<TestGameClient>();
        try
        {
            for (var i = 0; i < 10; i++) clients.Add(await TestGameClient.ConnectAsync(server.WsUrl));
            var ex = await Should.ThrowAsync<WebSocketException>(() => TestGameClient.ConnectAsync(server.WsUrl));
            ex.Message.ShouldContain("429");
            // Al cerrar una, vuelve a haber hueco.
            await clients[0].DisposeAsync();
            clients.RemoveAt(0);
            await WaitUntilAsync(() => server.Services.GetService(typeof(ConnectionManager)) is ConnectionManager cm && cm.Count == 9, 3000);
            clients.Add(await TestGameClient.ConnectAsync(server.WsUrl));
        }
        finally
        {
            foreach (var c in clients) await c.DisposeAsync();
        }
    }

    [Fact]
    public async Task Fuzz_ThousandMalformedMessages_ServerSurvives_TickUnder50Ms() // CA4
    {
        await using var server = await TestServer.StartAsync();
        var rng = new Random(71);
        var types = new[] { "Ping", "Hello", "EnterWorld", "MoveInput", "CastSpell", "SelectTarget", "InventoryMove", "ChatSend", "LootOpen", "VendorBuy", "UsePortal", "PartyInvite", "DuelRequest", "TradeOffer", "SetHotbar", "Respawn", "Nope", "", "Error", "Snapshot" };
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        var reconnects = 0;
        try
        {
            for (var i = 0; i < 1000; i++)
            {
                if (client.State != WebSocketState.Open)
                {
                    await client.DisposeAsync();
                    client = await TestGameClient.ConnectAsync(server.WsUrl);
                    reconnects++;
                    if (i % 50 == 0) await Task.Delay(60, TestContext.Current.CancellationToken); // deja respirar a los buckets de vez en cuando
                }
                var msg = RandomMessage(rng, types);
                try { await client.SendRawAsync(msg); }
                catch (WebSocketException ex) { _ = ex; /* cerrada por el servidor: la siguiente iteración reconecta */ }
                catch (InvalidOperationException ex) { _ = ex; }
                if (i % 100 == 99) await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
        finally { await client.DisposeAsync(); }

        // El servidor sigue vivo y el tick no se ha disparado.
        using var http = new HttpClient();
        var health = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(server.BaseUrl + "/health", cancellationToken: TestContext.Current.CancellationToken);
        health.GetProperty("status").GetString().ShouldBe("ok");
        health.GetProperty("tickP99Ms").GetDouble().ShouldBeLessThan(50);
        await using var fresh = await TestGameClient.ConnectAsync(server.WsUrl);
        await fresh.SendRawAsync("""{"t":"Ping","d":{"clientTime":7}}""");
        (await fresh.ExpectAsync("Pong")).GetProperty("clientTime").GetInt64().ShouldBe(7);
        reconnects.ShouldBeGreaterThan(0); // el fuzz realmente provocó cierres (payloads inválidos / rate limit)
    }

    private static string RandomMessage(Random rng, string[] types)
    {
        switch (rng.Next(8))
        {
            case 0: return RandomBytes(rng, rng.Next(1, 300));
            case 1: return "{\"t\":" + rng.Next() + "}";
            case 2: return "[1,2,3]";
            case 3: return "{\"t\":\"" + types[rng.Next(types.Length)] + "\"}";
            case 4: return "{\"t\":\"" + types[rng.Next(types.Length)] + "\",\"d\":" + RandomJson(rng, 0) + "}";
            case 5: return "{\"t\":\"" + types[rng.Next(types.Length)] + "\",\"d\":{\"" + RandomBytes(rng, 5) + "\":" + RandomJson(rng, 0) + "}}";
            case 6: return new string('{', rng.Next(1, 200));
            default: return "{\"t\":\"" + types[rng.Next(types.Length)] + "\",\"d\":{\"dx\":" + RandomNumber(rng) + ",\"dy\":" + RandomNumber(rng) + ",\"id\":" + RandomNumber(rng) + ",\"spellId\":\"" + RandomBytes(rng, 8) + "\",\"targetId\":" + RandomNumber(rng) + ",\"x\":" + RandomNumber(rng) + ",\"y\":" + RandomNumber(rng) + ",\"text\":\"" + RandomBytes(rng, 30) + "\",\"channel\":\"say\",\"from\":{\"c\":\"bag\",\"i\":" + rng.Next(-5, 40) + "},\"to\":{\"c\":\"equip\",\"i\":" + rng.Next(-5, 40) + "},\"qty\":" + RandomNumber(rng) + "}}";
        }
    }

    private static string RandomNumber(Random rng) => rng.Next(6) switch
    {
        0 => "0",
        1 => "-1",
        2 => int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
        3 => "1e308",
        4 => "\"abc\"",
        _ => rng.Next(-100, 1000).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string RandomJson(Random rng, int depth)
    {
        if (depth > 3) return "null";
        return rng.Next(6) switch
        {
            0 => "null",
            1 => "true",
            2 => RandomNumber(rng),
            3 => "\"" + RandomBytes(rng, rng.Next(0, 20)) + "\"",
            4 => "[" + RandomJson(rng, depth + 1) + "," + RandomJson(rng, depth + 1) + "]",
            _ => "{\"a\":" + RandomJson(rng, depth + 1) + ",\"d\":" + RandomJson(rng, depth + 1) + "}",
        };
    }

    private static string RandomBytes(Random rng, int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789 _-\\\"{}[]:,ñ漢字😀";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            var ch = alphabet[rng.Next(alphabet.Length)];
            if (ch == '"' || ch == '\\') { sb.Append(ch); continue; } // escapes rotos a propósito
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condición no cumplida");
            await Task.Delay(25);
        }
    }
}

/// <summary>Tests que miden el tick en tiempo real (p99 de `/health`, ticks por segundo): con otros servidores de test en paralelo la máquina compartida los dispara.</summary>
[CollectionDefinition(nameof(TickTimingIsolation), DisableParallelization = true)]
public sealed class TickTimingIsolation;
