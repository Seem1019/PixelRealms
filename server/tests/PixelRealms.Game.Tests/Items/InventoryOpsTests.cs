using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Items;

/// <summary>HU-051/HU-052/HU-056/HU-058: operaciones de bolsa y equipo, invariantes de la skill inventory-items (propiedad), equipo inicial.</summary>
public sealed class InventoryOpsTests
{
    private static readonly ContentDb Db = TestContent.Load();

    private static Player NewPlayer(string classId = "warrior", int level = 5)
        => new(new EntityId(1), "Ana", classId) { CharacterId = Guid.NewGuid(), Level = level };

    private static SlotRef Bag(int i) => new("bag", i);
    private static SlotRef Equip(int i) => new("equip", i);

    [Fact]
    public void Move_Empty_Merge_Swap_Split() // HU-051 CA2, HU-056 CA1
    {
        var p = NewPlayer();
        p.Inventory.Bag[0] = ItemInstance.New("bread", 5);
        p.Inventory.Bag[1] = ItemInstance.New("bread", 18);
        p.Inventory.Bag[2] = ItemInstance.New("worn_sword");
        // Fusionar: 5 + 18 → 20 en destino, 3 quedan en origen.
        InventoryOps.Move(p, Bag(0), Bag(1), null, Db).Ok.ShouldBeTrue();
        p.Inventory.Bag[1]!.Qty.ShouldBe(20);
        p.Inventory.Bag[0]!.Qty.ShouldBe(3);
        p.PendingAudit.ShouldContain(a => a.Action == "merge" && a.Quantity == 2); // HU-057 CA3: la fusión parcial también se audita
        // Intercambiar con distinto.
        InventoryOps.Move(p, Bag(0), Bag(2), null, Db).Ok.ShouldBeTrue();
        p.Inventory.Bag[2]!.TemplateId.ShouldBe("bread");
        p.Inventory.Bag[0]!.TemplateId.ShouldBe("worn_sword");
        // Dividir a casilla vacía: id nuevo.
        var originalId = p.Inventory.Bag[1]!.Id;
        InventoryOps.Move(p, Bag(1), Bag(5), 7, Db).Ok.ShouldBeTrue();
        p.Inventory.Bag[1]!.Qty.ShouldBe(13);
        p.Inventory.Bag[5]!.Qty.ShouldBe(7);
        p.Inventory.Bag[5]!.Id.ShouldNotBe(originalId);
        p.PendingAudit.ShouldContain(a => a.Action == "split" && a.ItemId == p.Inventory.Bag[5]!.Id && a.Quantity == 7); // HU-057 CA3
        // Mover a vacía.
        InventoryOps.Move(p, Bag(0), Bag(9), null, Db).Ok.ShouldBeTrue();
        p.Inventory.Bag[0].ShouldBeNull();
        p.Inventory.Bag[9]!.TemplateId.ShouldBe("worn_sword");
    }

    [Fact]
    public void Move_InvalidIndexes_OrQty_Rejected() // HU-051 CA3, HU-056 CA3
    {
        var p = NewPlayer();
        p.Inventory.Bag[0] = ItemInstance.New("bread", 5);
        InventoryOps.Move(p, Bag(0), Bag(24), null, Db).ErrorCode.ShouldBe("invalid_payload");
        InventoryOps.Move(p, Bag(-1), Bag(1), null, Db).ErrorCode.ShouldBe("invalid_payload");
        InventoryOps.Move(p, Bag(3), Bag(1), null, Db).ErrorCode.ShouldBe("not_found");
        InventoryOps.Move(p, Bag(0), Bag(1), 0, Db).ErrorCode.ShouldBe("invalid_payload");
        InventoryOps.Move(p, Bag(0), Bag(1), 6, Db).ErrorCode.ShouldBe("invalid_payload");
        InventoryOps.Move(p, Bag(0), Bag(0), null, Db).ErrorCode.ShouldBe("invalid_payload");
        p.Inventory.Bag[0]!.Qty.ShouldBe(5); // nada cambió
    }

    [Fact]
    public void Equip_Swaps_RecalcDirty_LevelTooLow_AnyClass() // HU-052 CA1, CA2
    {
        var p = NewPlayer("mage", 5);
        p.Equipment.Slots[7] = ItemInstance.New("apprentice_staff");
        p.Inventory.Bag[0] = ItemInstance.New("iron_sword");       // nivel 4, afinidad baja para Mago: se equipa igual (ADR-009)
        p.Inventory.Bag[1] = ItemInstance.New("foreman_breastplate"); // nivel 6
        p.Combat.Stats = new DerivedStats(default, 1, 1, 1, 1, 0, 0, 0, 0, 1, 0, 0, 1);
        InventoryOps.Equip(p, 0, Db).Ok.ShouldBeTrue();
        p.Equipment.MainHand!.TemplateId.ShouldBe("iron_sword");
        p.Inventory.Bag[0]!.TemplateId.ShouldBe("apprentice_staff"); // intercambio
        p.Combat.Stats.ShouldBeNull(); // MarkStatsDirty
        InventoryOps.Equip(p, 1, Db).ErrorCode.ShouldBe("level_too_low");
        p.Inventory.Bag[1]!.TemplateId.ShouldBe("foreman_breastplate");
        // Arrastrar a un slot que no es el suyo → invalid_payload
        InventoryOps.Move(p, Bag(0), Equip(2), null, Db).ErrorCode.ShouldBe("invalid_payload");
        InventoryOps.Move(p, Bag(0), Equip(7), null, Db).Ok.ShouldBeTrue(); // vuelve el bastón
    }

