// NO COMPILADO EN LA SESIÓN DE LA FASE 1 (NuGet bloqueado). Excluido con OfflineBuild=true.
using Microsoft.EntityFrameworkCore;
using PixelRealms.Persistence.Ef;
using PixelRealms.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace PixelRealms.Persistence.Tests.Ef;

public sealed class CharacterRepositoryTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private const int Max = 4;

    [Fact]
    public async Task SaveAndLoad_RoundTrip() // HU-026 CA5, HU-057 CA1
    {
        var accounts = new EfAccountRepository(pg.Factory);
        var chars = new EfCharacterRepository(pg.Factory);
        var acc = (await accounts.CreateAsync("ana", "hash", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var sword = new SavedItem(Guid.CreateVersion7(), "worn_sword", 1, 1, 7);
        var created = (await chars.CreateAsync(new NewCharacter(acc.Id, "Ana", "warrior", "meadow", 10, 12, 60, 0, [sword], [new SavedHotbarSlot(0, 0, "warrior_heroic_strike")]), Max, TestContext.Current.CancellationToken)).Character.ShouldNotBeNull();
        var bread = new SavedItem(Guid.CreateVersion7(), "bread", 5, 0, 0);
        await chars.SaveAsync(created with { Level = 3, Xp = 50, Gold = 120, X = 20, Y = 21, Hp = 44, Items = [sword, bread], Audit = [new AuditEntry(bread.Id, "loot", "bread", 5)] }, TestContext.Current.CancellationToken);
        var loaded = (await chars.LoadAsync(created.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        loaded.Level.ShouldBe(3);
        loaded.Gold.ShouldBe(120);
        loaded.Items.Count.ShouldBe(2);
        loaded.Items.ShouldContain(i => i.TemplateId == "bread" && i.Quantity == 5 && i.Container == 0);
        loaded.Hotbar.ShouldHaveSingleItem().Ref.ShouldBe("warrior_heroic_strike");
    }

    [Fact]
    public async Task Cooldowns_RoundTrip_AsUtc_AndEachSaveReplacesThePrevious() // HU-015 pendiente
    {
        var ct = TestContext.Current.CancellationToken;
        var accounts = new EfAccountRepository(pg.Factory);
        var chars = new EfCharacterRepository(pg.Factory);
        var acc = (await accounts.CreateAsync("cdu", "hash", ct)).ShouldNotBeNull();
        var created = (await chars.CreateAsync(new NewCharacter(acc.Id, "Cdu", "warrior", "meadow", 10, 12, 60, 0, [], []), Max, ct)).Character.ShouldNotBeNull();
        var ends = new DateTime(2026, 10, 2, 12, 0, 30, DateTimeKind.Utc);
        await chars.SaveAsync(created with { Cooldowns = [new SavedCooldown(0, "warrior_charge", ends), new SavedCooldown(1, "potion_minor", ends)] }, ct);
        await chars.SaveAsync(created with { Cooldowns = [new SavedCooldown(0, "warrior_charge", ends)] }, ct);

        var loaded = (await chars.LoadAsync(created.Id, ct)).ShouldNotBeNull();
        var cd = loaded.Cooldowns.ShouldNotBeNull().ShouldHaveSingleItem(); // el consumible ya no estaba en el último guardado
        cd.Ref.ShouldBe("warrior_charge");
        cd.EndsAtUtc.ShouldBe(ends);
        cd.EndsAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task DuplicateNames_AreRejected_CaseInsensitive() // HU-010 CA2, HU-012 CA3
    {
        var accounts = new EfAccountRepository(pg.Factory);
        (await accounts.CreateAsync("Bob", "h", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await accounts.CreateAsync("bob", "h", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task CharacterNames_UniqueAmongLive_ReusableAfterSoftDelete() // HU-012 CA3, HU-013 CA4 (sufijo #deleted-<id>)
    {
        var ct = TestContext.Current.CancellationToken;
        var accounts = new EfAccountRepository(pg.Factory);
        var chars = new EfCharacterRepository(pg.Factory);
        var acc = (await accounts.CreateAsync("zed_owner", "h", ct)).ShouldNotBeNull();
        NewCharacter New(string name) => new(acc.Id, name, "warrior", "meadow", 10, 12, 60, 0, [], []);

        var first = (await chars.CreateAsync(New("Zed"), Max, ct)).Character.ShouldNotBeNull();
        (await chars.NameExistsAsync("zED", ct)).ShouldBeTrue();
        (await chars.CreateAsync(New("zed"), Max, ct)).Status.ShouldBe(CreateCharacterStatus.NameTaken); // el índice único rechaza aunque se salte NameExistsAsync

        (await chars.SoftDeleteAsync(acc.Id, first.Id, ct)).ShouldBeTrue();
        (await chars.NameExistsAsync("Zed", ct)).ShouldBeFalse();
        (await chars.CreateAsync(New("zed"), Max, ct)).Status.ShouldBe(CreateCharacterStatus.Created);
        (await chars.CreateAsync(New("ZED"), Max, ct)).Status.ShouldBe(CreateCharacterStatus.NameTaken);
    }

    [Fact]
    public async Task AccountCreate_NonUniquenessFailure_Propagates()
    {
        var accounts = new EfAccountRepository(pg.Factory);
        // varchar(20) excedido (22001): no es "usuario en uso", no debe disfrazarse de null.
        await Should.ThrowAsync<DbUpdateException>(() => accounts.CreateAsync(new string('x', 21), "h", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CharacterCreate_NonUniquenessFailure_Propagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var accounts = new EfAccountRepository(pg.Factory);
        var chars = new EfCharacterRepository(pg.Factory);
        var acc = (await accounts.CreateAsync("slot_clash", "h", ct)).ShouldNotBeNull();
        // Dos items en la misma casilla violan otro índice único (character_items), no el del nombre.
        SavedItem[] clash = [new(Guid.CreateVersion7(), "bread", 1, 0, 0), new(Guid.CreateVersion7(), "bread", 1, 0, 0)];
        await Should.ThrowAsync<DbUpdateException>(() => chars.CreateAsync(new NewCharacter(acc.Id, "Clash", "warrior", "meadow", 0, 0, 60, 0, clash, []), Max, ct));
        (await chars.NameExistsAsync("Clash", ct)).ShouldBeFalse(); // la transacción se revirtió entera
    }

    [Fact]
    public async Task ConcurrentCreates_NeverExceedLimitPerAccount() // HU-012 CA4, con peticiones en paralelo
    {
        var ct = TestContext.Current.CancellationToken;
        var accounts = new EfAccountRepository(pg.Factory);
        var chars = new EfCharacterRepository(pg.Factory);
        var acc = (await accounts.CreateAsync("racer", "h", ct)).ShouldNotBeNull();

        // Varias rondas: una sola podría no solaparse por casualidad y dar un falso verde.
        for (var round = 0; round < 5; round++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
            {
                await start.Task;
                return await chars.CreateAsync(new NewCharacter(acc.Id, $"Racer{round}x{i}", "warrior", "meadow", 0, 0, 60, 0, [], []), Max, ct);
            }, ct)).ToList();
            start.SetResult();
            var results = await Task.WhenAll(tasks);

            (await chars.CountByAccountAsync(acc.Id, ct)).ShouldBe(Max);
            results.Count(r => r.Status == CreateCharacterStatus.Created).ShouldBe(Max);
            results.Count(r => r.Status == CreateCharacterStatus.LimitReached).ShouldBe(12 - Max);

            foreach (var c in await chars.ListByAccountAsync(acc.Id, ct)) await chars.SoftDeleteAsync(acc.Id, c.Id, ct);
        }
    }
}
