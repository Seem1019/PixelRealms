using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;

namespace PixelRealms.Game.Combat;

/// <summary>Enlaza combate y movimiento (ADR-019, HU-035 CA3/CA5): velocidad = base · castMoveSpeedMult (si castea) · auras; stun/root/muerto inmovilizan.</summary>
public static class CombatMovementRules
{
    public static float SpeedMultiplier(Actor actor, TickContext ctx)
    {
        var rules = ctx.Rules.Combat;
        var mult = actor.Auras.SpeedMultiplier(rules.MaxSlowPct);
        if (actor.Combat.IsCasting) mult *= rules.CastMoveSpeedMult;
        return (float)mult;
    }

    public static bool IsImmobilized(Actor actor) => actor.IsDead || actor.Auras.IsStunned || actor.Auras.IsRooted || actor.Combat.Flight is not null;
}
