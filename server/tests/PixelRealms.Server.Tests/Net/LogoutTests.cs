using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-015: volver a la selección de personaje desde el juego (`Logout` → `LoggedOut`), bloqueado en combate.</summary>
public sealed class LogoutTests
{
    private static async Task<(ApiClient Api, Guid CharacterId, TestGameClient Client, int SelfId)> Enter(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await Connect(server, api, id);
        var selfId = (await client.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
        return (api, id, client, selfId);
    }

    private static async Task<TestGameClient> Connect(TestServer server, ApiClient api, Guid characterId)
    {
        var ticket = await api.Ticket(characterId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
        return client;
    }

    [Fact]
    public async Task Logout_OutOfCombat_SavesLeavesAndConfirmsBeforeClosing() // CA1, CA3
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, _, ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        var (bobApi, _, bob, _) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            await bob.ExpectForIdAsync("EntitySpawn", anaId);
            var saver = server.Services.GetRequiredService<SaveService>();
            var savedBefore = saver.Saved;

            await ana.SendAsync("Logout", """{"reqId":1}""");
            await ana.ExpectAsync("LoggedOut");
            (await ana.ExpectCloseAsync()).ShouldBe("logout"); // el cierre llega después de la confirmación

            (await bob.ExpectForIdAsync("EntityDespawn", anaId)).GetProperty("reason").GetString().ShouldBe("left");
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            registry.Count.ShouldBe(1);
            registry.All.ShouldNotContain(p => p.Name == "Ana");
            server.Services.GetRequiredService<WorldSession>().LinkdeadCount.ShouldBe(0); // no queda linkdead
            await WaitUntil(() => saver.Saved > savedBefore);
            await bob.DisposeAsync();
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_InCombat_IsRefused_AndThePlayerStaysInTheWorld() // CA2
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, _, ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            // Solo test: marcar combate como lo haría un golpe recibido (IsInCombat mira LastCombatAtMs).
            registry.All.First(p => p.Id.Value == anaId).LastCombatAtMs = long.MaxValue / 2;

            await ana.SendAsync("Logout", """{"reqId":9}""");
            var err = await ana.ExpectAsync("Error");
            err.GetProperty("code").GetString().ShouldBe("in_combat");
            err.GetProperty("reqId").GetInt32().ShouldBe(9);
            (await ana.ArrivesAsync("LoggedOut", _ => true, 400)).ShouldBeFalse();
            registry.Count.ShouldBe(1);
            await ana.SendAsync("Ping", """{"clientTime":1}""");
            await ana.ExpectAsync("Pong"); // la conexión sigue abierta y el jugador dentro
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_ThenImmediateReentry_WithTheSameCharacter_ReadsTheSavedStateOnce() // CA5
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, anaChar, ana, _) = await Enter(server, "ana", "Ana", "warrior");
        using (anaApi)
        {
            for (var seq = 1; seq <= 6; seq++) await ana.SendAsync("MoveInput", $$"""{"seq":{{seq}},"dx":1,"dy":0}""");
            await ana.SendAsync("MoveInput", """{"seq":7,"dx":0,"dy":0}""");
            var moved = await ana.ExpectAsync("Snapshot", s => s.GetProperty("ackSeq").GetInt32() == 7);
            var x = moved.GetProperty("self").GetProperty("x").GetSingle();

            await ana.SendAsync("Logout");
            await ana.ExpectAsync("LoggedOut");
            await ana.ExpectCloseAsync();

            // Sin esperar: ticket nuevo y Hello enseguida, como al pulsar Jugar en la selección.
            await using var again = await Connect(server, anaApi, anaChar);
            var welcome = await again.ExpectAsync("Welcome");
            welcome.GetProperty("self").GetProperty("x").GetSingle().ShouldBe(x, 0.01f); // estado guardado al salir, no el viejo de BD
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            registry.Count.ShouldBe(1); // ni duplicado
            server.Services.GetRequiredService<WorldSession>().LinkdeadCount.ShouldBe(0); // ni linkdead
            (await again.ArrivesAsync("Error", _ => true, 300)).ShouldBeFalse();
            await ana.DisposeAsync();
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
