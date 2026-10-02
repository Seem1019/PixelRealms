using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Social;

public sealed record TradeOfferState(List<(Guid ItemId, int Qty)> Items, long Gold)
{
    public static TradeOfferState Empty => new(new List<(Guid, int)>(), 0);
}

public enum TradeState { Requested, Open, Completed, Cancelled }

/// <summary>Intercambio entre dos jugadores (HU-059): ofertas versionadas, doble confirmación, commit atómico.</summary>
public sealed class TradeSession(Player a, Player b, long requestedAtMs)
{
    public const int MaxItems = 6;
    public Player A { get; } = a;
    public Player B { get; } = b;
    public TradeState State { get; set; } = TradeState.Requested;
    public long RequestedAtMs { get; } = requestedAtMs;
    public TradeOfferState OfferA { get; set; } = TradeOfferState.Empty;
    public TradeOfferState OfferB { get; set; } = TradeOfferState.Empty;
    public bool ConfirmedA { get; set; }
    public bool ConfirmedB { get; set; }
    public int Version { get; set; } = 1;

    public bool Involves(Player p) => ReferenceEquals(p, A) || ReferenceEquals(p, B);
    public Player Partner(Player p) => ReferenceEquals(p, A) ? B : A;
    public TradeOfferState OfferOf(Player p) => ReferenceEquals(p, A) ? OfferA : OfferB;
    public bool ConfirmedBy(Player p) => ReferenceEquals(p, A) ? ConfirmedA : ConfirmedB;

    /// <summary>Items bloqueados por este intercambio (no se mueven, usan, venden ni destruyen).</summary>
    public bool IsLocked(Guid itemId) => OfferA.Items.Any(i => i.ItemId == itemId) || OfferB.Items.Any(i => i.ItemId == itemId);
}

public sealed record TradeChangedEvent(int MapInstanceId, TradeSession Trade, string State, string? Reason) : IGameEvent;

/// <summary>
/// HU-059: solicitud (≤ 3 casillas, caduca 30 s), ofertas en vivo (hasta 6 items + oro; cualquier cambio desmarca ambas
/// confirmaciones y sube `version`), confirmación con `version`, commit atómico (propiedad, cantidades, espacio, oro ≥ 0) y
/// cancelación por distancia, desconexión, muerte o `TradeCancel`. Auditoría `trade_out`/`trade_in` con la contraparte.
/// </summary>
public sealed class TradeService(CombatServices services)
{
    public const double RangeTiles = 3.0;
    public const int RequestExpireMs = 30_000;

    private readonly Dictionary<int, List<TradeSession>> _trades = new();

    /// <summary>¿El jugador está retando o en un duelo? No se intercambia en pleno duelo (lo rellena CombatModule).</summary>
    public Func<Player, bool> InDuel { get; set; } = static _ => false;

    public TradeSession? TradeOf(Player p)
    {
        if (!_trades.TryGetValue(p.MapInstanceId, out var list)) return null;
        foreach (var t in list) if (t.State is TradeState.Requested or TradeState.Open && t.Involves(p)) return t;
        return null;
    }

    public bool IsLocked(Player p, Guid itemId) => TradeOf(p) is { State: TradeState.Open } t && t.IsLocked(itemId);

    public string? Request(Player from, Player to, MapInstance map, TickContext ctx)
    {
        if (ReferenceEquals(from, to) || from.MapInstanceId != to.MapInstanceId) return "invalid_target";
        if (Vec2.Distance(from.Position, to.Position) > RangeTiles) return "out_of_range";
        if (TradeOf(from) is not null || TradeOf(to) is not null) return "trade_busy";
        if (InDuel(from) || InDuel(to)) return "duel_busy";
        var trade = new TradeSession(from, to, ctx.NowMs);
        if (!_trades.TryGetValue(map.Id, out var list)) _trades[map.Id] = list = new List<TradeSession>();
        list.Add(trade);
        ctx.Emit(new TradeChangedEvent(map.Id, trade, "requested", null));
        return null;
    }

