using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>Prueba de juego: los NPC (tendera, maestro de clases) se podían atacar y matar de un golpe. Son neutrales: ni
/// enemigos ni aliados de nadie, no reciben hechizos, áreas ni autoataque, y los monstruos no los persiguen.</summary>
public sealed class NpcNeutralityTests
{
    private static (TestWorld World, Npc Npc) Village()
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 5, (10.5f, 10.5f))
            .WithMonster("slime", (13.5f, 12.5f), wanderRadius: 0).BuildWithCombat();
        var npc = new Npc(w.World.EntityIds.Next(), new NpcDef("vendor", "Marta la tendera", "robledal_general_goods", "vendor", new Vec2(12.5f, 10.5f)))
        {
            Position = new Vec2(12.5f, 10.5f), Hp = 1, MaxHp = 1, // como los crea ServerApp: un golpe los mataba
        };
        w.Map.Add(npc);
        return (w, npc);
    }

    [Fact]
    public void Npc_IsNeitherEnemyNorAllyOfAnyone()
    {
        var (w, npc) = Village();
        var services = w.Combat.Services;
        services.IsEnemy(w.Player("Ana"), npc).ShouldBeFalse();
        services.IsEnemy(npc, w.Player("Ana")).ShouldBeFalse();
        services.IsEnemy(w.Monster("slime"), npc).ShouldBeFalse();
        services.IsAlly(w.Player("Ana"), npc).ShouldBeFalse();
    }

    [Fact]
    public void SingleTargetSpell_OnANpc_IsRejected()
    {
        var (w, npc) = Village();
        w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell("mage_fireball"), npc.Id, null, w.Map, w.Begin())
            .ShouldBe(CastErrors.InvalidTarget);
        npc.IsAlive.ShouldBeTrue();
    }

    [Fact]
    public void AreaSpell_OverANpc_LeavesItOut()
    {
        var (w, npc) = Village();
        var ana = w.Player("Ana");
        var targets = w.Combat.Targets.Resolve(ana, w.Content.Spell("mage_flame_burst"), null, npc.Position, ana.Position, w.Map, w.Begin());
        targets.ShouldNotContain(npc);
        targets.ShouldContain(w.Monster("slime")); // el área sigue alcanzando a los monstruos
    }
}
