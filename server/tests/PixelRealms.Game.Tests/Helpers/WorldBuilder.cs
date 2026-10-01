using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Tests.Helpers;

/// <summary>
/// Constructor fluido de mundos de prueba (skill dotnet-server §Tests):
/// <c>new WorldBuilder().WithMap(20, 20).WithPlayer("Ana", "mage", level: 3, at: (5, 5)).WithMonster("wolf", at: (8, 5)).Build()</c>.
/// </summary>
public sealed class WorldBuilder
{
    private readonly ContentDb _content = TestContent.Load();
    private readonly World _world = new();
    private readonly List<Action<MapInstance>> _spawns = new();
    private MapData? _map;
    private int _seed = 1234;
    private IRng? _rng;

    public ContentDb Content => _content;

    public WorldBuilder WithSeed(int seed) { _seed = seed; return this; }

    public WorldBuilder WithRng(IRng rng) { _rng = rng; return this; }

    /// <summary>Mapa vacío de width×height con borde sólido y un cementerio en (2, 2).</summary>
    public WorldBuilder WithMap(int width = 32, int height = 32, string mapId = "test")
    {
        var grid = new CollisionGrid(width, height);
        for (var x = 0; x < width; x++) { grid.SetSolid(x, 0); grid.SetSolid(x, height - 1); grid.SetBlocksSight(x, 0); grid.SetBlocksSight(x, height - 1); }
        for (var y = 0; y < height; y++) { grid.SetSolid(0, y); grid.SetSolid(width - 1, y); grid.SetBlocksSight(0, y); grid.SetBlocksSight(width - 1, y); }
        return WithMap(grid, mapId);
    }

    public WorldBuilder WithMap(CollisionGrid grid, string mapId = "test")
    {
        _map = new MapData(mapId, mapId, grid, [], [], [new GraveyardDef("gy", new Vec2(2, 2))], [], [], "gy");
        return this;
    }

    public WorldBuilder WithMap(MapData data) { _map = data; return this; }

    /// <summary>Marca sólida (y bloquea visión) una casilla del mapa de prueba.</summary>
    public WorldBuilder WithWall(int x, int y)
    {
        _spawns.Add(inst => { inst.Data.Collision.SetSolid(x, y); inst.Data.Collision.SetBlocksSight(x, y); });
        return this;
    }

    public WorldBuilder WithPlayer(string name, string classId, int level = 1, (float x, float y) at = default)
    {
        _spawns.Add(inst =>
        {
            var p = new Player(_world.EntityIds.Next(), name, classId) { CharacterId = Guid.NewGuid(), Level = level, Position = new Vec2(at.x, at.y) };
            var cls = _content.Class(classId);
            p.MaxHp = cls.BaseHp; p.Hp = p.MaxHp;
            p.BaseSpeed = (float)_content.Rules.Movement.BaseSpeedTilesPerSec;
            inst.Add(p);
        });
        return this;
    }

    public WorldBuilder WithMonster(string templateId, (float x, float y) at = default, float wanderRadius = 3)
    {
        _spawns.Add(inst =>
        {
            var t = _content.Monster(templateId);
            var m = new Monster(_world.EntityIds.Next(), t, new Vec2(at.x, at.y), wanderRadius) { Level = t.Level, Position = new Vec2(at.x, at.y), Hp = t.Hp, MaxHp = t.Hp, BaseSpeed = (float)t.Speed };
            inst.Add(m);
        });
        return this;
    }

    public TestWorld Build()
    {
        _map ??= new WorldBuilder().WithMap()._map!;
        _world.RegisterMap(_map);
        var inst = _world.CreateInstance(_map.MapId);
        foreach (var s in _spawns) s(inst);
        var clock = new TickClock();
        var sim = new Simulation(_world, _content.Rules, _rng ?? new SeededRng(_seed), clock);
        return new TestWorld(_world, inst, sim, clock, _content);
    }
}

public sealed record TestWorld(World World, MapInstance Map, Simulation Simulation, TickClock Clock, ContentDb Content)
{
    public Player Player(string name) => Map.Players.Values.First(p => p.Name == name);

    public Monster Monster(string templateId) => Map.Monsters.Values.First(m => m.TemplateId == templateId);
}
