using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-070: comandos de administrador por `AdminCommand{text}`; `forbidden` para cuentas normales; `make-admin` vía repositorio.</summary>
public sealed class AdminCommandTests
{
    private static async Task<(TestGameClient Client, int SelfId)> Enter(TestServer server, string user, string name, string classId, bool admin)
    {
        using var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        if (admin)
        {
            // CA4: lo que hace `dotnet run -- make-admin <usuario>`.
            var accounts = server.Services.GetRequiredService<IAccountRepository>();
            var account = (await accounts.FindByUsernameAsync(user))!;
            await accounts.SetAdminAsync(account.Id, true);
            await api.Login(user, "segura123"); // el JWT (y el ticket) llevan el claim admin
        }
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (client, (await client.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32());
    }

    private static Task<JsonElement> System(TestGameClient c, int timeoutMs = 3000) => c.ExpectAsync("ChatMessage", m => m.GetProperty("channel").GetString() == "system", timeoutMs);

    [Fact]
    public async Task NormalAccount_GetsForbidden() // CA2
    {
        await using var server = await TestServer.StartAsync();
        var (bob, _) = await Enter(server, "bob", "Bob", "mage", admin: false);
        await using (bob)
        {
            await bob.SendAsync("AdminCommand", """{"text":"/heal"}""");
            (await bob.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("forbidden");
        }
    }

    [Fact]
    public async Task Admin_Tp_Level_Heal_Gold_God_Give_Spawn_Announce() // CA1, CA3
    {
        await using var server = await TestServer.StartAsync();
        var (ana, anaId) = await Enter(server, "ana", "Ana", "warrior", admin: true);
        var (bob, _) = await Enter(server, "bob", "Bob", "mage", admin: false);
        await using (ana) await using (bob)
        {
            var players = server.Services.GetRequiredService<PlayerRegistry>();
            var self = players.ByName("Ana")!;

            await ana.SendAsync("AdminCommand", """{"text":"/tp 5 5"}""");
            (await System(ana)).GetProperty("text").GetString()!.ShouldContain("Teletransportado");
            // El snapshot refleja la posición (en px, 16 por casilla).
            var snap = await ana.ExpectAsync("Snapshot", s => Math.Abs(s.GetProperty("self").GetProperty("x").GetDouble() - 80) < 1, 3000);
            snap.ValueKind.ShouldBe(JsonValueKind.Object);
            _ = anaId;

            await ana.SendAsync("AdminCommand", """{"text":"/level 4"}""");
            (await System(ana)).GetProperty("text").GetString().ShouldBe("Nivel 4");
            self.Level.ShouldBe(4);
            (await ana.ExpectAsync("StatsUpdate", s => s.GetProperty("level").GetInt32() == 4)).ValueKind.ShouldBe(JsonValueKind.Object);

            await ana.SendAsync("AdminCommand", """{"text":"/gold 250"}""");
            (await System(ana)).GetProperty("text").GetString().ShouldBe("Oro: 250");
            self.PendingAudit.ShouldContain(a => a.Action == "admin_give" && a.TemplateId == "gold" && a.Quantity == 250);
            (await ana.ExpectAsync("InventoryUpdate", i => i.GetProperty("gold").GetInt64() == 250)).ValueKind.ShouldBe(JsonValueKind.Object);

            await ana.SendAsync("AdminCommand", """{"text":"/give minor_healing_potion 3 Bob"}""");
            var give = (await System(ana)).GetProperty("text").GetString()!;
            give.ShouldContain("Bob");
            var bobPlayer = players.ByName("Bob")!;
            bobPlayer.Inventory.Bag.Count(i => i is { TemplateId: "minor_healing_potion" }).ShouldBeGreaterThan(0);
            bobPlayer.PendingAudit.ShouldContain(a => a.Action == "admin_give" && a.TemplateId == "minor_healing_potion");
            (await bob.ExpectAsync("InventoryUpdate")).ValueKind.ShouldBe(JsonValueKind.Object);

            self.Hp = 1;
            await ana.SendAsync("AdminCommand", """{"text":"/heal"}""");
            (await System(ana)).GetProperty("text").GetString()!.ShouldContain("máximo");
            self.Hp.ShouldBe(self.MaxHp);

            await ana.SendAsync("AdminCommand", """{"text":"/god"}""");
            (await System(ana)).GetProperty("text").GetString().ShouldBe("Modo dios: ON");
            self.GodMode.ShouldBeTrue();

            await ana.SendAsync("AdminCommand", """{"text":"/spawn wolf 2"}""");
            (await System(ana)).GetProperty("text").GetString().ShouldBe("2 × wolf creados");

            await ana.SendAsync("AdminCommand", """{"text":"/debug move on"}""");
            (await System(ana)).GetProperty("text").GetString()!.ShouldContain("ON");
            await ana.SendAsync("MoveInput", """{"seq":1,"dx":1,"dy":0}""");
            (await System(ana)).GetProperty("text").GetString()!.ShouldStartWith("[move] seq 1");

            await ana.SendAsync("AdminCommand", """{"text":"/announce Mantenimiento en 5 min"}""");
            (await System(bob)).GetProperty("text").GetString().ShouldBe("Mantenimiento en 5 min");

            await ana.SendAsync("AdminCommand", """{"text":"/nada"}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload");
        }
    }

    [Fact]
    public async Task Admin_Kill_Target_And_TpTo() // CA1
    {
        await using var server = await TestServer.StartAsync();
        var (ana, _) = await Enter(server, "ana", "Ana", "warrior", admin: true);
        var (bob, bobId) = await Enter(server, "bob", "Bob", "mage", admin: false);
        await using (ana) await using (bob)
        {
            var players = server.Services.GetRequiredService<PlayerRegistry>();
            await ana.SendAsync("AdminCommand", """{"text":"/tpto Bob"}""");
            (await System(ana)).GetProperty("text").GetString()!.ShouldContain("Bob");
            var self = players.ByName("Ana")!;
            var target = players.ByName("Bob")!;
            self.Position.ShouldBe(target.Position);

            await ana.SendAsync("SelectTarget", $$"""{"targetId":{{bobId}}}""");
            await Task.Delay(150, TestContext.Current.CancellationToken);
            await ana.SendAsync("AdminCommand", """{"text":"/kill"}""");
            (await System(ana)).GetProperty("text").GetString()!.ShouldContain("eliminado");
            (await bob.ExpectAsync("Died")).ValueKind.ShouldBe(JsonValueKind.Object);
            target.IsDead.ShouldBeTrue();
        }
    }
}
