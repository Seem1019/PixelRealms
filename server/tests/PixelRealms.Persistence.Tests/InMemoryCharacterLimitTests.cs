using PixelRealms.Persistence.InMemory;
using PixelRealms.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace PixelRealms.Persistence.Tests;

/// <summary>Mismo contrato que EF: el límite por cuenta se respeta con creaciones concurrentes (HU-012 CA4).</summary>
public sealed class InMemoryCharacterLimitTests
{
    private const int Max = 4;

    [Fact]
    public async Task ConcurrentCreates_NeverExceedLimitPerAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var round = 0; round < 50; round++)
        {
            var repo = new InMemoryCharacterRepository(new InMemoryStore());
            var acc = Guid.CreateVersion7();
            using var start = new ManualResetEventSlim();
            var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            {
                start.Wait(ct);
                return repo.CreateAsync(new NewCharacter(acc, $"Racer{i}", "warrior", "meadow", 0, 0, 60, 0, [], []), Max, ct);
            }, ct)).ToList();
            start.Set();
            var results = await Task.WhenAll(tasks);

            (await repo.CountByAccountAsync(acc, ct)).ShouldBe(Max);
            results.Count(r => r.Status == CreateCharacterStatus.Created).ShouldBe(Max);
        }
    }

    [Fact]
    public async Task LimitReached_IsReported_AndOtherAccountsUnaffected()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new InMemoryCharacterRepository(new InMemoryStore());
        var acc = Guid.CreateVersion7();
        for (var i = 0; i < Max; i++)
            (await repo.CreateAsync(new NewCharacter(acc, $"Hero{i}", "warrior", "meadow", 0, 0, 60, 0, [], []), Max, ct)).Status.ShouldBe(CreateCharacterStatus.Created);
        (await repo.CreateAsync(new NewCharacter(acc, "HeroX", "warrior", "meadow", 0, 0, 60, 0, [], []), Max, ct)).Status.ShouldBe(CreateCharacterStatus.LimitReached);
        (await repo.CreateAsync(new NewCharacter(Guid.CreateVersion7(), "HeroX", "warrior", "meadow", 0, 0, 60, 0, [], []), Max, ct)).Status.ShouldBe(CreateCharacterStatus.Created);
    }
}
