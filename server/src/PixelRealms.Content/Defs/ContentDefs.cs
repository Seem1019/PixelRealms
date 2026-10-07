using System.Text.Json.Serialization;

namespace PixelRealms.Content.Defs;

// Modelos inmutables de content/*.json (uno por schema). Los nombres de propiedad son los del JSON en camelCase;
// la deserialización usa UnmappedMemberHandling.Disallow, así que cada campo del schema está aquí.

public sealed record Stats
{
    public int Str { get; init; }
    public int Agi { get; init; }
    public int Int { get; init; }
    public int Spi { get; init; }
    public int Sta { get; init; }

    public static readonly Stats Zero = new();

    public int Get(string stat) => stat switch
    {
        "str" => Str, "agi" => Agi, "int" => Int, "spi" => Spi, "sta" => Sta,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "stat desconocido"),
    };

    public static Stats operator +(Stats a, Stats b) => new() { Str = a.Str + b.Str, Agi = a.Agi + b.Agi, Int = a.Int + b.Int, Spi = a.Spi + b.Spi, Sta = a.Sta + b.Sta };

    public static Stats operator *(Stats a, int k) => new() { Str = a.Str * k, Agi = a.Agi * k, Int = a.Int * k, Spi = a.Spi * k, Sta = a.Sta * k };
}

// ---------------------------------------------------------------- classes.json
public sealed record ClassesFile
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required IReadOnlyList<ClassDef> Classes { get; init; }
}

public sealed record StartingItemDef
{
    public required string ItemId { get; init; }
    public int Qty { get; init; } = 1;
    public bool Equip { get; init; }
}

public sealed record ClassDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ClassRole Role { get; init; }
    public required Resource Resource { get; init; }
    public required int BaseHp { get; init; }
    public int BaseMana { get; init; }
    public required Stats BaseStats { get; init; }
    public required Stats StatsPerLevel { get; init; }
    public required IReadOnlyList<StartingItemDef> StartingItems { get; init; }
    public required string Sprite { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<WeaponType> RecommendedWeapons { get; init; }
    public required IReadOnlyList<ArmorType> RecommendedArmor { get; init; }
}

// ---------------------------------------------------------------- spells.json
public sealed record SpellsFile
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required IReadOnlyList<SpellDef> Spells { get; init; }
}

public sealed record CostDef
{
    public required Resource Resource { get; init; }
    public required int Amount { get; init; }
}

public sealed record ProjectileDef
{
    public required string Sprite { get; init; }
    public required double Speed { get; init; }
}

public sealed record EffectDef
{
    public required EffectType Type { get; init; }
    public double Base { get; init; }
    public double ApCoef { get; init; }
    public double SpCoef { get; init; }
    public double WeaponPct { get; init; }
    public string? AuraId { get; init; }
    public Resource? Resource { get; init; }
    public double Amount { get; init; }
    public int DurationMs { get; init; }
    public double MinRange { get; init; }
    public double MaxRange { get; init; }
    public int TravelMs { get; init; }
    public ApplyTo ApplyTo { get; init; } = ApplyTo.Targets;
    public double BonusBelowHpPct { get; init; }
    public double BonusMult { get; init; } = 1.0;
    /// <summary>`apply_aura` de un hechizo mejorado (HU-104): copia del aura con los campos que cambia la mejora, mismo id. No
    /// viene del JSON: la pone <see cref="SpellUpgrades.Apply"/>.</summary>
    [JsonIgnore] public AuraDef? AuraOverride { get; init; }
}

/// <summary>Mejora 1-de-2 de un hechizo de clase (HU-104, ADR-027 D1): una lista de modificadores genéricos.</summary>
public sealed record SpellUpgradeDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<SpellModDef> Mods { get; init; }
}

/// <summary>
/// Un modificador de una mejora, de uno de cuatro tipos: un campo del hechizo (`stat`); la potencia de sus efectos de un tipo
/// (`effect` + `mult`: base y coeficientes); un campo de un aura que aplica (`aura` + `stat`: `durationMs`, `pct` o `amount`);
/// o un efecto añadido (`addEffect`). Valor final = valor · `mult` + `add`.
/// </summary>
public sealed record SpellModDef
{
    public string? Stat { get; init; }
    public EffectType? Effect { get; init; }
    public string? Aura { get; init; }
    public double Add { get; init; }
    public double Mult { get; init; } = 1.0;
    public EffectDef? AddEffect { get; init; }
}

public sealed record SpellDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required SpellSource Source { get; init; }
    public string? ClassId { get; init; }
    public int LevelReq { get; init; } = 1;
    public required School School { get; init; }
    public required int CastMs { get; init; }
    public required int CooldownMs { get; init; }
    public bool TriggersGcd { get; init; } = true;
    /// <summary>Solo fuera de combate (`in_combat`), como comer.</summary>
    public bool OutOfCombatOnly { get; init; }
    public CostDef? Cost { get; init; }
    public required double Range { get; init; }
    public required Targeting Targeting { get; init; }
    public double AoeRadius { get; init; }
    public int MaxTargets { get; init; } = 10;
    public ProjectileDef? Projectile { get; init; }
    public required IReadOnlyList<EffectDef> Effects { get; init; }
    public required string Icon { get; init; }
    public string? Vfx { get; init; }
    public required string Description { get; init; }
    public Shape Shape { get; init; } = Shape.Circle;
    public double AoeAngleDeg { get; init; }
    public double AoeLength { get; init; }
    public double AoeWidth { get; init; }
    public bool Provisional { get; init; }
    /// <summary>HU-104: las dos mejoras entre las que elige el jugador (vacío si el hechizo no tiene).</summary>
    public IReadOnlyList<SpellUpgradeDef> Upgrades { get; init; } = [];
    /// <summary>En un hechizo efectivo, la mejora aplicada (viaja en `CastStarted`/`AreaSpawn` para que los demás dibujen la
    /// forma mejorada). No viene del JSON: la pone <see cref="SpellUpgrades.Apply"/>.</summary>
    [JsonIgnore] public string? AppliedUpgradeId { get; init; }

    public bool IsInstant => CastMs == 0;
}

