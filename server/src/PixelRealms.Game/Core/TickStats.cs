namespace PixelRealms.Game.Core;

/// <summary>Duraciones de tick de la última ventana para calcular p50/p99 sin asignar en cada tick (buffer circular fijo).</summary>
public sealed class TickStats(int capacity = 600)
{
    private readonly double[] _samples = new double[capacity];
    private readonly double[] _sorted = new double[capacity];
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

    public (double P50, double P99) Percentiles()
    {
        if (_count == 0) return (0, 0);
        Array.Copy(_samples, _sorted, _count);
        Array.Sort(_sorted, 0, _count);
        return (_sorted[Index(0.50)], _sorted[Index(0.99)]);
    }

    public void Reset()
    {
        _count = 0;
        _next = 0;
        MaxMs = 0;
    }

    private int Index(double p) => Math.Clamp((int)Math.Ceiling(p * _count) - 1, 0, _count - 1);
}
