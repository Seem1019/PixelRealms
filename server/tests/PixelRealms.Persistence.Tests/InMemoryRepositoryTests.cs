using PixelRealms.Persistence.InMemory;
using PixelRealms.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace PixelRealms.Persistence.Tests;

/// <summary>Mismo contrato que los repositorios EF, sobre el proveedor en memoria (compila sin NuGet).</summary>
public sealed class InMemoryRepositoryTests
{
    [Fact]
    public async Task Accounts_UniqueCaseInsensitive_AndLastLogin()
    {
        var store = new InMemoryStore();
        var repo = new InMemoryAccountRepository(store);
        var a = (await repo.CreateAsync("Ana", "hash", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await repo.CreateAsync("ana", "hash", TestContext.Current.CancellationToken)).ShouldBeNull();
        (await repo.FindByUsernameAsync("ANA", TestContext.Current.CancellationToken)).ShouldNotBeNull().Id.ShouldBe(a.Id);
        var at = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        await repo.TouchLastLoginAsync(a.Id, at, TestContext.Current.CancellationToken);
        (await repo.GetAsync(a.Id, TestContext.Current.CancellationToken))!.LastLoginAt.ShouldBe(at);
    }

    [Fact]
    public async Task Characters_CreateSaveLoadDelete()
    {
        var store = new InMemoryStore();
        var repo = new InMemoryCharacterRepository(store);
        var acc = Guid.CreateVersion7();
        var sword = new SavedItem(Guid.CreateVersion7(), "worn_sword", 1, 1, 7);
        var c = (await repo.CreateAsync(new NewCharacter(acc, "Ana", "warrior", "meadow", 10, 12, 60, 0, [sword], []), TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await repo.CreateAsync(new NewCharacter(acc, "ANA", "mage", "meadow", 0, 0, 40, 60, [], []), TestContext.Current.CancellationToken)).ShouldBeNull();
        (await repo.CountByAccountAsync(acc, TestContext.Current.CancellationToken)).ShouldBe(1);
        await repo.SaveAsync(c with { Level = 2, Gold = 7, Items = [sword, new SavedItem(Guid.CreateVersion7(), "bread", 3, 0, 0)], Audit = [new AuditEntry(sword.Id, "loot", "worn_sword", 1)] }, TestContext.Current.CancellationToken);
        var loaded = (await repo.LoadAsync(c.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        loaded.Level.ShouldBe(2);
        loaded.Items.Count.ShouldBe(2);
        store.Audit.Count.ShouldBe(1);
        (await repo.SoftDeleteAsync(Guid.CreateVersion7(), c.Id, TestContext.Current.CancellationToken)).ShouldBeFalse(); // otra cuenta → 404 en la API (HU-013 CA3)
        (await repo.SoftDeleteAsync(acc, c.Id, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await repo.LoadAsync(c.Id, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await repo.NameExistsAsync("Ana", TestContext.Current.CancellationToken)).ShouldBeFalse(); // el nombre queda libre (HU-013 CA4)
        (await repo.ListByAccountAsync(acc, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }
}
