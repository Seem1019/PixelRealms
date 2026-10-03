using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>HU-060/061/062/064/044 por WebSocket: chat por canales, grupo con PartyUpdate periódico, duelo y cambio de clase.</summary>
public sealed class SocialFlowTests
{
    private static async Task<(TestGameClient Client, int SelfId)> Enter(TestServer server, string user, string name, string classId)
    {
        using var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        return (client, (await client.ExpectAsync("Welcome")).GetProperty("selfId").GetInt32());
    }

    [Fact]
    public async Task OnlineList_GivesEveryConnectedPlayer_WithClassLevelAndZone() // HU-063 CA1
    {
        await using var server = await TestServer.StartAsync();
        var (ana, _) = await Enter(server, "ana", "Ana", "warrior");
        var (bob, _) = await Enter(server, "bob", "Bob", "mage");
        await using (ana) await using (bob)
        {
            await ana.SendAsync("OnlineListRequest");
            var list = (await ana.ExpectAsync("OnlineList")).GetProperty("players").EnumerateArray().ToList();
            list.Select(p => p.GetProperty("name").GetString()).ShouldBe(new[] { "Ana", "Bob" }); // por nombre
            var bobRow = list[1];
            bobRow.GetProperty("classId").GetString().ShouldBe("mage");
            bobRow.GetProperty("level").GetInt32().ShouldBe(1);
            bobRow.GetProperty("zone").GetString().ShouldNotBeNullOrEmpty(); // la zona de Tiled donde aparece (la Aldea)
        }
    }

