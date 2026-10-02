using PixelRealms.Content.Defs;
using PixelRealms.Content.Validation;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>Códigos de error de casteo (docs/protocol.md §Códigos de error).</summary>
public static class CastErrors
{
    public const string NotFound = "not_found";
    public const string LevelTooLow = "level_too_low";
    public const string IsDead = "is_dead";
    public const string Stunned = "stunned";
    public const string Rooted = "rooted";
    public const string Silenced = "silenced";
    public const string LockedOut = "locked_out";
    public const string OnCooldown = "on_cooldown";
    public const string OnGcd = "on_gcd";
    public const string NotEnoughResource = "not_enough_resource";
    public const string InvalidTarget = "invalid_target";
    public const string OutOfRange = "out_of_range";
    public const string NoLos = "no_los";
    public const string InvalidPayload = "invalid_payload";
    public const string AreaLimit = "area_limit";
    public const string Forbidden = "forbidden";
}

/// <summary>
/// Paso 4 del tick (HU-033): valida e inicia casteos (skill combat-system §Pipeline), los avanza, revalida al terminar los de
/// un objetivo (alcance + `castRangeToleranceTiles`, LOS, vivo, recurso), descuenta recurso y cooldown al resolver, programa
/// impactos de proyectil (`distancia / speed`) y los resuelve aunque el lanzador muera. GCD y `abilityLockMs` (ADR-019).
/// </summary>
public sealed class CastSystem(CombatServices services, EffectResolver effects, DamagePipeline damage) : IMapSystem, ICastInterrupter
{
    private sealed record PendingImpact(long AtMs, Actor Caster, SpellDef Spell, EntityId? TargetId, Vec2? TargetPos, Vec2 Origin);

    private readonly Dictionary<int, List<PendingImpact>> _impacts = new();
    private readonly List<Actor> _casting = new(32);

    public string Name => "casts";

    public int PendingImpacts(MapInstance map) => _impacts.TryGetValue(map.Id, out var l) ? l.Count : 0;

