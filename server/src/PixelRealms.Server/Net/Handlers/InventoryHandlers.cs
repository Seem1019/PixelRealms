using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Game.Items;
using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>Handlers de bolsa/equipo/botín/vendedor (HU-050..HU-056): validar → dominio → `Error` con código; cambios → InventoryUpdate.</summary>
public sealed class InventoryMoveHandler(CombatHandlerDeps deps, ReloadableContent content) : IMessageHandler<InventoryMove>
{
    public void Handle(InventoryMove msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        if (msg.From is null || msg.To is null) { ctx.SendError(ErrorCodes.InvalidPayload, msg.ReqId); return; }
        if (IsLockedSlot(p, msg.From, deps) || IsLockedSlot(p, msg.To, deps)) { ctx.SendError(ErrorCodes.TradeBusy, msg.ReqId); return; }
        var result = InventoryOps.Move(p, new Game.Items.SlotRef(msg.From.C, msg.From.I), new Game.Items.SlotRef(msg.To.C, msg.To.I), msg.Qty, content.Current);
        if (!result.Ok) { ctx.SendError(result.ErrorCode!, msg.ReqId); return; }
        if (msg.From.C == "equip" || msg.To.C == "equip")
        {
            deps.Combat.Services.Recalculate(p);
            ctx.Tick.Emit(new Game.Progression.StatsChangedEvent(map.Id, p));
        }
        p.Dirty = true;
        ctx.Tick.Emit(new InventoryChangedEvent(map.Id, p, msg.ReqId));
    }

    /// <summary>Items ofrecidos en un intercambio: no se mueven, usan, venden ni destruyen (HU-059).</summary>
    internal static bool IsLockedSlot(Game.Entities.Player p, Protocol.Messages.SlotRef slot, CombatHandlerDeps deps)
    {
        if (slot.C != "bag" || slot.I < 0 || slot.I >= Inventory.BagSize) return false;
        var item = p.Inventory.Bag[slot.I];
        return item is not null && deps.Combat.Trades.IsLocked(p, item.Id);
    }
}

file static class LockCheck
{
    public static bool Locked(Game.Entities.Player p, Guid id, CombatHandlerDeps deps) => deps.Combat.Trades.IsLocked(p, id);
}

public sealed class UseItemHandler(CombatHandlerDeps deps) : IMessageHandler<UseItem>
{
    public void Handle(UseItem msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        if (!Guid.TryParse(msg.ItemId, out var id)) { ctx.SendError(ErrorCodes.InvalidPayload, msg.ReqId); return; }
        if (LockCheck.Locked(p, id, deps)) { ctx.SendError(ErrorCodes.TradeBusy, msg.ReqId); return; }
        var error = deps.Combat.ItemUse.Use(p, id, map, ctx.Tick);
        if (error is not null) ctx.SendError(error, msg.ReqId);
    }
}

public sealed class DestroyItemHandler(CombatHandlerDeps deps, ReloadableContent content) : IMessageHandler<DestroyItem>
{
    public void Handle(DestroyItem msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        if (!Guid.TryParse(msg.ItemId, out var id)) { ctx.SendError(ErrorCodes.InvalidPayload, msg.ReqId); return; }
        if (LockCheck.Locked(p, id, deps)) { ctx.SendError(ErrorCodes.TradeBusy, msg.ReqId); return; }
        var result = InventoryOps.Remove(p, id, msg.Qty, "destroy", content.Current);
        if (!result.Ok) { ctx.SendError(result.ErrorCode!, msg.ReqId); return; }
        ctx.Tick.Emit(new InventoryChangedEvent(map.Id, p, msg.ReqId));
    }
}

public sealed class LootOpenHandler(CombatHandlerDeps deps) : IMessageHandler<LootOpen>
{
    public void Handle(LootOpen msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var (bag, error) = deps.Combat.Loot.Open(p, new EntityId(msg.LootId), map, ctx.Tick);
        if (bag is null) { ctx.SendError(error ?? ErrorCodes.NotFound); return; }
        ctx.Send(LootWindowFor(bag, p, map, ctx.Tick.NowMs));
    }

