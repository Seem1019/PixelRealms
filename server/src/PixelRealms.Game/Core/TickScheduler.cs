namespace PixelRealms.Game.Core;

/// <summary>
/// Acumulador de paso fijo (HU-004 CA1/CA2): dado el tiempo real transcurrido decide cuántos ticks ejecutar. Si el loop se
/// retrasa más de <see cref="GameConstants.MaxCatchUpTicks"/> ticks, descarta el tiempo sobrante en vez de intentar recuperarlo
/// (espiral de la muerte). Puro: lo prueban los tests con tiempos simulados.
/// </summary>
public sealed class TickScheduler(int tickMs = GameConstants.TickMs, int maxCatchUp = GameConstants.MaxCatchUpTicks)
{
    private long _accumulatorMs;

    public int TickMs { get; } = tickMs;

    public long DroppedMs { get; private set; }

    /// <summary>Añade tiempo real transcurrido y devuelve cuántos ticks hay que ejecutar ahora.</summary>
    public int Advance(long elapsedMs)
    {
        _accumulatorMs += Math.Max(0, elapsedMs);
        var ticks = (int)(_accumulatorMs / TickMs);
        if (ticks > maxCatchUp)
        {
            DroppedMs += (ticks - maxCatchUp) * (long)TickMs;
            ticks = maxCatchUp;
            _accumulatorMs = _accumulatorMs % TickMs;
        }
        else
        {
            _accumulatorMs -= ticks * (long)TickMs;
        }
        return ticks;
    }

    /// <summary>Milisegundos que faltan para el siguiente tick (para dormir sin derivar).</summary>
    public long MsUntilNextTick => Math.Max(0, TickMs - _accumulatorMs);
}
