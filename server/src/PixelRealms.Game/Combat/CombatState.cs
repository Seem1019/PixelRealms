using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Progression;

namespace PixelRealms.Game.Combat;

/// <summary>Casteo en curso (skill combat-system §Pipeline). El punto/objetivo quedan fijos al empezar (ADR-015/016).</summary>
public sealed class CastState(SpellDef spell, long startedAtMs, long endsAtMs, EntityId? targetId, Vec2? targetPos, Vec2 origin)
{
    public SpellDef Spell { get; } = spell;

    public long StartedAtMs { get; } = startedAtMs;

    public long EndsAtMs { get; } = endsAtMs;

    public EntityId? TargetId { get; } = targetId;

    public Vec2? TargetPos { get; } = targetPos;

    /// <summary>Posición del lanzador al empezar (origen fijo de áreas alrededor del lanzador).</summary>
    public Vec2 Origin { get; } = origin;
}

/// <summary>Recuperación del perdedor de un duelo (HU-064 CA3): regenera vida × `Mult` desde `FromMs`, sin esperar
/// `hpRegenDelaySec`, hasta `UntilHp` (la vida con que empezó el duelo: solo devuelve lo que el duelo quitó) o hasta volver a
/// entrar en combate.</summary>
public readonly record struct PostDuelRecovery(long FromMs, double Mult, int UntilHp);

/// <summary>Salto en el aire (HU-087 CA1): de `From` a `To` en `travelMs`; los efectos se resuelven al aterrizar.</summary>
public sealed record LeapFlight(SpellDef Spell, Vec2 From, Vec2 To, long StartMs, long EndMs);

/// <summary>
/// Estado de combate de un actor: objetivo, ataque básico, casteo, GCD y cooldowns, bloqueos e inmunidades, acumuladores de
/// regeneración. Lo mutan solo los sistemas del tick. Nada de aquí se guarda en BD (ADR-018).
/// </summary>
public sealed class CombatState
{
    /// <summary>Objetivo seleccionado (HU-030); null si no hay.</summary>
    public EntityId? TargetId { get; set; }

    public bool AutoAttackOn { get; set; }

    /// <summary>Progreso del temporizador del básico en ms; solo avanza en rango, sin castear y sin control (HU-032 CA2/CA6).</summary>
    public double SwingProgressMs { get; set; }

    public CastState? Cast { get; set; }

    /// <summary>Salto en curso; mientras dura no se mueve por input ni actúa (HU-087 CA1).</summary>
    public LeapFlight? Flight { get; set; }

    /// <summary>Perdió un duelo y se está recuperando (ResourceSystem).</summary>
    public PostDuelRecovery? Recovery { get; set; }

    public long GcdEndsAtMs { get; set; } = long.MinValue;

    /// <summary>Fin del cooldown por hechizo.</summary>
    public Dictionary<string, long> CooldownEndsAtMs { get; } = new(StringComparer.Ordinal);

    /// <summary>Bloqueo tras interrupción (`interruptLockoutMs`): no se puede castear.</summary>
    public long LockoutEndsAtMs { get; set; } = long.MinValue;

    /// <summary>Bloqueo tras un instantáneo (`abilityLockMs`): ni básico ni otro hechizo (HU-032 CA7).</summary>
    public long AbilityLockEndsAtMs { get; set; } = long.MinValue;

    /// <summary>Inmunidad a stun/root/silence tras terminar un control fuerte (ADR-022).</summary>
    public long HardControlImmuneUntilMs { get; set; } = long.MinValue;

    /// <summary>Hasta cuándo la regeneración de maná va penalizada tras gastar maná (HU-039 CA1).</summary>
    public long ManaPenaltyUntilMs { get; set; } = long.MinValue;

    /// <summary>Fracciones acumuladas de regeneración (vida, recurso) para que los enteros por tick no pierdan decimales.</summary>
    public double HpRegenAcc { get; set; }

    public double ResourceRegenAcc { get; set; }

    /// <summary>Instante de la muerte (para cadáver y respawn); long.MinValue si vive.</summary>
    public long DiedAtMs { get; set; } = long.MinValue;

    /// <summary>Quién lo mató (XP y marcos); null si no aplica.</summary>
    public EntityId? KilledBy { get; set; }

    /// <summary>Monstruo volviendo a su spawn: inmune y sin amenaza (HU-036 CA4).</summary>
    public bool Evading { get; set; }

    /// <summary>Stats derivados cacheados; null = recalcular (equipo, nivel o auras cambiaron).</summary>
    public DerivedStats? Stats { get; set; }

    public bool IsCasting => Cast is not null;

    public bool IsOnGcd(long nowMs) => nowMs < GcdEndsAtMs;

    public bool IsOnCooldown(string spellId, long nowMs) => CooldownEndsAtMs.TryGetValue(spellId, out var end) && nowMs < end;

    public int CooldownRemainingMs(string spellId, long nowMs) => CooldownEndsAtMs.TryGetValue(spellId, out var end) ? (int)Math.Max(0, end - nowMs) : 0;

    public bool IsLockedOut(long nowMs) => nowMs < LockoutEndsAtMs;

    public bool IsAbilityLocked(long nowMs) => nowMs < AbilityLockEndsAtMs;

    public bool IsHardControlImmune(long nowMs) => nowMs < HardControlImmuneUntilMs;

    /// <summary>Al morir o evadir: se limpia todo lo transitorio salvo cooldowns (HU-037 CA1).</summary>
    public void ResetTransient()
    {
        TargetId = null;
        AutoAttackOn = false;
        SwingProgressMs = 0;
        Cast = null;
        Flight = null;
        Recovery = null;
    }
}
