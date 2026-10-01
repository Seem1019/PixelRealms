using PixelRealms.Game.Core;

namespace PixelRealms.Game.Combat;

/// <summary>
/// Tabla de amenaza de un monstruo (combat.md §Amenaza): daño ×threatPerDamage, cura ×threatPerHeal repartida; cambio de
/// objetivo solo si otro supera 110 % (melee) / 130 % (a distancia) de la amenaza del actual; `taunt` fija al lanzador.
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

    public IEnumerable<int> Ids => _threat.Keys;

    public void Add(EntityId id, double amount)
    {
        _threat[id.Value] = Of(id) + amount;
        Current ??= id;
    }

    public void Remove(EntityId id)
    {
        _threat.Remove(id.Value);
        if (Current == id) Current = Top();
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

    /// <summary>Reevalúa el objetivo con la regla 110 %/130 %; devuelve el objetivo tras la evaluación.</summary>
    public EntityId? Reevaluate(long nowMs, double switchMultiplier, Func<EntityId, bool> isValid)
    {
        if (Current is { } cur && !isValid(cur)) { Remove(cur); }
        if (nowMs < TauntedUntilMs && Current is not null) return Current;
        var top = Top();
        if (top is null) { Current = null; return null; }
        if (Current is null) { Current = top; return top; }
        if (top != Current && Of(top.Value) >= Of(Current.Value) * switchMultiplier - 1e-9) Current = top;
        return Current;
    }

    /// <summary>Ids con amenaza ordenados de mayor a menor (para `random_not_top_threat`).</summary>
    public List<EntityId> Ranked()
    {
        var list = new List<EntityId>(_threat.Count);
        foreach (var kv in _threat.OrderByDescending(k => k.Value)) list.Add(new EntityId(kv.Key));
        return list;
    }
}
