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

    /// <summary>
    /// Teletransporta al jugador junto a un monstruo, en el hilo del tick (regla 2): escrito desde el test, el tick podía pisar
    /// la posición o la vida a mitad de su propia escritura.
    /// </summary>
    private static async Task<(int MonsterId, float X, float Y)> PlaceNextToMonster(TestServer server, int selfId, string templateId)
    {
        var world = server.Services.GetRequiredService<World>();
        var registry = server.Services.GetRequiredService<PlayerRegistry>();
        (int, float, float) placed = default;
        await server.RunOnTickAsync(_ =>
        {
            var player = registry.All.First(p => p.Id.Value == selfId);
            var map = world.GetInstance(player.MapInstanceId)!;
            var monster = map.Monsters.Values.First(m => m.TemplateId == templateId && m.IsAlive);
            player.Position = new Vec2(monster.Position.X - 1f, monster.Position.Y);
            placed = (monster.Id.Value, monster.Position.X, monster.Position.Y);
        });
        return placed;
    }

    [Fact]
    public async Task SelectTarget_AutoAttack_KillsSlime_CombatEventsBatched()
    {
        await using var server = await TestServer.StartAsync();
        var (ana, selfId) = await Enter(server, "ana", "Ana", "mage"); // bastón: alcance 5, el slime puede pasear
        await using var _ = ana;
        var (slimeId, _, _) = await PlaceNextToMonster(server, selfId, "slime");
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
        var (slimeId, _, _) = await PlaceNextToMonster(server, selfId, "slime");
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
    public async Task CastSpell_KnownButNotOnTheBar_IsNotEquipped() // ADR-014: solo los hechizos equipados
    {
        await using var server = await TestServer.StartAsync();
        var (ana, _) = await Enter(server, "ana", "Ana", "mage");
        await using var _ = ana;
        for (var slot = 0; slot < TestContent.Load().Rules.Loadout.SpellSlots; slot++)
            await ana.SendAsync("SetHotbar", $$"""{"slot":{{slot}}}""");
        await ana.SendAsync("CastSpell", """{"spellId":"mage_fireball","reqId":4}""");
        var err = await ana.ExpectAsync("Error");
        err.GetProperty("code").GetString().ShouldBe("not_equipped");
        err.GetProperty("reqId").GetInt32().ShouldBe(4);

        await ana.SendAsync("SetHotbar", """{"slot":2,"kind":"spell","ref":"mage_fireball"}""");
        await ana.SendAsync("CastSpell", """{"spellId":"mage_fireball","reqId":5}"""); // equipada: ya solo falta un objetivo
        (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("invalid_target");
    }

    [Fact]
    public async Task LevelUp_OthersSeeTheNewLevel_AndItIsSaved() // HU-041 CA3 (el número; el efecto visual es del cliente), HU-026 CA6
    {
        await using var server = await TestServer.StartAsync();
        var (ana, anaId) = await Enter(server, "ana", "Ana", "warrior");
        var (bob, _) = await Enter(server, "bob", "Bob", "mage");
        await using var _ = ana;
        await using var __ = bob;
        await bob.ExpectForIdAsync("EntitySpawn", anaId);
        var registry = server.Services.GetRequiredService<PlayerRegistry>();
        var combat = server.Services.GetRequiredService<CombatModule>();
        var world = server.Services.GetRequiredService<World>();
        var saver = server.Services.GetRequiredService<PixelRealms.Server.Hosting.SaveService>();
        var anaPlayer = registry.All.First(p => p.Id.Value == anaId);
        var savedBefore = saver.Saved;
        await server.RunOnTickAsync(t =>
            combat.Progression.GrantXp(anaPlayer, PixelRealms.Game.Progression.XpCurve.XpToNextLevel(combat.Services.Content.Rules.Progression, 1), null, world.GetInstance(anaPlayer.MapInstanceId)!, t));
        var spawn = await bob.ExpectAsync("EntitySpawn", m => m.GetProperty("id").GetInt32() == anaId && m.GetProperty("level").GetInt32() == 2);
        spawn.GetProperty("name").GetString().ShouldBe("Ana");
        for (var i = 0; i < 40 && saver.Saved == savedBefore; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        saver.Saved.ShouldBeGreaterThan(savedBefore);
    }

    [Fact]
    public async Task EnteringTheAoi_BringsTheAurasAlreadyOnTheEntity() // HU-035 CA6 / HU-098 CA2
    {
        await using var server = await TestServer.StartAsync();
        var (ana, anaId) = await Enter(server, "ana", "Ana", "priest");
        await using var _ = ana;
        var registry = server.Services.GetRequiredService<PlayerRegistry>();
        var combat = server.Services.GetRequiredService<CombatModule>();
        var world = server.Services.GetRequiredService<World>();
        var anaPlayer = registry.All.First(p => p.Id.Value == anaId);
        await server.RunOnTickAsync(t =>
            combat.Auras.Apply(anaPlayer, combat.Services.Content.Aura("priest_renew_hot"), anaPlayer, world.GetInstance(anaPlayer.MapInstanceId)!, t));

        var (bob, _) = await Enter(server, "bob", "Bob", "mage"); // mismo cementerio: Ana ya está en su AOI con el aura puesta
        await using var __ = bob;
        await bob.ExpectForIdAsync("EntitySpawn", anaId);
        var aura = await bob.ExpectAsync("AuraApplied", m => m.GetProperty("targetId").GetInt32() == anaId);
        aura.GetProperty("auraId").GetString().ShouldBe("priest_renew_hot");
        aura.GetProperty("durationMs").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SelectTarget_OnlyKeepsEntitiesOfTheSameMap() // HU-030 CA4: un id inventado no se guarda
    {
        await using var server = await TestServer.StartAsync();
        var (ana, selfId) = await Enter(server, "ana", "Ana", "mage");
        await using var _ = ana;
        var player = server.Services.GetRequiredService<PlayerRegistry>().All.First(p => p.Id.Value == selfId);
        var (slimeId, _, _) = await PlaceNextToMonster(server, selfId, "slime");
        await ana.SendAsync("SelectTarget", $$"""{"targetId":{{slimeId}}}""");
        await ana.SendAsync("Ping", """{"clientTime":0}""");
        await ana.ExpectAsync("Pong");
        int? target = null;
        await server.RunOnTickAsync(_ => target = player.Combat.TargetId?.Value);
        target.ShouldBe(slimeId);
        await ana.SendAsync("SelectTarget", """{"targetId":987654}""");
        await ana.SendAsync("Ping", """{"clientTime":0}""");
        await ana.ExpectAsync("Pong");
        await server.RunOnTickAsync(_ => target = player.Combat.TargetId?.Value);
        target.ShouldBeNull();
    }

    [Fact]
    public async Task CastSpell_WithAPotionOrMonsterSpell_IsRejected_AndHealsNothing()
    {
        // Cliente tramposo: el hechizo de la poción por CastSpell no tiene recarga, coste ni GCD.
        await using var server = await TestServer.StartAsync();
        var (ana, selfId) = await Enter(server, "ana", "Ana", "warrior");
        await using var _ = ana;
        var player = server.Services.GetRequiredService<PlayerRegistry>().All.First(p => p.Id.Value == selfId);
        await server.RunOnTickAsync(_ => player.Hp = 10);
        foreach (var spellId in new[] { "item_minor_heal", "item_minor_mana", "lich_shadow_bolt", "foreman_rally" })
        {
            await ana.SendAsync("CastSpell", $$"""{"spellId":"{{spellId}}","targetId":{{selfId}},"reqId":1}""");
            (await ana.ExpectAsync("Error")).GetProperty("code").GetString().ShouldBe("not_found");
        }
        var hp = 0;
        await server.RunOnTickAsync(_ => hp = player.Hp);
        hp.ShouldBeLessThan(40); // como mucho la regeneración fuera de combate, nunca 4 curas de 60
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
        var saver = server.Services.GetRequiredService<PixelRealms.Server.Hosting.SaveService>();
        var savedBefore = saver.Saved;
        await server.RunOnTickAsync(_ => player.Hp = 1); // la muerte la provoca el jabalí: lo colocamos encima y lo agitamos
        var (boarId, _, _) = await PlaceNextToMonster(server, selfId, "boar");
        var died = await ana.ExpectAsync("Died", 15000);
        for (var i = 0; i < 40 && saver.Saved == savedBefore; i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        saver.Saved.ShouldBeGreaterThan(savedBefore); // HU-026 CA6: morir guarda
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
