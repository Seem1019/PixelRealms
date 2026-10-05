using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>Quien puede cortar un casteo cuando llega un control (lo implementa CastSystem).</summary>
public interface ICastInterrupter
{
    void InterruptByControl(Actor target, AuraKind kind, MapInstance map, TickContext ctx);
}

/// <summary>
/// Paso 5 del tick (HU-035): aplicar/renovar/acumular auras, ticks de DoT/HoT, expiración, escudos, inmunidades.
/// Reglas: ADR-021 (topes 16/16 sin contar controles; la nueva siempre entra), ADR-022 (instancia = (aura, lanzador);
/// renovar no reinicia el ritmo de ticks; inmunidad `hardControlImmunitySec` tras stun/root/silence).
/// </summary>
public sealed class AuraSystem(CombatServices services, DamagePipeline damage) : IMapSystem
{
    public string Name => "auras";

    public ICastInterrupter? Interrupter { get; set; }

    // HU-088 CA1: reserva de instancias. Lo quitado en un tick vuelve a la reserva al empezar el siguiente: los eventos del tick
    // (AuraApplied lleva la instancia) se envían en el post-tick y no deben ver un aura reutilizada.
    private readonly Stack<AuraInstance> _free = new();
    private readonly List<AuraInstance> _released = new();
    private long _releasedInTick = long.MinValue;

    private AuraInstance Rent(AuraDef def, EntityId? casterId, TickContext ctx)
    {
        Recycle(ctx);
        return (_free.Count > 0 ? _free.Pop() : new AuraInstance()).Reset(def, casterId, ctx.NowMs);
    }

    private void Release(AuraInstance aura, TickContext ctx)
    {
        Recycle(ctx);
        _released.Add(aura);
    }

    private void Recycle(TickContext ctx)
    {
        if (ctx.Tick == _releasedInTick) return;
        foreach (var a in _released) _free.Push(a);
        _released.Clear();
        _releasedInTick = ctx.Tick;
    }

    /// <summary>Aplica un aura; devuelve la instancia o null si fue inmune (y emite el evento `immune`).</summary>
    public AuraInstance? Apply(Actor target, AuraDef def, Actor? caster, MapInstance map, TickContext ctx, string? spellId = null)
    {
        var rules = ctx.Rules.Combat;
        var now = ctx.NowMs;
        if (target.IsDead) return null;

        if (IsImmune(target, def.Kind, now, rules))
        {
            ctx.Emit(new CombatHitEvent(map.Id, caster ?? target, target, spellId, HitKinds.Immune, 0, false, def.School ?? School.Magic));
            return null;
        }

        foreach (var kind in def.RemovesKinds) RemoveKind(target, kind, map, ctx);

        var casterId = caster?.Id;
        var existing = target.Auras.Find(def.Id, casterId);
        var casterStats = caster is not null ? services.StatsOf(caster) : null;
        var amount = PerStackAmount(def, casterStats);
        if (caster is Player rankPlayer && spellId is not null && services.Content.TryGetSpell(spellId, out var srcSpell) && srcSpell is { Source: SpellSource.Class })
            amount = PerStackAmount(def with { Base = def.Base * Progression.SpellRanks.BaseMultiplier(ctx.Rules.Progression, rankPlayer.Level) }, casterStats);
        if (existing is not null)
        {
            // Mismo lanzador, misma aura: duración completa sin reiniciar el ritmo de ticks; cargas solo si maxStacks > 1.
            existing.ExpiresAtMs = now + def.DurationMs;
            if (def.MaxStacks > 1) existing.Stacks = Math.Min(def.MaxStacks, existing.Stacks + 1);
            existing.Amount = amount;
            if (def.Kind == AuraKind.Shield) existing.ShieldRemaining = amount;
            if (def.Kind == AuraKind.Dot && def.School == School.Physical && caster is not null)
                existing.Mitigation = CombatCalculator.Mitigation(services.StatsOf(target).Armor, caster.Level, rules);
            ctx.Emit(new AuraAppliedEvent(map.Id, target, existing));
            AfterApply(target, def, map, ctx);
            return existing;
        }

        var isControl = IsControl(def.Kind, rules);
        if (!isControl) EnforceCap(target, def.IsDebuff, map, ctx);

        var aura = Rent(def, casterId, ctx);
        aura.Amount = amount;
        if (def.Kind == AuraKind.Shield) aura.ShieldRemaining = amount;
        if (def.Kind == AuraKind.Dot && def.School == School.Physical && caster is not null)
            aura.Mitigation = CombatCalculator.Mitigation(services.StatsOf(target).Armor, caster.Level, rules);
        // Capacidad fija por actor: los dos topes de ADR-021 más los controles, reservada una vez.
        target.Auras.Mutable.EnsureCapacity(ctx.Rules.Limits.MaxBuffsPerEntity + ctx.Rules.Limits.MaxDebuffsPerEntity + rules.ControlAuraKinds.Count);
        target.Auras.Mutable.Add(aura);
        ctx.Emit(new AuraAppliedEvent(map.Id, target, aura));
        AfterApply(target, def, map, ctx);
        return aura;
    }

