namespace PixelRealms.Protocol.Messages;

/// <summary>Mensaje emitido por el servidor (docs/protocol.md §Servidor → Cliente).</summary>
public interface IServerMessage;

public sealed record ItemStackDto(string Id, string TemplateId, int Qty);

public sealed record SelfStateDto(float X, float Y, int Level, int Xp, int XpNext, int Hp, int MaxHp, int Res, int MaxRes, string Resource, string ClassId, string Name);

public sealed record HotbarSlotDto(int Slot, string Kind, string Ref);

/// <param name="SpellUpgrades">HU-104: mejora elegida por hechizo (spellId → upgradeId); se omite si no hay ninguna.</param>
public sealed record Welcome(
    int SelfId, long Tick, int TickRate, int SnapshotRate, string MapId, SelfStateDto Self,
    IReadOnlyList<ItemStackDto?> Inventory, IReadOnlyList<ItemStackDto?> Equipment, IReadOnlyList<HotbarSlotDto> Hotbar,
    IReadOnlyList<string> KnownSpells, string RulesHash, IReadOnlyDictionary<string, string>? SpellUpgrades = null) : IServerMessage;

public sealed record SnapshotSelfDto(float X, float Y, float Speed, int Hp, int MaxHp, int Res, int MaxRes);

/// <summary>Solo campos que cambian con frecuencia; los estáticos van en EntitySpawn.</summary>
public sealed record EntStateDto(int Id, float X, float Y, string Dir, int HpPct, string Anim, int? Tgt);

public sealed record Snapshot(long Tick, int AckSeq, SnapshotSelfDto Self, IReadOnlyList<EntStateDto> Ents) : IServerMessage;

public sealed record EntitySpawn(int Id, string Kind, string TemplateId, string Name, float X, float Y, string Dir, int Level, string? ClassId, int HpPct, int Flags) : IServerMessage;

public sealed record EntityDespawn(int Id, string Reason) : IServerMessage;

/// <param name="UpgradeId">HU-104: la mejora con que se lanza; el cliente dibuja la forma del hechizo mejorado.</param>
public sealed record CastStarted(int CasterId, string SpellId, int? TargetId, Vec2Dto? TargetPos, Vec2Dto? Origin, float? Radius, int DurationMs, string? UpgradeId = null) : IServerMessage;

public sealed record CastEnded(int CasterId, string SpellId, string Result, string? Reason) : IServerMessage;

/// <summary>HU-100 (ADR-018): aparece un área duradera; `pos` es el centro (o el punto canónico de un cono o una línea, con
/// `origin`). Se va con `AreaDespawn` o al pasar `expiresInMs`. Nada por tick.</summary>
public sealed record AreaSpawn(int AreaId, int CasterId, string SpellId, Vec2Dto Pos, Vec2Dto? Origin, int ExpiresInMs, string? UpgradeId = null) : IServerMessage;

public sealed record AreaDespawn(int AreaId) : IServerMessage;

public sealed record CombatEventDto(int Src, int Dst, string? SpellId, string Kind, int Amount, bool Crit, string School);

public sealed record CombatEvents(long Tick, IReadOnlyList<CombatEventDto> E) : IServerMessage;

public sealed record AuraApplied(int TargetId, string AuraId, int? CasterId, int Stacks, int DurationMs) : IServerMessage;

public sealed record AuraRemoved(int TargetId, string AuraId, int? CasterId) : IServerMessage;

public sealed record Cooldown(string? SpellId, int? RemainingMs, int? GcdMs, string? TemplateId = null) : IServerMessage;

public sealed record StatsDto(int Str, int Agi, int Int, int Spi, int Sta);

public sealed record DerivedStatsDto(int MaxHp, int MaxRes, float AttackPower, float SpellPower, float CritChance, float DodgeChance, float Armor, float Haste, float Mitigation);

public sealed record StatsUpdate(int Level, int Xp, int XpNext, StatsDto Stats, DerivedStatsDto Derived, long Gold) : IServerMessage;

public sealed record XpGain(int Amount, int? SourceId) : IServerMessage;

public sealed record RankUpDto(string SpellId, int Rank);

/// <param name="UpgradesUnlocked">HU-104: hechizos cuya mejora se puede elegir desde este nivel (se omite si no hay).</param>
public sealed record LevelUp(int Level, IReadOnlyList<string> NewSpells, IReadOnlyList<RankUpDto>? RankUps, IReadOnlyList<string>? UpgradesUnlocked = null) : IServerMessage;

/// <summary>HU-104: las mejoras elegidas tras un `ChooseSpellUpgrade` aceptado (todas, spellId → upgradeId).</summary>
public sealed record SpellUpgradesUpdate(IReadOnlyDictionary<string, string> Upgrades, int? ReqId) : IServerMessage;

public sealed record InventoryUpdate(IReadOnlyList<ItemStackDto?> Bag, IReadOnlyList<ItemStackDto?> Equipment, long Gold, int? ReqId) : IServerMessage;

