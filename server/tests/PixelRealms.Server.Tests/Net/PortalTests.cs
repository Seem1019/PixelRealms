using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-027: portales y cambio de mapa de extremo a extremo.</summary>
public sealed class PortalTests
{
    /// <summary>Mueve el portal `to_mine` de meadow sobre el cementerio de la aldea (donde aparecen los personajes nuevos).</summary>
    private static PatchedContent PortalOverSpawn(bool keepMinLevel) => PatchedContent.WithMap("meadow", map =>
    {
        var portal = PatchedContent.FindObject(map, "portals", "to_mine");
        portal["x"] = 352; portal["y"] = 944; portal["width"] = 32; portal["height"] = 32; // casillas 22..23 × 59..60 ⊇ gy_village (23, 60)
        if (!keepMinLevel) PatchedContent.SetProperty(portal, "minLevel", null);
    });

    private static async Task<(ApiClient Api, Guid Id, TestGameClient Client)> Enter(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        return (api, id, await Connect(server, api, id));
    }

    private static async Task<TestGameClient> Connect(TestServer server, ApiClient api, Guid id)
    {
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return client;
    }

    [Fact]
    public async Task WalkOntoPortal_ChangesMap_UsePortalBack_DespawnsForOthers_AndSavesMapId() // CA1, CA2 (servidor), CA6
    {
        using var content = PortalOverSpawn(keepMinLevel: false);
        await using var server = await TestServer.StartAsync(content.Settings);
        var (anaApi, anaId, ana) = await Enter(server, "ana", "Ana", "warrior");
        var (bobApi, bobId, bob) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var anaWelcome = await ana.ExpectAsync("Welcome");
            anaWelcome.GetProperty("mapId").GetString().ShouldBe("meadow");
            var anaEnt = anaWelcome.GetProperty("selfId").GetInt32();
            var bobEnt = (await bob.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
            var change = await ana.ExpectAsync("ChangeMap");
            change.GetProperty("mapId").GetString().ShouldBe("mine");
            change.GetProperty("x").GetSingle().ShouldBe(11 * 16f); // targetX 11 casillas (entrada de la mina)
            change.GetProperty("y").GetSingle().ShouldBe(28 * 16f);
            await bob.ExpectAsync("ChangeMap");
            // En la mina se ven entre sí (AOI nueva).
            (await ana.ExpectForIdAsync("EntitySpawn", bobEnt)).GetProperty("name").GetString().ShouldBe("Bob");
            (await bob.ExpectForIdAsync("EntitySpawn", anaEnt)).GetProperty("name").GetString().ShouldBe("Ana");

            // Ana usa el portal de vuelta (está a ≤ 1 casilla de su borde): Bob la deja de ver.
            await ana.SendAsync("UsePortal", """{"portalId":"mine_to_meadow"}""");
            var back = await ana.ExpectAsync("ChangeMap");
            back.GetProperty("mapId").GetString().ShouldBe("meadow");
            back.GetProperty("x").GetSingle().ShouldBe(238 * 16f);
            var despawn = await bob.ExpectForIdAsync("EntityDespawn", anaEnt);
            despawn.GetProperty("reason").GetString().ShouldBe("left");

            // CA6: el guardado refleja el mapa nuevo; al reconectar aparecen allí.
            var saver = server.Services.GetRequiredService<SaveService>();
            await ana.DisposeAsync();
            await bob.DisposeAsync();
            await WaitUntil(() => saver.Pending == 0 && saver.Saved >= 4);
            await using var ana2 = await Connect(server, anaApi, anaId);
            var w = await ana2.ExpectAsync("Welcome");
            w.GetProperty("mapId").GetString().ShouldBe("meadow");
            w.GetProperty("self").GetProperty("x").GetSingle().ShouldBe(238 * 16f, 0.01f);
            await using var bob2 = await Connect(server, bobApi, bobId);
            (await bob2.ExpectAsync("Welcome")).GetProperty("mapId").GetString().ShouldBe("mine");
        }
    }

    [Fact]
    public async Task MinLevel_NotReached_LevelTooLow_OnlyOnce() // CA4
    {
        using var content = PortalOverSpawn(keepMinLevel: true);
        await using var server = await TestServer.StartAsync(content.Settings);
        var (api, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (api)
        {
            await ana.ExpectAsync("Welcome");
            var err = await ana.ExpectAsync("Error");
            err.GetProperty("code").GetString().ShouldBe("level_too_low");
            err.GetProperty("message").GetString().ShouldBe("Necesitas nivel 4");
            await Should.ThrowAsync<TimeoutException>(() => ana.ExpectAsync("Error", 400)); // sin spam mientras sigue encima
            await Should.ThrowAsync<TimeoutException>(() => ana.ExpectAsync("ChangeMap", 100));
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task UsePortal_FarAway_OutOfRange_UnknownPortal_NotFound() // CA1 (rango)
    {
        await using var server = await TestServer.StartAsync();
        var (api, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (api)
        {
            await ana.ExpectAsync("Welcome");
            await ana.SendAsync("UsePortal", """{"portalId":"meadow_to_mine"}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("out_of_range");
            await ana.SendAsync("UsePortal", """{"portalId":"nope"}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("not_found");
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
