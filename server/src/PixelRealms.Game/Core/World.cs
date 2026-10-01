using PixelRealms.Game.Map;

namespace PixelRealms.Game.Core;

/// <summary>El mundo: colección de <see cref="MapInstance"/> activas sobre <see cref="MapData"/> compartidos (ADR-007). Solo lo muta el tick.</summary>
public sealed class World
{
    private readonly Dictionary<string, MapData> _maps = new(StringComparer.Ordinal);
    private readonly Dictionary<int, MapInstance> _instances = new();
    private readonly List<MapInstance> _instanceList = new();
    private int _nextInstanceId = 1;

    public EntityIdAllocator EntityIds { get; } = new();

    public IReadOnlyDictionary<string, MapData> Maps => _maps;

    /// <summary>Instancias en orden de creación; el GameLoop las recorre todas cada tick.</summary>
    public IReadOnlyList<MapInstance> Instances => _instanceList;

    public void RegisterMap(MapData data) => _maps[data.MapId] = data;

    public MapInstance CreateInstance(string mapId)
    {
        var data = _maps.TryGetValue(mapId, out var d) ? d : throw new KeyNotFoundException($"No existe el mapa '{mapId}'");
        var inst = new MapInstance(_nextInstanceId++, data);
        _instances[inst.Id] = inst;
        _instanceList.Add(inst);
        return inst;
    }

    public MapInstance? GetInstance(int id) => _instances.GetValueOrDefault(id);

    /// <summary>MVP: la única instancia de un mapa (ADR-007). Con instancias por grupo habrá que elegir.</summary>
    public MapInstance? InstanceOf(string mapId) => _instanceList.FirstOrDefault(i => i.MapId == mapId);
}
