using PixelRealms.Content.Defs;

namespace PixelRealms.Game.Progression;

/// <summary>Rangos de hechizo (ADR-024): suben en `spellRankLevels` (4, 8, 12) y cada rango añade `spellRankBonusPct` sobre el `base` del efecto.</summary>
public static class SpellRanks
{
    /// <summary>Rango alcanzado a un nivel: cuántos niveles de `spellRankLevels` son ≤ nivel.</summary>
    public static int RankAt(ProgressionRules p, int level)
    {
        var rank = 0;
        foreach (var l in p.SpellRankLevels) if (level >= l) rank++;
        return rank;
    }

    /// <summary>Multiplicador del `base` de los efectos numéricos: 1 + rango · spellRankBonusPct (lineal; la Fase 2 decide si compuesto).</summary>
    public static double BaseMultiplier(ProgressionRules p, int level) => 1 + RankAt(p, level) * p.SpellRankBonusPct;

    public static bool IsRankLevel(ProgressionRules p, int level) => p.SpellRankLevels.Contains(level);
}
