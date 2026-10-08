using PixelRealms.Content.Defs;
using PixelRealms.Game.Ai;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>
/// HU-117: el Árbol Podrido del contenido real (inmóvil, Raíces, Esporas, Retoños bajo el 50 %, inmunidades y botín) y lo que
/// su IA necesita: monstruos inmóviles que castigan a distancia y se reinician en su sitio, e invocaciones que van a por quien
/// menos amenaza tiene (`threatTarget: lowest`).
/// </summary>
public sealed class RottenTreeTests
{
    private const string Roots = "rotten_tree_roots";
    private const string Spores = "rotten_tree_spores";
    private const string Saplings = "rotten_tree_saplings";

    /// <summary>Sala abierta: el árbol en (20, 10); Ana (Guerrero) a su lado, Bob (Mago) y Cid (Sacerdote) a distancia.</summary>
    private static TestWorld Room() => new WorldBuilder().WithMap(40, 40)
        .WithPlayer("Ana", "warrior", 9, (20, 11.5f)).WithPlayer("Bob", "mage", 9, (26, 14)).WithPlayer("Cid", "priest", 9, (14, 16))
        .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();

    private static void Tough(params Player[] players)
    {
        foreach (var p in players) p.Hp = p.MaxHp = 100_000; // que aguanten el básico del jefe mientras se mide otra cosa
    }

    private static void Hold(Monster tree, params string[] spellIds)
    {
        foreach (var id in spellIds) tree.Combat.CooldownEndsAtMs[id] = long.MaxValue;
    }

