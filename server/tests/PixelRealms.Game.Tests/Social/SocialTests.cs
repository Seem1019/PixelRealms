using PixelRealms.Content;
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
    public void Party_MaxFiveMembers_LeaderLeavingHandsOver_AndAStaleInviteIsRefused() // HU-061 CA2, CA3
    {
        var w = new WorldBuilder().WithMap(60, 60).WithPlayer("P1", "warrior", 5, (10, 10)).WithPlayer("P2", "mage", 5, (11, 10))
            .WithPlayer("P3", "priest", 5, (12, 10)).WithPlayer("P4", "rogue", 5, (13, 10)).WithPlayer("P5", "mage", 5, (14, 10))
            .WithPlayer("P6", "priest", 5, (15, 10)).WithPlayer("Zed", "rogue", 5, (16, 10)).BuildWithCombat();
        var rules = w.Content.Rules.Group;
        rules.MaxMembers.ShouldBe(5);
        var party = MakeParty(w, "P1", "P2", "P3", "P4", "P5");
        party.Members.Count.ShouldBe(5);
        w.Combat.Parties.Invite(w.Player("P1"), w.Player("P6"), 0, rules).ShouldBe("forbidden"); // lleno: el 6.º no entra

        // El líder se va con ≥ 3: hereda el siguiente y el grupo sigue.
        var (after, disbanded, _) = w.Combat.Parties.Leave(w.Player("P1").CharacterId);
        disbanded.ShouldBeFalse();
        after!.Leader.ShouldBe(w.Player("P2").CharacterId);
        after.Members.Count.ShouldBe(4);

        // Invitación vieja: Zed invitó a P6 y luego Zed entró en el grupo de P2; aceptar no mete a P6 sin permiso de P2.
        w.Combat.Parties.Invite(w.Player("Zed"), w.Player("P6"), 0, rules).ShouldBeNull();
        w.Combat.Parties.Invite(w.Player("P2"), w.Player("Zed"), 0, rules).ShouldBeNull();
        w.Combat.Parties.Respond(w.Player("Zed"), true, id => w.Map.Players.Values.FirstOrDefault(x => x.CharacterId == id), 0, rules).Error.ShouldBeNull();
        w.Combat.Parties.Respond(w.Player("P6"), true, id => w.Map.Players.Values.FirstOrDefault(x => x.CharacterId == id), 0, rules).Error.ShouldBe("forbidden");
        w.Combat.Parties.PartyOf(w.Player("P6").CharacterId).ShouldBeNull();
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
    public void Duel_Request_Accept_Countdown_Active_EndsAtHpPct_NobodyIsRestored() // HU-064 CA1-CA3, CA7
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
        // Daño con classAdvantage y recorte en endAtHpPct: nadie muere y termina; cada uno se queda como acabó.
        bob.Hp = 10;
        var ctx2 = w.Begin();
        var applied = w.Combat.Damage.Deal(ana, bob, 999, School.Physical, false, null, w.Map, ctx2);
        bob.IsDead.ShouldBeFalse();
        var ended = ctx2.Events.OfType<DuelChangedEvent>().Single(e => e.State == "ended");
        ended.Duel.Winner.ShouldBe(ana);
        bob.Hp.ShouldBe(Math.Max(1, (int)Math.Ceiling(bob.MaxHp * rs.EndAtHpPct))); // HU-064 CA3: al umbral, sin restaurar
        bob.Combat.Recovery.ShouldNotBeNull(); // el perdedor se recupera más rápido
        ana.Combat.Recovery.ShouldBeNull();
        ana.Resource.ShouldBeLessThan(ana.MaxResource); // ira: la que llevaba, no el máximo
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
        _ = applied;
        // enabledRulesets vacío → siempre null.
        var noPvp = TestContent.Load().Rules with { Pvp = rules.Pvp with { EnabledRulesets = [] } };
        w.Combat.Pvp.CanAttack(ana, bob, noPvp).ShouldBeNull();
    }

    [Fact]
    public void Duel_EndedByALanding_WhileTheRivalIsInTheAir_DoesNotBreakTheTick() // HU-064 + HU-087
    {
        // 0.99: golpe normal (ni fallo, ni esquiva, ni crítico).
        var w = new WorldBuilder().WithRng(new FixedRng(0.99)).WithMap(60, 60)
            .WithPlayer("Ana", "warrior", 13, (10.5f, 10.5f)).WithPlayer("Bob", "rogue", 11, (11.5f, 10.5f)).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Pvp.InActiveDuel(ana).ShouldBeTrue();
        bob.Hp = (int)Math.Ceiling(bob.MaxHp * rs.EndAtHpPct) + 1; // el golpe del aterrizaje de Ana lo deja en el umbral
        // Ana aterriza este tick con Golpe poderoso sobre Bob, que sigue en el aire; Ana va antes en el recorrido.
        ana.Combat.Flight = new LeapFlight(w.Content.Spell("warrior_mighty_blow"), ana.Position, bob.Position, 0, 0);
        bob.Combat.Flight = new LeapFlight(w.Content.Spell("rogue_shadowstep"), bob.Position, bob.Position + new Vec2(3, 0), 0, 60_000);

        Should.NotThrow(() => TickRunner.Run(w, 1));
        w.Combat.Pvp.InActiveDuel(ana).ShouldBeFalse();
        bob.Combat.Flight.ShouldBeNull();
        bob.IsDead.ShouldBeFalse();
    }

    [Fact]
    public void Duel_And_Trade_ExcludeEachOther() // prueba de juego: se podía intercambiar en pleno duelo
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Trades.Request(bob, ana, w.Map, w.Begin()).ShouldBe("duel_busy"); // reto pendiente
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Pvp.InActiveDuel(ana).ShouldBeTrue();
        w.Combat.Trades.Request(ana, bob, w.Map, w.Begin()).ShouldBe("duel_busy"); // en pleno duelo
        w.Combat.Trades.TradeOf(ana).ShouldBeNull();
        w.Combat.Pvp.Forfeit(bob, w.Map, w.Begin()).ShouldBeNull();

        // Y al revés: con un intercambio en curso no se puede retar a duelo.
        w.Combat.Trades.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Trades.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Request(bob, ana, w.Map, w.Begin()).ShouldBe("trade_busy");
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
    }

    [Fact]
    public void Duel_AcceptedAfterTheChallengerDied_IsDeclined_AndNobodyIsRevived() // revisión de autoridad: resurrección gratis
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        ana.Hp = 0; // la mata un monstruo antes de que Bob acepte
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBe("is_dead");
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
        TickRunner.RunMs(w, 5000);
        ana.Hp.ShouldBe(0); // antes: viva y con la vida al máximo al terminar el duelo, sin pasar por Respawn
    }

    [Fact]
    public void Duel_WhereSomeoneDiesDuringTheCountdown_IsCancelled_AndTheDeadStayDead()
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        ana.Hp = 0;
        TickRunner.Run(w, 1).OfType<DuelChangedEvent>().ShouldContain(e => e.State == "declined");
        w.Combat.Pvp.DuelOf(bob).ShouldBeNull();
        ana.Hp.ShouldBe(0);
    }

    [Fact]
    public void Duel_EndingBecauseSomeoneDied_DoesNotRestoreTheDead()
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Pvp.InActiveDuel(ana).ShouldBeTrue();
        ana.Hp = 0; // muere por otra causa en pleno duelo
        TickRunner.Run(w, 1).OfType<DuelChangedEvent>().ShouldContain(e => e.State == "ended");
        ana.Hp.ShouldBe(0);
        bob.Hp.ShouldBe(bob.MaxHp); // el vivo sigue como estaba (no recibió daño) y, como ganó, sin recuperación especial
        bob.Combat.Recovery.ShouldBeNull();
    }

    [Fact]
    public void AttackingAPlayerOutsideADuel_IsPvpNotAllowed() // HU-064 CA6
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        bob.Position = new Vec2(11, 10);
        ana.Resource = ana.MaxResource; // ira: el coste se valida antes que el objetivo
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("warrior_heroic_strike"), bob.Id, null, w.Map, w.Begin()).ShouldBe(CastErrors.PvpNotAllowed);
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("warrior_heroic_strike"), bob.Id, null, w.Map, w.Begin()).ShouldBe(CastErrors.PvpNotAllowed); // cuenta atrás
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("warrior_heroic_strike"), bob.Id, null, w.Map, w.Begin()).ShouldNotBe(CastErrors.PvpNotAllowed);
    }

    [Fact]
    public void Duel_ClassAdvantage_AppliesToBasicAttacksAndDots() // HU-064 CA2 (antes solo a hechizos)
    {
        // Neutro (1,0) frente a doble (2,0): el valor real del contenido lo decide el balance (HU-084), no este test.
        using var neutralTmp = new TempContent();
        neutralTmp.Patch("rules.json", n => n["classAdvantage"]!["warrior"]!["mage"] = 1.0);
        var neutral = ContentLoader.LoadOrThrow(neutralTmp.Path);
        using var tmp = new TempContent();
        tmp.Patch("rules.json", n => n["classAdvantage"]!["warrior"]!["mage"] = 2.0);
        var doubled = ContentLoader.LoadOrThrow(tmp.Path);

        var (basicNormal, dotNormal) = DuelHits(neutral);
        var (basicDoubled, dotDoubled) = DuelHits(doubled);
        basicNormal.ShouldBeGreaterThan(0);
        dotNormal.ShouldBeGreaterThan(0);
        basicDoubled.ShouldBeInRange(basicNormal * 2 - 1, basicNormal * 2 + 1);
        dotDoubled.ShouldBeInRange(dotNormal * 2 - 1, dotNormal * 2 + 1);
    }

    /// <summary>Guerrero contra Mago en duelo con tiradas fijas: daño del primer básico y del primer tick de un DoT físico.</summary>
    private static (int Basic, int Dot) DuelHits(ContentDb? content)
    {
        var w = new WorldBuilder(content).WithMap(40, 40).WithRng(new FixedRng(0.5)).WithPlayer("Ana", "warrior", 5, (10, 10))
            .WithPlayer("Bob", "mage", 5, (11, 10)).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Pvp.InActiveDuel(ana).ShouldBeTrue();
        ana.Combat.TargetId = bob.Id;
        ana.Combat.AutoAttackOn = true;
        var basic = TickRunner.RunMs(w, 4000).OfType<CombatHitEvent>().First(e => e.Source == ana && e.Target == bob && e.Kind == HitKinds.Damage).Amount;
        ana.Combat.AutoAttackOn = false;
        bob.Hp = bob.MaxHp;
        var bleed = w.Content.Aura("foreman_whip_bleed");
        w.Combat.Auras.Apply(bob, bleed, ana, w.Map, w.Begin());
        var dot = TickRunner.RunMs(w, bleed.TickMs + 100).OfType<CombatHitEvent>().First(e => e.SpellId == bleed.Id).Amount;
        return (basic, dot);
    }

    [Fact]
    public void Duel_ForfeitBeforeItStarts_WithdrawsTheChallenge_AndRestoresNobody() // revisión de autoridad: retar y rendirse curaba
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        ana.Hp = 10; bob.Hp = 10;
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        var ctx = w.Begin();
        w.Combat.Pvp.Forfeit(ana, w.Map, ctx).ShouldBeNull(); // reto sin aceptar
        ctx.Events.OfType<DuelChangedEvent>().Single().State.ShouldBe("declined");
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();

        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        var ctx2 = w.Begin();
        w.Combat.Pvp.Forfeit(bob, w.Map, ctx2).ShouldBeNull(); // en la cuenta atrás
        ctx2.Events.OfType<DuelChangedEvent>().Single().State.ShouldBe("declined");
        ana.Hp.ShouldBe(10); bob.Hp.ShouldBe(10);
    }

    [Fact]
    public void Duel_IsNotAnInn_EachOneEndsAsTheyFinished() // revisión de autoridad: retar a un alt y rendirse curaba al 100 %
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        ana.Hp = 20; bob.Resource = 5; // herida y sin maná de una pelea anterior (ya fuera de combate)
        var cid = w.Player("Cid");
        w.Combat.Auras.Apply(ana, w.Content.Aura("priest_power_shield_speed"), cid, w.Map, w.Begin()); // un beneficio de un tercero
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        var hpAtStart = ana.Hp;
        w.Combat.Auras.Apply(ana, w.Content.Aura("warrior_charge_stun"), bob, w.Map, w.Begin()); // lo que le pone el rival
        w.Combat.Pvp.Forfeit(ana, w.Map, w.Begin()).ShouldBeNull();
        ana.Hp.ShouldBe(hpAtStart);
        bob.Resource.ShouldBeLessThan(bob.MaxResource);
        ana.Auras.All.ShouldNotContain(a => a.AuraId == "warrior_charge_stun"); // se quita lo del rival
        ana.Auras.All.ShouldContain(a => a.AuraId == "priest_power_shield_speed"); // lo de otros sigue
    }

    [Fact]
    public void Duel_Loser_RecoversFasterAndWithoutTheDelay_TheWinnerRegeneratesNormally() // HU-064 CA3
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        var combat = w.Content.Rules.Combat;
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        ana.Hp = ana.MaxHp - 50; bob.Hp = 10;
        w.Combat.Damage.Deal(ana, bob, 999, School.Physical, false, null, w.Map, w.Begin()); // Ana gana: Bob queda al umbral
        var bobAtEnd = bob.Hp; var anaAtEnd = ana.Hp;

        TickRunner.RunMs(w, 1000);
        var expected = w.Combat.Services.StatsOf(bob).HpRegenPerSec * rs.LoserRegenMult; // por segundo, sin esperar
        (bob.Hp - bobAtEnd).ShouldBeInRange((int)expected - 1, (int)Math.Ceiling(expected) + 1);
        ana.Hp.ShouldBe(anaAtEnd); // el ganador espera hpRegenDelaySec como siempre

        // Volver a entrar en combate corta la recuperación: a partir de ahí, la regeneración normal (con su espera).
        bob.EnterCombat(w.Clock.NowMs);
        var bobBefore = bob.Hp;
        TickRunner.RunMs(w, 1000);
        bob.Combat.Recovery.ShouldBeNull();
        bob.Hp.ShouldBe(bobBefore);
        TickRunner.RunMs(w, (int)(combat.HpRegenDelaySec * 1000));
        ana.Hp.ShouldBeGreaterThan(anaAtEnd);
    }

    [Fact]
    public void Duel_LoserRecovery_OnlyGivesBackWhatTheDuelTook() // HU-064 CA3; revisión de autoridad: no es una posada
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        bob.Hp = bob.MaxHp * 6 / 10; // llega herido de una pelea anterior
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        var startHp = w.Combat.Pvp.DuelOf(bob).ShouldNotBeNull().StartB.Hp; // al activarse (en la cuenta atrás aún regenera)
        startHp.ShouldBeLessThan(bob.MaxHp);
        w.Combat.Damage.Deal(ana, bob, 999, School.Physical, false, null, w.Map, w.Begin());
        bob.Combat.Recovery.ShouldNotBeNull().UntilHp.ShouldBe(startHp);

        for (var i = 0; i < 600 && bob.Combat.Recovery is not null; i++) TickRunner.Run(w, 1);
        bob.Combat.Recovery.ShouldBeNull();
        bob.Hp.ShouldBeInRange(startHp, startHp + 1); // se corta al volver a la vida del inicio, no al máximo
    }

    [Fact]
    public void Duel_LostByForfeit_GivesNoRecovery() // revisión de autoridad: retar, rendirse y regenerar ×2 tras cada pelea
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        bob.Hp = bob.MaxHp / 2;
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Pvp.Forfeit(bob, w.Map, w.Begin()).ShouldBeNull();
        bob.Combat.Recovery.ShouldBeNull();
    }

    [Fact]
    public void ActiveDuelist_IsNoOnesAlly_AndHealingSomeoneInCombatPutsTheHealerInCombat() // revisión de autoridad: sanador intocable
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Services.IsAlly(cid, ana).ShouldBeTrue();
        w.Combat.Pvp.Request(cid, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        w.Combat.Services.IsAlly(cid, ana).ShouldBeFalse(); // el sacerdote en duelo no cura al grupo...
        w.Combat.Services.IsAlly(ana, cid).ShouldBeFalse(); // ...ni el grupo lo cura a él
        w.Combat.Pvp.Forfeit(cid, w.Map, w.Begin());

        ana.EnterCombat(w.Clock.NowMs); ana.Hp = 10;
        cid.IsInCombat(w.Clock.NowMs, w.Content.Rules.Combat.InCombatWindowSec).ShouldBeFalse();
        w.Combat.Damage.Heal(cid, ana, 20, false, null, w.Map, w.Begin());
        cid.IsInCombat(w.Clock.NowMs, w.Content.Rules.Combat.InCombatWindowSec).ShouldBeTrue();
        w.Combat.Pvp.Request(cid, bob, w.Map, w.Begin()).ShouldBe("in_combat");
    }

    [Fact]
    public void Duel_StrayingFromTheStartPoint_EndsIt() // revisión de autoridad: viajar juntos ignorados por los monstruos
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        var far = (float)rs.MaxDistanceTiles + 3;
        ana.Position = new Vec2(10.5f + far, 10); bob.Position = new Vec2(11.5f + far, 10); // juntos, lejos del punto de inicio
        TickRunner.Run(w, 1).OfType<DuelChangedEvent>().ShouldContain(e => e.State == "ended");
    }

    [Fact]
    public void Duel_InCombat_CannotBeRequestedNorAccepted_AndCombatCancelsTheCountdown()
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var now = w.Clock.NowMs;
        ana.LastCombatAtMs = now; // pelea con un monstruo
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBe("in_combat");
        w.Combat.Pvp.Request(bob, ana, w.Map, w.Begin()).ShouldBe("in_combat");
        ana.LastCombatAtMs = long.MinValue;

        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        bob.LastCombatAtMs = w.Clock.NowMs; // entra en combate antes de aceptar
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBe("in_combat");
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
        bob.LastCombatAtMs = long.MinValue;

        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        ana.LastCombatAtMs = w.Clock.NowMs; // la ataca un monstruo durante la cuenta atrás
        TickRunner.Run(w, 1).OfType<DuelChangedEvent>().ShouldContain(e => e.State == "declined" && e.Reason == "in_combat");
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
    }

    [Fact]
    public void Duel_AcceptedWhenTooFar_IsDeclined()
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        bob.Position = new Vec2(10 + (float)rs.MaxDistanceTiles + 2, 10);
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBe("out_of_range");
        w.Combat.Pvp.DuelOf(ana).ShouldBeNull();
    }

    [Fact]
    public void Duel_DamageWhoseSourceIsTheVictim_CountsAsTheOpponents() // DoT de un rival que salió del mapa
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rs = w.Content.Rules.Pvp.Rulesets["duel"];
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, (int)(rs.CountdownSec * 1000) + 50);
        bob.Hp = 5;
        var ctx = w.Begin();
        w.Combat.Damage.Deal(bob, bob, 999, School.Physical, false, null, w.Map, ctx);
        bob.IsDead.ShouldBeFalse();
        ctx.Events.OfType<DuelChangedEvent>().Single(e => e.State == "ended").Duel.Winner.ShouldBe(ana);
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
        w.Clock.Advance((long)(w.Content.Rules.Social.ChatRateLimitWindowSec * 1000));
        w.Combat.Chat.Send(ana, "say", "7", null, w.Map, all, _ => null, w.Begin()).ShouldBeNull();
        var maxLength = w.Content.Rules.Social.ChatMaxLength;
        ChatService.Sanitize("   ", maxLength).ShouldBeNull();
        ChatService.Sanitize("ho\u0007la", maxLength).ShouldBe("hola");
        ChatService.Sanitize(new string('x', maxLength + 50), maxLength)!.Length.ShouldBe(maxLength);
    }

    [Fact]
    public void Chat_AcrossMaps_SayStaysInTheMap_PartyAndGlobalArrive() // HU-027 CA5
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        // Bob cruza a otra instancia (la Mina en el juego real): misma posición, otro mapa.
        var other = w.World.CreateInstance(w.Map.MapId);
        w.Map.Remove(bob.Id);
        other.Add(bob);
        foreach (var p in new[] { ana, bob }) p.ConnectionId = p.Id.Value;
        var all = new List<Player> { ana, bob };
        var party = MakePartyAcrossMaps(w, ana, bob);
        var ctx = w.Begin();
        w.Combat.Chat.Send(ana, "say", "hola", null, w.Map, all, _ => party, ctx).ShouldBeNull();
        ctx.Events.OfType<ChatDeliveredEvent>().Last().Recipients.ShouldNotContain(bob);
        w.Combat.Chat.Send(ana, "party", "¿dónde estás?", null, w.Map, all, _ => party, ctx).ShouldBeNull();
        ctx.Events.OfType<ChatDeliveredEvent>().Last().Recipients.ShouldContain(bob);
        w.Combat.Chat.Send(ana, "global", "a todos", null, w.Map, all, _ => party, ctx).ShouldBeNull();
        ctx.Events.OfType<ChatDeliveredEvent>().Last().Recipients.ShouldContain(bob);
    }

    private static Party MakePartyAcrossMaps(TestWorld w, Player leader, Player member)
    {
        var rules = w.Content.Rules.Group;
        w.Combat.Parties.Invite(leader, member, 0, rules).ShouldBeNull();
        var (party, err) = w.Combat.Parties.Respond(member, true, _ => leader, 0, rules);
        err.ShouldBeNull();
        return party!;
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
    public void ClassChange_InADuelOrATrade_SaysWhich() // HU-044 CA3: duel_busy / trade_busy (antes ambos daban duel_busy)
    {
        var data = new PixelRealms.Game.Map.MapData("t", "T", new PixelRealms.Game.Map.CollisionGrid(40, 40), [], [new PixelRealms.Game.Map.NpcDef("maestro", "Maestro", null, "class_change", new Vec2(12, 10))],
            [new PixelRealms.Game.Map.GraveyardDef("gy", new Vec2(2, 2))], [], [], "gy");
        var w = new WorldBuilder().WithMap(data).WithPlayer("Ana", "warrior", 5, (10, 10)).WithPlayer("Bob", "mage", 5, (11, 10)).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var npc = new Npc(w.World.EntityIds.Next(), data.Npcs[0]) { Position = data.Npcs[0].Position };
        w.Map.Add(npc);
        w.Combat.Trades.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.ClassChange.Change(ana, npc.Id, "mage", w.Map, w.Begin()).ShouldBe("trade_busy");
        w.Combat.Trades.CancelBy(ana, "test", w.Map, w.Begin());
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.ClassChange.Change(ana, npc.Id, "mage", w.Map, w.Begin()).ShouldBe("duel_busy");
    }

    [Fact]
    public void Trade_PropertyTest_RandomTradesBetweenTwo_KeepItemsGoldAndUniqueIds() // HU-059 CA5
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        var rng = new SeededRng(4242);
        string[] templates = ["bread", "minor_healing_potion", "iron_sword", "slime_goo", "worn_dagger"];
        foreach (var p in new[] { ana, bob })
        {
            p.Gold = 500;
            for (var i = 0; i < 10; i++) p.Inventory.Bag[i] = ItemInstance.New(templates[rng.Next(0, templates.Length)], 1);
        }
        Dictionary<string, int> Totals()
        {
            var d = new Dictionary<string, int>();
            foreach (var it in ana.Inventory.Bag.Concat(bob.Inventory.Bag).Concat(ana.Equipment.Slots).Concat(bob.Equipment.Slots))
                if (it is not null) d[it.TemplateId] = d.GetValueOrDefault(it.TemplateId) + it.Qty;
            return d;
        }
        var totals = Totals(); var gold = ana.Gold + bob.Gold;

        List<(Guid, int)> RandomOffer(Player p)
        {
            var offer = new List<(Guid, int)>();
            foreach (var it in p.Inventory.Bag)
                if (it is not null && offer.Count < w.Content.Rules.Social.TradeMaxItems && rng.Next(0, 3) == 0) offer.Add((it.Id, rng.Next(1, it.Qty + 1)));
            return offer;
        }
        var completed = 0;
        for (var round = 0; round < 300; round++)
        {
            var ctx = w.Begin();
            w.Combat.Trades.Request(ana, bob, w.Map, ctx).ShouldBeNull();
            w.Combat.Trades.Respond(bob, true, w.Map, ctx).ShouldBeNull();
            var trade = w.Combat.Trades.TradeOf(ana)!;
            w.Combat.Trades.Offer(ana, RandomOffer(ana), rng.Next(0, (int)Math.Min(ana.Gold, 50) + 1), w.Map, ctx).ShouldBeNull();
            w.Combat.Trades.Offer(bob, RandomOffer(bob), rng.Next(0, (int)Math.Min(bob.Gold, 50) + 1), w.Map, ctx).ShouldBeNull();
            if (rng.Next(0, 5) == 0) { w.Combat.Trades.CancelBy(ana, "test", w.Map, ctx); continue; }
            w.Combat.Trades.Confirm(ana, trade.Version, w.Map, ctx).ShouldBeNull();
            var result = w.Combat.Trades.Confirm(bob, trade.Version, w.Map, ctx);
            if (result is null) completed++;
            else { result.ShouldBe("bag_full"); w.Combat.Trades.CancelBy(ana, "test", w.Map, ctx); }
            w.Combat.Trades.TradeOf(ana).ShouldBeNull();

            Totals().ShouldBe(totals, ignoreOrder: true);
            (ana.Gold + bob.Gold).ShouldBe(gold);
            ana.Gold.ShouldBeGreaterThanOrEqualTo(0); bob.Gold.ShouldBeGreaterThanOrEqualTo(0);
            var ids = ana.Inventory.Bag.Concat(bob.Inventory.Bag).Where(i => i is not null).Select(i => i!.Id).ToList();
            ids.Distinct().Count().ShouldBe(ids.Count); // ningún id repetido entre los dos
        }
        completed.ShouldBeGreaterThan(100); // el test ejerce intercambios reales, no solo cancelaciones
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