    private void AfterApply(Actor target, AuraDef def, MapInstance map, TickContext ctx)
    {
        if (def.Mods?.Stats is not null) StatsChanged(target, map, ctx);
        if (def.Kind is AuraKind.Stun or AuraKind.Silence) Interrupter?.InterruptByControl(target, def.Kind, map, ctx);
    }

    /// <summary>Jefe inmune a `bossImmuneToAuraKinds`; inmunidad tras control fuerte; inmunidad concedida por otra aura (Carrera).</summary>
    public static bool IsImmune(Actor target, AuraKind kind, long nowMs, CombatRules rules)
    {
        if (target is Monster { IsBoss: true } && rules.BossImmuneToAuraKinds.Contains(kind)) return true;
        if (rules.HardControlKinds.Contains(kind) && target.Combat.IsHardControlImmune(nowMs)) return true;
        if (target.Combat.Evading) return true;
        return target.Auras.ImmuneByAura(kind);
    }

    public static bool IsControl(AuraKind kind, CombatRules rules) => rules.ControlAuraKinds.Contains(kind);

    /// <summary>Cantidad por carga: base + apCoef · AP + spCoef · SP del lanzador al aplicar (snapshot).</summary>
    public static double PerStackAmount(AuraDef def, Progression.DerivedStats? casterStats) =>
        def.Base + (casterStats is null ? 0 : def.ApCoef * casterStats.AttackPower + def.SpCoef * casterStats.SpellPower);

    /// <summary>ADR-021: si el grupo (beneficiosas / perjudiciales, sin controles) está lleno, sale la de menos tiempo restante.</summary>
    private void EnforceCap(Actor target, bool isDebuff, MapInstance map, TickContext ctx)
    {
        var limits = ctx.Rules.Limits;
        var cap = isDebuff ? limits.MaxDebuffsPerEntity : limits.MaxBuffsPerEntity;
        var count = 0;
        AuraInstance? shortest = null;
        foreach (var a in target.Auras.All)
        {
            if (a.Def.IsDebuff != isDebuff || IsControl(a.Kind, ctx.Rules.Combat)) continue;
            count++;
            if (shortest is null || a.ExpiresAtMs < shortest.ExpiresAtMs) shortest = a;
        }
        if (count >= cap && shortest is not null) Remove(target, shortest, map, ctx);
    }

    public void Remove(Actor target, AuraInstance aura, MapInstance map, TickContext ctx)
    {
        if (!target.Auras.Mutable.Remove(aura)) return;
        Release(aura, ctx);
        ctx.Emit(new AuraRemovedEvent(map.Id, target, aura.AuraId, aura.CasterId));
        if (aura.Def.Mods?.Stats is not null) StatsChanged(target, map, ctx);
        if (ctx.Rules.Combat.HardControlKinds.Contains(aura.Kind) && !target.Auras.HasKind(aura.Kind))
            target.Combat.HardControlImmuneUntilMs = Math.Max(target.Combat.HardControlImmuneUntilMs, ctx.NowMs + (long)(ctx.Rules.Combat.HardControlImmunitySec * 1000));
    }

    public void RemoveKind(Actor target, AuraKind kind, MapInstance map, TickContext ctx)
    {
        for (var i = target.Auras.Mutable.Count - 1; i >= 0; i--)
            if (target.Auras.Mutable[i].Kind == kind) Remove(target, target.Auras.Mutable[i], map, ctx);
    }

    /// <summary>Recibir daño quita las auras `breaksOnDamage` (comer: la curación se corta).</summary>
    public void BreakOnDamage(Actor target, MapInstance map, TickContext ctx)
    {
        var auras = target.Auras.Mutable;
        for (var i = auras.Count - 1; i >= 0; i--)
            if (i < auras.Count && auras[i].Def.BreaksOnDamage) Remove(target, auras[i], map, ctx);
    }

