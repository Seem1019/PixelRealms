// NO COMPILADO EN LA SESIÓN DE LA FASE 1 (NuGet bloqueado). Excluido con OfflineBuild=true.
using PixelRealms.Persistence.Ef;
using PixelRealms.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace PixelRealms.Persistence.Tests.Ef;

public sealed class CharacterRepositoryTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SaveAndLoad_RoundTrip() // HU-026 CA5, HU-057 CA1
    {
        var accounts = new EfAccountRepository(pg.Factory);
        var chars = new EfCharacterRepository(pg.Factory);
        var acc = (await accounts.CreateAsync("ana", "hash")).ShouldNotBeNull();
        var sword = new SavedItem(Guid.CreateVersion7(), "worn_sword", 1, 1, 7);
        var created = (await chars.CreateAsync(new NewCharacter(acc.Id, "Ana", "warrior", "meadow", 10, 12, 60, 0, [sword], [new SavedHotbarSlot(0, 0, "warrior_heroic_strike")]))).ShouldNotBeNull();
        var bread = new SavedItem(Guid.CreateVersion7(), "bread", 5, 0, 0);
        await chars.SaveAsync(created with { Level = 3, Xp = 50, Gold = 120, X = 20, Y = 21, Hp = 44, Items = [sword, bread], Audit = [new AuditEntry(bread.Id, "loot", "bread", 5)] });
        var loaded = (await chars.LoadAsync(created.Id)).ShouldNotBeNull();
        loaded.Level.ShouldBe(3);
        loaded.Gold.ShouldBe(120);
        loaded.Items.Count.ShouldBe(2);
        loaded.Items.ShouldContain(i => i.TemplateId == "bread" && i.Quantity == 5 && i.Container == 0);
        loaded.Hotbar.ShouldHaveSingleItem().Ref.ShouldBe("warrior_heroic_strike");
    }

    [Fact]
    public async Task DuplicateNames_AreRejected_CaseInsensitive() // HU-010 CA2, HU-012 CA3
    {
        var accounts = new EfAccountRepository(pg.Factory);
        (await accounts.CreateAsync("Bob", "h")).ShouldNotBeNull();
        (await accounts.CreateAsync("bob", "h")).ShouldBeNull();
    }
}
