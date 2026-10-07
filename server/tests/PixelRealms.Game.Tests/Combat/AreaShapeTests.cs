using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Combat;

/// <summary>
/// HU-102: el cono y la línea alcanzan a quien tenga el cuadro del cuerpo dentro (como el círculo de HU-086), salen del lanzador
/// hacia el punto apuntado, la línea no atraviesa muros y el punto solo da la dirección.
/// </summary>
public sealed class AreaShapeTests
{
    // Cuadro del cuerpo de rules.json: 0,375 a cada lado, 0,75 por encima de los pies y 0,25 por debajo.
    private static readonly TargetResolver.BodyBox Body = new(0.375f, 0.75f, 0.25f);
    private static readonly Vec2 East = new(1, 0);

    private static bool Hits(AreaShape s, float x, float y) => s.Touches(new Vec2(x, y), Body, out _);

    [Fact]
    public void Cone_HitsInFront_NotBehindNorBesideTheOpening()
    {
        var cone = AreaShape.Cone(new Vec2(10, 10), East, 2.5f, 90); // Tajo amplio
        Hits(cone, 12, 10).ShouldBeTrue();
        Hits(cone, 8, 10).ShouldBeFalse();     // detrás
        Hits(cone, 10, 12.4f).ShouldBeFalse(); // al lado, a 90°: fuera de los ±45°
    }

    [Fact]
    public void Cone_BodyInsideTheEdge_HitsEvenIfTheFeetAreOutside()
    {
        var cone = AreaShape.Cone(new Vec2(10, 10), East, 2.5f, 90);
        // Pies a 51,7° del eje (fuera); la esquina de arriba a la derecha del cuadro, a 43° y a 2,2 del vértice (dentro).
        Hits(cone, 11.5f, 11.9f).ShouldBeTrue();
        // Más abajo, ni el cuadro entra en la apertura.
        Hits(cone, 11.5f, 12.6f).ShouldBeFalse();
    }

    [Fact]
    public void Cone_RadiusCountsToTheNearestPartOfTheBody()
    {
        var cone = AreaShape.Cone(new Vec2(10, 10), East, 2.5f, 90);
        Hits(cone, 12.85f, 10).ShouldBeTrue();  // el lado izquierdo del cuadro, a 2,475
        Hits(cone, 12.9f, 10).ShouldBeFalse();  // a 2,525
    }

    [Fact]
    public void Cone_OfHalfACircle_TakesEverythingInFrontAndNothingBehind()
    {
        var cone = AreaShape.Cone(new Vec2(10, 10), East, 3, 180);
        Hits(cone, 10.2f, 12).ShouldBeTrue();
        Hits(cone, 10.2f, 8.5f).ShouldBeTrue();
        Hits(cone, 9, 10).ShouldBeFalse(); // el cuadro llega a 9,375: detrás del vértice
    }

    [Fact]
    public void Cone_TouchingTheCaster_IsInside()
    {
        var cone = AreaShape.Cone(new Vec2(10, 10), East, 2.5f, 50);
        Hits(cone, 10.1f, 10.5f).ShouldBeTrue(); // el vértice cae dentro de su cuadro
    }

    [Fact]
    public void Line_IsARectangleFromTheOrigin()
    {
        var line = AreaShape.Line(new Vec2(10, 10), East, 8, 1.5f); // Sendero de luz: y de 9,25 a 10,75
        Hits(line, 17, 10).ShouldBeTrue();
        Hits(line, 18.3f, 10).ShouldBeTrue();   // el cuadro empieza en 17,925
        Hits(line, 18.6f, 10).ShouldBeFalse();  // en 18,225
        Hits(line, 14, 11).ShouldBeTrue();      // el cuadro sube hasta 10,25
        Hits(line, 14, 11.6f).ShouldBeFalse();  // desde 10,85
        Hits(line, 14, 9).ShouldBeTrue();       // el cuadro baja hasta 9,25, el borde
        Hits(line, 14, 8.9f).ShouldBeFalse();
        Hits(line, 9, 10).ShouldBeFalse();      // detrás del origen
    }