public sealed record LootEntryDto(int Index, string TemplateId, int Qty, int OwnerId, int FreeInMs);

public sealed record LootWindow(int LootId, long Gold, IReadOnlyList<LootEntryDto> Items) : IServerMessage;

public sealed record ChangeMap(string MapId, float X, float Y) : IServerMessage;

/// <summary>HU-101: zona del duelo en píxeles (centro y radio); solo la reciben los dos duelistas.</summary>
public sealed record DuelZoneDto(float X, float Y, float R);

/// <summary>
/// Ciclo de vida del duelo. `Zone` va en `countdown` y `active`; `OutsideMs` = ms que le quedan a quien está fuera de la zona
/// para volver (se reenvía `active` solo a él al salir y al volver, sin el campo). `Reason` en `ended`/`declined` (HU-101).
/// </summary>
public sealed record DuelUpdate(string State, int OpponentId, int? WinnerId, int? StartsInMs, DuelZoneDto? Zone = null, int? OutsideMs = null,
    string? Reason = null) : IServerMessage;

/// <summary>Objeto de una oferta de intercambio tal como lo ven los dos: id de la instancia, plantilla y cantidad.</summary>
public sealed record OfferedItemDto(string ItemId, string TemplateId, int Qty);

public sealed record OfferDto(IReadOnlyList<OfferedItemDto> Items, long Gold);

public sealed record TradeUpdate(string State, int PartnerId, int Version, OfferDto Mine, OfferDto Theirs, bool ConfirmedMine, bool ConfirmedTheirs, string? Reason) : IServerMessage;

public sealed record VendorItemDto(string TemplateId, long Price);

public sealed record VendorWindow(int NpcId, IReadOnlyList<VendorItemDto> Items) : IServerMessage;

public sealed record ChatMessage(string Channel, string From, string Text, long Ts) : IServerMessage;

/// <summary>`ResPct` (aditivo, HU-062 CA1): recurso del compañero en % (maná, ira o energía según su clase).</summary>
public sealed record PartyMemberDto(string Name, int? EntityId, string ClassId, int Level, int HpPct, bool Online, string? MapId, int? ResPct = null);

public sealed record PartyUpdate(string Leader, IReadOnlyList<PartyMemberDto> Members) : IServerMessage;

public sealed record Died(int? KillerId, int RespawnInMs) : IServerMessage;

public sealed record Error(string Code, string? Message, int? ReqId) : IServerMessage;

public sealed record Pong(long ClientTime, long ServerTick) : IServerMessage;

/// <summary>HU-015: respuesta a `Logout` aceptado; el personaje ya está guardado (encolado) y fuera del mundo. Después el
/// servidor cierra la conexión con motivo "logout".</summary>
public sealed record LoggedOut : IServerMessage;

/// <summary>HU-063: un jugador conectado (nombre, clase, nivel y zona donde está).</summary>
public sealed record OnlinePlayerDto(string Name, string ClassId, int Level, string Zone);

/// <summary>HU-063: respuesta a `OnlineListRequest`, ordenada por nombre.</summary>
public sealed record OnlineList(IReadOnlyList<OnlinePlayerDto> Players) : IServerMessage;

/// <summary>HU-083: estado de un objeto del mapa: palanca `on`/`off`, puerta `open`/`closed`.</summary>
public sealed record MapObjectDto(string Id, string State);

/// <summary>HU-083: todos los objetos del mapa al entrar en él (tras `Welcome` o `ChangeMap`) y luego solo los que cambian.</summary>
public sealed record MapObjects(IReadOnlyList<MapObjectDto> Objects) : IServerMessage;

/// <summary>Códigos de error de docs/protocol.md §Códigos de error.</summary>
public static class ErrorCodes
{
    public const string BadVersion = "bad_version";
    public const string BadTicket = "bad_ticket";
    public const string RateLimited = "rate_limited";
    public const string NotFound = "not_found";
    public const string OutOfRange = "out_of_range";
    public const string NoLos = "no_los";
    public const string OnCooldown = "on_cooldown";
    public const string OnGcd = "on_gcd";
    public const string NotEnoughResource = "not_enough_resource";
    public const string InvalidTarget = "invalid_target";
    public const string IsDead = "is_dead";
    public const string Stunned = "stunned";
    public const string Rooted = "rooted";
    public const string Silenced = "silenced";
    public const string LockedOut = "locked_out";
    public const string AreaLimit = "area_limit";
    public const string NotEquipped = "not_equipped";
    public const string BagFull = "bag_full";
    public const string NotEnoughGold = "not_enough_gold";
    public const string LevelTooLow = "level_too_low";
    public const string NotOwner = "not_owner";
    public const string InCombat = "in_combat";
    public const string PvpNotAllowed = "pvp_not_allowed";
    public const string DuelBusy = "duel_busy";
    public const string TradeBusy = "trade_busy";
    public const string TradeVersion = "trade_version";
    public const string Forbidden = "forbidden";
    public const string InvalidPayload = "invalid_payload";
}
