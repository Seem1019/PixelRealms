using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Items;

/// <summary>Una entrada del cadáver: item asignado a un dueño; libre para los demás elegibles desde `FreeAtMs`.</summary>
public sealed class LootEntryState(int index, string templateId, int qty, Guid ownerCharacterId, long freeAtMs)
{
    public int Index { get; } = index;
    public string TemplateId { get; } = templateId;
    public int Qty { get; } = qty;
    public Guid OwnerCharacterId { get; } = ownerCharacterId;
    public long FreeAtMs { get; } = freeAtMs;
}

/// <summary>Botín de un cadáver (ADR-012): oro repartido a partes iguales entre elegibles y cada item asignado al azar a uno.</summary>
public sealed class LootBag(EntityId lootId, Vec2 position, long expiresAtMs)
{
    public EntityId LootId { get; } = lootId;
    public Vec2 Position { get; } = position;
    public long ExpiresAtMs { get; } = expiresAtMs;
    public List<LootEntryState> Entries { get; } = new();
    /// <summary>Oro pendiente por elegible (characterId → cobre).</summary>
    public Dictionary<Guid, long> GoldShares { get; } = new();

    /// <summary>Oro ya cobrado por elegible al abrir: la ventana lo sigue mostrando (HU-050 CA2).</summary>
    public Dictionary<Guid, long> GoldCollected { get; } = new();

    /// <summary>Lo que sobra al dividir el oro entre los elegibles: para el primero que abra (HU-062 CA3).</summary>
    public long GoldRemainder { get; set; }

    /// <summary>Oro de este cadáver para `characterId`, cobrado o no: lo que muestra `LootWindow.gold`.</summary>
    public long GoldFor(Guid characterId) => GoldShares.GetValueOrDefault(characterId) + GoldCollected.GetValueOrDefault(characterId);

    public HashSet<Guid> Eligible { get; } = new();

    /// <summary>Hubo algo que saquear (si cayó vacío, el cadáver dura su tiempo normal).</summary>
    public bool HadLoot { get; set; }

    public bool IsEmpty => Entries.Count == 0 && GoldRemainder <= 0 && GoldShares.Values.All(g => g <= 0);

    public bool HasLootFor(Guid characterId, long nowMs)
    {
        if (GoldShares.TryGetValue(characterId, out var g) && g > 0) return true;
        if (GoldRemainder > 0 && Eligible.Contains(characterId)) return true;
        foreach (var e in Entries) if (e.OwnerCharacterId == characterId || nowMs >= e.FreeAtMs) return true;
        return false;
    }
}

/// <summary>Evento: hay botín nuevo para estos jugadores (el cadáver brilla, HU-050 CA1).</summary>
public sealed record LootAvailableEvent(int MapInstanceId, LootBag Bag, IReadOnlyList<Player> Winners) : IGameEvent;

/// <summary>HU-062 CA4 / HU-083 CA4: un item uncommon+ cayó para `Winner`; `Global` si lo soltó un jefe (si no, chat de grupo).</summary>
public sealed record LootAnnouncedEvent(int MapInstanceId, Player Winner, string TemplateId, Rarity Rarity, bool Global) : IGameEvent;

/// <summary>El inventario de un jugador cambió (botín, uso, compra, venta…): el servidor envía InventoryUpdate.</summary>
public sealed record InventoryChangedEvent(int MapInstanceId, Player Player, int? ReqId) : IGameEvent;

/// <summary>
/// HU-050: al morir un monstruo taggeado tira su tabla (`entries` independientes + `groups` garantizados, `maxItems`), asigna
/// cada item al azar entre los elegibles (vivos a ≤ `eligibleRangeTiles`) y reparte el oro a partes iguales; `LootOpen`/`LootTake`
/// con `exclusiveSec` y `lootRangeTiles`. Las bolsas viven mientras el cadáver (DeathSystem pregunta `IsLooted`).
/// </summary>
public sealed class LootSystem(CombatServices services) : IMapSystem
{
    private readonly Dictionary<(int Map, int Loot), LootBag> _bags = new();
    private readonly List<(int Map, int Loot)> _expired = new();

    public string Name => "loot";

    /// <summary>Elegibles para el botín de un monstruo (HU-062 lo amplía al grupo). Por defecto: quien lo taggeó.</summary>
    public Func<Player, Monster, MapInstance, IReadOnlyList<Player>> EligibleFor { get; set; } = static (tagger, _, _) => [tagger];

