using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-043: la barra de 4 hechizos + 4 utilizables (ADR-014) se valida en el servidor y se guarda con el personaje.</summary>
public sealed class SetHotbarTests
{
    private static async Task<(ApiClient Api, Guid CharacterId, TestGameClient Client, JsonElement Welcome)> Enter(TestServer server, ApiClient? api = null, Guid? characterId = null)
    {
        api ??= await new ApiClient(server).RegisterAndLogin("ana");
        var id = characterId ?? await api.CreateCharacterId("Ana", "warrior");
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (api, id, client, await client.ExpectAsync("Welcome"));
    }

    [Fact]
    public async Task SpellInAnItemSlot_ItemInASpellSlot_OrUnknown_IsInvalidPayload() // CA5
    {
        await using var server = await TestServer.StartAsync();
        var (api, _, ana, _) = await Enter(server);
        using (api) await using (ana)
        {
            foreach (var bad in new[]
                     {
                         """{"slot":5,"kind":"spell","ref":"warrior_heroic_strike"}""", // hechizo en casilla de utilizable
                         """{"slot":1,"kind":"item","ref":"bread"}""",                   // utilizable en casilla de hechizo
                         """{"slot":2,"kind":"spell","ref":"mage_fireball"}""",          // hechizo que no conoce
                         """{"slot":6,"kind":"item","ref":"worn_sword"}""",              // no es consumible
                         """{"slot":8,"kind":"item","ref":"bread"}""",                   // fuera de la barra
                     })
            {
                await ana.SendAsync("SetHotbar", bad);
                (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload", bad);
            }
        }
    }

    [Fact]
    public async Task ValidSlots_ArePersisted_AndComeBackInTheNextWelcome() // CA2, CA3
    {
        await using var server = await TestServer.StartAsync();
        var (api, id, ana, _) = await Enter(server);
        using (api)
        {
            await ana.SendAsync("SetHotbar", """{"slot":3,"kind":"spell","ref":"warrior_heroic_strike"}""");
            await ana.SendAsync("SetHotbar", """{"slot":5,"kind":"item","ref":"bread"}""");
            await ana.SendAsync("SetHotbar", """{"slot":0}"""); // sin kind: vacía la casilla
            await ana.SendAsync("Logout", """{"reqId":1}""");
            await ana.ExpectAsync("LoggedOut");
            await ana.DisposeAsync();

            var (_, _, again, welcome) = await Enter(server, api, id);
            await using (again)
            {
                var hotbar = welcome.GetProperty("hotbar").EnumerateArray().ToList();
                hotbar.ShouldContain(h => h.GetProperty("slot").GetInt32() == 3 && h.GetProperty("ref").GetString() == "warrior_heroic_strike");
                hotbar.ShouldContain(h => h.GetProperty("slot").GetInt32() == 5 && h.GetProperty("kind").GetString() == "item" && h.GetProperty("ref").GetString() == "bread");
                hotbar.ShouldNotContain(h => h.GetProperty("slot").GetInt32() == 0);
                // Un hechizo no ocupa dos casillas: al ponerlo en la 3 salió de la casilla donde estaba.
                hotbar.Count(h => h.GetProperty("ref").GetString() == "warrior_heroic_strike").ShouldBe(1);
            }
        }
    }

    [Fact]
    public async Task InCombat_AnOccupiedSpellSlotCannotChange_ButAnEmptyOneCanBeFilled() // revisión de autoridad: kit completo a mano
    {
        await using var server = await TestServer.StartAsync();
        var (api, _, ana, _) = await Enter(server);
        using (api) await using (ana)
        {
            var players = server.Services.GetRequiredService<PlayerRegistry>();
            await server.RunOnTickAsync(t => players.ByName("Ana")!.EnterCombat(t.NowMs));
            await ana.SendAsync("SetHotbar", """{"slot":0}"""); // vaciar la casilla de Golpe heroico en plena pelea
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("in_combat");

            await ana.SendAsync("SetHotbar", """{"slot":3,"kind":"spell","ref":"warrior_heroic_strike"}"""); // casilla vacía: sí
            (string Kind, string Ref)? slot3 = null;
            for (var i = 0; i < 20 && slot3 is null; i++)
            {
                await server.RunOnTickAsync(_ => slot3 = players.ByName("Ana")!.Hotbar[3]);
                if (slot3 is null) await Task.Delay(50, TestContext.Current.CancellationToken);
            }
            slot3.ShouldBe(("spell", "warrior_heroic_strike"));
        }
    }
}
