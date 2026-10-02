// NO COMPILADO EN LA SESIÓN DE LA FASE 1 (NuGet bloqueado): revisar con `dotnet build` antes de confiar en él.
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PixelRealms.Persistence.Entities;
using PixelRealms.Persistence.Repositories;

namespace PixelRealms.Persistence.Ef;

/// <summary>Solo una violación de unicidad (23505) del índice indicado es "nombre en uso"; cualquier otro fallo se propaga.</summary>
internal static class UniqueViolation
{
    public static bool Of(DbUpdateException ex, string indexName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == indexName;
}

public sealed class EfAccountRepository(IDbContextFactory<GameDbContext> factory) : IAccountRepository
{
    public async Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // La columna usa la collation case_insensitive: "=" ya ignora mayúsculas y usa el índice único.
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Username == username, ct);
        return a is null ? null : Map(a);
    }

    public async Task<AccountRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : Map(a);
    }

    public async Task<AccountRecord?> CreateAsync(string username, string passwordHash, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Accounts.AnyAsync(x => x.Username == username, ct)) return null;
        var a = new Account { Id = Guid.CreateVersion7(), Username = username, PasswordHash = passwordHash, CreatedAt = DateTime.UtcNow };
        db.Accounts.Add(a);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (UniqueViolation.Of(ex, GameDbContext.AccountsUsernameIndex)) { return null; } // carrera con el índice único
        return Map(a);
    }

    public async Task TouchLastLoginAsync(Guid id, DateTime at, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Accounts.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLoginAt, at), ct);
    }

    public async Task SetAdminAsync(Guid id, bool isAdmin, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Accounts.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsAdmin, isAdmin), ct);
    }

    private static AccountRecord Map(Account a) => new(a.Id, a.Username, a.PasswordHash, a.IsAdmin, a.CreatedAt, a.LastLoginAt);
}

public sealed class EfCharacterRepository(IDbContextFactory<GameDbContext> factory) : ICharacterRepository
{
    public async Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Characters.AsNoTracking().Where(c => c.AccountId == accountId && c.DeletedAt == null).OrderBy(c => c.CreatedAt)
            .Select(c => new CharacterSummary(c.Id, c.Name, c.ClassId, c.Level, c.MapId)).ToListAsync(ct);
    }

    public async Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Characters.CountAsync(c => c.AccountId == accountId && c.DeletedAt == null, ct);
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Characters.AnyAsync(c => c.DeletedAt == null && c.Name == name, ct);
    }

    public async Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Bloquea la fila de la cuenta hasta el COMMIT: las creaciones de una misma cuenta se serializan y el conteo
        // de abajo (READ COMMITTED: instantánea nueva por sentencia) ya ve lo que insertó la anterior.
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM accounts WHERE id = {character.AccountId} FOR UPDATE", ct);
        if (await db.Characters.CountAsync(x => x.AccountId == character.AccountId && x.DeletedAt == null, ct) >= maxPerAccount)
            return new CreateCharacterResult(CreateCharacterStatus.LimitReached);
        var now = DateTime.UtcNow;
        var c = new Character
        {
            Id = Guid.CreateVersion7(), AccountId = character.AccountId, Name = character.Name, ClassId = character.ClassId, Level = 1, MapId = character.MapId, X = character.X, Y = character.Y,
            Hp = character.Hp, Resource = character.Resource, CreatedAt = now, UpdatedAt = now,
        };
        c.Items = character.Items.Select(i => new CharacterItem { Id = i.Id, CharacterId = c.Id, TemplateId = i.TemplateId, Quantity = i.Quantity, Container = i.Container, Slot = i.Slot }).ToList();
        c.Hotbar = character.Hotbar.Select(h => new CharacterHotbarSlot { CharacterId = c.Id, Slot = h.Slot, Kind = h.Kind, Ref = h.Ref }).ToList();
        db.Characters.Add(c);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (UniqueViolation.Of(ex, GameDbContext.CharactersNameIndex)) { return new CreateCharacterResult(CreateCharacterStatus.NameTaken); }
        await tx.CommitAsync(ct);
        return new CreateCharacterResult(CreateCharacterStatus.Created, Map(c));
    }

    public async Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var c = await db.Characters.AsNoTracking().Include(x => x.Items).Include(x => x.Hotbar).Include(x => x.Cooldowns).FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        return c is null ? null : Map(c);
    }

    /// <summary>Una transacción: UPDATE del personaje, DELETE + INSERT masivo de items y barra, INSERT de auditoría (architecture.md §5).</summary>
    public async Task SaveAsync(CharacterSaveDto character, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        await db.Characters.Where(c => c.Id == character.Id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Level, character.Level).SetProperty(c => c.Xp, character.Xp).SetProperty(c => c.Gold, character.Gold).SetProperty(c => c.ClassId, character.ClassId)
            .SetProperty(c => c.MapId, character.MapId).SetProperty(c => c.X, character.X).SetProperty(c => c.Y, character.Y)
            .SetProperty(c => c.Hp, character.Hp).SetProperty(c => c.Resource, character.Resource).SetProperty(c => c.UpdatedAt, now), ct);
        await db.CharacterItems.Where(i => i.CharacterId == character.Id).ExecuteDeleteAsync(ct);
        await db.CharacterHotbar.Where(h => h.CharacterId == character.Id).ExecuteDeleteAsync(ct);
        await db.CharacterCooldowns.Where(c => c.CharacterId == character.Id).ExecuteDeleteAsync(ct);
        db.CharacterItems.AddRange(character.Items.Select(i => new CharacterItem { Id = i.Id, CharacterId = character.Id, TemplateId = i.TemplateId, Quantity = i.Quantity, Container = i.Container, Slot = i.Slot }));
        db.CharacterHotbar.AddRange(character.Hotbar.Select(h => new CharacterHotbarSlot { CharacterId = character.Id, Slot = h.Slot, Kind = h.Kind, Ref = h.Ref }));
        db.CharacterCooldowns.AddRange((character.Cooldowns ?? []).Select(c => new CharacterCooldown { CharacterId = character.Id, Kind = c.Kind, Ref = c.Ref, EndsAt = c.EndsAtUtc }));
        db.ItemAuditLog.AddRange(character.Audit.Select(a => new ItemAuditLog { ItemId = a.ItemId, CharacterId = character.Id, Action = a.Action, TemplateId = a.TemplateId, Quantity = a.Quantity, At = now, CounterpartyCharacterId = a.CounterpartyCharacterId }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var c = await db.Characters.FirstOrDefaultAsync(x => x.Id == characterId && x.AccountId == accountId && x.DeletedAt == null, ct);
        if (c is null) return false;
        c.DeletedAt = DateTime.UtcNow;
        c.Name = $"{c.Name}#deleted-{c.Id:N}";
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static CharacterSaveDto Map(Character c) => new(c.Id, c.AccountId, c.Name, c.ClassId, c.Level, c.Xp, c.Gold, c.MapId, c.X, c.Y, c.Hp, c.Resource,
        c.Items.Select(i => new SavedItem(i.Id, i.TemplateId, i.Quantity, i.Container, i.Slot)).ToList(),
        c.Hotbar.Select(h => new SavedHotbarSlot(h.Slot, h.Kind, h.Ref)).ToList(), [],
        c.Cooldowns.Select(x => new SavedCooldown(x.Kind, x.Ref, DateTime.SpecifyKind(x.EndsAt, DateTimeKind.Utc))).ToList());
}