    [Fact]
    public void Unequip_FirstFree_BagFull() // HU-052 CA3
    {
        var p = NewPlayer();
        p.Equipment.Slots[7] = ItemInstance.New("worn_sword");
        for (var i = 0; i < Inventory.BagSize; i++) p.Inventory.Bag[i] = ItemInstance.New("slime_goo", 1);
        InventoryOps.Unequip(p, 7, null, Db).ErrorCode.ShouldBe("bag_full");
        p.Equipment.MainHand.ShouldNotBeNull();
        p.Inventory.Bag[3] = null;
        InventoryOps.Unequip(p, 7, null, Db).Ok.ShouldBeTrue();
        p.Inventory.Bag[3]!.TemplateId.ShouldBe("worn_sword");
        p.Equipment.MainHand.ShouldBeNull();
    }

    [Fact]
    public void Mage_IronSword_ExactStats_ViaCombatServices() // HU-052 CA2b, CA5
    {
        var w = new WorldBuilder().WithMap().WithPlayer("Ana", "mage", 5, (5, 5), ["apprentice_staff", "novice_robe"]).Build();
        var ana = w.Player("Ana");
        var before = w.Combat.Services.StatsOf(ana);
        ana.Inventory.Bag[0] = ItemInstance.New("iron_sword");
        InventoryOps.Equip(ana, 0, w.Content).Ok.ShouldBeTrue();
        var after = w.Combat.Services.Recalculate(ana);
        var staffStats = w.Content.Item("apprentice_staff").Stats!;
        // +2 str · 0.7 = +1.4 str → attackPower +0.84; +1 sta · 0.7 → maxHp +7; el bastón quitado tenía +1 int (afinidad alta).
        (after.Primary.Str - before.Primary.Str).ShouldBe(1.4, 1e-9);
        (after.Primary.Sta - before.Primary.Sta).ShouldBe(0.7, 1e-9);
        (after.Primary.Int - before.Primary.Int).ShouldBe(-staffStats.Int, 1e-9);
        (after.MaxHp - before.MaxHp).ShouldBe(7);
        after.WeaponAffinity.ShouldBe(0.7);
        // CA5: la vida actual no supera la nueva máxima al quitar aguante.
        ana.Hp = ana.MaxHp;
        InventoryOps.Unequip(ana, 7, null, w.Content).Ok.ShouldBeTrue();
        w.Combat.Services.Recalculate(ana);
        ana.Hp.ShouldBeLessThanOrEqualTo(ana.MaxHp);
        ana.Hp.ShouldBe(ana.MaxHp);
    }

    [Fact]
    public void AddItem_FillsStacksThenSlots_AllOrNothing()
    {
        var p = NewPlayer();
        var bread = Db.Item("bread"); // maxStack 20
        p.Inventory.Bag[0] = ItemInstance.New("bread", 15);
        for (var i = 1; i < Inventory.BagSize - 1; i++) p.Inventory.Bag[i] = ItemInstance.New("slime_goo", 1);
        // Caben 5 en el stack + 20 en el único hueco = 25.
        InventoryOps.AddItem(p, bread, 26, "loot").ErrorCode.ShouldBe("bag_full");
        p.Inventory.Bag[0]!.Qty.ShouldBe(15); // sin cambios
        InventoryOps.AddItem(p, bread, 25, "loot").Ok.ShouldBeTrue();
        p.Inventory.Bag[0]!.Qty.ShouldBe(20);
        p.Inventory.Bag[23]!.Qty.ShouldBe(20);
        p.PendingAudit.Count(a => a.Action == "loot").ShouldBe(2);
    }

    [Fact]
    public void Remove_Destroy_Audits() // HU-056 CA2, HU-057 CA3
    {
        var p = NewPlayer();
        var item = ItemInstance.New("bread", 5);
        p.Inventory.Bag[0] = item;
        InventoryOps.Remove(p, item.Id, 6, "destroy", Db).ErrorCode.ShouldBe("invalid_payload");
        InventoryOps.Remove(p, item.Id, 2, "destroy", Db).Ok.ShouldBeTrue();
        item.Qty.ShouldBe(3);
        InventoryOps.Remove(p, item.Id, 3, "destroy", Db).Ok.ShouldBeTrue();
        p.Inventory.Bag[0].ShouldBeNull(); // HU-054 CA4: el último deja la casilla vacía
        p.PendingAudit.Count(a => a.Action == "destroy").ShouldBe(2);
        InventoryOps.Remove(p, Guid.NewGuid(), 1, "destroy", Db).ErrorCode.ShouldBe("not_found");
    }

