using PixelRealms.Game.Core;
using PixelRealms.Game.Items;

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

    public Guid AccountId { get; init; }

    public Inventory Inventory { get; } = new();

    public Equipment Equipment { get; } = new();

    /// <summary>Barra 4+4 (ADR-014): índice 0–3 hechizos, 4–7 utilizables; null = casilla vacía. (kind, ref).</summary>
    public (string Kind, string Ref)?[] Hotbar { get; } = new (string, string)?[8];

    public List<string> KnownSpells { get; } = new();

    /// <summary>Marca de "hay cambios sin guardar" para el autosave (HU-026).</summary>
    public bool Dirty { get; set; }

    /// <summary>Instante (ms de reloj de juego) del último guardado encolado; el autosave cuenta desde aquí.</summary>
    public long LastSaveAtMs { get; set; }

    /// <summary>Id de conexión que controla a este jugador; −1 si está linkdead (HU-025).</summary>
    public int ConnectionId { get; set; } = -1;

    /// <summary>Instante (ms de reloj de juego) en que perdió la conexión; −1 si está conectado (HU-025).</summary>
    public long LinkdeadSinceMs { get; set; } = -1;

    public bool IsLinkdead => LinkdeadSinceMs >= 0;

    /// <summary>`UsePortal{portalId}` pendiente de resolver en el tick (HU-027).</summary>
    public string? RequestedPortalId { get; set; }

    /// <summary>Portal cuyo rechazo ya se notificó; se limpia al salir de su rectángulo (HU-027, evita spam de Error).</summary>
    public string? RejectedPortalId { get; set; }
}