    [Fact]
    public async Task Chat_Say_Global_Whisper_RateLimit_Who()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, _) = await Enter(server, "ana", "Ana", "warrior");
        var (bob, _) = await Enter(server, "bob", "Bob", "mage");
        await using (ana) await using (bob)
        {
            await ana.SendAsync("ChatSend", """{"channel":"say","text":"hola"}""");
            var m = await bob.ExpectAsync("ChatMessage");
            m.GetProperty("channel").GetString().ShouldBe("say");
            m.GetProperty("from").GetString().ShouldBe("Ana");
            m.GetProperty("text").GetString().ShouldBe("hola");
            await ana.SendAsync("ChatSend", """{"channel":"whisper","text":"psst","to":"bob"}""");
            (await bob.ExpectAsync("ChatMessage", x => x.GetProperty("channel").GetString() == "whisper")).GetProperty("text").GetString().ShouldBe("psst");
            await ana.SendAsync("ChatSend", """{"channel":"whisper","text":"psst","to":"nadie"}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("not_found");
            await ana.SendAsync("ChatSend", """{"channel":"say","text":""}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload");
            for (var i = 0; i < 4; i++) await ana.SendAsync("ChatSend", $$"""{"channel":"global","text":"{{i}}"}""");
            (await ana.ExpectAsync("Error", 3000)).GetProperty("code").GetString().ShouldBe("rate_limited"); // 2 + 4 > 5 en 5 s
            await bob.SendAsync("ChatSend", """{"channel":"who","text":"/who"}""");
            var who = await bob.ExpectAsync("ChatMessage", x => x.GetProperty("channel").GetString() == "system");
            who.GetProperty("text").GetString()!.ShouldContain("Ana · warrior · nv 1");
        }
    }

    [Fact]
    public async Task Party_Invite_Accept_PeriodicFrames_Leave()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        var (bob, bobId) = await Enter(server, "bob", "Bob", "mage");
        await using (ana) await using (bob)
        {
            await ana.SendAsync("PartyInvite", """{"name":"Bob"}""");
            var invite = await bob.ExpectAsync("PartyUpdate");
            invite.GetProperty("leader").GetString().ShouldBe("Ana");
            invite.GetProperty("members").GetArrayLength().ShouldBe(0); // invitación pendiente
            await bob.SendAsync("PartyRespond", """{"accept":true}""");
            var pu = await ana.ExpectAsync("PartyUpdate", x => x.GetProperty("members").GetArrayLength() == 2);
            pu.GetProperty("leader").GetString().ShouldBe("Ana");
            var members = pu.GetProperty("members").EnumerateArray().ToList();
            members.ShouldContain(x => x.GetProperty("name").GetString() == "Bob" && x.GetProperty("entityId").GetInt32() == bobId && x.GetProperty("online").GetBoolean());
            // Marcos periódicos ≥ 2/s (HU-062 CA1): al menos 2 PartyUpdate en 1,2 s.
            await bob.ExpectAsync("PartyUpdate", x => x.GetProperty("members").GetArrayLength() == 2, 1000);
            await bob.ExpectAsync("PartyUpdate", x => x.GetProperty("members").GetArrayLength() == 2, 1000);
            await bob.SendAsync("PartyLeave");
            (await ana.ExpectAsync("PartyUpdate", x => x.GetProperty("members").GetArrayLength() == 0, 2000)).GetProperty("leader").GetString().ShouldBe("");
            _ = anaId;
        }
    }

    [Fact]
    public async Task Duel_Request_Accept_Countdown_Active_Forfeit()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        var (bob, bobId) = await Enter(server, "bob", "Bob", "mage");
        await using (ana) await using (bob)
        {
            await ana.SendAsync("DuelRequest", """{"name":"Bob"}""");
            var req = await bob.ExpectAsync("DuelUpdate");
            req.GetProperty("state").GetString().ShouldBe("requested");
            req.GetProperty("opponentId").GetInt32().ShouldBe(anaId);
            await bob.SendAsync("DuelRespond", """{"accept":true}""");
            var cd = await ana.ExpectAsync("DuelUpdate", x => x.GetProperty("state").GetString() == "countdown");
            cd.GetProperty("startsInMs").GetInt32().ShouldBeGreaterThan(0);
            (await ana.ExpectAsync("DuelUpdate", x => x.GetProperty("state").GetString() == "active", 5000)).GetProperty("opponentId").GetInt32().ShouldBe(bobId);
            await bob.SendAsync("DuelForfeit");
            var ended = await ana.ExpectAsync("DuelUpdate", x => x.GetProperty("state").GetString() == "ended");
            ended.GetProperty("winnerId").GetInt32().ShouldBe(anaId);
            (await ana.ExpectAsync("ChatMessage", x => x.GetProperty("channel").GetString() == "system")).GetProperty("text").GetString()!.ShouldContain("ganado el duelo");
        }
    }

    [Fact]
    public async Task ChangeClass_AtNpc_NewWelcome_AndSaved()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, selfId) = await Enter(server, "ana", "Ana", "warrior");
        await using (ana)
        {
            var world = server.Services.GetRequiredService<World>();
            var player = server.Services.GetRequiredService<PlayerRegistry>().All.First(p => p.Id.Value == selfId);
            var map = world.GetInstance(player.MapInstanceId)!;
            var master = map.Actors.Values.OfType<Npc>().First(n => n.NpcKind == "class_change");
            player.Position = master.Position + new Vec2(1, 0);
            await ana.ExpectForIdAsync("EntitySpawn", master.Id.Value);
            await ana.SendAsync("ChangeClass", $$"""{"npcId":{{master.Id.Value}},"classId":"priest","reqId":4}""");
            var welcome = await ana.ExpectAsync("Welcome", 3000);
            welcome.GetProperty("self").GetProperty("classId").GetString().ShouldBe("priest");
            welcome.GetProperty("self").GetProperty("resource").GetString().ShouldBe("mana");
            welcome.GetProperty("knownSpells").EnumerateArray().Select(s => s.GetString()).ShouldContain("priest_heal");
            welcome.GetProperty("hotbar").EnumerateArray().First().GetProperty("ref").GetString().ShouldBe("priest_heal");
            welcome.GetProperty("equipment").EnumerateArray().Count(e => e.ValueKind != JsonValueKind.Null).ShouldBe(3); // conserva el equipo
            await ana.SendAsync("ChangeClass", $$"""{"npcId":{{master.Id.Value}},"classId":"priest","reqId":5}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_payload");
        }
    }
}
