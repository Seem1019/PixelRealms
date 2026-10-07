using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>
/// HU-100 (ADR-018): un área duradera llega como `AreaSpawn` a todos los de la instancia, también a quien entra después, y se va
/// con `AreaDespawn`; nada por tick. Con Estallido de llamas como área de 3 s (el contenido real aún no tiene ninguna: HU-117).
/// </summary>
public sealed class PersistentAreaNetTests
{
    private static async Task<(ApiClient Api, TestGameClient Client, JsonElement Welcome)> Enter(TestServer server, string user, string name, bool admin)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, "mage");
        if (admin)
        {
            var accounts = server.Services.GetRequiredService<IAccountRepository>();
            await accounts.SetAdminAsync((await accounts.FindByUsernameAsync(user))!.Id, true);
            await api.Login(user, "segura123"); // el ticket lleva el claim admin
        }
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (api, client, await client.ExpectAsync("Welcome"));
    }

    [Fact]
    public async Task ALastingArea_ReachesEveryoneOnTheMap_AlsoWhoEntersLater_AndThenGoes()
    {
        using var content = PatchedContent.WithRules(_ => { }).PatchContent("spells.json", root =>
            root["spells"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "mage_flame_burst")!["areaDurationMs"] = 3000);
        await using var server = await TestServer.StartAsync(content.Settings);
        var (anaApi, ana, welcome) = await Enter(server, "ana", "Ana", admin: true);
        using (anaApi) await using (ana)
        {
            await ana.SendAsync("AdminCommand", """{"text":"/level 5"}""");
            await ana.ExpectAsync("LevelUp", m => m.GetProperty("level").GetInt32() == 5);
            await ana.SendAsync("SetHotbar", """{"slot":3,"kind":"spell","ref":"mage_flame_burst"}""");
            var self = welcome.GetProperty("self");
            var (x, y) = (self.GetProperty("x").GetSingle(), self.GetProperty("y").GetSingle());
            await ana.SendAsync("CastSpell", $$"""{"spellId":"mage_flame_burst","targetPos":{"x":{{x + 48}},"y":{{y}}},"reqId":1}""");
            var spawn = await ana.ExpectAsync("AreaSpawn", timeoutMs: 4000);
            spawn.GetProperty("spellId").GetString().ShouldBe("mage_flame_burst");
            spawn.GetProperty("pos").GetProperty("x").GetSingle().ShouldBe(x + 48, 0.5f);
            var areaId = spawn.GetProperty("areaId").GetInt32();

            var (bobApi, bob, _) = await Enter(server, "bob", "Bob", admin: false); // entra con el área ya en el suelo
            using (bobApi) await using (bob)
            {
                (await bob.ExpectAsync("AreaSpawn")).GetProperty("areaId").GetInt32().ShouldBe(areaId);
                (await bob.ExpectAsync("AreaDespawn", timeoutMs: 5000)).GetProperty("areaId").GetInt32().ShouldBe(areaId);
            }
            (await ana.ExpectAsync("AreaDespawn", timeoutMs: 5000)).GetProperty("areaId").GetInt32().ShouldBe(areaId);
        }
    }
}
