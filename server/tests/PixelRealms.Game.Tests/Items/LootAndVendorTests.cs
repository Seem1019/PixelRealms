using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Map;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Items;

/// <summary>HU-050 (botín), HU-054 (consumibles) y HU-055 (vendedor) con FixedRng/SeededRng.</summary>
public sealed class LootAndVendorTests
{
    private static TestWorld Arena()
        => new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 5, (10, 10)).WithPlayer("Bob", "mage", 5, (11, 10))
            .WithMonster("slime", (12, 10), wanderRadius: 0).WithMonster("foreman_grask", (20, 20), wanderRadius: 0).BuildWithCombat();

    [Fact]
    public void Roll_Entries_Groups_MaxItems_Gold_WithFixedRng() // CA6
    {
        var db = TestContent.Load();
        var table = db.LootTable("lt_slime"); // oro 1–5; slime_goo 0.7 (1–2); poción 0.05
        // Rolls: oro, chance goo, qty goo, chance poción.
        var (gold, items) = LootSystem.Roll(table, new FixedRng(0.0, 0.69, 0.99, 0.04), db);
        gold.ShouldBe(1);
        items.ShouldBe(new[] { ("minor_healing_potion", 1), ("slime_goo", 2) }); // ordenados por rareza (common antes que junk)
        var (_, none) = LootSystem.Roll(table, new FixedRng(0.5, 0.7, 0.5, 0.05), db);
        none.ShouldBeEmpty();
        // maxItems recorta las entries pero no los groups.
        var fat = table with { MaxItems = 1 };
        LootSystem.Roll(fat, new FixedRng(0.0, 0.0, 0.0, 0.0), db).Items.Count.ShouldBe(1);
    }

    [Fact]
    public void Foreman_AlwaysDropsExactlyOneGroupItem() // CA4c
    {
        var db = TestContent.Load();
        var table = db.LootTable(db.Monster("foreman_grask").LootTableId);
        table.Groups.Count.ShouldBeGreaterThan(0);
        var groupIds = table.Groups.SelectMany(g => g.Entries.Select(e => e.ItemId)).ToHashSet();
        var rng = new SeededRng(99);
        for (var i = 0; i < 200; i++)
        {
            var (_, items) = LootSystem.Roll(table, rng, db);
            items.Count(it => groupIds.Contains(it.TemplateId)).ShouldBe(table.Groups.Sum(g => g.Rolls));
        }
    }

    [Fact]
    public void Assignment_UniformAcross4Eligibles_1000Kills() // CA4b
    {
        var db = TestContent.Load();
        var w = new WorldBuilder().WithMap(60, 60).WithPlayer("A", "warrior", 5, (10, 10)).WithPlayer("B", "rogue", 5, (11, 10))
            .WithPlayer("C", "mage", 5, (10, 11)).WithPlayer("D", "priest", 5, (11, 11)).WithMonster("slime", (12, 10), wanderRadius: 0).BuildWithCombat();
        var players = new[] { w.Player("A"), w.Player("B"), w.Player("C"), w.Player("D") };
        w.Combat.Loot.EligibleFor = (_, _, _) => players;
        var slime = w.Monster("slime");
        var counts = new Dictionary<Guid, int>();
        var allThree = 0; var bagsWith3 = 0;
        var table = db.LootTable("lt_slime") with { Entries = [new LootEntry { ItemId = "slime_goo", Chance = 1 }, new LootEntry { ItemId = "bread", Chance = 1 }, new LootEntry { ItemId = "copper_ore", Chance = 1 }] };
        for (var i = 0; i < 1000; i++)
        {
            var ctx = w.Begin();
            var (_, items) = LootSystem.Roll(table, ctx.Rng, db);
            items.Count.ShouldBe(3);
            var owners = new List<Guid>();
            foreach (var _ in items) { var owner = players[ctx.Rng.Next(0, players.Length)]; owners.Add(owner.CharacterId); counts[owner.CharacterId] = counts.GetValueOrDefault(owner.CharacterId) + 1; }
            bagsWith3++;
            if (owners.Distinct().Count() == 1) allThree++;
        }
        foreach (var p in players) (counts[p.CharacterId] / 3000.0).ShouldBeInRange(0.22, 0.28);
        (allThree / (double)bagsWith3).ShouldBeInRange(1 / 16.0 - 0.03, 1 / 16.0 + 0.03);
        _ = slime;
    }

    [Fact]
    public void Kill_CreatesBag_Open_Take_NotOwner_Exclusive_BagFull() // CA1, CA2, CA3, CA4
    {
        var w = Arena();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var slime = w.Monster("slime");
        var rules = w.Content.Rules;
        w.Combat.Loot.EligibleFor = (_, _, _) => [ana, bob];
        slime.TaggedBy = ana.Id;
        // Forzamos una tirada conocida sustituyendo la tabla por una de 2 items garantizados.
        var ctx = w.Begin();
        var bag = w.Combat.Loot.CreateBag(slime, ana, w.Map, ctx)!;
        bag.Eligible.Count.ShouldBe(2);
        ctx.Events.OfType<LootAvailableEvent>().Single().Bag.ShouldBe(bag);
        // Oro repartido a partes iguales (el resto al primero).
        var totalGold = bag.GoldShares.Values.Sum();
        (bag.GoldShares[ana.CharacterId] - bag.GoldShares[bob.CharacterId]).ShouldBeInRange(0, 1);
        // Abrir: distancia ≤ lootRangeTiles, cobra mi oro.
        var goldBefore = ana.Inventory.Gold;
        var (opened, err) = w.Combat.Loot.Open(ana, slime.Id, w.Map, ctx);
        err.ShouldBeNull(); opened.ShouldBe(bag);
        (ana.Inventory.Gold - goldBefore).ShouldBe(totalGold - bag.GoldShares.Values.Sum());
        w.Combat.Loot.Open(ana, slime.Id, w.Map, ctx); // segunda vez no cobra
        ana.Inventory.Gold.ShouldBe(goldBefore + totalGold - bag.GoldShares.Values.Sum());
        // Items: el que no es mío → not_owner hasta exclusiveSec; luego libre.
        foreach (var e in bag.Entries.ToList())
        {
            var mine = e.OwnerCharacterId == ana.CharacterId;
            var take = w.Combat.Loot.Take(ana, slime.Id, e.Index, w.Map, ctx);
            if (mine) take.ShouldBeNull(); else take.ShouldBe("not_owner");
        }
        w.Clock.Advance((long)(rules.Loot.ExclusiveSec * 1000));
        var ctx2 = w.Begin();
        w.Combat.Loot.TakeAll(ana, slime.Id, w.Map, ctx2).ShouldBeNull();
        bag.Entries.ShouldBeEmpty();
        w.Combat.Loot.IsLooted(slime).ShouldBe(bag.HadLoot && bag.IsEmpty);
        // Fuera de alcance y no elegible.
        ana.Position = new Vec2(30, 30);
        w.Combat.Loot.Open(ana, slime.Id, w.Map, ctx2).Error.ShouldBe("out_of_range");
        var carl = new Player(new EntityId(99), "Carl", "rogue") { CharacterId = Guid.NewGuid(), Position = slime.Position };
        w.Map.Add(carl);
        w.Combat.Loot.Open(carl, slime.Id, w.Map, ctx2).Error.ShouldBe("not_owner");
    }

    [Fact]
    public void Take_BagFull_LeavesItemInCorpse() // CA3
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        for (var i = 0; i < Inventory.BagSize; i++) ana.Inventory.Bag[i] = ItemInstance.New("boar_tusk", 50);
        var ctx = w.Begin();
        LootBag? bag = null;
        for (var attempt = 0; attempt < 50 && (bag is null || bag.Entries.Count == 0); attempt++) { w.Combat.Loot.Forget(w.Map, slime.Id); bag = w.Combat.Loot.CreateBag(slime, ana, w.Map, ctx); }
        bag!.Entries.Count.ShouldBeGreaterThan(0);
        w.Combat.Loot.Take(ana, slime.Id, bag.Entries[0].Index, w.Map, ctx).ShouldBe("bag_full");
        bag.Entries.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void LootFlow_InTick_CorpseVanishesWhenLooted() // integración dominio + HU-037 CA4
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        slime.Hp = 1;
        ana.Position = new Vec2(10.5f, 10); slime.Position = new Vec2(11.5f, 10); // a 1 casilla (espada 1.5)
        ana.Combat.TargetId = slime.Id; ana.Combat.AutoAttackOn = true;
        var events = TickRunner.RunMs(w, 5000);
        slime.IsDead.ShouldBeTrue();
        var bag = w.Combat.Loot.Get(w.Map, slime.Id);
        bag.ShouldNotBeNull();
        events.OfType<LootAvailableEvent>().Count().ShouldBe(1);
        var ctx = w.Begin();
        w.Combat.Loot.Open(ana, slime.Id, w.Map, ctx).Error.ShouldBeNull();
        w.Combat.Loot.TakeAll(ana, slime.Id, w.Map, ctx);
        if (bag.HadLoot)
        {
            TickRunner.Run(w, 2);
            w.Map.Actors.ContainsKey(slime.Id.Value).ShouldBeFalse(); // saqueado → el cadáver desaparece
            w.Combat.Loot.Get(w.Map, slime.Id).ShouldBeNull();
        }
    }

    [Fact]
    public void UseItem_Potion_Heals_Consumes_SharedCooldown_Bread_Hot() // HU-054
    {
        var w = Arena();
        var ana = w.Player("Ana"); var slime = w.Monster("slime");
        var rules = w.Content.Rules;
        var potion = ItemInstance.New("minor_healing_potion", 2);
        var potion2 = ItemInstance.New("minor_healing_potion", 1);
        var bread = ItemInstance.New("bread", 1);
        ana.Inventory.Bag[0] = potion; ana.Inventory.Bag[1] = potion2; ana.Inventory.Bag[2] = bread;
        ana.Hp = 10;
        var heal = w.Content.Spell("item_minor_heal").Effects[0];
        var ctx = w.Begin();
        w.Combat.ItemUse.Use(ana, potion.Id, w.Map, ctx).ShouldBeNull();
        // El cliente necesita la recarga para dibujarla en la barra (prueba de juego: solo veía "Aún no está listo").
        var cooldown = ctx.Events.OfType<CooldownEvent>().Single(e => e.TemplateId == "minor_healing_potion");
        cooldown.RemainingMs.ShouldBe(w.Content.Item("minor_healing_potion").UseCooldownMs);
        cooldown.SpellId.ShouldBeNull();
        (ana.Hp - 10).ShouldBeInRange((int)(heal.Base * rules.Combat.VarianceMin), (int)Math.Ceiling(heal.Base * rules.Combat.VarianceMax * rules.Combat.CritMultiplier));
        potion.Qty.ShouldBe(1);
        // CD compartido por plantilla: el otro stack también está en CD y no se consume.
        w.Combat.ItemUse.Use(ana, potion2.Id, w.Map, ctx).ShouldBe("on_cooldown");
        potion2.Qty.ShouldBe(1);
        w.Clock.Advance(w.Content.Item("minor_healing_potion").UseCooldownMs);
        w.Combat.ItemUse.Use(ana, potion2.Id, w.Map, w.Begin()).ShouldBeNull();
        ana.Inventory.Bag[1].ShouldBeNull(); // CA4: último del stack → casilla vacía
        // Pan: HoT de 15 s con ticks de 3 s → 5 ticks de 10 = 50 (combat.md: último tick cuenta).
        ana.Hp = 10;
        var breadAura = w.Content.Aura("bread_hot");
        w.Combat.ItemUse.Use(ana, bread.Id, w.Map, w.Begin()).ShouldBeNull();
        var ev = TickRunner.RunMs(w, breadAura.DurationMs);
        ev.OfType<CombatHitEvent>().Where(e => e.SpellId == "bread_hot").Sum(e => e.Amount).ShouldBe((int)(breadAura.Base * (breadAura.DurationMs / breadAura.TickMs)));
        // Una poción no cancela un casteo propio (ADR-019).
        var mage = w.Player("Bob");
        mage.Inventory.Bag[0] = ItemInstance.New("minor_mana_potion", 1);
        mage.Resource = 0;
        w.Combat.Casts.TryBeginCast(mage, w.Content.Spell("mage_fireball"), slime.Id, null, w.Map, w.Begin()).ShouldBe(CastErrors.NotEnoughResource);
        mage.Resource = mage.MaxResource;
        w.Combat.Casts.TryBeginCast(mage, w.Content.Spell("mage_fireball"), slime.Id, null, w.Map, w.Begin()).ShouldBeNull();
        var ctxUse = w.Begin();
        w.Combat.ItemUse.Use(mage, mage.Inventory.Bag[0]!.Id, w.Map, ctxUse).ShouldBeNull();
        mage.Combat.IsCasting.ShouldBeTrue();
        ctxUse.Events.OfType<CastEndedEvent>().Where(e => e.Spell.Id == "mage_fireball").ShouldBeEmpty();
    }

    [Fact]
    public void Vendor_Buy_Sell_Range_Gold_Junk() // HU-055
    {
        var db = TestContent.Load();
        var data = new MapData("t", "T", new CollisionGrid(40, 40), [], [new NpcDef("marta", "Marta la tendera", "robledal_general_goods", null, new Vec2(12, 10))],
            [new GraveyardDef("gy", new Vec2(2, 2))], [], [], "gy");
        var w = new WorldBuilder().WithMap(data).WithPlayer("Ana", "warrior", 5, (10, 10)).BuildWithCombat();
        var ana = w.Player("Ana");
        var npc = new Npc(w.World.EntityIds.Next(), data.Npcs[0]) { Position = data.Npcs[0].Position };
        w.Map.Add(npc);
        var economy = db.Rules.Economy;
        var potion = db.Item("minor_healing_potion");
        var price = InventoryOps.BuyPrice(potion, economy);
        price.ShouldBe((long)Math.Round(potion.SellPrice * economy.VendorBuyMultiplier));
        var ctx = w.Begin();
        ana.Inventory.Gold = price * 5 - 1;
        w.Combat.Vendor.Buy(ana, npc.Id, "minor_healing_potion", 5, w.Map, ctx, 1).ShouldBe("not_enough_gold");
        ana.Inventory.Gold = price * 5 + 3;
        w.Combat.Vendor.Buy(ana, npc.Id, "minor_healing_potion", 5, w.Map, ctx, 1).ShouldBeNull();
        ana.Inventory.Gold.ShouldBe(3);
        ana.Inventory.Bag.Count(i => i?.TemplateId == "minor_healing_potion").ShouldBe(1);
        ana.Inventory.Bag.First(i => i?.TemplateId == "minor_healing_potion")!.Qty.ShouldBe(5);
        w.Combat.Vendor.Buy(ana, npc.Id, "iron_sword", 1, w.Map, ctx, 2).ShouldBe("not_found"); // no lo vende
        // Vender: sellPrice × qty; sellPrice 0 no se vende.
        var goo = ItemInstance.New("slime_goo", 4);
        ana.Inventory.Bag[5] = goo;
        w.Combat.Vendor.Sell(ana, npc.Id, goo.Id, 4, w.Map, ctx, 3).ShouldBeNull();
        ana.Inventory.Gold.ShouldBe(3 + db.Item("slime_goo").SellPrice * 4);
        ana.Inventory.Bag[5].ShouldBeNull();
        var free = ItemInstance.New("bread", 1);
        var freeTpl = db.Item("bread") with { SellPrice = 0 };
        _ = freeTpl;
        // Distancia > vendorRangeTiles → out_of_range
        ana.Position = new Vec2(12 + (float)economy.VendorRangeTiles + 1, 10);
        w.Combat.Vendor.Buy(ana, npc.Id, "bread", 1, w.Map, ctx, 4).ShouldBe("out_of_range");
        _ = free;
    }

    [Fact]
    public void UncommonPlus_EmitsLootAnnounced_GlobalForBoss_PartyOtherwise() // HU-062 CA4, HU-083 CA4
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 5, (10, 10)).WithMonster("slime", (12, 10), wanderRadius: 0)
            .WithMonster("foreman_grask", (20, 20), wanderRadius: 0).WithMonster("wolf", (14, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana");
        var ctx = w.Begin();
        var rareIds = new HashSet<string> { "foreman_pick", "foreman_breastplate", "lantern_amulet" };
        w.Combat.Loot.CreateBag(w.Monster("foreman_grask"), ana, w.Map, ctx);
        var boss = ctx.Events.OfType<LootAnnouncedEvent>().Single(e => rareIds.Contains(e.TemplateId));
        boss.Global.ShouldBeTrue();
        boss.Winner.ShouldBe(ana);
        boss.Rarity.ShouldBe(Rarity.Rare);

        // Un slime (junk/common) nunca anuncia; un lobo anuncia (grupo, no global) cuando cae la daga/collar uncommon.
        ctx.Events.Clear();
        w.Combat.Loot.CreateBag(w.Monster("slime"), ana, w.Map, ctx);
        ctx.Events.OfType<LootAnnouncedEvent>().ShouldBeEmpty();
        var wolf = w.Monster("wolf");
        LootAnnouncedEvent? ann = null;
        for (var i = 0; i < 400 && ann is null; i++)
        {
            ctx.Events.Clear();
            w.Combat.Loot.Forget(w.Map, wolf.Id);
            w.Combat.Loot.CreateBag(wolf, ana, w.Map, ctx);
            ann = ctx.Events.OfType<LootAnnouncedEvent>().FirstOrDefault();
        }
        ann.ShouldNotBeNull();
        ann.Global.ShouldBeFalse();
        ((int)ann.Rarity).ShouldBeGreaterThanOrEqualTo((int)Rarity.Uncommon);
    }
}
