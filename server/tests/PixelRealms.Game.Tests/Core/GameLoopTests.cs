using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Core;

public sealed class GameLoopTests
{
    private sealed class CountingSystem : IMapSystem
    {
        public int Calls { get; private set; }
        public List<long> Ticks { get; } = new();
        public string Name => "counting";
        public void Tick(MapInstance map, TickContext ctx) { Calls++; Ticks.Add(ctx.Tick); }
    }

    [Fact]
    public void Simulation_Advances40Ticks_ClockAndSystems() // HU-004 CA3/CA4
    {
        var w = new WorldBuilder().WithMap().WithPlayer("Ana", "mage", 3, (5, 5)).WithMonster("wolf", (8, 5)).Build();
        var sys = new CountingSystem();
        w.Simulation.AddSystem(sys);
        TickRunner.Run(w, 40);
        w.Simulation.Tick.ShouldBe(40);
        w.Clock.NowMs.ShouldBe(2000);
        sys.Calls.ShouldBe(40);
        sys.Ticks[0].ShouldBe(1);
        w.Simulation.EntityCount().ShouldBe(2);
        w.Player("Ana").Level.ShouldBe(3);
        w.Monster("wolf").Hp.ShouldBe(130);
    }

    [Fact]
    public void Simulation_RunsSystemsPerInstance_InOrder()
    {
        var w = new WorldBuilder().WithMap().Build();
        w.World.RegisterMap(new MapData("mine", "Mina", new CollisionGrid(8, 8), [], [], [new GraveyardDef("g", new Vec2(1, 1))], [], [], "g"));
        w.World.CreateInstance("mine");
        var order = new List<string>();
        w.Simulation.AddSystem(new NamedSystem("a", order)).AddSystem(new NamedSystem("b", order));
        w.Simulation.RunTick();
        order.ShouldBe(new[] { "a:test", "a:mine", "b:test", "b:mine" });
    }

    /// <summary>Pide 1 000 bytes por tick, pero solo en la instancia de `mapId`.</summary>
    private sealed class AllocatingSystem(string mapId) : IMapSystem
    {
        public object? Sink { get; private set; }
        public string Name => "alloc";
        public void Tick(MapInstance map, TickContext ctx) { if (map.MapId == mapId) Sink = new byte[1000]; }
    }

    [Fact]
    public void InstanceAllocs_ChargeEachInstanceWithWhatItsSystemsAllocated() // HU-072 CA3
    {
        var w = new WorldBuilder().WithMap().Build();
        w.World.RegisterMap(new MapData("mine", "Mina", new CollisionGrid(8, 8), [], [], [new GraveyardDef("g", new Vec2(1, 1))], [], [], "g"));
        var mine = w.World.CreateInstance("mine");
        w.Simulation.CombatTimings = new();
        w.Simulation.InstanceAllocs = new();
        w.Simulation.AddSystem(new AllocatingSystem("mine"));
        TickRunner.Run(w, 10);
        w.Simulation.InstanceAllocs[mine.Id].ShouldBeGreaterThanOrEqualTo(10 * 1000);
        w.Simulation.InstanceAllocs.GetValueOrDefault(w.Map.Id).ShouldBeLessThan(1000);
    }

    [Fact]
    public void PreAndPostTickHooks_RunAroundSystems()
    {
        var w = new WorldBuilder().WithMap().Build();
        var order = new List<string>();
        w.Simulation.OnPreTick(_ => order.Add("pre")).AddSystem(new NamedSystem("sys", order)).OnPostTick(_ => order.Add("post"));
        w.Simulation.RunTick();
        order.ShouldBe(new[] { "pre", "sys:test", "post" });
    }

    [Fact]
    public void TickScheduler_FixedStep_NoDrift() // HU-004 CA1
    {
        var s = new TickScheduler();
        var total = 0;
        for (var i = 0; i < 100; i++) total += s.Advance(100); // 100 × 100 ms = 10 s
        total.ShouldBe(200);
        s.DroppedMs.ShouldBe(0);
    }

    [Fact]
    public void TickScheduler_IrregularSleep_StillExactTickCount()
    {
        var s = new TickScheduler();
        var elapsed = new[] { 47L, 53, 61, 39, 50, 50, 70, 30, 48, 52 }; // suma 500 → 10 ticks
        var total = 0;
        foreach (var e in elapsed) total += s.Advance(e);
        total.ShouldBe(10);
    }

    [Fact]
    public void TickScheduler_LongStall_CatchesUpAtMost3Ticks() // HU-004 CA2 (espiral de la muerte)
    {
        var s = new TickScheduler();
        s.Advance(1000).ShouldBe(3);            // 20 ticks atrasados → solo 3
        s.DroppedMs.ShouldBe(17 * 50);
        s.Advance(50).ShouldBe(1);              // y sigue a ritmo normal
        s.MsUntilNextTick.ShouldBe(50);
    }

    [Fact]
    public void TickStats_Percentiles()
    {
        var st = new TickStats(100);
        for (var i = 1; i <= 100; i++) st.Record(i);
        var (p50, p99) = st.Percentiles();
        p50.ShouldBe(50);
        p99.ShouldBe(99);
        st.MaxMs.ShouldBe(100);
    }

    [Fact]
    public void FixedRng_ReturnsRollsInOrder_AndRepeatsLast()
    {
        var rng = new FixedRng(0.1, 0.9);
        rng.NextDouble().ShouldBe(0.1);
        rng.NextDouble().ShouldBe(0.9);
        rng.NextDouble().ShouldBe(0.9);
        new FixedRng(0.5).Next(0, 10).ShouldBe(5);
    }

    [Fact]
    public void SeededRng_IsReproducible()
    {
        var a = new SeededRng(42); var b = new SeededRng(42);
        for (var i = 0; i < 10; i++) a.Next(0, 1000).ShouldBe(b.Next(0, 1000));
    }

    private sealed class NamedSystem(string name, List<string> log) : IMapSystem
    {
        public string Name => name;
        public void Tick(MapInstance map, TickContext ctx) => log.Add($"{name}:{map.MapId}");
    }
}
