using System.Text.Json;
using PixelRealms.Game.Core;

namespace PixelRealms.Game.Map;

/// <summary>El mapa no cumple las convenciones de la skill world-maps: el servidor no arranca (HU-020 CA2).</summary>
public sealed class MapLoadException(string mapPath, IReadOnlyList<string> errors)
    : Exception($"Mapa inválido '{mapPath}':\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Lee un `.tmj` de Tiled (JSON, capas CSV sin compresión) y construye un <see cref="MapData"/> (skill world-maps §Servidor).
/// Solo lee los campos necesarios; los GIDs se enmascaran con 0x1FFFFFFF para ignorar los flags de flip.
/// </summary>
public static class TiledMapLoader
{
    private const uint GidMask = 0x1FFFFFFF;

    /// <summary>Validación dependiente del contenido: existe el monstruo / vendedor referenciado.</summary>
    public sealed record ContentCheck(Func<string, bool> MonsterExists, Func<string, bool> VendorExists);

    public static MapData Load(string tmjPath, ContentCheck content, Func<string, bool>? mapExists = null)
    {
        var errors = new List<string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(tmjPath));
        var root = doc.RootElement;
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        var props = ReadProperties(root);
        var mapId = props.GetValueOrDefault("mapId") as string ?? Path.GetFileNameWithoutExtension(tmjPath);
        var displayName = props.GetValueOrDefault("displayName") as string ?? mapId;
        var defaultGraveyard = props.GetValueOrDefault("defaultGraveyard") as string ?? "";

        var tileProps = LoadTileProperties(root, Path.GetDirectoryName(tmjPath) ?? ".");
        var grid = new CollisionGrid(width, height);
        var spawns = new List<SpawnDef>();
        var npcs = new List<NpcDef>();
        var graveyards = new List<GraveyardDef>();
        var zones = new List<ZoneDef>();
        var portals = new List<PortalDef>();
        var levers = new List<LeverDef>();
        var doors = new List<DoorDef>();

        foreach (var layer in root.GetProperty("layers").EnumerateArray())
        {
            var name = layer.GetProperty("name").GetString() ?? "";
            var type = layer.GetProperty("type").GetString();
            if (type == "tilelayer")
            {
                if (name is not ("walls" or "collision")) continue;
                if (!layer.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    errors.Add($"capa '{name}': la capa debe estar en CSV sin compresión (data como array)");
                    continue;
                }
                var i = 0;
                foreach (var cell in data.EnumerateArray())
                {
                    var gid = (uint)cell.GetInt64() & GidMask;
                    if (gid != 0 && tileProps.TryGetValue(gid, out var tp))
                    {
                        var x = i % width; var y = i / width;
                        if (tp.Solid) grid.SetSolid(x, y);
                        if (tp.BlocksSight) grid.SetBlocksSight(x, y);
                    }
                    i++;
                }
            }
            else if (type == "objectgroup")
            {
                foreach (var o in layer.GetProperty("objects").EnumerateArray())
                {
                    var op = ReadProperties(o);
                    var pos = new Vec2((float)o.GetProperty("x").GetDouble() / GameConstants.PixelsPerTile, (float)o.GetProperty("y").GetDouble() / GameConstants.PixelsPerTile);
                    var size = new Vec2((float)Num(o, "width") / GameConstants.PixelsPerTile, (float)Num(o, "height") / GameConstants.PixelsPerTile);
                    var oname = o.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    switch (name)
                    {
                        case "spawns":
                        {
                            var monsterId = op.GetValueOrDefault("monsterId") as string ?? "";
                            var count = Math.Clamp(ToInt(op.GetValueOrDefault("count"), 1), 1, 10);
                            var wander = ToFloat(op.GetValueOrDefault("wanderRadius"), 2f);
                            if (!content.MonsterExists(monsterId)) errors.Add($"spawn '{oname}': monsterId '{monsterId}' no existe en monsters.json");
                            spawns.Add(new SpawnDef(oname, monsterId, count, wander, pos, size));
                            break;
                        }
                        case "npcs":
                            npcs.Add(new NpcDef(oname, op.GetValueOrDefault("name") as string ?? oname, op.GetValueOrDefault("vendorId") as string, op.GetValueOrDefault("kind") as string, pos));
                            if (op.GetValueOrDefault("vendorId") is string v && !content.VendorExists(v)) errors.Add($"npc '{oname}': vendorId '{v}' no existe en vendors.json");
                            break;
                        case "graveyards":
                            graveyards.Add(new GraveyardDef(oname, pos));
                            break;
                        case "zones":
                            zones.Add(new ZoneDef(oname, op.GetValueOrDefault("name") as string ?? oname, op.GetValueOrDefault("safe") is true, ToInt(op.GetValueOrDefault("minLevel"), 1), ToInt(op.GetValueOrDefault("maxLevel"), 99), op.GetValueOrDefault("landmark") as string, pos, size));
                            break;
                        case "portals":
                        {
                            var target = op.GetValueOrDefault("targetMapId") as string ?? "";
                            var minLevel = op.TryGetValue("minLevel", out var ml) ? ToInt(ml, 0) : (int?)null;
                            var minPhase = op.TryGetValue("minPhase", out var mp) ? ToInt(mp, 0) : (int?)null;
                            // Mal escrito (texto, 0) dejaría abierto el paso que separa los tiers: no arranca.
                            if (minPhase is not null && (mp is not double d || d != Math.Floor(d) || d < 1))
                                errors.Add($"portal '{oname}': minPhase debe ser un entero ≥ 1 (es '{mp}')");
                            var lockedText = op.GetValueOrDefault("lockedText") is string { Length: > 0 } lt ? lt : null;
                            portals.Add(new PortalDef(op.GetValueOrDefault("portalId") as string ?? oname, target, ToFloat(op.GetValueOrDefault("targetX"), 0), ToFloat(op.GetValueOrDefault("targetY"), 0), minLevel, pos, size, minPhase, lockedText));
                            // HU-112: un portal con `minPhase` puede llevar a un mapa que aún no existe (la salida a un tier de una fase
                            // futura): queda cerrado y el servidor lo avisa al arrancar (PortalsToMissingMaps).
                            if (mapExists is not null && !mapExists(target) && minPhase is null) errors.Add($"portal '{oname}': targetMapId '{target}' no existe en maps/ (solo se admite con minPhase)");
                            break;
                        }
                        case "levers":
                            levers.Add(new LeverDef(op.GetValueOrDefault("leverId") as string ?? oname, op.GetValueOrDefault("doorId") as string ?? "", pos, op.GetValueOrDefault("opensAlone") is true));
                            break;
                        case "doors":
                            doors.Add(new DoorDef(op.GetValueOrDefault("doorId") as string ?? oname, pos, size));
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        // HU-083: ids únicos (entre palancas y puertas también: comparten el espacio de ids de `Interact` y `MapObjects`); cada
        // palanca abre una puerta que existe; cada puerta tiene al menos una palanca y un rectángulo dentro del mapa.
        var objectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in levers.Select(l => l.LeverId).Concat(doors.Select(d => d.DoorId)))
            if (!objectIds.Add(id)) errors.Add($"id de palanca o puerta repetido: '{id}'");
        foreach (var l in levers)
        {
            if (!doors.Exists(d => d.DoorId == l.DoorId)) errors.Add($"palanca '{l.LeverId}': doorId '{l.DoorId}' no existe en la capa doors");
            if (grid.IsSolidAt(l.Position.X, l.Position.Y)) errors.Add($"palanca '{l.LeverId}' cae en una casilla sólida");
        }
        foreach (var d in doors)
        {
            if (d.Size.X <= 0 || d.Size.Y <= 0 || !grid.InBounds(d.Tiles.X0, d.Tiles.Y0) || !grid.InBounds(d.Tiles.X1, d.Tiles.Y1))
                errors.Add($"puerta '{d.DoorId}': su rectángulo debe tener tamaño y estar dentro del mapa");
            if (!levers.Exists(l => l.DoorId == d.DoorId)) errors.Add($"puerta '{d.DoorId}' no tiene ninguna palanca");
        }

        if (graveyards.Count == 0) errors.Add("no hay ningún cementerio (capa graveyards)");
        foreach (var s in spawns)
        {
            var (sx, sy) = ((int)MathF.Floor(s.Position.X), (int)MathF.Floor(s.Position.Y));
            if (s.Size.X <= 0 && grid.IsSolid(sx, sy)) errors.Add($"spawn '{s.Id}' cae en una casilla sólida ({sx}, {sy})");
            if (s.Size.X > 0 && !RectHasFreeTile(grid, s)) errors.Add($"spawn '{s.Id}': su rectángulo no tiene ninguna casilla libre");
        }
        foreach (var g in graveyards)
            if (grid.IsSolidAt(g.Position.X, g.Position.Y)) errors.Add($"cementerio '{g.Id}' cae en una casilla sólida");

        if (errors.Count > 0) throw new MapLoadException(tmjPath, errors);
        return new MapData(mapId, displayName, grid, spawns, npcs, graveyards, zones, portals, defaultGraveyard, levers, doors);
    }

    /// <summary>Carga todos los `.tmj` de la carpeta y comprueba los portales entre ellos. Error → el servidor no arranca.</summary>
    public static IReadOnlyList<MapData> LoadAll(string mapsDir, ContentCheck content)
    {
        var files = Directory.GetFiles(mapsDir, "*.tmj").Order().ToList();
        var ids = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.Ordinal);
        return files.Select(f => Load(f, content, ids.Contains)).ToList();
    }

    /// <summary>
    /// Portales cuyo mapa de destino no está entre los cargados (HU-112). El cargador solo los admite con `minPhase`; en juego
    /// están cerrados aunque la fase lo permita, y el servidor los avisa al arrancar.
    /// </summary>
    public static IReadOnlyList<(MapData Map, PortalDef Portal)> PortalsToMissingMaps(IReadOnlyList<MapData> maps)
    {
        var ids = maps.Select(m => m.MapId).ToHashSet(StringComparer.Ordinal);
        return maps.SelectMany(m => m.Portals.Where(p => !ids.Contains(p.TargetMapId)).Select(p => (m, p))).ToList();
    }

    private static bool RectHasFreeTile(CollisionGrid grid, SpawnDef s)
    {
        for (var y = (int)s.Position.Y; y < (int)(s.Position.Y + s.Size.Y); y++)
            for (var x = (int)s.Position.X; x < (int)(s.Position.X + s.Size.X); x++)
                if (!grid.IsSolid(x, y)) return true;
        return false;
    }

    private readonly record struct TileProps(bool Solid, bool BlocksSight);

    private static Dictionary<uint, TileProps> LoadTileProperties(JsonElement root, string mapDir)
    {
        var result = new Dictionary<uint, TileProps>();
        if (!root.TryGetProperty("tilesets", out var tilesets)) return result;
        foreach (var ts in tilesets.EnumerateArray())
        {
            var firstGid = (uint)ts.GetProperty("firstgid").GetInt64();
            JsonDocument? external = null;
            var def = ts;
            if (ts.TryGetProperty("source", out var src))
            {
                var path = Path.GetFullPath(Path.Combine(mapDir, src.GetString()!));
                external = JsonDocument.Parse(File.ReadAllText(path));
                def = external.RootElement;
            }
            if (def.TryGetProperty("tiles", out var tiles))
            {
                foreach (var tile in tiles.EnumerateArray())
                {
                    var p = ReadProperties(tile);
                    result[firstGid + (uint)tile.GetProperty("id").GetInt32()] = new TileProps(p.GetValueOrDefault("solid") is true, p.GetValueOrDefault("blocksSight") is true);
                }
            }
            external?.Dispose();
        }
        return result;
    }

    private static Dictionary<string, object?> ReadProperties(JsonElement el)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!el.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Array) return d;
        foreach (var p in props.EnumerateArray())
        {
            var name = p.GetProperty("name").GetString()!;
            var value = p.GetProperty("value");
            d[name] = value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.String => value.GetString(),
                _ => null,
            };
        }
        return d;
    }

    private static double Num(JsonElement o, string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static int ToInt(object? v, int fallback) => v is double d ? (int)Math.Round(d) : fallback;

    private static float ToFloat(object? v, float fallback) => v is double d ? (float)d : fallback;
}
