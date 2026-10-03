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
public sealed class PlayerMapper(ReloadableContent content, ILogger<PlayerMapper>? logger = null)
{
    /// <summary>Contenedor de `character_items` para los objetos que no se pueden colocar (<see cref="Player.Unplaced"/>).</summary>
    public const short UnplacedContainer = 2;

    /// <summary>Nombre visible de una plantilla (o el id si no existe).</summary>
    public string ItemName(string templateId) => content.Current.TryGetItem(templateId, out var tpl) && tpl is not null ? tpl.Name : templateId;

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
            if (!db.TryGetItem(item.TemplateId, out _))
            {
                // HU-057 CA4: plantilla que ya no existe → el personaje carga igual, se avisa y el objeto se conserva aparte (antes
                // se descartaba y el siguiente guardado lo borraba para siempre).
                logger?.LogWarning("{Name}: item {ItemId} con plantilla desconocida '{TemplateId}' apartado", dto.Name, item.Id, item.TemplateId);
                player.Unplaced.Add(inst);
                continue;
            }
            if (item.Container == 1 && item.Slot >= 0 && item.Slot < Equipment.SlotCount && player.Equipment.Slots[item.Slot] is null) player.Equipment.Slots[item.Slot] = inst;
            else if (item.Container == 0 && item.Slot >= 0 && item.Slot < Inventory.BagSize && player.Inventory.Bag[item.Slot] is null) player.Inventory.Bag[item.Slot] = inst;
            else player.Unplaced.Add(inst); // casilla repetida o fuera de rango (o ya apartado): se recoloca abajo si cabe
        }
        // Lo apartado con plantilla válida vuelve a la bolsa en cuanto hay hueco; lo demás sigue aparte, sin perderse.
        for (var i = 0; i < player.Unplaced.Count;)
        {
            var it = player.Unplaced[i];
            var free = Array.IndexOf(player.Inventory.Bag, null);
            if (db.TryGetItem(it.TemplateId, out _) && free >= 0) { player.Inventory.Bag[free] = it; player.Unplaced.RemoveAt(i); player.Dirty = true; }
            else i++;
        }
        if (player.Unplaced.Count > 0) logger?.LogWarning("{Name}: {Count} items apartados (sin plantilla o sin hueco)", dto.Name, player.Unplaced.Count);
        foreach (var h in dto.Hotbar)
            if (h.Slot >= 0 && h.Slot < player.Hotbar.Length) player.Hotbar[h.Slot] = (h.Kind == 0 ? "spell" : "item", h.Ref);
        player.KnownSpells.AddRange(db.KnownSpells(cls.Id, dto.Level).Select(s => s.Id));
        Recalculate(player);
        player.Hp = Math.Clamp(dto.Hp, 0, player.MaxHp);
        // HU-039 CA2: la ira empieza en 0 en cada entrada al mundo; maná y energía se conservan.
        player.Resource = cls.Resource == Resource.Rage ? 0 : Math.Clamp(dto.Resource, 0, player.MaxResource);
        // HU-015: los cooldowns siguen corriendo con el reloj real mientras está fuera; solo vuelven los que no han terminado.
        var nowMs = NowMs();
        var utcNow = Time.GetUtcNow().UtcDateTime;
        foreach (var cd in dto.Cooldowns ?? [])
        {
            // Nunca más que la recarga actual del contenido (reloj del host corregido, contenido rebajado) ni de algo que ya no existe.
            int maxMs;
            Dictionary<string, long> cooldowns;
            if (cd.Kind == 0 && db.TryGetSpell(cd.Ref, out var spell) && spell is not null) (maxMs, cooldowns) = (spell.CooldownMs, player.Combat.CooldownEndsAtMs);
            else if (cd.Kind == 1 && db.TryGetItem(cd.Ref, out var tpl) && tpl is not null) (maxMs, cooldowns) = (tpl.UseCooldownMs, player.ItemCooldownEndsAtMs);
            else continue;
            var remainingMs = Math.Min((long)(cd.EndsAtUtc - utcNow).TotalMilliseconds, maxMs);
            if (remainingMs <= 0) continue;
            cooldowns[cd.Ref] = nowMs + remainingMs;
        }
        return player;
    }

    public CharacterSaveDto ToSave(Player p, IReadOnlyList<AuditEntry>? audit = null)
    {
        // HU-057 CA3: la auditoría pendiente viaja en lote con el guardado y se vacía.
        if (audit is null && p.PendingAudit.Count > 0)
        {
            audit = p.PendingAudit.Select(a => new AuditEntry(a.ItemId, a.Action, a.TemplateId, a.Quantity, a.CounterpartyCharacterId)).ToList();
            p.PendingAudit.Clear();
        }
        var items = new List<SavedItem>();
        for (var i = 0; i < p.Inventory.Bag.Length; i++)
            if (p.Inventory.Bag[i] is { } it) items.Add(new SavedItem(it.Id, it.TemplateId, it.Qty, 0, (short)i));
        for (var i = 0; i < p.Equipment.Slots.Length; i++)
            if (p.Equipment.Slots[i] is { } it) items.Add(new SavedItem(it.Id, it.TemplateId, it.Qty, 1, (short)i));
        for (var i = 0; i < p.Unplaced.Count; i++)
            items.Add(new SavedItem(p.Unplaced[i].Id, p.Unplaced[i].TemplateId, p.Unplaced[i].Qty, UnplacedContainer, (short)i));
        var hotbar = new List<SavedHotbarSlot>();
        for (var i = 0; i < p.Hotbar.Length; i++)
            if (p.Hotbar[i] is { } h) hotbar.Add(new SavedHotbarSlot((short)i, (short)(h.Kind == "spell" ? 0 : 1), h.Ref));
        return new CharacterSaveDto(p.CharacterId, p.AccountId, p.Name, p.ClassId, p.Level, p.Xp, p.Gold, MapIdOf(p), p.Position.X, p.Position.Y, p.Hp, p.Resource, items, hotbar, audit ?? [],
            ToSavedCooldowns(p));
    }

    /// <summary>Cooldowns de hechizo y de consumible aún activos, como instante UTC de fin (el reloj de juego no sobrevive al reinicio).</summary>
    private List<SavedCooldown> ToSavedCooldowns(Player p)
    {
        var nowMs = NowMs();
        var utcNow = Time.GetUtcNow().UtcDateTime;
        var saved = new List<SavedCooldown>();
        foreach (var (spellId, end) in p.Combat.CooldownEndsAtMs)
            if (end > nowMs) saved.Add(new SavedCooldown(0, spellId, utcNow.AddMilliseconds(end - nowMs)));
        foreach (var (templateId, end) in p.ItemCooldownEndsAtMs)
            if (end > nowMs) saved.Add(new SavedCooldown(1, templateId, utcNow.AddMilliseconds(end - nowMs)));
        return saved;
    }

    /// <summary>`Cooldown` de cada hechizo y cada consumible aún en recarga: tras Welcome el cliente los dibuja en la barra (HU-015).</summary>
    public IEnumerable<Cooldown> ToCooldowns(Player p)
    {
        var nowMs = NowMs();
        foreach (var (spellId, end) in p.Combat.CooldownEndsAtMs)
            if (end > nowMs) yield return new Cooldown(spellId, (int)(end - nowMs), null);
        foreach (var (templateId, end) in p.ItemCooldownEndsAtMs)
            if (end > nowMs) yield return new Cooldown(null, (int)(end - nowMs), null, templateId);
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

    public InventoryUpdate ToInventoryUpdate(Player p, int? reqId) =>
        new(p.Inventory.Bag.Select(ToDto).ToList(), p.Equipment.Slots.Select(ToDto).ToList(), p.Inventory.Gold, reqId);

    /// <summary>StatsUpdate (docs/protocol.md): nivel, XP, stats primarios redondeados, derivados y oro. Solo lee: los máximos
    /// vivos los mantiene CombatServices.Recalculate (con auras) y se envían tal cual, como en los Snapshot.</summary>
    public StatsUpdate ToStatsUpdate(Player p)
    {
        var db = content.Current;
        var cls = db.Class(p.ClassId);
        var equipped = new List<ItemTemplate>(Equipment.SlotCount);
        foreach (var slot in p.Equipment.Slots)
            if (slot is not null && db.TryGetItem(slot.TemplateId, out var tpl) && tpl is not null) equipped.Add(tpl);
        var d = StatCalculator.Derive(cls, p.Level, db.Rules, equipped, PrimaryStats.From(p.Auras.StatMods()));
        var pr = d.Primary;
        var stats = new StatsDto((int)Math.Round(pr.Str), (int)Math.Round(pr.Agi), (int)Math.Round(pr.Int), (int)Math.Round(pr.Spi), (int)Math.Round(pr.Sta));
        var derived = new DerivedStatsDto(p.MaxHp, p.MaxResource, (float)d.AttackPower, (float)d.SpellPower, (float)d.CritChancePhysical, (float)d.DodgeChance, (float)d.Armor, (float)d.Haste, (float)d.MitigationAgainst(p.Level, db.Rules.Combat));
        return new StatsUpdate(p.Level, p.Xp, XpCurve.XpToNextLevel(db.Rules.Progression, p.Level), stats, derived, p.Gold);
    }

    private static ItemStackDto? ToDto(ItemInstance? i) => i is null ? null : new ItemStackDto(i.Id.ToString(), i.TemplateId, i.Qty);

    /// <summary>MapId actual del jugador (su instancia); se rellena desde el mundo en WorldSession.</summary>
    public Func<Player, string> MapIdOf { get; set; } = _ => "";

    /// <summary>Reloj de juego (ms) para convertir cooldowns; lo fija la composición con el de la simulación.</summary>
    public Func<long> NowMs { get; set; } = () => 0;

    /// <summary>Reloj real para que los cooldowns guardados sigan corriendo fuera del juego (en tests, uno falso).</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;
}
