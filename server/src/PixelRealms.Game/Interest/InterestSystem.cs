using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Interest;

/// <summary>Una entidad entró en la vista de un jugador (hay que enviarle EntitySpawn).</summary>
public sealed record EntityEnteredView(int MapInstanceId, Player Observer, Actor Entity) : IGameEvent;

/// <summary>Una entidad salió de la vista de un jugador (EntityDespawn con motivo).</summary>
public sealed record EntityLeftView(int MapInstanceId, Player Observer, EntityId EntityId, string Reason) : IGameEvent;

/// <summary>
/// Paso 9 del tick (HU-023): rejilla AOI de celdas de `rules.movement.aoiCellTiles` casillas; un jugador ve su celda y las 8
/// vecinas. Mantiene por jugador el conjunto de entidades visibles y emite las diferencias (spawn/despawn por observador).
/// La rejilla la reutilizan las áreas de combate (ADR-018).
/// </summary>
public sealed class InterestSystem : IMapSystem
{
    private readonly Dictionary<(int Map, int Player), HashSet<int>> _visible = new();
    private readonly Dictionary<(int, int), List<Actor>> _cells = new();
    private readonly HashSet<int> _scratch = new();
    private readonly List<int> _gone = new();
    private readonly List<(int Map, int Player)> _staleKeys = new();

    public string Name => "interest";

    public const string ReasonLeft = "left";
    public const string ReasonDied = "died";
    public const string ReasonDespawn = "despawn";

    /// <summary>Entidades visibles ahora por un jugador (ids), o vacío.</summary>
    public IReadOnlySet<int> VisibleTo(MapInstance map, Player p) => _visible.TryGetValue((map.Id, p.Id.Value), out var s) ? s : new HashSet<int>();

    public static (int X, int Y) CellOf(Vec2 pos, int cellTiles) => ((int)MathF.Floor(pos.X / cellTiles), (int)MathF.Floor(pos.Y / cellTiles));

    public void Tick(MapInstance map, TickContext ctx)
    {
        var cellTiles = ctx.Rules.Movement.AoiCellTiles;
        // Las listas por celda se reutilizan entre ticks (HU-088 CA1: sin asignar por tick); se vacían en vez de recrearse.
        foreach (var list in _cells.Values) list.Clear();
        foreach (var actor in map.Actors.Values)
        {
            var cell = CellOf(actor.Position, cellTiles);
            if (!_cells.TryGetValue(cell, out var list)) _cells[cell] = list = new List<Actor>(8);
            list.Add(actor);
        }

        foreach (var player in map.Players.Values)
        {
            var key = (map.Id, player.Id.Value);
            if (!_visible.TryGetValue(key, out var seen)) _visible[key] = seen = new HashSet<int>();
            _scratch.Clear();
            var (cx, cy) = CellOf(player.Position, cellTiles);
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if (_cells.TryGetValue((cx + dx, cy + dy), out var list))
                        foreach (var a in list)
                            if (a.Id != player.Id) _scratch.Add(a.Id.Value);

            foreach (var id in _scratch)
                if (seen.Add(id)) ctx.Emit(new EntityEnteredView(map.Id, player, map.Actors[id]));
            if (seen.Count > _scratch.Count || !seen.SetEquals(_scratch))
            {
                _gone.Clear();
                foreach (var id in seen) if (!_scratch.Contains(id)) _gone.Add(id);
                foreach (var id in _gone)
                {
                    seen.Remove(id);
                    var reason = map.Actors.TryGetValue(id, out var a) ? (a.IsDead && a is Player ? ReasonDied : ReasonLeft) : ReasonLeft;
                    ctx.Emit(new EntityLeftView(map.Id, player, new EntityId(id), reason));
                }
            }
        }

        // Jugadores que ya no están en la instancia: olvidar su conjunto.
        if (_visible.Count > map.Players.Count)
        {
            _staleKeys.Clear();
            foreach (var k in _visible.Keys) if (k.Map == map.Id && !map.Players.ContainsKey(k.Player)) _staleKeys.Add(k);
            foreach (var key in _staleKeys) _visible.Remove(key);
        }
    }

    /// <summary>Reconexión (HU-025 CA2): olvida lo que veía el jugador para que el siguiente tick reenvíe todos los EntitySpawn.</summary>
    public void ResetObserver(MapInstance map, Player p) => _visible.Remove((map.Id, p.Id.Value));

    /// <summary>Despawn explícito (muerte de monstruo, salida de mapa) para quienes lo veían, con el motivo dado.</summary>
    public void ForgetEntity(MapInstance map, EntityId id, string reason, TickContext ctx)
    {
        foreach (var player in map.Players.Values)
            if (_visible.TryGetValue((map.Id, player.Id.Value), out var seen) && seen.Remove(id.Value))
                ctx.Emit(new EntityLeftView(map.Id, player, id, reason));
    }

    /// <summary>Jugadores que ven a una entidad (para eventos de combate: solo a quien ve al atacante o al objetivo).</summary>
    public IEnumerable<Player> ObserversOf(MapInstance map, EntityId id)
    {
        foreach (var player in map.Players.Values)
            if (player.Id == id || (_visible.TryGetValue((map.Id, player.Id.Value), out var seen) && seen.Contains(id.Value)))
                yield return player;
    }
}
