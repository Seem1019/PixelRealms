using System.Net.Http.Json;
using PixelRealms.Persistence.InMemory;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

public sealed class EnterWorldTests
{
    private static async Task<(ApiClient Api, Guid CharacterId, string Ticket)> NewCharacterWithTicket(TestServer server, string user = "ana", string name = "Ana", string classId = "warrior")
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        return (api, id, await api.Ticket(id));
    }

    [Fact]
    public async Task Hello_WithValidTicket_ReturnsWelcome() // HU-014 CA1/CA2
    {
        await using var server = await TestServer.StartAsync();
        var (api, id, ticket) = await NewCharacterWithTicket(server);
        using (api)
        {
            await using var client = await TestGameClient.ConnectAsync(server.WsUrl + "?ticket=" + ticket);
            await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
            var w = await client.ExpectAsync("Welcome");
            w.GetProperty("mapId").GetString().ShouldBe("meadow");
            w.GetProperty("tickRate").GetInt32().ShouldBe(20);
            w.GetProperty("snapshotRate").GetInt32().ShouldBe(10);
            w.GetProperty("selfId").GetInt32().ShouldBeGreaterThan(0);
            var self = w.GetProperty("self");
            self.GetProperty("name").GetString().ShouldBe("Ana");
            self.GetProperty("classId").GetString().ShouldBe("warrior");
            self.GetProperty("hp").GetInt32().ShouldBe(204);
            self.GetProperty("maxRes").GetInt32().ShouldBe(100);
            self.GetProperty("resource").GetString().ShouldBe("rage");
            self.GetProperty("xpNext").GetInt32().ShouldBe(100);
            self.GetProperty("x").GetSingle().ShouldBe(23 * 16); // cementerio de la aldea (23, 60) en píxeles
            w.GetProperty("knownSpells").EnumerateArray().Select(e => e.GetString()).ToArray().ShouldBe(new[] { "warrior_heroic_strike" });
            w.GetProperty("inventory").GetArrayLength().ShouldBe(24);
            w.GetProperty("equipment").GetArrayLength().ShouldBe(9);
            w.GetProperty("equipment")[7].GetProperty("templateId").GetString().ShouldBe("worn_sword");
            w.GetProperty("hotbar")[0].GetProperty("ref").GetString().ShouldBe("warrior_heroic_strike");
            w.GetProperty("rulesHash").GetString()!.Length.ShouldBe(16);
            var registry = (PlayerRegistry)server.Services.GetService(typeof(PlayerRegistry))!;
            await Task.Delay(100, TestContext.Current.CancellationToken);
            registry.ByCharacter(id).ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Hello_UsedOrBadTicket_Error_AndClose() // HU-014 CA3
    {
        await using var server = await TestServer.StartAsync(new() { ["Net:RequireTicket"] = "true" });
        var (api, _, ticket) = await NewCharacterWithTicket(server);
        using (api)
        {
            await using var first = await TestGameClient.ConnectAsync(server.WsUrl + "?ticket=" + ticket);
            await first.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
            await first.ExpectAsync("Welcome");

            await using var second = await TestGameClient.ConnectAsync(server.WsUrl + "?ticket=" + ticket);
            await second.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
            (await second.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("bad_ticket");
            (await second.ExpectCloseAsync()).ShouldBe("bad_ticket");
        }
    }

    [Fact]
    public async Task Hello_WrongVersion_BadVersion() // HU-014 CA4
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", """{"protocolVersion":99,"ticket":"x"}""");
        (await client.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("bad_version");
        (await client.ExpectCloseAsync()).ShouldBe("bad_version");
    }

    [Fact]
    public async Task SecondLogin_SameAccount_ReplacesAndSavesFirst() // HU-014 CA5
    {
        await using var server = await TestServer.StartAsync();
        var (api, id, ticket1) = await NewCharacterWithTicket(server);
        using (api)
        {
            await using var first = await TestGameClient.ConnectAsync(server.WsUrl);
            await first.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket1}}"}""");
            await first.ExpectAsync("Welcome");

            var ticket2 = await api.Ticket(id);
            await using var second = await TestGameClient.ConnectAsync(server.WsUrl);
            await second.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket2}}"}""");
            await second.ExpectAsync("Welcome");
            (await first.ExpectCloseAsync()).ShouldBe("replaced");

            var saver = (PixelRealms.Server.Hosting.SaveService)server.Services.GetService(typeof(PixelRealms.Server.Hosting.SaveService))!;
            await Task.Delay(200, TestContext.Current.CancellationToken);
            saver.Saved.ShouldBeGreaterThanOrEqualTo(1);
            var store = (InMemoryStore)server.Services.GetService(typeof(InMemoryStore))!;
            store.Characters[id].UpdatedAt.ShouldBeGreaterThan(store.Characters[id].CreatedAt);
        }
    }

    [Fact]
    public async Task Disconnect_SavesCharacter()
    {
        await using var server = await TestServer.StartAsync();
        var (api, id, ticket) = await NewCharacterWithTicket(server);
        using (api)
        {
            var client = await TestGameClient.ConnectAsync(server.WsUrl);
            await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{ticket}}"}""");
            await client.ExpectAsync("Welcome");
            await client.DisposeAsync();
            var registry = (PlayerRegistry)server.Services.GetService(typeof(PlayerRegistry))!;
            var saver = (PixelRealms.Server.Hosting.SaveService)server.Services.GetService(typeof(PixelRealms.Server.Hosting.SaveService))!;
            for (var i = 0; i < 20 && saver.Saved == 0; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
            saver.Saved.ShouldBe(1);
            registry.ByCharacter(id).ShouldBeNull();
        }
    }
}
