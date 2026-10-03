using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Paso 7 del tick (HU-039): maná por `spi`/`int` (por 5 s, penalizado tras gastar), energía +energyPerSec, ira que decae fuera
/// de combate, y vida fuera de combate tras `hpRegenDelaySec` según `spi`/`sta`; el perdedor de un duelo regenera más rápido y sin
/// esperar (HU-064 CA3). Todo por tick con acumuladores de fracciones.
/// </summary>
public sealed class ResourceSystem(CombatServices services, DamagePipeline damage) : IMapSystem
{
    public string Name => "resources";

    public void Tick(MapInstance map, TickContext ctx)
    {
        var rules = ctx.Rules.Combat;
        var dt = ctx.DeltaMs / 1000.0;
        foreach (var p in map.Players.Values)
        {
            if (p.IsDead) continue;
            var stats = services.StatsOf(p);
            var inCombat = p.IsInCombat(ctx.NowMs, rules.InCombatWindowSec);
            switch (services.ResourceOf(p))
            {
                case Resource.Mana:
                {
                    var perSec = stats.ManaRegenPer5s / 5.0;
                    if (ctx.NowMs < p.Combat.ManaPenaltyUntilMs) perSec *= rules.ManaRegenCastingPenalty;
                    if (p.Resource < p.MaxResource) damage.AddResource(p, perSec * dt, ctx);
                    break;
                }
                case Resource.Energy:
                    if (p.Resource < p.MaxResource) damage.AddResource(p, rules.EnergyPerSec * dt, ctx);
                    break;
                case Resource.Rage:
                    if (!inCombat && p.Resource > 0) damage.AddResource(p, -rules.RageDecayPerSecOutOfCombat * dt, ctx);
                    break;
                default:
                    break;
            }

            // Vida fuera de combate: tras hpRegenDelaySec sin hacer ni recibir daño. La recuperación tras perder un duelo no espera
            // y multiplica, hasta la vida con que empezó el duelo o hasta volver a entrar en combate.
            if (p.Combat.Recovery is { } r && (p.Hp >= r.UntilHp || p.LastCombatAtMs > r.FromMs)) p.Combat.Recovery = null;
            var recovery = p.Combat.Recovery;
            var sinceCombat = p.LastCombatAtMs == long.MinValue ? double.MaxValue : (ctx.NowMs - p.LastCombatAtMs) / 1000.0;
            if (p.Hp < p.MaxHp && (recovery is not null || sinceCombat >= rules.HpRegenDelaySec))
            {
                var acc = p.Combat.HpRegenAcc + stats.HpRegenPerSec * (recovery?.Mult ?? 1.0) * dt;
                var whole = (int)Math.Truncate(acc);
                p.Combat.HpRegenAcc = acc - whole;
                if (whole > 0) { p.Hp = Math.Min(p.MaxHp, p.Hp + whole); p.Dirty = true; }
                if (p.Combat.Recovery is { } done && p.Hp >= done.UntilHp) p.Combat.Recovery = null;
            }
        }
    }
}
