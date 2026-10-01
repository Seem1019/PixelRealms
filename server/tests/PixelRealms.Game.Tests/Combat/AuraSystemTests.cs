using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-035: ticks exactos, cargas, inmunidades, controles, escudos, modificadores, topes (ADR-021/022).</summary>
public sealed class AuraSystemTests
{
    private static TestWorld Arena()
        => new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "priest", 7, (10, 10)).WithPlayer("Bob", "rogue", 13, (11, 10))
            .WithMonster("slime", (12, 10), wanderRadius: 0).WithMonster("foreman_grask", (20, 20), wanderRadius: 0).BuildWithCombat();

    private static AuraInstance? Apply(TestWorld w, Actor target, string auraId, Actor caster) => w.Combat.Auras.Apply(target, w.Content.Aura(auraId), caster, w.Map, w.Begin());

    [Fact]
    public void Renew_Exactly4Ticks_LastOnExpiry_Whip_3Ticks_ThenRemoved() // CA1
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        ana.Hp = 1;
        var renew = w.Content.Aura("priest_renew_hot"); // 12 s, tick 3 s
        Apply(w, ana, "priest_renew_hot", ana).ShouldNotBeNull();
        var events = TickRunner.RunMs(w, renew.DurationMs);
        var heals = events.OfType<CombatHitEvent>().Where(e => e.Kind == HitKinds.Heal && e.SpellId == "priest_renew_hot").ToList();
        heals.Count.ShouldBe(renew.DurationMs / renew.TickMs);
        events.OfType<AuraRemovedEvent>().Count(e => e.AuraId == "priest_renew_hot").ShouldBe(1);
        TickRunner.RunMs(w, 3000).OfType<CombatHitEvent>().Count(e => e.SpellId == "priest_renew_hot").ShouldBe(0);

        var whip = w.Content.Aura("foreman_whip_bleed"); // dot físico 9 s, tick 3 s
        var boss = w.Monster("foreman_grask");
        var hpBefore = bob.Hp;
        var aura = Apply(w, bob, "foreman_whip_bleed", boss)!;
        var mitigation = CombatCalculator.Mitigation(w.Combat.Services.StatsOf(bob).Armor, boss.Level, w.Content.Rules.Combat);
        aura.Mitigation.ShouldBe(mitigation, 1e-9);
        var ev2 = TickRunner.RunMs(w, whip.DurationMs);
        var ticks = ev2.OfType<CombatHitEvent>().Where(e => e.SpellId == "foreman_whip_bleed" && e.Kind == HitKinds.Damage).ToList();
        ticks.Count.ShouldBe(whip.DurationMs / whip.TickMs);
        ticks.ShouldAllBe(t => !t.Crit);
        ticks.ShouldAllBe(t => t.Amount == (int)Math.Round(whip.Base * (1 - mitigation), MidpointRounding.AwayFromZero));
        (hpBefore - bob.Hp).ShouldBe(ticks.Sum(t => t.Amount));
        ev2.OfType<AuraRemovedEvent>().ShouldContain(e => e.AuraId == "foreman_whip_bleed");
    }

    [Fact]
    public void Poison_StacksTo3_RefreshKeepsTickRhythm() // CA2, CA10
    {
        var w = Arena();
        var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var poison = w.Content.Aura("rogue_poison");
        var a = Apply(w, slime, "rogue_poison", bob)!;
        TickRunner.RunMs(w, 2000);
        var nextTick = a.NextTickAtMs;
        for (var i = 0; i < 3; i++) Apply(w, slime, "rogue_poison", bob);
        a.Stacks.ShouldBe(poison.MaxStacks);
        a.NextTickAtMs.ShouldBe(nextTick); // el ritmo no se reinicia
        a.ExpiresAtMs.ShouldBe(w.Clock.NowMs + poison.DurationMs); // duración refrescada
        slime.Auras.Count.ShouldBe(1);
        var ev = TickRunner.RunMs(w, 1000); // llega el primer tick (a 3 s del inicio)
        var tick = ev.OfType<CombatHitEvent>().Single(e => e.SpellId == "rogue_poison");
        tick.Amount.ShouldBe((int)Math.Round(a.Amount * 3, MidpointRounding.AwayFromZero));

        // Veneno de otro lanzador: instancia propia.
        var ana = w.Player("Ana");
        Apply(w, slime, "rogue_poison", ana);
        slime.Auras.Count.ShouldBe(2);
    }

    [Fact]
    public void Sprint_RemovesRootAndSlow_AndGrantsImmunity() // CA2b
    {
        var w = Arena();
        var bob = w.Player("Bob"); var slime = w.Monster("slime");
        Apply(w, bob, "mage_frost_nova_root", slime).ShouldNotBeNull();
        bob.Auras.IsRooted.ShouldBeTrue();
        Apply(w, bob, "rogue_sprint_aura", bob).ShouldNotBeNull();
        bob.Auras.IsRooted.ShouldBeFalse();
        var ctx = w.Begin();
        w.Combat.Auras.Apply(bob, w.Content.Aura("mage_frost_nova_root"), slime, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CombatHitEvent>().Single().Kind.ShouldBe(HitKinds.Immune);
        bob.Auras.IsRooted.ShouldBeFalse();
    }

    [Fact]
    public void Boss_IgnoresStunRootSlow_WithImmuneEvent() // CA2c
    {
        var w = Arena();
        var boss = w.Monster("foreman_grask"); var bob = w.Player("Bob");
        var ctx = w.Begin();
        w.Combat.Auras.Apply(boss, w.Content.Aura("rogue_gouge_stun"), bob, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CombatHitEvent>().Single().Kind.ShouldBe(HitKinds.Immune);
        boss.Auras.Count.ShouldBe(0);
        Apply(w, boss, "rogue_poison", bob).ShouldNotBeNull(); // un DoT sí entra
    }

    [Fact]
    public void Controls_Stun_Root_Silence_Slow() // CA3
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var rules = w.Content.Rules.Combat;
        Apply(w, ana, "warrior_charge_stun", slime);
        CombatMovementRules.IsImmobilized(ana).ShouldBeTrue();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("priest_smite"), slime.Id, null, w.Map, w.Begin()).ShouldBe(CastErrors.Stunned);
        w.Combat.Auras.ClearAll(ana, w.Map, w.Begin());

        Apply(w, ana, "mage_frost_nova_root", slime);
        CombatMovementRules.IsImmobilized(ana).ShouldBeTrue();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("priest_smite"), slime.Id, null, w.Map, w.Begin()).ShouldBeNull(); // enraizado sí castea
        w.Combat.Casts.Cancel(ana, w.Map, w.Begin());
        w.Combat.Auras.ClearAll(ana, w.Map, w.Begin());

        Apply(w, ana, "mage_chill", slime); // slow 0.4
        CombatMovementRules.IsImmobilized(ana).ShouldBeFalse();
        CombatMovementRules.SpeedMultiplier(ana, w.Begin()).ShouldBe((float)(1 - w.Content.Aura("mage_chill").Pct), 1e-6f);
    }

    [Fact]
    public void Shield_Absorbs_ThenBreaks_ReportsAbsorb() // CA4
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var shield = Apply(w, ana, "priest_power_shield_aura", ana)!;
        shield.ShieldRemaining = 50; // 50 exactos para el caso de la HU
        var hp = ana.Hp;
        var ctx = w.Begin();
        w.Combat.Damage.Deal(slime, ana, 70, School.Physical, false, null, w.Map, ctx);
        (hp - ana.Hp).ShouldBe(20);
        ctx.Events.OfType<CombatHitEvent>().Single(e => e.Kind == HitKinds.Absorb).Amount.ShouldBe(50);
        ctx.Events.OfType<CombatHitEvent>().Single(e => e.Kind == HitKinds.Damage).Amount.ShouldBe(20);
        ana.Auras.HasKind(AuraKind.Shield).ShouldBeFalse();
    }

    [Fact]
    public void StatMod_SpeedChangesAndReverts() // CA5
    {
        var w = Arena();
        var bob = w.Player("Bob");
        var sprint = w.Content.Aura("rogue_sprint_aura");
        Apply(w, bob, "rogue_sprint_aura", bob);
        CombatMovementRules.SpeedMultiplier(bob, w.Begin()).ShouldBe((float)(1 + sprint.Mods!.SpeedPct), 1e-6f);
        TickRunner.RunMs(w, sprint.DurationMs);
        CombatMovementRules.SpeedMultiplier(bob, w.Begin()).ShouldBe(1f, 1e-6f);
    }

    [Fact]
    public void Caps_16Buffs_NewOneReplacesShortest_DebuffsSeparate_ControlsDontCount() // CA7, CA8
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var limits = w.Content.Rules.Limits;
        var hot = w.Content.Aura("priest_renew_hot");
        for (var i = 0; i < limits.MaxBuffsPerEntity; i++)
        {
            w.Clock.Advance(100);
            w.Combat.Auras.Apply(ana, hot with { Id = $"buff_{i}" }, ana, w.Map, w.Begin());
        }
        ana.Auras.Count.ShouldBe(limits.MaxBuffsPerEntity);
        // Una perjudicial entra igual (grupo aparte).
        Apply(w, ana, "rogue_poison", slime).ShouldNotBeNull();
        ana.Auras.Count.ShouldBe(limits.MaxBuffsPerEntity + 1);
        // Una beneficiosa nueva saca la de menos tiempo restante (buff_0) y entra.
        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, hot with { Id = "buff_new" }, ana, w.Map, ctx).ShouldNotBeNull();
        ctx.Events.OfType<AuraRemovedEvent>().Single().AuraId.ShouldBe("buff_0");
        ana.Auras.All.Count(a => !a.Def.IsDebuff).ShouldBe(limits.MaxBuffsPerEntity);
        // Un control no cuenta ni se bloquea aunque el grupo esté lleno.
        for (var i = 0; i < limits.MaxDebuffsPerEntity; i++) w.Combat.Auras.Apply(ana, w.Content.Aura("rogue_poison") with { Id = $"debuff_{i}" }, slime, w.Map, w.Begin());
        var before = ana.Auras.Count;
        Apply(w, ana, "mage_chill", slime).ShouldNotBeNull();
        ana.Auras.Count.ShouldBe(before + 1);
    }

    [Fact]
    public void TwoSlows_StrongestWins_TwoStuns_LongestWins_MaxSlowPct() // CA9, CA11, CA12
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime"); var bob = w.Player("Bob");
        var rules = w.Content.Rules.Combat;
        Apply(w, ana, "priest_pulse_slow", slime); // 0.3
        Apply(w, ana, "mage_chill", bob);          // 0.4
        ana.Auras.MaxSlow().ShouldBe(0.4);
        CombatMovementRules.SpeedMultiplier(ana, w.Begin()).ShouldBe((float)(1 - Math.Min(0.4, rules.MaxSlowPct)), 1e-6f);
        ana.Auras.IsDominant(ana.Auras.Find("mage_chill", bob.Id)!).ShouldBeTrue();
        ana.Auras.IsDominant(ana.Auras.Find("priest_pulse_slow", slime.Id)!).ShouldBeFalse(); // en gris
        // Velocidad: Carrera (+50 %) y Sendero (+30 %) → +50 %
        Apply(w, ana, "rogue_sprint_aura", bob);
        Apply(w, ana, "priest_path_speed", ana);
        ana.Auras.MaxSpeedBonus().ShouldBe(0.5);
        w.Combat.Auras.ClearAll(ana, w.Map, w.Begin());

        // Dos aturdimientos solapados: dura hasta el que termina más tarde.
        Apply(w, ana, "warrior_charge_stun", slime); // 1500
        w.Clock.Advance(1000);
        Apply(w, ana, "rogue_gouge_stun", bob);      // 2000 → termina en +3000 desde el inicio
        TickRunner.RunMs(w, 600);
        ana.Auras.IsStunned.ShouldBeTrue();
        TickRunner.RunMs(w, 1500);
        ana.Auras.IsStunned.ShouldBeFalse();
        // CA12: inmunidad 1,5 s a controles fuertes tras terminar; una ralentización sí entra.
        var ctx = w.Begin();
        w.Combat.Auras.Apply(ana, w.Content.Aura("mage_frost_nova_root"), slime, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CombatHitEvent>().Single().Kind.ShouldBe(HitKinds.Immune);
        Apply(w, ana, "mage_chill", slime).ShouldNotBeNull();
        w.Clock.Advance((long)(rules.HardControlImmunitySec * 1000));
        Apply(w, ana, "mage_frost_nova_root", slime).ShouldNotBeNull();
    }

    [Fact]
    public void TwoShields_DifferentCasters_Coexist_EarliestExpiryConsumedFirst() // CA11
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var first = Apply(w, ana, "priest_power_shield_aura", ana)!;
        w.Clock.Advance(1000);
        var second = Apply(w, ana, "priest_power_shield_aura", bob)!;
        first.ShieldRemaining = 30; second.ShieldRemaining = 30;
        ana.Auras.Count.ShouldBe(2);
        w.Combat.Damage.Deal(slime, ana, 10, School.Physical, false, null, w.Map, w.Begin());
        first.ShieldRemaining.ShouldBe(20);
        second.ShieldRemaining.ShouldBe(30);
    }
}
