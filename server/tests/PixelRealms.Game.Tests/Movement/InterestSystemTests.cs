using PixelRealms.Game.Core;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Movement;

public sealed class InterestSystemTests
{
    [Fact]
    public void PlayersWithin16Tiles_SeeEachOther_AndLoseSightWhenFar() // HU-023 CA1/CA2
    {
        var w = new WorldBuilder().WithMap(100, 100).WithPlayer("Ana", "mage", at: (20, 20)).WithPlayer("Bob", "rogue", at: (28, 20)).Build();
        var interest = new InterestSystem();
        w.Simulation.AddSystem(new MovementSystem()).AddSystem(interest);
        var events = TickRunner.Run(w, 1);
        events.OfType<EntityEnteredView>().Count().ShouldBe(2);
        events.OfType<EntityEnteredView>().ShouldContain(e => e.Observer.Name == "Ana" && e.Entity.Name == "Bob");
        interest.VisibleTo(w.Map, w.Player("Ana")).ShouldContain(w.Player("Bob").Id.Value);

        // Bob se va a 3 celdas de distancia (celdas de 16): sale de la vista con "left".
        w.Player("Bob").Position = new Vec2(70, 70);
        events = TickRunner.Run(w, 1);
        var left = events.OfType<EntityLeftView>().ToList();
        left.Count.ShouldBe(2);
        left.ShouldContain(e => e.Observer.Name == "Ana" && e.Reason == InterestSystem.ReasonLeft);
        interest.VisibleTo(w.Map, w.Player("Ana")).ShouldBeEmpty();

        // Vuelve: reaparece sin repetir eventos en ticks siguientes.
        w.Player("Bob").Position = new Vec2(22, 22);
        TickRunner.Run(w, 1).OfType<EntityEnteredView>().Count().ShouldBe(2);
        TickRunner.Run(w, 3).Count.ShouldBe(0);
    }

    [Fact]
    public void NeighbourCells_CountAsVisible()
    {
        // Ana en la celda (1,1) [16..32); Bob en la (2,2) [32..48): celdas vecinas → visible aunque estén a ~22 casillas.
        var w = new WorldBuilder().WithMap(100, 100).WithPlayer("Ana", "mage", at: (17, 17)).WithPlayer("Bob", "rogue", at: (47, 47)).Build();
        var interest = new InterestSystem();
        w.Simulation.AddSystem(interest);
        TickRunner.Run(w, 1).OfType<EntityEnteredView>().Count().ShouldBe(2);
        // Carla en la celda (3,3): no vecina de la (1,1).
        var carla = new Game.Entities.Player(w.World.EntityIds.Next(), "Carla", "priest") { Position = new Vec2(50, 50), Hp = 1, MaxHp = 1 };
        w.Map.Add(carla);
        var events = TickRunner.Run(w, 1).OfType<EntityEnteredView>().ToList();
        events.ShouldContain(e => e.Observer.Name == "Bob" && e.Entity.Name == "Carla");
        events.ShouldNotContain(e => e.Observer.Name == "Ana" && e.Entity.Name == "Carla");
        interest.ObserversOf(w.Map, carla.Id).Select(p => p.Name).ToArray().ShouldBe(new[] { "Bob", "Carla" }, ignoreOrder: true);
    }

    [Fact]
    public void Monsters_AreVisibleToo_AndForgetEntityEmitsDespawn()
    {
        var w = new WorldBuilder().WithMap(100, 100).WithPlayer("Ana", "mage", at: (20, 20)).WithMonster("wolf", at: (24, 20)).Build();
        var interest = new InterestSystem();
        w.Simulation.AddSystem(interest);
        TickRunner.Run(w, 1).OfType<EntityEnteredView>().ShouldHaveSingleItem().Entity.Name.ShouldBe("Lobo de las colinas");
        var wolf = w.Monster("wolf");
        w.Simulation.Context.BeginTick(99, 99);
        interest.ForgetEntity(w.Map, wolf.Id, InterestSystem.ReasonDied, w.Simulation.Context);
        w.Simulation.Context.Events.OfType<EntityLeftView>().ShouldHaveSingleItem().Reason.ShouldBe("died");
    }
}
