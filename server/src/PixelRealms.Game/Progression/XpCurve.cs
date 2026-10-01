using PixelRealms.Content.Defs;

namespace PixelRealms.Game.Progression;

/// <summary>Curva de XP por tiempo (ADR-017, gdd.md §Progresión) y XP por monstruo. Todo sale de rules.progression.</summary>
public static class XpCurve
{
    /// <summary>XP del monstruo normal de ese nivel: round((monsterXpPerLevel · nivel + monsterXpBase) · tipo).</summary>
    public static int MonsterXp(ProgressionRules p, int level, MonsterType type)
    {
        var mult = p.MonsterTypeMultiplier[type.ToString().ToLowerInvariant()];
        return (int)Math.Round((p.MonsterXpPerLevel * level + p.MonsterXpBase) * mult, MidpointRounding.AwayFromZero);
    }

    /// <summary>XP para pasar del nivel L al L+1: round(minutesPerLevel[L−1] · (60 / killCycleSecTarget) · xpMonstruoNormal(L)). 0 en el nivel máximo.</summary>
    public static int XpToNextLevel(ProgressionRules p, int level)
    {
        if (level < 1 || level >= p.MaxLevel || level > p.MinutesPerLevel.Count) return 0;
        var minutes = p.MinutesPerLevel[level - 1];
        var raw = minutes * (60.0 / p.KillCycleSecTarget) * MonsterXp(p, level, MonsterType.Normal);
        return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
    }

    /// <summary>Modificador por diferencia de nivel: 0 si diff ≤ levelDiffGreyAt; si no 1 + levelDiffModPerLevel · clamp(diff, −clamp, +clamp).</summary>
    public static double LevelDiffModifier(ProgressionRules p, int monsterLevel, int referenceLevel)
    {
        var diff = monsterLevel - referenceLevel;
        if (diff <= p.LevelDiffGreyAt) return 0;
        return 1 + p.LevelDiffModPerLevel * Math.Clamp(diff, -p.LevelDiffClamp, p.LevelDiffClamp);
    }

    /// <summary>XP que recibe un jugador solo por matar un monstruo (gdd.md §Progresión, con xpRate).</summary>
    public static int SoloKillXp(ProgressionRules p, MonsterTemplate monster, int playerLevel) =>
        (int)Math.Round(MonsterXp(p, monster.Level, monster.Type) * LevelDiffModifier(p, monster.Level, playerLevel) * p.XpRate, MidpointRounding.AwayFromZero);
}
