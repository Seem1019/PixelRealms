using PixelRealms.Content.Defs;

namespace PixelRealms.Game.Progression;

/// <summary>Stats primarios con decimales: el equipo multiplicado por la afinidad puede dar +1.4 de fuerza (HU-052 CA2b).</summary>
public readonly record struct PrimaryStats(double Str, double Agi, double Int, double Spi, double Sta)
{
    public static PrimaryStats From(Stats s) => new(s.Str, s.Agi, s.Int, s.Spi, s.Sta);

    public static PrimaryStats operator +(PrimaryStats a, PrimaryStats b) => new(a.Str + b.Str, a.Agi + b.Agi, a.Int + b.Int, a.Spi + b.Spi, a.Sta + b.Sta);

    public static PrimaryStats operator *(PrimaryStats a, double k) => new(a.Str * k, a.Agi * k, a.Int * k, a.Spi * k, a.Sta * k);

    public double Get(Stat stat) => stat switch { Stat.Str => Str, Stat.Agi => Agi, _ => Int };
}

/// <summary>Stats derivados (combat.md §Stats derivados). Todo sale de rules.classScaling / rules.combat / rules.affinity.</summary>
public sealed record DerivedStats(
    PrimaryStats Primary, int MaxHp, int MaxMana, double AttackPower, double SpellPower, double CritChancePhysical, double CritChanceMagic,
    double DodgeChance, double Armor, double Haste, double ManaRegenPer5s, double HpRegenPerSec, double WeaponAffinity)
{
    /// <summary>Mitigación contra un atacante del nivel dado: armor / (armor + perLevel · nivel + constante), tope mitigationCap.</summary>
    public double MitigationAgainst(int attackerLevel, CombatRules c) =>
        Math.Min(c.MitigationCap, Armor / (Armor + c.MitigationPerLevel * attackerLevel + c.MitigationConstant));
}

/// <summary>
/// Calcula los stats de un personaje: baseStats de clase + statsPerLevel · (nivel − 1) + equipo × afinidad (+ auras stat_mod)
/// y los derivados con la matriz de conversión de su clase. Puro: recibe reglas y contenido, no toca el mundo.
/// </summary>
public static class StatCalculator
{
    public static PrimaryStats Primary(ClassDef cls, int level, IRules rules, IEnumerable<ItemTemplate> equipped, PrimaryStats auraMods = default)
    {
        var stats = PrimaryStats.From(cls.BaseStats) + PrimaryStats.From(cls.StatsPerLevel) * (level - 1);
        foreach (var item in equipped)
        {
            if (item.Stats is null) continue;
            var mult = rules.Affinity.MultiplierFor(cls.Id, item.AffinityType);
            stats += PrimaryStats.From(item.Stats) * mult;
        }
        return stats + auraMods;
    }

    public static DerivedStats Derive(ClassDef cls, int level, IRules rules, IReadOnlyList<ItemTemplate> equipped, PrimaryStats auraMods = default)
    {
        var p = Primary(cls, level, rules, equipped, auraMods);
        var scaling = rules.ClassScaling[cls.Id];
        var c = rules.Combat;

        var itemSpellPower = 0.0;
        var itemArmor = 0.0;
        var weaponAffinity = 1.0;
        foreach (var item in equipped)
        {
            var mult = rules.Affinity.MultiplierFor(cls.Id, item.AffinityType);
            itemSpellPower += item.SpellPower * mult;
            itemArmor += item.Armor * mult;
            if (item.Slot == EquipSlot.MainHand && item.IsWeapon) weaponAffinity = mult;
        }

        var maxHp = (int)Math.Round(cls.BaseHp + p.Sta * scaling.HpPerSta);
        var maxMana = scaling.ManaPerInt > 0 ? (int)Math.Round(cls.BaseMana + p.Int * scaling.ManaPerInt) : 0;
        var ap = scaling.Ap.Str * p.Str + scaling.Ap.Agi * p.Agi + scaling.Ap.Int * p.Int;
        var sp = scaling.Sp.Str * p.Str + scaling.Sp.Agi * p.Agi + scaling.Sp.Int * p.Int + itemSpellPower;
        var critPhys = Math.Min(c.CritCap, c.CritBase + p.Agi * c.CritPerAgi);
        var critMagic = Math.Min(c.CritCap, c.CritBase + p.Int * c.CritPerInt);
        var dodge = Math.Min(c.DodgeCap, c.DodgeBase + p.Agi * c.DodgePerAgi);
        var armor = itemArmor * scaling.ArmorMult + p.Agi * c.ArmorPerAgi;
        var manaRegen = p.Spi * c.ManaRegenPerSpiPer5s + p.Int * c.ManaRegenPerIntPer5s;
        var hpRegen = p.Spi * c.HpRegenPerSpi + p.Sta * c.HpRegenPerSta;
        return new DerivedStats(p, maxHp, maxMana, ap, sp, critPhys, critMagic, dodge, armor, scaling.Haste, manaRegen, hpRegen, weaponAffinity);
    }

    /// <summary>Recurso máximo del personaje: maná derivado para Mago/Sacerdote; resourceCap (100) para ira y energía.</summary>
    public static int MaxResource(ClassDef cls, DerivedStats d, IRules rules) =>
        cls.Resource == Resource.Mana ? d.MaxMana : (int)rules.Combat.ResourceCap;
}