    /// <summary>Intenta lanzar; devuelve el código de error o null si el hechizo empezó (o se resolvió si es instantáneo).</summary>
    public string? TryBeginCast(Actor caster, SpellDef spell, EntityId? targetId, Vec2? targetPos, MapInstance map, TickContext ctx, bool cancelCurrent = true)
    {
        var rules = ctx.Rules.Combat;
        var now = ctx.NowMs;
        var combat = caster.Combat;

        if (caster is Player p)
        {
            if (spell.Source == SpellSource.Class && !p.KnownSpells.Contains(spell.Id)) return CastErrors.NotFound;
            if (EngineCapabilities.UnavailableReason(spell) is not null) return CastErrors.NotFound;
            if (p.Level < spell.LevelReq) return CastErrors.LevelTooLow;
        }
        if (caster.IsDead) return CastErrors.IsDead;
        if (caster.Auras.IsStunned) return CastErrors.Stunned;
        if (spell.School == School.Magic && caster.Auras.IsSilenced) return CastErrors.Silenced;
        if (combat.IsLockedOut(now)) return CastErrors.LockedOut;
        if (combat.IsOnCooldown(spell.Id, now)) return CastErrors.OnCooldown;
        if (caster is Player && spell.Source != SpellSource.Item && ((combat.IsOnGcd(now) && spell.TriggersGcd) || combat.IsAbilityLocked(now))) return CastErrors.OnGcd;
        var hasLeap = false; EffectDef? dash = null;
        foreach (var e in spell.Effects) { if (e.Type == EffectType.Leap) hasLeap = true; if (e.Type == EffectType.Dash) dash = e; }
        if (hasLeap && caster.Auras.IsRooted) return CastErrors.Rooted;

        if (caster is Player pc && spell.Cost is { Amount: > 0 } cost)
        {
            if (services.ResourceOf(pc) != cost.Resource) return CastErrors.NotEnoughResource;
            if (pc.Resource < cost.Amount) return CastErrors.NotEnoughResource;
        }

        // Objetivo / punto según targeting.
        Actor? target = null;
        var tolerance = 0.0;
        switch (spell.Targeting)
        {
            case Targeting.Enemy:
            {
                target = targetId is { } id ? map.Find(id) : null;
                if (target is null || target.IsDead || target.Combat.Evading || !services.IsEnemy(caster, target)) return CastErrors.InvalidTarget;
                if (Vec2.Distance(caster.Position, target.Position) > spell.Range + tolerance) return CastErrors.OutOfRange;
                if (dash is not null && Vec2.Distance(caster.Position, target.Position) < dash.MinRange) return CastErrors.OutOfRange;
                if (!LineOfSight.Has(map.Data.Collision, caster.Position, target.Position)) return CastErrors.NoLos;
                break;
            }
            case Targeting.Ally:
            {
                target = targetId is { } id ? map.Find(id) : null;
                if (target is null || target.IsDead || !services.IsAlly(caster, target)) target = caster; // CA6: sin aliado → sobre mí
                if (!ReferenceEquals(target, caster))
                {
                    if (Vec2.Distance(caster.Position, target.Position) > spell.Range) return CastErrors.OutOfRange;
                    if (!LineOfSight.Has(map.Data.Collision, caster.Position, target.Position)) return CastErrors.NoLos;
                }
                targetId = target.Id;
                break;
            }
            case Targeting.Self:
                targetId = caster.Id;
                // Salto: el punto se recorta a la última casilla libre con LOS dentro de maxRange (HU-087 CA1), no se rechaza.
                if (hasLeap && !ValidPoint(targetPos, map)) return CastErrors.InvalidPayload;
                break;
            default:
                if (spell.Targeting.IsGround())
                {
                    if (!ValidPoint(targetPos, map)) return CastErrors.InvalidPayload;
                    if (hasLeap) break;
                    if (Vec2.Distance(caster.Position, targetPos!.Value) > spell.Range + rules.CastRangeToleranceTiles) return CastErrors.OutOfRange;
                    if (!LineOfSight.Has(map.Data.Collision, caster.Position, targetPos.Value)) return CastErrors.NoLos;
                    // LineOfSight no mira la casilla de destino: apuntar dentro de un muro alcanzaría a quien está detrás.
                    if (map.Data.Collision.BlocksSight((int)MathF.Floor(targetPos.Value.X), (int)MathF.Floor(targetPos.Value.Y))) return CastErrors.NoLos;
                }
                break;
        }

        // Otro hechizo durante un casteo lo cancela sin coste (ADR-019); los usables (pociones) no.
        if (cancelCurrent && combat.Cast is { } current) EndCast(caster, current, CastResults.Cancelled, null, map, ctx);

        if (caster is Player && spell.Source != SpellSource.Item)
        {
            if (spell.TriggersGcd) { combat.GcdEndsAtMs = now + rules.GcdMs; ctx.Emit(new CooldownEvent(map.Id, caster, null, null, rules.GcdMs)); }
            if (spell.IsInstant) combat.AbilityLockEndsAtMs = now + rules.AbilityLockMs;
        }

        if (caster is Player actedPlayer) actedPlayer.LastActionAtMs = now;
        if (spell.IsInstant)
        {
            // Instantáneo: CastStarted{durationMs:0} + CastEnded{done} para que el cliente dibuje el efecto (misma secuencia que un casteo).
            ctx.Emit(new CastStartedEvent(map.Id, caster, spell, targetId, targetPos, 0));
            ctx.Emit(new CastEndedEvent(map.Id, caster, spell, CastResults.Done, null));
            Resolve(caster, spell, targetId, targetPos, caster.Position, map, ctx);
            return null;
        }
        combat.Cast = new CastState(spell, now, now + spell.CastMs, targetId, targetPos, caster.Position);
        ctx.Emit(new CastStartedEvent(map.Id, caster, spell, targetId, targetPos, spell.CastMs));
        return null;
    }

