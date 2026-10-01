namespace PixelRealms.Game.Core;

/// <summary>Fuente de azar inyectada (regla 3). <see cref="NextDouble"/> ∈ [0, 1); <see cref="Next"/> ∈ [min, max).</summary>
public interface IRng
{
    double NextDouble();

    int Next(int minInclusive, int maxExclusive);
}

/// <summary>Azar reproducible por semilla (xoshiro vía System.Random con semilla fija).</summary>
public sealed class SeededRng(int seed) : IRng
{
    private readonly Random _random = new(seed);

    public double NextDouble() => _random.NextDouble();

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
