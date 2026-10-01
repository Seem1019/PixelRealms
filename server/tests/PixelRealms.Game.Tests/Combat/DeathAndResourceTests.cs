using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-037 (muerte y reaparición) y HU-039 (recursos y regeneración) con números exactos de rules.json.</summary>
public sealed class DeathAndResourceTests
{
    private static TestWorld Arena(string classId, int level = 3)
    {
        var data = new MapData("t", "T", new CollisionGrid(40, 40), [], [], [new GraveyardDef("far", new Vec2(30, 30)), new GraveyardDef("near", new Vec2(5, 5))], [], [], "far");
        return new WorldBuilder().WithMap(data).WithPlayer("Ana", classId, level, (10, 10)).WithMonster("slime", (11, 10), wanderRadius: 0).BuildWithCombat();
    }

    [Fact]
    public void Death_ClearsAuras_MonstersForget_DiedEvent_DeadCannotAct() // CA1, CA3
    {
        var w = Arena("mage");
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        w.Combat.Auras.Apply(ana, w.Content.Aura("priest_renew_hot"), ana, w.Map, w.Begin());
        slime.Threat.Add(ana.Id, 50);
        var ctx = w.Begin();
        w.Combat.Damage.Deal(slime, ana, ana.Hp + 10, School.Physical, false, null, w.Map, ctx);
        ana.IsDead.ShouldBeTrue();
        ana.Hp.ShouldBe(0);
        ana.Auras.Count.ShouldBe(0);
        slime.Threat.Contains(ana.Id).ShouldBeFalse();
        var died = ctx.Events.OfType<ActorDiedEvent>().Single();
        died.Victim.ShouldBe(ana); died.Killer.ShouldBe(slime);
        // Muerto: no se mueve ni castea.
        CombatMovementRules.IsImmobilized(ana).ShouldBeTrue();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("mage_fireball"), slime.Id, null, w.Map, w.Begin()).ShouldBe(CastErrors.IsDead);
        // No recibe curas ni daño adicional.
        w.Combat.Damage.Heal(ana, ana, 50, false, null, w.Map, w.Begin()).ShouldBe(0);
    }

    [Fact]
    public void Respawn_NearestGraveyard_WithRulesPct() // CA2
    {
        var w = Arena("mage");
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var rules = w.Content.Rules.Combat;
        w.Combat.Death.Kill(ana, slime, w.Map, w.Begin());
        var ctx = w.Begin();
        w.Combat.Death.Respawn(ana, w.Map, ctx).ShouldBeTrue();
        ana.Position.ShouldBe(new Vec2(5, 5)); // el más cercano, no el por defecto
        ana.Hp.ShouldBe((int)Math.Round(ana.MaxHp * rules.RespawnHpPct));
        ana.Resource.ShouldBe((int)Math.Round(ana.MaxResource * rules.RespawnResourcePct));
        ana.IsDead.ShouldBeFalse();
        ctx.Events.OfType<RespawnedEvent>().Single().Player.ShouldBe(ana);
        w.Combat.Death.Respawn(ana, w.Map, w.Begin()).ShouldBeFalse(); // vivo: no hace nada
    }

    [Fact]
    public void MonsterCorpse_LastsCorpseLifetime_ThenDespawns() // CA4
    {
        var w = Arena("mage");
        var slime = w.Monster("slime");
        var corpseMs = (int)(w.Content.Rules.Combat.CorpseLifetimeSec * 1000);
        w.Combat.Death.Kill(slime, w.Player("Ana"), w.Map, w.Begin());
        TickRunner.RunMs(w, corpseMs - 100);
        w.Map.Actors.ContainsKey(slime.Id.Value).ShouldBeTrue();
        TickRunner.RunMs(w, 200);
        w.Map.Actors.ContainsKey(slime.Id.Value).ShouldBeFalse();
    }

    [Fact]
    public void Mana_RegenPerSecond_WithCastingPenalty() // HU-039 CA1
    {
        var w = Arena("mage");
        var ana = w.Player("Ana");
        var c = w.Content.Rules.Combat;
        var stats = w.Combat.Services.StatsOf(ana);
        ana.Resource = 0;
        ana.Combat.ManaPenaltyUntilMs = long.MinValue;
        TickRunner.RunMs(w, 10_000);
        ((double)ana.Resource).ShouldBe(stats.ManaRegenPer5s / 5.0 * 10, 1.01);
        // Tras gastar maná: ×manaRegenCastingPenalty durante manaRegenPenaltyDurationSec.
        ana.Resource = 0;
        w.Combat.Damage.Spend(ana, 1, w.Begin());
        var penaltySec = c.ManaRegenPenaltyDurationSec;
        TickRunner.RunMs(w, (int)(penaltySec * 1000));
        ((double)ana.Resource).ShouldBe(stats.ManaRegenPer5s / 5.0 * penaltySec * c.ManaRegenCastingPenalty, 1.01);
    }

    [Fact]
    public void Rage_GainOnHitAndTaken_DecaysOutOfCombat_StartsAtZero() // HU-039 CA2
    {
        var w = Arena("warrior");
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var c = w.Content.Rules.Combat;
        ana.Resource.ShouldBe(0);
        w.Combat.Damage.Deal(ana, slime, 1, School.Physical, false, null, w.Map, w.Begin());
        ana.Resource.ShouldBe((int)c.RagePerHitDealt);
        w.Combat.Damage.Deal(slime, ana, 1, School.Physical, false, null, w.Map, w.Begin());
        ana.Resource.ShouldBe((int)(c.RagePerHitDealt + c.RagePerHitTaken));
        // Fuera de combate (ventana inCombatWindowSec) decae rageDecayPerSecOutOfCombat por segundo.
        var start = ana.Resource;
        slime.Combat.Evading = true; // que no pegue
        TickRunner.RunMs(w, (int)(c.InCombatWindowSec * 1000));
        ana.Resource.ShouldBe(start);
        TickRunner.RunMs(w, 2000);
        ((double)ana.Resource).ShouldBe(start - c.RageDecayPerSecOutOfCombat * 2, 1.01);
    }

    [Fact]
    public void Energy_TenPerSecond_CappedAt100() // HU-039 CA3
    {
        var w = Arena("rogue");
        var ana = w.Player("Ana");
        var c = w.Content.Rules.Combat;
        ana.Resource = 0;
        TickRunner.RunMs(w, 3000);
        ((double)ana.Resource).ShouldBe(c.EnergyPerSec * 3, 1.01);
        TickRunner.RunMs(w, 60_000);
        ana.Resource.ShouldBe((int)c.ResourceCap);
    }

    [Fact]
    public void HpRegen_OutOfCombat_After6s() // HU-039 CA4
    {
        var w = Arena("priest");
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var c = w.Content.Rules.Combat;
        var stats = w.Combat.Services.StatsOf(ana);
        slime.Combat.Evading = true;
        w.Combat.Damage.Deal(slime, ana, 30, School.Physical, false, null, w.Map, w.Begin());
        var hp = ana.Hp;
        TickRunner.RunMs(w, (int)(c.HpRegenDelaySec * 1000) - 50);
        ana.Hp.ShouldBe(hp);
        TickRunner.RunMs(w, 50 + 2000);
        ((double)(ana.Hp - hp)).ShouldBe(Math.Min(30, stats.HpRegenPerSec * 2), 1.01);
    }
}
