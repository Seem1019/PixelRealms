using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;

namespace PixelRealms.Server.Hosting;

/// <summary>
/// HU-072 CA3: recuentos, tiempos de combate y memoria asignada por segundo por instancia para `/admin/stats`. Los calcula el tick (regla 2: el hilo HTTP no
/// recorre el mundo ni las estadísticas de la simulación, que mutan a la vez) y los publica como copia inmutable una vez por segundo.
/// </summary>
public sealed class WorldStats(World world, CombatModule combat, Simulation simulation)
{
    public sealed record InstanceStats(int Id, string MapId, int Players, int Monsters, int AreasActive, int ProjectilesInFlight, int AurasActive,
        double CombatP50Ms, double CombatP99Ms, double AllocBytesPerSec);

    public const int EveryTicks = 20;

    private volatile IReadOnlyList<InstanceStats> _latest = [];

    /// <summary>Bytes acumulados por instancia en la publicación anterior y cuándo fue: la tasa es la diferencia entre dos.</summary>
    private readonly Dictionary<int, long> _allocsAtLast = new();

    private long _lastAtMs = long.MinValue;

    public IReadOnlyList<InstanceStats> Latest => _latest;

    public void OnPostTick(TickContext ctx)
    {
        if (ctx.Tick % EveryTicks != 0) return;
        var elapsedSec = _lastAtMs == long.MinValue ? 0 : (ctx.NowMs - _lastAtMs) / 1000.0;
        _lastAtMs = ctx.NowMs;
        var list = new List<InstanceStats>(world.Instances.Count);
        foreach (var inst in world.Instances)
        {
            var auras = 0;
            foreach (var a in inst.Actors.Values) auras += a.Auras.Count;
            var (cp50, cp99) = simulation.CombatTimings is { } ct && ct.TryGetValue(inst.Id, out var cs) ? cs.Percentiles() : (0, 0);
            var allocs = simulation.InstanceAllocs?.GetValueOrDefault(inst.Id) ?? 0;
            var allocRate = elapsedSec > 0 ? (allocs - _allocsAtLast.GetValueOrDefault(inst.Id)) / elapsedSec : 0;
            _allocsAtLast[inst.Id] = allocs;
            list.Add(new InstanceStats(inst.Id, inst.MapId, inst.Players.Count, inst.Monsters.Count, combat.Casts.ActiveAreas(inst), combat.Casts.PendingImpacts(inst), auras,
                cp50, cp99, allocRate));
        }
        _latest = list;
    }
}