    public LootBag? Get(MapInstance map, EntityId lootId) => _bags.GetValueOrDefault((map.Id, lootId.Value));

    public bool IsLooted(Monster m) => _bags.TryGetValue((m.MapInstanceId, m.Id.Value), out var bag) && bag.HadLoot && bag.IsEmpty;

    public void Forget(MapInstance map, EntityId lootId) => _bags.Remove((map.Id, lootId.Value));

    /// <summary>Tirada pura de una tabla (CA6): entries independientes, groups por peso sin repetir, orden por rareza, corte a maxItems.</summary>
    public static (long Gold, List<(string TemplateId, int Qty)> Items) Roll(LootTable table, IRng rng, ContentDb db)
    {
        var gold = table.Gold.Max <= table.Gold.Min ? table.Gold.Min : rng.Next(table.Gold.Min, table.Gold.Max + 1);
        var fromEntries = new List<(string ItemId, int Qty, Rarity Rarity)>();
        foreach (var e in table.Entries)
        {
            if (rng.NextDouble() >= e.Chance) continue;
            var qty = e.Max <= e.Min ? e.Min : rng.Next(e.Min, e.Max + 1);
            fromEntries.Add((e.ItemId, qty, db.Item(e.ItemId).Rarity));
        }
        fromEntries.Sort(static (a, b) => b.Rarity.CompareTo(a.Rarity));
        if (fromEntries.Count > table.MaxItems) fromEntries.RemoveRange(table.MaxItems, fromEntries.Count - table.MaxItems);

        var result = new List<(string, int)>(fromEntries.Count);
        foreach (var (itemId, qty, _) in fromEntries) result.Add((itemId, qty));
        foreach (var g in table.Groups)
        {
            var pool = new List<LootGroupEntry>(g.Entries);
            for (var r = 0; r < g.Rolls && pool.Count > 0; r++)
            {
                var total = 0.0;
                foreach (var x in pool) total += x.Weight;
                var roll = rng.NextDouble() * total;
                var pick = pool[^1];
                foreach (var cand in pool) { roll -= cand.Weight; if (roll < 0) { pick = cand; break; } }
                pool.Remove(pick);
                var qty = pick.Max <= pick.Min ? pick.Min : rng.Next(pick.Min, pick.Max + 1);
                result.Add((pick.ItemId, qty));
            }
        }
        return (gold, result);
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        var count = ctx.Events.Count;
        for (var i = 0; i < count; i++)
        {
            if (ctx.Events[i] is not ActorDiedEvent { Victim: Monster monster } died || died.MapInstanceId != map.Id) continue;
            var taggerId = monster.TaggedBy ?? died.Killer?.Id;
            if (taggerId is null || map.Find(taggerId.Value) is not Player tagger) continue;
            CreateBag(monster, tagger, map, ctx);
        }
        // Caducidad: va de la mano del cadáver; por seguridad, limpiar bolsas vencidas de esta instancia.
        if (_bags.Count == 0) return;
        _expired.Clear();
        foreach (var (key, bag) in _bags)
            if (key.Map == map.Id && bag.ExpiresAtMs <= ctx.NowMs) _expired.Add(key);
        foreach (var key in _expired) _bags.Remove(key);
    }

    public LootBag? CreateBag(Monster monster, Player tagger, MapInstance map, TickContext ctx)
    {
        var db = services.Content;
        var rules = ctx.Rules;
        var eligible = new List<Player>();
        foreach (var p in EligibleFor(tagger, monster, map))
            if (p.IsAlive && Vec2.Distance(p.Position, monster.Position) <= rules.Loot.EligibleRangeTiles) eligible.Add(p);
        if (eligible.Count == 0) eligible.Add(tagger);
        var (gold, items) = Roll(db.LootTable(monster.Template.LootTableId), ctx.Rng, db);
        var bag = new LootBag(monster.Id, monster.Position, ctx.NowMs + (long)(rules.Combat.CorpseLifetimeSec * 1000));
        foreach (var p in eligible) bag.Eligible.Add(p.CharacterId);
        // Oro a partes iguales; el resto al primero que abra (ver Open).
        var share = gold / eligible.Count;
        foreach (var p in eligible) bag.GoldShares[p.CharacterId] = share;
        bag.GoldRemainder = gold - share * eligible.Count;
        var winners = new HashSet<Player>();
        var index = 0;
        foreach (var (templateId, qty) in items)
        {
            var owner = eligible[ctx.Rng.Next(0, eligible.Count)];
            bag.Entries.Add(new LootEntryState(index++, templateId, qty, owner.CharacterId, ctx.NowMs + (long)(rules.Loot.ExclusiveSec * 1000)));
            winners.Add(owner);
            var rarity = db.Item(templateId).Rarity;
            if (rarity >= Rarity.Uncommon) ctx.Emit(new LootAnnouncedEvent(map.Id, owner, templateId, rarity, monster.Template.Type == MonsterType.Boss));
        }
        bag.HadLoot = items.Count > 0 || gold > 0;
        _bags[(map.Id, monster.Id.Value)] = bag;
        ctx.Emit(new LootAvailableEvent(map.Id, bag, [.. winners]));
        return bag;
    }

