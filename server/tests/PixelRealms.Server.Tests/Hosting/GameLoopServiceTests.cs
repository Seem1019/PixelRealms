using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Hosting;

public sealed class GameLoopServiceTests
{
    private static Simulation NewSimulation()
    {
        var world = new World();
        world.RegisterMap(new MapData("test", "Test", new CollisionGrid(8, 8), [], [], [new GraveyardDef("g", new Vec2(1, 1))], [], [], "g"));
        world.CreateInstance("test");
        return new Simulation(world, TestContent.Load().Rules, new SeededRng(1), new TickClock());
    }

    [Fact]
    public async Task Loop_Runs20TicksPerSecond() // HU-004 CA1 (2 s en vez de 10 para no alargar la suite)
    {
        var sim = NewSimulation();
        using var loop = new GameLoopService(sim, NullLogger<GameLoopService>.Instance);
        await loop.StartAsync(CancellationToken.None);
        await Task.Delay(2000);
        var ticks = loop.TicksRun;
        await loop.StopAsync(CancellationToken.None);
        ticks.ShouldBeInRange(38, 42);
        loop.Stats.Count.ShouldBeGreaterThan(30);
    }

    [Fact]
    public async Task Loop_StopsInUnderOneSecond_AndRunsOnStopping() // HU-004 CA5
    {
        var sim = NewSimulation();
        using var loop = new GameLoopService(sim, NullLogger<GameLoopService>.Instance);
        var stopped = false;
        loop.OnStopping = () => stopped = true;
        await loop.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        var sw = Stopwatch.StartNew();
        await loop.StopAsync(CancellationToken.None);
        sw.ElapsedMilliseconds.ShouldBeLessThan(1000);
        stopped.ShouldBeTrue();
        var after = loop.TicksRun;
        await Task.Delay(150);
        loop.TicksRun.ShouldBe(after); // ya no avanza
    }

    [Fact]
    public async Task Loop_SurvivesExceptionInSystem()
    {
        var sim = NewSimulation();
        sim.AddSystem(new ThrowingSystem());
        using var loop = new GameLoopService(sim, NullLogger<GameLoopService>.Instance);
        await loop.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await loop.StopAsync(CancellationToken.None);
        loop.TicksRun.ShouldBeGreaterThan(3);
    }

    private sealed class ThrowingSystem : IMapSystem
    {
        public string Name => "throws";
        public void Tick(MapInstance map, TickContext ctx) => throw new InvalidOperationException("boom");
    }
}
