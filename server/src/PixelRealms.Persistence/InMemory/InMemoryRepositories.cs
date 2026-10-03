using System.Collections.Concurrent;
using PixelRealms.Persistence.Entities;
using PixelRealms.Persistence.Repositories;

namespace PixelRealms.Persistence.InMemory;

/// <summary>Repositorios en memoria: tests del servidor y modo `Persistence:Provider=InMemory` (sin BD; nada sobrevive al reinicio).</summary>
public sealed class InMemoryStore
{
    public ConcurrentDictionary<Guid, Account> Accounts { get; } = new();

    public ConcurrentDictionary<Guid, Character> Characters { get; } = new();

    public ConcurrentQueue<ItemAuditLog> Audit { get; } = new();

    private long _auditId;

    public long NextAuditId() => Interlocked.Increment(ref _auditId);
}

public sealed class InMemoryAccountRepository(InMemoryStore store) : IAccountRepository
{
    private readonly Lock _lock = new();

    public Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        var a = store.Accounts.Values.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(a is null ? null : Map(a));
    }

    public Task<AccountRecord?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(store.Accounts.TryGetValue(id, out var a) ? Map(a) : null);

    public Task<AccountRecord?> CreateAsync(string username, string passwordHash, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (store.Accounts.Values.Any(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase))) return Task.FromResult<AccountRecord?>(null);
            var a = new Account { Id = Guid.CreateVersion7(), Username = username, PasswordHash = passwordHash, CreatedAt = DateTime.UtcNow };
            store.Accounts[a.Id] = a;
            return Task.FromResult<AccountRecord?>(Map(a));
        }
    }

    public Task TouchLastLoginAsync(Guid id, DateTime at, CancellationToken ct = default)
    {
        if (store.Accounts.TryGetValue(id, out var a)) a.LastLoginAt = at;
        return Task.CompletedTask;
    }

    public Task SetAdminAsync(Guid id, bool isAdmin, CancellationToken ct = default)
    {
        if (store.Accounts.TryGetValue(id, out var a)) a.IsAdmin = isAdmin;
        return Task.CompletedTask;
    }

    private static AccountRecord Map(Account a) => new(a.Id, a.Username, a.PasswordHash, a.IsAdmin, a.CreatedAt, a.LastLoginAt);
}

public sealed class InMemoryCharacterRepository(InMemoryStore store) : ICharacterRepository
{
    private readonly Lock _lock = new();

    public Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        IReadOnlyList<CharacterSummary> list = store.Characters.Values.Where(c => c.AccountId == accountId && c.DeletedAt is null)
            .OrderBy(c => c.CreatedAt).Select(c => new CharacterSummary(c.Id, c.Name, c.ClassId, c.Level, c.MapId)).ToList();
        return Task.FromResult(list);
    }

    public Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(store.Characters.Values.Count(c => c.AccountId == accountId && c.DeletedAt is null));

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(store.Characters.Values.Any(c => c.DeletedAt is null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default)
    {
        // El mismo lock cubre límite y nombre: contar e insertar es atómico, como el FOR UPDATE de EF.
        lock (_lock)
        {
            if (store.Characters.Values.Count(c => c.AccountId == character.AccountId && c.DeletedAt is null) >= maxPerAccount)
                return Task.FromResult(new CreateCharacterResult(CreateCharacterStatus.LimitReached));
            if (store.Characters.Values.Any(c => c.DeletedAt is null && string.Equals(c.Name, character.Name, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(new CreateCharacterResult(CreateCharacterStatus.NameTaken));
            var now = DateTime.UtcNow;
            var c = new Character
            {
                Id = Guid.CreateVersion7(), AccountId = character.AccountId, Name = character.Name, ClassId = character.ClassId, Level = 1, MapId = character.MapId, X = character.X, Y = character.Y,
                Hp = character.Hp, Resource = character.Resource, CreatedAt = now, UpdatedAt = now,
                Items = character.Items.Select(i => new CharacterItem { Id = i.Id, CharacterId = Guid.Empty, TemplateId = i.TemplateId, Quantity = i.Quantity, Container = i.Container, Slot = i.Slot }).ToList(),
                Hotbar = character.Hotbar.Select(h => new CharacterHotbarSlot { Slot = h.Slot, Kind = h.Kind, Ref = h.Ref }).ToList(),
            };
            foreach (var i in c.Items) i.CharacterId = c.Id;
            store.Characters[c.Id] = c;
            return Task.FromResult(new CreateCharacterResult(CreateCharacterStatus.Created, Map(c)));
        }
    }

    public Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(store.Characters.TryGetValue(id, out var c) && c.DeletedAt is null ? Map(c) : null);

    public Task SaveAsync(CharacterSaveDto character, CancellationToken ct = default) => SaveManyAsync([character], ct);

    public Task SaveManyAsync(IReadOnlyList<CharacterSaveDto> characters, CancellationToken ct = default)
    {
        lock (_lock) // todos bajo el mismo cerrojo: nadie ve a uno guardado sin el otro
            foreach (var character in characters) Apply(character);
        return Task.CompletedTask;
    }

    private void Apply(CharacterSaveDto character)
    {
        if (!store.Characters.TryGetValue(character.Id, out var c)) return;
        c.Level = character.Level; c.Xp = character.Xp; c.Gold = character.Gold; c.MapId = character.MapId; c.X = character.X; c.Y = character.Y; c.Hp = character.Hp; c.Resource = character.Resource; c.ClassId = character.ClassId;
        c.UpdatedAt = DateTime.UtcNow;
        c.Items = character.Items.Select(i => new CharacterItem { Id = i.Id, CharacterId = c.Id, TemplateId = i.TemplateId, Quantity = i.Quantity, Container = i.Container, Slot = i.Slot }).ToList();
        c.Hotbar = character.Hotbar.Select(h => new CharacterHotbarSlot { CharacterId = c.Id, Slot = h.Slot, Kind = h.Kind, Ref = h.Ref }).ToList();
        c.Cooldowns = (character.Cooldowns ?? []).Select(x => new CharacterCooldown { CharacterId = c.Id, Kind = x.Kind, Ref = x.Ref, EndsAt = x.EndsAtUtc }).ToList();
        foreach (var a in character.Audit)
            store.Audit.Enqueue(new ItemAuditLog { Id = store.NextAuditId(), ItemId = a.ItemId, CharacterId = c.Id, Action = a.Action, TemplateId = a.TemplateId, Quantity = a.Quantity, At = DateTime.UtcNow, CounterpartyCharacterId = a.CounterpartyCharacterId });
    }

    public Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!store.Characters.TryGetValue(characterId, out var c) || c.AccountId != accountId || c.DeletedAt is not null) return Task.FromResult(false);
            c.DeletedAt = DateTime.UtcNow;
            c.Name = $"{c.Name}#deleted-{c.Id:N}";
            return Task.FromResult(true);
        }
    }

    private static CharacterSaveDto Map(Character c) => new(c.Id, c.AccountId, c.Name, c.ClassId, c.Level, c.Xp, c.Gold, c.MapId, c.X, c.Y, c.Hp, c.Resource,
        c.Items.Select(i => new SavedItem(i.Id, i.TemplateId, i.Quantity, i.Container, i.Slot)).ToList(),
        c.Hotbar.Select(h => new SavedHotbarSlot(h.Slot, h.Kind, h.Ref)).ToList(), [],
        c.Cooldowns.Select(x => new SavedCooldown(x.Kind, x.Ref, x.EndsAt)).ToList());
}