    public static LootWindow LootWindowFor(LootBag bag, Game.Entities.Player viewer, Game.Map.MapInstance map, long nowMs)
    {
        var items = new List<LootEntryDto>(bag.Entries.Count);
        foreach (var e in bag.Entries)
        {
            var owner = map.Players.Values.FirstOrDefault(pl => pl.CharacterId == e.OwnerCharacterId);
            items.Add(new LootEntryDto(e.Index, e.TemplateId, e.Qty, owner?.Id.Value ?? 0, (int)Math.Max(0, e.FreeAtMs - nowMs)));
        }
        // Oro de este cadáver para quien mira: se cobra al abrir, pero la ventana lo sigue enseñando (antes salía siempre 0).
        return new LootWindow(bag.LootId.Value, bag.GoldFor(viewer.CharacterId), items);
    }
}

public sealed class LootTakeHandler(CombatHandlerDeps deps) : IMessageHandler<LootTake>
{
    public void Handle(LootTake msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Loot.Take(p, new EntityId(msg.LootId), msg.Index, map, ctx.Tick);
        if (error is not null) { ctx.SendError(error); return; }
        if (deps.Combat.Loot.Get(map, new EntityId(msg.LootId)) is { } bag) ctx.Send(LootOpenHandler.LootWindowFor(bag, p, map, ctx.Tick.NowMs));
    }
}

public sealed class LootTakeAllHandler(CombatHandlerDeps deps) : IMessageHandler<LootTakeAll>
{
    public void Handle(LootTakeAll msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Loot.TakeAll(p, new EntityId(msg.LootId), map, ctx.Tick);
        if (error is not null) ctx.SendError(error);
        // La ventana solo a quien puede saquear: con not_owner, out_of_range o is_dead revelaría el botín de cualquier cadáver.
        if (error is null or "bag_full" && deps.Combat.Loot.Get(map, new EntityId(msg.LootId)) is { } bag) ctx.Send(LootOpenHandler.LootWindowFor(bag, p, map, ctx.Tick.NowMs));
    }
}

public sealed class VendorOpenHandler(CombatHandlerDeps deps, ReloadableContent content) : IMessageHandler<VendorOpen>
{
    public void Handle(VendorOpen msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var npc = deps.Combat.Vendor.FindVendor(p, new EntityId(msg.NpcId), map, ctx.Tick, out var error);
        if (npc is null) { ctx.SendError(error ?? ErrorCodes.NotFound); return; }
        var db = content.Current;
        var vendor = db.Vendor(npc.VendorId!);
        var items = vendor.Items.Where(id => db.TryGetItem(id, out _)).Select(id => new VendorItemDto(id, InventoryOps.BuyPrice(db.Item(id), db.Rules.Economy))).ToList();
        ctx.Send(new VendorWindow(npc.Id.Value, items));
    }
}

public sealed class VendorBuyHandler(CombatHandlerDeps deps) : IMessageHandler<VendorBuy>
{
    public void Handle(VendorBuy msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var error = deps.Combat.Vendor.Buy(p, new EntityId(msg.NpcId), msg.TemplateId, msg.Qty, map, ctx.Tick, null);
        if (error is not null) ctx.SendError(error);
    }
}

public sealed class VendorSellHandler(CombatHandlerDeps deps) : IMessageHandler<VendorSell>
{
    public void Handle(VendorSell msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        if (!Guid.TryParse(msg.ItemId, out var id)) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
        if (LockCheck.Locked(p, id, deps)) { ctx.SendError(ErrorCodes.TradeBusy); return; }
        var error = deps.Combat.Vendor.Sell(p, new EntityId(msg.NpcId), id, msg.Qty, map, ctx.Tick, null);
        if (error is not null) ctx.SendError(error);
    }
}
