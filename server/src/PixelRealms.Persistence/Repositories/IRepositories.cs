namespace PixelRealms.Persistence.Repositories;

/// <summary>Cuentas (HU-010/011). Los nombres son únicos sin distinguir mayúsculas (índice sobre lower(username)).</summary>
public interface IAccountRepository
{
    Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken ct = default);

    Task<AccountRecord?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Devuelve null si el nombre ya existe (sin distinguir mayúsculas).</summary>
    Task<AccountRecord?> CreateAsync(string username, string passwordHash, CancellationToken ct = default);

    Task TouchLastLoginAsync(Guid id, DateTime at, CancellationToken ct = default);

    Task SetAdminAsync(Guid id, bool isAdmin, CancellationToken ct = default);
}

/// <summary>Personajes (HU-012/013/014/026/057).</summary>
public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(Guid accountId, CancellationToken ct = default);

    Task<int> CountByAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>¿Existe un personaje no borrado con ese nombre (sin distinguir mayúsculas)?</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);

    /// <summary>Crea el personaje si la cuenta tiene menos de <paramref name="maxPerAccount"/> vivos y el nombre está libre.
    /// Comprobar el límite e insertar es atómico frente a creaciones concurrentes de la misma cuenta.</summary>
    Task<CreateCharacterResult> CreateAsync(NewCharacter character, int maxPerAccount, CancellationToken ct = default);

    /// <summary>Carga completa (items + hotbar). Null si no existe o está borrado.</summary>
    Task<CharacterSaveDto?> LoadAsync(Guid id, CancellationToken ct = default);

    /// <summary>Guarda el personaje completo en una transacción (DELETE items + INSERT, HU-026/HU-057).</summary>
    Task SaveAsync(CharacterSaveDto character, CancellationToken ct = default);

    /// <summary>Soft delete; el nombre queda libre con sufijo `#deleted-&lt;id&gt;` (HU-013 CA4, MVP). False si no es de esa cuenta.</summary>
    Task<bool> SoftDeleteAsync(Guid accountId, Guid characterId, CancellationToken ct = default);
}