    /// <summary>LootOpen: distancia ≤ lootRangeTiles y elegible; cobra la parte de oro pendiente. Devuelve la bolsa o un código de error.</summary>
    public (LootBag? Bag, string? Error) Open(Player p, EntityId lootId, MapInstance map, TickContext ctx)
    {
        var bag = Get(map, lootId);
        if (bag is null) return (null, "not_found");
        if (p.IsDead) return (null, "is_dead"); // HU-037 CA3
        if (Vec2.Distance(p.Position, bag.Position) > ctx.Rules.Loot.LootRangeTiles) return (null, "out_of_range");
        if (!bag.Eligible.Contains(p.CharacterId)) return (null, "not_owner");
        var gold = bag.GoldShares.GetValueOrDefault(p.CharacterId) + bag.GoldRemainder;
        if (gold > 0)
        {
            p.Inventory.Gold += gold;
            bag.GoldShares[p.CharacterId] = 0;
            bag.GoldRemainder = 0;
            bag.GoldCollected[p.CharacterId] = bag.GoldCollected.GetValueOrDefault(p.CharacterId) + gold;
            p.Dirty = true;
            ctx.Emit(new InventoryChangedEvent(map.Id, p, null));
        }
        return (bag, null);
    }

    /// <summary>LootTake: dueño o ya libre, espacio en bolsa; crea instancias nuevas (ids nuevos) y audita "loot".</summary>
    public string? Take(Player p, EntityId lootId, int index, MapInstance map, TickContext ctx)
    {
        var bag = Get(map, lootId);
        if (bag is null) return "not_found";
        if (p.IsDead) return "is_dead"; // HU-037 CA3
        if (Vec2.Distance(p.Position, bag.Position) > ctx.Rules.Loot.LootRangeTiles) return "out_of_range";
        if (!bag.Eligible.Contains(p.CharacterId)) return "not_owner";
        var entry = bag.Entries.FirstOrDefault(e => e.Index == index);
        if (entry is null) return "not_found";
        if (entry.OwnerCharacterId != p.CharacterId && ctx.NowMs < entry.FreeAtMs) return "not_owner";
        var tpl = services.Content.Item(entry.TemplateId);
        var result = InventoryOps.AddItem(p, tpl, entry.Qty, "loot");
        if (!result.Ok) return result.ErrorCode;
        bag.Entries.Remove(entry);
        ctx.Emit(new InventoryChangedEvent(map.Id, p, null));
        return null;
    }

    /// <summary>LootTakeAll: toma todo lo mío (o libre) que quepa; devuelve el primer error de espacio si lo hubo.</summary>
    public string? TakeAll(Player p, EntityId lootId, MapInstance map, TickContext ctx)
    {
        var bag = Get(map, lootId);
        if (bag is null) return "not_found";
        if (p.IsDead) return "is_dead"; // HU-037 CA3
        // Las mismas comprobaciones que Open aunque no haya nada que tomar: si no, un extraño recibía la ventana con el botín ajeno.
        if (Vec2.Distance(p.Position, bag.Position) > ctx.Rules.Loot.LootRangeTiles) return "out_of_range";
        if (!bag.Eligible.Contains(p.CharacterId)) return "not_owner";
        string? lastError = null;
        foreach (var entry in bag.Entries.ToList())
        {
            if (entry.OwnerCharacterId != p.CharacterId && ctx.NowMs < entry.FreeAtMs) continue;
            var err = Take(p, lootId, entry.Index, map, ctx);
            if (err is not null) lastError = err;
        }
        return lastError;
    }
}
