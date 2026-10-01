using System.Net.Http.Json;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

public sealed class ProtocolBaseTests
{
    [Fact]
    public async Task Ping_ReturnsPong_WithServerTick() // HU-006 CA1
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendRawAsync("""{"t":"Ping","d":{"clientTime":123}}""");
        var pong = await client.ExpectAsync("Pong");
        pong.GetProperty("clientTime").GetInt64().ShouldBe(123);
        pong.GetProperty("serverTick").GetInt64().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task InvalidMessages_GetError_AndThirdCloses() // HU-006 CA2
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendRawAsync("""{"t":"Nope","d":{}}""");
        (await client.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload");
        await client.SendRawAsync("not json at all");
        (await client.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload");
        await client.SendRawAsync(new string('x', 5000));
        (await client.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload");
        (await client.ExpectCloseAsync()).ShouldBe("invalid_payload");
    }

    [Fact]
    public async Task IdleConnection_IsClosedByServer() // HU-006 CA5 (timeout reducido a 1 s para el test)
    {
        await using var server = await TestServer.StartAsync(new() { ["Net:IdleTimeoutSec"] = "1" });
        await using var client = await TestGameClient.ConnectAsync(server.WsUrl);
        (await client.ExpectCloseAsync(4000)).ShouldBe("idle_timeout");
    }

    [Fact]
    public async Task Health_ReportsPlayersAndTick()
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();
        var health = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(server.BaseUrl + "/health", cancellationToken: TestContext.Current.CancellationToken);
        health.GetProperty("status").GetString().ShouldBe("ok");
        health.GetProperty("players").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task WsWithoutTicket_IsRejected_WhenRequired()
    {
        await using var server = await TestServer.StartAsync(new() { ["Net:RequireTicket"] = "true" });
        var ex = await Should.ThrowAsync<System.Net.WebSockets.WebSocketException>(() => TestGameClient.ConnectAsync(server.WsUrl));
        ex.Message.ShouldContain("401");
    }
}
