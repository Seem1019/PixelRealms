using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Social;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Social;

/// <summary>HU-060 chat, HU-061 grupos, HU-062 XP de grupo, HU-064 duelos, HU-044 cambio de clase, HU-059 intercambio.</summary>
public sealed class SocialTests
{
    private static TestWorld Arena(int level = 5)
        => new WorldBuilder().WithMap(60, 60).WithPlayer("Ana", "warrior", level, (10, 10)).WithPlayer("Bob", "mage", level, (11, 10))
            .WithPlayer("Cid", "priest", level, (12, 10)).WithMonster("slime", (30, 30), wanderRadius: 0).BuildWithCombat();

    private static Party MakeParty(TestWorld w, params string[] names)
    {
        var rules = w.Content.Rules.Group;
        var leader = w.Player(names[0]);
        Party? party = null;
        foreach (var n in names.Skip(1))
        {
            var p = w.Player(n);
            w.Combat.Parties.Invite(leader, p, 0, rules).ShouldBeNull();
            var (pt, err) = w.Combat.Parties.Respond(p, true, id => w.Map.Players.Values.FirstOrDefault(x => x.CharacterId == id), 0, rules);
            err.ShouldBeNull();
            party = pt;
        }
        return party!;
    }

    [Fact]
    public void Party_Invite_Accept_Leader_Leave_Kick_Disband_MaxMembers() // HU-061
    {
        var w = Arena();
        var rules = w.Content.Rules.Group;
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid");
        var party = MakeParty(w, "Ana", "Bob", "Cid");
        party.Leader.ShouldBe(ana.CharacterId);
        party.Members.Count.ShouldBe(3);
        w.Combat.Parties.PartyOf(cid.CharacterId).ShouldBe(party);
        // Solo el líder invita; invitar a alguien ya agrupado falla.
        w.Combat.Parties.Invite(bob, ana, 0, rules).ShouldBe("forbidden");
        // Expulsar
        w.Combat.Parties.Kick(ana.CharacterId, "cid", out var after, out var kicked).ShouldBeNull();
        kicked.ShouldBe(cid.CharacterId);
        after!.Members.Count.ShouldBe(2);
        w.Combat.Parties.Kick(bob.CharacterId, "ana", out _, out _).ShouldBe("forbidden");
        // El líder se va: hereda el siguiente → con 1 se disuelve.
        var (p2, disbanded, last) = w.Combat.Parties.Leave(ana.CharacterId);
        disbanded.ShouldBeTrue(); p2.ShouldBeNull(); last.ShouldBe(bob.CharacterId);
        w.Combat.Parties.PartyOf(bob.CharacterId).ShouldBeNull();
        // Invitación caduca.
        w.Combat.Parties.Invite(ana, bob, 0, rules).ShouldBeNull();
        w.Combat.Parties.Respond(bob, true, _ => ana, (long)(rules.InviteExpireSec * 1000) + 1, rules).Error.ShouldBe("not_found");
        // Desconectado 5 min → fuera.
        var party2 = MakeParty(w, "Ana", "Bob", "Cid");
        w.Combat.Parties.SetOnline(cid.CharacterId, false, 1000);
        w.Combat.Parties.Tick(1000 + (long)(rules.OfflineGraceSec * 1000) - 1, rules).ShouldBeEmpty();
        var changes = w.Combat.Parties.Tick(1000 + (long)(rules.OfflineGraceSec * 1000), rules);
        changes.Count.ShouldBe(1);
        party2.Members.Count.ShouldBe(2);
    }

    [Fact]
    public void GroupXp_Example_10_8_5_vs_Normal9() // HU-062 CA2b
    {
        var rules = TestContent.Load().Rules;
        var monster = TestContent.Load().Monster("slime") with { Level = 9, Type = MonsterType.Normal };
        var shares = GroupXp.Split(rules.Progression, rules.Group, monster, [10, 8, 5]);
        shares[0].ShouldBe(26.5, 0.05);
        shares[1].ShouldBe(26.5, 0.05);
        shares[2].ShouldBe(11.2, 0.05);
        GroupXp.Weight(rules.Group, 10, 5).ShouldBe(Math.Pow(0.75, 3), 1e-9);
        GroupXp.Weight(rules.Group, 15, 1).ShouldBe(rules.Group.LevelGapMinWeight);
    }

