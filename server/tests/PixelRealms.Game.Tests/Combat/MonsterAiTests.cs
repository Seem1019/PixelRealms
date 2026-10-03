using PixelRealms.Game.Ai;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>HU-031 (patrulla, respawn) y HU-036 (aggro, persecución, amenaza, evadir, hechizos de monstruo).</summary>
public sealed class MonsterAiTests
{
    [Fact]
    public void Boar_AggroWithinRangeAndLos_Slime_OnlyWhenHit() // HU-036 CA1
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 2, (10, 10)).WithMonster("boar", (13.5f, 10), wanderRadius: 0).WithMonster("slime", (12, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var boar = w.Monster("boar"); var slime = w.Monster("slime");
        TickRunner.RunMs(w, 600);
        boar.Brain.State.ShouldNotBe(AiState.Idle); // a 3.5 ≤ 4 con LOS
        slime.Brain.State.ShouldBe(AiState.Idle);   // aggroRange 0
        w.Combat.Damage.Deal(ana, slime, 1, PixelRealms.Content.Defs.School.Physical, false, null, w.Map, w.Begin());
        TickRunner.Run(w, 2);
        slime.Brain.State.ShouldNotBe(AiState.Idle);
        slime.Combat.TargetId.ShouldBe(ana.Id);

        // Sin LOS no hay aggro.
        var w2 = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 2, (10, 10)).WithMonster("boar", (13.5f, 10), wanderRadius: 0).BuildWithCombat();
        w2.Map.Data.Collision.SetBlocksSight(12, 10);
        TickRunner.RunMs(w2, 600);
        w2.Monster("boar").Brain.State.ShouldBe(AiState.Idle);
    }

    [Fact]
    public void Chase_GoesAroundWall_AndAttacksInRange() // HU-036 CA2
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 2, (10.5f, 10.5f)).WithMonster("boar", (16.5f, 10.5f), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var boar = w.Monster("boar");
        for (var y = 6; y <= 14; y++) w.Map.Data.Collision.SetSolid(13, y); // muro vertical entre ambos (sin bloquear la vista)
        boar.Threat.Add(ana.Id, 10);
        var events = TickRunner.RunMs(w, 8000);
        boar.IsAlive.ShouldBeTrue();
        boar.Brain.State.ShouldBe(AiState.Attack);
        Vec2.Distance(boar.Position, ana.Position).ShouldBeLessThanOrEqualTo((float)boar.Template.AttackRange + 0.01f);
        events.OfType<CombatHitEvent>().ShouldContain(e => e.Source == boar && e.Target == ana);
        // Nunca pisó el muro.
        w.Map.Data.Collision.IsSolidAt(boar.Position.X, boar.Position.Y).ShouldBeFalse();
    }

    [Fact]
    public void Threat_SwitchAt130PctForRanged_110ForMelee_TauntPins() // HU-036 CA3
    {
        var rules = TestContent.Load().Rules.Combat;
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 5, (10, 10)).WithPlayer("Bob", "mage", 5, (10, 12))
            .WithMonster("kobold_miner", (11, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var kobold = w.Monster("kobold_miner");
        kobold.Threat.Add(ana.Id, 100);
        kobold.Threat.Add(bob.Id, 109); // < 110 % → sigue con Ana
        kobold.Threat.Reevaluate(0, rules.ThreatSwitchMelee, _ => true).ShouldBe(ana.Id);
        kobold.Threat.Add(bob.Id, 1);   // 110 → cambia
        kobold.Threat.Reevaluate(0, rules.ThreatSwitchMelee, _ => true).ShouldBe(bob.Id);
        // A distancia: 130 %
        var archer = new ThreatTable();
        archer.Add(ana.Id, 100); archer.Add(bob.Id, 129);
        archer.Reevaluate(0, rules.ThreatSwitchRanged, _ => true).ShouldBe(ana.Id);
        archer.Add(bob.Id, 1);
        archer.Reevaluate(0, rules.ThreatSwitchRanged, _ => true).ShouldBe(bob.Id);
        // Provocar: fija a Ana durationMs y le pone amenaza máx · (1 + bonus).
        var taunt = w.Content.Spell("warrior_taunt").Effects.Single(e => e.Type == PixelRealms.Content.Defs.EffectType.Taunt);
        kobold.Threat.Taunt(ana.Id, 1000, taunt.DurationMs, rules.TauntThreatBonus);
        kobold.Threat.Of(ana.Id).ShouldBe(110 * (1 + rules.TauntThreatBonus), 1e-9);
        kobold.Threat.Add(bob.Id, 1000);
        kobold.Threat.Reevaluate(1000 + taunt.DurationMs - 1, rules.ThreatSwitchMelee, _ => true).ShouldBe(ana.Id);
        kobold.Threat.Reevaluate(1000 + taunt.DurationMs, rules.ThreatSwitchMelee, _ => true).ShouldBe(bob.Id);
    }

    [Fact]
    public void Leash_Evade_HealsForgetsImmune() // HU-036 CA4
    {
        var w = new WorldBuilder().WithMap(60, 60).WithPlayer("Ana", "warrior", 2, (10, 10)).WithMonster("boar", (12, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var boar = w.Monster("boar");
        boar.Hp = 10;
        w.Combat.Damage.Deal(ana, boar, 1, PixelRealms.Content.Defs.School.Physical, false, null, w.Map, w.Begin());
        ana.Position = new Vec2(12 + (float)boar.Template.LeashRange + 5, 10);
        boar.Position = new Vec2(12 + (float)boar.Template.LeashRange + 1, 10); // arrastrado más allá del leash
        TickRunner.Run(w, 2);
        boar.Combat.Evading.ShouldBeTrue();
        boar.Threat.Count.ShouldBe(0);
        // Inmune mientras vuelve.
        var hp = boar.Hp;
        w.Combat.Damage.Deal(ana, boar, 5, PixelRealms.Content.Defs.School.Physical, false, null, w.Map, w.Begin());
        boar.Hp.ShouldBe(hp);
        TickRunner.RunMs(w, 15_000);
        boar.Combat.Evading.ShouldBeFalse();
        boar.Hp.ShouldBe(boar.MaxHp);
        boar.Position.ShouldBe(boar.SpawnPosition);
        boar.Brain.State.ShouldBe(AiState.Idle);
    }

    [Fact]
    public void GoblinArcher_StaysAtRange_CastsShoot() // HU-036 CA5
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 5, (10, 10)).WithMonster("goblin_archer", (15, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var goblin = w.Monster("goblin_archer");
        goblin.Threat.Add(ana.Id, 1);
        var events = TickRunner.RunMs(w, 4000);
        Vec2.Distance(goblin.Position, ana.Position).ShouldBeGreaterThan(3f); // no se acerca al cuerpo a cuerpo
        var shoot = w.Content.Spell("goblin_shoot");
        events.OfType<CastStartedEvent>().ShouldContain(e => e.Caster == goblin && e.Spell.Id == "goblin_shoot");
        events.OfType<CastEndedEvent>().ShouldContain(e => e.Caster == goblin && e.Result == CastResults.Done);
        // Respeta su cooldown.
        var starts = events.OfType<CastStartedEvent>().Where(e => e.Spell.Id == "goblin_shoot").Count();
        starts.ShouldBeLessThanOrEqualTo(1 + 4000 / shoot.CooldownMs);
    }

    [Fact]
    public void Foreman_RallyBelowHalf_WhipNotTopThreat() // HU-036 CA5b
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 6, (10, 10)).WithPlayer("Bob", "mage", 6, (10, 12))
            .WithMonster("foreman_grask", (11, 10), wanderRadius: 0).BuildWithCombat();
        var ana = w.Player("Ana"); var bob = w.Player("Bob"); var boss = w.Monster("foreman_grask");
        boss.Threat.Add(ana.Id, 1000); boss.Threat.Add(bob.Id, 10);
        boss.Combat.CooldownEndsAtMs["foreman_slam"] = long.MaxValue; // aislamos el látigo
        var events = TickRunner.RunMs(w, 500);
        var whip = events.OfType<CastStartedEvent>().Where(e => e.Spell.Id == "foreman_whip").ToList();
        whip.ShouldNotBeEmpty();
        whip.ShouldAllBe(e => e.TargetId == bob.Id); // nunca al de mayor amenaza
        events.OfType<CastStartedEvent>().ShouldNotContain(e => e.Spell.Id == "foreman_rally"); // vida > 50 %
        boss.Hp = boss.MaxHp / 2 - 1;
        var ev2 = TickRunner.RunMs(w, 500);
        ev2.OfType<CastStartedEvent>().ShouldContain(e => e.Spell.Id == "foreman_rally" && e.TargetId == boss.Id);
        ev2.OfType<AuraAppliedEvent>().ShouldContain(e => e.Target == boss && e.Aura.AuraId == "foreman_rally_aura");
    }

    [Fact]
    public void Wander_StaysInRadius_WithPauses_Spawn_PopulatesAndRespawns() // HU-031 CA1/CA2/CA3
    {
        var db = TestContent.Load();
        var data = new MapData("t", "T", new CollisionGrid(40, 40), [new SpawnDef("s1", "slime", 3, 2f, new Vec2(10, 10), new Vec2(4, 4))], [],
            [new GraveyardDef("gy", new Vec2(2, 2))], [], [], "gy");
        var w = new WorldBuilder().WithMap(data).BuildWithCombat();
        w.Combat.Spawns.Populate(w.Map, new SeededRng(7)).ShouldBe(3);
        w.Map.Monsters.Count.ShouldBe(3);
        w.Map.Monsters.Values.ShouldAllBe(m => m.TemplateId == "slime" && m.Hp == db.Monster("slime").Hp && m.Level == db.Monster("slime").Level);
        var slime = w.Map.Monsters.Values.First();
        var spawnPos = slime.SpawnPosition;
        var ai = db.Rules.Ai;
        var moved = false;
        var stillTicks = 0;
        var pauses = new List<int>(); // duración (ticks) de cada pausa completa entre dos tramos de marcha
        for (var i = 0; i < 800; i++)
        {
            var before = slime.Position;
            TickRunner.Run(w, 1);
            if (slime.Position != before)
            {
                if (moved && stillTicks > 0) pauses.Add(stillTicks);
                moved = true; stillTicks = 0;
            }
            else if (moved) stillTicks++;
            // CA2: dentro del círculo de radio wanderRadius (antes un cuadrado: hasta √2·r).
            Vec2.Distance(slime.Position, spawnPos).ShouldBeLessThanOrEqualTo(slime.WanderRadius + (float)ai.ArriveToleranceTiles);
        }
        moved.ShouldBeTrue();
        pauses.ShouldNotBeEmpty();
        // Pausas de wanderPauseMinMs a wanderPauseMaxMs (± un tick). Si el punto nuevo cae a menos de la tolerancia de llegada, el
        // monstruo "llega" sin moverse y encadena otra pausa: por eso solo se exige el mínimo a todas y el rango a alguna.
        pauses.ShouldAllBe(t => t * GameConstants.TickMs >= ai.WanderPauseMinMs - GameConstants.TickMs);
        pauses.ShouldContain(t => t * GameConstants.TickMs <= ai.WanderPauseMaxMs + GameConstants.TickMs);

        // Respawn: reaparece respawnSec tras morir; el cadáver dura corpseLifetimeSec (HU-037 CA4), independientemente.
        var rules = db.Rules.Combat;
        var respawnMs = db.Monster("slime").RespawnSec * 1000;
        var corpseMs = (int)(rules.CorpseLifetimeSec * 1000);
        w.Combat.Death.Kill(slime, null, w.Map, w.Begin());
        w.Combat.Spawns.PendingCount(w.Map).ShouldBe(1);
        TickRunner.RunMs(w, respawnMs - 100);
        w.Map.Monsters.Values.Count(m => m.IsAlive).ShouldBe(2);
        TickRunner.RunMs(w, 200);
        w.Map.Monsters.Values.Count(m => m.IsAlive).ShouldBe(3); // reapareció
        w.Combat.Spawns.PendingCount(w.Map).ShouldBe(0);
        w.Map.Monsters.ContainsKey(slime.Id.Value).ShouldBe(respawnMs + 100 < corpseMs); // el cadáver sigue si aún no venció
        TickRunner.RunMs(w, Math.Max(0, corpseMs - respawnMs) + 200);
        w.Map.Monsters.ContainsKey(slime.Id.Value).ShouldBeFalse();
        w.Map.Monsters.Count.ShouldBe(3);
    }

    [Fact]
    public void Pathfinder_AvoidsWalls_NoCornerCutting_FailsOverLimit()
    {
        var grid = new CollisionGrid(20, 20);
        for (var y = 2; y <= 8; y++) grid.SetSolid(10, y);
        var path = new List<Vec2>();
        Pathfinder.FindPath(grid, new Vec2(5.5f, 5.5f), new Vec2(15.5f, 5.5f), path).ShouldBeTrue();
        path.ShouldAllBe(p => !grid.IsSolidAt(p.X, p.Y));
        path.Last().ShouldBe(new Vec2(15.5f, 5.5f));
        // Límite de nodos → false
        Pathfinder.FindPath(grid, new Vec2(5.5f, 5.5f), new Vec2(15.5f, 5.5f), path, maxNodes: 5).ShouldBeFalse();
    }
}
