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
    public const string InCombat = "in_combat";
    public const string PvpNotAllowed = "pvp_not_allowed";
    public const string Forbidden = "forbidden";
}

/// <summary>
/// Paso 4 del tick (HU-033): valida e inicia casteos (skill combat-system §Pipeline), los avanza, revalida al terminar los de
/// un objetivo (alcance + `castRangeToleranceTiles`, LOS, vivo, recurso), descuenta recurso y cooldown al resolver, programa
/// impactos de proyectil (`distancia / speed`) y los resuelve aunque el lanzador muera. GCD y `abilityLockMs` (ADR-019).
/// </summary>
public sealed class CastSystem(CombatServices services, EffectResolver effects, DamagePipeline damage) : IMapSystem, ICastInterrupter
{
    private readonly record struct PendingImpact(long AtMs, Actor Caster, SpellDef Spell, EntityId? TargetId, Vec2? TargetPos, Vec2 Origin);

    private readonly Dictionary<int, List<PendingImpact>> _impacts = new();
    private readonly List<Actor> _casting = new(32);
    private readonly List<Actor> _flying = new(8);

    public string Name => "casts";

    public int PendingImpacts(MapInstance map) => _impacts.TryGetValue(map.Id, out var l) ? l.Count : 0;

    /// <summary>
    /// Áreas apuntadas en curso en la instancia: casteos `ground_*` con su marca en el suelo (los saltos no cuentan). Son las
    /// "áreas" de `rules.limits.maxAreasPerInstance` y de `/admin/stats` (HU-072 CA3); en la Fase 1 no hay áreas duraderas.
    /// </summary>
    public int ActiveAreas(MapInstance map)
    {
        var n = 0;
        foreach (var a in map.Actors.Values)
            if (a.Combat.Cast is { } c && c.Spell.Targeting.IsGround() && !HasLeap(c.Spell)) n++;
        return n;
    }

    private static bool HasLeap(SpellDef spell)
    {
        foreach (var e in spell.Effects) if (e.Type == EffectType.Leap) return true;
        return false;
    }

    /// <summary>Descarta los impactos en vuelo de quien sale del mundo (HU-015): no resuelven a nombre de un ausente.</summary>
    public void ForgetCaster(Actor caster, MapInstance map)
    {
        if (!_impacts.TryGetValue(map.Id, out var list)) return;
        for (var i = list.Count - 1; i >= 0; i--)
            if (list[i].Caster.Id == caster.Id) list.RemoveAt(i);
    }

