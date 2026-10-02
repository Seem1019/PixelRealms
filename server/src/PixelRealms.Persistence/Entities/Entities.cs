namespace PixelRealms.Persistence.Entities;

// Entidades de docs/database.md (snake_case lo aplica EFCore.NamingConventions). Son POCOs: también las usa el repositorio en memoria.

public sealed class Account
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<Character> Characters { get; set; } = new();
}

public sealed class Character
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public string Name { get; set; } = "";
    public string ClassId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public long Gold { get; set; }
    public string MapId { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public int Hp { get; set; }
    public int Resource { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<CharacterItem> Items { get; set; } = new();
    public List<CharacterHotbarSlot> Hotbar { get; set; } = new();
    public List<CharacterCooldown> Cooldowns { get; set; } = new();
}

/// <summary>Instancia de item (id de instancia NUNCA se reutiliza). container: 0 = bolsa, 1 = equipo.</summary>
public sealed class CharacterItem
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public string TemplateId { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public short Container { get; set; }
    public short Slot { get; set; }
}

/// <summary>slot 0..7 (0–3 hechizos, 4–7 utilizables); kind 0 = spell, 1 = item.</summary>
public sealed class CharacterHotbarSlot
{
    public Guid CharacterId { get; set; }
    public short Slot { get; set; }
    public short Kind { get; set; }
    public string Ref { get; set; } = "";
}

/// <summary>Cooldown que sigue corriendo al salir (HU-015). kind 0 = spell, 1 = item (plantilla de consumible).</summary>
public sealed class CharacterCooldown
{
    public Guid CharacterId { get; set; }
    public short Kind { get; set; }
    public string Ref { get; set; } = "";
    public DateTime EndsAt { get; set; }
}

public sealed class ItemAuditLog
{
    public long Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid CharacterId { get; set; }
    public string Action { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public int Quantity { get; set; }
    public DateTime At { get; set; }
    public Guid? CounterpartyCharacterId { get; set; }
}
