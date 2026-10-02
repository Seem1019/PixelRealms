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
        public Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Cada guardado tarda <paramref name="delay"/>; con <paramref name="honorCancel"/> el token lo corta (como un SaveAsync de EF).</summary>
    private sealed class SlowRepo(TimeSpan delay, bool honorCancel) : ICharacterRepository
    {
        private int _saves;
        public int Saves => _saves;

        public async Task SaveAsync(CharacterSaveDto dto, CancellationToken ct = default)
        {
            await Task.Delay(delay, honorCancel ? ct : CancellationToken.None);
            Interlocked.Increment(ref _saves);
        }

        public Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>La primera llamada falla con una cancelación que no viene del token del guardado (p. ej. un timeout interno del driver).</summary>
    private sealed class SpuriousCancelRepo : ICharacterRepository
    {
        private int _calls;
        private int _saves;
        public int Saves => _saves;

        public Task SaveAsync(CharacterSaveDto dto, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1) throw new TaskCanceledException("timeout del driver");
            Interlocked.Increment(ref _saves);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Las primeras <paramref name="failures"/> llamadas fallan; anota la auditoría de cada guardado escrito.</summary>
    private sealed class AuditRecorderRepo(int failures) : ICharacterRepository
    {
        private int _calls;
        public System.Collections.Concurrent.ConcurrentQueue<AuditEntry> Written { get; } = new();

        public Task SaveAsync(CharacterSaveDto dto, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) <= failures) throw new InvalidOperationException("bd caída");
            foreach (var a in dto.Audit) Written.Enqueue(a);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static CharacterSaveDto Dto() => new(Guid.NewGuid(), Guid.NewGuid(), "Ana", "warrior", 1, 0, 0, "meadow", 1, 1, 10, 0, [], [], []);

    private static async Task WaitUntil(Func<bool> cond, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!cond())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condición no cumplida");
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task AuditOfASaveThatFailedForGood_IsWrittenWithTheNextSaveOfThatCharacter() // HU-015 / HU-057 CA3
    {
        var repo = new AuditRecorderRepo(3); // el primer guardado agota sus 3 intentos
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance);
        await svc.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var first = Dto() with { Audit = [new AuditEntry(Guid.NewGuid(), "loot", "potion_minor", 1)] };
            svc.Enqueue(first);
            // Failed sube dentro del guardado y la auditoría se aparta justo después: se espera a esto último.
            await WaitUntil(() => svc.UnwrittenAuditCount(first.Id) == 1);
            svc.Failed.ShouldBe(1);

            var next = first with { Audit = [new AuditEntry(Guid.NewGuid(), "sell", "potion_minor", 1)] };
            svc.Enqueue(next);
            await WaitUntil(() => svc.Saved == 1);

            repo.Written.Select(a => a.Action).ShouldBe(["loot", "sell"]); // la del guardado fallido primero, sin duplicar
            svc.UnwrittenAuditCount(first.Id).ShouldBe(0);
        }
        finally { await svc.StopAsync(CancellationToken.None); }
    }

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

    [Fact]
    public async Task WaitForCharacter_ReturnsOnceThatCharactersSaveIsWritten() // HU-015 CA5
    {
        var repo = new SlowRepo(TimeSpan.FromMilliseconds(300), honorCancel: false);
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance);
        await svc.StartAsync(TestContext.Current.CancellationToken);
        var dto = Dto();
        svc.Enqueue(dto);
        svc.HasPending(dto.Id).ShouldBeTrue();
        svc.HasPending(Guid.NewGuid()).ShouldBeFalse(); // otro personaje no espera
        await svc.WaitForCharacterAsync(dto.Id, TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        repo.Saves.ShouldBe(1); // al volver, el guardado ya está escrito
        svc.HasPending(dto.Id).ShouldBeFalse();
        await svc.StopAsync(TestContext.Current.CancellationToken);
    }

    // Sin StartAsync el servicio no consume en segundo plano: todo lo encolado lo vacía StopAsync.

    [Fact]
    public async Task Drain_DeadlineReachedBetweenSaves_CountsTheRestAsFailed()
    {
        var repo = new SlowRepo(TimeSpan.FromMilliseconds(300), honorCancel: false);
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance) { DrainTimeout = TimeSpan.FromMilliseconds(100) };
        svc.Enqueue(Dto());
        svc.Enqueue(Dto());

        await svc.StopAsync(CancellationToken.None);

        repo.Saves.ShouldBe(1);
        svc.Saved.ShouldBe(1);
        svc.Failed.ShouldBe(1); // el segundo no se escribió: se cuenta y se loguea, no se descarta en silencio
        svc.Pending.ShouldBe(0);
    }

    [Fact]
    public async Task Drain_DeadlineReachedDuringSave_DoesNotThrow_AndCountsEverythingLeft()
    {
        var repo = new SlowRepo(Timeout.InfiniteTimeSpan, honorCancel: true);
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance) { DrainTimeout = TimeSpan.FromMilliseconds(100) };
        for (var i = 0; i < 3; i++) svc.Enqueue(Dto());

        await Should.NotThrowAsync(() => svc.StopAsync(CancellationToken.None));

        repo.Saves.ShouldBe(0);
        svc.Failed.ShouldBe(3); // el que estaba en curso y los dos que no llegaron a empezar
        svc.Pending.ShouldBe(0);
    }

    [Fact]
    public async Task Stop_WithHostDeadlineGone_WhileBackgroundSaveRuns_CountsTheQueueAsFailedOnceItEnds()
    {
        var repo = new SlowRepo(TimeSpan.FromMilliseconds(600), honorCancel: false);
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance) { DrainTimeout = TimeSpan.FromMilliseconds(100) };
        await svc.StartAsync(CancellationToken.None);
        for (var i = 0; i < 3; i++) svc.Enqueue(Dto());
        await WaitUntil(() => svc.Pending == 3 && repo.Saves == 0); // el primero está en curso en segundo plano

        // El GameLoop agotó el plazo del host: SaveService recibe el token ya cancelado.
        await svc.StopAsync(new CancellationToken(canceled: true));

        await WaitUntil(() => svc.Pending == 0);
        svc.Saved.ShouldBe(1); // el que estaba en curso termina
        svc.Failed.ShouldBe(2); // los que quedaban en cola se loguean con su DTO, no solo un recuento
    }

    [Fact]
    public async Task EnqueueAfterStop_IsRejected_AndCountedAsFailed()
    {
        var svc = new SaveService(new FlakyRepo(0), NullLogger<SaveService>.Instance);
        await svc.StopAsync(CancellationToken.None);

        svc.Enqueue(Dto());

        svc.Failed.ShouldBe(1); // nadie lo va a leer: perderlo en silencio no es una opción
        svc.Pending.ShouldBe(0);
    }

    [Fact]
    public async Task SpuriousCancellation_IsRetried_AndBackgroundSavingKeepsRunning()
    {
        var repo = new SpuriousCancelRepo();
        var svc = new SaveService(repo, NullLogger<SaveService>.Instance);
        await svc.StartAsync(CancellationToken.None);
        try
        {
            svc.Enqueue(Dto());
            await WaitUntil(() => svc.Saved == 1); // reintentado tras la cancelación espuria
            svc.Enqueue(Dto());
            await WaitUntil(() => svc.Saved == 2); // el bucle de fondo sigue vivo
            svc.Failed.ShouldBe(0);
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
        }
    }
    [Fact]
    public async Task Generations_GrowPerCharacter_AndOnlyWrittenSavesCount() // HU-015 CA5
    {
        var svc = new SaveService(new FlakyRepo(0), NullLogger<SaveService>.Instance);
        var dto = Dto();
        svc.WrittenGeneration(dto.Id).ShouldBe(0);
        await svc.StartAsync(CancellationToken.None);
        try
        {
            svc.Enqueue(dto).ShouldBe(1);
            svc.Enqueue(dto).ShouldBe(2);
            await WaitUntil(() => svc.WrittenGeneration(dto.Id) == 2); // `Saved` sube un instante antes de marcarlo escrito
            svc.Saved.ShouldBe(2);
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
        }
        var failing = new SaveService(new FlakyRepo(99), NullLogger<SaveService>.Instance);
        await failing.StartAsync(CancellationToken.None);
        try
        {
            failing.Enqueue(dto).ShouldBe(1);
            await WaitUntil(() => failing.Failed == 1 && failing.Pending == 0, 5000);
            failing.WrittenGeneration(dto.Id).ShouldBe(0, "un guardado fallido no está en la BD");
        }
        finally
        {
            await failing.StopAsync(CancellationToken.None);
        }
    }
}
