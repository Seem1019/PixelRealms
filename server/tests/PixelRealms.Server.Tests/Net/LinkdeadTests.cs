using Microsoft.Extensions.DependencyInjection;
using System.Net.WebSockets;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-025: linkdead, reconexión con el mismo personaje y cierre normal.</summary>
public sealed class LinkdeadTests
{
    private static async Task<(ApiClient Api, Guid CharacterId, TestGameClient Client)> Enter(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await Connect(server, api, id);
        return (api, id, client);
    }

    private static async Task<TestGameClient> Connect(TestServer server, ApiClient api, Guid characterId)
    {
        var ticket = await api.Ticket(characterId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
        return client;
    }

    [Fact]
    public async Task ConnectionCut_PlayerStaysLinkdead_ThenLeavesAfterLinkdeadSec() // CA1
    {
        using var content = PatchedContent.WithRules(r => r["combat"]!["linkdeadSec"] = 1);
        await using var server = await TestServer.StartAsync(content.Settings);
        var (anaApi, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        var (bobApi, _, bob) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var anaId = (await ana.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
            await bob.ExpectAsync("Welcome");
            await bob.ExpectForIdAsync("EntitySpawn", anaId);

            ana.Abort(); // corte sin Close
            await Task.Delay(400, TestContext.Current.CancellationToken);
            server.Services.GetRequiredService<WorldSession>().LinkdeadCount.ShouldBe(1);
            // Sigue en el mundo: Bob no recibe su despawn todavía.
            (await bob.ArrivesAsync("EntityDespawn", m => m.GetProperty("id").GetInt32() == anaId, 300)).ShouldBeFalse();

            var despawn = await bob.ExpectForIdAsync("EntityDespawn", anaId, 2500);
            despawn.GetProperty("reason").GetString().ShouldBe("left");
            server.Services.GetRequiredService<PlayerRegistry>().Count.ShouldBe(1);
            var saver = server.Services.GetRequiredService<SaveService>();
            await WaitUntil(() => saver.Saved >= 1);
            await bob.DisposeAsync();
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Reconnect_WithinLinkdead_ResumesSameCharacter_AndResendsAoi() // CA2
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, anaId, ana) = await Enter(server, "ana", "Ana", "warrior");
        var (bobApi, _, bob) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var welcome = await ana.ExpectAsync("Welcome");
            var selfId = welcome.GetProperty("selfId").GetInt32();
            var bobId = (await bob.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
            await ana.ExpectForIdAsync("EntitySpawn", bobId);
            await ana.SendAsync("MoveInput", """{"seq":1,"dx":1,"dy":0}""");
            await Task.Delay(200, TestContext.Current.CancellationToken);
            var before = (await ana.LatestAsync("Snapshot")).GetProperty("self").GetProperty("x").GetSingle();

            ana.Abort();
            await Task.Delay(300, TestContext.Current.CancellationToken);

            await using var ana2 = await Connect(server, anaApi, anaId);
            var welcome2 = await ana2.ExpectAsync("Welcome");
            welcome2.GetProperty("selfId").GetInt32().ShouldBe(selfId);          // mismo personaje vivo, no uno nuevo
            welcome2.GetProperty("self").GetProperty("x").GetSingle().ShouldBeGreaterThanOrEqualTo(before); // posición del mundo, no de BD
            (await ana2.ExpectForIdAsync("EntitySpawn", bobId)).GetProperty("name").GetString().ShouldBe("Bob"); // AOI reenviada
            (await bob.ArrivesAsync("EntityDespawn", m => m.GetProperty("id").GetInt32() == selfId, 300)).ShouldBeFalse(); // Bob nunca dejó de verla
            server.Services.GetRequiredService<PlayerRegistry>().Count.ShouldBe(2);
            await ana.DisposeAsync();
            await bob.DisposeAsync();
        }
    }

    [Fact]
    public async Task Reconnect_RestartsInputSequence_SoTheNewConnectionCanMove()
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, anaId, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            var selfId = (await ana.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
            for (var seq = 1; seq <= 5; seq++) await ana.SendAsync("MoveInput", $$"""{"seq":{{seq}},"dx":1,"dy":0}""");
            await ana.SendAsync("MoveInput", """{"seq":6,"dx":0,"dy":0}""");
            await ana.ExpectAsync("Snapshot", s => s.GetProperty("ackSeq").GetInt32() == 6);
            ana.Abort();
            await Task.Delay(300, TestContext.Current.CancellationToken);

            await using var ana2 = await Connect(server, anaApi, anaId);
            var welcome2 = await ana2.ExpectAsync("Welcome");
            welcome2.GetProperty("selfId").GetInt32().ShouldBe(selfId); // reconexión real (mismo Player), no una sesión nueva con seq 0
            var startX = welcome2.GetProperty("self").GetProperty("x").GetSingle();
            // El cliente nuevo empieza otra vez en seq 1: si el servidor conservara el 6 lo descartaría y no se movería.
            await ana2.SendAsync("MoveInput", """{"seq":1,"dx":1,"dy":0}""");
            var moved = await ana2.ExpectAsync("Snapshot", s => s.GetProperty("ackSeq").GetInt32() == 1 && s.GetProperty("self").GetProperty("x").GetSingle() > startX);
            moved.GetProperty("ackSeq").GetInt32().ShouldBe(1);
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task NormalClose_SavesImmediately() // CA4
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            await ana.ExpectAsync("Welcome");
            await ana.DisposeAsync(); // CloseAsync normal (la X del juego)
            var saver = server.Services.GetRequiredService<SaveService>();
            await WaitUntil(() => saver.Saved >= 1, 2000);
            server.Services.GetRequiredService<PlayerRegistry>().Count.ShouldBe(0);
            server.Services.GetRequiredService<WorldSession>().LinkdeadCount.ShouldBe(0);
        }
    }

    private static async Task WaitUntil(Func<bool> cond, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!cond())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condición no cumplida");
            await Task.Delay(25);
        }
    }
}
