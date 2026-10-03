namespace PixelRealms.Game.Core;

/// <summary>Duraciones de tick de la última ventana para calcular p50/p99 sin asignar en cada tick (buffer circular fijo).</summary>
public sealed class TickStats(int capacity = 600)
{
    private readonly double[] _samples = new double[capacity];
    private int _count;
    private int _next;

    public int Count => _count;

    public double MaxMs { get; private set; }

    public void Record(double ms)
    {
        _samples[_next] = ms;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        if (ms > MaxMs) MaxMs = ms;
    }

    /// <summary>Copia propia en cada llamada (no por tick): `/health`, `/admin/stats` y el log del loop la piden desde hilos distintos.</summary>
    public (double P50, double P99) Percentiles()
    {
        var count = _count;
        if (count == 0) return (0, 0);
        var sorted = new double[count];
        Array.Copy(_samples, sorted, count);
        Array.Sort(sorted);
        return (sorted[Index(0.50, count)], sorted[Index(0.99, count)]);
    }

    public void Reset()
    {
        _count = 0;
        _next = 0;
        MaxMs = 0;
    }

    private static int Index(double p, int count) => Math.Clamp((int)Math.Ceiling(p * count) - 1, 0, count - 1);
}
