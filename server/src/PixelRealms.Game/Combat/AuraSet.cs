using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Aura activa sobre un actor: instancia = (aura, lanzador) (ADR-022). La cantidad por tick y la mitigación de un DoT físico se
/// fijan al aplicarse (snapshot). El ritmo de ticks (`NextTickAtMs`) no se reinicia al renovar. Las instancias salen de la reserva
/// de <see cref="AuraSystem"/> (HU-088 CA1): una quitada se reutiliza a partir del tick siguiente, nunca en el mismo.
/// </summary>
public sealed class AuraInstance
{
    public AuraDef Def { get; internal set; } = null!;

    public EntityId? CasterId { get; private set; }

    public long AppliedAtMs { get; private set; }

    public long ExpiresAtMs { get; set; }

    public long NextTickAtMs { get; set; }

    public int Stacks { get; set; }

    /// <summary>Cantidad por tick (DoT/HoT) o absorción restante (escudo), por carga.</summary>
    public double Amount { get; set; }

    /// <summary>Absorción que le queda a un escudo.</summary>
    public double ShieldRemaining { get; set; }

    /// <summary>Mitigación por armadura calculada al aplicar un DoT físico (snapshot, combat.md §Tabla de impacto).</summary>
    public double Mitigation { get; set; }

    /// <summary>Tick en que `AuraSystem.Tick` ya la procesó: si un tick quita auras de la lista, el recorrido vuelve a empezar
    /// sin repetir las ya hechas.</summary>
    internal long ProcessedTick { get; set; } = -1;

    public string AuraId => Def.Id;

    public AuraKind Kind => Def.Kind;

    public int RemainingMs(long nowMs) => (int)Math.Max(0, ExpiresAtMs - nowMs);

    public bool SameInstance(string auraId, EntityId? casterId) => Def.Id == auraId && CasterId == casterId;

    /// <summary>Deja la instancia como recién aplicada (nueva o sacada de la reserva).</summary>
    internal AuraInstance Reset(AuraDef def, EntityId? casterId, long appliedAtMs)
    {
        Def = def;
        CasterId = casterId;
        AppliedAtMs = appliedAtMs;
        ExpiresAtMs = appliedAtMs + def.DurationMs;
        NextTickAtMs = def.TickMs > 0 ? appliedAtMs + def.TickMs : long.MaxValue;
        Stacks = 1;
        Amount = 0;
        ShieldRemaining = 0;
        Mitigation = 0;
        ProcessedTick = -1;
        return this;
    }
}

/// <summary>
/// Auras de un actor y las consultas que hacen los demás sistemas (control, velocidad, modificadores de daño). Las reglas
/// de topes, acumulación y "manda el más fuerte" están en <see cref="AuraSystem"/>; aquí solo se consulta.
/// </summary>
public sealed class AuraSet
{
    private readonly List<AuraInstance> _auras = new(8);

    public IReadOnlyList<AuraInstance> All => _auras;

    public int Count => _auras.Count;

    internal List<AuraInstance> Mutable => _auras;

    public AuraInstance? Find(string auraId, EntityId? casterId)
    {
        foreach (var a in _auras) if (a.SameInstance(auraId, casterId)) return a;
        return null;
    }

    public bool HasKind(AuraKind kind)
    {
        foreach (var a in _auras) if (a.Kind == kind) return true;
        return false;
    }

    /// <summary>Stun: no se mueve, no castea, no ataca. Root: no se mueve. Silence: no castea `magic` (HU-035 CA3).</summary>
    public bool IsStunned => HasKind(AuraKind.Stun);

    public bool IsRooted => HasKind(AuraKind.Root);

    public bool IsSilenced => HasKind(AuraKind.Silence);

    /// <summary>¿Alguna aura activa da inmunidad a ese tipo (Carrera: root, slow)?</summary>
    public bool ImmuneByAura(AuraKind kind)
    {
        foreach (var a in _auras) foreach (var k in a.Def.ImmuneKinds) if (k == kind) return true;
        return false;
    }

    /// <summary>Mayor bonificación de velocidad (speedPct > 0) entre las auras: no se suman, manda la más fuerte (ADR-022).</summary>
    public double MaxSpeedBonus()
    {
        var best = 0.0;
        foreach (var a in _auras) if (a.Def.Mods is { SpeedPct: > 0 } m && m.SpeedPct > best) best = m.SpeedPct;
        return best;
    }

    /// <summary>Mayor ralentización activa (`slow.pct`); el tope `maxSlowPct` lo aplica quien la usa.</summary>
    public double MaxSlow()
    {
        var best = 0.0;
        foreach (var a in _auras) if (a.Kind == AuraKind.Slow && a.Def.Pct > best) best = a.Def.Pct;
        return best;
    }

    /// <summary>Velocidad resultante: base · (1 + mayorBonus) · (1 − min(mayorRalentización, maxSlowPct)).</summary>
    public double SpeedMultiplier(double maxSlowPct) => (1 + MaxSpeedBonus()) * (1 - Math.Min(MaxSlow(), maxSlowPct));

    /// <summary>Multiplicador de daño hecho: manda el modificador más fuerte en valor absoluto (ADR-022).</summary>
    public double DamageDoneMultiplier() => 1 + Strongest(static m => m.DamageDonePct);

    public double DamageTakenMultiplier() => 1 + Strongest(static m => m.DamageTakenPct);

    private double Strongest(Func<AuraMods, double> pick)
    {
        var best = 0.0;
        foreach (var a in _auras)
        {
            if (a.Def.Mods is null) continue;
            var v = pick(a.Def.Mods);
            if (Math.Abs(v) > Math.Abs(best)) best = v;
        }
        return best;
    }

    /// <summary>Suma de `mods.stats` de las auras (stat_mod con stats primarios; no hay ninguna en el contenido actual).</summary>
    public Stats StatMods()
    {
        var total = Stats.Zero;
        foreach (var a in _auras) if (a.Def.Mods?.Stats is { } s) total += s * a.Stacks;
        return total;
    }

    /// <summary>¿Es el aura que manda en su tipo de modificador? Las demás se muestran en gris (HU-035 CA11, HU-038 CA4b, ADR-022:
    /// velocidad, ralentización, daño hecho y daño recibido).</summary>
    public bool IsDominant(AuraInstance aura)
    {
        if (aura.Kind == AuraKind.Slow) return aura.Def.Pct >= MaxSlow();
        if (aura.Def.Mods is not { } m) return true;
        if (m.SpeedPct > 0 && m.SpeedPct < MaxSpeedBonus()) return false;
        if (m.DamageDonePct != 0 && Math.Abs(m.DamageDonePct) < Math.Abs(Strongest(static x => x.DamageDonePct))) return false;
        if (m.DamageTakenPct != 0 && Math.Abs(m.DamageTakenPct) < Math.Abs(Strongest(static x => x.DamageTakenPct))) return false;
        return true;
    }
}
