using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Progression;

/// <summary>HU-040 (XP al matar, tag, tope de fase, xpRate) y HU-041 (subida con sobrante, hechizos nuevos, rangos ADR-024).</summary>
public sealed class ProgressionSystemTests
{
    private static TestWorld Arena(string classId = "mage", int level = 1)
        => new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", classId, level, (10, 10)).WithPlayer("Bob", "warrior", level, (10, 12))
            .WithMonster("slime", (11, 10), wanderRadius: 0).WithMonster("goblin_archer", (30, 30), wanderRadius: 0).BuildWithCombat();

    [Fact]
    public void Kill_GrantsXpToTagger_NotToOthers_WithXpGainEvent() // CA1, CA3
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var ctx = w.Begin();
        w.Combat.Damage.Deal(ana, slime, 1, School.Physical, false, null, w.Map, ctx);   // Ana taggea
        slime.TaggedBy.ShouldBe(ana.Id);
        w.Combat.Damage.Deal(bob, slime, slime.Hp, School.Physical, false, null, w.Map, ctx); // Bob remata
        slime.IsDead.ShouldBeTrue();
        // El evento de muerte está en `ctx`: el sistema de progresión lo procesa (en el tick corre tras la muerte).
        w.Combat.Progression.Tick(w.Map, ctx);
        var expected = XpCurve.SoloKillXp(w.Content.Rules.Progression, slime.Template, ana.Level);
        ana.Xp.ShouldBe(expected);
        bob.Xp.ShouldBe(0);
        ctx.Events.OfType<XpGainedEvent>().Single().Amount.ShouldBe(expected);
    }

    [Fact]
    public void Kill_InTick_GrantsXp_EndToEnd()
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 1, (10, 10)).WithMonster("slime", (11, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        slime.Hp = 1;
        ana.Combat.TargetId = slime.Id; ana.Combat.AutoAttackOn = true;
        var events = TickRunner.RunMs(w, 10_000);
        slime.IsDead.ShouldBeTrue();
        events.OfType<XpGainedEvent>().Single().Amount.ShouldBe(XpCurve.SoloKillXp(w.Content.Rules.Progression, slime.Template, 1));
    }

    [Fact]
    public void XpRate_MultipliesEverything() // CA1c
    {
        var p = TestContent.Load().Rules.Progression;
        var slime = TestContent.Load().Monster("goblin_archer");
        var baseXp = XpCurve.SoloKillXp(p, slime, 5);
        var tripled = XpCurve.SoloKillXp(p with { XpRate = 3.0 }, slime, 5);
        tripled.ShouldBe(baseXp * 3);
    }

    [Fact]
    public void LevelCap_NoXpAccumulates() // CA4
    {
        var w = Arena(level: TestContent.Load().Rules.CurrentLevelCap);
        var ana = w.Player("Ana");
        var ctx = w.Begin();
        w.Combat.Progression.GrantXp(ana, 1000, null, w.Map, ctx);
        ana.Xp.ShouldBe(0);
        ana.Level.ShouldBe(w.Content.Rules.CurrentLevelCap);
        ctx.Events.OfType<XpGainedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void LevelUp_CarriesRemainder_MultipleLevels_FullHpAndResource_NewSpells() // HU-041 CA1, CA2
    {
        var w = Arena(level: 1);
        var ana = w.Player("Ana");
        var p = w.Content.Rules.Progression;
        ana.Hp = 5; ana.Resource = 0;
        var toLevel3 = XpCurve.XpToNextLevel(p, 1) + XpCurve.XpToNextLevel(p, 2);
        var ctx = w.Begin();
        w.Combat.Progression.GrantXp(ana, toLevel3 + 7, null, w.Map, ctx);
        ana.Level.ShouldBe(3);
        ana.Xp.ShouldBe(7);
        ana.Hp.ShouldBe(ana.MaxHp);
        ana.Resource.ShouldBe(ana.MaxResource);
        var levelUps = ctx.Events.OfType<LevelUpEvent>().ToList();
        levelUps.Select(l => l.Level).ToArray().ShouldBe(new[] { 2, 3 });
        levelUps[0].NewSpells.ShouldBe(new[] { "mage_frostbolt" });
        levelUps[1].NewSpells.ShouldBe(new[] { "mage_frost_nova" });
        ana.KnownSpells.ShouldContain("mage_frost_nova");
        ctx.Events.OfType<StatsChangedEvent>().Count().ShouldBe(1);
        // Stats: +statsPerLevel por nivel (vida máxima sube).
        var expectedHp = StatCalculator.Derive(w.Content.Class("mage"), 3, w.Content.Rules, []).MaxHp;
        ana.MaxHp.ShouldBeGreaterThanOrEqualTo(expectedHp);
    }

    [Fact]
    public void RankLevel4_RankUpsKnownSpells_Level5_NewSpellsNoRankUps() // HU-041 CA3b
    {
        var w = Arena(level: 3);
        var ana = w.Player("Ana");
        var p = w.Content.Rules.Progression;
        ana.KnownSpells.Count.ShouldBe(3);
        var ctx = w.Begin();
        w.Combat.Progression.GrantXp(ana, XpCurve.XpToNextLevel(p, 3), null, w.Map, ctx);
        var up4 = ctx.Events.OfType<LevelUpEvent>().Single();
        up4.Level.ShouldBe(4);
        up4.NewSpells.ShouldBeEmpty();
        up4.RankUps.Select(r => r.SpellId).OrderBy(x => x).ToArray().ShouldBe(new[] { "mage_fireball", "mage_frost_nova", "mage_frostbolt" });
        up4.RankUps.ShouldAllBe(r => r.Rank == 1);

        var ctx5 = w.Begin();
        w.Combat.Progression.GrantXp(ana, XpCurve.XpToNextLevel(p, 4), null, w.Map, ctx5);
        var up5 = ctx5.Events.OfType<LevelUpEvent>().Single();
        up5.Level.ShouldBe(5);
        up5.NewSpells.ShouldBe(new[] { "mage_flame_burst" });
        up5.RankUps.ShouldBeEmpty();
    }

    [Fact]
    public void RankBonus_AppliesToEffectBase() // ADR-024: +15 % sobre el base a partir del nivel 4
    {
        var p = TestContent.Load().Rules.Progression;
        SpellRanks.RankAt(p, 3).ShouldBe(0);
        SpellRanks.RankAt(p, 4).ShouldBe(1);
        SpellRanks.BaseMultiplier(p, 4).ShouldBe(1 + p.SpellRankBonusPct, 1e-9);
        // Golpe heroico (physical, base + apCoef·AP) con FixedRng: mismo roll a nivel 3 y 4 → el daño sube por el rango (más stats).
        var smite = TestContent.Load().Spell("priest_smite").Effects[0];
        var raw3 = CombatCalculator.MagicRaw(smite, 20);
        var raw4 = CombatCalculator.MagicRaw(smite with { Base = smite.Base * SpellRanks.BaseMultiplier(p, 4) }, 20);
        (raw4 - raw3).ShouldBe(smite.Base * p.SpellRankBonusPct, 1e-9);
    }

    [Fact]
    public void UnlockAndRankLevels_FromRules() // HU-041 CA4
    {
        var p = TestContent.Load().Rules.Progression;
        p.SpellUnlockLevels.ToArray().ShouldBe(new[] { 1, 2, 3, 5, 7, 9, 11, 13 });
        p.SpellRankLevels.ToArray().ShouldBe(new[] { 4, 8, 12 });
        var db = TestContent.Load();
        foreach (var cls in db.Classes)
            db.ClassSpells(cls.Id).Select(s => s.LevelReq).OrderBy(l => l).ToArray().ShouldBe(p.SpellUnlockLevels.ToArray(), cls.Id);
    }
}
