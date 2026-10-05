using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-083 CA1 de extremo a extremo: al entrar en la Mina llega el estado de palancas y puerta; `Interact` las cambia.</summary>
public sealed class MapObjectsFlowTests
{
    private static async Task<TestGameClient> EnterAsAdmin(TestServer server, string user, string name)
    {
        using var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, "warrior");
        var accounts = server.Services.GetRequiredService<IAccountRepository>();
        await accounts.SetAdminAsync((await accounts.FindByUsernameAsync(user))!.Id, true);
        await api.Login(user, "segura123"); // el ticket lleva el claim admin
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        await client.ExpectAsync("Welcome");
        return client;
    }

    private static Task<JsonElement> System(TestGameClient c) => c.ExpectAsync("ChatMessage", m => m.GetProperty("channel").GetString() == "system");

    private static Dictionary<string, string> States(JsonElement objects) =>
        objects.GetProperty("objects").EnumerateArray().ToDictionary(o => o.GetProperty("id").GetString()!, o => o.GetProperty("state").GetString()!);

    [Fact]
    public async Task EnteringTheMine_SendsLeversAndDoor_AndBothLeversOpenIt()
    {
        await using var server = await TestServer.StartAsync();
        var ana = await EnterAsAdmin(server, "ana", "Ana");
        await using (ana)
        {
            await ana.SendAsync("AdminCommand", """{"text":"/level 4"}""");
            await System(ana);
            await ana.SendAsync("AdminCommand", """{"text":"/tp 243 53.5"}"""); // sobre el portal de la Mina
            (await ana.ExpectAsync("ChangeMap")).GetProperty("mapId").GetString().ShouldBe("mine");
            var initial = States(await ana.ExpectAsync("MapObjects"));
            initial.ShouldBe(new Dictionary<string, string> { ["mine_lever_west"] = "off", ["mine_lever_east"] = "off", ["mine_lever_inside"] = "off", ["mine_boss_door"] = "closed" });

            await ana.SendAsync("Interact", """{"objectId":"mine_lever_east","reqId":1}"""); // desde la entrada: lejos
            var far = await ana.ExpectAsync("Error");
            far.GetProperty("code").GetString().ShouldBe("out_of_range");
            far.GetProperty("reqId").GetInt32().ShouldBe(1);

            await ana.SendAsync("AdminCommand", """{"text":"/tp 51.5 33"}""");
            await System(ana);
            await ana.SendAsync("Interact", """{"objectId":"mine_lever_west"}""");
            States(await ana.ExpectAsync("MapObjects")).ShouldBe(new Dictionary<string, string> { ["mine_lever_west"] = "on" });

            await ana.SendAsync("AdminCommand", """{"text":"/tp 62.5 33"}""");
            await System(ana);
            await ana.SendAsync("Interact", """{"objectId":"mine_lever_east"}""");
            States(await ana.ExpectAsync("MapObjects")).ShouldBe(new Dictionary<string, string> { ["mine_lever_east"] = "on" });
            States(await ana.ExpectAsync("MapObjects")).ShouldBe(new Dictionary<string, string> { ["mine_boss_door"] = "open" });
        }
    }
}
