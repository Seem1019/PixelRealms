using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-026: posición al volver, guardado al apagar y autosave de los `Dirty`.</summary>
public sealed class PersistenceFlowTests
{
    private static async Task<TestGameClient> Connect(TestServer server, ApiClient api, Guid characterId)
    {
        var ticket = await api.Ticket(characterId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
        return client;
    }

    [Fact]
    public async Task LeaveAndReturn_SamePositionHpAndResource() // CA1
    {
        await using var server = await TestServer.StartAsync();
        using var api = await new ApiClient(server).RegisterAndLogin("ana");
        var id = await api.CreateCharacterId("Ana", "warrior");
        var saver = server.Services.GetRequiredService<SaveService>();

        var ana = await Connect(server, api, id);
        var w1 = await ana.ExpectAsync("Welcome");
        var hp = w1.GetProperty("self").GetProperty("hp").GetInt32();
        var res = w1.GetProperty("self").GetProperty("res").GetInt32();
        await ana.SendAsync("MoveInput", """{"seq":1,"dx":1,"dy":1}""");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await ana.SendAsync("MoveInput", """{"seq":2,"dx":0,"dy":0}""");
        await Task.Delay(150, TestContext.Current.CancellationToken);
        var snap = await ana.LatestAsync("Snapshot");
        var x = snap.GetProperty("self").GetProperty("x").GetSingle();
        var y = snap.GetProperty("self").GetProperty("y").GetSingle();
        await ana.DisposeAsync();
        await WaitUntil(() => saver.Saved >= 1);

        await using var ana2 = await Connect(server, api, id);
        var w2 = await ana2.ExpectAsync("Welcome");
        w2.GetProperty("self").GetProperty("x").GetSingle().ShouldBe(x, 0.01f);
        w2.GetProperty("self").GetProperty("y").GetSingle().ShouldBe(y, 0.01f);
        w2.GetProperty("self").GetProperty("hp").GetInt32().ShouldBe(hp);
        w2.GetProperty("self").GetProperty("res").GetInt32().ShouldBe(res);
    }

    [Fact]
    public async Task Shutdown_SavesEveryConnectedPlayer() // CA2
    {
        var server = await TestServer.StartAsync();
        using var anaApi = await new ApiClient(server).RegisterAndLogin("ana");
        using var bobApi = await new ApiClient(server).RegisterAndLogin("bob");
        var ana = await Connect(server, anaApi, await anaApi.CreateCharacterId("Ana", "warrior"));
        var bob = await Connect(server, bobApi, await bobApi.CreateCharacterId("Bob", "mage"));
        await ana.ExpectAsync("Welcome");
        await bob.ExpectAsync("Welcome");
        var saver = server.Services.GetRequiredService<SaveService>();
        var registry = server.Services.GetRequiredService<PlayerRegistry>();
        registry.Count.ShouldBe(2);

        await server.DisposeAsync(); // Ctrl+C

        saver.Saved.ShouldBe(2);
        registry.Count.ShouldBe(0);
        await ana.DisposeAsync();
        await bob.DisposeAsync();
    }

    [Fact]
    public async Task Autosave_OnlyWhenDirty_AndIntervalElapsed() // CA3
    {
        await using var server = await TestServer.StartAsync(new() { ["Persistence:AutosaveSec"] = "0.4" });
        using var api = await new ApiClient(server).RegisterAndLogin("ana");
        await using var ana = await Connect(server, api, await api.CreateCharacterId("Ana", "warrior"));
        await ana.ExpectAsync("Welcome");
        var saver = server.Services.GetRequiredService<SaveService>();

        await Task.Delay(700, TestContext.Current.CancellationToken);
        saver.Saved.ShouldBe(0); // sin cambios: no se guarda

        await ana.SendAsync("MoveInput", """{"seq":1,"dx":1,"dy":0}""");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await ana.SendAsync("MoveInput", """{"seq":2,"dx":0,"dy":0}""");
        await WaitUntil(() => saver.Saved >= 1, 1500);
        await Task.Delay(600, TestContext.Current.CancellationToken); // puede caer un segundo guardado con el movimiento posterior al primero
        var afterMove = saver.Saved;
        afterMove.ShouldBeInRange(1, 2);
        await Task.Delay(700, TestContext.Current.CancellationToken);
        saver.Saved.ShouldBe(afterMove); // ya no está Dirty: no se repite
    }

    private static async Task WaitUntil(Func<bool> cond, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!cond())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condición no cumplida");
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }
}
