namespace PixelRealms.Game.Core;

/// <summary>
/// Solo constantes técnicas (skill dotnet-server §Estilo). Todo número de juego sale de <c>rules.json</c> vía IRules (ADR-008).
/// </summary>
public static class GameConstants
{
    /// <summary>Duración de un tick de simulación: 20 Hz.</summary>
    public const int TickMs = 50;

    /// <summary>Un snapshot cada 2 ticks (10 Hz).</summary>
    public const int SnapshotEveryTicks = 2;

    /// <summary>Ticks atrasados que el loop recupera como máximo antes de descartar tiempo (evita la espiral de la muerte).</summary>
    public const int MaxCatchUpTicks = 3;

    /// <summary>Un tick que supera este tiempo se loguea como warn.</summary>
    public const int SlowTickWarnMs = 50;

    /// <summary>Píxeles por casilla (docs/protocol.md).</summary>
    public const int PixelsPerTile = 16;
}
