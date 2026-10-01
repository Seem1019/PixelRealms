using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;

namespace PixelRealms.Game.Combat;

public enum HitOutcome { Miss, Dodge, Crit, Hit }

/// <summary>
/// Fórmulas puras de `docs/design/combat.md` (§Tabla de impacto, §Daño físico, §Daño mágico, §Curación, §Ataque básico).
/// Todo roll usa el <see cref="IRng"/> inyectado; toda constante viene de <see cref="CombatRules"/>.
/// </summary>
public static class CombatCalculator
{
    /// <summary>
    /// Un solo roll por objetivo. Físico: miss (base + perTargetLevel por nivel del objetivo sobre el atacante) → dodge → crit → hit.
    /// Mágico: miss (magicMissBase) → crit → hit (no se esquiva).
    /// </summary>
    public static HitOutcome RollHit(IRng rng, School school, int attackerLevel, int targetLevel, double targetDodge, double critChance, CombatRules c)
    {
        var roll = rng.NextDouble();
        var miss = school == School.Physical
            ? c.PhysicalMissBase + Math.Max(0, targetLevel - attackerLevel) * c.PhysicalMissPerTargetLevel
            : c.MagicMissBase;
        if (roll < miss) return HitOutcome.Miss;
        roll -= miss;
        if (school == School.Physical)
        {
            if (roll < targetDodge) return HitOutcome.Dodge;
            roll -= targetDodge;
        }
        return roll < critChance ? HitOutcome.Crit : HitOutcome.Hit;
    }

    /// <summary>variance ∈ [varianceMin, varianceMax] con un roll.</summary>
    public static double RollVariance(IRng rng, CombatRules c) => c.VarianceMin + rng.NextDouble() * (c.VarianceMax - c.VarianceMin);

    /// <summary>Tirada de arma en [min, max] (enteros) con un roll.</summary>
    public static int RollWeapon(IRng rng, int min, int max) => max <= min ? min : rng.Next(min, max + 1);

    /// <summary>mitig = armor / (armor + mitigationPerLevel · nivelAtacante + mitigationConstant), tope mitigationCap.</summary>
    public static double Mitigation(double armor, int attackerLevel, CombatRules c) =>
        armor <= 0 ? 0 : Math.Min(c.MitigationCap, armor / (armor + c.MitigationPerLevel * attackerLevel + c.MitigationConstant));

    /// <summary>raw del básico: weaponRoll · afinidad + poder / basicAttackPowerDivisor · (swingMs / 1000).</summary>
    public static double BasicAttackRaw(int weaponRoll, double affinity, double power, double swingMs, CombatRules c) =>
        weaponRoll * affinity + power / c.BasicAttackPowerDivisor * (swingMs / 1000.0);

    /// <summary>raw físico de habilidad: base + apCoef · attackPower + weaponPct · weaponRollMedio · afinidad.</summary>
    public static double PhysicalRaw(EffectDef e, double attackPower, double weaponAverage, double affinity) =>
        e.Base + e.ApCoef * attackPower + e.WeaponPct * weaponAverage * affinity;

    /// <summary>raw mágico: base + spCoef · spellPower.</summary>
    public static double MagicRaw(EffectDef e, double spellPower) => e.Base + e.SpCoef * spellPower;

    /// <summary>dmg físico = round(raw · (1 − mitig) · critMult · variance · classAdvantage).</summary>
    public static int PhysicalDamage(double raw, double mitigation, bool crit, double variance, double classAdvantage, CombatRules c) =>
        (int)Math.Round(raw * (1 - mitigation) * (crit ? c.CritMultiplier : 1.0) * variance * classAdvantage, MidpointRounding.AwayFromZero);

    /// <summary>dmg mágico = round(raw · (1 − resist) · critMult · variance · classAdvantage); resist = 0 en el MVP.</summary>
    public static int MagicDamage(double raw, bool crit, double variance, double classAdvantage, CombatRules c, double resist = 0) =>
        (int)Math.Round(raw * (1 - resist) * (crit ? c.CritMultiplier : 1.0) * variance * classAdvantage, MidpointRounding.AwayFromZero);

    /// <summary>heal = round((base + spCoef · spellPower) · critMult · variance); nunca falla.</summary>
    public static int Heal(double raw, bool crit, double variance, CombatRules c) =>
        (int)Math.Round(raw * (crit ? c.CritMultiplier : 1.0) * variance, MidpointRounding.AwayFromZero);

    /// <summary>Maná por básico que impacta: maxMana · manaPerBasicHitPctPerSec · (swingMs / 1000) (ADR-014).</summary>
    public static double ManaPerBasicHit(int maxMana, double swingMs, CombatRules c) => maxMana * c.ManaPerBasicHitPctPerSec * (swingMs / 1000.0);
}