    [Fact]
    public void Line_FollowsADiagonalDirection()
    {
        var dir = new Vec2(1, 1).Normalized();
        var line = AreaShape.Line(new Vec2(10, 10), dir, 8, 1.5f);
        Hits(line, 13.5f, 13.5f).ShouldBeTrue();
        Hits(line, 15, 12).ShouldBeFalse(); // a 2,1 del eje
        Hits(line, 13.5f, 9).ShouldBeFalse();
    }

    [Fact]
    public void ClearDistance_StopsAtTheFirstWallAlongTheRay()
    {
        var grid = new CollisionGrid(30, 30);
        LineOfSight.ClearDistance(grid, new Vec2(10.5f, 10.5f), East, 8).ShouldBe(8);
        grid.SetBlocksSight(14, 10);
        LineOfSight.ClearDistance(grid, new Vec2(10.5f, 10.5f), East, 8).ShouldBe(3.5f, 1e-4);
        // En diagonal se para en la primera casilla que cruza el rayo, (13,11) al bajar de la fila 10 a la 11, y no en una
        // vecina por la que no pasa.
        var diagonal = new Vec2(1, 0.3f).Normalized();
        grid.SetBlocksSight(12, 11);
        LineOfSight.ClearDistance(grid, new Vec2(10.5f, 10.2f), diagonal, 8).ShouldBe(8);
        grid.SetBlocksSight(13, 11);
        LineOfSight.ClearDistance(grid, new Vec2(10.5f, 10.2f), diagonal, 8).ShouldBe(0.8f / diagonal.Y, 1e-3);
        LineOfSight.ClearDistance(grid, new Vec2(10.5f, 10.5f), Vec2.Zero, 8).ShouldBe(8);
    }

    // --- Objetivos con los hechizos reales ---------------------------------------------------------------------------------

    private static TestWorld Arena() => new WorldBuilder().WithMap(40, 40)
        .WithPlayer("Ana", "warrior", 10, (10, 10))
        .WithPlayer("Bob", "priest", 10, (11.5f, 10))          // aliado delante
        .WithMonster("slime", (12, 10), wanderRadius: 0)       // delante
        .WithMonster("boar", (11.3f, 11.2f), wanderRadius: 0)  // delante, a un lado
        .WithMonster("wolf", (8, 10), wanderRadius: 0)         // detrás
        .BuildWithCombat();

    private static List<Actor> Targets(TestWorld w, string spellId, Vec2? pos)
    {
        var ana = w.Player("Ana");
        return w.Combat.Targets.Resolve(ana, w.Content.Spell(spellId), null, pos, ana.Position, w.Map, w.Begin());
    }

    [Fact]
    public void Cleave_HitsTheEnemiesInFront_NotTheOneBehind_NorAllies() // CA1
    {
        var w = Arena();
        var t = Targets(w, "warrior_cleave", new Vec2(12, 10));
        t.ShouldContain(w.Monster("slime"));
        t.ShouldContain(w.Monster("boar"));
        t.ShouldNotContain(w.Monster("wolf"));
        t.ShouldNotContain(w.Player("Bob"));
        // Apuntando hacia atrás, solo el de atrás.
        Targets(w, "warrior_cleave", new Vec2(8, 10)).ShouldBe(new[] { w.Monster("wolf") });
    }

    [Fact]
    public void ConeWithoutAnAimPoint_UsesWhereTheCasterFaces()
    {
        var w = Arena();
        w.Player("Ana").Facing = Direction.W;
        Targets(w, "warrior_cleave", null).ShouldBe(new[] { w.Monster("wolf") });
        Targets(w, "warrior_cleave", w.Player("Ana").Position).ShouldBe(new[] { w.Monster("wolf") }); // el punto sobre sus pies
    }

