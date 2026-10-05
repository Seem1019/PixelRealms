using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>
/// HU-088 CA1: los sistemas del tick no piden memoria en estado estable (sin LINQ, sin closures, sin enumeradores en el heap, con
/// reservas fijas). Se mide en el hilo del test, que es el que corre el tick: lo de otros hilos no cuenta.
/// </summary>
[Collection(nameof(LoadScenarioIsolation))]
public sealed class TickAllocationTests
{
    [Fact]
    public void IdleWorld_MonstersPatrolling_AllocatesNothingPerTick()
    {
        // 40 monstruos que patrullan y un jugador lejos de todos: IA, movimiento, interés, recursos y el resto de sistemas en marcha.
        var b = new WorldBuilder().WithMap(80, 80).WithPlayer("Ana", "warrior", 3, (75, 75));
        for (var i = 0; i < 40; i++) b.WithMonster(i % 2 == 0 ? "slime" : "boar", (5 + i % 8 * 4, 5 + i / 8 * 4), wanderRadius: 3);
        var w = b.BuildWithCombat();
        TickRunner.Run(w, 400); // calentamiento: rutas, diccionarios y listas llegan a su tamaño

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 400; i++) w.Simulation.RunTick();
        var perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 400.0;

        perTick.ShouldBe(0, "bytes por tick en un mundo sin combate");
    }

    [Fact]
    public void ActiveDuel_BothInsideTheZone_AllocatesNothingPerTick() // HU-101: la zona se comprueba cada tick
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 5, (10, 10)).WithPlayer("Bob", "mage", 5, (12, 10)).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob");
        w.Combat.Pvp.Request(ana, bob, w.Map, w.Begin()).ShouldBeNull();
        w.Combat.Pvp.Respond(bob, true, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.Run(w, 400); // cuenta atrás y calentamiento
        w.Combat.Pvp.InActiveDuel(ana).ShouldBeTrue();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 400; i++) w.Simulation.RunTick();
        var perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 400.0;

        perTick.ShouldBe(0, "bytes por tick con un duelo en marcha y los dos dentro de la zona");
    }
}
