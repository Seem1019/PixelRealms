using System.Text.Json.Nodes;
using PixelRealms.Content;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>
/// HU-100: áreas duraderas. Ningún hechizo del contenido real las usa todavía (las Esporas del Árbol Podrido, HU-117): estos tests
/// cargan una copia donde Estallido de llamas deja el área 2 s (4 pulsos de 500 ms) y la instancia admite 3 áreas.
/// </summary>
[Collection(nameof(LoadScenarioIsolation))]
public sealed class PersistentAreaTests
{
    private static readonly Lazy<ContentDb> Db = new(() =>
    {
        using var tmp = new TempContent();
        tmp.Patch("spells.json", root =>
            root["spells"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "mage_flame_burst")!["areaDurationMs"] = 2000);
        tmp.PatchPointer("rules.json", "/limits/maxAreasPerInstance", "3");
        return ContentLoader.LoadOrThrow(tmp.Path);
    });

    private static TestWorld Arena(params (string Name, float X)[] mages)
    {
        var b = new WorldBuilder(Db.Value).WithMap(60, 60);
        foreach (var (name, x) in mages) b.WithPlayer(name, "mage", 5, (x, 10));
        var w = b.WithMonster("boar", (10, 14), wanderRadius: 0).WithMonster("wolf", (30, 14), wanderRadius: 0).BuildWithCombat();
        w.Monster("boar").BaseSpeed = 0; // quieto: si persigue al mago, sale del área
        return w;
    }

    /// <summary>Lanza Estallido de llamas (casteo 1,5 s) en `at` y avanza hasta que termina el casteo; `durationMs` alarga el área.</summary>
    private static List<IGameEvent> Burst(TestWorld w, string caster, Vec2 at, int? durationMs = null)
    {
        var p = w.Player(caster);
        p.Resource = p.MaxResource;
        p.Combat.CooldownEndsAtMs.Clear(); // varias áreas seguidas sin esperar la recarga
        var spell = w.Content.Spell("mage_flame_burst");
        if (durationMs is { } d) spell = spell with { AreaDurationMs = d };
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(p, spell, null, at, w.Map, ctx).ShouldBeNull();
        return [.. ctx.Events, .. TickRunner.RunMs(w, w.Content.Spell("mage_flame_burst").CastMs + 50)];
    }

    private static int Hits(IEnumerable<IGameEvent> events, string target) =>
        events.OfType<CombatHitEvent>().Count(e => e.SpellId == "mage_flame_burst" && e.Target.Name == target);

    [Fact]
    public void Area_PulsesEveryInterval_FirstOneAfterAnInterval_ThenGoes() // CA2
    {
        var w = Arena(("Ana", 10));
        var boar = w.Monster("boar");
        boar.Hp = boar.MaxHp = 100000; // que aguante los cuatro pulsos
        var cast = Burst(w, "Ana", new Vec2(10, 14));
        var spawned = cast.OfType<PersistentAreaSpawnedEvent>().Single();
        Hits(cast, boar.Name).ShouldBe(0, "el primer pulso llega un intervalo después de aparecer: da tiempo a salir");
        w.Map.PersistentAreas.Count.ShouldBe(1);

        var later = TickRunner.RunMs(w, 2100);
        Hits(later, boar.Name).ShouldBe(4); // 2 000 ms / 500 ms
        later.OfType<PersistentAreaDespawnedEvent>().Single().AreaId.ShouldBe(spawned.Area.Id);
        w.Map.PersistentAreas.ShouldBeEmpty();
    }

    [Fact]
    public void Area_OnlyHitsWhoIsInsideWhenItPulses()
    {
        var w = Arena(("Ana", 10));
        var boar = w.Monster("boar");
        boar.Hp = boar.MaxHp = 100000;
        Burst(w, "Ana", new Vec2(10, 14));
        boar.Position = new Vec2(10, 20); // sale antes del primer pulso (sin pasar su correa, que lo devolvería a su sitio)
        Hits(TickRunner.RunMs(w, 2100), boar.Name).ShouldBe(0);
    }

    [Fact]
    public void AThirdAreaFromTheSameCaster_ReplacesTheOldest() // CA1
    {
        var w = Arena(("Ana", 10));
        var first = Burst(w, "Ana", new Vec2(10, 14)).OfType<PersistentAreaSpawnedEvent>().Single().Area.Id;
        Burst(w, "Ana", new Vec2(12, 14));
        var third = Burst(w, "Ana", new Vec2(14, 14));
        third.OfType<PersistentAreaDespawnedEvent>().Single().AreaId.ShouldBe(first);
        w.Map.PersistentAreas.Count.ShouldBe(2);
    }

    [Fact]
    public void TheInstanceCap_CountsLastingAreas() // CA1: maxAreasPerInstance (3 en esta copia)
    {
        var w = Arena(("Ana", 10), ("Bob", 20));
        Burst(w, "Ana", new Vec2(10, 14), durationMs: 60000); // que no caduquen mientras se lanzan las demás
        Burst(w, "Ana", new Vec2(12, 14), durationMs: 60000);
        Burst(w, "Bob", new Vec2(20, 14), durationMs: 60000);
        var bob = w.Player("Bob");
        bob.Resource = bob.MaxResource;
        bob.Combat.CooldownEndsAtMs.Clear();
        w.Combat.Casts.TryBeginCast(bob, w.Content.Spell("mage_flame_burst"), null, new Vec2(22, 14), w.Map, w.Begin()).ShouldBe(CastErrors.AreaLimit);
    }

