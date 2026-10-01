namespace PixelRealms.Game.Core;

/// <summary>Reloj del mundo en milisegundos de simulación. El dominio nunca usa DateTime (regla 3 de CLAUDE.md).</summary>
public interface IGameClock
{
    long NowMs { get; }
}

/// <summary>Reloj que avanza con los ticks (lo mueve el GameLoop). También sirve como FakeClock en tests.</summary>
public sealed class TickClock : IGameClock
{
    public long NowMs { get; private set; }

    public void Advance(long ms) => NowMs += ms;

    public void Set(long ms) => NowMs = ms;
}