    public string? Respond(Player target, bool accept, MapInstance map, TickContext ctx)
    {
        var trade = TradeOf(target);
        if (trade is null || trade.State != TradeState.Requested || !ReferenceEquals(trade.B, target)) return "not_found";
        if (!accept) { Cancel(trade, "declined", map, ctx); return null; }
        if (InDuel(trade.A) || InDuel(trade.B)) { Cancel(trade, "duel_busy", map, ctx); return "duel_busy"; }
        trade.State = TradeState.Open;
        ctx.Emit(new TradeChangedEvent(map.Id, trade, "open", null));
        return null;
    }

    public string? Offer(Player p, IReadOnlyList<(Guid ItemId, int Qty)> items, long gold, MapInstance map, TickContext ctx)
    {
        var trade = TradeOf(p);
        if (trade is null || trade.State != TradeState.Open) return "not_found";
        if (items.Count > TradeSession.MaxItems || gold < 0 || gold > p.Inventory.Gold) return "invalid_payload";
        var seen = new HashSet<Guid>();
        foreach (var (itemId, qty) in items)
        {
            var inst = InventoryOps.Find(p.Inventory, itemId);
            if (inst is null) return "not_found";
            if (qty <= 0 || qty > inst.Qty || !seen.Add(itemId)) return "invalid_payload";
        }
        var offer = new TradeOfferState(items.ToList(), gold);
        if (ReferenceEquals(p, trade.A)) trade.OfferA = offer; else trade.OfferB = offer;
        trade.ConfirmedA = false; trade.ConfirmedB = false; // cualquier cambio desmarca a ambos (CA2)
        trade.Version++;
        ctx.Emit(new TradeChangedEvent(map.Id, trade, "open", null));
        return null;
    }

    public string? Confirm(Player p, int version, MapInstance map, TickContext ctx)
    {
        var trade = TradeOf(p);
        if (trade is null || trade.State != TradeState.Open) return "not_found";
        if (version != trade.Version) return "trade_version";
        if (ReferenceEquals(p, trade.A)) trade.ConfirmedA = true; else trade.ConfirmedB = true;
        if (trade.ConfirmedA && trade.ConfirmedB)
        {
            var error = Commit(trade, map, ctx);
            if (error is not null)
            {
                trade.ConfirmedA = false; trade.ConfirmedB = false; trade.Version++;
                ctx.Emit(new TradeChangedEvent(map.Id, trade, "open", error));
                return error;
            }
            return null;
        }
        ctx.Emit(new TradeChangedEvent(map.Id, trade, "open", null));
        return null;
    }

    public string? CancelBy(Player p, string reason, MapInstance map, TickContext ctx)
    {
        var trade = TradeOf(p);
        if (trade is null) return "not_found";
        Cancel(trade, reason, map, ctx);
        return null;
    }

    private void Cancel(TradeSession trade, string reason, MapInstance map, TickContext ctx)
    {
        trade.State = TradeState.Cancelled;
        ctx.Emit(new TradeChangedEvent(map.Id, trade, "cancelled", reason));
        if (_trades.TryGetValue(map.Id, out var list)) list.Remove(trade);
    }

