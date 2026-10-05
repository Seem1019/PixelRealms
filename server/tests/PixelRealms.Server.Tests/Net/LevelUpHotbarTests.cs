using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-041 / HU-043: los hechizos que se aprenden al subir de nivel (también con `/level`) quedan en la barra del servidor
/// tal como los coloca el cliente, y se pueden lanzar.</summary>
public sealed class LevelUpHotbarTests
{
    private static async Task<(ApiClient Api, TestGameClient Client, JsonElement Welcome)> EnterAsAdmin(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var accounts = server.Services.GetRequiredService<IAccountRepository>();
        await accounts.SetAdminAsync((await accounts.FindByUsernameAsync(user))!.Id, true);
        await api.Login(user, "segura123"); // el ticket lleva el claim admin
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (api, client, await client.ExpectAsync("Welcome"));
    }

    /// <summary>Lo mismo que `game_state.gd::_on_level_up`: cada hechizo nuevo a la primera casilla de hechizo libre.</summary>
    private static async Task PlaceLikeTheClient(TestGameClient client, HashSet<int> taken, JsonElement levelUp)
    {
        foreach (var s in levelUp.GetProperty("newSpells").EnumerateArray())
        {
            var free = Enumerable.Range(0, 4).FirstOrDefault(i => !taken.Contains(i), -1);
            if (free < 0) continue;
            taken.Add(free);
            await client.SendAsync("SetHotbar", $$"""{"slot":{{free}},"kind":"spell","ref":"{{s.GetString()}}"}""");
        }
    }

    [Theory]
    [InlineData("warrior")]
    [InlineData("mage")]
    public async Task LevelCommand_NewSpellsPlacedByTheClient_AreCastable(string classId)
    {
        await using var server = await TestServer.StartAsync();
        var (api, ana, welcome) = await EnterAsAdmin(server, "ana", "Ana", classId);
        using (api) await using (ana)
        {
            var taken = welcome.GetProperty("hotbar").EnumerateArray().Select(h => h.GetProperty("slot").GetInt32()).ToHashSet();
            await ana.SendAsync("AdminCommand", """{"text":"/level 6"}""");
            for (var level = 2; level <= 6; level++)
                await PlaceLikeTheClient(ana, taken, await ana.ExpectAsync("LevelUp"));

            var players = server.Services.GetRequiredService<PlayerRegistry>();
            string[] known = [];
            await server.RunOnTickAsync(_ => known = [.. players.ByName("Ana")!.KnownSpells]);
            known.Length.ShouldBeGreaterThan(1);
            var req = 100;
            foreach (var spell in known)
                await ana.SendAsync("CastSpell", $$"""{"spellId":"{{spell}}","reqId":{{req++}}}""");
            (await ana.ArrivesAsync("Error", e => e.GetProperty("code").GetString() == "not_equipped", 1500)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task LevelCommandDownAndUpAgain_KeepsTheClientBarInStepWithTheServer() // la barra con hechizos fantasma
    {
        await using var server = await TestServer.StartAsync();
        var (api, ana, welcome) = await EnterAsAdmin(server, "ana", "Ana", "warrior");
        using (api) await using (ana)
        {
            var taken = welcome.GetProperty("hotbar").EnumerateArray().Select(h => h.GetProperty("slot").GetInt32()).ToHashSet();
            await ana.SendAsync("AdminCommand", """{"text":"/level 6"}""");
            for (var level = 2; level <= 6; level++)
                await PlaceLikeTheClient(ana, taken, await ana.ExpectAsync("LevelUp"));

            // Baja a 4: olvida Torbellino (nivel 5) y el servidor manda la barra y los hechizos que quedan.
            await ana.SendAsync("AdminCommand", """{"text":"/level 4"}""");
            var renewed = await ana.ExpectAsync("Welcome");
            renewed.GetProperty("knownSpells").EnumerateArray().Select(s => s.GetString()).ShouldNotContain("warrior_whirlwind");
            renewed.GetProperty("hotbar").EnumerateArray().ShouldNotContain(h => h.GetProperty("ref").GetString() == "warrior_whirlwind");

            // El cliente rehace su barra con ese Welcome y, al volver a subir, coloca Torbellino donde el servidor lo espera.
            taken = renewed.GetProperty("hotbar").EnumerateArray().Select(h => h.GetProperty("slot").GetInt32()).ToHashSet();
            await ana.SendAsync("AdminCommand", """{"text":"/level 6"}""");
            for (var level = 5; level <= 6; level++)
                await PlaceLikeTheClient(ana, taken, await ana.ExpectAsync("LevelUp"));
            await ana.SendAsync("CastSpell", """{"spellId":"warrior_whirlwind","reqId":7}""");
            (await ana.ArrivesAsync("Error", e => e.GetProperty("code").GetString() == "not_equipped", 1500)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task LevelCommandDownInTheMine_SendsTheLeverAndDoorStateAgain() // revisión de autoridad
    {
        await using var server = await TestServer.StartAsync();
        var (api, ana, _) = await EnterAsAdmin(server, "ana", "Ana", "warrior");
        using (api) await using (ana)
        {
            await ana.SendAsync("AdminCommand", """{"text":"/level 6"}""");
            await ana.ExpectAsync("ChatMessage", m => m.GetProperty("channel").GetString() == "system");
            await ana.SendAsync("AdminCommand", """{"text":"/tp 243 53.5"}"""); // portal de la Mina
            (await ana.ExpectAsync("ChangeMap")).GetProperty("mapId").GetString().ShouldBe("mine");
            await ana.ExpectAsync("MapObjects");

            // El Welcome renovado vacía en el cliente el estado de palancas y puerta: tiene que llegar otra vez detrás.
            await ana.SendAsync("AdminCommand", """{"text":"/level 4"}""");
            await ana.ExpectAsync("Welcome");
            (await ana.ExpectAsync("MapObjects")).GetProperty("objects").GetArrayLength().ShouldBe(4);
        }
    }
}
