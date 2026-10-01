using PixelRealms.Content;
using PixelRealms.Game.Ai;
using PixelRealms.Game.Core;
using PixelRealms.Game.Interest;
using PixelRealms.Game.Movement;

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
        Movement = movement;
        Interest = interest;

        Damage.Auras = Auras;
        Damage.Death = Death;
        Auras.Interrupter = Casts;
        Effects.Casts = Casts;
        Death.OnMonsterKilled = Spawns.ScheduleRespawn;
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
    public MovementSystem Movement { get; }
    public InterestSystem Interest { get; }

    public static CombatModule Create(Func<ContentDb> content, World world, MovementSystem movement, InterestSystem interest) => new(content, world, movement, interest);

    /// <summary>Registra los sistemas en orden; `extraBeforeInterest` permite insertar portales u otros antes de la AOI.</summary>
    public Simulation Register(Simulation sim, params IMapSystem[] extraBeforeInterest)
    {
        sim.AddSystem(Movement).AddSystem(Casts).AddSystem(Auras).AddSystem(Ai).AddSystem(AutoAttack).AddSystem(Resources).AddSystem(Death).AddSystem(Spawns);
        foreach (var s in extraBeforeInterest) sim.AddSystem(s);
        return sim.AddSystem(Interest);
    }
}
