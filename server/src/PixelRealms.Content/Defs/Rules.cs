using System.Text.Json.Serialization;

namespace PixelRealms.Content.Defs;

// content/rules.json (ADR-008): toda constante numérica del juego. Se carga como RulesDb inmutable y se inyecta como IRules.

/// <summary>Acceso de solo lectura a las reglas. Los sistemas de PixelRealms.Game reciben esta interfaz, nunca números.</summary>
public interface IRules
{
    ProgressionRules Progression { get; }
    GroupRules Group { get; }
    CombatRules Combat { get; }
    IReadOnlyDictionary<string, ClassScaling> ClassScaling { get; }
    AffinityRules Affinity { get; }
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ClassAdvantage { get; }
    PvpRules Pvp { get; }
    LootRules Loot { get; }
    EconomyRules Economy { get; }
    LoadoutRules Loadout { get; }
    WeaponRules Weapons { get; }
    LimitRules Limits { get; }
    BalanceTargets BalanceTargets { get; }
    BossRules Boss { get; }
    MovementRules Movement { get; }
    WorldRules World { get; }

    /// <summary>Hash estable del archivo cargado (Welcome.rulesHash): el cliente detecta un rules.json distinto.</summary>
    string Hash { get; }

    /// <summary>Tope de nivel de la fase activa (ADR-013).</summary>
    int CurrentLevelCap { get; }
}

public sealed record RulesDb : IRules
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    [JsonPropertyName("_doc")] public string? Doc { get; init; }
    public required ProgressionRules Progression { get; init; }
    public required GroupRules Group { get; init; }
    public required CombatRules Combat { get; init; }
    public required IReadOnlyDictionary<string, ClassScaling> ClassScaling { get; init; }
    public required AffinityRules Affinity { get; init; }
    [JsonPropertyName("classAdvantage")] public required ClassAdvantageRules ClassAdvantageTable { get; init; }
    public required PvpRules Pvp { get; init; }
    public required LootRules Loot { get; init; }
    public required EconomyRules Economy { get; init; }
    public required LoadoutRules Loadout { get; init; }
    public required WeaponRules Weapons { get; init; }
    public required LimitRules Limits { get; init; }
    public required BalanceTargets BalanceTargets { get; init; }
    public required BossRules Boss { get; init; }
    public required MovementRules Movement { get; init; }
    public required WorldRules World { get; init; }

    [JsonIgnore] public string Hash { get; init; } = "";

    [JsonIgnore]
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> IRules.ClassAdvantage => ClassAdvantageTable.Rows;

    /// <summary>Tope de nivel de la fase activa: levelCapByPhase[world.currentPhase − 1] (ADR-013).</summary>
    public int CurrentLevelCap => Progression.LevelCapByPhase[World.CurrentPhase - 1];
}

public sealed record ProgressionRules
{
    public required int MaxLevel { get; init; }
    public required IReadOnlyList<int> LevelCapByPhase { get; init; }
    public required IReadOnlyList<double> MinutesPerLevel { get; init; }
    public required double KillCycleSecTarget { get; init; }
    public required double XpRate { get; init; }
    public required double MonsterXpPerLevel { get; init; }
    public required double MonsterXpBase { get; init; }
    public required IReadOnlyDictionary<string, double> MonsterTypeMultiplier { get; init; }
    public required int LevelDiffGreyAt { get; init; }
    public required double LevelDiffModPerLevel { get; init; }
    public required int LevelDiffClamp { get; init; }
    public required IReadOnlyList<int> SpellUnlockLevels { get; init; }
    public required IReadOnlyList<int> SpellRankLevels { get; init; }
    public required double SpellRankBonusPct { get; init; }
    public required ClassChangeRules ClassChange { get; init; }
    public required AltCatchUpRules AltCatchUp { get; init; }
}

public sealed record ClassChangeRules
{
    public required int NpcUntilPhase { get; init; }
}

public sealed record AltCatchUpRules
{
    public required int FromPhase { get; init; }
    public required double XpMultiplier { get; init; }
}

public sealed record GroupRules
{
    public required int MaxMembers { get; init; }
    public required double XpRangeTiles { get; init; }
    public required double ActiveWindowSec { get; init; }
    public required IReadOnlyList<double> BonusBySize { get; init; }
    public required int LevelGapFreeLevels { get; init; }
    public required double LevelGapDecay { get; init; }
    public required double LevelGapMinWeight { get; init; }
    public required double InviteExpireSec { get; init; }
    public required double OfflineGraceSec { get; init; }
}

