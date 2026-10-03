using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Map;

/// <summary>Cambio de estado de un objeto del mapa (palanca `on`/`off`, puerta `open`/`closed`); se avisa a todo el mapa.</summary>
public sealed record MapObjectChangedEvent(int MapInstanceId, string ObjectId, string State) : IGameEvent;

/// <summary>
/// HU-083 CA1: palancas y puertas de las capas `levers`/`doors`. Tirar de una palanca (a ≤ `rules.world.interactRangeTiles`) la
/// deja activada; cuando todas las de una puerta lo están (o al tirar de una `opensAlone`, la de dentro de la sala), la puerta se
/// abre durante `rules.world.doorResetSec`, y tirar de cualquiera de sus palancas con la puerta abierta renueva el plazo. Al
/// cumplirse se cierra (si hay alguien debajo o saltando a través, espera: nadie se queda en la pared) y sus palancas vuelven.
/// El estado es de la instancia (lo comparten todos los que están en ella) y no se guarda. Va justo después del movimiento.
/// </summary>
public sealed class MapObjectSystem : IMapSystem
{
    public const string On = "on", Off = "off", Open = "open", Closed = "closed";

    public string Name => "map_objects";

    /// <summary>`Interact` sobre una palanca: código de error o null (también si ya estaba activada).</summary>
    public string? Pull(Player player, string leverId, MapInstance map, TickContext ctx)
    {
        if (player.IsDead) return "is_dead";
        LeverDef? lever = null;
        foreach (var l in map.Data.Levers) if (l.LeverId == leverId) { lever = l; break; }
        if (lever is null) return "not_found";
        if (Vec2.Distance(player.Position, lever.Position) > ctx.Rules.World.InteractRangeTiles) return "out_of_range";
        var openUntil = ctx.NowMs + (long)(ctx.Rules.World.DoorResetSec * 1000);
        if (map.LeversOn.Add(lever.LeverId)) ctx.Emit(new MapObjectChangedEvent(map.Id, lever.LeverId, On));
        if (map.DoorsOpenUntil.ContainsKey(lever.DoorId))
        {
            map.DoorsOpenUntil[lever.DoorId] = openUntil; // abierta: la palanca renueva el plazo (un grupo que entra tarde no se queda dentro)
            return null;
        }
        if (!lever.OpensAlone)
            foreach (var l in map.Data.Levers)
                if (l.DoorId == lever.DoorId && !l.OpensAlone && !map.LeversOn.Contains(l.LeverId)) return null;
        foreach (var door in map.Data.Doors)
        {
            if (door.DoorId != lever.DoorId) continue;
            map.SetDoorTiles(door, closed: false);
            map.DoorsOpenUntil[door.DoorId] = openUntil;
            ctx.Emit(new MapObjectChangedEvent(map.Id, door.DoorId, Open));
        }
        return null;
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        if (map.DoorsOpenUntil.Count == 0) return;
        foreach (var door in map.Data.Doors)
        {
            if (!map.DoorsOpenUntil.TryGetValue(door.DoorId, out var until) || ctx.NowMs < until || Occupied(map, door)) continue;
            map.DoorsOpenUntil.Remove(door.DoorId);
            map.SetDoorTiles(door, closed: true);
            ctx.Emit(new MapObjectChangedEvent(map.Id, door.DoorId, Closed));
            foreach (var l in map.Data.Levers)
                if (l.DoorId == door.DoorId && map.LeversOn.Remove(l.LeverId)) ctx.Emit(new MapObjectChangedEvent(map.Id, l.LeverId, Off));
        }
    }

    /// <summary>Estado de cada objeto del mapa, para quien entra en él (`MapObjects` tras `Welcome` o `ChangeMap`).</summary>
    public static List<(string Id, string State)> States(MapInstance map)
    {
        var states = new List<(string, string)>(map.Data.Levers.Count + map.Data.Doors.Count);
        foreach (var l in map.Data.Levers) states.Add((l.LeverId, map.LeversOn.Contains(l.LeverId) ? On : Off));
        foreach (var d in map.Data.Doors) states.Add((d.DoorId, map.DoorsOpenUntil.ContainsKey(d.DoorId) ? Open : Closed));
        return states;
    }

    /// <summary>¿Hay alguien vivo con los pies dentro de la puerta o pegado a ella (media casilla), o saltando por encima (el
    /// salto validó su recta al despegar, con la puerta abierta)?</summary>
    private static bool Occupied(MapInstance map, DoorDef door)
    {
        var (x0, y0, x1, y1) = door.Tiles;
        var (minX, maxX, minY, maxY) = (x0 - 0.5f, x1 + 1.5f, y0 - 0.5f, y1 + 1.5f);
        foreach (var a in map.Actors.Values)
        {
            if (!a.IsAlive) continue;
            if (a.Position.X >= minX && a.Position.X < maxX && a.Position.Y >= minY && a.Position.Y < maxY) return true;
            if (a.Combat.Flight is { } f && MathF.Max(f.From.X, f.To.X) >= minX && MathF.Min(f.From.X, f.To.X) < maxX
                && MathF.Max(f.From.Y, f.To.Y) >= minY && MathF.Min(f.From.Y, f.To.Y) < maxY) return true;
        }
        return false;
    }
}
