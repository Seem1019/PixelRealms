using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Tabla de amenaza de un monstruo (combat.md §Amenaza): daño ×threatPerDamage, cura ×threatPerHeal repartida; cambio de
/// objetivo solo si otro supera 110 % (melee) / 130 % (a distancia) de la amenaza del actual; `taunt` fija al lanzador. Un
/// monstruo con `threatTarget: lowest` (HU-117) va a por quien menos amenaza tiene, con la misma regla al revés.
/// </summary>
public sealed class ThreatTable
{
    private readonly Dictionary<int, double> _threat = new();

    public EntityId? Current { get; private set; }

    /// <summary>Hasta cuándo el objetivo está fijado por Provocar.</summary>
    public long TauntedUntilMs { get; private set; } = long.MinValue;

    public int Count => _threat.Count;

    public bool Contains(EntityId id) => _threat.ContainsKey(id.Value);

    public double Of(EntityId id) => _threat.GetValueOrDefault(id.Value);

    public Dictionary<int, double>.KeyCollection Ids => _threat.Keys;

    public void Add(EntityId id, double amount)
    {
        _threat[id.Value] = Of(id) + amount;
        Current ??= id;
    }

    /// <summary>Quita a `id` de la tabla. Si era el objetivo, se suelta también la fijación de Provocar: el siguiente
    /// <see cref="Reevaluate(long, double, Func{EntityId, bool}, ThreatTarget)"/> elige de nuevo entre los válidos.</summary>
    public void Remove(EntityId id)
    {
        _threat.Remove(id.Value);
        if (Current != id) return;
        Current = null;
        TauntedUntilMs = long.MinValue;
    }

    public void Clear()
    {
        _threat.Clear();
        Current = null;
        TauntedUntilMs = long.MinValue;
    }

    /// <summary>Quien más amenaza tiene (o null).</summary>
    public EntityId? Top()
    {
        EntityId? best = null;
        var bestValue = double.MinValue;
        foreach (var (id, v) in _threat) if (v > bestValue) { bestValue = v; best = new EntityId(id); }
        return best;
    }

    /// <summary>Provocar: amenaza = máx · (1 + tauntThreatBonus) y objetivo fijado `durationMs`.</summary>
    public void Taunt(EntityId id, long nowMs, int durationMs, double tauntThreatBonus)
    {
        var max = 0.0;
        foreach (var v in _threat.Values) if (v > max) max = v;
        _threat[id.Value] = Math.Max(Of(id), max * (1 + tauntThreatBonus));
        Current = id;
        TauntedUntilMs = nowMs + durationMs;
    }

    /// <summary>Reevalúa el objetivo con la regla 110 %/130 %; devuelve el objetivo tras la evaluación. Solo elige entre los
    /// candidatos válidos (`isValid`), sin tocar a los demás; el objetivo actual que deja de ser válido sale de la tabla.</summary>
    /// <param name="pick">`Lowest` (HU-117): va a por quien menos amenaza tiene y cambia cuando la del actual llega al
    /// `switchMultiplier` de la del que menos tiene (la misma regla, al revés).</param>
    public EntityId? Reevaluate(long nowMs, double switchMultiplier, Func<EntityId, bool> isValid, ThreatTarget pick = ThreatTarget.Highest) =>
        Reevaluate(nowMs, switchMultiplier, isValid, static (id, f) => f(id), pick);

    /// <summary>Variante sin closure (HU-088 CA1): el estado viaja como argumento para no asignar un delegado por monstruo y tick.</summary>
    public EntityId? Reevaluate<TState>(long nowMs, double switchMultiplier, TState state, Func<EntityId, TState, bool> isValid,
        ThreatTarget pick = ThreatTarget.Highest)
    {
        if (Current is { } cur && !isValid(cur, state)) Remove(cur);
        if (nowMs < TauntedUntilMs && Current is not null) return Current;
        var best = BestValid(pick, state, isValid);
        if (best is null) { Current = null; return null; }
        if (Current is null) { Current = best; return best; }
        if (best == Current) return Current;
        var change = pick == ThreatTarget.Lowest
            ? Of(Current.Value) >= Of(best.Value) * switchMultiplier - 1e-9
            : Of(best.Value) >= Of(Current.Value) * switchMultiplier - 1e-9;
        if (change) Current = best;
        return Current;
    }

    /// <summary>El de más (o menos) amenaza entre los válidos; un candidato inválido nunca pasa a ser el objetivo (revisión de
    /// autoridad, HU-117: un señuelo lejano o fuera del mapa con poca amenaza sacaba a los retoños de su correa).</summary>
    private EntityId? BestValid<TState>(ThreatTarget pick, TState state, Func<EntityId, TState, bool> isValid)
    {
        var lowest = pick == ThreatTarget.Lowest;
        EntityId? best = null;
        var bestValue = lowest ? double.MaxValue : double.MinValue;
        foreach (var (id, v) in _threat)
        {
            if (lowest ? v >= bestValue : v <= bestValue) continue;
            var e = new EntityId(id);
            if (!isValid(e, state)) continue;
            best = e;
            bestValue = v;
        }
        return best;
    }
}