public sealed record CombatRules
{
    public required int GcdMs { get; init; }
    public required double CritMultiplier { get; init; }
    public required double VarianceMin { get; init; }
    public required double VarianceMax { get; init; }
    public required double PhysicalMissBase { get; init; }
    public required double PhysicalMissPerTargetLevel { get; init; }
    public required double MagicMissBase { get; init; }
    public required double CritBase { get; init; }
    public required double CritPerAgi { get; init; }
    public required double CritPerInt { get; init; }
    public required double CritCap { get; init; }
    public required double DodgeBase { get; init; }
    public required double DodgePerAgi { get; init; }
    public required double DodgeCap { get; init; }
    public required double ArmorPerAgi { get; init; }
    public required double MitigationPerLevel { get; init; }
    public required double MitigationConstant { get; init; }
    public required double MitigationCap { get; init; }
    public required double BasicAttackPowerDivisor { get; init; }
    public required double CastRangeToleranceTiles { get; init; }
    public required double InCombatWindowSec { get; init; }
    public required double HpRegenDelaySec { get; init; }
    public required double HpRegenPerSpi { get; init; }
    public required double HpRegenPerSta { get; init; }
    public required double ManaRegenPerSpiPer5s { get; init; }
    public required double ManaRegenPerIntPer5s { get; init; }
    public required double ManaRegenCastingPenalty { get; init; }
    public required double ManaRegenPenaltyDurationSec { get; init; }
    public required double CastMoveSpeedMult { get; init; }
    public required int InterruptLockoutMs { get; init; }
    public required int AbilityLockMs { get; init; }
    public required int MinInstantSpellCooldownMs { get; init; }
    public required double LinkdeadSec { get; init; }
    public required double LinkdeadInCombatMaxSec { get; init; }
    public required double ManaPerBasicHitPctPerSec { get; init; }
    public required double RagePerHitDealt { get; init; }
    public required double RagePerHitTaken { get; init; }
    public required double RageDecayPerSecOutOfCombat { get; init; }
    public required double EnergyPerSec { get; init; }
    public required double ResourceCap { get; init; }
    public required double ThreatPerDamage { get; init; }
    public required double ThreatPerHeal { get; init; }
    public required double ThreatSwitchMelee { get; init; }
    public required double ThreatSwitchRanged { get; init; }
    public required double TauntThreatBonus { get; init; }
    public required double RespawnHpPct { get; init; }
    public required double RespawnResourcePct { get; init; }
    public required double CorpseLifetimeSec { get; init; }
    public required double EvadeSpeedMult { get; init; }
    public required IReadOnlyList<AuraKind> BossImmuneToAuraKinds { get; init; }
    public required IReadOnlyList<AuraKind> ControlAuraKinds { get; init; }
    public required IReadOnlyList<AuraKind> HardControlKinds { get; init; }
    public required double HardControlImmunitySec { get; init; }
    public required double MaxSlowPct { get; init; }
}

public sealed record StatConv
{
    public required double Str { get; init; }
    public required double Agi { get; init; }
    public required double Int { get; init; }

    public double Apply(Stats s) => Str * s.Str + Agi * s.Agi + Int * s.Int;
}

public sealed record ClassScaling
{
    public required double HpPerSta { get; init; }
    public required double ManaPerInt { get; init; }
    public required StatConv Ap { get; init; }
    public required StatConv Sp { get; init; }
    public required double ArmorMult { get; init; }
    public required double Haste { get; init; }
}

