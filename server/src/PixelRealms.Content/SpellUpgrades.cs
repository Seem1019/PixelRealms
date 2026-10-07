using PixelRealms.Content.Defs;

namespace PixelRealms.Content;

/// <summary>
/// HU-104: aplica una mejora a un hechizo y devuelve el hechizo efectivo (mismo id, sin mejoras). ContentDb lo calcula una vez
/// por pareja al cargar el contenido: el tick no combina modificadores en cada casteo (HU-088).
/// </summary>
public static class SpellUpgrades
{
    /// <summary>Campos del hechizo que una mejora puede cambiar.</summary>
    public static readonly IReadOnlySet<string> SpellStats = new HashSet<string>(StringComparer.Ordinal)
        { "cooldownMs", "castMs", "cost", "range", "aoeRadius", "aoeAngleDeg", "aoeLength", "aoeWidth", "maxTargets" };

    /// <summary>Campos de un aura aplicada que una mejora puede cambiar (`amount` escala base y coeficientes).</summary>
    public static readonly IReadOnlySet<string> AuraStats = new HashSet<string>(StringComparer.Ordinal) { "durationMs", "pct", "amount" };

    public static SpellDef Apply(SpellDef spell, SpellUpgradeDef upgrade, Func<string, AuraDef> aura)
    {
        var s = spell with { Upgrades = [], AppliedUpgradeId = upgrade.Id };
        var effects = spell.Effects.ToList();
        foreach (var m in upgrade.Mods)
        {
            if (m.AddEffect is { } added)
            {
                effects.Add(added);
            }
            else if (m.Aura is { } auraId)
            {
                for (var i = 0; i < effects.Count; i++)
                    if (effects[i] is { Type: EffectType.ApplyAura } e && e.AuraId == auraId)
                        effects[i] = e with { AuraOverride = ApplyToAura(e.AuraOverride ?? aura(auraId), m) };
            }
            else if (m.Effect is { } type)
            {
                for (var i = 0; i < effects.Count; i++)
                    if (effects[i].Type == type) effects[i] = Scale(effects[i], m.Mult);
            }
            else if (m.Stat is { } stat)
            {
                s = ApplyToSpell(s, stat, m);
            }
        }
        return s with { Effects = effects };
    }

    private static double V(double value, SpellModDef m) => value * m.Mult + m.Add;

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static SpellDef ApplyToSpell(SpellDef s, string stat, SpellModDef m) => stat switch
    {
        "cooldownMs" => s with { CooldownMs = Round(V(s.CooldownMs, m)) },
        "castMs" => s with { CastMs = Round(V(s.CastMs, m)) },
        "cost" => s.Cost is { } c ? s with { Cost = c with { Amount = Round(V(c.Amount, m)) } } : s,
        "range" => s with { Range = V(s.Range, m) },
        "aoeRadius" => s with { AoeRadius = V(s.AoeRadius, m) },
        "aoeAngleDeg" => s with { AoeAngleDeg = V(s.AoeAngleDeg, m) },
        "aoeLength" => s with { AoeLength = V(s.AoeLength, m) },
        "aoeWidth" => s with { AoeWidth = V(s.AoeWidth, m) },
        "maxTargets" => s with { MaxTargets = Round(V(s.MaxTargets, m)) },
        _ => throw new ArgumentException($"campo de hechizo '{stat}' no admitido en una mejora", nameof(stat)),
    };

    private static AuraDef ApplyToAura(AuraDef a, SpellModDef m) => m.Stat switch
    {
        "durationMs" => a with { DurationMs = Round(V(a.DurationMs, m)) },
        "pct" => a with { Pct = V(a.Pct, m) },
        "amount" => a with { Base = V(a.Base, m), ApCoef = a.ApCoef * m.Mult, SpCoef = a.SpCoef * m.Mult },
        _ => throw new ArgumentException($"campo de aura '{m.Stat}' no admitido en una mejora", nameof(m)),
    };

    /// <summary>Potencia de un efecto: base, coeficientes, porcentaje de arma y cantidad, todo × mult.</summary>
    private static EffectDef Scale(EffectDef e, double mult) => e with
    {
        Base = e.Base * mult, ApCoef = e.ApCoef * mult, SpCoef = e.SpCoef * mult, WeaponPct = e.WeaponPct * mult, Amount = e.Amount * mult,
    };
}
