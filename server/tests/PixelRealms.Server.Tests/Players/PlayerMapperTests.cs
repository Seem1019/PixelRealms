using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Players;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Players;

public sealed class PlayerMapperTests
{
    private static PlayerMapper Mapper() => new(new ReloadableContent(TestContent.Load(), TestContent.ContentDir));

    private static CharacterSaveDto Dto() => new(Guid.NewGuid(), Guid.NewGuid(), "Ana", "warrior", 3, 0, 50, "meadow", 23, 60, 100, 0, [], [], []);

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Cooldowns_SurviveTheSave_AndKeepRunningWhileOffline() // HU-015 pendiente
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var mapper = Mapper();
        mapper.Time = time;
        mapper.NowMs = () => 10_000;
        var p = mapper.ToPlayer(Dto(), new EntityId(1));
        const string spellId = "warrior_shield_block"; // recarga de 12 s (HU-106)
        p.Combat.CooldownEndsAtMs[spellId] = 25_000; // faltan 15 s
        p.Combat.CooldownEndsAtMs["warrior_strike"] = 9_000; // ya terminó: no se guarda
        p.ItemCooldownEndsAtMs["potion_minor"] = 15_000; // faltan 5 s

        var saved = mapper.ToSave(p);
        saved.Cooldowns!.Count.ShouldBe(2);

        // Vuelve 10 s después (reloj real) a un servidor recién arrancado (reloj de juego en 500 ms).
        time.Now = time.Now.AddSeconds(10);
        mapper.NowMs = () => 500;
        var back = mapper.ToPlayer(saved, new EntityId(2));

        back.Combat.CooldownEndsAtMs[spellId].ShouldBe(5_500); // 5 s restantes
        back.ItemCooldownEndsAtMs.ShouldNotContainKey("potion_minor"); // terminó mientras estaba fuera
        mapper.ToCooldowns(back).Single().ShouldBe(new PixelRealms.Protocol.Messages.Cooldown(spellId, 5_000, null));
    }

    [Fact]
    public void ToCooldowns_IncludesPendingItemCooldowns_SoTheHotbarCanDrawThem()
    {
        var mapper = Mapper();
        mapper.NowMs = () => 10_000;
        var p = mapper.ToPlayer(Dto(), new EntityId(1));
        p.ItemCooldownEndsAtMs["minor_healing_potion"] = 52_000; // faltan 42 s
        mapper.ToCooldowns(p).ShouldContain(new PixelRealms.Protocol.Messages.Cooldown(null, 42_000, null, "minor_healing_potion"));
    }

    [Fact]
    public void LoadedCooldowns_AreCappedToTheContent_AndUnknownOnesAreDropped() // revisión de autoridad
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var mapper = Mapper();
        mapper.Time = time;
        mapper.NowMs = () => 1_000;
        var content = TestContent.Load();
        var spell = content.KnownSpells("warrior", 15).First(s => s.CooldownMs > 0);
        var far = time.Now.UtcDateTime.AddDays(60); // reloj del host adelantado al guardar
        var dto = Dto() with { Cooldowns = [new SavedCooldown(0, spell.Id, far), new SavedCooldown(0, "spell_that_was_removed", far), new SavedCooldown(7, spell.Id, far)] };

        var p = mapper.ToPlayer(dto, new EntityId(1));

        // Tope: la recarga más larga entre el hechizo y sus mejoras (revisión de autoridad de HU-104; desde HU-107 alguna la alarga).
        p.Combat.CooldownEndsAtMs.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<string, long>(spell.Id, 1_000 + content.LongestCooldownMs(spell)));
        p.ItemCooldownEndsAtMs.ShouldBeEmpty(); // un kind desconocido no se trata como item
    }

    [Fact]
    public void ToStatsUpdate_DoesNotTouchTheLivePlayer()
    {
        var mapper = Mapper();
        var p = mapper.ToPlayer(Dto(), new EntityId(1));
        // Máximos vivos subidos por un aura (CombatServices.Recalculate cuenta auras; el mapper no las conoce).
        p.MaxHp += 40;
        p.Hp = p.MaxHp;
        var (maxHp, hp, maxRes, res) = (p.MaxHp, p.Hp, p.MaxResource, p.Resource);

        var update = mapper.ToStatsUpdate(p); // se envía tras cada Welcome, también al reconectar

        (p.MaxHp, p.Hp, p.MaxResource, p.Resource).ShouldBe((maxHp, hp, maxRes, res));
        update.Derived.MaxHp.ShouldBe(maxHp); // el mismo máximo que ve el cliente en los Snapshot
        update.Gold.ShouldBe(50);
    }

    [Fact]
    public void UnknownTemplate_IsKeptAside_AndSavedAgain_NotDeleted() // HU-057 CA4
    {
        var mapper = Mapper();
        var lost = new SavedItem(Guid.NewGuid(), "espada_retirada", 1, 0, 3);
        var p = mapper.ToPlayer(Dto() with { Items = [lost] }, new EntityId(1));
        p.Inventory.Bag[3].ShouldBeNull(); // no se muestra ni se usa
        p.Unplaced.Single().Id.ShouldBe(lost.Id);
        var saved = mapper.ToSave(p);
        saved.Items.ShouldContain(i => i.Id == lost.Id && i.Container == PlayerMapper.UnplacedContainer);
    }

    [Fact]
    public void TwoItemsInTheSameSlot_TheSecondMovesToAFreeBagSlot() // HU-057: antes el segundo pisaba al primero y se perdía
    {
        var mapper = Mapper();
        var a = new SavedItem(Guid.NewGuid(), "bread", 2, 0, 0);
        var b = new SavedItem(Guid.NewGuid(), "minor_healing_potion", 1, 0, 0);
        var p = mapper.ToPlayer(Dto() with { Items = [a, b] }, new EntityId(1));
        p.Inventory.Bag[0]!.Id.ShouldBe(a.Id);
        p.Inventory.Bag.Count(i => i?.Id == b.Id).ShouldBe(1);
        p.Unplaced.ShouldBeEmpty();
        mapper.ToSave(p).Items.Select(i => (i.Container, i.Slot)).Distinct().Count().ShouldBe(2); // sin casillas repetidas
    }

    [Fact]
    public void Rage_StartsAtZeroOnEveryEntry_ManaIsKept() // HU-039 CA2
    {
        var mapper = Mapper();
        mapper.ToPlayer(Dto() with { Resource = 80 }, new EntityId(1)).Resource.ShouldBe(0); // Guerrero: ira
        var mage = mapper.ToPlayer(Dto() with { ClassId = "mage", Resource = 30 }, new EntityId(2));
        mage.Resource.ShouldBe(30);
    }
}
