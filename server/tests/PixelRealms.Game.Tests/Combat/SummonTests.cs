using System.Text.Json.Nodes;
using PixelRealms.Content;
using PixelRealms.Game.Ai;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Progression;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>
/// HU-116: invocaciones. Ningún monstruo del contenido real invoca todavía (el Árbol Podrido, HU-117): estos tests cargan una copia
/// con un hechizo de prueba que invoca 3 slimes y una instancia que admite 5.
/// </summary>
public sealed class SummonTests
{
    private const string SummonSpell = "test_call_slimes";

    private static readonly Lazy<ContentDb> Db = new(() =>
    {
        using var tmp = new TempContent();
        tmp.Patch("spells.json", root => root["spells"]!.AsArray().Add(JsonNode.Parse($$"""
            { "id": "{{SummonSpell}}", "name": "Llamada", "source": "monster", "school": "magic", "castMs": 0, "cooldownMs": 1000,
              "range": 0, "targeting": "self", "effects": [{ "type": "summon", "monsterId": "slime", "count": 3 }],
              "icon": "spells/rally", "description": "Prueba." }
            """)));
        tmp.PatchPointer("rules.json", "/limits/maxSummonsPerInstance", "5");
        return ContentLoader.LoadOrThrow(tmp.Path);
    });

    private static TestWorld Arena() => new WorldBuilder(Db.Value).WithMap(40, 40)
        .WithPlayer("Ana", "warrior", 6, (10, 10))
        .WithMonster("foreman_grask", (14, 10), wanderRadius: 0)
        .WithMonster("rubble_golem", (30, 30), wanderRadius: 0)
        .BuildWithCombat();

    private static List<Monster> SummonsOf(TestWorld w, Monster invoker) =>
        w.Map.Monsters.Values.Where(m => m.SummonedBy == invoker.Id).ToList();

    private static void Call(TestWorld w, Monster invoker)
    {
        invoker.Combat.CooldownEndsAtMs.Clear();
        w.Combat.Casts.TryBeginCast(invoker, w.Content.Spell(SummonSpell), null, null, w.Map, w.Begin()).ShouldBeNull();
    }

    [Fact]
    public void Summons_AppearBesideTheInvoker_WithItsThreat_AndChase() // CA1
    {
        var w = Arena();
        var boss = w.Monster("foreman_grask");
        var ana = w.Player("Ana");
        boss.Threat.Add(ana.Id, 500);
        Call(w, boss);
        var summons = SummonsOf(w, boss);
        summons.Count.ShouldBe(3);
        foreach (var s in summons)
        {
            Vec2.Distance(s.Position, boss.Position).ShouldBeLessThanOrEqualTo(2.6f);
            w.Map.Collision.IsSolidAt(s.Position.X, s.Position.Y).ShouldBeFalse();
            s.Threat.Of(ana.Id).ShouldBe(500);
            s.Brain.Spawn.ShouldBeNull(); // no reaparece
        }
        TickRunner.Run(w, 2);
        SummonsOf(w, boss).ShouldAllBe(s => s.Brain.State != AiState.Idle);
    }

    [Fact]
    public void Summons_RespectThePerInvokerAndPerInstanceCaps() // CA3: 4 por invocador (rules) y 5 por instancia (esta copia)
    {
        var w = Arena();
        var boss = w.Monster("foreman_grask");
        var golem = w.Monster("rubble_golem");
        Call(w, boss);
        Call(w, boss);
        SummonsOf(w, boss).Count.ShouldBe(4);
        Call(w, golem);
        SummonsOf(w, golem).Count.ShouldBe(1); // la instancia ya tenía 4 de 5
    }

    [Fact]
    public void Summons_GoWhenTheInvokerDiesEvadesOrResets() // CA2
    {
        var w = Arena();
        var ana = w.Player("Ana");
        var boss = w.Monster("foreman_grask");
        var golem = w.Monster("rubble_golem");
        boss.Threat.Add(ana.Id, 10);
        golem.Threat.Add(ana.Id, 10);
        Call(w, boss);
        Call(w, golem);
        w.Combat.Death.Kill(boss, ana, w.Map, w.Begin());
        golem.Position = new Vec2(20, 30); // lejos de su sitio: tarda en volver
        golem.Combat.Evading = true;
        TickRunner.Run(w, 1);
        w.Map.Monsters.Values.ShouldNotContain(m => m.SummonedBy != null);
    }

    [Fact]
    public void Summons_GoWhenTheInvokerHasNobodyLeftToFight() // CA2: se reinicia sin moverse (p. ej. murieron todos)
    {
        var w = Arena();
        var boss = w.Monster("foreman_grask");
        boss.Threat.Add(w.Player("Ana").Id, 10);
        Call(w, boss);
        TickRunner.Run(w, 1);
        SummonsOf(w, boss).Count.ShouldBe(3);
        boss.Threat.Clear();
        TickRunner.Run(w, 1);
        SummonsOf(w, boss).ShouldBeEmpty();
    }

    [Fact]
    public void ASummonRemovedAlive_LosesItsAuras() // revisión de autoridad: sus auras vuelven a la reserva
    {
        var w = Arena();
        var boss = w.Monster("foreman_grask");
        var ana = w.Player("Ana");
        boss.Threat.Add(ana.Id, 10);
        Call(w, boss);
        var slime = SummonsOf(w, boss)[0];
        w.Combat.Auras.Apply(slime, w.Content.Aura("mage_chill"), ana, w.Map, w.Begin(), "mage_frostbolt").ShouldNotBeNull();
        slime.Auras.All.Count.ShouldBe(1);
        w.Combat.Death.Kill(boss, ana, w.Map, w.Begin()); // la invocación sigue viva: se quita con su invocador
        TickRunner.Run(w, 1).OfType<AuraRemovedEvent>().ShouldContain(e => e.Target == slime);
        slime.Auras.All.ShouldBeEmpty();
    }

    [Fact]
    public void ASummonKilled_GivesNoXpNorLoot_AndLeavesNoCorpse() // CA4
    {
        var w = Arena();
        var boss = w.Monster("foreman_grask");
        var ana = w.Player("Ana");
        boss.Threat.Add(ana.Id, 10);
        Call(w, boss);
        var slime = SummonsOf(w, boss)[0];
        slime.TaggedBy = ana.Id;
        var ctx = w.Begin();
        w.Combat.Death.Kill(slime, ana, w.Map, ctx);
        var events = TickRunner.Run(w, 1);
        events.OfType<XpGainedEvent>().ShouldBeEmpty();
        events.OfType<LootAvailableEvent>().ShouldBeEmpty();
        w.Combat.Loot.Get(w.Map, slime.Id).ShouldBeNull();
        w.Map.Find(slime.Id).ShouldBeNull();
    }
}
