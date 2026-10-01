namespace PixelRealms.Server.Hosting;

/// <summary>Opciones técnicas del host (appsettings `Net`). Los números de juego NO van aquí (ADR-008).</summary>
public sealed class NetOptions
{
    public const string Section = "Net";

    /// <summary>Sin tráfico durante este tiempo → el servidor cierra la conexión (HU-006 CA5).</summary>
    public double IdleTimeoutSec { get; set; } = 15;

    /// <summary>Flag de desarrollo: hasta HU-014 se permite conectar a /ws sin ticket.</summary>
    public bool RequireTicket { get; set; }
}
