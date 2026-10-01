using Microsoft.Extensions.Logging.Abstractions;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Hosting;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Hosting;

/// <summary>HU-026 CA4: 3 intentos con backoff; el fallo definitivo se loguea con el DTO y no tumba nada.</summary>
public sealed class SaveServiceTests
{
    private sealed class FlakyRepo(int failures) : ICharacterRepository
    {
        private int _calls;
        public int Saves { get; private set; }

        public Task SaveAsync(CharacterSaveDto dto, CancellationToken ct = default)
        {
            if (++_calls <= failures) throw new InvalidOperationException("bd caída");
            Saves++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CharacterSaveDto?> CreateAsync(NewCharacter character, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static CharacterSaveDto Dto() => new(Guid.NewGuid(), Guid.NewGuid(), "Ana", "warrior", 1, 0, 0, "meadow", 1, 1, 10, 0, [], [], []);

    [Fact]
    public async Task RetriesTwice_ThenSucceeds()
    {
        var repo = new FlakyRepo(2);
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance);
        await svc.SaveWithRetryAsync(Dto(), CancellationToken.None);
        repo.Saves.ShouldBe(1);
        svc.Saved.ShouldBe(1);
        svc.Failed.ShouldBe(0);
    }

    [Fact]
    public async Task ThreeFailures_CountsAsFailed_WithoutThrowing()
    {
        var repo = new FlakyRepo(3);
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance);
        await svc.SaveWithRetryAsync(Dto(), CancellationToken.None);
        repo.Saves.ShouldBe(0);
        svc.Failed.ShouldBe(1);
    }
}
