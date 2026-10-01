using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;

namespace PixelRealms.Game.Entities;

public enum ActorKind { Player, Monster, Npc }

/// <summary>Entidad viva del mundo (jugador o monstruo): posición, vida y estado común. Los sistemas la mutan solo en el tick.</summary>
public abstract class Actor(EntityId id, string name)
{
    public EntityId Id { get; } = id;

    public string Name { get; set; } = name;

    public abstract ActorKind Kind { get; }

    public int MapInstanceId { get; internal set; } = -1;

    public Vec2 Position { get; set; }

    /// <summary>Dirección de la vista (8 direcciones) para la animación.</summary>
    public Direction Facing { get; set; } = Direction.S;

    public int Level { get; set; } = 1;

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public bool IsDead => Hp <= 0;

    public bool IsAlive => Hp > 0;

    /// <summary>Último instante (ms de reloj de juego) en que hizo o recibió daño; long.MinValue = nunca.</summary>
    public long LastCombatAtMs { get; set; } = long.MinValue;

    /// <summary>"En combate": hizo o recibió daño en los últimos `rules.combat.inCombatWindowSec` (docs/design/combat.md).</summary>
    public bool IsInCombat(long nowMs, double inCombatWindowSec) => LastCombatAtMs != long.MinValue && nowMs - LastCombatAtMs < (long)(inCombatWindowSec * 1000);

    public void EnterCombat(long nowMs) => LastCombatAtMs = nowMs;

    /// <summary>Objetivo, básico, casteo, cooldowns, bloqueos (M2). Transitorio: no se guarda.</summary>
    public CombatState Combat { get; } = new();

    /// <summary>Auras activas (HU-035).</summary>
    public AuraSet Auras { get; } = new();

    /// <summary>Invalida la caché de stats derivados (equipo, nivel o aura stat_mod cambiaron).</summary>
    public void MarkStatsDirty() => Combat.Stats = null;

    /// <summary>Flags del protocolo (EntitySpawn.flags): bit 1 = muerto, bit 2 = evadiendo.</summary>
    public int Flags => (IsDead ? 2 : 0) | (Combat.Evading ? 4 : 0);

    /// <summary>Velocidad base en casillas/s antes de modificadores (rules.movement.baseSpeedTilesPerSec).</summary>
    public float BaseSpeed { get; set; }

    public override string ToString() => $"{Kind} {Name}#{Id}";
}

/// <summary>Las 8 direcciones del protocolo (Dir): n, ne, e, se, s, sw, w, nw.</summary>
public enum Direction { N, NE, E, SE, S, SW, W, NW }

public static class DirectionExtensions
{
    public static string ToWire(this Direction d) => d switch
    {
        Direction.N => "n", Direction.NE => "ne", Direction.E => "e", Direction.SE => "se",
        Direction.S => "s", Direction.SW => "sw", Direction.W => "w", _ => "nw",
    };

    /// <summary>Dirección a partir de un input (dx, dy ∈ {−1, 0, 1}); sin movimiento devuelve null.</summary>
    public static Direction? FromInput(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => Direction.N, (1, -1) => Direction.NE, (1, 0) => Direction.E, (1, 1) => Direction.SE,
        (0, 1) => Direction.S, (-1, 1) => Direction.SW, (-1, 0) => Direction.W, (-1, -1) => Direction.NW,
        _ => null,
    };
}
