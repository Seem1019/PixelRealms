using PixelRealms.Content.Defs;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Core;

/// <summary>Sistema que se ejecuta una vez por tick y por instancia de mapa (pasos 3–9 de docs/architecture.md §3).</summary>
public interface IMapSystem
{
    string Name { get; }

    void Tick(MapInstance map, TickContext ctx);
}

/// <summary>
/// Un tick completo del mundo, sin hilos ni IO: avanza el reloj, ejecuta los ganchos previos (drenar la red, aplicar comandos),
/// recorre cada <see cref="MapInstance"/> con la lista explícita de sistemas y ejecuta los ganchos posteriores (salida,
/// persistencia). Los sistemas se registran en una lista, no por reflexión (HU-004 notas técnicas).
/// </summary>
public sealed class Simulation
{
    private readonly List<IMapSystem> _systems = new();
    private readonly List<Action<TickContext>> _preTick = new();
    private readonly List<Action<TickContext>> _postTick = new();

    public Simulation(World world, IRules rules, IRng rng, TickClock clock)
    {
        World = world;
        Clock = clock;
        Context = new TickContext(rules, rng);
    }

    public World World { get; }

    public TickClock Clock { get; }

    public TickContext Context { get; }

    public long Tick { get; private set; }

    public IReadOnlyList<IMapSystem> Systems => _systems;

    public Simulation AddSystem(IMapSystem system) { _systems.Add(system); return this; }

    public Simulation OnPreTick(Action<TickContext> hook) { _preTick.Add(hook); return this; }

    public Simulation OnPostTick(Action<TickContext> hook) { _postTick.Add(hook); return this; }

    public void RunTick()
    {
        Tick++;
        Clock.Advance(GameConstants.TickMs);
        Context.BeginTick(Tick, Clock.NowMs);
        foreach (var hook in _preTick) hook(Context);
        var instances = World.Instances;
        foreach (var system in _systems)
            for (var i = 0; i < instances.Count; i++)
                system.Tick(instances[i], Context);
        foreach (var hook in _postTick) hook(Context);
    }

    public int EntityCount()
    {
        var n = 0;
        foreach (var inst in World.Instances) n += inst.Actors.Count;
        return n;
    }
}
