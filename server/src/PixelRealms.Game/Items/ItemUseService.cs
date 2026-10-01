using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Items;

/// <summary>
/// HU-054: usar un consumible = validar `useCooldownMs` (compartido por plantilla), lanzar `useSpellId` con el jugador como
/// lanzador (los usables no cancelan el casteo ni activan GCD) y, si el hechizo se acepta, `Qty -= 1`.
/// </summary>
public sealed class ItemUseService(CombatServices services, CastSystem casts)
{
    public string? Use(Player p, Guid itemId, MapInstance map, TickContext ctx)
    {
        var item = InventoryOps.Find(p.Inventory, itemId);
        if (item is null) return "not_found";
        var tpl = services.Content.Item(item.TemplateId);
        if (tpl.Type != ItemType.Consumable || tpl.UseSpellId is null) return "invalid_payload";
        if (p.IsDead) return "is_dead";
        if (p.ItemCooldownEndsAtMs.TryGetValue(tpl.Id, out var end) && ctx.NowMs < end) return "on_cooldown";
        if (!services.Content.TryGetSpell(tpl.UseSpellId, out var spell) || spell is null) return "invalid_payload";
        var error = casts.TryBeginCast(p, spell, p.Id, null, map, ctx, cancelCurrent: false);
        if (error is not null) return error;
        if (tpl.UseCooldownMs > 0) p.ItemCooldownEndsAtMs[tpl.Id] = ctx.NowMs + tpl.UseCooldownMs;
        InventoryOps.Remove(p, itemId, 1, "use", services.Content);
        ctx.Emit(new InventoryChangedEvent(map.Id, p, null));
        return null;
    }
}

/// <summary>HU-055: vendedor NPC. Distancia ≤ `vendorRangeTiles` en cada operación; compra y venta atómicas.</summary>
public sealed class VendorService(CombatServices services)
{
    public Npc? FindVendor(Player p, EntityId npcId, MapInstance map, TickContext ctx, out string? error)
    {
        error = null;
        if (map.Find(npcId) is not Npc npc || npc.VendorId is null) { error = "not_found"; return null; }
        if (Vec2.Distance(p.Position, npc.Position) > ctx.Rules.Economy.VendorRangeTiles) { error = "out_of_range"; return null; }
        return npc;
    }

    public string? Buy(Player p, EntityId npcId, string templateId, int qty, MapInstance map, TickContext ctx, int? reqId)
    {
        var npc = FindVendor(p, npcId, map, ctx, out var error);
        if (npc is null) return error;
        var vendor = services.Content.Vendor(npc.VendorId!);
        if (!vendor.Items.Contains(templateId) || !services.Content.TryGetItem(templateId, out var tpl) || tpl is null) return "not_found";
        if (qty <= 0 || qty > 1000) return "invalid_payload";
        var total = InventoryOps.BuyPrice(tpl, ctx.Rules.Economy) * qty;
        if (p.Inventory.Gold < total) return "not_enough_gold";
        if (!InventoryOps.CanAdd(p.Inventory, tpl, qty)) return "bag_full";
        InventoryOps.AddItem(p, tpl, qty, "buy");
        p.Inventory.Gold -= total;
        ctx.Emit(new InventoryChangedEvent(map.Id, p, reqId));
        return null;
    }

    public string? Sell(Player p, EntityId npcId, Guid itemId, int qty, MapInstance map, TickContext ctx, int? reqId)
    {
        var npc = FindVendor(p, npcId, map, ctx, out var error);
        if (npc is null) return error;
        var item = InventoryOps.Find(p.Inventory, itemId);
        if (item is null) return "not_found";
        var tpl = services.Content.Item(item.TemplateId);
        if (tpl.SellPrice <= 0) return "invalid_payload";
        var removed = InventoryOps.Remove(p, itemId, qty, "sell", services.Content);
        if (!removed.Ok) return removed.ErrorCode;
        p.Inventory.Gold += (long)tpl.SellPrice * qty;
        ctx.Emit(new InventoryChangedEvent(map.Id, p, reqId));
        return null;
    }
}