    [Fact]
    public void GroupXp_InWorld_DeadOrFarMembersExcluded() // HU-062 CA2
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid"); var slime = w.Monster("slime");
        MakeParty(w, "Ana", "Bob", "Cid");
        slime.Position = new Vec2(13, 10);
        var now = 10_000L;
        foreach (var p in new[] { ana, bob, cid }) p.LastActionAtMs = now;
        cid.Position = new Vec2(13 + (float)w.Content.Rules.Group.XpRangeTiles + 1, 10); // a 41 casillas: no cuenta
        bob.Hp = 0; bob.Combat.DiedAtMs = now; // muerto: no cuenta
        ana.LastCombatAtMs = now;
        var rules = w.Content.Rules;
        var recipients = w.Combat.Progression.XpRecipients(ana, slime, w.Map, rules);
        recipients.Select(r => r.Player).ToArray().ShouldBe(new[] { ana });
        recipients[0].Xp.ShouldBe((int)Math.Round(GroupXp.Split(rules.Progression, rules.Group, slime.Template, [ana.Level])[0]));
    }

    [Fact]
    public void Duel_Request_Accept_Countdown_Active_EndsAtHpPct_Restores() // HU-064 CA1-CA3, CA7
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var rules = w.Content.Rules;
        var rs = rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.CanAttack(ana, bob, rules).ShouldBeNull(); // sin duelo → null
        w.Combat.Services.IsEnemy(ana, bob).ShouldBeFalse();
        var ctx = w.Begin();
        w.Combat.Pvp.Request(ana, bob, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<DuelChangedEvent>().Single().State.ShouldBe("requested");
        w.Combat.Pvp.Respond(bob, true, w.Map, ctx).ShouldBeNull();
        w.Combat.Pvp.CanAttack(ana, bob, rules).ShouldBeNull(); // cuenta atrás: aún no
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Pvp.CanAttack(ana, bob, rules).ShouldNotBeNull();
        w.Combat.Services.IsEnemy(ana, bob).ShouldBeTrue();
        w.Combat.Services.IsEnemy(ana, slime).ShouldBeFalse(); // duelista no ataca monstruos (CA5)
        w.Combat.Services.IsEnemy(slime, ana).ShouldBeFalse();
        // Los `ally` no aceptan al rival: un hechizo de cura del Sacerdote sobre su rival va a sí mismo (ya cubierto por IsAlly).
        w.Combat.Services.IsAlly(ana, bob).ShouldBeFalse();
        // Daño con classAdvantage y recorte en endAtHpPct: nadie muere, se restauran y termina.
        bob.Hp = 10;
        var ctx2 = w.Begin();
        var applied = w.Combat.Damage.Deal(ana, bob, 999, School.Physical, false, null, w.Map, ctx2);
        bob.IsDead.ShouldBeFalse();
        var ended = ctx2.Events.OfType<DuelChangedEvent>().Single(e => e.State == "ended");
        ended.Duel.Winner.ShouldBe(ana);
        bob.Hp.ShouldBe(bob.MaxHp); ana.Resource.ShouldBe(ana.MaxResource);
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
        _ = applied;
        // enabledRulesets vacío → siempre null.
        var noPvp = TestContent.Load().Rules with { Pvp = rules.Pvp with { EnabledRulesets = [] } };
        w.Combat.Pvp.CanAttack(ana, bob, noPvp).ShouldBeNull();
    }

    [Fact]
    public void Duel_Forfeit_Distance_Expiry() // HU-064 CA4
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        var ctx = w.Begin();
        w.Combat.Pvp.Request(ana, bob, w.Map, ctx).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.RequestExpireSec * 1000) + 100).OfType<DuelChangedEvent>().ShouldContain(e => e.State == "declined");
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();

        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        var ctxF = w.Begin();
        w.Combat.Pvp.Forfeit(bob, w.Map, ctxF).ShouldBeNull();
        ctxF.Events.OfType<DuelChangedEvent>().Single().Duel.Winner.ShouldBe(ana);

        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        bob.Position = new Vec2(11 + (float)rs.MaxDistanceTiles + 2, 10);
        var ev = TickRunner.Run(w, 1).OfType<DuelChangedEvent>().Single(e => e.State == "ended");
        ev.Duel.Winner.ShouldBe(ana); // el que se alejó pierde
    }

    [Fact]
    public void Chat_Channels_RateLimit_Sanitize() // HU-060 CA2-CA5
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid");
        foreach (var p in new[] { ana, bob, cid }) p.ConnectionId = p.Id.Value;
        var all = w.Map.Players.Values.ToList();
        cid.Position = new Vec2(10 + (float)w.Content.Rules.Movement.SayRangeTiles + 1, 10); // fuera del say
        var ctx = w.Begin();
        w.Combat.Chat.Send(ana, "say", "hola", null, w.Map, all, _ => null, ctx).ShouldBeNull();
        var say = ctx.Events.OfType<ChatDeliveredEvent>().Single();
        say.Recipients.ShouldBe(new[] { ana, bob }, ignoreOrder: true);
        w.Combat.Chat.Send(ana, "global", "a todos", null, w.Map, all, _ => null, ctx).ShouldBeNull();
        ctx.Events.OfType<ChatDeliveredEvent>().Last().Recipients.Count.ShouldBe(3);
        w.Combat.Chat.Send(ana, "whisper", "psst", "cid", w.Map, all, _ => null, ctx).ShouldBeNull();
        ctx.Events.OfType<ChatDeliveredEvent>().Last().Recipients.ShouldBe(new[] { cid, ana }, ignoreOrder: true);
        w.Combat.Chat.Send(ana, "whisper", "psst", "nadie", w.Map, all, _ => null, ctx).ShouldBe("not_found");
        w.Combat.Chat.Send(ana, "party", "grupo", null, w.Map, all, _ => null, ctx).ShouldBe("not_found");
        // Rate limit: 5 por 5 s (solo cuentan los aceptados: ya van 3).
        w.Combat.Chat.Send(ana, "say", "4", null, w.Map, all, _ => null, ctx).ShouldBeNull();
        w.Combat.Chat.Send(ana, "say", "5", null, w.Map, all, _ => null, ctx).ShouldBeNull();
        w.Combat.Chat.Send(ana, "say", "6", null, w.Map, all, _ => null, ctx).ShouldBe("rate_limited");
        w.Clock.Advance(ChatService.RateLimitWindowMs);
        w.Combat.Chat.Send(ana, "say", "7", null, w.Map, all, _ => null, w.Begin()).ShouldBeNull();
        ChatService.Sanitize("   ").ShouldBeNull();
        ChatService.Sanitize("ho\u0007la").ShouldBe("hola");
        ChatService.Sanitize(new string('x', 250))!.Length.ShouldBe(200);
    }

    [Fact]
    public void ClassChange_KeepsLevelItemsGold_ChangesSpellsAndStats_Errors() // HU-044
    {
        var data = new PixelRealms.Game.Map.MapData("t", "T", new PixelRealms.Game.Map.CollisionGrid(40, 40), [], [new PixelRealms.Game.Map.NpcDef("maestro", "Maestro", null, "class_change", new Vec2(12, 10))],
            [new PixelRealms.Game.Map.GraveyardDef("gy", new Vec2(2, 2))], [], [], "gy");
        var w = new WorldBuilder().WithMap(data).WithPlayer("Ana", "warrior", 5, (10, 10)).WithMonster("slime", (30, 30), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana");
        var npc = new Npc(w.World.EntityIds.Next(), data.Npcs[0]) { Position = data.Npcs[0].Position };
        w.Map.Add(npc);
        ana.Gold = 777; ana.Xp = 50;
        var bag = ana.Inventory.Bag.Count(i => i is not null);
        var ctx = w.Begin();
        w.Combat.ClassChange.Change(ana, npc.Id, "warrior", w.Map, ctx).ShouldBe("invalid_payload"); // la misma clase
        w.Combat.ClassChange.Change(ana, npc.Id, "mage", w.Map, ctx).ShouldBeNull();
        ana.ClassId.ShouldBe("mage");
        ana.Level.ShouldBe(5); ana.Xp.ShouldBe(50); ana.Gold.ShouldBe(777);
        ana.Inventory.Bag.Count(i => i is not null).ShouldBe(bag);
        ana.Equipment.MainHand!.TemplateId.ShouldBe("worn_sword"); // conserva el equipo (afinidad baja ahora)
        ana.KnownSpells.ShouldBe(w.Content.KnownSpells("mage", 5).Select(s => s.Id).ToList());
        ana.Hotbar[0]!.Value.Ref.ShouldBe("mage_fireball");
        ana.Resource.ShouldBe(ana.MaxResource); ana.MaxResource.ShouldBeGreaterThan(100); // maná
        ctx.Events.OfType<ClassChangedEvent>().Single().OldClassId.ShouldBe("warrior");
        // Errores: en combate / muerto / lejos / fase
        ana.EnterCombat(w.Clock.NowMs);
        w.Combat.ClassChange.Change(ana, npc.Id, "rogue", w.Map, w.Begin()).ShouldBe("in_combat");
        ana.LastCombatAtMs = long.MinValue;
        ana.Position = new Vec2(20, 10);
        w.Combat.ClassChange.Change(ana, npc.Id, "rogue", w.Map, w.Begin()).ShouldBe("out_of_range");
        ana.Position = new Vec2(10, 10);
        w.Combat.Death.Kill(ana, null, w.Map, w.Begin());
        w.Combat.ClassChange.Change(ana, npc.Id, "rogue", w.Map, w.Begin()).ShouldBe("is_dead");
        var later = TestContent.Load().Rules with { World = TestContent.Load().Rules.World with { CurrentPhase = 3 } };
        w.Combat.ClassChange.IsAvailable(later).ShouldBeFalse();
    }

    [Fact]
    public void Trade_Request_Offer_Confirm_Atomic_Conservation_Cancel() // HU-059
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var db = w.Content;
        ana.Gold = 100; bob.Gold = 50;
        var sword = ItemInstance.New("iron_sword"); ana.Inventory.Bag[0] = sword;
        var bread = ItemInstance.New("bread", 10); ana.Inventory.Bag[1] = bread;
        var potion = ItemInstance.New("minor_healing_potion", 3); bob.Inventory.Bag[0] = potion;
        Dictionary<string, int> Totals() { var d = new Dictionary<string, int>(); foreach (var it in ana.Inventory.Bag.Concat(bob.Inventory.Bag)) if (it is not null) d[it.TemplateId] = d.GetValueOrDefault(it.TemplateId) + it.Qty; return d; }
        var before = Totals(); var goldBefore = ana.Gold + bob.Gold;

        var ctx = w.Begin();
        w.Combat.Trades.Request(ana, bob, w.Map, ctx).ShouldBeNull();
        w.Combat.Trades.Respond(bob, true, w.Map, ctx).ShouldBeNull();
        var trade = w.Combat.Trades.TradeOf(ana)!;
        w.Combat.Trades.Offer(ana, [(sword.Id, 1), (bread.Id, 4)], 30, w.Map, ctx).ShouldBeNull();
        var v1 = trade.Version;
        w.Combat.Trades.Confirm(ana, v1, w.Map, ctx).ShouldBeNull();
        trade.ConfirmedA.ShouldBeTrue();
        w.Combat.Trades.Offer(bob, [(potion.Id, 3)], 0, w.Map, ctx).ShouldBeNull();
        trade.ConfirmedA.ShouldBeFalse(); // cambio de oferta desmarca
        trade.Version.ShouldBe(v1 + 1);
        w.Combat.Trades.Confirm(bob, v1, w.Map, ctx).ShouldBe("trade_version");
        w.Combat.Trades.IsLocked(ana, sword.Id).ShouldBeTrue();
        w.Combat.Trades.Confirm(ana, trade.Version, w.Map, ctx).ShouldBeNull();
        w.Combat.Trades.Confirm(bob, trade.Version, w.Map, ctx).ShouldBeNull();
        trade.State.ShouldBe(TradeState.Completed);
        bob.Inventory.Bag.Count(i => i?.TemplateId == "iron_sword").ShouldBe(1);
        bob.Inventory.Bag.First(i => i?.TemplateId == "bread")!.Qty.ShouldBe(4);
        ana.Inventory.Bag.First(i => i?.TemplateId == "bread")!.Qty.ShouldBe(6);
        ana.Inventory.Bag.Count(i => i?.TemplateId == "minor_healing_potion").ShouldBe(1);
        ana.Gold.ShouldBe(70); bob.Gold.ShouldBe(80);
        var after = Totals();
        after.Count.ShouldBe(before.Count);
        foreach (var (k, v) in before) after[k].ShouldBe(v);
        (ana.Gold + bob.Gold).ShouldBe(goldBefore);
        ana.Inventory.Bag.Concat(bob.Inventory.Bag).Where(i => i is not null).Select(i => i!.Id).Distinct().Count().ShouldBe(ana.Inventory.Bag.Concat(bob.Inventory.Bag).Count(i => i is not null));
        ana.PendingAudit.ShouldContain(a => a.Action == "trade_out" && a.CounterpartyCharacterId == bob.CharacterId);
        bob.PendingAudit.ShouldContain(a => a.Action == "trade_in" && a.CounterpartyCharacterId == ana.CharacterId);

        // Cancelación por distancia y espacio insuficiente.
        w.Combat.Trades.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Trades.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        bob.Position = new Vec2(20, 10);
        TickRunner.Run(w, 1).OfType<TradeChangedEvent>().Single().State.ShouldBe("cancelled");
        bob.Position = new Vec2(11, 10);
        for (var i = 0; i < Inventory.BagSize; i++) bob.Inventory.Bag[i] ??= ItemInstance.New("copper_ore", 50);
        w.Combat.Trades.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Trades.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        var t2 = w.Combat.Trades.TradeOf(ana)!;
        var potionNow = ana.Inventory.Bag.First(i => i?.TemplateId == "minor_healing_potion")!; // Bob no tiene stack de pociones ni huecos
        w.Combat.Trades.Offer(ana, [(potionNow.Id, 1)], 0, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Trades.Confirm(ana, t2.Version, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Trades.Confirm(bob, t2.Version, w.Map, w.Begin()).ShouldBe("bag_full");
        t2.State.ShouldBe(TradeState.Open); // nadie perdió nada
        potionNow.Qty.ShouldBe(3);
    }
}
