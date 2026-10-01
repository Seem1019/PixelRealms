using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Aplicación final de daño y curación (skill combat-system, regla de oro 4): `damageDonePct`/`damageTakenPct` → escudo
/// absorbe → vida; eventos del lote del tick; ira (Guerrero), amenaza (monstruos), estado "en combate" y muerte.
/// Puro respecto a la red: solo muta actores y emite eventos.
/// </summary>
public sealed class DamagePipeline(CombatServices services)
{
    /// <summary>Lo fija la composición (dependencia circular con AuraSystem).</summary>
    public AuraSystem? Auras { get; set; }

    public DeathSystem? Death { get; set; }

    /// <summary>Recorte de daño en duelo (HU-064): devuelve el daño que se aplica de verdad; por defecto todo.</summary>
    public Func<Actor, Actor, int, MapInstance, TickContext, int> DuelClamp { get; set; } = static (_, _, dmg, _, _) => dmg;

    /// <summary>Aplica daño ya calculado (sin tabla de impacto). Devuelve el daño que entró en vida.</summary>
    public int Deal(Actor source, Actor target, int amount, School school, bool crit, string? spellId, MapInstance map, TickContext ctx)
    {
        if (target.IsDead || target.Combat.Evading) return 0;
        var rules = ctx.Rules.Combat;
        var scaled = (int)Math.Round(amount * source.Auras.DamageDoneMultiplier() * target.Auras.DamageTakenMultiplier(), MidpointRounding.AwayFromZero);
        scaled = Math.Max(0, scaled);
        var absorbed = Auras?.Absorb(target, scaled, map, ctx) ?? 0;
        var toHp = scaled - absorbed;
        toHp = DuelClamp(source, target, toHp, map, ctx);
        if (target is Player { GodMode: true }) toHp = 0; // HU-070 /god
        if (absorbed > 0) ctx.Emit(new CombatHitEvent(map.Id, source, target, spellId, HitKinds.Absorb, absorbed, false, school));
        ctx.Emit(new CombatHitEvent(map.Id, source, target, spellId, HitKinds.Damage, toHp, crit, school));

        target.Hp = Math.Max(0, target.Hp - toHp);
        source.EnterCombat(ctx.NowMs);
        target.EnterCombat(ctx.NowMs);
        if (target is Player tp) tp.Dirty = true;
        if (source is Player sp) sp.Dirty = true;

        // Ira: +ragePerHitDealt al impactar, +ragePerHitTaken al recibir (combat.md §Recursos).
        GrantRage(source, rules.RagePerHitDealt, ctx);
        GrantRage(target, rules.RagePerHitTaken, ctx);

        // Amenaza: el monstruo golpeado suma threatPerDamage por punto (incluido lo absorbido: el golpe fue real).
        if (target is Monster m && source is Player && !m.Combat.Evading)
        {
            m.Threat.Add(source.Id, scaled * rules.ThreatPerDamage);
            m.TaggedBy ??= source.Id;
        }

        if (target.Hp <= 0) Death?.Kill(target, source, map, ctx);
        return toHp;
    }

    /// <summary>Cura ya calculada; no excede maxHp. Genera amenaza del sanador hacia los monstruos que tienen al sanado en su tabla.</summary>
    public int Heal(Actor source, Actor target, int amount, bool crit, string? spellId, MapInstance map, TickContext ctx)
    {
        if (target.IsDead) return 0;
        var effective = Math.Max(0, Math.Min(amount, target.MaxHp - target.Hp));
        target.Hp += effective;
        ctx.Emit(new CombatHitEvent(map.Id, source, target, spellId, HitKinds.Heal, effective, crit, School.Magic));
        if (target is Player tp) tp.Dirty = true;
        if (source is Player healer) healer.LastActionAtMs = ctx.NowMs;
        if (effective > 0 && source is not Monster)
        {
            var threat = effective * ctx.Rules.Combat.ThreatPerHeal;
            var receivers = 0;
            foreach (var m in map.Monsters.Values) if (m.Threat.Contains(target.Id) && !m.Combat.Evading) receivers++;
            if (receivers > 0)
                foreach (var m in map.Monsters.Values)
                    if (m.Threat.Contains(target.Id) && !m.Combat.Evading) m.Threat.Add(source.Id, threat / receivers);
        }
        return effective;
    }

    /// <summary>Suma ira a un jugador cuyo recurso sea ira (tope resourceCap).</summary>
    public void GrantRage(Actor actor, double amount, TickContext ctx)
    {
        if (actor is not Player p || services.ResourceOf(p) != Resource.Rage) return;
        AddResource(p, amount, ctx);
    }

    /// <summary>Suma (o resta) recurso con acumulador de fracciones; recorta a [0, max].</summary>
    public void AddResource(Player p, double amount, TickContext ctx)
    {
        var acc = p.Combat.ResourceRegenAcc + amount;
        var whole = (int)Math.Truncate(acc);
        p.Combat.ResourceRegenAcc = acc - whole;
        var before = p.Resource;
        p.Resource = Math.Clamp(p.Resource + whole, 0, p.MaxResource);
        if (p.Resource != before) p.Dirty = true;
    }

    /// <summary>Gasta recurso; si es maná arranca la penalización de regeneración (HU-039 CA1).</summary>
    public void Spend(Player p, int amount, TickContext ctx)
    {
        if (amount <= 0) return;
        p.Resource = Math.Max(0, p.Resource - amount);
        p.Dirty = true;
        if (services.ResourceOf(p) == Resource.Mana)
            p.Combat.ManaPenaltyUntilMs = ctx.NowMs + (long)(ctx.Rules.Combat.ManaRegenPenaltyDurationSec * 1000);
    }
}