    [Fact]
    public void IsImmuneToStunRootAndSlow_ButItsSaplingsAreNot() // CA4: rules.combat.bossImmuneToAuraKinds
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Bob", "rogue", 9, (18, 10))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).WithMonster("rotten_sapling", (24, 10), wanderRadius: 0).BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var bob = w.Player("Bob");
        foreach (var aura in new[] { "rogue_gouge_stun", "mage_frost_nova_root", "mage_chill" })
        {
            var ctx = w.Begin();
            w.Combat.Auras.Apply(tree, w.Content.Aura(aura), bob, w.Map, ctx).ShouldBeNull(aura);
            ctx.Events.OfType<CombatHitEvent>().Single().Kind.ShouldBe(HitKinds.Immune);
        }
        tree.Auras.Count.ShouldBe(0);
        w.Combat.Auras.Apply(tree, w.Content.Aura("rogue_poison"), bob, w.Map, w.Begin()).ShouldNotBeNull(); // un DoT sí entra
        w.Combat.Auras.Apply(w.Monster("rotten_sapling"), w.Content.Aura("mage_frost_nova_root"), bob, w.Map, w.Begin()).ShouldNotBeNull();
    }

    [Fact]
    public void StaysPut_AndCastsRootsAndSporesOnWhoeverIsNotItsTarget() // CA1
    {
        var w = Room();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid");
        Tough(ana, bob, cid);
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(bob.Id, 10); tree.Threat.Add(cid.Id, 10);
        var events = TickRunner.RunMs(w, 8000); // no pega mientras castea: Raíces y Esporas (1,5 s cada una) y luego el básico (2,6 s)
        tree.Position.ShouldBe(tree.SpawnPosition);
        var casts = events.OfType<CastStartedEvent>().Where(e => e.Caster == tree).ToList();
        casts.ShouldContain(e => e.Spell.Id == Roots);
        casts.ShouldContain(e => e.Spell.Id == Spores);
        casts.ShouldNotContain(e => e.Spell.Id == Saplings); // vida > 50 %
        // Marcas en el suelo donde estaba alguien que no es el tanque, y el básico para el tanque.
        foreach (var c in casts.Where(e => e.Spell.Id is Roots or Spores))
            new[] { bob.Position, cid.Position }.ShouldContain(c.TargetPos!.Value);
        events.OfType<CombatHitEvent>().ShouldContain(e => e.Source == tree && e.Target == ana && e.SpellId == null);
        events.OfType<CombatHitEvent>().ShouldNotContain(e => e.Source == tree && e.Target == ana && e.SpellId != null);
    }

    [Fact]
    public void Immobile_StillCastsOnThoseInReach_WhenItsTargetIsOutOfReach() // CA1 (nota): alejarse no es seguro
    {
        // Ana tiene la amenaza pero está a 20 casillas, fuera de su básico (13,5) y tras un muro (sus hechizos piden vista); Bob, a 7:
        // mientras pegue a alguien con el básico, no se reinicia aunque pase el plazo de rules.ai.
        var b = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (20, 30)).WithPlayer("Bob", "mage", 9, (26, 14))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0);
        for (var x = 17; x <= 23; x++) b.WithWall(x, 20);
        var w = b.BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var bob = w.Player("Bob");
        Tough(ana, bob);
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(bob.Id, 10);
        tree.Hp = tree.MaxHp / 2 + 10; // herido, pero sin Retoños
        var events = TickRunner.RunMs(w, w.Content.Rules.Ai.ImmobileOutOfReachResetMs + 2000);
        tree.Position.ShouldBe(tree.SpawnPosition);
        tree.Combat.Evading.ShouldBeFalse(); // sin persecución ni A*: no evade por no llegar
        events.OfType<CombatHitEvent>().ShouldNotContain(e => e.Target == tree && e.Kind == HitKinds.Immune);
        tree.Hp.ShouldBe(tree.MaxHp / 2 + 10);
        tree.Threat.Current.ShouldBe(ana.Id); // por amenaza sigue yendo a por Ana...
        tree.Combat.TargetId.ShouldBe(bob.Id); // ...pero pega a quien alcanza
        var marks = events.OfType<CastStartedEvent>().Where(e => e.Caster == tree && e.Spell.Id is Roots or Spores).ToList();
        marks.ShouldNotBeEmpty();
        marks.ShouldAllBe(e => e.TargetPos == bob.Position);
        events.OfType<CombatHitEvent>().ShouldContain(e => e.Source == tree && e.Target == bob && e.SpellId == null);
        events.OfType<CombatHitEvent>().ShouldNotContain(e => e.Source == tree && e.Target == ana);
    }

    [Fact]
    public void Immobile_HitsTheTopThreatItCanReach_WhenItsTargetStepsOut() // revisión de autoridad: el tanque no se aparta gratis
    {
        // Ana (1000) a 14 casillas, fuera de su básico (13,5); Bob (300) a 6 y Cid (10) a 8: el básico va a Bob, el de más amenaza
        // de los que alcanza, y vuelve a Ana en cuanto ella entra.
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (20, 24)).WithPlayer("Bob", "rogue", 9, (26, 10))
            .WithPlayer("Cid", "priest", 9, (12, 10)).WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid");
        Tough(ana, bob, cid);
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(bob.Id, 300); tree.Threat.Add(cid.Id, 10);
        Hold(tree, Roots, Spores);
        var basics = TickRunner.RunMs(w, 6000).OfType<CombatHitEvent>().Where(e => e.Source == tree && e.SpellId == null).ToList();
        basics.ShouldNotBeEmpty();
        basics.ShouldAllBe(e => e.Target == bob);
        tree.Threat.Current.ShouldBe(ana.Id);
        ana.Position = new Vec2(20, 18); // vuelve a su alcance
        TickRunner.RunMs(w, 6000).OfType<CombatHitEvent>().Where(e => e.Source == tree && e.SpellId == null)
            .ShouldContain(e => e.Target == ana);
    }

    [Fact]
    public void Immobile_NoDeadlock_WhenItsTargetIsJustOutOfReachAndTheOtherFarAway() // revisión de autoridad
    {
        // Ana (su objetivo) a 14 casillas con vista y Bob a 20 tras un muro: nadie a tiro del básico, y Raíces y Esporas, que no
        // van a Bob porque no le llegan, caen en Ana en vez de no hacer nada (antes el reloj de reinicio no corría y el árbol se
        // quedaba quieto).
        var b = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (20, 24)).WithPlayer("Bob", "mage", 9, (20, 30))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0);
        for (var x = 17; x <= 23; x++) b.WithWall(x, 27);
        var w = b.BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var bob = w.Player("Bob");
        Tough(ana, bob);
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(bob.Id, 10);
        tree.Hp = tree.MaxHp * 3 / 4;
        var marks = TickRunner.RunMs(w, 4000).OfType<CastStartedEvent>().Where(e => e.Caster == tree && e.Spell.Id is Roots or Spores).ToList();
        marks.ShouldNotBeEmpty();
        marks.ShouldAllBe(e => e.TargetPos == ana.Position);
    }

    [Fact]
    public void RootsAndSpores_GoToSomeoneWhoIsNotItsTarget_EvenWithMoreThreat() // revisión de autoridad: no al objetivo, no al Top
    {
        // Ana es su objetivo (1000) y Bob la supera sin llegar al 130 % (1200): las marcas caen en Bob o en Cid, nunca en Ana.
        var w = Room();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid");
        Tough(ana, bob, cid);
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(bob.Id, 1200); tree.Threat.Add(cid.Id, 10);
        var marks = TickRunner.RunMs(w, 4000).OfType<CastStartedEvent>().Where(e => e.Caster == tree && e.Spell.Id is Roots or Spores).ToList();
        tree.Threat.Current.ShouldBe(ana.Id);
        marks.ShouldNotBeEmpty();
        marks.ShouldAllBe(e => e.TargetPos == bob.Position || e.TargetPos == cid.Position);
    }

    [Fact]
    public void ForgetLeaving_DropsItsThreatTagsAndProjectiles_AlsoFromTheSaplings() // cambio de mapa o salida del mundo
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 9, (20, 18)).WithPlayer("Cid", "priest", 9, (14, 16))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var cid = w.Player("Cid");
        Tough(ana, cid);
        tree.Threat.Add(ana.Id, 100); tree.Threat.Add(cid.Id, 50);
        Hold(tree, Roots, Spores);
        tree.Hp = tree.MaxHp / 2 - 1;
        TickRunner.RunMs(w, w.Content.Spell(Saplings).CastMs + 100);
        var summons = w.Map.Monsters.Values.Where(m => m.SummonedBy == tree.Id).ToList();
        summons.ShouldAllBe(s => s.Threat.Contains(ana.Id));
        tree.TaggedBy = ana.Id;
        var fireball = w.Content.Spell("mage_fireball");
        w.Combat.Casts.TryBeginCast(ana, fireball, tree.Id, null, w.Map, w.Begin()).ShouldBeNull();
        TickRunner.RunMs(w, fireball.CastMs);
        w.Combat.Casts.PendingImpacts(w.Map).ShouldBeGreaterThan(0); // la bola de fuego va en vuelo
        w.Combat.ForgetLeaving(ana, w.Map);
        w.Combat.Casts.PendingImpacts(w.Map).ShouldBe(0);
        tree.Threat.Contains(ana.Id).ShouldBeFalse();
        tree.TaggedBy.ShouldBeNull();
        summons.ShouldAllBe(s => !s.Threat.Contains(ana.Id));
    }

    /// <summary>Árbol herido con solo Ana en su tabla, en `at`; `walls` tapa la vista. Devuelve el mundo listo para correr.</summary>
    private static TestWorld Alone((float x, float y) at, params (int x, int y)[] walls)
    {
        var b = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, at).WithMonster("rotten_tree", (20, 10), wanderRadius: 0);
        foreach (var (x, y) in walls) b.WithWall(x, y);
        var w = b.BuildWithCombat();
        Tough(w.Player("Ana"));
        w.Monster("rotten_tree").Threat.Add(w.Player("Ana").Id, 50);
        w.Monster("rotten_tree").Hp = w.Monster("rotten_tree").MaxHp / 3;
        return w;
    }

    private static readonly (int, int)[] WallBetween = [.. Enumerable.Range(14, 13).Select(x => (x, 13))];

    [Theory] // revisión de autoridad: sin zona muerta entre su básico y 2 × leashRange, ni tras una columna
    [InlineData(20f, 30f, false)]   // a 20 casillas: fuera de todo su alcance y dentro de 2 × leashRange (24)
    [InlineData(20f, 24.5f, false)] // a 14,5: le llegan Raíces y Esporas (14 + 1,5), que se esquivan, pero no su básico (13,5)
    [InlineData(20f, 16f, true)]    // a 6 casillas, tras un muro que le tapa la vista: le pegan sin que pueda contestar
    public void Immobile_ResetsInPlace_WhenItCannotStrikeAnyoneWithItsBasic_ForAWhile(float x, float y, bool behindWall)
    {
        var w = behindWall ? Alone((x, y), WallBetween) : Alone((x, y));
        var tree = w.Monster("rotten_tree");
        var resetMs = w.Content.Rules.Ai.ImmobileOutOfReachResetMs;
        TickRunner.RunMs(w, resetMs - 500);
        tree.Hp.ShouldBe(tree.MaxHp / 3, "antes del plazo sigue herido y con su amenaza");
        tree.Threat.Count.ShouldBe(1);
        TickRunner.RunMs(w, 1000);
        tree.Hp.ShouldBe(tree.MaxHp);
        tree.Threat.Count.ShouldBe(0);
        tree.Combat.Evading.ShouldBeFalse();
        tree.Brain.State.ShouldBe(AiState.Idle);
    }

    [Fact]
    public void Immobile_ItsOwnLongCasts_NeverResetItWhileTheTankIsInReach() // revisión de autoridad: casteos que pausan su básico
    {
        var w = Alone((20, 11.5f)); // Ana pegada; herido al tercio: Retoños (2 s), Raíces y Esporas (1,5 s) se encadenan
        var tree = w.Monster("rotten_tree");
        var events = TickRunner.RunMs(w, 30_000);
        events.OfType<CastStartedEvent>().Count(e => e.Caster == tree).ShouldBeGreaterThan(4);
        tree.Hp.ShouldBe(tree.MaxHp / 3);
        tree.Threat.Count.ShouldBe(1);
        events.OfType<CombatHitEvent>().ShouldNotContain(e => e.Target == tree && e.Kind == HitKinds.Immune);
    }

    [Fact]
    public void Immobile_PeekingInDoesNotStopTheReset_OnlyStayingInReachOfItsBasicDoes() // revisión de autoridad: sin pausas gratis
    {
        // El tiempo sin poder pegar con el básico se acumula y el que pasa pegando lo descuenta al mismo ritmo: asomarse dos ticks
        // no frena el reinicio (antes, sí: un solo jugador en el anillo de sus hechizos pausaba el combate sin límite).
        var w = Alone((20, 30));
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana");
        var resetMs = w.Content.Rules.Ai.ImmobileOutOfReachResetMs;
        var inReach = new Vec2(20, 20); // a 10 casillas y a la vista
        var outOfReach = new Vec2(20, 30);
        TickRunner.RunMs(w, resetMs - 1000);
        ana.Position = inReach;
        TickRunner.Run(w, 2);
        ana.Position = outOfReach;
        TickRunner.RunMs(w, 1200);
        tree.Hp.ShouldBe(tree.MaxHp, "asomarse un instante no cuenta");

        var w2 = Alone((20, 30));
        var tree2 = w2.Monster("rotten_tree"); var ana2 = w2.Player("Ana");
        TickRunner.RunMs(w2, resetMs - 1000);
        ana2.Position = inReach;
        TickRunner.RunMs(w2, resetMs - 1000); // tanto tiempo dentro como fuera: el contador vuelve a cero
        ana2.Position = outOfReach;
        TickRunner.RunMs(w2, resetMs - 1000);
        tree2.Hp.ShouldBe(tree2.MaxHp / 3);
        TickRunner.RunMs(w2, 1200);
        tree2.Hp.ShouldBe(tree2.MaxHp);
    }

    [Fact]
    public void Saplings_SkipAHealerTheyCannotReach_InsteadOfLeavingTheFight() // revisión de autoridad: `lowest` y el A*
    {
        // Cid cura tras una franja sólida que no tapa la vista (agua): el retoño no puede llegar hasta él. En vez de evadir (y borrarse
        // por ser una invocación), lo quita de su tabla y va a por el siguiente.
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (20, 11.5f)).WithPlayer("Cid", "priest", 9, (12.5f, 16.5f))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();
        for (var i = 11; i <= 14; i++) { w.Map.Data.Collision.SetSolid(i, 15); w.Map.Data.Collision.SetSolid(i, 18); }
        for (var j = 16; j <= 17; j++) { w.Map.Data.Collision.SetSolid(11, j); w.Map.Data.Collision.SetSolid(14, j); }
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var cid = w.Player("Cid");
        Tough(ana, cid);
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(cid.Id, 20);
        Hold(tree, Roots, Spores);
        tree.Hp = tree.MaxHp / 2 - 1;
        TickRunner.RunMs(w, w.Content.Spell(Saplings).CastMs + 2000);
        var summons = w.Map.Monsters.Values.Where(m => m.SummonedBy == tree.Id).ToList();
        summons.Count.ShouldBe(2);
        foreach (var s in summons)
        {
            s.Combat.Evading.ShouldBeFalse();
            s.Threat.Contains(cid.Id).ShouldBeFalse();
            s.Combat.TargetId.ShouldBe(ana.Id);
        }
    }

    [Fact]
    public void Immobile_TheResetTakesItsSaplingsAndSporesAway()
    {
        var w = Alone((20, 11.5f));
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana");
        Hold(tree, Roots);
        tree.Hp = tree.MaxHp / 2 - 1;
        var events = TickRunner.RunMs(w, w.Content.Spell(Saplings).CastMs + w.Content.Spell(Spores).CastMs + 200);
        w.Map.Monsters.Values.Count(m => m.SummonedBy == tree.Id).ShouldBe(2);
        var area = events.OfType<PersistentAreaSpawnedEvent>().Single().Area;
        ana.Position = new Vec2(20, 30); // fuera de su alcance (los retoños la siguen)
        TickRunner.RunMs(w, w.Content.Rules.Ai.ImmobileOutOfReachResetMs + 200);
        tree.Hp.ShouldBe(tree.MaxHp);
        w.Clock.NowMs.ShouldBeLessThan(area.ExpiresAtMs, "la nube se va con el reinicio, no por caducar");
        w.Map.PersistentAreas.ShouldNotContain(a => ReferenceEquals(a.Caster, tree));
        w.Map.Monsters.Values.ShouldNotContain(m => m.SummonedBy == tree.Id);
    }

    [Fact]
    public void TrapPlant_DoesNotResetBecauseItCannotWalkThere_OnlyWhenNobodyIsInReachForAWhile() // regresión de HU-109
    {
        // Ana encerrada a 6 casillas: sin camino ni vista. Antes el A* fallaba y la planta se curaba al instante.
        var b = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (20.5f, 16.5f)).WithMonster("trap_plant", (20.5f, 10.5f), wanderRadius: 0);
        for (var i = 18; i <= 23; i++) { b.WithWall(i, 14); b.WithWall(i, 19); }
        for (var j = 15; j <= 18; j++) { b.WithWall(18, j); b.WithWall(23, j); }
        var w = b.BuildWithCombat();
        var plant = w.Monster("trap_plant"); var ana = w.Player("Ana");
        Tough(ana);
        plant.Threat.Add(ana.Id, 10);
        plant.Hp = 50;
        TickRunner.RunMs(w, 2000);
        plant.Combat.Evading.ShouldBeFalse();
        plant.Hp.ShouldBe(50);
        plant.Position.ShouldBe(plant.SpawnPosition);
        TickRunner.RunMs(w, w.Content.Rules.Ai.ImmobileOutOfReachResetMs);
        plant.Hp.ShouldBe(plant.MaxHp);
        plant.Brain.State.ShouldBe(AiState.Idle);
    }

    [Fact]
    public void RootsAndSpores_NeverGoToSomeoneItCannotTarget() // revisión de autoridad: el mismo criterio que su objetivo
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (20, 11.5f)).WithPlayer("Eve", "mage", 9, (26, 14))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var eve = w.Player("Eve");
        Tough(ana, eve);
        w.Combat.Ai.CanBeAggroed = p => p.IsAlive && p != eve; // Eve, en duelo: los monstruos no la ven
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(eve.Id, 10);
        var events = TickRunner.RunMs(w, 4000);
        var marks = events.OfType<CastStartedEvent>().Where(e => e.Caster == tree && e.Spell.Id is Roots or Spores).ToList();
        marks.ShouldNotBeEmpty();
        marks.ShouldAllBe(e => e.TargetPos == ana.Position); // sin nadie más válido, a su objetivo
    }

    [Fact]
    public void Saplings_IgnoreDecoys_FarAwayGoneFromTheMapOrInADuel() // revisión de autoridad: `lowest` solo entre válidos
    {
        var w = new WorldBuilder().WithMap(80, 40)
            .WithPlayer("Ana", "warrior", 9, (20, 11.5f)).WithPlayer("Cid", "priest", 9, (14, 16))
            .WithPlayer("Dan", "rogue", 9, (52, 10)).WithPlayer("Eve", "mage", 9, (24, 14)).WithPlayer("Fay", "mage", 9, (22, 14))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();
        var tree = w.Monster("rotten_tree");
        var (ana, cid, dan, eve, fay) = (w.Player("Ana"), w.Player("Cid"), w.Player("Dan"), w.Player("Eve"), w.Player("Fay"));
        Tough(ana, cid, dan, eve, fay);
        w.Combat.Ai.CanBeAggroed = p => p.IsAlive && p != eve; // Eve, en duelo
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(cid.Id, 50);
        tree.Threat.Add(dan.Id, 0);  // señuelo a 32 casillas: más allá de la correa de los retoños
        tree.Threat.Add(eve.Id, 1);
        tree.Threat.Add(fay.Id, 2);
        w.Map.Remove(fay.Id);        // se fue del mapa sin limpiar su amenaza
        Hold(tree, Roots, Spores);
        tree.Hp = tree.MaxHp / 2 - 1;
        TickRunner.RunMs(w, w.Content.Spell(Saplings).CastMs + 100);
        var summons = w.Map.Monsters.Values.Where(m => m.SummonedBy == tree.Id).ToList();
        summons.Count.ShouldBe(2);
        foreach (var s in summons)
        {
            s.Threat.Contains(ana.Id).ShouldBeTrue();
            s.Threat.Contains(cid.Id).ShouldBeTrue();
            s.Threat.Contains(dan.Id).ShouldBeFalse();
            s.Threat.Contains(eve.Id).ShouldBeFalse();
            s.Threat.Contains(fay.Id).ShouldBeFalse();
        }
        TickRunner.RunMs(w, 1500);
        w.Map.Monsters.Values.Count(m => m.SummonedBy == tree.Id).ShouldBe(2); // ni evaden ni se borran
        foreach (var s in summons)
        {
            s.Combat.Evading.ShouldBeFalse();
            s.Combat.TargetId.ShouldBe(cid.Id);
        }
        tree.Combat.TargetId.ShouldBe(ana.Id);
    }

    [Fact]
    public void Immobile_ResetsInPlace_WhenNobodyIsLeftToFight() // salir de la sala no deja al jefe a medias
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (20, 12))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana");
        Tough(ana);
        tree.Threat.Add(ana.Id, 50);
        TickRunner.RunMs(w, 500);
        tree.Hp = tree.MaxHp / 3;
        ana.Position = new Vec2(20, 10 + (float)tree.Template.LeashRange * 2 + 2); // más allá de 2 × leashRange: lo olvida
        TickRunner.Run(w, 3);
        tree.Hp.ShouldBe(tree.MaxHp);
        tree.Threat.Count.ShouldBe(0);
        tree.Combat.Evading.ShouldBeFalse();
        tree.Brain.State.ShouldBe(AiState.Idle);
        tree.Position.ShouldBe(tree.SpawnPosition);
    }

    [Fact]
    public void Spores_LeaveACloudThatHurtsEveryPulse_UntilYouStepOut() // CA1: área duradera (HU-100)
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 9, (26, 10))
            .WithMonster("rotten_tree", (20, 10), wanderRadius: 0).BuildWithCombat();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana");
        Tough(ana);
        tree.Threat.Add(ana.Id, 10);
        Hold(tree, Roots, Saplings);
        var spores = w.Content.Spell(Spores);
        w.Combat.Casts.TryBeginCast(tree, spores, ana.Id, ana.Position, w.Map, w.Begin()).ShouldBeNull();
        var cast = TickRunner.RunMs(w, spores.CastMs + 50);
        var area = cast.OfType<PersistentAreaSpawnedEvent>().Single().Area;
        area.Spell.Id.ShouldBe(Spores);
        int Pulses(IEnumerable<IGameEvent> ev) => ev.OfType<CombatHitEvent>().Count(e => e.SpellId == Spores && e.Target == ana);
        Pulses(cast).ShouldBe(0, "el primer pulso llega medio segundo después: da tiempo a salir");
        var tickMs = w.Content.Rules.Limits.PersistentAreaTickMs;
        var inside = TickRunner.RunMs(w, 2 * tickMs);
        Pulses(inside).ShouldBe(2);
        inside.OfType<CombatHitEvent>().Where(e => e.SpellId == Spores).ShouldAllBe(e => e.School == School.Magic);
        ana.Position = new Vec2(26, 16); // fuera de la nube (radio 2)
        Pulses(TickRunner.RunMs(w, 4 * tickMs)).ShouldBe(0);
        TickRunner.RunMs(w, spores.AreaDurationMs).OfType<PersistentAreaDespawnedEvent>().ShouldContain(e => e.AreaId == area.Id);
    }

    [Fact]
    public void BelowHalf_CallsSaplings_ThatGoForWhoeverHasTheLeastThreat() // CA1: invocación (HU-116) + threatTarget lowest
    {
        var w = Room();
        var tree = w.Monster("rotten_tree"); var ana = w.Player("Ana"); var bob = w.Player("Bob"); var cid = w.Player("Cid");
        Tough(ana, bob, cid);
        tree.Threat.Add(ana.Id, 1000); tree.Threat.Add(bob.Id, 300); tree.Threat.Add(cid.Id, 20);
        Hold(tree, Roots, Spores);
        tree.Hp = tree.MaxHp / 2 - 1;
        var saplings = w.Content.Spell(Saplings);
        var events = TickRunner.RunMs(w, saplings.CastMs + 100);
        events.OfType<CastStartedEvent>().ShouldContain(e => e.Caster == tree && e.Spell.Id == Saplings);
        var summons = w.Map.Monsters.Values.Where(m => m.SummonedBy == tree.Id).ToList();
        summons.Count.ShouldBe(saplings.Effects.Single(e => e.Type == EffectType.Summon).Count);
        summons.ShouldAllBe(s => s.TemplateId == "rotten_sapling");
        var before = summons.Select(s => Vec2.Distance(s.Position, cid.Position)).ToList();
        TickRunner.RunMs(w, 1500);
        foreach (var (s, d) in summons.Zip(before))
        {
            s.Combat.TargetId.ShouldBe(cid.Id); // el sanador, no el tanque
            Vec2.Distance(s.Position, cid.Position).ShouldBeLessThan(d);
        }
        tree.Combat.TargetId.ShouldBe(ana.Id); // el árbol sigue con el tanque
    }

    [Fact]
    public void LowestThreat_GoesForTheLeast_SwitchesWithTheMirroredRule_AndTauntPins() // regla 110 %/130 % al revés
    {
        var rules = TestContent.Load().Rules.Combat;
        EntityId ana = new(1), bob = new(2), cid = new(3);
        var t = new ThreatTable();
        t.Add(ana, 1000); t.Add(bob, 100); t.Add(cid, 20);
        t.Reevaluate(0, rules.ThreatSwitchMelee, _ => true, ThreatTarget.Lowest).ShouldBe(cid);
        t.Add(cid, 89); // 109 < 110 % de 100 → sigue con Cid
        t.Reevaluate(0, rules.ThreatSwitchMelee, _ => true, ThreatTarget.Lowest).ShouldBe(cid);
        t.Add(cid, 1);  // 110 → cambia al que menos tiene
        t.Reevaluate(0, rules.ThreatSwitchMelee, _ => true, ThreatTarget.Lowest).ShouldBe(bob);
        t.Taunt(ana, 1000, 2000, rules.TauntThreatBonus); // Provocar lo fija aunque Ana sea quien más tiene
        t.Reevaluate(2999, rules.ThreatSwitchMelee, _ => true, ThreatTarget.Lowest).ShouldBe(ana);
        t.Reevaluate(3000, rules.ThreatSwitchMelee, _ => true, ThreatTarget.Lowest).ShouldBe(bob);
        t.Reevaluate(3000, rules.ThreatSwitchMelee, _ => true).ShouldBe(ana, "por defecto, el que más tiene");
    }

    [Fact]
    public void Threat_OnlyPicksValidCandidates_AndRemovingTheTauntedReleasesThePin() // revisión de autoridad
    {
        var rules = TestContent.Load().Rules.Combat;
        EntityId ana = new(1), bob = new(2), cid = new(3);
        var t = new ThreatTable();
        t.Add(ana, 1000); t.Add(bob, 100); t.Add(cid, 20);
        t.Reevaluate(0, rules.ThreatSwitchMelee, id => id != cid, ThreatTarget.Lowest).ShouldBe(bob); // Cid no es válido: no se elige
        t.Contains(cid).ShouldBeTrue(); // ni se borra: solo sale de la tabla el objetivo actual que deja de ser válido
        t.Reevaluate(0, rules.ThreatSwitchMelee, id => id != ana).ShouldBe(bob, "tampoco el que más tiene si no es válido");
        t.Taunt(ana, 1000, 2000, rules.TauntThreatBonus);
        t.Remove(ana); // p. ej. muere o se va: la fijación de Provocar se suelta con él
        t.TauntedUntilMs.ShouldBe(long.MinValue);
        t.Current.ShouldBeNull();
        t.Reevaluate(1500, rules.ThreatSwitchMelee, _ => true, ThreatTarget.Lowest).ShouldBe(cid);
    }

    [Fact]
    public void Death_DropsExactlyOneRareFromItsGroup_OnePiecePerRole_ToAPartyMember_AnnouncedGlobally() // CA3
    {
        var w = Room();
        var db = w.Content;
        var tree = w.Monster("rotten_tree");
        var table = db.LootTable(tree.Template.LootTableId);
        var group = table.Groups.Single();
        group.Rolls.ShouldBe(1);
        var groupIds = group.Entries.Select(e => e.ItemId).ToHashSet();
        // Una pieza por rol: cada clase tiene en el grupo una pieza con afinidad alta para ella, y no sobra ninguna.
        groupIds.Count.ShouldBe(db.Classes.Count);
        foreach (var cls in db.Classes)
            groupIds.ShouldContain(id => db.Rules.Affinity.MultiplierFor(cls.Id, db.Item(id).AffinityType) == 1.0, cls.Id);
        var rng = new SeededRng(117);
        for (var i = 0; i < 300; i++)
        {
            var (_, items) = LootSystem.Roll(table, rng, db);
            var rares = items.Where(it => db.Item(it.TemplateId).Rarity >= Rarity.Rare).ToList();
            rares.Count.ShouldBe(1);
            groupIds.ShouldContain(rares[0].TemplateId);
        }
        var party = new[] { w.Player("Ana"), w.Player("Bob"), w.Player("Cid") };
        w.Combat.Loot.EligibleFor = (_, _, _) => party;
        var ctx = w.Begin();
        var bag = w.Combat.Loot.CreateBag(tree, party[0], w.Map, ctx)!;
        var rare = bag.Entries.Single(e => groupIds.Contains(e.TemplateId));
        party.Select(p => p.CharacterId).ShouldContain(rare.OwnerCharacterId);
        var ann = ctx.Events.OfType<LootAnnouncedEvent>().Single(e => e.TemplateId == rare.TemplateId);
        ann.Global.ShouldBeTrue();
        ann.Rarity.ShouldBe(Rarity.Rare);
        ann.Winner.CharacterId.ShouldBe(rare.OwnerCharacterId);
    }
}