// ---------------------------------------------------------------- auras.json
public sealed record AurasFile
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required IReadOnlyList<AuraDef> Auras { get; init; }
}

public sealed record AuraMods
{
    public Stats? Stats { get; init; }
    public double DamageTakenPct { get; init; }
    public double DamageDonePct { get; init; }
    public double SpeedPct { get; init; }
}

public sealed record AuraDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required AuraKind Kind { get; init; }
    public required bool IsDebuff { get; init; }
    public School? School { get; init; }
    public required int DurationMs { get; init; }
    public int TickMs { get; init; }
    public int MaxStacks { get; init; } = 1;
    public double Base { get; init; }
    public double ApCoef { get; init; }
    public double SpCoef { get; init; }
    public double Pct { get; init; }
    public AuraMods? Mods { get; init; }
    public required string Icon { get; init; }
    public IReadOnlyList<AuraKind> RemovesKinds { get; init; } = [];
    public IReadOnlyList<AuraKind> ImmuneKinds { get; init; } = [];
    /// <summary>Recibir daño la quita (comer).</summary>
    public bool BreaksOnDamage { get; init; }
    public bool Provisional { get; init; }
}

// ---------------------------------------------------------------- items.json
public sealed record ItemsFile
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required IReadOnlyList<ItemTemplate> Items { get; init; }
}

public sealed record ItemTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ItemType Type { get; init; }
    public required Rarity Rarity { get; init; }
    public int LevelReq { get; init; } = 1;
    public EquipSlot? Slot { get; init; }
    public WeaponType? WeaponType { get; init; }
    public ArmorType? ArmorType { get; init; }
    public int DamageMin { get; init; }
    public int DamageMax { get; init; }
    public int SpeedMs { get; init; }
    public int Armor { get; init; }
    public int SpellPower { get; init; }
    public Stats? Stats { get; init; }
    public int MaxStack { get; init; } = 1;
    public string? UseSpellId { get; init; }
    public int UseCooldownMs { get; init; }
    public required int SellPrice { get; init; }
    public int? VendorPrice { get; init; }
    public required string Icon { get; init; }
    public string? Description { get; init; }
    public Stat? Scaling { get; init; }

    public bool IsEquippable => Slot is not null;
    public bool IsWeapon => WeaponType is not null;
    public bool IsStackable => MaxStack > 1;

    /// <summary>Tipo de afinidad del item: el tipo de arma o de armadura; null para consumibles y materiales.</summary>
    public string? AffinityType => WeaponType?.ToString().ToLowerInvariant() ?? ArmorType?.ToString().ToLowerInvariant();
}

// ---------------------------------------------------------------- monsters.json
public sealed record MonstersFile
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required IReadOnlyList<MonsterTemplate> Monsters { get; init; }
}

public sealed record MonsterSpellDef
{
    public required string SpellId { get; init; }
    public double HpBelowPct { get; init; } = 1.0;
    public MonsterSpellTarget Target { get; init; } = MonsterSpellTarget.Current;
}

public sealed record MonsterTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required MonsterType Type { get; init; }
    public required int Hp { get; init; }
    public required int DamageMin { get; init; }
    public required int DamageMax { get; init; }
    public School School { get; init; } = School.Physical;
    public required int AttackSpeedMs { get; init; }
    public required double AttackRange { get; init; }
    public required int Armor { get; init; }
    public required double AggroRange { get; init; }
    public required double LeashRange { get; init; }
    public required double Speed { get; init; }
    public required string LootTableId { get; init; }
    public required int RespawnSec { get; init; }
    public bool Boss { get; init; }
    public IReadOnlyList<MonsterSpellDef> Spells { get; init; } = [];
    public required string Sprite { get; init; }
    [JsonPropertyName("_note")] public string? Note { get; init; }
}

// ---------------------------------------------------------------- loot_tables.json
public sealed record LootTablesFile
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required IReadOnlyList<LootTable> LootTables { get; init; }
}

public sealed record IntRange
{
    public required int Min { get; init; }
    public required int Max { get; init; }
}

public sealed record LootEntry
{
    public required string ItemId { get; init; }
    public required double Chance { get; init; }
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;
}

public sealed record LootGroupEntry
{
    public required string ItemId { get; init; }
    public required double Weight { get; init; }
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 1;
}

public sealed record LootGroup
{
    public required int Rolls { get; init; }
    public required IReadOnlyList<LootGroupEntry> Entries { get; init; }
}

public sealed record LootTable
{
    public required string Id { get; init; }
    public required IntRange Gold { get; init; }
    public required IReadOnlyList<LootEntry> Entries { get; init; }
    public int MaxItems { get; init; } = 4;
    public IReadOnlyList<LootGroup> Groups { get; init; } = [];
}

// ---------------------------------------------------------------- vendors.json
public sealed record VendorsFile
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required IReadOnlyList<VendorDef> Vendors { get; init; }
}

public sealed record VendorDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Sprite { get; init; }
    public required IReadOnlyList<string> Items { get; init; }
}
