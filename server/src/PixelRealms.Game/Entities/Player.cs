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

    /// <summary>Oro en cobre (vive en la bolsa; aquí solo como acceso directo).</summary>
    public long Gold { get => Inventory.Gold; set => Inventory.Gold = value; }

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

    /// <summary>HU-104: mejora elegida por hechizo (spellId → upgradeId); se guarda con el personaje.</summary>
    public Dictionary<string, string> SpellUpgrades { get; } = new(StringComparer.Ordinal);

    /// <summary>Marca de "hay cambios sin guardar" para el autosave (HU-026).</summary>
    public bool Dirty { get; set; }

    /// <summary>
    /// Compañero de un intercambio completado que aún no se ha guardado (HU-059): el siguiente guardado de cualquiera de los dos
    /// (el del intercambio, un cambio de mapa, el autosave o la salida del mismo tick) los escribe juntos, en una transacción.
    /// </summary>
    public Player? TradeSavePartner { get; set; }

    /// <summary>
    /// Objetos guardados que no se pueden mostrar: plantilla que ya no existe o casilla repetida sin hueco en la bolsa (HU-057 CA4).
    /// No se ven ni se usan, pero se vuelven a guardar tal cual (contenedor 2) para no borrarlos; al entrar, lo que vuelva a ser
    /// válido pasa a la bolsa si hay hueco.
    /// </summary>
    public List<Items.ItemInstance> Unplaced { get; } = new();

    /// <summary>Entradas de auditoría de items pendientes de guardar en lote (HU-057 CA3).</summary>
    public List<PendingAudit> PendingAudit { get; } = new();

    public void Audit(PendingAudit entry)
    {
        PendingAudit.Add(entry);
        Dirty = true;
    }

    /// <summary>Última acción de combate (daño hecho/recibido, cura o habilidad) para la XP de grupo (HU-062: activos en 90 s).</summary>
    public long LastActionAtMs { get; set; } = long.MinValue;

    /// <summary>HU-070 `/god`: no recibe daño (sesión, no se persiste).</summary>
    public bool GodMode { get; set; }

    /// <summary>HU-070 `/debug move on|off`: el servidor traza cada MoveInput de este jugador (sesión).</summary>
    public bool DebugMove { get; set; }

    public bool IsActive(long nowMs, double windowSec) => Math.Max(LastActionAtMs, LastCombatAtMs) != long.MinValue && nowMs - Math.Max(LastActionAtMs, LastCombatAtMs) <= windowSec * 1000;

    /// <summary>Cooldown compartido por plantilla de consumible (HU-054): templateId → fin en ms.</summary>
    public Dictionary<string, long> ItemCooldownEndsAtMs { get; } = new(StringComparer.Ordinal);

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