    [Fact]
    public void PathOfLight_StopsAtTheFirstWall() // CA2
    {
        var w = new WorldBuilder().WithMap(40, 40)
            .WithPlayer("Ana", "priest", 10, (10.5f, 10.5f))
            .WithPlayer("Bob", "warrior", 10, (13.5f, 10.5f))
            .WithMonster("slime", (16.5f, 10.5f), wanderRadius: 0)
            .BuildWithCombat();
        var path = w.Content.Spell("priest_path_of_light");
        path.Shape.ShouldBe(Shape.Line);
        // El sacerdote está en el origen de su propia línea: también recibe la velocidad y la cura, como en un círculo.
        Targets(w, path.Id, new Vec2(18.5f, 10.5f)).ShouldBe(new Actor[] { w.Player("Ana"), w.Player("Bob"), w.Monster("slime") }, ignoreOrder: true);
        w.Map.Data.Collision.SetBlocksSight(15, 10);
        Targets(w, path.Id, new Vec2(18.5f, 10.5f)).ShouldBe(new Actor[] { w.Player("Ana"), w.Player("Bob") }, ignoreOrder: true);
    }

    // --- Validación del casteo -------------------------------------------------------------------------------------------

    [Fact]
    public void ConeAimedOverAWall_IsNotRejected_TheAimPointIsOnlyADirection() // CA7
    {
        var w = Arena();
        var ana = w.Player("Ana");
        ana.Resource = ana.MaxResource;
        w.Map.Data.Collision.SetBlocksSight(12, 9);
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("warrior_cleave"), null, new Vec2(12.5f, 9.5f), w.Map, ctx).ShouldBeNull();
        var started = ctx.Events.OfType<CastStartedEvent>().Single();
        started.Origin.ShouldBe(ana.Position); // el cliente dibuja el cono desde aquí
    }

    [Fact]
    public void ConeAimedTooFar_IsOutOfRange() // CA7
    {
        var w = Arena();
        var ana = w.Player("Ana");
        ana.Resource = ana.MaxResource;
        w.Combat.Casts.TryBeginCast(ana, w.Content.Spell("warrior_cleave"), null, new Vec2(20, 10), w.Map, w.Begin()).ShouldBe(CastErrors.OutOfRange);
    }

    [Fact]
    public void ConeCastWithCastTime_KeepsTheDirectionItStartedWith() // CA3 (revisión de autoridad)
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 10, (10, 10))
            .WithMonster("slime", (12, 10), wanderRadius: 0)   // al este
            .WithMonster("wolf", (8, 10), wanderRadius: 0)     // al oeste
            .BuildWithCombat();
        var ana = w.Player("Ana");
        ana.Resource = ana.MaxResource;
        ana.Facing = Direction.E;
        var slowCleave = w.Content.Spell("warrior_cleave") with { CastMs = 1000 };
        var ctx = w.Begin();
        // Sin punto: la dirección es hacia donde mira AHORA, y el punto que ven todos es origen + dirección · radio.
        w.Combat.Casts.TryBeginCast(ana, slowCleave, null, ana.Position, w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastStartedEvent>().Single().TargetPos.ShouldBe(new Vec2(12.5f, 10));
        ana.Facing = Direction.W; // gira durante el casteo
        var hits = TickRunner.RunMs(w, 1200).OfType<CombatHitEvent>().Where(e => e.SpellId == "warrior_cleave").Select(e => e.Target).ToList();
        hits.ShouldContain(w.Monster("slime"));
        hits.ShouldNotContain(w.Monster("wolf"));
    }

    [Fact]
    public void SelfAroundConeWithAHugeAimPoint_IsRejected() // revisión de autoridad: el cono se volvía un círculo
    {
        var w = Arena();
        var ana = w.Player("Ana");
        ana.Resource = ana.MaxResource;
        var around = w.Content.Spell("warrior_cleave") with { Targeting = Targeting.SelfAoeEnemies };
        w.Combat.Casts.TryBeginCast(ana, around, null, new Vec2(1e20f, 0), w.Map, w.Begin()).ShouldBe(CastErrors.InvalidPayload);
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(ana, around, null, new Vec2(8, 10), w.Map, ctx).ShouldBeNull(); // hacia atrás, en el mapa
        ctx.Events.OfType<CombatHitEvent>().Select(e => e.Target).Distinct().ShouldBe(new Actor[] { w.Monster("wolf") });
    }

    [Fact]
    public void SelfAroundCircle_DoesNotForwardTheClientsPoint() // revisión de autoridad: la marca iba donde dijera el cliente
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "priest", 11, (10, 10)).BuildWithCombat();
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell("priest_hymn"), null, new Vec2(30, 30), w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastStartedEvent>().Single().TargetPos.ShouldBeNull();
    }

    [Fact]
    public void ConeDoesNotHitAroundACorner() // CA1: el punto no necesita LOS, pero cada objetivo sí, desde el vértice
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "warrior", 10, (10.5f, 10.5f))
            .WithMonster("slime", (12.5f, 11.5f), wanderRadius: 0)
            .BuildWithCombat();
        Targets(w, "warrior_cleave", new Vec2(12.5f, 10.5f)).ShouldContain(w.Monster("slime"));
        w.Map.Data.Collision.SetBlocksSight(11, 11);
        w.Map.Data.Collision.SetBlocksSight(12, 10);
        w.Map.Data.Collision.SetBlocksSight(11, 10);
        Targets(w, "warrior_cleave", new Vec2(12.5f, 10.5f)).ShouldNotContain(w.Monster("slime"));
    }

    [Fact]
    public void MonsterCone_AimsAtItsTargetWhenItStarts() // CA5
    {
        using var tmp = new TempContent();
        tmp.Patch("spells.json", root => root["spells"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
            { "id": "test_boar_gore", "name": "Corneada", "source": "monster", "school": "physical", "castMs": 1000, "cooldownMs": 5000,
              "range": 0, "targeting": "self_aoe_enemies", "shape": "cone", "aoeRadius": 2, "aoeAngleDeg": 60,
              "effects": [{ "type": "damage", "base": 5 }], "icon": "spells/slam", "description": "Prueba." }
            """)));
        tmp.Patch("monsters.json", root => root["monsters"]!.AsArray().First(m => m!["id"]!.GetValue<string>() == "boar")!["spells"] =
            System.Text.Json.Nodes.JsonNode.Parse("""[{ "spellId": "test_boar_gore" }]"""));
        var db = PixelRealms.Content.ContentLoader.LoadOrThrow(tmp.Path);
        var w = new WorldBuilder(db).WithMap(40, 40).WithPlayer("Ana", "warrior", 5, (10, 11)).WithMonster("boar", (10, 10), wanderRadius: 0).BuildWithCombat();
        var boar = w.Monster("boar");
        boar.Threat.Add(w.Player("Ana").Id, 1);
        var start = TickRunner.RunMs(w, 3000).OfType<CastStartedEvent>().First(e => e.Spell.Id == "test_boar_gore");
        var dir = (start.TargetPos!.Value - start.Origin!.Value).Normalized();
        dir.Y.ShouldBeGreaterThan(0.9f); // hacia Ana, al sur
    }

    [Fact]
    public void CircleCasts_CarryNoOrigin()
    {
        var w = new WorldBuilder().WithMap(40, 40).WithPlayer("Ana", "mage", 5, (10, 10)).BuildWithCombat();
        var ctx = w.Begin();
        w.Combat.Casts.TryBeginCast(w.Player("Ana"), w.Content.Spell("mage_flame_burst"), null, new Vec2(13, 10), w.Map, ctx).ShouldBeNull();
        ctx.Events.OfType<CastStartedEvent>().Single().Origin.ShouldBeNull();
    }
}
