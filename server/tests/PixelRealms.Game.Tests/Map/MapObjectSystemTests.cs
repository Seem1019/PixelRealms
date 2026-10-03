using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Map;

/// <summary>HU-083 CA1: palancas y puertas (puzle de la Sala 2 de la Mina).</summary>
public sealed class MapObjectSystemTests
{
    // Mapa de 20×10 partido por una puerta de una casilla de ancho en x = 10; una palanca a cada lado y, al fondo del lado este
    // (la "sala"), una que abre sola.
    private static readonly DoorDef Door = new("door", new Vec2(10, 0), new Vec2(1, 10));
    private static readonly LeverDef West = new("lever_w", "door", new Vec2(8.5f, 5.5f));
    private static readonly LeverDef East = new("lever_e", "door", new Vec2(12.5f, 5.5f));
    private static readonly LeverDef Inside = new("lever_in", "door", new Vec2(17.5f, 5.5f), OpensAlone: true);

    private static TestWorld Build()
    {
        var data = new MapData("cave", "Cueva", new CollisionGrid(20, 10), [], [], [new GraveyardDef("gy", new Vec2(2, 2))], [], [], "gy", [West, East, Inside], [Door]);
        return new WorldBuilder().WithMap(data).WithPlayer("Ana", "warrior", 4, (8.5f, 5.5f)).WithPlayer("Bob", "mage", 4, (12.5f, 5.5f)).BuildWithCombat();
    }

    [Fact]
    public void Door_StartsClosed_InTheInstanceGrid_NotInTheSharedOne()
    {
        var w = Build();
        w.Map.Collision.IsSolid(10, 5).ShouldBeTrue();
        w.Map.Collision.BlocksSight(10, 5).ShouldBeTrue();
        w.Map.Data.Collision.IsSolid(10, 5).ShouldBeFalse(); // el MapData es de todas las instancias
        // Ana camina hacia el este y choca con la puerta.
        var ana = w.Player("Ana");
        for (var i = 0; i < 40; i++) MovementSystem.Move(ana, 1, 0, w.Map.Collision, 4f);
        ana.Position.X.ShouldBeLessThan(10f);
    }

