using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
using PixelRealms.Game.Portals;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Portals;

/// <summary>HU-027 CA1/CA3/CA4: detección de portal, rechazos (una sola vez) y UsePortal en rango.</summary>
public sealed class PortalSystemTests
{
    private static readonly PortalDef Portal = new("to_mine", "mine", 5, 5, MinLevel: 4, new Vec2(10, 10), new Vec2(2, 2));

    private static TestWorld Build(int level, (float x, float y) at)
    {
        var data = new MapData("test", "Test", new CollisionGrid(32, 32), [], [], [new GraveyardDef("gy", new Vec2(2, 2))], [], [Portal], "gy");
        var w = new WorldBuilder().WithMap(data).WithPlayer("Ana", "warrior", level, at).Build();
        w.Simulation.AddSystem(new PortalSystem());
        return w;
    }

    [Fact]
    public void StandingOnPortal_WithLevel_EmitsPortalUsed_Once()
    {
        var w = Build(4, (11, 11));
        var events = TickRunner.Run(w, 1);
        var used = events.OfType<PortalUsed>().Single();
        used.Portal.PortalId.ShouldBe("to_mine");
        used.Player.Name.ShouldBe("Ana");
    }

    [Fact]
    public void OutsidePortal_NothingHappens()
    {
        var w = Build(4, (12.5f, 11));
        TickRunner.Run(w, 3).OfType<PortalUsed>().ShouldBeEmpty();
    }

    [Fact]
    public void LevelTooLow_RejectedOnce_UntilLeavingPortal()
    {
        var w = Build(1, (11, 11));
        var rejected = TickRunner.Run(w, 5).OfType<PortalRejected>().ToList();
        rejected.Count.ShouldBe(1);
        rejected[0].ErrorCode.ShouldBe(PortalPolicy.LevelTooLow);
        // Sale del portal y vuelve: se vuelve a avisar.
        w.Player("Ana").Position = new Vec2(20, 20);
        TickRunner.Run(w, 1);
        w.Player("Ana").Position = new Vec2(11, 11);
        TickRunner.Run(w, 1).OfType<PortalRejected>().Count().ShouldBe(1);
    }

    [Fact]
    public void Dead_IsDead_InCombat_InCombat()
    {
        var w = Build(4, (11, 11));
        var ana = w.Player("Ana");
        ana.Hp = 0;
        TickRunner.Run(w, 1).OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.IsDead);

        var w2 = Build(4, (11, 11));
        var bob = w2.Player("Ana");
        bob.EnterCombat(w2.Clock.NowMs);
        TickRunner.Run(w2, 1).OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.InCombat);
        // Al salir de combate (ventana de rules) y recolocarse, cruza.
        bob.LastCombatAtMs = long.MinValue;
        bob.RejectedPortalId = null;
        TickRunner.Run(w2, 1).OfType<PortalUsed>().Count().ShouldBe(1);
    }

    [Fact]
    public void UsePortal_WithinOneTile_Used_FartherOutOfRange_UnknownNotFound()
    {
        var w = Build(4, (13, 11)); // a 1 casilla del borde derecho (12)
        w.Player("Ana").RequestedPortalId = "to_mine";
        TickRunner.Run(w, 1).OfType<PortalUsed>().Count().ShouldBe(1);

        var far = Build(4, (13.5f, 11));
        far.Player("Ana").RequestedPortalId = "to_mine";
        TickRunner.Run(far, 1).OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.OutOfRange);

        var unknown = Build(4, (11, 11));
        unknown.Player("Ana").RequestedPortalId = "nope";
        TickRunner.Run(unknown, 1).OfType<PortalRejected>().Single().ErrorCode.ShouldBe("not_found");
    }

    [Fact]
    public void DistanceToPortalRect()
    {
        PortalPolicy.DistanceTo(Portal, new Vec2(11, 11)).ShouldBe(0f);
        PortalPolicy.DistanceTo(Portal, new Vec2(13, 11)).ShouldBe(1f);
        PortalPolicy.DistanceTo(Portal, new Vec2(13, 13)).ShouldBe(MathF.Sqrt(2), 0.0001f);
    }
}