    /// <summary>Intenta lanzar; devuelve el código de error o null si el hechizo empezó (o se resolvió si es instantáneo).</summary>
    /// <param name="viaItem">true solo desde `ItemUseService`: un jugador lanza hechizos de objeto únicamente al usar el objeto
    /// (que paga su recarga y consume una unidad); por `CastSpell` serían curas y maná gratis y sin recarga.</param>
    public string? TryBeginCast(Actor caster, SpellDef spell, EntityId? targetId, Vec2? targetPos, MapInstance map, TickContext ctx, bool cancelCurrent = true, bool viaItem = false)
    {
        var rules = ctx.Rules.Combat;
        var now = ctx.NowMs;
        var combat = caster.Combat;

        if (caster is Player p)
        {
            // Un jugador lanza sus hechizos de clase; los de objeto solo al usar el objeto y los de monstruo nunca.
            if (spell.Source == SpellSource.Monster || (spell.Source == SpellSource.Item && !viaItem)) return CastErrors.NotFound;
            if (spell.Source == SpellSource.Class && !p.KnownSpells.Contains(spell.Id)) return CastErrors.NotFound;
            if (EngineCapabilities.UnavailableReason(spell) is not null) return CastErrors.NotFound;
            if (p.Level < spell.LevelReq) return CastErrors.LevelTooLow;
            // HU-104: desde aquí todo (coste, recarga, casteo, alcance, área y efectos) sale del hechizo con su mejora.
            spell = Progression.SpellUpgradeRules.Effective(p, spell, services.Content, ctx.Rules.Progression);
        }
        if (caster.IsDead) return CastErrors.IsDead;
        if (caster.Auras.IsStunned) return CastErrors.Stunned;
        // Silencio y bloqueo por interrupción impiden habilidades de cualquier escuela, no usar objetos: un silenciado puede beber
        // pociones (y atacar con el arma, que no pasa por aquí).
        if (!viaItem && caster.Auras.IsSilenced) return CastErrors.Silenced;
        if (!viaItem && combat.IsLockedOut(now)) return CastErrors.LockedOut;
        if (spell.OutOfCombatOnly && caster.IsInCombat(now, ctx.Rules.Combat.InCombatWindowSec)) return CastErrors.InCombat;
        if (combat.IsOnCooldown(spell.Id, now)) return CastErrors.OnCooldown;
        if (caster is Player && spell.Source != SpellSource.Item && ((combat.IsOnGcd(now) && spell.TriggersGcd) || combat.IsAbilityLocked(now))) return CastErrors.OnGcd;
        var hasLeap = false; EffectDef? dash = null;
        foreach (var e in spell.Effects) { if (e.Type == EffectType.Leap) hasLeap = true; if (e.Type == EffectType.Dash) dash = e; }
        // Enraizado no se mueve: ni salta ni carga (una Carga enraizada lo sacaba de la raíz hasta el objetivo).
        if ((hasLeap || dash is not null) && caster.Auras.IsRooted) return CastErrors.Rooted;

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
                // HU-064 CA6: a otro jugador solo se le ataca en duelo; fuera de él, pvp_not_allowed (no un objetivo inválido sin más).
                if (target is Player && caster is Player && !ReferenceEquals(target, caster) && !target.IsDead && !services.IsEnemy(caster, target)) return CastErrors.PvpNotAllowed;
                if (target is null || target.IsDead || target.Combat.Evading || !services.IsEnemy(caster, target)) return CastErrors.InvalidTarget;
                if (Vec2.Distance(caster.Position, target.Position) > spell.Range + tolerance) return CastErrors.OutOfRange;
                if (dash is not null && Vec2.Distance(caster.Position, target.Position) < dash.MinRange) return CastErrors.OutOfRange;
                if (!LineOfSight.Has(map.Collision, caster.Position, target.Position)) return CastErrors.NoLos;
                break;
            }
            case Targeting.Ally:
            {
                target = targetId is { } id ? map.Find(id) : null;
                if (target is null || target.IsDead || !services.IsAlly(caster, target)) target = caster; // CA6: sin aliado → sobre mí
                if (!ReferenceEquals(target, caster))
                {
                    if (Vec2.Distance(caster.Position, target.Position) > spell.Range) return CastErrors.OutOfRange;
                    if (!LineOfSight.Has(map.Collision, caster.Position, target.Position)) return CastErrors.NoLos;
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
                    // HU-033 CA4 / HU-086 CA7b: tope de marcas en el suelo por instancia (rendimiento del cliente y del servidor).
                    if (!spell.IsInstant && ActiveAreas(map) >= ctx.Rules.Limits.MaxAreasPerInstance) return CastErrors.AreaLimit;
                    if (Vec2.Distance(caster.Position, targetPos!.Value) > spell.Range + rules.CastRangeToleranceTiles) return CastErrors.OutOfRange;
                    // HU-102: en el cono y la línea el punto solo da la dirección (la línea se corta en la primera pared).
                    if (spell.Shape != Shape.Circle) break;
                    if (!LineOfSight.Has(map.Collision, caster.Position, targetPos.Value)) return CastErrors.NoLos;
                    // LineOfSight no mira la casilla de destino: apuntar dentro de un muro alcanzaría a quien está detrás.
                    if (map.Collision.BlocksSight((int)MathF.Floor(targetPos.Value.X), (int)MathF.Floor(targetPos.Value.Y))) return CastErrors.NoLos;
                }
                // Alrededor del lanzador: un cono o una línea usan el punto como dirección, que tiene que estar en el mapa (un punto
                // enorme desbordaba la dirección a cero y el cono pasaba a ser un círculo).
                else if (spell.Shape != Shape.Circle && targetPos is not null && !ValidPoint(targetPos, map)) return CastErrors.InvalidPayload;
                break;
        }
        // Revisión de autoridad (HU-102): el cono y la línea salen en una dirección que se fija ahora, aunque luego gire o se mueva;
        // el punto que viaja en CastStarted y llega a TargetResolver es el mismo para todos. Alrededor del lanzador, un círculo no
        // usa el punto: no se reenvía.
        if (spell.Targeting.IsArea() && spell.Shape != Shape.Circle) targetPos = AimPoint(caster, spell, targetPos);
        else if (spell.Targeting is Targeting.SelfAoeEnemies or Targeting.SelfAoeAllies) targetPos = null;

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
            ctx.Emit(new CastStartedEvent(map.Id, caster, spell, targetId, targetPos, 0, OriginFor(spell, caster)));
            ctx.Emit(new CastEndedEvent(map.Id, caster, spell, CastResults.Done, null));
            Resolve(caster, spell, targetId, targetPos, caster.Position, map, ctx);
            return null;
        }
        combat.Cast = new CastState(spell, now, now + spell.CastMs, targetId, targetPos, caster.Position);
        ctx.Emit(new CastStartedEvent(map.Id, caster, spell, targetId, targetPos, spell.CastMs, OriginFor(spell, caster)));
        return null;
    }

    /// <summary>
    /// Punto canónico de un cono o una línea: origen + dirección · alcance de la forma. La dirección va hacia el punto apuntado o,
    /// si está a menos de un píxel de los pies (o no hay), hacia donde mira el lanzador ahora (como el cliente).
    /// </summary>
    private static Vec2 AimPoint(Actor caster, SpellDef spell, Vec2? aim)
    {
        const float minAimTiles = 1f / GameConstants.PixelsPerTile;
        var d = aim is { } a ? a - caster.Position : Vec2.Zero;
        var dir = d.LengthSquared >= minAimTiles * minAimTiles ? d.Normalized() : AreaShape.FacingVector(caster.Facing);
        var reach = spell.Shape == Shape.Line ? spell.AoeLength : spell.AoeRadius;
        return caster.Position + dir * (float)Math.Max(reach, 1.0);
    }

    /// <summary>HU-102: el cono y la línea se dibujan desde donde estaba el lanzador al empezar, aunque luego se mueva.</summary>
    private static Vec2? OriginFor(SpellDef spell, Actor caster) => spell.Targeting.IsArea() && spell.Shape != Shape.Circle ? caster.Position : null;

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
        if (kind == AuraKind.Stun || (kind == AuraKind.Silence && cast.Spell.Source != SpellSource.Item)) Interrupt(target, map, ctx);
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
        _flying.Clear();
        foreach (var a in map.Actors.Values)
        {
            if (a.Combat.Cast is not null) _casting.Add(a);
            if (a.Combat.Flight is not null) _flying.Add(a);
        }
        foreach (var a in _flying) AdvanceFlight(a, map, ctx);
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
        // Una comida con casteo no termina si entretanto ha entrado en combate (hoy el pan es instantáneo).
        if (spell.OutOfCombatOnly && caster.IsInCombat(ctx.NowMs, ctx.Rules.Combat.InCombatWindowSec)) return CastErrors.InCombat;
        if (spell.Targeting is Targeting.Enemy or Targeting.Ally && cast.TargetId is { } id)
        {
            var target = map.Find(id);
            if (target is null || target.IsDead) return CastErrors.InvalidTarget;
            if (ReferenceEquals(target, caster)) return null;
            if (Vec2.Distance(caster.Position, target.Position) > spell.Range + ctx.Rules.Combat.CastRangeToleranceTiles) return CastErrors.OutOfRange;
            if (!LineOfSight.Has(map.Collision, caster.Position, target.Position)) return CastErrors.NoLos;
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
            // HU-088 CA1: reserva de capacidad fija, el tope de la instancia; no crece porque el tope se respeta abajo.
            if (!_impacts.TryGetValue(map.Id, out var list)) _impacts[map.Id] = list = new List<PendingImpact>(ctx.Rules.Limits.MaxPendingImpactsPerInstance);
            if (list.Count >= ctx.Rules.Limits.MaxPendingImpactsPerInstance)
            {
                // Tope de seguridad: el más antiguo se resuelve ya en vez de perderse (su lanzador ya pagó el coste).
                var oldest = list[0];
                list.RemoveAt(0);
                if (oldest.TargetId is not { } oid || map.Find(oid) is { IsDead: false }) effects.Apply(oldest.Caster, oldest.Spell, oldest.TargetId, oldest.TargetPos, oldest.Origin, map, ctx);
            }
            list.Add(new PendingImpact(ctx.NowMs + travelMs, caster, spell, targetId, targetPos, origin));
            return;
        }
        // HU-087 CA1: un salto con `travelMs` vuela hasta el punto (recortado ya ahora, con colisión y LOS) y resuelve al aterrizar.
        if (LeapOf(spell) is { TravelMs: > 0 } leap && targetPos is { } aim)
        {
            var dest = ForcedMovement.LeapDestination(caster.Position, aim, leap.MaxRange > 0 ? leap.MaxRange : spell.Range, map.Collision);
            var end = ctx.NowMs + leap.TravelMs;
            caster.Combat.Flight = new LeapFlight(spell, caster.Position, dest, ctx.NowMs, end);
            caster.Combat.AbilityLockEndsAtMs = Math.Max(caster.Combat.AbilityLockEndsAtMs, end); // en el aire ni se castea ni se pega
            return;
        }
        effects.Apply(caster, spell, targetId, targetPos, origin, map, ctx);
    }

    private static EffectDef? LeapOf(SpellDef spell)
    {
        foreach (var e in spell.Effects) if (e.Type == EffectType.Leap) return e;
        return null;
    }

    /// <summary>Mueve al saltador en línea recta hacia su destino; al llegar, aplica los efectos del hechizo en el punto de llegada.</summary>
    private void AdvanceFlight(Actor a, MapInstance map, TickContext ctx)
    {
        // El aterrizaje de otro en este mismo tick puede haberlo anulado (p. ej. termina el duelo y ResetTransient).
        if (a.Combat.Flight is not { } f) return;
        if (a.IsDead) { a.Combat.Flight = null; return; }
        if (a is Player p) p.Dirty = true;
        if (ctx.NowMs >= f.EndMs)
        {
            a.Position = f.To;
            a.Combat.Flight = null;
            effects.Apply(a, f.Spell, null, f.To, f.To, map, ctx); // ya está en el destino: el salto del efecto no lo mueve más
            return;
        }
        var t = (float)(ctx.NowMs - f.StartMs) / (f.EndMs - f.StartMs);
        a.Position = f.From + (f.To - f.From) * t; // la recta ya se validó al despegar (casillas libres con LOS)
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
