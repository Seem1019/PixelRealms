using Microsoft.AspNetCore.RateLimiting;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Auth;

namespace PixelRealms.Server.Api;

/// <summary>HU-010 Registro · HU-011 Inicio de sesión.</summary>
public static class AuthEndpoints
{
    public const string LoginPolicy = "login";
    public const string RegisterPolicy = "register";

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
            if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password)) return invalid;
            var account = await accounts.FindByUsernameAsync(req.Username, ct);
            if (account is null || !passwords.Verify(account.PasswordHash, req.Password)) return invalid;
            var now = DateTimeOffset.UtcNow;
            await accounts.TouchLastLoginAsync(account.Id, now.UtcDateTime, ct);
            var (token, exp) = jwt.Issue(account.Id, account.Username, account.IsAdmin, now);
            return Results.Ok(new LoginResponse(token, exp));
        }).RequireRateLimiting(LoginPolicy);
    }

    /// <summary>Límites por IP (HU-010: 5 registros/hora · HU-011 CA3: 5 intentos/min).</summary>
    public static void AddRateLimiting(IServiceCollection services) => services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(LoginPolicy, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            Ip(http), _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        o.AddPolicy(RegisterPolicy, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            Ip(http), _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    });

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
