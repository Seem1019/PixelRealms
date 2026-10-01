using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Progression;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Players;

/// <summary>Convierte entre el personaje guardado (DTO inmutable) y la entidad viva del mundo, y construye los DTOs del protocolo.</summary>
public sealed class PlayerMapper(ReloadableContent content)
{
    public Player ToPlayer(CharacterSaveDto dto, EntityId id)
    {
        var db = content.Current;
        var cls = db.Class(dto.ClassId);
        var player = new Player(id, dto.Name, dto.ClassId)
        {
            CharacterId = dto.Id, AccountId = dto.AccountId, Level = dto.Level, Xp = dto.Xp, Gold = dto.Gold,
            Position = new Vec2(dto.X, dto.Y), BaseSpeed = (float)db.Rules.Movement.BaseSpeedTilesPerSec,
        };
        foreach (var item in dto.Items)
        {
            var inst = new ItemInstance(item.Id, item.TemplateId, item.Quantity);
            if (item.Container == 1 && item.Slot >= 0 && item.Slot < Equipment.SlotCount) player.Equipment.Slots[item.Slot] = inst;
            else if (item.Container == 0 && item.Slot >= 0 && item.Slot < Inventory.BagSize) player.Inventory.Bag[item.Slot] = inst;
        }
        foreach (var h in dto.Hotbar)
            if (h.Slot >= 0 && h.Slot < player.Hotbar.Length) player.Hotbar[h.Slot] = (h.Kind == 0 ? "spell" : "item", h.Ref);
        player.KnownSpells.AddRange(db.KnownSpells(cls.Id, dto.Level).Select(s => s.Id));
        Recalculate(player);
        player.Hp = Math.Clamp(dto.Hp, 0, player.MaxHp);
        player.Resource = Math.Clamp(dto.Resource, 0, player.MaxResource);
        return player;
    }

    public CharacterSaveDto ToSave(Player p, IReadOnlyList<AuditEntry>? audit = null)
    {
        var items = new List<SavedItem>();
        for (var i = 0; i < p.Inventory.Bag.Length; i++)
            if (p.Inventory.Bag[i] is { } it) items.Add(new SavedItem(it.Id, it.TemplateId, it.Qty, 0, (short)i));
        for (var i = 0; i < p.Equipment.Slots.Length; i++)
            if (p.Equipment.Slots[i] is { } it) items.Add(new SavedItem(it.Id, it.TemplateId, it.Qty, 1, (short)i));
        var hotbar = new List<SavedHotbarSlot>();
        for (var i = 0; i < p.Hotbar.Length; i++)
            if (p.Hotbar[i] is { } h) hotbar.Add(new SavedHotbarSlot((short)i, (short)(h.Kind == "spell" ? 0 : 1), h.Ref));
        return new CharacterSaveDto(p.CharacterId, p.AccountId, p.Name, p.ClassId, p.Level, p.Xp, p.Gold, MapIdOf(p), p.Position.X, p.Position.Y, p.Hp, p.Resource, items, hotbar, audit ?? []);
    }

    /// <summary>Recalcula máximos con clase + nivel + equipo (StatCalculator); la vida actual se recorta si bajó el máximo.</summary>
    public DerivedStats Recalculate(Player p)
    {
        var db = content.Current;
        var cls = db.Class(p.ClassId);
        var equipped = p.Equipment.Slots.Where(s => s is not null).Select(s => db.Item(s!.TemplateId)).ToList();
        var derived = StatCalculator.Derive(cls, p.Level, db.Rules, equipped);
        p.MaxHp = derived.MaxHp;
        p.MaxResource = StatCalculator.MaxResource(cls, derived, db.Rules);
        p.Hp = Math.Min(p.Hp, p.MaxHp);
        p.Resource = Math.Min(p.Resource, p.MaxResource);
        return derived;
    }

    public Welcome ToWelcome(Player p, string mapId, long tick)
    {
        var db = content.Current;
        var cls = db.Class(p.ClassId);
        var bag = p.Inventory.Bag.Select(ToDto).ToList();
        var equip = p.Equipment.Slots.Select(ToDto).ToList();
        var hotbar = new List<HotbarSlotDto>();
        for (var i = 0; i < p.Hotbar.Length; i++) if (p.Hotbar[i] is { } h) hotbar.Add(new HotbarSlotDto(i, h.Kind, h.Ref));
        var self = new SelfStateDto(p.Position.X * GameConstants.PixelsPerTile, p.Position.Y * GameConstants.PixelsPerTile, p.Level, p.Xp,
            XpCurve.XpToNextLevel(db.Rules.Progression, p.Level), p.Hp, p.MaxHp, p.Resource, p.MaxResource, ContentJson.EnumName(cls.Resource), p.ClassId, p.Name);
        return new Welcome(p.Id.Value, tick, 1000 / GameConstants.TickMs, 1000 / (GameConstants.TickMs * GameConstants.SnapshotEveryTicks), mapId, self, bag, equip, hotbar, p.KnownSpells.ToList(), db.Rules.Hash);
    }

    /// <summary>StatsUpdate (docs/protocol.md): nivel, XP, stats primarios redondeados, derivados y oro.</summary>
    public StatsUpdate ToStatsUpdate(Player p)
    {
        var db = content.Current;
        var d = Recalculate(p);
        var pr = d.Primary;
        var stats = new StatsDto((int)Math.Round(pr.Str), (int)Math.Round(pr.Agi), (int)Math.Round(pr.Int), (int)Math.Round(pr.Spi), (int)Math.Round(pr.Sta));
        var derived = new DerivedStatsDto(d.MaxHp, p.MaxResource, (float)d.AttackPower, (float)d.SpellPower, (float)d.CritChancePhysical, (float)d.DodgeChance, (float)d.Armor, (float)d.Haste, (float)d.MitigationAgainst(p.Level, db.Rules.Combat));
        return new StatsUpdate(p.Level, p.Xp, XpCurve.XpToNextLevel(db.Rules.Progression, p.Level), stats, derived, p.Gold);
    }

    private static ItemStackDto? ToDto(ItemInstance? i) => i is null ? null : new ItemStackDto(i.Id.ToString(), i.TemplateId, i.Qty);

    /// <summary>MapId actual del jugador (su instancia); se rellena desde el mundo en WorldSession.</summary>
    public Func<Player, string> MapIdOf { get; set; } = _ => "";
}