    /// <summary>Validar todo (propiedad, cantidades, espacio en ambas bolsas, oro) y mover en una sola operación (CA3).</summary>
    private string? Commit(TradeSession trade, MapInstance map, TickContext ctx)
    {
        var db = services.Content;
        foreach (var (giver, offer) in new[] { (trade.A, trade.OfferA), (trade.B, trade.OfferB) })
        {
            if (offer.Gold > giver.Inventory.Gold) return "not_enough_gold";
            foreach (var (itemId, qty) in offer.Items)
            {
                var inst = InventoryOps.Find(giver.Inventory, itemId);
                if (inst is null || inst.Qty < qty) return "not_found";
            }
        }
        // Espacio: simulamos sobre copias de las bolsas (quitando lo que sale, añadiendo lo que entra).
        if (!FitsAfterTrade(trade.A, trade.OfferA, trade.B, trade.OfferB, db) || !FitsAfterTrade(trade.B, trade.OfferB, trade.A, trade.OfferA, db)) return "bag_full";

        // Mutación: primero quitar todo, luego añadir todo (ya validado que cabe).
        var toA = Collect(trade.B, trade.OfferB, trade.A.CharacterId, db);
        var toB = Collect(trade.A, trade.OfferA, trade.B.CharacterId, db);
        trade.A.Inventory.Gold += trade.OfferB.Gold - trade.OfferA.Gold;
        trade.B.Inventory.Gold += trade.OfferA.Gold - trade.OfferB.Gold;
        foreach (var (tpl, qty) in toA) InventoryOps.AddItem(trade.A, tpl, qty, "trade_in", counterparty: trade.B.CharacterId);
        foreach (var (tpl, qty) in toB) InventoryOps.AddItem(trade.B, tpl, qty, "trade_in", counterparty: trade.A.CharacterId);
        trade.A.Dirty = true; trade.B.Dirty = true;
        trade.State = TradeState.Completed;
        ctx.Emit(new TradeChangedEvent(map.Id, trade, "completed", null));
        ctx.Emit(new InventoryChangedEvent(map.Id, trade.A, null));
        ctx.Emit(new InventoryChangedEvent(map.Id, trade.B, null));
        if (_trades.TryGetValue(map.Id, out var list)) list.Remove(trade);
        return null;
    }

    private static List<(ItemTemplate Tpl, int Qty)> Collect(Player giver, TradeOfferState offer, Guid counterparty, Content.ContentDb db)
    {
        var result = new List<(ItemTemplate, int)>();
        foreach (var (itemId, qty) in offer.Items)
        {
            var inst = InventoryOps.Find(giver.Inventory, itemId)!;
            result.Add((db.Item(inst.TemplateId), qty));
            InventoryOps.Remove(giver, itemId, qty, "trade_out", db, counterparty);
        }
        return result;
    }

    /// <summary>¿Le cabe al receptor lo que recibe después de quitar lo que entrega? Simulación sobre una copia de su bolsa.</summary>
    private static bool FitsAfterTrade(Player receiver, TradeOfferState giving, Player partner, TradeOfferState receiving, Content.ContentDb db)
    {
        var sim = new Player(new EntityId(0), receiver.Name, receiver.ClassId) { CharacterId = receiver.CharacterId, Level = receiver.Level };
        for (var i = 0; i < Inventory.BagSize; i++)
            if (receiver.Inventory.Bag[i] is { } it) sim.Inventory.Bag[i] = new ItemInstance(it.Id, it.TemplateId, it.Qty);
        foreach (var (itemId, qty) in giving.Items)
        {
            var idx = InventoryOps.IndexOf(sim.Inventory, itemId);
            if (idx < 0) return false;
            sim.Inventory.Bag[idx]!.Qty -= qty;
            if (sim.Inventory.Bag[idx]!.Qty <= 0) sim.Inventory.Bag[idx] = null;
        }
        foreach (var (itemId, qty) in receiving.Items)
        {
            var inst = InventoryOps.Find(partner.Inventory, itemId);
            if (inst is null) return false;
            if (!InventoryOps.AddItem(sim, db.Item(inst.TemplateId), qty, "sim").Ok) return false;
        }
        return true;
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        if (!_trades.TryGetValue(map.Id, out var list) || list.Count == 0) return;
        foreach (var t in list.ToList())
        {
            if (t.State == TradeState.Requested && ctx.NowMs - t.RequestedAtMs > RequestExpireMs) { Cancel(t, "expired", map, ctx); continue; }
            if (t.A.IsDead || t.B.IsDead) { Cancel(t, "died", map, ctx); continue; }
            if (t.A.ConnectionId < 0 || t.B.ConnectionId < 0) { Cancel(t, "disconnected", map, ctx); continue; }
            if (t.A.MapInstanceId != map.Id || t.B.MapInstanceId != map.Id || Vec2.Distance(t.A.Position, t.B.Position) > RangeTiles) { Cancel(t, "distance", map, ctx); }
        }
    }
}
