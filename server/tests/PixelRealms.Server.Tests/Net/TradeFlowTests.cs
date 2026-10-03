using System.Text.Json;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>Prueba de juego: en el intercambio el que recibía solo veía «item ×2», sin saber qué le ofrecían.
/// `TradeUpdate` lleva la plantilla de cada objeto ofrecido, en las dos ofertas.</summary>
public sealed class TradeFlowTests
{
    private static async Task<(ApiClient Api, TestGameClient Client, JsonElement Welcome)> Enter(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (api, client, await client.ExpectAsync("Welcome"));
    }

    [Fact]
    public async Task TradeUpdate_TellsBothSidesWhichItemIsOffered()
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, ana, anaWelcome) = await Enter(server, "ana", "Ana", "priest");
        var (bobApi, bob, _) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var offered = anaWelcome.GetProperty("inventory").EnumerateArray().First(i => i.ValueKind == JsonValueKind.Object);
            var itemId = offered.GetProperty("id").GetString()!;
            var templateId = offered.GetProperty("templateId").GetString()!;
            await bob.ExpectForIdAsync("EntitySpawn", anaWelcome.GetProperty("selfId").GetInt32());
            await ana.SendAsync("TradeRequest", """{"name":"Bob"}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "requested");
            await bob.SendAsync("TradeRespond", """{"accept":true}""");
            await ana.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "open");

            await ana.SendAsync("TradeOffer", $$"""{"items":[{"itemId":"{{itemId}}","qty":1}],"gold":0}""");
            var theirs = await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("theirs").GetProperty("items").GetArrayLength() == 1);
            theirs.GetProperty("theirs").GetProperty("items")[0].GetProperty("templateId").GetString().ShouldBe(templateId);
            var mine = await ana.ExpectAsync("TradeUpdate", x => x.GetProperty("mine").GetProperty("items").GetArrayLength() == 1);
            mine.GetProperty("mine").GetProperty("items")[0].GetProperty("templateId").GetString().ShouldBe(templateId);
            await ana.DisposeAsync(); await bob.DisposeAsync();
        }
    }

    [Fact]
    public async Task CompletedTrade_SavesBothCharactersAtOnce() // HU-026 CA6: sin esperar al autosave de 60 s
    {
        await using var server = await TestServer.StartAsync();
        var (anaApi, ana, anaWelcome) = await Enter(server, "ana", "Ana", "priest");
        var (bobApi, bob, _) = await Enter(server, "bob", "Bob", "mage");
        using (anaApi) using (bobApi)
        {
            var offered = anaWelcome.GetProperty("inventory").EnumerateArray().First(i => i.ValueKind == JsonValueKind.Object);
            var itemId = Guid.Parse(offered.GetProperty("id").GetString()!);
            await bob.ExpectForIdAsync("EntitySpawn", anaWelcome.GetProperty("selfId").GetInt32());
            await ana.SendAsync("TradeRequest", """{"name":"Bob"}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "requested");
            await bob.SendAsync("TradeRespond", """{"accept":true}""");
            await ana.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "open");
            await ana.SendAsync("TradeOffer", $$"""{"items":[{"itemId":"{{itemId}}","qty":1}],"gold":0}""");
            var version = (await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("theirs").GetProperty("items").GetArrayLength() == 1)).GetProperty("version").GetInt32();

            var saver = (PixelRealms.Server.Hosting.SaveService)server.Services.GetService(typeof(PixelRealms.Server.Hosting.SaveService))!;
            var savedBefore = saver.Saved;
            await ana.SendAsync("TradeConfirm", $$"""{"version":{{version}}}""");
            await bob.SendAsync("TradeConfirm", $$"""{"version":{{version}}}""");
            await bob.ExpectAsync("TradeUpdate", x => x.GetProperty("state").GetString() == "completed");
            for (var i = 0; i < 40 && saver.Saved < savedBefore + 2; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
            saver.Saved.ShouldBeGreaterThanOrEqualTo(savedBefore + 2);

            // En la BD (InMemory) el objeto ya no está en Ana y Bob tiene uno de la misma plantilla.
            var store = (PixelRealms.Persistence.InMemory.InMemoryStore)server.Services.GetService(typeof(PixelRealms.Persistence.InMemory.InMemoryStore))!;
            var anaRow = store.Characters.Values.Single(c => c.Name == "Ana");
            var bobRow = store.Characters.Values.Single(c => c.Name == "Bob");
            anaRow.Items.ShouldNotContain(i => i.Id == itemId);
            bobRow.Items.ShouldContain(i => i.TemplateId == offered.GetProperty("templateId").GetString());
            await ana.DisposeAsync(); await bob.DisposeAsync();
        }
    }
}
