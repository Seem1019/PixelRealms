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
        var a = (await repo.CreateAsync("Ana", "hash")).ShouldNotBeNull();
        (await repo.CreateAsync("ana", "hash")).ShouldBeNull();
        (await repo.FindByUsernameAsync("ANA")).ShouldNotBeNull().Id.ShouldBe(a.Id);
        var at = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        await repo.TouchLastLoginAsync(a.Id, at);
        (await repo.GetAsync(a.Id))!.LastLoginAt.ShouldBe(at);
    }

    [Fact]
    public async Task Characters_CreateSaveLoadDelete()
    {
        var store = new InMemoryStore();
        var repo = new InMemoryCharacterRepository(store);
        var acc = Guid.CreateVersion7();
        var sword = new SavedItem(Guid.CreateVersion7(), "worn_sword", 1, 1, 7);
        var c = (await repo.CreateAsync(new NewCharacter(acc, "Ana", "warrior", "meadow", 10, 12, 60, 0, [sword], []))).ShouldNotBeNull();
        (await repo.CreateAsync(new NewCharacter(acc, "ANA", "mage", "meadow", 0, 0, 40, 60, [], []))).ShouldBeNull();
        (await repo.CountByAccountAsync(acc)).ShouldBe(1);
        await repo.SaveAsync(c with { Level = 2, Gold = 7, Items = [sword, new SavedItem(Guid.CreateVersion7(), "bread", 3, 0, 0)], Audit = [new AuditEntry(sword.Id, "loot", "worn_sword", 1)] });
        var loaded = (await repo.LoadAsync(c.Id)).ShouldNotBeNull();
        loaded.Level.ShouldBe(2);
        loaded.Items.Count.ShouldBe(2);
        store.Audit.Count.ShouldBe(1);
        (await repo.SoftDeleteAsync(Guid.CreateVersion7(), c.Id)).ShouldBeFalse(); // otra cuenta → 404 en la API (HU-013 CA3)
        (await repo.SoftDeleteAsync(acc, c.Id)).ShouldBeTrue();
        (await repo.LoadAsync(c.Id)).ShouldBeNull();
        (await repo.NameExistsAsync("Ana")).ShouldBeFalse(); // el nombre queda libre (HU-013 CA4)
        (await repo.ListByAccountAsync(acc)).ShouldBeEmpty();
    }
}
