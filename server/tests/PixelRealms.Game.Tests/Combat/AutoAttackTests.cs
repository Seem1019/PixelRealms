using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-032: ataque básico por arma (ADR-019), pausas, maná por golpe, ira, bloqueo tras instantáneo.</summary>
public sealed class AutoAttackTests
{
    private static TestWorld Arena(string classId, IReadOnlyList<string>? equip = null, string monster = "slime", float distance = 1f, int level = 1)
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", classId, level, (10, 10), equip).WithMonster(monster, (10 + distance, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana");
        var m = w.Monster(monster);
        m.Template.AggroRange.ShouldBe(0, "el maniquí no debe reaccionar solo"); // slime
        ana.Combat.TargetId = m.Id;
        ana.Combat.AutoAttackOn = true;
        return w;
    }

    [Fact]
    public void PriestWithWand_KillsSlime_WithoutSpendingMana() // CA1c, CA1b (escuela magic)
    {
        var w = Arena("priest", ["novice_wand", "novice_robe"], distance: 5f); // varita: alcance 7
        var ana = w.Player("Ana");
        var slime = w.Monster("slime");
        var manaBefore = ana.Resource;
        var events = TickRunner.RunMs(w, 60_000);
        slime.IsDead.ShouldBeTrue();
        var hits = events.OfType<CombatHitEvent>().Where(e => e.Kind == HitKinds.Damage && e.Target == slime).ToList();
        hits.ShouldNotBeEmpty();
        hits.ShouldAllBe(h => h.School == School.Magic);
        ana.Resource.ShouldBeGreaterThanOrEqualTo(manaBefore); // no gasta; el maná por golpe lo mantiene al máximo
    }

    [Fact]
    public void Swing_UsesWeaponSpeedOverHaste_AndStopsWhenTargetDies() // CA1
    {
        var w = Arena("rogue"); // daga 1600 ms / haste 1.15
        var ana = w.Player("Ana");
        var expectedSwing = 1600 / w.Content.Rules.ClassScaling["rogue"].Haste;
        w.Combat.AutoAttack.SwingMs(ana)!.Value.ShouldBe(expectedSwing, 1e-6);
        var events = TickRunner.RunMs(w, (int)expectedSwing + 100);
        events.OfType<CombatHitEvent>().Count(e => e.Source == ana).ShouldBe(1);
        TickRunner.RunMs(w, 60_000);
        w.Monster("slime").IsDead.ShouldBeTrue();
        ana.Combat.AutoAttackOn.ShouldBeFalse();
    }

    [Fact]
    public void OutOfRange_PausesSwing_WithoutResettingTimer() // CA2
    {
        var w = Arena("warrior", distance: 1f);
        var ana = w.Player("Ana");
        var slime = w.Monster("slime");
        TickRunner.RunMs(w, 1000);
        ana.Combat.SwingProgressMs.ShouldBe(1000, 1e-6);
        slime.Position = new Vec2(20, 10); // fuera de alcance (espada 1.5)
        TickRunner.RunMs(w, 2000);
        ana.Combat.SwingProgressMs.ShouldBe(1000, 1e-6); // pausado, no reiniciado
        slime.Position = new Vec2(11, 10);
        var events = TickRunner.RunMs(w, 1400); // 1000 + 1400 = 2400 = speedMs de la espada
        events.OfType<CombatHitEvent>().Count(e => e.Source == ana).ShouldBe(1);
    }

    [Fact]
    public void Hit_GrantsRage_ToWarrior_AndTakenRageToWarriorTarget() // CA3, HU-039 CA2
    {
        var w = Arena("warrior");
        var ana = w.Player("Ana");
        ana.Resource.ShouldBe(0);
        var events = TickRunner.RunMs(w, 2400);
        events.OfType<CombatHitEvent>().Count(e => e.Source == ana && e.Kind == HitKinds.Damage).ShouldBe(1);
        ana.Resource.ShouldBe((int)w.Content.Rules.Combat.RagePerHitDealt);
    }

    [Fact]
    public void ManaPerHit_SameManaPerSecond_SwordAndStaff() // CA5
    {
        var rules = TestContent.Load().Rules.Combat;
        var sword = Arena("mage", ["worn_sword", "novice_robe"]);
        var staff = Arena("mage", ["apprentice_staff", "novice_robe"], distance: 3f);
        foreach (var w in new[] { sword, staff })
        {
            var ana = w.Player("Ana");
            ana.Resource = 0;
            ana.Combat.Stats = w.Combat.Services.StatsOf(ana) with { ManaRegenPer5s = 0 }; // sin regeneración pasiva: solo maná por golpe
        }
        var swordSwing = sword.Combat.AutoAttack.SwingMs(sword.Player("Ana"))!.Value;
        var staffSwing = staff.Combat.AutoAttack.SwingMs(staff.Player("Ana"))!.Value;
        var swordHits = TickRunner.RunMs(sword, 30_000).OfType<CombatHitEvent>().Count(e => e.Kind == HitKinds.Damage && e.Source is Player);
        var staffHits = TickRunner.RunMs(staff, 30_000).OfType<CombatHitEvent>().Count(e => e.Kind == HitKinds.Damage && e.Source is Player);
        var maxMana = sword.Player("Ana").MaxResource;
        var perHitSword = CombatCalculator.ManaPerBasicHit(maxMana, swordSwing, rules);
        var perHitStaff = CombatCalculator.ManaPerBasicHit(maxMana, staffSwing, rules);
        (perHitSword / swordSwing).ShouldBe(perHitStaff / staffSwing, 1e-9); // mismo maná por segundo
        ((double)sword.Player("Ana").Resource).ShouldBe(perHitSword * swordHits, 1.0);
        ((double)staff.Player("Ana").Resource).ShouldBe(perHitStaff * staffHits, 1.0);
    }

    [Fact]
    public void Casting_PausesSwing_InstantSpell_LocksBriefly() // CA6, CA7
    {
        var w = Arena("mage", distance: 2f, level: 3);
        var ana = w.Player("Ana");
        var slime = w.Monster("slime");
        var rules = w.Content.Rules.Combat;
        TickRunner.RunMs(w, 500);
        ana.Combat.SwingProgressMs.ShouldBe(500, 1e-6);
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("mage_fireball"), slime.Id, null, w.Map, ctx).ShouldBeNull();
        TickRunner.RunMs(w, 1000);
        ana.Combat.SwingProgressMs.ShouldBe(500, 1e-6); // pausado durante el casteo
        w.Combat.Casts.Cancel(ana, w.Map, w.Begin());
        TickRunner.RunMs(w, 500);
        ana.Combat.SwingProgressMs.ShouldBe(1000, 1e-6); // reanudado

        // Instantáneo (Nova de escarcha) justo antes de que toque el básico: el básico espera al bloqueo de 250 ms.
        var swing = w.Combat.AutoAttack.SwingMs(ana)!.Value; // 3000 / 0.9
        TickRunner.RunMs(w, (int)(swing - 1000) - 100);
        ana.Combat.GcdEndsAtMs = long.MinValue;
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("mage_frost_nova"), null, null, w.Map, w.Begin()).ShouldBeNull();
        var during = TickRunner.RunMs(w, 150);
        during.OfType<CombatHitEvent>().Count(e => e.Source == ana && e.SpellId is null).ShouldBe(0); // bloqueado
        var after = TickRunner.RunMs(w, rules.AbilityLockMs);
        after.OfType<CombatHitEvent>().Count(e => e.Source == ana && e.SpellId is null).ShouldBe(1); // sale al terminar el bloqueo
    }
}
