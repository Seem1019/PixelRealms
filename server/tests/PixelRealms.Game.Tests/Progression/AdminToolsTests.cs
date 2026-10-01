using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Progression;

/// <summary>HU-070: piezas de dominio detrás de los comandos admin (`/level`, `/god`, `/spawn`).</summary>
public sealed class AdminToolsTests
{
    private static TestWorld Arena() => new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 1, (10, 10)).WithMonster("slime", (11, 10), wanderRadius: 0).BuildWithCombat();

    [Fact]
    public void SetLevel_Up_LearnsSpells_AndDown_ForgetsThem_ClampedToCap()
    {
        var w = Arena();
        var ana = w.Player("Ana");
        var ctx = w.Begin();
        var cap = w.Content.Rules.CurrentLevelCap;
        w.Combat.Progression.SetLevel(ana, 99, w.Map, ctx).ShouldBe(cap);
        ana.Level.ShouldBe(cap);
        ana.Xp.ShouldBe(0);
        var atCap = ana.KnownSpells.Count;
        atCap.ShouldBeGreaterThan(0);
        ctx.Events.OfType<LevelUpEvent>().Count().ShouldBe(cap - 1);
        ana.Hotbar[0] = ("spell", ana.KnownSpells[^1]);

        w.Combat.Progression.SetLevel(ana, 1, w.Map, ctx).ShouldBe(1);
        ana.Level.ShouldBe(1);
        ana.KnownSpells.ShouldAllBe(id => w.Content.Spell(id).LevelReq <= 1);
        ana.KnownSpells.Count.ShouldBeLessThan(atCap);
        if (w.Content.Spell(ana.Hotbar[0]?.Ref ?? ana.KnownSpells[0]).LevelReq > 1) ana.Hotbar[0].ShouldBeNull();
        ana.Hp.ShouldBe(ana.MaxHp);
    }

    [Fact]
    public void GodMode_TakesNoDamage_ButStillEntersCombat()
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var ctx = w.Begin();
        ana.GodMode = true;
        var before = ana.Hp;
        w.Combat.Damage.Deal(slime, ana, 50, School.Physical, false, null, w.Map, ctx).ShouldBe(0);
        ana.Hp.ShouldBe(before);
        ana.IsInCombat(ctx.NowMs, 5).ShouldBeTrue();
        ana.GodMode = false;
        w.Combat.Damage.Deal(slime, ana, 5, School.Physical, false, null, w.Map, ctx).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void SpawnAt_CreatesMonsters_WithoutRespawn()
    {
        var w = Arena();
        var ana = w.Player("Ana");
        var ctx = w.Begin();
        var before = w.Map.Monsters.Count;
        w.Combat.Spawns.SpawnAt("wolf", ana.Position, 3, w.Map, ctx.Rng).ShouldBe(3);
        w.Combat.Spawns.SpawnAt("nope", ana.Position, 3, w.Map, ctx.Rng).ShouldBe(0);
        w.Map.Monsters.Count.ShouldBe(before + 3);
        var wolf = w.Map.Monsters.Values.First(m => m.TemplateId == "wolf");
        Vec2.Distance(wolf.Position, ana.Position).ShouldBeLessThan(4);
        wolf.Hp = 0;
        w.Combat.Death.Kill(wolf, ana, w.Map, ctx);
        w.Combat.Spawns.PendingCount(w.Map).ShouldBe(0); // sin punto de spawn no reaparece
    }
}

/// <summary>HU-072: tiempo de los sistemas de combate por instancia (p99 en /admin/stats).</summary>
public sealed class CombatTimingTests
{
    [Fact]
    public void Simulation_RecordsCombatTime_PerInstance_WhenEnabled()
    {
        var w = new WorldBuilder().WithMap(20, 20).WithPlayer("Ana", "mage", 1, (5, 5)).WithMonster("slime", (6, 5), wanderRadius: 0).BuildWithCombat();
        w.Simulation.CombatTimings.ShouldBeNull(); // por defecto no mide (tests deterministas y sin coste)
        w.Simulation.CombatTimings = new Dictionary<int, TickStats>();
        TickRunner.Run(w, 40);
        var stats = w.Simulation.CombatTimings[w.Map.Id];
        stats.Count.ShouldBe(40);
        stats.Percentiles().P99.ShouldBeGreaterThanOrEqualTo(0);
        var names = w.Simulation.Systems.Select(s => s.Name).ToHashSet();
        Simulation.CombatSystemNames.All(names.Contains).ShouldBeTrue(); // los nombres medidos existen de verdad
    }
}
