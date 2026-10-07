using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Progression;

/// <summary>
/// HU-104 (ADR-027 D1): mejoras 1-de-2 de los hechizos de clase. Desde `rules.progression.spellUpgradeLevel` cada hechizo
/// aprendido con mejoras elige una; se cambia o se quita gratis fuera de combate (es el reinicio de ADR-014). El hechizo
/// mejorado lo precalcula ContentDb: aquí solo se elige cuál usar.
/// </summary>
public static class SpellUpgradeRules
{
    /// <summary>Elige (o quita, con `upgradeId` null) la mejora de un hechizo. Devuelve el código de error o null.</summary>
    public static string? Choose(Player p, string spellId, string? upgradeId, ContentDb db, ProgressionRules rules, bool inCombat)
    {
        if (!p.KnownSpells.Contains(spellId) || !db.TryGetSpell(spellId, out var spell) || spell is null || spell.Upgrades.Count == 0) return "invalid_payload";
        if (upgradeId is not null && db.Upgraded(spellId, upgradeId) is null) return "invalid_payload";
        if (p.Level < rules.SpellUpgradeLevel) return "level_too_low";
        if (inCombat) return "in_combat";
        if (upgradeId is null) p.SpellUpgrades.Remove(spellId);
        else p.SpellUpgrades[spellId] = upgradeId;
        p.Dirty = true;
        return null;
    }

    /// <summary>El hechizo que lanza el jugador: con su mejora si la tiene elegida y le toca por nivel; si no, el del contenido.</summary>
    public static SpellDef Effective(Player p, SpellDef spell, ContentDb db, ProgressionRules rules) =>
        spell.Source == SpellSource.Class && p.Level >= rules.SpellUpgradeLevel && p.SpellUpgrades.TryGetValue(spell.Id, out var up)
            ? db.Upgraded(spell.Id, up) ?? spell
            : spell;

    /// <summary>
    /// Hechizos cuya mejora se puede elegir desde este nivel (`LevelUp.upgradesUnlocked`): al llegar a `spellUpgradeLevel`, todos
    /// los aprendidos con mejoras; después, los que se aprenden ese nivel.
    /// </summary>
    public static List<string> UnlockedAt(Player p, IReadOnlyList<string> newSpells, ContentDb db, ProgressionRules rules)
    {
        var unlocked = new List<string>();
        if (p.Level < rules.SpellUpgradeLevel) return unlocked;
        var candidates = p.Level == rules.SpellUpgradeLevel ? (IEnumerable<string>)p.KnownSpells : newSpells;
        foreach (var id in candidates)
            if (db.TryGetSpell(id, out var s) && s is { Upgrades.Count: > 0 }) unlocked.Add(id);
        return unlocked;
    }

    /// <summary>
    /// Quita las mejoras que ya no valen: de hechizos que no conoce (cambio de clase, `/level` hacia abajo), por debajo de
    /// `spellUpgradeLevel` o que el contenido ya no tiene. También al cargar el personaje.
    /// </summary>
    public static void Prune(Player p, ContentDb db, ProgressionRules rules)
    {
        if (p.SpellUpgrades.Count == 0) return;
        List<string>? stale = null;
        foreach (var (spellId, upgradeId) in p.SpellUpgrades)
            if (p.Level < rules.SpellUpgradeLevel || !p.KnownSpells.Contains(spellId) || db.Upgraded(spellId, upgradeId) is null)
                (stale ??= []).Add(spellId);
        if (stale is null) return;
        foreach (var id in stale) p.SpellUpgrades.Remove(id);
        p.Dirty = true;
    }
}
