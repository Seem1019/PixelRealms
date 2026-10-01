namespace PixelRealms.Server.Auth;

/// <summary>Filtro de endpoint: exige `Authorization: Bearer <jwt>` válido y deja los claims en HttpContext.Items.</summary>
public static class JwtAuth
{
    public const string ItemKey = "pixelrealms.claims";

    public static JwtClaims? ClaimsOf(HttpContext http) => http.Items.TryGetValue(ItemKey, out var c) ? c as JwtClaims : null;

    public static RouteHandlerBuilder RequireJwt(this RouteHandlerBuilder builder) => builder.AddEndpointFilter(async (ctx, next) =>
    {
        var http = ctx.HttpContext;
        var jwt = http.RequestServices.GetRequiredService<JwtService>();
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Results.Unauthorized();
        var claims = jwt.Validate(header["Bearer ".Length..].Trim(), DateTimeOffset.UtcNow);
        if (claims is null) return Results.Unauthorized();
        http.Items[ItemKey] = claims;
        return await next(ctx);
    });
}
