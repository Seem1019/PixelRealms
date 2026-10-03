using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Map;

/// <summary>
/// Estado vivo de una copia de un mapa (ADR-007): jugadores, monstruos, botín, amenaza, AOI. El id de instancia es propio y
/// distinto del mapId (el cliente solo conoce mapId). MVP: una instancia por mapa.
/// </summary>
public sealed class MapInstance(int id, MapData data)
{
    private readonly Dictionary<int, Player> _players = new();
    private readonly Dictionary<int, Monster> _monsters = new();
    private readonly Dictionary<int, Actor> _actors = new();

    public int Id { get; } = id;

    public MapData Data { get; } = data;

    public string MapId => Data.MapId;

    public EntityTable<Player> Players => new(_players);

    public EntityTable<Monster> Monsters => new(_monsters);

    public EntityTable<Actor> Actors => new(_actors);

    public void Add(Actor actor)
    {
        _actors[actor.Id.Value] = actor;
        actor.MapInstanceId = Id;
        switch (actor)
        {
            case Player p: _players[p.Id.Value] = p; break;
            case Monster m: _monsters[m.Id.Value] = m; break;
            default: break;
        }
    }

    public bool Remove(EntityId id)
    {
        if (!_actors.Remove(id.Value, out var actor)) return false;
        _players.Remove(id.Value);
        _monsters.Remove(id.Value);
        actor.MapInstanceId = -1;
        return true;
    }

    public Actor? Find(EntityId id) => _actors.GetValueOrDefault(id.Value);
}

/// <summary>
/// Vista de solo lectura de las entidades de una instancia. Es un struct sobre el diccionario: recorrer `Values` usa el enumerador
/// struct de <see cref="Dictionary{TKey,TValue}.ValueCollection"/> y no asigna (con IReadOnlyDictionary cada foreach creaba uno
/// en el heap; HU-088 CA1). Nadie de fuera puede añadir ni quitar: solo <see cref="MapInstance.Add"/> y <see cref="MapInstance.Remove"/>.
/// </summary>
public readonly struct EntityTable<T>(Dictionary<int, T> inner) where T : Actor
{
    public Dictionary<int, T>.ValueCollection Values => inner.Values;

    public int Count => inner.Count;

    public T this[int id] => inner[id];

    public bool ContainsKey(int id) => inner.ContainsKey(id);

    public bool TryGetValue(int id, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) => inner.TryGetValue(id, out value);
}
