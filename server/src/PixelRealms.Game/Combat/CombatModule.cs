using PixelRealms.Content;
using PixelRealms.Game.Ai;
using PixelRealms.Game.Core;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Movement;
using PixelRealms.Game.Social;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Composición de los sistemas de combate (M2) en el orden del tick de docs/architecture.md §3: movimiento → casteo → auras →
/// IA → básico → recursos → muerte/respawn → interés. La usan el servidor y los tests (WorldBuilder).
/// </summary>
public sealed class CombatModule
{
    private CombatModule(Func<ContentDb> content, World world, MovementSystem movement, InterestSystem interest)
    {
        Services = new CombatServices(content);
        Damage = new DamagePipeline(Services);
        Auras = new AuraSystem(Services, Damage);
        Targets = new TargetResolver(Services);
        Effects = new EffectResolver(Services, Damage, Auras, Targets);
        Casts = new CastSystem(Services, Effects, Damage);
        AutoAttack = new AutoAttackSystem(Services, Damage);
        Resources = new ResourceSystem(Services, Damage);
        Death = new DeathSystem(Services, Auras, interest);
        Spawns = new SpawnSystem(content, world);
        Ai = new MonsterAiSystem(Services, Casts, Auras, movement);
        Progression = new Progression.ProgressionSystem(Services);
        Loot = new Items.LootSystem(Services);
        ItemUse = new Items.ItemUseService(Services, Casts);
        Vendor = new Items.VendorService(Services);
        Pvp = new PvpService(Auras);
        Parties = new PartyService();
        Chat = new ChatService();
        Trades = new TradeService(Services);
        ClassChange = new ClassChangeService(Services);
        Social = new SocialTickSystem(Pvp, Trades);
        Movement = movement;
        Interest = interest;

        Damage.Auras = Auras;
        Damage.Death = Death;
        Auras.Interrupter = Casts;
        Effects.Casts = Casts;
        Death.OnMonsterKilled = Spawns.ScheduleRespawn;
        Death.IsLooted = Loot.IsLooted;
        Death.OnCorpseRemoved = (m, map) => Loot.Forget(map, m.Id);
        Services.PvpCanAttack = (a, b) => Pvp.CanAttack(a, b, Services.Content.Rules) is not null;
        Services.InDuel = Pvp.InActiveDuel;
        Damage.DuelClamp = Pvp.ClampDamage;
        Ai.CanBeAggroed = p => !p.IsDead && !Pvp.InActiveDuel(p);
        ClassChange.InDuel = p => Pvp.DuelOf(p) is not null;
        ClassChange.InTrade = p => Trades.TradeOf(p) is not null;
        Trades.InDuel = p => Pvp.DuelOf(p) is not null;
        Pvp.InTrade = p => Trades.TradeOf(p) is not null;
        Progression.XpRecipients = GroupRecipients;
        Loot.EligibleFor = LootEligible;
        movement.SpeedMultiplier = CombatMovementRules.SpeedMultiplier;
        movement.IsImmobilized = CombatMovementRules.IsImmobilized;
    }

    public CombatServices Services { get; }
    public DamagePipeline Damage { get; }
    public AuraSystem Auras { get; }
    public TargetResolver Targets { get; }
    public EffectResolver Effects { get; }
    public CastSystem Casts { get; }
    public AutoAttackSystem AutoAttack { get; }
    public ResourceSystem Resources { get; }
    public DeathSystem Death { get; }
    public SpawnSystem Spawns { get; }
    public MonsterAiSystem Ai { get; }
    public Progression.ProgressionSystem Progression { get; }
    public Items.LootSystem Loot { get; }
    public Items.ItemUseService ItemUse { get; }
    public Items.VendorService Vendor { get; }
    public PvpService Pvp { get; }
    public PartyService Parties { get; }
    public ChatService Chat { get; }
    public TradeService Trades { get; }
    public ClassChangeService ClassChange { get; }
    public SocialTickSystem Social { get; }

    /// <summary>HU-062 CA2: XP repartida entre los miembros activos del grupo del que taggeó (vivos, ≤ xpRangeTiles, acción en activeWindowSec).</summary>
    private List<(Entities.Player Player, int Xp)> GroupRecipients(Entities.Player tagger, Entities.Monster monster, Map.MapInstance map, Content.Defs.IRules rules)
    {
        var party = Parties.PartyOf(tagger.CharacterId);
        if (party is null) return [(tagger, PixelRealms.Game.Progression.XpCurve.SoloKillXp(rules.Progression, monster.Template, tagger.Level))];
        var now = Math.Max(tagger.LastCombatAtMs, tagger.LastActionAtMs);
        var active = new List<Entities.Player>();
        foreach (var p in map.Players.Values)
            if (party.Contains(p.CharacterId) && p.IsAlive && Vec2.Distance(p.Position, monster.Position) <= rules.Group.XpRangeTiles && p.IsActive(now, rules.Group.ActiveWindowSec))
                active.Add(p);
        if (active.Count == 0) return [(tagger, PixelRealms.Game.Progression.XpCurve.SoloKillXp(rules.Progression, monster.Template, tagger.Level))];
        var shares = GroupXp.Split(rules.Progression, rules.Group, monster.Template, active.Select(p => p.Level).ToList());
        var result = new List<(Entities.Player, int)>(active.Count);
        for (var i = 0; i < active.Count; i++) result.Add((active[i], (int)Math.Round(shares[i], MidpointRounding.AwayFromZero)));
        return result;
    }

    /// <summary>HU-062 CA3/CA4: elegibles para el botín = miembros vivos del grupo a ≤ eligibleRangeTiles (o quien taggeó).</summary>
    private List<Entities.Player> LootEligible(Entities.Player tagger, Entities.Monster monster, Map.MapInstance map)
    {
        var party = Parties.PartyOf(tagger.CharacterId);
        if (party is null) return [tagger];
        var range = Services.Content.Rules.Loot.EligibleRangeTiles;
        var list = new List<Entities.Player>();
        foreach (var p in map.Players.Values)
            if (party.Contains(p.CharacterId) && p.IsAlive && Vec2.Distance(p.Position, monster.Position) <= range) list.Add(p);
        return list.Count == 0 ? [tagger] : list;
    }
    public MovementSystem Movement { get; }
    public InterestSystem Interest { get; }

    public static CombatModule Create(Func<ContentDb> content, World world, MovementSystem movement, InterestSystem interest) => new(content, world, movement, interest);

    /// <summary>Registra los sistemas en orden; `extraBeforeInterest` permite insertar portales u otros antes de la AOI.</summary>
    public Simulation Register(Simulation sim, params IMapSystem[] extraBeforeInterest)
    {
        sim.AddSystem(Movement).AddSystem(Casts).AddSystem(Auras).AddSystem(Ai).AddSystem(AutoAttack).AddSystem(Resources).AddSystem(Death).AddSystem(Progression).AddSystem(Loot).AddSystem(Spawns).AddSystem(Social);
        foreach (var s in extraBeforeInterest) sim.AddSystem(s);
        return sim.AddSystem(Interest);
    }
}
