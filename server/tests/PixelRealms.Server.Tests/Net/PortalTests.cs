using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Game.Core;
using PixelRealms.Game.Portals;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-027: portales y cambio de mapa de extremo a extremo; HU-112: la salida de la Mina al Bosque, cerrada por fase.</summary>
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

    /// <summary>HU-112: los personajes nuevos empiezan en la entrada de la Mina; `phase` = `rules.world.currentPhase`.</summary>
    private static PatchedContent StartInTheMine(int phase) => PatchedContent.WithRules(r =>
    {
        r["world"]!["startMapId"] = "mine";
        r["world"]!["currentPhase"] = phase;
    });

    /// <summary>Pone al jugador en el centro del portal, en el hilo del tick (regla 2): lo pisa en ese mismo tick.</summary>
    private static Task StepOnPortal(TestServer server, int selfId, string portalId) => server.RunOnTickAsync(_ =>
    {
        var player = server.Services.GetRequiredService<PlayerRegistry>().All.First(p => p.Id.Value == selfId);
        var portal = server.Services.GetRequiredService<World>().GetInstance(player.MapInstanceId)!.Data.Portals.First(p => p.PortalId == portalId);
        player.Position = portal.Position + portal.Size * 0.5f;
    });

    [Fact]
    public async Task MineExit_Phase1_DoesNotCross_AndWarnsOnceThatTheCollapseBlocksIt() // HU-112 CA2
    {
        using var content = StartInTheMine(phase: 1);
        await using var server = await TestServer.StartAsync(content.Settings);
        var (api, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (api)
        {
            var welcome = await ana.ExpectAsync("Welcome");
            welcome.GetProperty("mapId").GetString().ShouldBe("mine");
            await StepOnPortal(server, welcome.GetProperty("selfId").GetInt32(), "mine_to_forest");
            var err = await ana.ExpectAsync("Error");
            err.GetProperty("code").GetString().ShouldBe("portal_locked");
            err.GetProperty("message").GetString().ShouldBe("El derrumbe aún bloquea el paso");
            await Should.ThrowAsync<TimeoutException>(() => ana.ExpectAsync("Error", 400)); // sin spam mientras sigue encima
            await Should.ThrowAsync<TimeoutException>(() => ana.ExpectAsync("ChangeMap", 100));
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task MineExit_Phase2_GoesToTheForestEdge_AndTheWayBackLeadsToRoom3() // HU-112 CA1
    {
        using var content = StartInTheMine(phase: 2);
        await using var server = await TestServer.StartAsync(content.Settings);
        var (api, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (api)
        {
            var selfId = (await ana.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
            await StepOnPortal(server, selfId, "mine_to_forest");
            var change = await ana.ExpectAsync("ChangeMap");
            change.GetProperty("mapId").GetString().ShouldBe("forest");
            change.GetProperty("x").GetSingle().ShouldBe(12 * 16f); // MINE_ARRIVAL del Linde
            change.GetProperty("y").GetSingle().ShouldBe(29 * 16f);

            await StepOnPortal(server, selfId, "forest_to_mine");
            var back = await ana.ExpectAsync("ChangeMap");
            back.GetProperty("mapId").GetString().ShouldBe("mine");
            var at = new Vec2(back.GetProperty("x").GetSingle() / 16f, back.GetProperty("y").GetSingle() / 16f);
            var exit = server.Services.GetRequiredService<World>().Maps["mine"].Portals.Single(p => p.PortalId == "mine_to_forest");
            exit.Contains(at).ShouldBeFalse("no deja encima de la salida");
            PortalPolicy.DistanceTo(exit, at).ShouldBeLessThanOrEqualTo(1f); // en la Sala 3, junto a la salida
            // Sigue andando hacia el oeste, como al cruzar el portal del Linde: se aleja de la salida y no rebota al Bosque
            // (con la caja de los pies metida en una roca, el primer paso la empujaba al revés, encima de la salida).
            await ana.SendAsync("MoveInput", """{"seq":1,"dx":-1,"dy":0}""");
            await Should.ThrowAsync<TimeoutException>(() => ana.ExpectAsync("ChangeMap", 400));
            var x = 0f;
            await server.RunOnTickAsync(_ => x = server.Services.GetRequiredService<PlayerRegistry>().All.First(p => p.Id.Value == selfId).Position.X);
            x.ShouldBeLessThan(at.X);
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task MineExit_TargetMapNotLoaded_StaysClosed_EvenInPhase2() // HU-112: la salida de un tier que aún no existe
    {
        using var content = StartInTheMine(phase: 2);
        File.Delete(Path.Combine(content.MapsDir, "forest.tmj"));
        File.Delete(Path.Combine(content.MapsDir, "crypt.tmj")); // su vuelta al Bosque no lleva minPhase: sin Bosque no cargaría
        await using var server = await TestServer.StartAsync(content.Settings);
        var (api, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (api)
        {
            var selfId = (await ana.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
            await StepOnPortal(server, selfId, "mine_to_forest");
            var err = await ana.ExpectAsync("Error");
            err.GetProperty("code").GetString().ShouldBe("portal_locked");
            err.GetProperty("message").GetString().ShouldBe("El derrumbe aún bloquea el paso");
            await Should.ThrowAsync<TimeoutException>(() => ana.ExpectAsync("ChangeMap", 300));
            await ana.DisposeAsync();
        }
    }

    [Fact]
    public async Task ChangingMap_ForgetsItsThreatAndTagInTheMapItLeaves() // revisión de autoridad (HU-117): como al salir del mundo
    {
        await using var server = await TestServer.StartAsync();
        var (api, _, ana) = await Enter(server, "ana", "Ana", "warrior");
        using (api)
        {
            var selfId = (await ana.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32();
            var player = server.Services.GetRequiredService<PlayerRegistry>().All.First(p => p.Id.Value == selfId);
            var world = server.Services.GetRequiredService<World>();
            var transfer = server.Services.GetRequiredService<MapTransferService>();
            (bool Moved, bool Threat, bool Tagged) after = default;
            await server.RunOnTickAsync(t =>
            {
                var monster = world.GetInstance(player.MapInstanceId)!.Monsters.Values.First();
                monster.Threat.Add(player.Id, 50);
                monster.TaggedBy = player.Id;
                var moved = transfer.Transfer(player, "mine", new Vec2(11, 28), t, "test");
                after = (moved, monster.Threat.Contains(player.Id), monster.TaggedBy == player.Id);
            });
            after.Moved.ShouldBeTrue();
            after.Threat.ShouldBeFalse("ni el jefe ni sus retoños siguen yendo a por quien ya no está en el mapa");
            after.Tagged.ShouldBeFalse();
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
