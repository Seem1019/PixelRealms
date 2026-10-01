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
        if (CombatTimings is null)
        {
            foreach (var system in _systems)
                for (var i = 0; i < instances.Count; i++)
                    system.Tick(instances[i], Context);
        }
        else
        {
            // HU-072: tiempo de los sistemas de combate por instancia y tick (p99 en /admin/stats).
            for (var i = 0; i < instances.Count; i++) _combatMsThisTick[instances[i].Id] = 0;
            foreach (var system in _systems)
            {
                var isCombat = CombatSystemNames.Contains(system.Name);
                for (var i = 0; i < instances.Count; i++)
                {
                    if (!isCombat) { system.Tick(instances[i], Context); continue; }
                    var start = System.Diagnostics.Stopwatch.GetTimestamp();
                    system.Tick(instances[i], Context);
                    _combatMsThisTick[instances[i].Id] += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
            }
            for (var i = 0; i < instances.Count; i++)
            {
                if (!CombatTimings.TryGetValue(instances[i].Id, out var stats)) CombatTimings[instances[i].Id] = stats = new TickStats();
                stats.Record(_combatMsThisTick[instances[i].Id]);
            }
        }
        foreach (var hook in _postTick) hook(Context);
    }

    /// <summary>Sistemas cuyo tiempo cuenta como "combate" (docs/architecture.md §8: ≤ 4 ms p99 por instancia).</summary>
    public static readonly HashSet<string> CombatSystemNames = new(StringComparer.Ordinal) { "casts", "auras", "monster_ai", "auto_attack", "resources", "death" };

    private readonly Dictionary<int, double> _combatMsThisTick = new();

    /// <summary>Activa la medición por instancia (null = sin medir, el valor por defecto en tests).</summary>
    public Dictionary<int, TickStats>? CombatTimings { get; set; }

    public int EntityCount()
    {
        var n = 0;
        foreach (var inst in World.Instances) n += inst.Actors.Count;
        return n;
    }
}
