using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Persistence.InMemory;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-051/HU-052/HU-055/HU-057 por WebSocket: mover/equipar, vendedor, InventoryUpdate completo y persistencia con auditoría.</summary>
public sealed class InventoryFlowTests
{
    private static async Task<(ApiClient Api, Guid CharId, TestGameClient Client, int SelfId)> Enter(TestServer server, string user, string name, string classId)
    {
        var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        var w = await client.ExpectAsync("Welcome");
        return (api, id, client, w.GetProperty("selfId").GetInt32());
    }

    [Fact]
    public async Task Move_Equip_Unequip_InventoryUpdate_AndStatsUpdate()
    {
        await using var server = await TestServer.StartAsync();
        var (api, _, ana, selfId) = await Enter(server, "ana", "Ana", "priest"); // maza equipada, varita en la bolsa (slot 0)
        using (api) await using (ana)
        {
            await ana.SendAsync("InventoryMove", """{"from":{"c":"bag","i":0},"to":{"c":"bag","i":5},"reqId":1}""");
            var upd = await ana.ExpectAsync("InventoryUpdate");
            upd.GetProperty("reqId").GetInt32().ShouldBe(1);
            upd.GetProperty("bag")[0].ValueKind.ShouldBe(JsonValueKind.Null);
            upd.GetProperty("bag")[5].GetProperty("templateId").GetString().ShouldBe("novice_wand");
            // Equipar la varita (clic derecho = mover a su slot): intercambia con la maza y llega StatsUpdate.
            await ana.SendAsync("InventoryMove", """{"from":{"c":"bag","i":5},"to":{"c":"equip","i":7},"reqId":2}""");
            var upd2 = await ana.ExpectAsync("InventoryUpdate", m => m.GetProperty("reqId").ValueKind != JsonValueKind.Null && m.GetProperty("reqId").GetInt32() == 2);
            upd2.GetProperty("equipment")[7].GetProperty("templateId").GetString().ShouldBe("novice_wand");
            upd2.GetProperty("bag")[5].GetProperty("templateId").GetString().ShouldBe("initiate_mace");
            var stats = await ana.ExpectAsync("StatsUpdate");
            stats.GetProperty("derived").GetProperty("spellPower").GetSingle().ShouldBeGreaterThan(0);
            // Índice fuera de rango → invalid_payload y nada cambia.
            await ana.SendAsync("InventoryMove", """{"from":{"c":"bag","i":5},"to":{"c":"bag","i":30},"reqId":3}""");
            var err = await ana.ExpectAsync("Error");
            err.GetProperty("code").GetString().ShouldBe("invalid_payload");
            err.GetProperty("reqId").GetInt32().ShouldBe(3);
            _ = selfId;
        }
    }

    [Fact]
    public async Task Vendor_Open_Buy_Sell_ThenPersistWithAudit()
    {
        await using var server = await TestServer.StartAsync();
        var (api, charId, ana, selfId) = await Enter(server, "ana", "Ana", "warrior");
        using (api)
        {
            var world = server.Services.GetRequiredService<World>();
            var registry = server.Services.GetRequiredService<PlayerRegistry>();
            var player = registry.All.First(p => p.Id.Value == selfId);
            var map = world.GetInstance(player.MapInstanceId)!;
            var marta = map.Actors.Values.OfType<Npc>().First(n => n.VendorId is not null);
            player.Position = marta.Position + new Vec2(1, 0);
            player.Gold = 1000;
            await ana.ExpectForIdAsync("EntitySpawn", marta.Id.Value);

            await ana.SendAsync("VendorOpen", $$"""{"npcId":{{marta.Id.Value}}}""");
            var window = await ana.ExpectAsync("VendorWindow");
            window.GetProperty("npcId").GetInt32().ShouldBe(marta.Id.Value);
            var potion = window.GetProperty("items").EnumerateArray().First(i => i.GetProperty("templateId").GetString() == "minor_healing_potion");
            var price = potion.GetProperty("price").GetInt64();

            await ana.SendAsync("VendorBuy", $$"""{"npcId":{{marta.Id.Value}},"templateId":"minor_healing_potion","qty":5}""");
            var upd = await ana.ExpectAsync("InventoryUpdate");
            upd.GetProperty("gold").GetInt64().ShouldBe(1000 - price * 5);
            var potionStack = upd.GetProperty("bag").EnumerateArray().First(b => b.ValueKind != JsonValueKind.Null && b.GetProperty("templateId").GetString() == "minor_healing_potion");
            potionStack.GetProperty("qty").GetInt32().ShouldBe(5);

            // Vender el pan inicial (sellPrice 1 × 5).
            var bread = upd.GetProperty("bag").EnumerateArray().First(b => b.ValueKind != JsonValueKind.Null && b.GetProperty("templateId").GetString() == "bread");
            await ana.SendAsync("VendorSell", $$"""{"npcId":{{marta.Id.Value}},"itemId":"{{bread.GetProperty("id").GetString()}}","qty":5}""");
            var upd2 = await ana.ExpectAsync("InventoryUpdate");
            upd2.GetProperty("gold").GetInt64().ShouldBe(1000 - price * 5 + 5);

            // Persistencia: salir y volver → mismos items, oro y auditoría en lote.
            await ana.DisposeAsync();
            var saver = server.Services.GetRequiredService<SaveService>();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (saver.Saved < 1 && DateTime.UtcNow < deadline) await Task.Delay(25, TestContext.Current.CancellationToken);
            var store = server.Services.GetRequiredService<InMemoryStore>();
            store.Audit.Count(a => a.CharacterId == charId && a.Action == "buy").ShouldBe(1);
            store.Audit.Count(a => a.CharacterId == charId && a.Action == "sell").ShouldBe(1);

            await using var ana2 = await TestGameClient.ConnectAsync(server.WsUrl);
            await ana2.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(charId)}}"}""");
            var welcome = await ana2.ExpectAsync("Welcome");
            welcome.GetProperty("self").ValueKind.ShouldBe(JsonValueKind.Object);
            welcome.GetProperty("inventory").EnumerateArray().Count(b => b.ValueKind != JsonValueKind.Null && b.GetProperty("templateId").GetString() == "minor_healing_potion").ShouldBe(1);
            welcome.GetProperty("inventory").EnumerateArray().Count(b => b.ValueKind != JsonValueKind.Null && b.GetProperty("templateId").GetString() == "bread").ShouldBe(0);
        }
    }
}
