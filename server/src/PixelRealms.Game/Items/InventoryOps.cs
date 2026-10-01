using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Items;

/// <summary>Referencia a una casilla: contenedor "bag" | "equip" e índice (docs/protocol.md `SlotRef`).</summary>
public readonly record struct SlotRef(string Container, int Index)
{
    public bool IsBag => Container == "bag";
    public bool IsEquip => Container == "equip";
}

public readonly record struct OpResult(bool Ok, string? ErrorCode)
{
    public static readonly OpResult Success = new(true, null);
    public static OpResult Fail(string code) => new(false, code);
}

/// <summary>Entrada pendiente de `item_audit_log` (HU-057 CA3): se escribe en lote con el guardado, nunca por operación en el tick.</summary>
public sealed record PendingAudit(Guid ItemId, string Action, string TemplateId, int Quantity, Guid? CounterpartyCharacterId = null);

/// <summary>
/// Operaciones de bolsa y equipo (skill inventory-items): validan todo primero y mutan después (invariante 6); conservación de
/// cantidades salvo las que lo declaran; ids únicos (los splits crean id nuevo); `1 ≤ qty ≤ maxStack`; en equipo solo items con
/// `slot` coincidente y `levelReq ≤ nivel` — sin restricción por clase ni tipo (ADR-009).
/// </summary>
public static class InventoryOps
{
    public const string BagContainer = "bag";
    public const string EquipContainer = "equip";

    public static OpResult Move(Player p, SlotRef from, SlotRef to, int? qty, ContentDb db)
    {
        if (!Valid(from) || !Valid(to) || from == to) return OpResult.Fail("invalid_payload");
        if (from.IsBag && to.IsBag) return MoveBagToBag(p, from.Index, to.Index, qty, db);
        if (from.IsBag && to.IsEquip) return EquipFromBag(p, from.Index, to.Index, db);
        if (from.IsEquip && to.IsBag) return UnequipTo(p, from.Index, to.Index, db);
        return OpResult.Fail("invalid_payload"); // equip → equip
    }

    private static bool Valid(SlotRef r) => (r.IsBag && r.Index >= 0 && r.Index < Inventory.BagSize) || (r.IsEquip && r.Index >= 0 && r.Index < Equipment.SlotCount);

    private static OpResult MoveBagToBag(Player p, int from, int to, int? qty, ContentDb db)
    {
        var bag = p.Inventory.Bag;
        var src = bag[from];
        if (src is null) return OpResult.Fail("not_found");
        if (qty is { } q && (q <= 0 || q > src.Qty)) return OpResult.Fail("invalid_payload");
        var dst = bag[to];
        var tpl = db.Item(src.TemplateId);
        if (dst is null)
        {
            if (qty is { } split && split < src.Qty)
            {
                // Dividir: id nuevo para el stack nuevo.
                src.Qty -= split;
                bag[to] = ItemInstance.New(src.TemplateId, split);
                p.Audit(new PendingAudit(bag[to]!.Id, "split", src.TemplateId, split));
            }
            else { bag[to] = src; bag[from] = null; }
            return OpResult.Success;
        }
        if (dst.TemplateId == src.TemplateId && tpl.IsStackable)
        {
            var moving = qty ?? src.Qty;
            var space = tpl.MaxStack - dst.Qty;
            var moved = Math.Min(space, moving);
            if (moved <= 0) { (bag[from], bag[to]) = (dst, src); return OpResult.Success; } // destino lleno: intercambiar
            dst.Qty += moved;
            src.Qty -= moved;
            if (src.Qty == 0) { bag[from] = null; p.Audit(new PendingAudit(src.Id, "merge", src.TemplateId, moved)); }
            return OpResult.Success;
        }
        if (qty is { } partial && partial < src.Qty) return OpResult.Fail("invalid_payload"); // no se puede dividir sobre otro item
        (bag[from], bag[to]) = (dst, src);
        return OpResult.Success;
    }

    /// <summary>Equipa el item de la casilla en su slot (o en `slotIndex` si coincide); intercambia con lo equipado. Único error de item: level_too_low.</summary>
    public static OpResult Equip(Player p, int bagIndex, ContentDb db) => EquipFromBag(p, bagIndex, -1, db);

    private static OpResult EquipFromBag(Player p, int bagIndex, int slotIndex, ContentDb db)
    {
        var item = p.Inventory.Bag[bagIndex];
        if (item is null) return OpResult.Fail("not_found");
        var tpl = db.Item(item.TemplateId);
        if (tpl.Slot is not { } slot) return OpResult.Fail("invalid_payload");
        var target = (int)slot;
        if (slotIndex >= 0 && slotIndex != target) return OpResult.Fail("invalid_payload");
        if (p.Level < tpl.LevelReq) return OpResult.Fail("level_too_low");
        var previous = p.Equipment.Slots[target];
        p.Equipment.Slots[target] = item;
        p.Inventory.Bag[bagIndex] = previous;
        p.MarkStatsDirty();
        return OpResult.Success;
    }

