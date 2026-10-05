using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-070: `/tpto` a otro mapa deja ver lo que hay en el destino (jugadores y NPC), como un portal.</summary>
public sealed class AdminTeleportTests
{
    private static async Task<(ApiClient Api, TestGameClient Client, int SelfId)> Enter(TestServer server, string user, string name, bool admin)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, "warrior");
        if (admin)
        {
            var accounts = server.Services.GetRequiredService<IAccountRepository>();
            await accounts.SetAdminAsync((await accounts.FindByUsernameAsync(user))!.Id, true);
            await api.Login(user, "segura123"); // el ticket lleva el claim admin
        }
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (api, client, (await client.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32());
    }

    private static Task<JsonElement> System(TestGameClient c) => c.ExpectAsync("ChatMessage", m => m.GetProperty("channel").GetString() == "system");

    [Fact]
    public async Task TpToAPlayerOnAnotherMap_ShowsThatPlayerAndTheNpcs()
    {
        await using var server = await TestServer.StartAsync();
        var (bobApi, bob, bobId) = await Enter(server, "bob", "Bob", admin: false);
        var (anaApi, ana, _) = await Enter(server, "ana", "Ana", admin: true);
        using (bobApi) using (anaApi) await using (bob) await using (ana)
        {
            await ana.SendAsync("AdminCommand", """{"text":"/level 4"}""");
            await System(ana);
            await ana.SendAsync("AdminCommand", """{"text":"/tp 243 53.5"}"""); // portal de la Mina
            (await ana.ExpectAsync("ChangeMap")).GetProperty("mapId").GetString().ShouldBe("mine");
            // Fuera lo que vio al entrar en la aldea: solo cuentan las apariciones posteriores al /tpto.
            try { await ana.LatestAsync("EntitySpawn", 500); } catch (TimeoutException) { }

            await ana.SendAsync("AdminCommand", """{"text":"/tpto Bob"}""");
            (await ana.ExpectAsync("ChangeMap")).GetProperty("mapId").GetString().ShouldBe("meadow");
            var spawns = new List<JsonElement>();
            while (await ana.ArrivesAsync("EntitySpawn", m => { spawns.Add(m); return false; }, 1500)) { }
            spawns.ShouldContain(m => m.GetProperty("id").GetInt32() == bobId);
            spawns.ShouldContain(m => m.GetProperty("kind").GetString() == "npc");
        }
    }
}