    [Fact]
    public void BothLevers_OpenTheDoor_OneIsNotEnough()
    {
        var w = Build();
        var ctx = w.Begin();
        w.Combat.Objects.Pull(w.Player("Ana"), "lever_w", w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<MapObjectChangedEvent>().Single().ShouldBe(new MapObjectChangedEvent(w.Map.Id, "lever_w", MapObjectSystem.On));
        w.Map.Collision.IsSolid(10, 5).ShouldBeTrue();

        var ctx2 = w.Begin();
        w.Combat.Objects.Pull(w.Player("Bob"), "lever_e", w.Map, ctx2).ShouldBeNull();
        ctx2.Events.OfType<MapObjectChangedEvent>().Select(e => (e.ObjectId, e.State)).ShouldBe([("lever_e", MapObjectSystem.On), ("door", MapObjectSystem.Open)]);
        w.Map.Collision.IsSolid(10, 5).ShouldBeFalse();
        w.Map.Collision.BlocksSight(10, 5).ShouldBeFalse();
        MapObjectSystem.States(w.Map).ShouldBe([("lever_w", "on"), ("lever_e", "on"), ("lever_in", "off"), ("door", "open")]);
    }

    [Fact]
    public void Pull_IsValidated_RangeDeathAndId()
    {
        var w = Build();
        var ana = w.Player("Ana");
        w.Combat.Objects.Pull(ana, "lever_e", w.Map, w.Begin()).ShouldBe("out_of_range"); // está al otro lado
        w.Combat.Objects.Pull(ana, "nope", w.Map, w.Begin()).ShouldBe("not_found");
        ana.Hp = 0;
        w.Combat.Objects.Pull(ana, "lever_w", w.Map, w.Begin()).ShouldBe("is_dead");
        w.Map.LeversOn.ShouldBeEmpty();
    }

    [Fact]
    public void Door_ClosesAfterTheResetTime_AndTheLeversGoBack_ButNeverOnSomeoneInside()
    {
        var w = Build();
        var resetMs = (int)(w.Content.Rules.World.DoorResetSec * 1000);
        w.Combat.Objects.Pull(w.Player("Ana"), "lever_w", w.Map, w.Begin());
        w.Combat.Objects.Pull(w.Player("Bob"), "lever_e", w.Map, w.Begin());
        var ana = w.Player("Ana");
        ana.Position = new Vec2(10.5f, 5.5f); // en el hueco de la puerta cuando toca cerrar

        TickRunner.RunMs(w, resetMs + 100).OfType<MapObjectChangedEvent>().ShouldBeEmpty();
        w.Map.Collision.IsSolid(10, 5).ShouldBeFalse(); // sigue abierta mientras Ana esté ahí

        ana.Position = new Vec2(7.5f, 5.5f);
        var closed = TickRunner.Run(w, 1).OfType<MapObjectChangedEvent>().Select(e => (e.ObjectId, e.State)).ToList();
        closed.ShouldBe([("door", "closed"), ("lever_w", "off"), ("lever_e", "off")]);
        w.Map.Collision.IsSolid(10, 5).ShouldBeTrue();
        w.Map.LeversOn.ShouldBeEmpty();
    }

    [Fact]
    public void SomeoneLeftInsideWhenItCloses_OpensItAloneWithTheInsideLever() // revisión de autoridad: encerrados con el jefe
    {
        var w = Build();
        var resetMs = (int)(w.Content.Rules.World.DoorResetSec * 1000);
        w.Combat.Objects.Pull(w.Player("Ana"), "lever_w", w.Map, w.Begin());
        w.Combat.Objects.Pull(w.Player("Bob"), "lever_e", w.Map, w.Begin());
        var bob = w.Player("Bob");
        bob.Position = new Vec2(17.5f, 5.5f); // al fondo de la sala, lejos del umbral
        TickRunner.RunMs(w, resetMs + 100);
        w.Map.Collision.IsSolid(10, 5).ShouldBeTrue(); // se cerró con Bob dentro

        var ctx = w.Begin();
        w.Combat.Objects.Pull(bob, "lever_in", w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<MapObjectChangedEvent>().ShouldContain(e => e.ObjectId == "door" && e.State == MapObjectSystem.Open);
        w.Map.Collision.IsSolid(10, 5).ShouldBeFalse();
    }

    [Fact]
    public void PullingALeverWhileTheDoorIsOpen_RenewsTheTimer() // revisión de autoridad: un grupo que entra tarde
    {
        var w = Build();
        var resetMs = (int)(w.Content.Rules.World.DoorResetSec * 1000);
        w.Combat.Objects.Pull(w.Player("Ana"), "lever_w", w.Map, w.Begin());
        w.Combat.Objects.Pull(w.Player("Bob"), "lever_e", w.Map, w.Begin());
        TickRunner.RunMs(w, resetMs - 1000);
        w.Combat.Objects.Pull(w.Player("Ana"), "lever_w", w.Map, w.Begin()).ShouldBeNull(); // ya activada: renueva
        TickRunner.RunMs(w, 2000).OfType<MapObjectChangedEvent>().ShouldBeEmpty();
        w.Map.Collision.IsSolid(10, 5).ShouldBeFalse(); // sigue abierta pasado el plazo original
    }

    [Fact]
    public void ALeapAcrossTheDoor_KeepsItOpenUntilLanding() // revisión de autoridad: el salto validó la recta al despegar
    {
        var w = Build();
        var resetMs = (int)(w.Content.Rules.World.DoorResetSec * 1000);
        w.Combat.Objects.Pull(w.Player("Ana"), "lever_w", w.Map, w.Begin());
        w.Combat.Objects.Pull(w.Player("Bob"), "lever_e", w.Map, w.Begin());
        TickRunner.RunMs(w, resetMs - 100);
        var ana = w.Player("Ana");
        ana.Position = new Vec2(7.5f, 5.5f);
        ana.Combat.Flight = new LeapFlight(w.Content.Spell("rogue_shadowstep"), new Vec2(7.5f, 5.5f), new Vec2(13.5f, 5.5f), 0, long.MaxValue);
        TickRunner.RunMs(w, 500).OfType<MapObjectChangedEvent>().ShouldBeEmpty();
        ana.Combat.Flight = null;
        ana.Position = new Vec2(13.5f, 5.5f);
        TickRunner.Run(w, 1).OfType<MapObjectChangedEvent>().ShouldContain(e => e.ObjectId == "door" && e.State == MapObjectSystem.Closed);
    }
}
