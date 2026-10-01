using PixelRealms.Game.Core;

namespace PixelRealms.Game.Entities;

/// <summary>Jugador en el mundo. La cuenta y el id de BD viven fuera del dominio (los guarda el servidor en Guid).</summary>
public sealed class Player(EntityId id, string name, string classId) : Actor(id, name)
{
    public override ActorKind Kind => ActorKind.Player;

    public Guid CharacterId { get; init; }

    public string ClassId { get; set; } = classId;

    public int Xp { get; set; }

    public long Gold { get; set; }

    public int Resource { get; set; }

    public int MaxResource { get; set; }

    /// <summary>Último input de movimiento (dx, dy) y su seq (HU-021).</summary>
    public int MoveDx { get; set; }

    public int MoveDy { get; set; }

    public int LastInputSeq { get; set; }

    public long LastInputAtMs { get; set; }

    /// <summary>Marca de "hay cambios sin guardar" para el autosave (HU-026).</summary>
    public bool Dirty { get; set; }
}
