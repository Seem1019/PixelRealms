using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>
/// HU-104: `ChooseSpellUpgrade` elige la mejora por la red, el servidor responde con `SpellUpgradesUpdate`, se guarda con el
/// personaje y vuelve en el `Welcome`; `LevelUp` avisa al llegar al nivel 8. Con mejoras de prueba en Bola de fuego y la Fase 2
/// abierta (el contenido real aún no tiene mejoras: HU-107).
/// </summary>
public sealed class SpellUpgradeNetTests
{
    private static PatchedContent Content() => PatchedContent.WithRules(r => r["world"]!["currentPhase"] = 2)
        .PatchContent("spells.json", root =>
        {
            var fireball = root["spells"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "mage_fireball")!;
            fireball["upgrades"] = JsonNode.Parse("""
                [{ "id": "fireball_quick", "name": "Llama rápida", "description": "Casteo 0,5 s más corto.", "mods": [{ "stat": "castMs", "add": -500 }] },
                 { "id": "fireball_hot", "name": "Llama intensa", "description": "+20 % de daño.", "mods": [{ "effect": "damage", "mult": 1.2 }] }]
                """);
        });

    private static async Task<(TestGameClient Client, JsonElement Welcome)> Enter(TestServer server, ApiClient api, Guid id)
    {
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (client, await client.ExpectAsync("Welcome"));
    }

    private static async Task<(ApiClient Api, Guid Id)> AdminMage(TestServer server)
    {
        var api = await new ApiClient(server).RegisterAndLogin("ana");
        var id = await api.CreateCharacterId("Ana", "mage");
        var accounts = server.Services.GetRequiredService<IAccountRepository>();
        await accounts.SetAdminAsync((await accounts.FindByUsernameAsync("ana"))!.Id, true);
        await api.Login("ana", "segura123"); // el ticket lleva el claim admin
        return (api, id);
    }

    [Fact]
    public async Task ChooseOverTheWire_IsConfirmed_Saved_AndComesBackInTheWelcome() // CA2, CA3, CA5
    {
        using var content = Content();
        await using var server = await TestServer.StartAsync(content.Settings);
        var (api, id) = await AdminMage(server);
        using (api)
        {
            var (ana, _) = await Enter(server, api, id);
            await ana.SendAsync("ChooseSpellUpgrade", """{"spellId":"mage_fireball","upgradeId":"fireball_quick","reqId":1}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("level_too_low"); // nivel 1

            await ana.SendAsync("AdminCommand", """{"text":"/level 8"}""");
            var levelEight = await ana.ExpectAsync("LevelUp", m => m.GetProperty("level").GetInt32() == 8);
            levelEight.GetProperty("upgradesUnlocked").EnumerateArray().Select(s => s.GetString()).ShouldContain("mage_fireball");

            await ana.SendAsync("ChooseSpellUpgrade", """{"spellId":"mage_fireball","upgradeId":"fireball_quick","reqId":2}""");
            var update = await ana.ExpectAsync("SpellUpgradesUpdate");
            update.GetProperty("upgrades").GetProperty("mage_fireball").GetString().ShouldBe("fireball_quick");
            update.GetProperty("reqId").GetInt32().ShouldBe(2);

            await ana.SendAsync("Logout", """{"reqId":3}""");
            await ana.ExpectAsync("LoggedOut");
            await ana.DisposeAsync();

            var (again, welcome) = await Enter(server, api, id);
            await using (again)
                welcome.GetProperty("spellUpgrades").GetProperty("mage_fireball").GetString().ShouldBe("fireball_quick");
        }
    }

    [Fact]
    public async Task InCombat_OrSomeoneElsesSpell_IsRejected() // CA3, CA7
    {
        using var content = Content();
        await using var server = await TestServer.StartAsync(content.Settings);
        var (api, id) = await AdminMage(server);
        using (api)
        {
            var (ana, _) = await Enter(server, api, id);
            await using (ana)
            {
                await ana.SendAsync("AdminCommand", """{"text":"/level 8"}""");
                await ana.ExpectAsync("LevelUp", m => m.GetProperty("level").GetInt32() == 8);
                await ana.SendAsync("ChooseSpellUpgrade", """{"spellId":"warrior_cleave","upgradeId":"fireball_quick","reqId":1}""");
                (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload");

                var players = server.Services.GetRequiredService<PlayerRegistry>();
                await server.RunOnTickAsync(t => players.ByName("Ana")!.EnterCombat(t.NowMs));
                await ana.SendAsync("ChooseSpellUpgrade", """{"spellId":"mage_fireball","upgradeId":"fireball_hot","reqId":2}""");
                (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("in_combat");
                string[] stored = [];
                await server.RunOnTickAsync(_ => stored = [.. players.ByName("Ana")!.SpellUpgrades.Keys]);
                stored.ShouldBeEmpty();
            }
        }
    }
}
