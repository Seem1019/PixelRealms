using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Net;

/// <summary>Combate de extremo a extremo por WebSocket: selección, básico, hechizo, lote CombatEvents, muerte y reaparición.</summary>
public sealed class CombatFlowTests
{
    private static async Task<(TestGameClient Client, int SelfId)> Enter(TestServer server, string user, string name, string classId)
    {
        using var api = await new ApiClient(server).RegisterAndLogin(user);
        var id = await api.CreateCharacterId(name, classId);
        var client = await TestGameClient.ConnectAsync(server.WsUrl);
        await client.SendAsync("Hello", $$"""{"protocolVersion":1,"ticket":"{{await api.Ticket(id)}}"}""");
        var w = await client.ExpectAsync("Welcome");
        return (client, w.GetProperty("selfId").GetInt32());
    }

    /// <summary>Teletransporta al jugador junto a un monstruo (hilo del tick no: solo tests, antes de que el jugador se mueva).</summary>
    private static (int MonsterId, float X, float Y) PlaceNextToMonster(TestServer server, int selfId, string templateId)
    {
        var world = server.Services.GetRequiredService<World>();
        var registry = server.Services.GetRequiredService<PlayerRegistry>();
        var player = registry.All.First(p => p.Id.Value == selfId);
        var map = world.GetInstance(player.MapInstanceId)!;
        var monster = map.Monsters.Values.First(m => m.TemplateId == templateId && m.IsAlive);
        player.Position = new Vec2(monster.Position.X - 1f, monster.Position.Y);
        return (monster.Id.Value, monster.Position.X, monster.Position.Y);
    }

    [Fact]
    public async Task SelectTarget_AutoAttack_KillsSlime_CombatEventsBatched()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, selfId) = await Enter(server, "ana", "Ana", "mage"); // bastón: alcance 5, el slime puede pasear
        await using var _ = ana;
        var (slimeId, _, _) = PlaceNextToMonster(server, selfId, "slime");
        await ana.ExpectForIdAsync("EntitySpawn", slimeId);
        await ana.SendAsync("SelectTarget", $$"""{"targetId":{{slimeId}}}""");
        await ana.SendAsync("AutoAttack", """{"on":true}""");
        var seenDamage = false;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            await ana.SendAsync("Ping", """{"clientTime":0}"""); // como el cliente real: sin tráfico propio el servidor cierra por idle_timeout (15 s)
            if (await ana.ArrivesAsync("CombatEvents", b =>
                    b.GetProperty("e").GetArrayLength() <= 64 && b.GetProperty("tick").GetInt64() > 0 &&
                    b.GetProperty("e").EnumerateArray().Any(e => e.GetProperty("src").GetInt32() == selfId && e.GetProperty("dst").GetInt32() == slimeId && e.GetProperty("kind").GetString() == "dmg"), 2000))
                seenDamage = true;
            var snap = await ana.LatestAsync("Snapshot");
            var slime = snap.GetProperty("ents").EnumerateArray().FirstOrDefault(e => e.GetProperty("id").GetInt32() == slimeId);
            if (slime.ValueKind != JsonValueKind.Undefined && slime.GetProperty("hpPct").GetInt32() == 0) break;
        }
        seenDamage.ShouldBeTrue();
        var final = await ana.LatestAsync("Snapshot");
        final.GetProperty("ents").EnumerateArray().First(e => e.GetProperty("id").GetInt32() == slimeId).GetProperty("anim").GetString().ShouldBe("dead");
    }

    [Fact]
    public async Task CastSpell_Fireball_CastStartedEnded_Cooldown_AndErrors()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, selfId) = await Enter(server, "ana", "Ana", "mage");
        await using var _ = ana;
        var (slimeId, _, _) = PlaceNextToMonster(server, selfId, "slime");
        await ana.ExpectForIdAsync("EntitySpawn", slimeId);

        await ana.SendAsync("CastSpell", $$"""{"spellId":"mage_fireball","targetId":{{slimeId}},"reqId":7}""");
        var started = await ana.ExpectAsync("CastStarted");
        started.GetProperty("casterId").GetInt32().ShouldBe(selfId);
        started.GetProperty("spellId").GetString().ShouldBe("mage_fireball");
        started.GetProperty("durationMs").GetInt32().ShouldBe(2000);
        (await ana.ExpectAsync("Cooldown")).GetProperty("gcdMs").GetInt32().ShouldBe(TestContent.Load().Rules.Combat.GcdMs);
        // Durante el GCD, otro hechizo → on_gcd con reqId
        await ana.SendAsync("CastSpell", $$"""{"spellId":"mage_fireball","targetId":{{slimeId}},"reqId":8}""");
        var err = await ana.ExpectAsync("Error");
        err.GetProperty("code").GetString().ShouldBe("on_gcd");
        err.GetProperty("reqId").GetInt32().ShouldBe(8);
        var ended = await ana.ExpectAsync("CastEnded", 3500);
        ended.GetProperty("result").GetString().ShouldBe("done");
        var hit = await ana.ExpectAsync("CombatEvents", m => m.GetProperty("e").EnumerateArray().Any(e => e.GetProperty("spellId").GetString() == "mage_fireball"), 3000);
        hit.GetProperty("e").EnumerateArray().First(e => e.GetProperty("spellId").GetString() == "mage_fireball").GetProperty("school").GetString().ShouldBe("magic");
        // Hechizo desconocido → not_found; sin maná → not_enough_resource con mensaje.
        await ana.SendAsync("CastSpell", """{"spellId":"nope","reqId":9}""");
        (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Fact]
    public async Task Death_SendsDied_RespawnRestoresAtGraveyard()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, selfId) = await Enter(server, "ana", "Ana", "mage");
        await using var _ = ana;
        var world = server.Services.GetRequiredService<World>();
        var registry = server.Services.GetRequiredService<PlayerRegistry>();
        var player = registry.All.First(p => p.Id.Value == selfId);
        player.Hp = 1; // la muerte la provoca el jabalí: lo colocamos encima y lo agitamos
        var (boarId, _, _) = PlaceNextToMonster(server, selfId, "boar");
        var died = await ana.ExpectAsync("Died", 15000);
        var killerId = died.GetProperty("killerId").GetInt32();
        world.GetInstance(player.MapInstanceId)!.Monsters.ContainsKey(killerId).ShouldBeTrue(); // lo mató un monstruo
        var snap = await ana.LatestAsync("Snapshot");
        snap.GetProperty("self").GetProperty("hp").GetInt32().ShouldBe(0);
        // Muerto no castea.
        await ana.SendAsync("CastSpell", $$"""{"spellId":"mage_fireball","targetId":{{boarId}}}""");
        (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("is_dead");
        await ana.SendAsync("Respawn");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var after = await ana.LatestAsync("Snapshot");
        var rules = TestContent.Load().Rules.Combat;
        var expectedHp = (int)Math.Round(player.MaxHp * rules.RespawnHpPct);
        after.GetProperty("self").GetProperty("hp").GetInt32().ShouldBeInRange(expectedHp, expectedHp + 10); // + regeneración fuera de combate
        var map = world.GetInstance(player.MapInstanceId)!;
        map.Data.Graveyards.ShouldContain(g => g.Position == player.Position);
    }
}
