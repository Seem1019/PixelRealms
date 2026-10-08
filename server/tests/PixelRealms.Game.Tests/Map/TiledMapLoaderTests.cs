using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Portals;
using PixelRealms.Game.Tests.Helpers;
using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests.Map;

/// <summary>HU-020 CA4: maps/test_small.tmj cubre CSV, flags de flip, solid, blocksSight y cada tipo de objeto.</summary>
public sealed class TiledMapLoaderTests
{
    private static string MapsDir => Path.Combine(TestContent.RepoRoot, "maps");

    private static TiledMapLoader.ContentCheck Check()
    {
        var db = TestContent.Load();
        return new TiledMapLoader.ContentCheck(id => db.TryGetMonster(id, out _), db.HasVendor);
    }

    /// <summary>HU-083: con la puerta cerrada no hay otro camino de la entrada de la Mina a la sala del jefe; abierta, sí.</summary>
    [Fact]
    public void Mine_ClosedBossDoor_IsTheOnlyWayToTheBoss()
    {
        var mine = TiledMapLoader.Load(Path.Combine(MapsDir, "mine.tmj"), Check(), _ => true);
        var map = new MapInstance(1, mine);
        var from = mine.DefaultGraveyard.Position;
        var boss = mine.Spawns.Single(s => s.MonsterId == "foreman_grask").Position;
        Reachable(map.Collision, from, boss).ShouldBeFalse("la puerta cerrada tiene que tapar todo el paso");
        map.SetDoorTiles(mine.Doors.Single(), closed: false);
        Reachable(map.Collision, from, boss).ShouldBeTrue();
    }

    /// <summary>Búsqueda en anchura por casillas (4 vecinos) sobre las no sólidas.</summary>
    private static bool Reachable(CollisionGrid grid, Vec2 from, Vec2 to) => Steps(grid, from, to) >= 0;