    /// <summary>Muerte o evasión: se pierden todas las auras (HU-037 CA1) sin conceder inmunidad.</summary>
    public void ClearAll(Actor target, MapInstance map, TickContext ctx)
    {
        var hadStats = false;
        for (var i = target.Auras.Mutable.Count - 1; i >= 0; i--)
        {
            var a = target.Auras.Mutable[i];
            target.Auras.Mutable.RemoveAt(i);
            Release(a, ctx);
            ctx.Emit(new AuraRemovedEvent(map.Id, target, a.AuraId, a.CasterId));
            hadStats |= a.Def.Mods?.Stats is not null;
        }
        if (hadStats) StatsChanged(target, map, ctx);
        else target.MarkStatsDirty();
    }

    /// <summary>HU-042 CA3: un aura que cambia stats primarios recalcula y el panel de personaje se actualiza en vivo (StatsUpdate).</summary>
    private static void StatsChanged(Actor target, MapInstance map, TickContext ctx)
    {
        target.MarkStatsDirty();
        if (target is Player p) ctx.Emit(new Progression.StatsChangedEvent(map.Id, p));
    }

    /// <summary>Escudos: absorben por orden de caducidad (el que caduca antes se gasta primero). Devuelve lo absorbido.</summary>
    public int Absorb(Actor target, int amount, MapInstance map, TickContext ctx)
    {
        if (amount <= 0) return 0;
        var absorbed = 0;
        while (amount > 0)
        {
            AuraInstance? shield = null;
            foreach (var a in target.Auras.All)
                if (a.Kind == AuraKind.Shield && a.ShieldRemaining > 0 && (shield is null || a.ExpiresAtMs < shield.ExpiresAtMs)) shield = a;
            if (shield is null) break;
            var take = (int)Math.Min(amount, Math.Floor(shield.ShieldRemaining));
            if (take <= 0) { Remove(target, shield, map, ctx); continue; }
            shield.ShieldRemaining -= take;
            absorbed += take;
            amount -= take;
            if (shield.ShieldRemaining < 1) Remove(target, shield, map, ctx);
        }
        return absorbed;
    }

    public void Tick(MapInstance map, TickContext ctx)
    {
        var now = ctx.NowMs;
        foreach (var actor in map.Actors.Values)
        {
            var auras = actor.Auras.Mutable;
            if (auras.Count == 0) continue;
            // Ticks primero (el último tick coincide con la expiración y cuenta), luego expiraciones.
            for (var i = 0; i < auras.Count; i++)
            {
                var a = auras[i];
                if (a.ProcessedTick == ctx.Tick) continue;
                a.ProcessedTick = ctx.Tick;
                var count = auras.Count;
                while (a.NextTickAtMs <= now && a.NextTickAtMs <= a.ExpiresAtMs)
                {
                    ApplyTick(actor, a, map, ctx);
                    a.NextTickAtMs += a.Def.TickMs;
                    if (actor.IsDead || !auras.Contains(a)) break; // el tick pudo quitarla (fin de duelo): no seguir aplicándola
                }
                if (actor.IsDead) break;
                // El tick pudo cambiar la lista (fin de duelo, un DoT que corta la comida): se vuelve a recorrer desde el
                // principio y las ya procesadas en este tick se saltan.
                if (auras.Count != count || !ReferenceEquals(auras[i], a)) i = -1;
            }
            for (var i = auras.Count - 1; i >= 0; i--)
                if (auras.Count > i && auras[i].ExpiresAtMs <= now) Remove(actor, auras[i], map, ctx);
        }
    }

    private void ApplyTick(Actor target, AuraInstance a, MapInstance map, TickContext ctx)
    {
        var caster = a.CasterId is { } cid ? map.Find(cid) : null;
        var source = caster ?? target;
        var perTick = a.Amount * a.Stacks;
        switch (a.Kind)
        {
            case AuraKind.Dot:
            {
                // Los ticks no fallan ni critican; el DoT físico usa la mitigación fijada al aplicarse.
                var school = a.Def.School ?? School.Magic;
                var advantage = caster is null ? 1.0 : EffectResolver.ClassAdvantage(caster, target, ctx); // HU-064 CA2: también en duelo
                var amount = (int)Math.Round((school == School.Physical ? perTick * (1 - a.Mitigation) : perTick) * advantage, MidpointRounding.AwayFromZero);
                damage.Deal(source, target, amount, school, false, a.AuraId, map, ctx);
                break;
            }
            case AuraKind.Hot:
                damage.Heal(source, target, (int)Math.Round(perTick, MidpointRounding.AwayFromZero), false, a.AuraId, map, ctx);
                break;
            default:
                break;
        }
    }
}
