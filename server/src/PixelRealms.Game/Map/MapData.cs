using PixelRealms.Game.Core;

namespace PixelRealms.Game.Map;

/// <summary>Spawn de monstruos de la capa `spawns` (skill world-maps). Punto = 1 spawn fijo; rectángulo = `count` posiciones.</summary>
public sealed record SpawnDef(string Id, string MonsterId, int Count, float WanderRadius, Vec2 Position, Vec2 Size);

public sealed record NpcDef(string Id, string Name, string? VendorId, string? Kind, Vec2 Position);

public sealed record GraveyardDef(string Id, Vec2 Position);

public sealed record ZoneDef(string Id, string Name, bool Safe, int MinLevel, int MaxLevel, string? Landmark, Vec2 Position, Vec2 Size)
{
    public bool Contains(Vec2 p) => p.X >= Position.X && p.Y >= Position.Y && p.X < Position.X + Size.X && p.Y < Position.Y + Size.Y;
}

public sealed record PortalDef(string PortalId, string TargetMapId, float TargetX, float TargetY, int? MinLevel, Vec2 Position, Vec2 Size)
{
    public bool Contains(Vec2 p) => p.X >= Position.X && p.Y >= Position.Y && p.X < Position.X + Size.X && p.Y < Position.Y + Size.Y;
}

/// <summary>
/// Datos estáticos de un mapa (ADR-007): colisión, spawns, NPCs, cementerios, zonas y portales. Inmutable y compartido por
/// todas las <see cref="MapInstance"/> del mismo mapa.
/// </summary>
public sealed class MapData
{
    public MapData(string mapId, string displayName, CollisionGrid collision, IReadOnlyList<SpawnDef> spawns, IReadOnlyList<NpcDef> npcs,
        IReadOnlyList<GraveyardDef> graveyards, IReadOnlyList<ZoneDef> zones, IReadOnlyList<PortalDef> portals, string defaultGraveyard)
    {
        MapId = mapId;
        DisplayName = displayName;
        Collision = collision;
        Spawns = spawns;
        Npcs = npcs;
        Graveyards = graveyards;
        Zones = zones;
        Portals = portals;
        if (graveyards.Count == 0) throw new ArgumentException($"El mapa '{mapId}' no tiene ningún cementerio", nameof(graveyards));
        DefaultGraveyard = graveyards.FirstOrDefault(g => g.Id == defaultGraveyard) ?? graveyards[0];
    }

    public string MapId { get; }
    public string DisplayName { get; }
    public CollisionGrid Collision { get; }
    public IReadOnlyList<SpawnDef> Spawns { get; }
    public IReadOnlyList<NpcDef> Npcs { get; }
    public IReadOnlyList<GraveyardDef> Graveyards { get; }
    public IReadOnlyList<ZoneDef> Zones { get; }
    public IReadOnlyList<PortalDef> Portals { get; }
    public GraveyardDef DefaultGraveyard { get; }

    public int Width => Collision.Width;
    public int Height => Collision.Height;

    public GraveyardDef NearestGraveyard(Vec2 p) => Graveyards.MinBy(g => Vec2.DistanceSquared(g.Position, p)) ?? DefaultGraveyard;

    public ZoneDef? ZoneAt(Vec2 p) => Zones.FirstOrDefault(z => z.Contains(p));

    public bool IsSafeZone(Vec2 p) => ZoneAt(p)?.Safe ?? false;
}
