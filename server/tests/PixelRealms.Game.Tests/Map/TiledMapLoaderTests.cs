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
    public void Meadow_And_Mine_Load_WithPortalsBetweenThem() // HU-020 CA1
    {
        var maps = TiledMapLoader.LoadAll(MapsDir, Check());
        maps.Select(m => m.MapId).ToArray().ShouldBe(new[] { "meadow", "mine", "test_small" });
        var meadow = maps.Single(m => m.MapId == "meadow");
        meadow.Width.ShouldBe(64);
        meadow.Spawns.Sum(s => s.Count).ShouldBeGreaterThanOrEqualTo(20);
        meadow.Portals.ShouldHaveSingleItem().TargetMapId.ShouldBe("mine");
        meadow.Portals[0].MinLevel.ShouldBe(4);
        meadow.Collision.IsSolid(0, 10).ShouldBeTrue();
        meadow.Collision.IsSolid(30, 25).ShouldBeTrue();   // muro de prueba
        meadow.Collision.IsSolid(30, 32).ShouldBeFalse();  // hueco
        meadow.ZoneAt(new Vec2(10, 10))!.Safe.ShouldBeTrue();
        meadow.ZoneAt(new Vec2(10, 30))!.Name.ShouldBe("Campos");
        meadow.NearestGraveyard(new Vec2(24, 50)).Id.ShouldBe("gy_fields");
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
    public void PortalToMissingMap_Fails()
    {
        var path = Path.Combine(MapsDir, "meadow.tmj");
        Should.Throw<MapLoadException>(() => TiledMapLoader.Load(path, Check(), _ => false)).Errors.ShouldContain(e => e.Contains("targetMapId 'mine'", StringComparison.Ordinal));
    }
}