public sealed record AffinityRules
{
    public required IReadOnlyDictionary<string, double> Multipliers { get; init; }
    public required IReadOnlyDictionary<string, Stat> WeaponScaling { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, Affinity>> ByClass { get; init; }

    /// <summary>Afinidad de una clase con un tipo de item; lo no listado cuenta como baja (combat.md §Afinidad).</summary>
    public Affinity Of(string classId, string? itemType)
    {
        if (itemType is null) return Affinity.Alta; // consumibles, materiales: sin afinidad
        return ByClass.TryGetValue(classId, out var row) && row.TryGetValue(itemType, out var a) ? a : Affinity.Baja;
    }

    public double Multiplier(Affinity a) => Multipliers[a.ToString().ToLowerInvariant()];

    public double MultiplierFor(string classId, string? itemType) => itemType is null ? 1.0 : Multiplier(Of(classId, itemType));
}

public sealed record ClassAdvantageRules
{
    [JsonPropertyName("_doc")] public string? Doc { get; init; }
    public required IReadOnlyDictionary<string, double> Warrior { get; init; }
    public required IReadOnlyDictionary<string, double> Rogue { get; init; }
    public required IReadOnlyDictionary<string, double> Mage { get; init; }
    public required IReadOnlyDictionary<string, double> Priest { get; init; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Rows => new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal)
    {
        ["warrior"] = Warrior, ["rogue"] = Rogue, ["mage"] = Mage, ["priest"] = Priest,
    };
}

public sealed record PvpRuleset
{
    public required bool RequiresConsent { get; init; }
    public required double EndAtHpPct { get; init; }
    public required bool RestoreOnEnd { get; init; }
    public required double XpLoss { get; init; }
    public required double GoldLoss { get; init; }
    public required bool ItemLoss { get; init; }
    public required bool AllowedInSafeZones { get; init; }
    public required double RequestExpireSec { get; init; }
    public required double CountdownSec { get; init; }
    public required double MaxDistanceTiles { get; init; }
}

public sealed record PvpRules
{
    public required IReadOnlyDictionary<string, PvpRuleset> Rulesets { get; init; }
    public required string DefaultRuleset { get; init; }
    public required IReadOnlyList<string> EnabledRulesets { get; init; }
}

public sealed record LootRules
{
    public required LootOwnerMode OwnerMode { get; init; }
    public required double ExclusiveSec { get; init; }
    public required double LootRangeTiles { get; init; }
    public required double EligibleRangeTiles { get; init; }
    public required GoldSplit GoldSplit { get; init; }
    public required Rarity AnnounceRarityFrom { get; init; }
}

public sealed record EconomyRules
{
    public required double VendorBuyMultiplier { get; init; }
    public required double VendorRangeTiles { get; init; }
    public required int CopperPerSilver { get; init; }
    public required int SilverPerGold { get; init; }
}

public sealed record LoadoutRules
{
    public required int MaxSpellsPerClass { get; init; }
    public required int SpellSlots { get; init; }
    public required int UsableSlots { get; init; }
}

public sealed record WeaponTypeRules
{
    public required double RangeTiles { get; init; }
    public required string Animation { get; init; }
    public required bool Projectile { get; init; }
}

public sealed record WeaponRules
{
    public required double RangedDpsMult { get; init; }
    public required IReadOnlyDictionary<string, WeaponTypeRules> Types { get; init; }
}

public sealed record LimitRules
{
    public required int MaxPersistentAreasPerCaster { get; init; }
    public required int MaxAreasPerInstance { get; init; }
    public required int MaxPendingImpactsPerInstance { get; init; }
    public required int AoeMaxTargetsCap { get; init; }
    public required int PersistentAreaTickMs { get; init; }
    public required int MaxBuffsPerEntity { get; init; }
    public required int MaxDebuffsPerEntity { get; init; }
}

public sealed record PentagramReferences
{
    public required int Level { get; init; }
    public required double SingleDps { get; init; }
    public required double AoeDps { get; init; }
    public required double CcSecPerMin { get; init; }
    public required double MobilityTilesPerMin { get; init; }
    public required double ArmorTtlSec { get; init; }
}

public sealed record PentagramClass
{
    public required double Single { get; init; }
    public required double Aoe { get; init; }
    public required double Cc { get; init; }
    public required double Mobility { get; init; }
    public required double Armor { get; init; }
    public required string ReferenceWeapon { get; init; }
}

public sealed record PentagramRules
{
    [JsonPropertyName("_doc")] public string? Doc { get; init; }
    public required double Budget { get; init; }
    public required double MaxPerAxis { get; init; }
    public required double AbilityMaxPoints { get; init; }
    public required double LoadoutMaxPct { get; init; }
    public required IReadOnlyList<int> ReferenceLevels { get; init; }
    public PentagramReferences? References { get; init; }
    public required IReadOnlyDictionary<string, PentagramClass> Classes { get; init; }
}

public sealed record BalanceTargets
{
    [JsonPropertyName("_doc")] public string? Doc { get; init; }
    public required IReadOnlyList<double> OffRoleDamagePct { get; init; }
    public required IReadOnlyList<double> OffRoleSurvivalPct { get; init; }
    public required double SoloXpPerHourSpreadPct { get; init; }
    public required IReadOnlyList<double> DuelFavoriteWinRate { get; init; }
    public required IReadOnlyList<double> HoursToMaxLevel { get; init; }
    public required double FirstGroupFightMinutes { get; init; }
    public PentagramRules? Pentagram { get; init; }
}

public sealed record BossRules
{
    [JsonPropertyName("_doc")] public string? Doc { get; init; }
    public required int ReferencePartySize { get; init; }
    public required int ReferenceLevelOffset { get; init; }
    public required IReadOnlyList<double> TargetDurationSec { get; init; }
    public required bool SoloKillable { get; init; }
}

public sealed record MovementRules
{
    public required double BaseSpeedTilesPerSec { get; init; }
    public required double SayRangeTiles { get; init; }
    public required int AoiCellTiles { get; init; }
}

public sealed record WorldRules
{
    public required string StartMapId { get; init; }
    public required int CurrentPhase { get; init; }
    public required IReadOnlyList<double> ZoneCrossTimeSecTarget { get; init; }
}