    private static bool ValidPoint(Vec2? p, MapInstance map) =>
        p is { } v && !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y)
        && v.X >= 0 && v.Y >= 0 && v.X < map.Data.Width && v.Y < map.Data.Height;

    /// <summary>`CancelCast` (Esc) o cancelación por otra acción: sin coste.</summary>
    public void Cancel(Actor caster, MapInstance map, TickContext ctx)
    {
        if (caster.Combat.Cast is { } cast) EndCast(caster, cast, CastResults.Cancelled, null, map, ctx);
    }

    /// <summary>Efecto `interrupt`: corta el casteo y bloquea `interruptLockoutMs` (aunque el objetivo sea inmune a controles).</summary>
    public void Interrupt(Actor target, MapInstance map, TickContext ctx)
    {
        if (target.Combat.Cast is not { } cast) return;
        EndCast(target, cast, CastResults.Interrupted, null, map, ctx);
        target.Combat.LockoutEndsAtMs = ctx.NowMs + ctx.Rules.Combat.InterruptLockoutMs;
    }

    public void InterruptByControl(Actor target, AuraKind kind, MapInstance map, TickContext ctx)
    {
        if (target.Combat.Cast is not { } cast) return;
        if (kind == AuraKind.Stun || (kind == AuraKind.Silence && cast.Spell.School == School.Magic)) Interrupt(target, map, ctx);
    }

    private static void EndCast(Actor caster, CastState cast, string result, string? reason, MapInstance map, TickContext ctx)
    {
        caster.Combat.Cast = null;
        ctx.Emit(new CastEndedEvent(map.Id, caster, cast.Spell, result, reason));
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        ResolveImpacts(map, ctx);
        _casting.Clear();
        foreach (var a in map.Actors.Values) if (a.Combat.Cast is not null) _casting.Add(a);
        foreach (var caster in _casting)
        {
            var cast = caster.Combat.Cast;
            if (cast is null) continue;
            if (caster.IsDead) { EndCast(caster, cast, CastResults.Cancelled, null, map, ctx); continue; }
            if (ctx.NowMs < cast.EndsAtMs) continue;
            var error = Revalidate(caster, cast, map, ctx);
            if (error is not null) { EndCast(caster, cast, CastResults.Failed, error, map, ctx); continue; }
            caster.Combat.Cast = null;
            ctx.Emit(new CastEndedEvent(map.Id, caster, cast.Spell, CastResults.Done, null));
            Resolve(caster, cast.Spell, cast.TargetId, cast.TargetPos, cast.Origin, map, ctx);
        }
    }

    /// <summary>Fin de casteo a un objetivo: alcance + tolerancia, LOS, vivo y recurso. Áreas y saltos no se revalidan (punto fijo).</summary>
    private string? Revalidate(Actor caster, CastState cast, MapInstance map, TickContext ctx)
    {
        var spell = cast.Spell;
        if (caster is Player p && spell.Cost is { Amount: > 0 } cost && p.Resource < cost.Amount) return CastErrors.NotEnoughResource;
        if (spell.Targeting is Targeting.Enemy or Targeting.Ally && cast.TargetId is { } id)
        {
            var target = map.Find(id);
            if (target is null || target.IsDead) return CastErrors.InvalidTarget;
            if (ReferenceEquals(target, caster)) return null;
            if (Vec2.Distance(caster.Position, target.Position) > spell.Range + ctx.Rules.Combat.CastRangeToleranceTiles) return CastErrors.OutOfRange;
            if (!LineOfSight.Has(map.Data.Collision, caster.Position, target.Position)) return CastErrors.NoLos;
        }
        return null;
    }

    /// <summary>Descuenta recurso, inicia cooldown y aplica (o programa el impacto del proyectil).</summary>
    private void Resolve(Actor caster, SpellDef spell, EntityId? targetId, Vec2? targetPos, Vec2 origin, MapInstance map, TickContext ctx)
    {
        if (caster is Player p && spell.Cost is { Amount: > 0 } cost) damage.Spend(p, cost.Amount, ctx);
        if (spell.CooldownMs > 0)
        {
            caster.Combat.CooldownEndsAtMs[spell.Id] = ctx.NowMs + spell.CooldownMs;
            ctx.Emit(new CooldownEvent(map.Id, caster, spell.Id, spell.CooldownMs, null));
        }
        if (spell.Projectile is { Speed: > 0 } proj && targetId is { } tid && map.Find(tid) is { } target && !ReferenceEquals(target, caster))
        {
            var travelMs = (long)Math.Round(Vec2.Distance(caster.Position, target.Position) / proj.Speed * 1000);
            if (!_impacts.TryGetValue(map.Id, out var list)) _impacts[map.Id] = list = new List<PendingImpact>();
            if (list.Count >= ctx.Rules.Limits.MaxPendingImpactsPerInstance) list.RemoveAt(0);
            list.Add(new PendingImpact(ctx.NowMs + travelMs, caster, spell, targetId, targetPos, origin));
            return;
        }
        effects.Apply(caster, spell, targetId, targetPos, origin, map, ctx);
    }

    private void ResolveImpacts(MapInstance map, TickContext ctx)
    {
        if (!_impacts.TryGetValue(map.Id, out var list) || list.Count == 0) return;
        for (var i = 0; i < list.Count; i++)
        {
            var imp = list[i];
            if (imp.AtMs > ctx.NowMs) continue;
            list.RemoveAt(i--);
            // Se resuelve aunque el lanzador haya muerto; si el objetivo murió, se descarta.
            if (imp.TargetId is { } tid && (map.Find(tid) is not { } t || t.IsDead)) continue;
            effects.Apply(imp.Caster, imp.Spell, imp.TargetId, imp.TargetPos, imp.Origin, map, ctx);
        }
    }
}
