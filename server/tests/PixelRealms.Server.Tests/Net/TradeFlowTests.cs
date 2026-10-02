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
}
