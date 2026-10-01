using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Combat;

/// <summary>Tipos de entrada de `CombatEvents.e[].kind` (docs/protocol.md).</summary>
public static class HitKinds
{
    public const string Damage = "dmg";
    public const string Heal = "heal";
    public const string Miss = "miss";
    public const string Dodge = "dodge";
    public const string Absorb = "absorb";
    public const string Immune = "immune";
}

/// <summary>Resultados de `CastEnded.result`.</summary>
public static class CastResults
{
    public const string Done = "done";
    public const string Interrupted = "interrupted";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public sealed record CastStartedEvent(int MapInstanceId, Actor Caster, SpellDef Spell, EntityId? TargetId, Vec2? TargetPos, int DurationMs) : IGameEvent;

public sealed record CastEndedEvent(int MapInstanceId, Actor Caster, SpellDef Spell, string Result, string? Reason) : IGameEvent;

/// <summary>Un resultado de combate (golpe, cura, fallo, esquiva, absorción, inmune) para el lote del tick (ADR-018).</summary>
public sealed record CombatHitEvent(int MapInstanceId, Actor Source, Actor Target, string? SpellId, string Kind, int Amount, bool Crit, School School) : IGameEvent;

public sealed record AuraAppliedEvent(int MapInstanceId, Actor Target, AuraInstance Aura) : IGameEvent;

public sealed record AuraRemovedEvent(int MapInstanceId, Actor Target, string AuraId, EntityId? CasterId) : IGameEvent;

public sealed record ActorDiedEvent(int MapInstanceId, Actor Victim, Actor? Killer) : IGameEvent;

/// <summary>Jugador reaparecido (posición nueva, vida/recurso restaurados).</summary>
public sealed record RespawnedEvent(int MapInstanceId, Player Player) : IGameEvent;

/// <summary>Cooldown iniciado (hechizo) o GCD iniciado (SpellId null) para el lanzador.</summary>
public sealed record CooldownEvent(int MapInstanceId, Actor Caster, string? SpellId, int? RemainingMs, int? GcdMs) : IGameEvent;

/// <summary>El lanzador se desplazó por habilidad (dash/leap): el cliente no lo predice (ADR-016).</summary>
public sealed record ForcedMoveEvent(int MapInstanceId, Actor Actor, Vec2 From, Vec2 To) : IGameEvent;

/// <summary>Error de combate hacia un jugador (códigos de docs/protocol.md), emitido por sistemas que no tienen el contexto de red.</summary>
public sealed record CombatErrorEvent(int MapInstanceId, Player Player, string Code, string? Message, int? ReqId) : IGameEvent;
