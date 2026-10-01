namespace PixelRealms.Persistence.Repositories;

// DTOs inmutables que cruzan del tick a la persistencia (regla 2): el tick nunca entrega entidades vivas.

public sealed record SavedItem(Guid Id, string TemplateId, int Quantity, short Container, short Slot);

public sealed record SavedHotbarSlot(short Slot, short Kind, string Ref);

public sealed record AuditEntry(Guid ItemId, string Action, string TemplateId, int Quantity, Guid? CounterpartyCharacterId = null);

/// <summary>Personaje completo tal como se guarda/carga (stats base no: se derivan de clase + nivel + equipo).</summary>
public sealed record CharacterSaveDto(
    Guid Id, Guid AccountId, string Name, string ClassId, int Level, int Xp, long Gold, string MapId, float X, float Y, int Hp, int Resource,
    IReadOnlyList<SavedItem> Items, IReadOnlyList<SavedHotbarSlot> Hotbar, IReadOnlyList<AuditEntry> Audit);

public sealed record CharacterSummary(Guid Id, string Name, string ClassId, int Level, string MapId);

public sealed record NewCharacter(Guid AccountId, string Name, string ClassId, string MapId, float X, float Y, int Hp, int Resource, IReadOnlyList<SavedItem> Items, IReadOnlyList<SavedHotbarSlot> Hotbar);

public sealed record AccountRecord(Guid Id, string Username, string PasswordHash, bool IsAdmin, DateTime CreatedAt, DateTime? LastLoginAt);
