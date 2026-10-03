using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Hosting;

/// <summary>HU-072: `/health` con uptime, `/admin/stats` solo para admins, contadores de red y tiempos de combate por instancia.</summary>
public sealed class MetricsTests
{
    [Fact]
    public void NetMetrics_Sample_ComputesRates()
    {
        var m = new NetMetrics();
        m.Sample(1000);
        for (var i = 0; i < 10; i++) m.RecordIn(100);
        for (var i = 0; i < 4; i++) m.RecordOut(250);
        m.Sample(3000); // 2 s
        m.MessagesInPerSec.ShouldBe(5, 0.01);
        m.BytesInPerSec.ShouldBe(500, 0.01);
        m.MessagesOutPerSec.ShouldBe(2, 0.01);
        m.BytesOutPerSec.ShouldBe(500, 0.01);
        m.MessagesIn.ShouldBe(10);
        m.BytesOut.ShouldBe(1000);
        m.AllocBytesPerSec.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Health_IncludesUptime() // CA1
    {
        await using var server = await TestServer.StartAsync();
        using var http = new HttpClient();
        var health = await http.GetFromJsonAsync<JsonElement>(server.BaseUrl + "/health", cancellationToken: TestContext.Current.CancellationToken);
        health.GetProperty("status").GetString().ShouldBe("ok");
        health.GetProperty("players").GetInt32().ShouldBe(0);
        health.GetProperty("tickP99Ms").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        health.GetProperty("uptime").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AdminStats_RequiresAdminJwt_AndReportsCounters() // CA3
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin("ana");
        using var anon = new HttpClient();
        (await anon.GetAsync(server.BaseUrl + "/admin/stats", cancellationToken: TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.Http.GetAsync("/admin/stats", cancellationToken: TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // JWT válido pero sin admin

        var accounts = server.Services.GetRequiredService<IAccountRepository>();
        await accounts.SetAdminAsync((await accounts.FindByUsernameAsync("ana", TestContext.Current.CancellationToken))!.Id, true, TestContext.Current.CancellationToken);
        await api.Login("ana", "segura123");
        var id = await api.CreateCharacterId("Ana", "warrior");
        await using var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        await client.ExpectAsync("Welcome");
        for (var i = 0; i < 5; i++) await client.SendRawAsync("""{"t":"Ping","d":{"clientTime":1}}""");
        await client.ExpectAsync("Pong");
        // Espera a una muestra de métricas (1 s) con tráfico de snapshots dentro (bajo carga puede tardar más de un ciclo).
        JsonElement stats = default;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            stats = await api.Http.GetFromJsonAsync<JsonElement>("/admin/stats", cancellationToken: TestContext.Current.CancellationToken);
            // Los recuentos por instancia los publica el tick una vez por segundo (WorldStats).
            if (stats.GetProperty("messagesOutPerSec").GetDouble() > 0
                && stats.GetProperty("instances").EnumerateArray().Any(i => i.GetProperty("mapId").GetString() == "meadow" && i.GetProperty("players").GetInt32() == 1)) break;
        }
        stats.GetProperty("players").GetInt32().ShouldBe(1);
        stats.GetProperty("connections").GetInt32().ShouldBe(1);
        stats.GetProperty("monsters").GetInt32().ShouldBeGreaterThan(0);
        stats.GetProperty("uptime").GetDouble().ShouldBeGreaterThan(0);
        stats.GetProperty("tickP99Ms").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        stats.GetProperty("messagesOutPerSec").GetDouble().ShouldBeGreaterThan(0); // snapshots a 10 Hz
        stats.GetProperty("bytesOutPerSec").GetDouble().ShouldBeGreaterThan(0);
        stats.GetProperty("allocBytesPerSec").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        var instances = stats.GetProperty("instances").EnumerateArray().ToList();
        instances.Count.ShouldBeGreaterThanOrEqualTo(2);
        var meadow = instances.Single(i => i.GetProperty("mapId").GetString() == "meadow");
        meadow.GetProperty("players").GetInt32().ShouldBe(1);
        meadow.GetProperty("monsters").GetInt32().ShouldBeGreaterThan(0);
        meadow.GetProperty("combatP99Ms").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        meadow.GetProperty("areasActive").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        meadow.GetProperty("aurasActive").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }
}