    /// <summary>Desequipa al índice dado o al primer hueco libre; `bag_full` si no hay.</summary>
    public static OpResult Unequip(Player p, int slot, int? bagIndex, ContentDb db)
    {
        if (slot < 0 || slot >= Equipment.SlotCount) return OpResult.Fail("invalid_payload");
        var idx = bagIndex ?? FirstFree(p.Inventory);
        if (idx < 0) return OpResult.Fail("bag_full");
        return UnequipTo(p, slot, idx, db);
    }

    private static OpResult UnequipTo(Player p, int slot, int bagIndex, ContentDb db)
    {
        var item = p.Equipment.Slots[slot];
        if (item is null) return OpResult.Fail("not_found");
        var occupant = p.Inventory.Bag[bagIndex];
        if (occupant is not null)
        {
            // Intercambio con un equipable del mismo slot (y nivel); si no, hace falta hueco.
            var otpl = db.Item(occupant.TemplateId);
            if (otpl.Slot is not { } oslot || (int)oslot != slot) return OpResult.Fail("bag_full");
            if (p.Level < otpl.LevelReq) return OpResult.Fail("level_too_low");
        }
        p.Equipment.Slots[slot] = occupant;
        p.Inventory.Bag[bagIndex] = item;
        p.MarkStatsDirty();
        return OpResult.Success;
    }

    public static int FirstFree(Inventory inv)
    {
        for (var i = 0; i < inv.Bag.Length; i++) if (inv.Bag[i] is null) return i;
        return -1;
    }

    /// <summary>¿Cabe TODO (rellenando stacks y luego huecos) sin cambiar nada?</summary>
    public static bool CanAdd(Inventory inv, ItemTemplate tpl, int qty)
    {
        var remaining = qty;
        if (tpl.IsStackable)
            foreach (var s in inv.Bag) if (s is not null && s.TemplateId == tpl.Id) remaining -= Math.Max(0, tpl.MaxStack - s.Qty);
        if (remaining <= 0) return true;
        var perSlot = Math.Max(1, tpl.MaxStack);
        var free = inv.FreeSlots;
        return (long)free * perSlot >= remaining;
    }

    /// <summary>Añade llenando stacks y luego huecos; si no cabe todo falla sin cambios (`bag_full`). Devuelve las instancias creadas/ampliadas.</summary>
    public static OpResult AddItem(Player p, ItemTemplate tpl, int qty, string auditAction, List<ItemInstance>? created = null, Guid? counterparty = null)
    {
        if (qty <= 0) return OpResult.Fail("invalid_payload");
        var inv = p.Inventory;
        if (!CanAdd(inv, tpl, qty)) return OpResult.Fail("bag_full");
        var remaining = qty;
        if (tpl.IsStackable)
        {
            foreach (var s in inv.Bag)
            {
                if (remaining == 0) break;
                if (s is null || s.TemplateId != tpl.Id || s.Qty >= tpl.MaxStack) continue;
                var add = Math.Min(tpl.MaxStack - s.Qty, remaining);
                s.Qty += add; remaining -= add;
                created?.Add(s);
                p.Audit(new PendingAudit(s.Id, auditAction, tpl.Id, add, counterparty));
            }
        }
        for (var i = 0; i < inv.Bag.Length && remaining > 0; i++)
        {
            if (inv.Bag[i] is not null) continue;
            var n = Math.Min(Math.Max(1, tpl.MaxStack), remaining);
            var inst = ItemInstance.New(tpl.Id, n);
            inv.Bag[i] = inst; remaining -= n;
            created?.Add(inst);
            p.Audit(new PendingAudit(inst.Id, auditAction, tpl.Id, n, counterparty));
        }
        return OpResult.Success;
    }

    /// <summary>Quita `qty` de un item de la bolsa por id (destruir, vender, usar). `not_found` / `invalid_payload`.</summary>
    public static OpResult Remove(Player p, Guid itemId, int qty, string auditAction, ContentDb db, Guid? counterparty = null)
    {
        var idx = IndexOf(p.Inventory, itemId);
        if (idx < 0) return OpResult.Fail("not_found");
        var item = p.Inventory.Bag[idx]!;
        if (qty <= 0 || qty > item.Qty) return OpResult.Fail("invalid_payload");
        item.Qty -= qty;
        if (item.Qty == 0) p.Inventory.Bag[idx] = null;
        p.Audit(new PendingAudit(item.Id, auditAction, item.TemplateId, qty, counterparty));
        return OpResult.Success;
    }

    public static int IndexOf(Inventory inv, Guid itemId)
    {
        for (var i = 0; i < inv.Bag.Length; i++) if (inv.Bag[i] is { } it && it.Id == itemId) return i;
        return -1;
    }

    public static ItemInstance? Find(Inventory inv, Guid itemId)
    {
        var i = IndexOf(inv, itemId);
        return i < 0 ? null : inv.Bag[i];
    }

    /// <summary>Precio de compra en un vendedor: `vendorPrice ?? sellPrice × vendorBuyMultiplier`.</summary>
    public static long BuyPrice(ItemTemplate tpl, EconomyRules economy) => tpl.VendorPrice ?? (long)Math.Round(tpl.SellPrice * economy.VendorBuyMultiplier);
}
