using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Social;

public sealed record ClassChangedEvent(int MapInstanceId, Player Player, string OldClassId, string NewClassId) : IGameEvent;

/// <summary>
/// HU-044: cambio de clase en el NPC `class_change` de la Aldea (≤ `vendorRangeTiles`, fuera de combate, vivo, sin duelo ni
/// intercambio, solo mientras `world.currentPhase ≤ progression.classChange.npcUntilPhase`). Conserva nivel, XP, items, equipo y oro;
/// cambia clase, stats, recurso (lleno) y hechizos (los de la nueva clase hasta el nivel); la barra se rellena con los nuevos.
/// </summary>
public sealed class ClassChangeService(CombatServices services)
{
    public Func<Player, bool> InDuel { get; set; } = static _ => false;

    public Func<Player, bool> InTrade { get; set; } = static _ => false;

    public bool IsAvailable(IRules rules) => rules.World.CurrentPhase <= rules.Progression.ClassChange.NpcUntilPhase;

    public string? Change(Player p, EntityId npcId, string classId, MapInstance map, TickContext ctx)
    {
        var rules = ctx.Rules;
        if (!IsAvailable(rules)) return "forbidden";
        if (map.Find(npcId) is not Npc npc || npc.NpcKind != "class_change") return "not_found";
        if (Vec2.Distance(p.Position, npc.Position) > rules.Economy.VendorRangeTiles) return "out_of_range";
        if (p.IsDead) return "is_dead";
        if (p.IsInCombat(ctx.NowMs, rules.Combat.InCombatWindowSec)) return "in_combat";
        if (InDuel(p)) return "duel_busy";
        if (InTrade(p)) return "trade_busy"; // HU-044 CA3: cada caso con su código
        if (!services.Content.TryGetClass(classId, out var cls) || cls is null) return "invalid_payload";
        if (p.ClassId == classId) return "invalid_payload";

        var old = p.ClassId;
        p.ClassId = classId;
        p.KnownSpells.Clear();
        p.KnownSpells.AddRange(services.Content.KnownSpells(classId, p.Level).Select(s => s.Id));
        Progression.SpellUpgradeRules.Prune(p, services.Content, rules.Progression); // HU-104 CA6: las de la clase anterior se van
        var spellSlots = rules.Loadout.SpellSlots;
        for (var i = 0; i < spellSlots; i++) p.Hotbar[i] = null;
        var slot = 0;
        foreach (var id in p.KnownSpells) { if (slot >= spellSlots) break; p.Hotbar[slot++] = ("spell", id); }
        p.Combat.CooldownEndsAtMs.Clear();
        p.Combat.Cast = null;
        p.Combat.ResetTransient();
        services.Recalculate(p);
        p.Resource = p.MaxResource;
        p.Hp = Math.Min(p.Hp, p.MaxHp);
        p.Dirty = true;
        ctx.Emit(new ClassChangedEvent(map.Id, p, old, classId));
        ctx.Emit(new Progression.StatsChangedEvent(map.Id, p));
        return null;
    }
}
