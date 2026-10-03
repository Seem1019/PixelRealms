using Microsoft.AspNetCore.RateLimiting;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Auth;

namespace PixelRealms.Server.Api;

/// <summary>HU-010 Registro · HU-011 Inicio de sesión.</summary>
public static class AuthEndpoints
{
    public const string LoginPolicy = "login";
    public const string RegisterPolicy = "register";
    public const string RefreshPolicy = "refresh";

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest req, IAccountRepository accounts, Passwords passwords, CancellationToken ct) =>
        {
            var errors = new[] { Validation.Username(req.Username), Validation.Password(req.Password) }.Where(e => e is not null).ToList();
            if (errors.Count > 0) return Results.BadRequest(new { errors });
            var created = await accounts.CreateAsync(req.Username!, passwords.Hash(req.Password!), ct);
            if (created is null) return Results.Conflict(new ApiError("username_taken", "Ese usuario ya existe"));
            return Results.Created($"/api/accounts/{created.Id}", new AccountResponse(created.Id, created.Username));
        }).RequireRateLimiting(RegisterPolicy);

        group.MapPost("/login", async (LoginRequest req, IAccountRepository accounts, Passwords passwords, JwtService jwt, CancellationToken ct) =>
        {
            // Mismo mensaje para usuario inexistente y contraseña errónea (HU-011 CA2).
            var invalid = Results.Json(new ApiError("invalid_credentials", "Usuario o contraseña incorrectos"), statusCode: StatusCodes.Status401Unauthorized);
            // Mismo formato que el registro antes de consultar la BD: ninguna cuenta puede tener otro, y así Postgres (ICU)
            // e InMemory responden igual ("ｂｏｂ" no entra como "bob") y un "\0" no llega a Npgsql.
            if (Validation.Username(req.Username) is not null || string.IsNullOrEmpty(req.Password)) return invalid;
            var account = await accounts.FindByUsernameAsync(req.Username!, ct);
            if (account is null || !passwords.Verify(account.PasswordHash, req.Password)) return invalid;
            var now = DateTimeOffset.UtcNow;
            await accounts.TouchLastLoginAsync(account.Id, now.UtcDateTime, ct);
            var (token, exp) = jwt.Issue(account.Id, account.Username, account.IsAdmin, now);
            return Results.Ok(new LoginResponse(token, exp));
        }).RequireRateLimiting(LoginPolicy);

        // HU-015 CA1: el JWT dura 15 min y una partida dura más; el cliente lo renueva antes de que caduque para volver a la
        // selección de personaje (o reconectar, HU-025) sin pedir la contraseña. Se relee la cuenta: si ya no existe, 401, y el
        // flag de admin sale de la BD, no del token viejo (quitar el admin surte efecto en la siguiente renovación).
        // Tope absoluto (`Auth:MaxSessionHours`, 12 h por defecto): un token filtrado no se mantiene vivo para siempre renovándolo.
        group.MapPost("/refresh", async (HttpContext http, IAccountRepository accounts, JwtService jwt, IConfiguration config, CancellationToken ct) =>
        {
            var claims = JwtAuth.ClaimsOf(http)!;
            var expired = Results.Json(new ApiError("unauthorized", "Tu sesión expiró"), statusCode: StatusCodes.Status401Unauthorized);
            var now = DateTimeOffset.UtcNow;
            if (now - claims.AuthTime > TimeSpan.FromHours(config.GetValue("Auth:MaxSessionHours", 12.0))) return expired;
            var account = await accounts.GetAsync(claims.AccountId, ct);
            if (account is null) return expired;
            var (token, exp) = jwt.Issue(account.Id, account.Username, account.IsAdmin, now, claims.AuthTime);
            return Results.Ok(new LoginResponse(token, exp));
        }).RequireJwt().RequireRateLimiting(RefreshPolicy);
    }

    /// <summary>
    /// Límites por IP (HU-010: 5 registros/hora · HU-011 CA3: 5 intentos/min · renovación 10/min). Configurables en
    /// `Auth:RegisterPerHour`, `Auth:LoginPerMinute` y `Auth:RefreshPerMinute` solo para pruebas de carga (LoadBot --network, que
    /// abre 30 cuentas desde una IP); en producción se dejan los valores por defecto.
    /// </summary>
    public static void AddRateLimiting(IServiceCollection services, IConfiguration config) => services.AddRateLimiter(o =>
    {
        int Limit(string key, int fallback) => config.GetValue(key, fallback);
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(LoginPolicy, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            Ip(http), _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = Limit("Auth:LoginPerMinute", 5), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        o.AddPolicy(RegisterPolicy, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            Ip(http), _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = Limit("Auth:RegisterPerHour", 5), Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
        // El cliente renueva cada 10 min; 10 por minuto e IP sobra para una casa con varios jugadores.
        o.AddPolicy(RefreshPolicy, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            Ip(http), _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = Limit("Auth:RefreshPerMinute", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
