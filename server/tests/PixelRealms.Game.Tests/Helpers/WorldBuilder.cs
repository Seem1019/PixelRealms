using PixelRealms.Content;
using PixelRealms.Content.Defs;
using PixelRealms.Game.Combat;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Items;
using PixelRealms.Game.Map;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Progression;

namespace PixelRealms.Game.Tests.Helpers;

/// <summary>
/// Constructor fluido de mundos de prueba (skill dotnet-server §Tests):
/// <c>new WorldBuilder().WithMap(20, 20).WithPlayer("Ana", "mage", level: 3, at: (5, 5)).WithMonster("wolf", at: (8, 5)).Build()</c>.
/// </summary>
public sealed class WorldBuilder(ContentDb? content = null)
{
    /// <summary>El contenido real del repo, o uno parcheado (<see cref="TempContent"/>) para probar números distintos.</summary>
    private readonly ContentDb _content = content ?? TestContent.Load();
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

    /// <summary>
    /// Jugador con los items iniciales de su clase equipados (o `equip` explícito: ids de items a equipar en su slot), vida al
    /// máximo, maná al máximo y ira/energía como un personaje nuevo (0 / máximo) y los hechizos conocidos a su nivel.
    /// </summary>
    public WorldBuilder WithPlayer(string name, string classId, int level = 1, (float x, float y) at = default, IReadOnlyList<string>? equip = null)
    {
        _spawns.Add(inst =>
        {
            var cls = _content.Class(classId);
            var p = new Player(_world.EntityIds.Next(), name, classId) { CharacterId = Guid.NewGuid(), AccountId = Guid.NewGuid(), Level = level, Position = new Vec2(at.x, at.y) };
            var itemIds = equip ?? cls.StartingItems.Where(i => i.Equip).Select(i => i.ItemId).ToList();
            foreach (var id in itemIds)
            {
                var tpl = _content.Item(id);
                if (tpl.Slot is { } slot) p.Equipment.Slots[(int)slot] = ItemInstance.New(id);
            }
            var equipped = p.Equipment.Slots.Where(s => s is not null).Select(s => _content.Item(s!.TemplateId)).ToList();
            var d = StatCalculator.Derive(cls, level, _content.Rules, equipped);
            p.MaxHp = d.MaxHp; p.Hp = p.MaxHp;
            p.MaxResource = StatCalculator.MaxResource(cls, d, _content.Rules);
            p.Resource = cls.Resource == Resource.Rage ? 0 : p.MaxResource;
            p.BaseSpeed = (float)_content.Rules.Movement.BaseSpeedTilesPerSec;
            p.KnownSpells.AddRange(_content.KnownSpells(classId, level).Select(sp => sp.Id));
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
            m.Brain.Spawn = new SpawnDef(templateId, templateId, 1, wanderRadius, new Vec2(at.x, at.y), Vec2.Zero);
            inst.Add(m);
        });
        return this;
    }

    /// <summary>Mundo sin sistemas registrados (cada test añade los suyos); `Combat` queda disponible para usarlo a mano.</summary>
    public TestWorld Build()
    {
        _map ??= new WorldBuilder().WithMap()._map!;
        _world.RegisterMap(_map);
        var inst = _world.CreateInstance(_map.MapId);
        foreach (var s in _spawns) s(inst);
        var clock = new TickClock();
        var sim = new Simulation(_world, _content.Rules, _rng ?? new SeededRng(_seed), clock);
        var combat = CombatModule.Create(() => _content, _world, new MovementSystem(), new InterestSystem());
        return new TestWorld(_world, inst, sim, clock, _content, combat);
    }

    /// <summary>Mundo con todos los sistemas de combate registrados en el orden del tick (M2).</summary>
    public TestWorld BuildWithCombat()
    {
        var w = Build();
        w.Combat.Register(w.Simulation);
        return w;
    }
}

public sealed record TestWorld(World World, MapInstance Map, Simulation Simulation, TickClock Clock, ContentDb Content, CombatModule Combat)
{
    public TickContext Ctx => Simulation.Context;

    /// <summary>Prepara el contexto para llamar a los sistemas a mano sin correr un tick (NowMs del reloj).</summary>
    public TickContext Begin()
    {
        Simulation.Context.BeginTick(Simulation.Tick, Clock.NowMs);
        return Simulation.Context;
    }

    public Player Player(string name) => Map.Players.Values.First(p => p.Name == name);

    public Monster Monster(string templateId) => Map.Monsters.Values.First(m => m.TemplateId == templateId);
}
