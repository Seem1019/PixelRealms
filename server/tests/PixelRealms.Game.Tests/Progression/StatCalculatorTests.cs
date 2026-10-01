using PixelRealms.Content.Defs;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Progression;

/// <summary>HU-034 CA3/CA3b y HU-052 CA2b: fórmulas de combat.md §Stats derivados con los valores reales de rules.json.</summary>
public sealed class StatCalculatorTests
{
    private readonly PixelRealms.Content.ContentDb _db = TestContent.Load();

    [Theory]
    [InlineData("warrior", 1, 60 + 12 * 12, 0)]
    [InlineData("rogue", 1, 50 + 9 * 10, 0)]
    [InlineData("mage", 1, 40 + 8 * 10, 60 + 14 * 8)]
    [InlineData("priest", 1, 45 + 9 * 10, 55 + 12 * 8)]
    [InlineData("mage", 5, 40 + 12 * 10, 60 + 22 * 8)]
    [InlineData("warrior", 15, 60 + 40 * 12, 0)]
    public void MaxHpAndMana_Level_NoEquipment(string classId, int level, int hp, int mana)
    {
        var d = StatCalculator.Derive(_db.Class(classId), level, _db.Rules, []);
        d.MaxHp.ShouldBe(hp);
        d.MaxMana.ShouldBe(mana);
    }

    [Fact]
    public void Mage_IronSword_LowAffinity_Exact() // HU-052 CA2b
    {
        var mage = _db.Class("mage");
        var sword = _db.Item("iron_sword");
        var without = StatCalculator.Derive(mage, 1, _db.Rules, []);
        var with = StatCalculator.Derive(mage, 1, _db.Rules, [sword]);
        (with.Primary.Str - without.Primary.Str).ShouldBe(1.4, 1e-9);
        (with.Primary.Sta - without.Primary.Sta).ShouldBe(0.7, 1e-9);
        (with.AttackPower - without.AttackPower).ShouldBe(0.84, 1e-9);
        (with.MaxHp - without.MaxHp).ShouldBe(7);
        with.WeaponAffinity.ShouldBe(0.7);
    }

    [Fact]
    public void Warrior_Level1_StartingGear_Armor()
    {
        var w = _db.Class("warrior");
        var gear = new[] { _db.Item("worn_sword"), _db.Item("recruit_mail_shirt"), _db.Item("wooden_shield") };
        var d = StatCalculator.Derive(w, 1, _db.Rules, gear);
        // (14 + 20) × alta 1.0 × armorMult 1.0 + agi 7 × 1
        d.Armor.ShouldBe(41, 1e-9);
        d.AttackPower.ShouldBe(2.0 * 12 + 0.5 * 7 + 0.3 * 3, 1e-9);
        d.Haste.ShouldBe(1.0);
        d.CritChancePhysical.ShouldBe(0.05 + 7 * 0.003, 1e-9);
        d.DodgeChance.ShouldBe(0.03 + 7 * 0.002, 1e-9);
        d.MitigationAgainst(1, _db.Rules.Combat).ShouldBe(41.0 / (41 + 20 + 100), 1e-9);
    }

    [Fact]
    public void Priest_Wand_SpellPower_FromIntAndItem()
    {
        var p = _db.Class("priest");
        var d = StatCalculator.Derive(p, 4, _db.Rules, [_db.Item("willow_wand")]);
        // int = 12 + 2·3 + 2 (varita, alta) = 20 → sp = 1.3·20 + 3 (spellPower item) = 29
        d.Primary.Int.ShouldBe(20, 1e-9);
        d.SpellPower.ShouldBe(29, 1e-9);
        StatCalculator.MaxResource(p, d, _db.Rules).ShouldBe(d.MaxMana);
        StatCalculator.MaxResource(_db.Class("rogue"), d, _db.Rules).ShouldBe(100);
    }

    [Fact]
    public void OffRole_Margins_FromRules() // HU-034 CA3b: mismo equipo iron_sword + recruit_mail_shirt
    {
        var gear = new[] { _db.Item("iron_sword"), _db.Item("recruit_mail_shirt") };
        var rogue = StatCalculator.Derive(_db.Class("rogue"), 5, _db.Rules, gear);
        var priest = StatCalculator.Derive(_db.Class("priest"), 5, _db.Rules, gear);
        var warrior = StatCalculator.Derive(_db.Class("warrior"), 5, _db.Rules, gear);
        var mage = StatCalculator.Derive(_db.Class("mage"), 5, _db.Rules, gear);
        var sword = _db.Item("iron_sword");
        double BasicDps(DerivedStats d, string cls)
        {
            var swing = sword.SpeedMs / d.Haste / 1000.0;
            var roll = (sword.DamageMin + sword.DamageMax) / 2.0 * _db.Rules.Affinity.MultiplierFor(cls, "sword");
            return (roll + d.AttackPower / _db.Rules.Combat.BasicAttackPowerDivisor * swing) / swing;
        }
        var ratio = BasicDps(priest, "priest") / BasicDps(rogue, "rogue");
        var margins = _db.Rules.BalanceTargets.OffRoleDamagePct;
        ratio.ShouldBeInRange(margins[0], margins[1]);
        // Aguante: vida efectiva contra un golpe físico de nivel 5 (vida / (1 − mitigación)).
        double Ehp(DerivedStats d) => d.MaxHp / (1 - d.MitigationAgainst(5, _db.Rules.Combat));
        var survival = Ehp(mage) / Ehp(warrior);
        var sm = _db.Rules.BalanceTargets.OffRoleSurvivalPct;
        survival.ShouldBeInRange(sm[0], sm[1]);
    }
}
