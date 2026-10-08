using PixelRealms.Game.Combat;
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
    public void MidLeap_OverPortal_DoesNotCross_UntilLanding() // HU-087: el vuelo lleva coordenadas de este mapa
    {
        var w = Build(4, (11, 11));
        var ana = w.Player("Ana");
        ana.Combat.Flight = new LeapFlight(w.Content.Spell("rogue_shadowstep"), new Vec2(8, 11), new Vec2(14, 11), 0, 60_000);
        ana.RequestedPortalId = "to_mine"; // ni pisándolo ni pidiéndolo
        TickRunner.Run(w, 2).OfType<PortalUsed>().ShouldBeEmpty();
        ana.Combat.Flight = null; // aterriza sobre el portal
        TickRunner.Run(w, 1).OfType<PortalUsed>().Single().Portal.PortalId.ShouldBe("to_mine");
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

    /// <summary>HU-112: la salida de la Mina al Bosque, cerrada hasta la Fase 2.</summary>
    private static readonly PortalDef Exit = new("to_forest", "forest", 12, 29, MinLevel: null, new Vec2(10, 10), new Vec2(2, 3), MinPhase: 2, LockedText: "El derrumbe aún bloquea el paso");

    /// <summary>Ana (nivel 6) sobre la salida, con la fase activa dada; `isMapLoaded` null = todos los destinos cargados.</summary>
    private static TestWorld BuildExit(int phase, Func<string, bool>? isMapLoaded = null)
    {
        var data = new MapData("test", "Test", new CollisionGrid(32, 32), [], [], [new GraveyardDef("gy", new Vec2(2, 2))], [], [Exit], "gy");
        var w = new WorldBuilder().WithMap(data).WithPlayer("Ana", "warrior", 6, (11, 11)).Build();
        var rules = w.Content.Rules with { World = w.Content.Rules.World with { CurrentPhase = phase } };
        w.Simulation.Context.RulesProvider = () => rules;
        w.Simulation.AddSystem(new PortalSystem(isMapLoaded));
        return w;
    }

    [Fact]
    public void MinPhase_AboveTheActivePhase_PortalLocked_Once_UntilLeavingPortal() // HU-112 CA2
    {
        var w = BuildExit(phase: 1);
        var events = TickRunner.Run(w, 5);
        events.OfType<PortalUsed>().ShouldBeEmpty();
        events.OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.PortalLocked);
        // Sale y vuelve a pisarla: se vuelve a avisar (como con el nivel).
        w.Player("Ana").Position = new Vec2(20, 20);
        TickRunner.Run(w, 1);
        w.Player("Ana").Position = new Vec2(11, 11);
        TickRunner.Run(w, 1).OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.PortalLocked);

        var asked = BuildExit(phase: 1);
        asked.Player("Ana").Position = new Vec2(12.5f, 11); // a menos de 1 casilla, sin pisarla: con UsePortal
        asked.Player("Ana").RequestedPortalId = "to_forest";
        var askedEvents = TickRunner.Run(asked, 1);
        askedEvents.OfType<PortalUsed>().ShouldBeEmpty();
        askedEvents.OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.PortalLocked);
    }

    [Fact]
    public void MinPhase_Reached_Crosses() // HU-112 CA1
    {
        var w = BuildExit(phase: 2);
        TickRunner.Run(w, 1).OfType<PortalUsed>().Single().Portal.TargetMapId.ShouldBe("forest");
    }

    [Fact]
    public void TargetMapNotLoaded_Locked_EvenWhenThePhaseAllowsIt() // HU-112 (HU-115: salida a un tier que aún no existe)
    {
        var w = BuildExit(phase: 3, isMapLoaded: id => id != "forest");
        TickRunner.Run(w, 3).OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.PortalLocked);

        var asked = BuildExit(phase: 3, isMapLoaded: id => id != "forest");
        asked.Player("Ana").RequestedPortalId = "to_forest"; // también con UsePortal
        TickRunner.Run(asked, 1).OfType<PortalRejected>().Single().ErrorCode.ShouldBe(PortalPolicy.PortalLocked);
    }

    [Fact]
    public void Check_ClosedPassage_GoesBeforeLevelAndCombat() // HU-112 CA2: el aviso es el del derrumbe, no "en combate"
    {
        var w = BuildExit(phase: 1);
        var ana = w.Player("Ana");
        var rules = w.Content.Rules with { World = w.Content.Rules.World with { CurrentPhase = 1 } }; // el contenido ya está en Fase 2
        var phase2 = rules with { World = rules.World with { CurrentPhase = 2 } };
        var exitWithLevel = Exit with { MinLevel = 8 };

        PortalPolicy.Check(ana, Exit, 0, rules, targetLoaded: true).ShouldBe(PortalPolicy.PortalLocked);
        PortalPolicy.Check(ana, Exit, 0, phase2, targetLoaded: true).ShouldBeNull();
        PortalPolicy.Check(ana, Exit, 0, phase2, targetLoaded: false).ShouldBe(PortalPolicy.PortalLocked);
        PortalPolicy.Check(ana, Portal, 0, rules, targetLoaded: false).ShouldBe(PortalPolicy.PortalLocked); // sin minPhase, también
        PortalPolicy.Check(ana, exitWithLevel, 0, rules, targetLoaded: true).ShouldBe(PortalPolicy.PortalLocked);
        PortalPolicy.Check(ana, exitWithLevel, 0, phase2, targetLoaded: true).ShouldBe(PortalPolicy.LevelTooLow);
        ana.EnterCombat(0);
        PortalPolicy.Check(ana, Exit, 0, rules, targetLoaded: true).ShouldBe(PortalPolicy.PortalLocked);
        PortalPolicy.Check(ana, Exit, 0, phase2, targetLoaded: true).ShouldBe(PortalPolicy.InCombat);
    }
}