    [Fact]
    public void TheCastersAreasGo_WhenTheCasterDies()
    {
        var w = Arena(("Ana", 10));
        var id = Burst(w, "Ana", new Vec2(10, 14)).OfType<PersistentAreaSpawnedEvent>().Single().Area.Id;
        w.Player("Ana").Hp = 0;
        w.Combat.Damage.Deal(w.Monster("wolf"), w.Player("Ana"), 1, PixelRealms.Content.Defs.School.Physical, false, null, w.Map, w.Begin());
        TickRunner.RunMs(w, 600).OfType<PersistentAreaDespawnedEvent>().Single().AreaId.ShouldBe(id);
    }

    [Fact]
    public void TheAreasGoAtTheMomentTheCasterDies_EvenIfItRespawnsBeforeThePulse() // revisión de autoridad
    {
        var w = Arena(("Ana", 10));
        var ana = w.Player("Ana");
        var id = Burst(w, "Ana", new Vec2(10, 14)).OfType<PersistentAreaSpawnedEvent>().Single().Area.Id;
        var ctx = w.Begin();
        w.Combat.Death.Kill(ana, w.Monster("wolf"), w.Map, ctx);
        ctx.Events.OfType<PersistentAreaDespawnedEvent>().Single().AreaId.ShouldBe(id);
        w.Combat.Death.Respawn(ana, w.Map, w.Begin()).ShouldBeTrue();
        w.Map.PersistentAreas.ShouldBeEmpty();
    }

    [Fact]
    public void SelfEffects_ApplyOnceWhenCast_NotOnEveryPulse() // revisión de autoridad
    {
        var w = Arena(("Ana", 10));
        var ana = w.Player("Ana");
        ana.Resource = ana.MaxResource;
        var shield = new PixelRealms.Content.Defs.EffectDef { Type = PixelRealms.Content.Defs.EffectType.ApplyAura, AuraId = "priest_power_shield_speed", ApplyTo = PixelRealms.Content.Defs.ApplyTo.Self };
        var spell = w.Content.Spell("mage_flame_burst") with { Effects = [.. w.Content.Spell("mage_flame_burst").Effects, shield] };
        w.Combat.Casts.TryBeginCast(ana, spell, null, new Vec2(10, 14), w.Map, w.Begin()).ShouldBeNull();
        var events = TickRunner.RunMs(w, spell.CastMs + 2100);
        events.OfType<AuraAppliedEvent>().Count(e => e.Target == ana && e.Aura.AuraId == "priest_power_shield_speed").ShouldBe(1);
    }

    [Fact]
    public void AMonstersAreasGo_WhenItResetsWithoutEvading() // revisión de autoridad: tras un wipe, el jefe vuelve a estar quieto
    {
        var w = Arena(("Ana", 10));
        var boar = w.Monster("boar");
        boar.Threat.Add(w.Player("Ana").Id, 10);
        var spores = w.Content.Spell("foreman_slam") with { AreaDurationMs = 5000, CastMs = 0 };
        w.Combat.Casts.TryBeginCast(boar, spores, null, new Vec2(10, 12), w.Map, w.Begin()).ShouldBeNull();
        w.Map.PersistentAreas.Count.ShouldBe(1);
        boar.Threat.Clear();
        TickRunner.Run(w, 1).OfType<PersistentAreaDespawnedEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void AtTheInstanceCap_ACasterCanStillReplaceItsOwnArea() // revisión de autoridad: no suma
    {
        var w = Arena(("Ana", 10), ("Bob", 20));
        Burst(w, "Ana", new Vec2(10, 14), durationMs: 60000);
        Burst(w, "Ana", new Vec2(12, 14), durationMs: 60000);
        Burst(w, "Bob", new Vec2(20, 14), durationMs: 60000);
        Burst(w, "Ana", new Vec2(14, 14), durationMs: 60000).OfType<PersistentAreaDespawnedEvent>().ShouldHaveSingleItem(); // sustituye
        w.Map.PersistentAreas.Count.ShouldBe(3);
    }

    [Fact]
    public void AnAreaWithNobodyInside_AllocatesNothingPerTick() // CA3: reserva fija, presupuesto de HU-088
    {
        var w = Arena(("Ana", 10));
        w.Monster("boar").Position = new Vec2(10, 40);
        w.Monster("wolf").Position = new Vec2(40, 40);
        var p = w.Player("Ana");
        p.Resource = p.MaxResource;
        var long2 = w.Content.Spell("mage_flame_burst") with { AreaDurationMs = 60000 };
        w.Combat.Casts.TryBeginCast(p, long2, null, new Vec2(10, 14), w.Map, w.Begin()).ShouldBeNull();
        TickRunner.Run(w, 200); // casteo, aparición y calentamiento

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) w.Simulation.RunTick();
        var perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 200.0;

        perTick.ShouldBe(0, "bytes por tick con un área duradera vacía");
        w.Map.PersistentAreas.Count.ShouldBe(1);
    }
}
