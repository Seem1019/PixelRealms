using PixelRealms.Game.Core;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Movement;

public sealed class MovementSystemTests
{
    [Fact]
    public void Player_MovesAt4TilesPerSecond_AndStopsAtWalls() // HU-021 CA2
    {
        var w = new WorldBuilder().WithMap(20, 20).WithWall(10, 5).WithPlayer("Ana", "warrior", at: (5, 5)).Build();
        var ana = w.Player("Ana");
        w.Simulation.AddSystem(new MovementSystem());
        ana.MoveDx = 1; ana.LastInputAtMs = 10_000; // el cliente reenvía el input cada 200 ms: aquí lo damos por fresco
        TickRunner.Run(w, 20); // 1 s → 4 casillas
        ana.Position.X.ShouldBe(9.0f, 0.01f);
        ana.Facing.ShouldBe(Game.Entities.Direction.E);
        TickRunner.Run(w, 20);
        // Pared en la casilla 10 (160..176 px): el cuerpo (half 5 px) se detiene en 155 px = 9.6875 casillas
        ana.Position.X.ShouldBe(155f / 16f, 0.01f);
        ana.Dirty.ShouldBeTrue();
    }

    [Fact]
    public void NoInputFor500Ms_StopsPlayer() // HU-021 CA5
    {
        var w = new WorldBuilder().WithMap(20, 20).WithPlayer("Ana", "warrior", at: (5, 5)).Build();
        var ana = w.Player("Ana");
        w.Simulation.AddSystem(new MovementSystem());
        ana.MoveDx = 1; ana.LastInputAtMs = 0;
        TickRunner.Run(w, 10); // 500 ms: sigue moviéndose justo hasta el límite
        var afterHalfSecond = ana.Position.X;
        TickRunner.Run(w, 10);
        ana.Position.X.ShouldBe(afterHalfSecond, 0.01f);
        ana.MoveDx.ShouldBe(0);
    }

    [Fact]
    public void DeadPlayer_DoesNotMove()
    {
        var w = new WorldBuilder().WithMap(20, 20).WithPlayer("Ana", "warrior", at: (5, 5)).Build();
        var ana = w.Player("Ana");
        ana.Hp = 0;
        w.Simulation.AddSystem(new MovementSystem());
        ana.MoveDx = 1; ana.LastInputAtMs = 10_000;
        TickRunner.Run(w, 5);
        ana.Position.X.ShouldBe(5f);
    }

    [Fact]
    public void SpeedMultiplier_Applies() // ADR-019: castear al 50 %
    {
        var w = new WorldBuilder().WithMap(20, 20).WithPlayer("Ana", "warrior", at: (5, 5)).Build();
        var ana = w.Player("Ana");
        w.Simulation.AddSystem(new MovementSystem { SpeedMultiplier = (_, _) => 0.5f });
        ana.MoveDy = 1; ana.LastInputAtMs = 10_000;
        TickRunner.Run(w, 10);
        ana.Position.Y.ShouldBe(6.0f, 0.01f);
    }
}
