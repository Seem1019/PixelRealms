using PixelRealms.Game.Core;

namespace PixelRealms.Game.Tests.Helpers;

/// <summary>Devuelve los valores dados en orden (y repite el último): fuerza hit/crit/miss en las fórmulas.</summary>
public sealed class FixedRng(params double[] rolls) : IRng
{
    private int _i;

    public double NextDouble()
    {
        var v = rolls[Math.Min(_i, rolls.Length - 1)];
        _i++;
        return v;
    }

    /// <summary>Entero en [min, max) a partir del siguiente roll: min + floor(roll · (max − min)).</summary>
    public int Next(int minInclusive, int maxExclusive) => minInclusive + (int)Math.Floor(NextDouble() * (maxExclusive - minInclusive));
}

/// <summary>Alias semántico del reloj manual del dominio.</summary>
public sealed class FakeClock : IGameClock
{
    public long NowMs { get; private set; }

    public void Advance(long ms) => NowMs += ms;

    public void Set(long ms) => NowMs = ms;
}
