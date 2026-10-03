namespace PixelRealms.Server.Hosting;

/// <summary>Opciones técnicas del host (appsettings `Net`). Los números de juego NO van aquí (ADR-008).</summary>
public sealed class NetOptions
{
    public const string Section = "Net";

    /// <summary>Sin tráfico durante este tiempo → el servidor cierra la conexión (HU-006 CA5).</summary>
    public double IdleTimeoutSec { get; set; } = 15;

    /// <summary>Flag de desarrollo: hasta HU-014 se permite conectar a /ws sin ticket.</summary>
    public bool RequireTicket { get; set; }

    /// <summary>HU-071: límites por conexión y por IP.</summary>
    public Net.RateLimitOptions RateLimits { get; set; } = new();

    /// <summary>
    /// HU-073: redes (CIDR) de las que se acepta `X-Forwarded-For` en producción. Sin configurar: loopback y rangos privados, que es
    /// donde vive el contenedor de Caddy en la red de compose. Una petición desde otra IP no puede fingir su origen.
    /// </summary>
    public string[]? TrustedProxyNetworks { get; set; }

    public static readonly string[] DefaultTrustedProxyNetworks = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];
}

/// <summary>Opciones técnicas de persistencia (appsettings `Persistence`).</summary>
public sealed class PersistenceOptions
{
    public const string Section = "Persistence";

    /// <summary>Cada cuánto se guarda un jugador con cambios (`Dirty`), HU-026 CA3. Infraestructura, no regla de juego.</summary>
    public double AutosaveSec { get; set; } = 60;
}
