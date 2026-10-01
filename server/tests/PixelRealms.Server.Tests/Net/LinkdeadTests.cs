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
            await ana.ExpectAsync("Welcome");
            await bob.ExpectAsync("Welcome");
            await bob.ExpectAsync("EntitySpawn");

            ana.Abort(); // corte sin Close
            await Task.Delay(400);
            server.Services.GetRequiredService<WorldSession>().LinkdeadCount.ShouldBe(1);
            // Sigue en el mundo: Bob no recibe despawn todavía.
            await Should.ThrowAsync<TimeoutException>(() => bob.ExpectAsync("EntityDespawn", 300));

            var despawn = await bob.ExpectAsync("EntityDespawn", 2500);
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
            await bob.ExpectAsync("Welcome");
            await ana.ExpectAsync("EntitySpawn");
            await ana.SendAsync("MoveInput", """{"seq":1,"dx":1,"dy":0}""");
            await Task.Delay(200);
            var before = (await ana.LatestAsync("Snapshot")).GetProperty("self").GetProperty("x").GetSingle();

            ana.Abort();
            await Task.Delay(300);

            await using var ana2 = await Connect(server, anaApi, anaId);
            var welcome2 = await ana2.ExpectAsync("Welcome");
            welcome2.GetProperty("selfId").GetInt32().ShouldBe(selfId);          // mismo personaje vivo, no uno nuevo
            welcome2.GetProperty("self").GetProperty("x").GetSingle().ShouldBeGreaterThanOrEqualTo(before); // posición del mundo, no de BD
            (await ana2.ExpectAsync("EntitySpawn")).GetProperty("name").GetString().ShouldBe("Bob"); // AOI reenviada
            await Should.ThrowAsync<TimeoutException>(() => bob.ExpectAsync("EntityDespawn", 300)); // Bob nunca dejó de verla
            server.Services.GetRequiredService<PlayerRegistry>().Count.ShouldBe(2);
            await ana.DisposeAsync();
            await bob.DisposeAsync();
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
