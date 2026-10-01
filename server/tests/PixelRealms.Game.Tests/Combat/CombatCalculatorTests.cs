using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-032 CA4: hit, crit, miss, dodge y mitigación con números exactos; las constantes salen del rules.json real.</summary>
public sealed class CombatCalculatorTests
{
    private static readonly CombatRules C = TestContent.Load().Rules.Combat;

    [Fact]
    public void HitTable_Physical_MissDodgeCritHit()
    {
        // miss 5 % (+1 % por nivel del objetivo sobre el atacante) → dodge → crit → hit.
        var miss = C.PhysicalMissBase + C.PhysicalMissPerTargetLevel; // objetivo un nivel por encima
        CombatCalculator.RollHit(new FixedRng(miss - 0.001), School.Physical, 1, 2, 0.10, 0.20, C).ShouldBe(HitOutcome.Miss);
        CombatCalculator.RollHit(new FixedRng(miss + 0.001), School.Physical, 1, 2, 0.10, 0.20, C).ShouldBe(HitOutcome.Dodge);
        CombatCalculator.RollHit(new FixedRng(miss + 0.10 + 0.001), School.Physical, 1, 2, 0.10, 0.20, C).ShouldBe(HitOutcome.Crit);
        CombatCalculator.RollHit(new FixedRng(miss + 0.10 + 0.20 + 0.001), School.Physical, 1, 2, 0.10, 0.20, C).ShouldBe(HitOutcome.Hit);
        // Un objetivo de nivel inferior no reduce el fallo por debajo de la base.
        CombatCalculator.RollHit(new FixedRng(C.PhysicalMissBase - 0.001), School.Physical, 5, 1, 0, 0, C).ShouldBe(HitOutcome.Miss);
        CombatCalculator.RollHit(new FixedRng(C.PhysicalMissBase + 0.001), School.Physical, 5, 1, 0, 0, C).ShouldBe(HitOutcome.Hit);
    }

    [Fact]
    public void HitTable_Magic_NoDodge()
    {
        CombatCalculator.RollHit(new FixedRng(C.MagicMissBase - 0.001), School.Magic, 1, 5, 0.25, 0.10, C).ShouldBe(HitOutcome.Miss);
        CombatCalculator.RollHit(new FixedRng(C.MagicMissBase + 0.001), School.Magic, 1, 5, 0.25, 0.10, C).ShouldBe(HitOutcome.Crit); // la esquiva no cuenta
        CombatCalculator.RollHit(new FixedRng(C.MagicMissBase + 0.10 + 0.001), School.Magic, 1, 5, 0.25, 0.10, C).ShouldBe(HitOutcome.Hit);
    }

    [Fact]
    public void Mitigation_FormulaAndCap()
    {
        // armor 50 contra nivel 5: 50 / (50 + 20·5 + 100) = 0.2
        CombatCalculator.Mitigation(50, 5, C).ShouldBe(50 / (50 + C.MitigationPerLevel * 5 + C.MitigationConstant), 1e-9);
        CombatCalculator.Mitigation(0, 5, C).ShouldBe(0);
        CombatCalculator.Mitigation(1_000_000, 1, C).ShouldBe(C.MitigationCap);
    }

    [Fact]
    public void PhysicalDamage_ExactNumbers()
    {
        // raw 40, mitig 0.2, sin crit, variance 1.0 → 32; con crit ×1.5 → 48; variance 0.95 → round(30.4) = 30
        CombatCalculator.PhysicalDamage(40, 0.2, false, 1.0, 1.0, C).ShouldBe(32);
        CombatCalculator.PhysicalDamage(40, 0.2, true, 1.0, 1.0, C).ShouldBe((int)Math.Round(32 * C.CritMultiplier));
        CombatCalculator.PhysicalDamage(40, 0.2, false, 0.95, 1.0, C).ShouldBe(30);
    }

    [Fact]
    public void MagicDamage_NoMitigation()
    {
        CombatCalculator.MagicDamage(37.4, false, 1.0, 1.0, C).ShouldBe(37);
        CombatCalculator.MagicDamage(37.4, true, 1.0, 1.0, C).ShouldBe((int)Math.Round(37.4 * C.CritMultiplier, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void BasicAttackRaw_UsesDivisorAndSwing()
    {
        // roll 5 · afinidad 0.85 + poder 28 / 14 · (2400 / 1000) = 4.25 + 4.8 = 9.05
        CombatCalculator.BasicAttackRaw(5, 0.85, 28, 2400, C).ShouldBe(5 * 0.85 + 28 / C.BasicAttackPowerDivisor * 2.4, 1e-9);
    }

    [Fact]
    public void Variance_Weapon_ManaPerHit()
    {
        CombatCalculator.RollVariance(new FixedRng(0.0), C).ShouldBe(C.VarianceMin);
        CombatCalculator.RollVariance(new FixedRng(0.999999), C).ShouldBe(C.VarianceMax, 1e-5);
        CombatCalculator.RollWeapon(new FixedRng(0.0), 3, 6).ShouldBe(3);
        CombatCalculator.RollWeapon(new FixedRng(0.999), 3, 6).ShouldBe(6);
        CombatCalculator.ManaPerBasicHit(240, 3000, C).ShouldBe(240 * C.ManaPerBasicHitPctPerSec * 3.0, 1e-9);
    }
}
