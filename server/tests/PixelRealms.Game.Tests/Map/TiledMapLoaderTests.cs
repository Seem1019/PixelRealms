using PixelRealms.Game.Core;
using PixelRealms.Game.Map;
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
        maps.Select(m => m.MapId).ToArray().ShouldBe(new[] { "meadow", "mine", "test_small" });
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
        var back = mine.Portals.ShouldHaveSingleItem();
        back.TargetMapId.ShouldBe("meadow");
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

    [Theory]
    [InlineData("meadow")]
    [InlineData("mine")]
    public void FloodFill_FromDefaultGraveyard_ReachesEveryWalkableTile_AndEveryObject(string mapId) // HU-080 CA4, HU-083 CA1
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
