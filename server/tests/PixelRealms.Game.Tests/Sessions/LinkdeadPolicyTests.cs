using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Sessions;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Sessions;

/// <summary>HU-025 CA1: 10 s quieto fuera de combate; en combate hasta salir de combate, máx. linkdeadInCombatMaxSec.</summary>
public sealed class LinkdeadPolicyTests
{
    private static readonly PixelRealms.Content.ContentDb Db = TestContent.Load();

    private static Player NewPlayer() => new(new EntityId(1), "Ana", "warrior") { MoveDx = 1, MoveDy = 0 };

    [Fact]
    public void ConnectedPlayer_IsNeverRemoved()
    {
        var p = NewPlayer();
        LinkdeadPolicy.ShouldRemove(p, 999_999, Db.Rules).ShouldBeFalse();
    }

    [Fact]
    public void MarkLinkdead_StopsMovement_AndRemovesAfterLinkdeadSec()
    {
        var p = NewPlayer();
        LinkdeadPolicy.MarkLinkdead(p, 1000);
        p.IsLinkdead.ShouldBeTrue();
        p.ConnectionId.ShouldBe(-1);
        (p.MoveDx, p.MoveDy).ShouldBe((0, 0));
        var linkdeadMs = (long)(Db.Rules.Combat.LinkdeadSec * 1000);
        LinkdeadPolicy.ShouldRemove(p, 1000 + linkdeadMs - 1, Db.Rules).ShouldBeFalse();
        LinkdeadPolicy.ShouldRemove(p, 1000 + linkdeadMs, Db.Rules).ShouldBeTrue();
    }

    [Fact]
    public void InCombat_StaysUntilOutOfCombat()
    {
        var p = NewPlayer();
        LinkdeadPolicy.MarkLinkdead(p, 0);
        var linkdeadMs = (long)(Db.Rules.Combat.LinkdeadSec * 1000);
        var windowMs = (long)(Db.Rules.Combat.InCombatWindowSec * 1000);
        p.EnterCombat(linkdeadMs - 1000); // recibió daño 1 s antes de vencer el plazo
        LinkdeadPolicy.ShouldRemove(p, linkdeadMs, Db.Rules).ShouldBeFalse();                    // sigue en combate
        LinkdeadPolicy.ShouldRemove(p, linkdeadMs - 1000 + windowMs - 1, Db.Rules).ShouldBeFalse();
        LinkdeadPolicy.ShouldRemove(p, linkdeadMs - 1000 + windowMs, Db.Rules).ShouldBeTrue();   // salió de combate → fuera
    }

    [Fact]
    public void InCombat_IsCappedByLinkdeadInCombatMaxSec()
    {
        var p = NewPlayer();
        LinkdeadPolicy.MarkLinkdead(p, 0);
        var maxMs = (long)(Db.Rules.Combat.LinkdeadInCombatMaxSec * 1000);
        p.EnterCombat(maxMs - 100); // sigue en combate justo al llegar al tope
        LinkdeadPolicy.ShouldRemove(p, maxMs - 1, Db.Rules).ShouldBeFalse();
        LinkdeadPolicy.ShouldRemove(p, maxMs, Db.Rules).ShouldBeTrue();
    }

    [Fact]
    public void Reconnect_ClearsLinkdead()
    {
        var p = NewPlayer();
        LinkdeadPolicy.MarkLinkdead(p, 0);
        LinkdeadPolicy.MarkReconnected(p, 7);
        p.IsLinkdead.ShouldBeFalse();
        p.ConnectionId.ShouldBe(7);
    }

    [Fact]
    public void Reconnect_RestartsInputSequence()
    {
        var p = NewPlayer();
        p.LastInputSeq = 4200;
        LinkdeadPolicy.MarkLinkdead(p, 0);
        LinkdeadPolicy.MarkReconnected(p, 7);
        p.LastInputSeq.ShouldBe(0); // el cliente nuevo numera desde 1
    }

    [Fact]
    public void InCombat_WindowFromRules()
    {
        var a = NewPlayer();
        a.IsInCombat(0, Db.Rules.Combat.InCombatWindowSec).ShouldBeFalse();
        a.EnterCombat(5000);
        var windowMs = (long)(Db.Rules.Combat.InCombatWindowSec * 1000);
        a.IsInCombat(5000 + windowMs - 1, Db.Rules.Combat.InCombatWindowSec).ShouldBeTrue();
        a.IsInCombat(5000 + windowMs, Db.Rules.Combat.InCombatWindowSec).ShouldBeFalse();
    }
}