    [Fact]
    public void PropertyTest_1000RandomOps_KeepInvariants() // HU-051 CA4
    {
        var rng = new SeededRng(42);
        var p = NewPlayer("rogue", 6);
        string[] templates = ["bread", "slime_goo", "minor_healing_potion", "worn_dagger", "leather_vest", "wolf_fang_dagger", "boar_hide_boots", "copper_ore"];
        foreach (var t in templates) InventoryOps.AddItem(p, Db.Item(t), Db.Item(t).IsStackable ? rng.Next(1, 30) : 1, "admin_give");
        var expected = Totals(p);
        for (var i = 0; i < 1000; i++)
        {
            var op = rng.Next(0, 4);
            switch (op)
            {
                case 0: InventoryOps.Move(p, Bag(rng.Next(0, 24)), Bag(rng.Next(0, 24)), rng.Next(0, 3) == 0 ? rng.Next(1, 10) : null, Db); break;
                case 1: InventoryOps.Equip(p, rng.Next(0, 24), Db); break;
                case 2: InventoryOps.Unequip(p, rng.Next(0, 9), rng.Next(0, 2) == 0 ? rng.Next(0, 24) : null, Db); break;
                default: InventoryOps.Move(p, Bag(rng.Next(0, 24)), Equip(rng.Next(0, 9)), null, Db); break;
            }
            CheckInvariants(p);
            var now = Totals(p);
            now.Count.ShouldBe(expected.Count, $"conservación tras la op {i}");
            foreach (var (k, v) in expected) now[k].ShouldBe(v, $"conservación de {k} tras la op {i}");
        }
    }

    private static Dictionary<string, int> Totals(Player p)
    {
        var d = new Dictionary<string, int>();
        foreach (var it in p.Inventory.Bag.Concat(p.Equipment.Slots)) if (it is not null) d[it.TemplateId] = d.GetValueOrDefault(it.TemplateId) + it.Qty;
        return d;
    }

    private static void CheckInvariants(Player p)
    {
        var all = p.Inventory.Bag.Concat(p.Equipment.Slots).Where(i => i is not null).Select(i => i!).ToList();
        all.Select(i => i.Id).Distinct().Count().ShouldBe(all.Count, "ids únicos");
        foreach (var it in all)
        {
            var tpl = Db.Item(it.TemplateId);
            it.Qty.ShouldBeInRange(1, Math.Max(1, tpl.MaxStack));
            if (!tpl.IsStackable) it.Qty.ShouldBe(1);
        }
        for (var s = 0; s < Equipment.SlotCount; s++)
        {
            if (p.Equipment.Slots[s] is not { } eq) continue;
            var tpl = Db.Item(eq.TemplateId);
            ((int)tpl.Slot!).ShouldBe(s);
            tpl.LevelReq.ShouldBeLessThanOrEqualTo(p.Level);
        }
        p.Inventory.Gold.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData("warrior")] [InlineData("rogue")] [InlineData("mage")] [InlineData("priest")]
    public void StartingGear_Exists_Fits_HighAffinity(string classId) // HU-058 CA2
    {
        var cls = Db.Class(classId);
        var bagSlots = 0;
        foreach (var si in cls.StartingItems)
        {
            var tpl = Db.Item(si.ItemId);
            if (si.Equip)
            {
                tpl.Slot.ShouldNotBeNull();
                Db.Rules.Affinity.Of(classId, tpl.AffinityType).ShouldBe(Affinity.Alta, $"{classId}: {si.ItemId}");
            }
            else bagSlots += (int)Math.Ceiling(si.Qty / (double)Math.Max(1, tpl.MaxStack));
        }
        bagSlots.ShouldBeLessThanOrEqualTo(Inventory.BagSize);
    }

    [Fact]
    public void StartingGear_WarriorAndPriest_PerClassesJson() // HU-058 CA1
    {
        var w = Db.Class("warrior");
        w.StartingItems.Where(s => s.Equip).Select(s => s.ItemId).ToArray().ShouldBe(new[] { "worn_sword", "recruit_mail_shirt", "wooden_shield" });
        w.StartingItems.Single(s => s.ItemId == "bread").Qty.ShouldBe(5);
        var p = Db.Class("priest");
        p.StartingItems.Count(s => s.Equip && s.ItemId == "initiate_mace").ShouldBe(1);
        p.StartingItems.Count(s => !s.Equip && s.ItemId == "novice_wand").ShouldBe(1);
    }
}