    /// <summary>Pasos del camino más corto (4 vecinos) sobre las casillas no sólidas ni tapadas por `blocked`; −1 si no se llega.</summary>
    private static int Steps(CollisionGrid grid, Vec2 from, Vec2 to, Func<int, int, bool>? blocked = null)
    {
        var start = ((int)from.X, (int)from.Y);
        var goal = ((int)to.X, (int)to.Y);
        var dist = new Dictionary<(int, int), int> { [start] = 0 };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if ((x, y) == goal) return dist[(x, y)];
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (x + dx, y + dy);
                if (n.Item1 < 0 || n.Item2 < 0 || n.Item1 >= grid.Width || n.Item2 >= grid.Height || grid.IsSolid(n.Item1, n.Item2)) continue;
                if (blocked?.Invoke(n.Item1, n.Item2) == true || !dist.TryAdd(n, dist[(x, y)] + 1)) continue;
                queue.Enqueue(n);
            }
        }
        return -1;
    }

    [Fact]
    public void TestSmall_CollisionAndSight()
    {
        var map = TiledMapLoader.Load(Path.Combine(MapsDir, "test_small.tmj"), Check());
        map.MapId.ShouldBe("test_small");
        map.Width.ShouldBe(10);
        map.Collision.IsSolid(3, 3).ShouldBeTrue();      // muro
        map.Collision.BlocksSight(3, 3).ShouldBeTrue();
        map.Collision.IsSolid(5, 5).ShouldBeTrue();      // arbusto: sólido…
        map.Collision.BlocksSight(5, 5).ShouldBeFalse(); // …pero no bloquea visión
        map.Collision.IsSolid(7, 7).ShouldBeTrue();      // gid con flag de flip horizontal
        map.Collision.IsSolid(1, 8).ShouldBeTrue();      // capa collision
        map.Collision.BlocksSight(1, 8).ShouldBeFalse();
        map.Collision.IsSolid(2, 2).ShouldBeFalse();
        map.Collision.IsSolid(-1, 0).ShouldBeTrue();     // fuera del mapa = sólido
    }

    [Fact]
    public void TestSmall_Objects()
    {
        var map = TiledMapLoader.Load(Path.Combine(MapsDir, "test_small.tmj"), Check());
        map.Spawns.Count.ShouldBe(2);
        var point = map.Spawns.Single(s => s.Id == "one_slime");
        point.MonsterId.ShouldBe("slime"); point.Count.ShouldBe(1); point.Position.ShouldBe(new Vec2(2, 6)); point.Size.ShouldBe(Vec2.Zero);
        var rect = map.Spawns.Single(s => s.Id == "boars");
        rect.Count.ShouldBe(3); rect.Size.ShouldBe(new Vec2(3, 2)); rect.WanderRadius.ShouldBe(2f);
        map.Npcs.ShouldHaveSingleItem().VendorId.ShouldBe("robledal_general_goods");
        map.Graveyards.ShouldHaveSingleItem().Position.ShouldBe(new Vec2(1, 2));
        map.DefaultGraveyard.Id.ShouldBe("gy");
        var zone = map.Zones.ShouldHaveSingleItem();
        zone.Safe.ShouldBeTrue(); zone.Name.ShouldBe("Plaza"); zone.Contains(new Vec2(2, 2)).ShouldBeTrue(); zone.Contains(new Vec2(5, 5)).ShouldBeFalse();
        map.IsSafeZone(new Vec2(1, 1)).ShouldBeTrue();
        map.Portals.ShouldBeEmpty();
    }

    [Fact]
    public void Meadow_And_Mine_Load_WithPortalsBetweenThem() // HU-020 CA1, HU-080 CA1–CA3
    {
        var maps = TiledMapLoader.LoadAll(MapsDir, Check());
        maps.Select(m => m.MapId).ToArray().ShouldBe(new[] { "crypt", "forest", "meadow", "mine", "test_small" });
        var meadow = maps.Single(m => m.MapId == "meadow");
        meadow.Width.ShouldBe(250); meadow.Height.ShouldBe(110); // aldea 42 + Campos 100 + Colinas 100 (~100×100 útiles por zona)
        meadow.Portals.ShouldHaveSingleItem().TargetMapId.ShouldBe("mine");
        meadow.Portals[0].MinLevel.ShouldBe(4);
        meadow.Collision.IsSolid(0, 10).ShouldBeTrue();
        meadow.Collision.IsSolid(146, 20).ShouldBeTrue();   // cresta Campos/Colinas
        meadow.Collision.IsSolid(146, 54).ShouldBeFalse();  // paso
        meadow.ZoneAt(new Vec2(20, 50))!.Safe.ShouldBeTrue();
        meadow.ZoneAt(new Vec2(80, 50))!.Name.ShouldBe("Campos");
        meadow.ZoneAt(new Vec2(200, 50))!.Name.ShouldBe("Colinas");
        meadow.Zones.Select(z => z.Name).ToArray().ShouldBe(new[] { "Aldea Robledal", "Campos", "Colinas" });
        meadow.Zones.ShouldAllBe(z => !string.IsNullOrEmpty(z.Landmark));
        meadow.NearestGraveyard(new Vec2(90, 50)).Id.ShouldBe("gy_fields");
        meadow.NearestGraveyard(new Vec2(200, 50)).Id.ShouldBe("gy_hills");
        meadow.Graveyards.Count.ShouldBe(3); // uno por zona (CA3)
        meadow.Npcs.ShouldContain(n => n.VendorId == "robledal_general_goods" && meadow.ZoneAt(n.Position)!.Safe);
        // CA2: monstruos suficientes para 5 jugadores.
        int Count(string id) => meadow.Spawns.Where(s => s.MonsterId == id).Sum(s => s.Count);
        Count("slime").ShouldBeGreaterThanOrEqualTo(20); Count("boar").ShouldBeGreaterThanOrEqualTo(20); Count("bandit").ShouldBeGreaterThanOrEqualTo(15);
        Count("wolf").ShouldBeGreaterThanOrEqualTo(20); Count("goblin_archer").ShouldBeGreaterThanOrEqualTo(15);
        // Subniveles: los campamentos de slimes están más cerca de la aldea que los de bandidos; lobos antes que goblins.
        meadow.Spawns.Where(s => s.MonsterId == "slime").Max(s => s.Position.X).ShouldBeLessThan(meadow.Spawns.Where(s => s.MonsterId == "bandit").Min(s => s.Position.X));
        meadow.Spawns.Where(s => s.MonsterId == "wolf").Max(s => s.Position.X).ShouldBeLessThan(meadow.Spawns.Where(s => s.MonsterId == "goblin_archer").Min(s => s.Position.X));
        // El portal a la Mina está al final de las Colinas y la Mina devuelve al lado del portal.
        meadow.ZoneAt(meadow.Portals[0].Position)!.Name.ShouldBe("Colinas");
        var mine = maps.Single(m => m.MapId == "mine");
        var back = mine.Portals.Single(p => p.TargetMapId == "meadow");
        Vec2.Distance(new Vec2(back.TargetX, back.TargetY), meadow.Portals[0].Position).ShouldBeLessThan(8);
        meadow.Collision.IsSolidAt(back.TargetX, back.TargetY).ShouldBeFalse();
        mine.Collision.IsSolidAt(meadow.Portals[0].TargetX, meadow.Portals[0].TargetY).ShouldBeFalse();
        // HU-083 CA1: dos palancas en la Sala 2 que abren la puerta del pasillo de la sala del jefe y una dentro que abre sola.
        mine.Levers.Count.ShouldBe(3);
        mine.Levers.ShouldAllBe(l => l.DoorId == "mine_boss_door");
        mine.Levers.Count(l => l.OpensAlone).ShouldBe(1);
        var door = mine.Doors.ShouldHaveSingleItem();
        door.DoorId.ShouldBe("mine_boss_door");
        mine.Spawns.Single(s => s.MonsterId == "foreman_grask").Position.Y.ShouldBeGreaterThan(door.Position.Y); // el jefe, detrás
    }

    /// <summary>HU-111 CA1–CA4: el Bosque tiene el Linde y el Pantano separados por un paso, un punto seguro por zona que es
    /// siempre el más cercano dentro de ella, los monstruos del bestiario con subniveles y el portal de vuelta a la Sala 3.</summary>
    [Fact]
    public void Forest_TwoZonesSplitByAPass_ASafePointEach_AndThePortalBackToTheMine()
    {
        var maps = TiledMapLoader.LoadAll(MapsDir, Check());
        var forest = maps.Single(m => m.MapId == "forest");
        forest.Width.ShouldBe(214); forest.Height.ShouldBe(104); // dos zonas de ~105×100 (la Pradera, con tres, es 250×110)
        var open = forest.Zones.Where(z => !z.Safe).ToArray();
        open.Select(z => (z.Name, z.MinLevel, z.MaxLevel)).ToArray().ShouldBe(new[] { ("Linde del Bosque", 6, 8), ("Pantano", 8, 10) });
        open.Select(z => z.Landmark).ToArray().ShouldBe(new[] { "Árbol Madre", "Torre hundida" });
        // CA1: la cresta entre las dos zonas solo se cruza por el paso de 4 casillas.
        var split = (int)open[1].Position.X;
        Enumerable.Range(0, forest.Height).Count(y => !forest.Collision.IsSolid(split, y)).ShouldBe(4);

        // CA3: una fogata por zona, dentro de una zona `safe`, y desde cualquier casilla de la zona es la más cercana (donde se
        // reaparece, DeathSystem): nadie vuelve a la Aldea ni a la fogata de la otra zona.
        forest.Graveyards.Count.ShouldBe(2);
        foreach (var zone in open)
        {
            var gy = forest.Graveyards.Single(g => zone.Contains(g.Position));
            forest.IsSafeZone(gy.Position).ShouldBeTrue(gy.Id);
            var wrong = 0;
            for (var y = (int)zone.Position.Y; y < (int)(zone.Position.Y + zone.Size.Y); y++)
                for (var x = (int)zone.Position.X; x < (int)(zone.Position.X + zone.Size.X); x++)
                    if (!forest.Collision.IsSolid(x, y) && forest.NearestGraveyard(new Vec2(x + 0.5f, y + 0.5f)).Id != gy.Id) wrong++;
            wrong.ShouldBe(0, zone.Name);
        }
        var brena = forest.Npcs.ShouldHaveSingleItem();
        brena.VendorId.ShouldBe("forest_camp");
        forest.IsSafeZone(brena.Position).ShouldBeTrue();
        open[0].Contains(brena.Position).ShouldBeTrue("Brena está en el punto seguro del Linde");

        // CA2: los monstruos de cada zona según el bestiario, un élite en su rama lateral, subniveles de la entrada (oeste) a la
        // salida (este) y ningún campamento dentro de un punto seguro.
        string[] MonstersIn(ZoneDef z) => forest.Spawns.Where(s => z.Contains(s.Position)).Select(s => s.MonsterId).Distinct().Order(StringComparer.Ordinal).ToArray();
        MonstersIn(open[0]).ShouldBe(new[] { "bandit_woodcutter", "forest_wolf", "old_bear", "weaver_spider" });
        MonstersIn(open[1]).ShouldBe(new[] { "giant_toad", "lizardman", "swamp_witch", "will_o_wisp" });
        forest.Spawns.Single(s => s.MonsterId == "old_bear").Count.ShouldBe(1);
        forest.Spawns.Single(s => s.MonsterId == "swamp_witch").Count.ShouldBe(1);
        float MaxX(string id) => forest.Spawns.Where(s => s.MonsterId == id).Max(s => s.Position.X);
        float MinX(string id) => forest.Spawns.Where(s => s.MonsterId == id).Min(s => s.Position.X);
        MaxX("forest_wolf").ShouldBeLessThan(MinX("bandit_woodcutter")); MaxX("bandit_woodcutter").ShouldBeLessThan(MinX("weaver_spider"));
        MaxX("giant_toad").ShouldBeLessThan(MinX("lizardman")); MaxX("lizardman").ShouldBeLessThan(MinX("will_o_wisp"));
        foreach (var s in forest.Spawns)
        {
            var inSafe = false;
            for (var y = (int)s.Position.Y; y < (int)s.Position.Y + Math.Max(1, (int)s.Size.Y); y++)
                for (var x = (int)s.Position.X; x < (int)s.Position.X + Math.Max(1, (int)s.Size.X); x++)
                    inSafe |= forest.IsSafeZone(new Vec2(x + 0.5f, y + 0.5f));
            inSafe.ShouldBeFalse(s.Id);
        }

        // CA4: el portal de vuelta sale de la boca de la Mina, en el Linde, y deja en la Sala 3 junto a la salida hacia aquí
        // (HU-112, la antigua hornacina tapiada en x 86..87 · y 25..27). El otro es el de la Cripta (HU-115).
        forest.Portals.Select(p => p.TargetMapId).Order(StringComparer.Ordinal).ToArray().ShouldBe(new[] { "crypt", "mine" });
        var back = forest.Portals.Single(p => p.TargetMapId == "mine");
        open[0].Contains(back.Position).ShouldBeTrue();
        var mine = maps.Single(m => m.MapId == "mine");
        mine.Collision.IsSolidAt(back.TargetX, back.TargetY).ShouldBeFalse();
        Vec2.Distance(new Vec2(back.TargetX, back.TargetY), new Vec2(86, 26)).ShouldBeLessThan(3);
    }

    // Casilla del jefe y esquina de la salida al Tier 3 en la Cripta (BOSS_SPAWN y TIER3_EXIT de tools/maps/gen_tier2_maps.py):
    // HU-115 deja el sitio y HU-117 / HU-112 añaden el spawn del Árbol Podrido y el portal con `minPhase`; desde entonces mandan
    // los del mapa.
    private static readonly Vec2 CryptBossSpawn = new(60, 49);
    private static readonly Vec2 CryptTier3Exit = new(99, 71);

    private static Vec2 TileCentre(Vec2 p) => new(MathF.Floor(p.X) + 0.5f, MathF.Floor(p.Y) + 0.5f);

    /// <summary>Casillas alcanzables (4 vecinos) desde `from` sin pisar las que `blocked` descarta (recibe el centro de la casilla).</summary>
    private static HashSet<(int X, int Y)> ReachAvoiding(MapData map, Vec2 from, Func<Vec2, bool> blocked)
    {
        var start = ((int)from.X, (int)from.Y);
        var seen = new HashSet<(int X, int Y)> { start };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (map.Collision.IsSolid(nx, ny) || blocked(new Vec2(nx + 0.5f, ny + 0.5f)) || !seen.Add((nx, ny))) continue;
                queue.Enqueue((nx, ny));
            }
        }
        return seen;
    }

    private static (int X, int Y) Tile(Vec2 p) => ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    /// <summary>HU-115 CA1 y CA4: la Cripta se entra por el portal del fondo del Pantano y sale al lado; su única fogata está en la
    /// entrada, que es zona segura: morir en cualquier sala deja allí.</summary>
    [Fact]
    public void Crypt_TheEntranceIsTheOnlySafePoint_AndItsPortalsPairWithTheSwamp()
    {
        var maps = TiledMapLoader.LoadAll(MapsDir, Check());
        var crypt = maps.Single(m => m.MapId == "crypt");
        var forest = maps.Single(m => m.MapId == "forest");
        crypt.DisplayName.ShouldBe("Cripta de Raíces");

        var gy = crypt.Graveyards.ShouldHaveSingleItem();
        crypt.DefaultGraveyard.ShouldBe(gy);
        crypt.IsSafeZone(gy.Position).ShouldBeTrue();
        for (var y = 0; y < crypt.Height; y++)
            for (var x = 0; x < crypt.Width; x++)
                if (!crypt.Collision.IsSolid(x, y)) crypt.NearestGraveyard(new Vec2(x + 0.5f, y + 0.5f)).ShouldBe(gy); // DeathSystem

        // Ida: del fondo del Pantano a la entrada segura de la Cripta, junto al portal de vuelta y fuera de él. Vuelta: al lado de
        // la boca del Pantano. Sin nivel mínimo ni fase: el Bosque solo se alcanza en la Fase 2.
        var into = forest.Portals.Single(p => p.TargetMapId == "crypt");
        var back = crypt.Portals.Single(p => p.TargetMapId == "forest");
        forest.ZoneAt(into.Position)!.Name.ShouldBe("Pantano");
        into.Position.X.ShouldBeGreaterThan(forest.Width - 10); // al fondo, en el borde este
        foreach (var (portal, here, there) in new[] { (into, forest, crypt), (back, crypt, forest) })
        {
            portal.MinLevel.ShouldBeNull(portal.PortalId);
            var target = new Vec2(portal.TargetX, portal.TargetY);
            there.Collision.IsSolidAt(target.X, target.Y).ShouldBeFalse(portal.PortalId);
            var returning = there.Portals.Single(p => p.TargetMapId == here.MapId);
            returning.Contains(target).ShouldBeFalse(portal.PortalId);
            Vec2.Distance(target, returning.Position).ShouldBeLessThan(8, portal.PortalId);
        }
        crypt.IsSafeZone(new Vec2(into.TargetX, into.TargetY)).ShouldBeTrue();
        crypt.ZoneAt(back.Position).ShouldBe(crypt.ZoneAt(gy.Position));
    }

    /// <summary>HU-115 CA1 y CA2: las plantas trampa se ven antes de despertar, por el centro despiertan todas y con cuidado se
    /// cruza sin despertar ninguna; la sala del élite queda entre la trampa y la salida, y la del jefe en una rama lateral.</summary>
    [Fact]
    public void Crypt_EveryTileTheBossSees_IsWithinItsAimedAreas() // revisión de autoridad (HU-117)
    {
        // Ni en la sala ni en el pasillo recto de entrada hay sitio con vista al Árbol Podrido fuera del alcance de Raíces y Esporas:
        // desde ahí un sanador curaría al tanque sin que el árbol pudiera responderle.
        var db = TestContent.Load();
        var crypt = TiledMapLoader.LoadAll(MapsDir, Check()).Single(m => m.MapId == "crypt");
        var boss = TileCentre(crypt.Spawns.Single(s => s.MonsterId == "rotten_tree").Position);
        var reach = db.Monster("rotten_tree").Spells.Select(s => db.Spell(s.SpellId)).Where(s => s.Targeting.IsGround()).Min(s => s.Range)
            + db.Rules.Combat.CastRangeToleranceTiles;
        var seenOutOfReach = new List<(int X, int Y)>();
        for (var y = 0; y < crypt.Height; y++)
            for (var x = 0; x < crypt.Width; x++)
            {
                var tile = new Vec2(x + 0.5f, y + 0.5f);
                if (!crypt.Collision.IsSolid(x, y) && Vec2.Distance(tile, boss) > reach && LineOfSight.Has(crypt.Collision, boss, tile))
                    seenOutOfReach.Add((x, y));
            }
        seenOutOfReach.ShouldBeEmpty();
    }

    [Fact]
    public void Crypt_TheTrapComesFirst_TheEliteGuardsTheExit_AndTheBossIsOnASideBranch()
    {
        var db = TestContent.Load();
        var crypt = TiledMapLoader.LoadAll(MapsDir, Check()).Single(m => m.MapId == "crypt");
        var from = crypt.DefaultGraveyard.Position;
        var boss = crypt.Spawns.SingleOrDefault(s => s.MonsterId == "rotten_tree")?.Position ?? CryptBossSpawn;
        var exit = crypt.Portals.SingleOrDefault(p => p.TargetMapId != "forest")?.Position ?? CryptTier3Exit;
        var guardian = crypt.Spawns.Single(s => s.MonsterId == "crypt_guardian");
        var guard = TileCentre(guardian.Position);

        // Salas de monstruos: esqueletos y espíritus del musgo, del nivel de la Cripta.
        crypt.Spawns.Where(s => s.MonsterId is "root_skeleton" or "moss_spirit").Sum(s => s.Count).ShouldBeGreaterThanOrEqualTo(10);

        // CA2: plantas inmóviles en su sitio (punto, una, sin pasear).
        var plantAggro = (float)db.Monster("trap_plant").AggroRange;
        var plantReach = (float)db.Monster("trap_plant").AttackRange;
        var plants = crypt.Spawns.Where(s => s.MonsterId == "trap_plant").ToArray();
        plants.Length.ShouldBeGreaterThanOrEqualTo(3);
        plants.ShouldAllBe(p => p.Size == Vec2.Zero && p.Count == 1 && p.WanderRadius == 0f);
        var centres = plants.Select(p => TileCentre(p.Position)).ToArray();
        bool InPlantAggro(Vec2 t) => centres.Any(c => Vec2.Distance(t, c) <= plantAggro);
        // Con cuidado: hay camino hasta el élite, la salida y el jefe sin entrar en el aggro de ninguna planta…
        var careful = ReachAvoiding(crypt, from, InPlantAggro);
        careful.ShouldContain(Tile(guard));
        careful.ShouldContain(Tile(exit));
        careful.ShouldContain(Tile(boss));
        // …y desde ese camino cada planta se ve (línea de visión) a su alcance antes de despertarla.
        foreach (var c in centres)
            careful.Any(t => Vec2.Distance(new Vec2(t.X + 0.5f, t.Y + 0.5f), c) <= plantReach && LineOfSight.Has(crypt.Collision, new Vec2(t.X + 0.5f, t.Y + 0.5f), c))
                .ShouldBeTrue($"la planta de {c} no se ve desde fuera de su aggro");
        // Por el camino más corto (el centro del jardín) despiertan todas.
        var path = ShortestPath(crypt, from, guard);
        centres.Count(c => path.Any(t => Vec2.Distance(new Vec2(t.X + 0.5f, t.Y + 0.5f), c) <= plantAggro)).ShouldBe(plants.Length);

        // Orden: sin cruzar el jardín (la caja de las plantas más su aggro) no se llega al élite, a la salida ni al jefe.
        var (x0, x1) = (centres.Min(c => c.X) - plantAggro, centres.Max(c => c.X) + plantAggro);
        var (y0, y1) = (centres.Min(c => c.Y) - plantAggro, centres.Max(c => c.Y) + plantAggro);
        var withoutTrap = ReachAvoiding(crypt, from, t => t.X >= x0 && t.X <= x1 && t.Y >= y0 && t.Y <= y1);
        withoutTrap.ShouldNotContain(Tile(guard));
        withoutTrap.ShouldNotContain(Tile(exit));
        withoutTrap.ShouldNotContain(Tile(boss));
        // La salida está detrás del élite: no se llega a ella sin entrar en su aggro. El jefe no.
        var guardAggro = (float)db.Monster("crypt_guardian").AggroRange;
        var pastTheGuard = ReachAvoiding(crypt, from, t => Vec2.Distance(t, guard) <= guardAggro);
        pastTheGuard.ShouldNotContain(Tile(exit));
        pastTheGuard.ShouldContain(Tile(boss));
        // Rama lateral: el camino al élite y a la salida no pasa por la sala del jefe.
        var aroundTheBoss = ReachAvoiding(crypt, from, t => Vec2.Distance(t, TileCentre(boss)) <= 12);
        aroundTheBoss.ShouldContain(Tile(guard));
        aroundTheBoss.ShouldContain(Tile(exit));
        // Sala del jefe (HU-117: inmóvil, con áreas sobre jugadores a distancia): amplia y sin columnas a 12 casillas a la redonda.
        for (var y = (int)boss.Y - 12; y <= (int)boss.Y + 12; y++)
            for (var x = (int)boss.X - 12; x <= (int)boss.X + 12; x++)
                if (Vec2.Distance(new Vec2(x + 0.5f, y + 0.5f), TileCentre(boss)) <= 12) crypt.Collision.IsSolid(x, y).ShouldBeFalse($"({x}, {y})");
    }

    /// <summary>Camino más corto (4 vecinos) de `from` a `to`, con sus casillas.</summary>
    private static List<(int X, int Y)> ShortestPath(MapData map, Vec2 from, Vec2 to)
    {
        var start = Tile(from);
        var goal = Tile(to);
        var parent = new Dictionary<(int X, int Y), (int X, int Y)> { [start] = start };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(start);
        while (queue.Count > 0 && !parent.ContainsKey(goal))
        {
            var (x, y) = queue.Dequeue();
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (map.Collision.IsSolid(n.Item1, n.Item2) || !parent.TryAdd(n, (x, y))) continue;
                queue.Enqueue(n);
            }
        }
        var path = new List<(int X, int Y)>();
        for (var c = goal; c != start; c = parent[c]) path.Add(c);
        path.Add(start);
        return path;
    }

    /// <summary>
    /// HU-112 CA1–CA3: la hornacina de la Sala 3 es la salida al Linde del Bosque, cerrada hasta la Fase 2 con el aviso del
    /// derrumbe, y sigue detrás del gólem élite: está al otro lado de la sala y el camino más corto desde la entrada pasa por su
    /// aggro. Llegar desde el Bosque deja junto a ella, sin pisarla.
    /// </summary>
    [Fact]
    public void Mine_Room3Exit_LeadsToTheForestEdge_ClosedUntilPhase2_BehindTheGolem()
    {
        var maps = TiledMapLoader.LoadAll(MapsDir, Check());
        var mine = maps.Single(m => m.MapId == "mine");
        var forest = maps.Single(m => m.MapId == "forest");
        var exit = mine.Portals.Single(p => p.TargetMapId == "forest");
        exit.PortalId.ShouldBe("mine_to_forest");
        exit.MinPhase.ShouldBe(2);
        exit.LockedText.ShouldBe("El derrumbe aún bloquea el paso");
        exit.MinLevel.ShouldBeNull();
        // CA1: la hornacina (x 86..87 · y 25..27) es el portal; deja en la llegada reservada del Linde (MINE_ARRIVAL), libre y
        // fuera del portal de vuelta.
        (exit.Position, exit.Size).ShouldBe((new Vec2(86, 25), new Vec2(2, 3)));
        (exit.TargetX, exit.TargetY).ShouldBe((12f, 29f));
        forest.Collision.IsSolidAt(exit.TargetX, exit.TargetY).ShouldBeFalse();
        forest.ZoneAt(new Vec2(exit.TargetX, exit.TargetY))!.Name.ShouldBe("Linde del Bosque");
        var back = forest.Portals.Single(p => p.TargetMapId == "mine");
        back.Contains(new Vec2(exit.TargetX, exit.TargetY)).ShouldBeFalse();
        var arrival = new Vec2(back.TargetX, back.TargetY);
        exit.Contains(arrival).ShouldBeFalse("volver del Bosque no deja encima de la salida");
        PortalPolicy.DistanceTo(exit, arrival).ShouldBeLessThanOrEqualTo(1f);

        // CA3: el gólem élite está entre la entrada de la Sala 3 (oeste) y la salida (este), y no se llega a ella sin entrar en
        // su aggro: con las casillas a su alcance tapadas, el camino desde la entrada de la Mina se alarga.
        var golem = mine.Spawns.Single(s => s.MonsterId == "rubble_golem");
        exit.Position.X.ShouldBeGreaterThan(golem.Position.X);
        Vec2.Distance(golem.Position, exit.Position).ShouldBeLessThan(10, "la salida está en la Sala 3, la del gólem");
        var aggro = (float)TestContent.Load().Monster("rubble_golem").AggroRange;
        var from = mine.DefaultGraveyard.Position;
        var to = exit.Position + new Vec2(0.5f, 1.5f);
        var direct = Steps(mine.Collision, from, to);
        direct.ShouldBeGreaterThan(0);
        var aroundTheGolem = Steps(mine.Collision, from, to, (x, y) => Vec2.Distance(new Vec2(x + 0.5f, y + 0.5f), golem.Position) <= aggro);
        (aroundTheGolem < 0 || aroundTheGolem > direct).ShouldBeTrue($"el camino más corto a la salida ({direct}) pasa por el aggro del gólem (sin pasar: {aroundTheGolem})");
    }

    /// <summary>
    /// Cada llegada de un portal y cada punto seguro deja libre la caja de los pies (MovementStep, 10×6 px), no solo el punto, y
    /// fuera de los portales del mapa: con la caja metida en una roca, el primer paso empuja al revés (en HU-112, una roca del
    /// derrumbe en (85, 24) tapaba la llegada desde el Bosque y devolvía al jugador encima de la salida).
    /// </summary>
    [Fact]
    public void PortalArrivals_AndSafePoints_LeaveTheFootBoxFree()
    {
        var maps = TiledMapLoader.LoadAll(MapsDir, Check()).ToDictionary(m => m.MapId);
        foreach (var map in maps.Values)
        {
            foreach (var p in map.Portals)
            {
                if (!maps.TryGetValue(p.TargetMapId, out var to))
                {
                    p.MinPhase.ShouldNotBeNull($"{p.PortalId}: su mapa no existe y no lleva minPhase");
                    continue; // la salida a un tier que aún no existe (la Montaña): su destino lo fija ese mapa
                }
                var at = new Vec2(p.TargetX, p.TargetY);
                FootBoxFree(to.Collision, at).ShouldBeTrue($"{p.PortalId} → {p.TargetMapId} {at}");
                to.Portals.ShouldNotContain(q => q.Contains(at), $"{p.PortalId} deja encima de otro portal");
            }
            foreach (var gy in map.Graveyards) FootBoxFree(map.Collision, gy.Position).ShouldBeTrue($"{map.MapId}: {gy.Id}");
        }
    }

    /// <summary>Lo que mira <see cref="MovementStep"/> al moverse: las casillas que toca la caja de los pies centrada en `at`.</summary>
    private static bool FootBoxFree(CollisionGrid grid, Vec2 at)
    {
        double x = at.X * MovementStep.TileSize, y = at.Y * MovementStep.TileSize;
        for (var row = (int)Math.Floor((y - MovementStep.HalfY) / MovementStep.TileSize); row <= (int)Math.Ceiling((y + MovementStep.HalfY) / MovementStep.TileSize) - 1; row++)
            for (var col = (int)Math.Floor((x - MovementStep.HalfX) / MovementStep.TileSize); col <= (int)Math.Ceiling((x + MovementStep.HalfX) / MovementStep.TileSize) - 1; col++)
                if (grid.IsSolid(col, row)) return false;
        return true;
    }

    [Fact]
    public void MinPhase_NotAPositiveInteger_Fails() // HU-112: un minPhase mal escrito no puede dejar abierto el paso
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pr-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "tilesets"));
        foreach (var f in Directory.GetFiles(Path.Combine(MapsDir, "tilesets"))) File.Copy(f, Path.Combine(tmp, "tilesets", Path.GetFileName(f)));
        var text = File.ReadAllText(Path.Combine(MapsDir, "mine.tmj"));
        const string good = "{\"name\":\"minPhase\",\"type\":\"int\",\"value\":2}";
        text.ShouldContain(good);
        foreach (var bad in new[] { "{\"name\":\"minPhase\",\"type\":\"string\",\"value\":\"2\"}", "{\"name\":\"minPhase\",\"type\":\"int\",\"value\":0}" })
        {
            File.WriteAllText(Path.Combine(tmp, "mine.tmj"), text.Replace(good, bad, StringComparison.Ordinal));
            Should.Throw<MapLoadException>(() => TiledMapLoader.Load(Path.Combine(tmp, "mine.tmj"), Check(), _ => true)).Errors
                .ShouldContain(e => e.Contains("minPhase", StringComparison.Ordinal), bad);
        }
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void PortalToMissingMap_WithMinPhase_Loads_AndIsListedAsClosed() // HU-112 (lo usará la salida de la Cripta, HU-115)
    {
        // La Mina sin el Bosque: la salida (minPhase 2) carga; el portal a la Pradera (sin minPhase) sigue exigiendo que exista.
        var path = Path.Combine(MapsDir, "mine.tmj");
        TiledMapLoader.Load(path, Check(), id => id != "forest").Portals.Count.ShouldBe(2);
        Should.Throw<MapLoadException>(() => TiledMapLoader.Load(path, Check(), id => id != "meadow")).Errors
            .ShouldContain(e => e.Contains("targetMapId 'meadow'", StringComparison.Ordinal));

        var tmp = Path.Combine(Path.GetTempPath(), "pr-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "tilesets"));
        foreach (var f in Directory.GetFiles(Path.Combine(MapsDir, "tilesets"))) File.Copy(f, Path.Combine(tmp, "tilesets", Path.GetFileName(f)));
        File.Copy(path, Path.Combine(tmp, "mine.tmj"));
        File.Copy(Path.Combine(MapsDir, "meadow.tmj"), Path.Combine(tmp, "meadow.tmj"));
        var maps = TiledMapLoader.LoadAll(tmp, Check());
        var (map, portal) = TiledMapLoader.PortalsToMissingMaps(maps).ShouldHaveSingleItem();
        (map.MapId, portal.PortalId).ShouldBe(("mine", "mine_to_forest"));
        // En el repo, el único cerrado por no existir su mapa es la salida de la Cripta a la Montaña (Tier 3, HU-115).
        TiledMapLoader.PortalsToMissingMaps(TiledMapLoader.LoadAll(MapsDir, Check())).Select(x => (x.Map.MapId, x.Portal.PortalId))
            .ShouldBe(new[] { ("crypt", "crypt_to_mountain") });
        Directory.Delete(tmp, true);
    }

    [Theory]
    [InlineData("meadow")]
    [InlineData("mine")]
    [InlineData("forest")]
    [InlineData("crypt")]
    public void Borders_AreSolid(string mapId) // skill world-maps, HU-111 CA5
    {
        var map = TiledMapLoader.LoadAll(MapsDir, Check()).Single(m => m.MapId == mapId);
        for (var x = 0; x < map.Width; x++)
        {
            map.Collision.IsSolid(x, 0).ShouldBeTrue($"({x}, 0)");
            map.Collision.IsSolid(x, map.Height - 1).ShouldBeTrue($"({x}, {map.Height - 1})");
        }
        for (var y = 0; y < map.Height; y++)
        {
            map.Collision.IsSolid(0, y).ShouldBeTrue($"(0, {y})");
            map.Collision.IsSolid(map.Width - 1, y).ShouldBeTrue($"({map.Width - 1}, {y})");
        }
    }

    [Theory]
    [InlineData("meadow")]
    [InlineData("mine")]
    [InlineData("forest")]
    [InlineData("crypt")]
    public void FloodFill_FromDefaultGraveyard_ReachesEveryWalkableTile_AndEveryObject(string mapId) // HU-080 CA4, HU-083 CA1, HU-111 CA5, HU-115 CA4
    {
        var map = TiledMapLoader.LoadAll(MapsDir, Check()).Single(m => m.MapId == mapId);
        var grid = map.Collision;
        var seen = new bool[map.Width, map.Height];
        var queue = new Queue<(int X, int Y)>();
        var start = ((int)map.DefaultGraveyard.Position.X, (int)map.DefaultGraveyard.Position.Y);
        seen[start.Item1, start.Item2] = true; queue.Enqueue(start);
        var reached = 0;
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue(); reached++;
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                if (nx >= 0 && ny >= 0 && nx < map.Width && ny < map.Height && !seen[nx, ny] && !grid.IsSolid(nx, ny)) { seen[nx, ny] = true; queue.Enqueue((nx, ny)); }
        }
        var walkable = 0;
        for (var y = 0; y < map.Height; y++) for (var x = 0; x < map.Width; x++) if (!grid.IsSolid(x, y)) walkable++;
        reached.ShouldBe(walkable); // ninguna bolsa inaccesible
        foreach (var gy in map.Graveyards) seen[(int)gy.Position.X, (int)gy.Position.Y].ShouldBeTrue(gy.Id);
        foreach (var npc in map.Npcs) seen[(int)npc.Position.X, (int)npc.Position.Y].ShouldBeTrue(npc.Name);
        foreach (var p in map.Portals) seen[(int)p.Position.X, (int)p.Position.Y].ShouldBeTrue(p.PortalId);
        foreach (var l in map.Levers) seen[(int)l.Position.X, (int)l.Position.Y].ShouldBeTrue(l.LeverId);
        foreach (var s in map.Spawns)
        {
            var w = Math.Max(1, (int)s.Size.X); var h = Math.Max(1, (int)s.Size.Y);
            var free = 0;
            for (var y = (int)s.Position.Y; y < (int)s.Position.Y + h; y++) for (var x = (int)s.Position.X; x < (int)s.Position.X + w; x++) if (seen[x, y]) free++;
            free.ShouldBeGreaterThanOrEqualTo(s.Count, s.Id);
        }
    }

    [Fact]
    public void UnknownMonster_OrSpawnOnSolid_Fails() // HU-020 CA2
    {
        var path = Path.Combine(MapsDir, "test_small.tmj");
        var badMonster = Should.Throw<MapLoadException>(() => TiledMapLoader.Load(path, new TiledMapLoader.ContentCheck(_ => false, _ => true)));
        badMonster.Errors.ShouldContain(e => e.Contains("monsterId 'slime' no existe", StringComparison.Ordinal));

        var tmp = Path.Combine(Path.GetTempPath(), "pr-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "tilesets"));
        foreach (var f in Directory.GetFiles(Path.Combine(MapsDir, "tilesets"))) File.Copy(f, Path.Combine(tmp, "tilesets", Path.GetFileName(f)));
        // Mueve el spawn puntual "one_slime" (x 32, y 96 px) sobre el muro de (3, 3) → (48, 48 px).
        var text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), "\"x\":\\s*32,\\s*\"y\":\\s*96", "\"x\": 48, \"y\": 48");
        File.WriteAllText(Path.Combine(tmp, "test_small.tmj"), text);
        var onSolid = Should.Throw<MapLoadException>(() => TiledMapLoader.Load(Path.Combine(tmp, "test_small.tmj"), Check()));
        onSolid.Errors.ShouldContain(e => e.Contains("casilla sólida", StringComparison.Ordinal));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void LeverForAMissingDoor_Fails() // HU-083: cada palanca abre una puerta que existe
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pr-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "tilesets"));
        foreach (var f in Directory.GetFiles(Path.Combine(MapsDir, "tilesets"))) File.Copy(f, Path.Combine(tmp, "tilesets", Path.GetFileName(f)));
        var text = File.ReadAllText(Path.Combine(MapsDir, "mine.tmj"));
        var i = text.IndexOf("\"mine_boss_door\"", StringComparison.Ordinal); // la primera es la palanca oeste
        File.WriteAllText(Path.Combine(tmp, "mine.tmj"), text[..i] + "\"nope\"" + text[(i + "\"mine_boss_door\"".Length)..]);
        var error = Should.Throw<MapLoadException>(() => TiledMapLoader.Load(Path.Combine(tmp, "mine.tmj"), Check(), _ => true));
        error.Errors.ShouldContain(e => e.Contains("doorId 'nope' no existe", StringComparison.Ordinal));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void RepeatedLeverOrDoorId_Fails() // revisión de autoridad: dos objetos con el mismo id
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pr-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "tilesets"));
        foreach (var f in Directory.GetFiles(Path.Combine(MapsDir, "tilesets"))) File.Copy(f, Path.Combine(tmp, "tilesets", Path.GetFileName(f)));
        var text = File.ReadAllText(Path.Combine(MapsDir, "mine.tmj")).Replace("\"mine_lever_east\"", "\"mine_lever_west\"", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(tmp, "mine.tmj"), text);
        var error = Should.Throw<MapLoadException>(() => TiledMapLoader.Load(Path.Combine(tmp, "mine.tmj"), Check(), _ => true));
        error.Errors.ShouldContain(e => e.Contains("repetido: 'mine_lever_west'", StringComparison.Ordinal));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void PortalToMissingMap_Fails()
    {
        var path = Path.Combine(MapsDir, "meadow.tmj");
        Should.Throw<MapLoadException>(() => TiledMapLoader.Load(path, Check(), _ => false)).Errors.ShouldContain(e => e.Contains("targetMapId 'mine'", StringComparison.Ordinal));
    }
}
