using PixelRealms.Content;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Auth;

namespace PixelRealms.Server.Api;

/// <summary>HU-012 Crear personaje · HU-013 Listar y borrar · HU-014 ticket de entrada.</summary>
public static class CharacterEndpoints
{
    public static void Map(WebApplication app)
    {
        var chars = app.MapGroup("/api/characters");

        chars.MapGet("/", async (HttpContext http, ICharacterRepository repo, CancellationToken ct) =>
        {
            var claims = JwtAuth.ClaimsOf(http)!;
            var list = await repo.ListByAccountAsync(claims.AccountId, ct);
            return Results.Ok(list.Select(c => new CharacterResponse(c.Id, c.Name, c.ClassId, c.Level, c.MapId)));
        }).RequireJwt();

        chars.MapPost("/", async (HttpContext http, CreateCharacterRequest req, ICharacterRepository repo, ReloadableContent content, CharacterFactory factory, CancellationToken ct) =>
        {
            var claims = JwtAuth.ClaimsOf(http)!;
            if (Validation.CharacterName(req.Name) is { } err)
                return err.Code == "reserved" ? Results.BadRequest(new ApiError("reserved_name", err.Message)) : Results.BadRequest(new { errors = new[] { err } });
            if (string.IsNullOrEmpty(req.ClassId) || !content.Current.TryGetClass(req.ClassId, out var cls))
                return Results.BadRequest(new ApiError("invalid_class", "Clase desconocida"));
            if (await repo.CountByAccountAsync(claims.AccountId, ct) >= Validation.MaxCharactersPerAccount)
                return Results.BadRequest(new ApiError("max_characters", $"Máximo {Validation.MaxCharactersPerAccount} personajes por cuenta"));
            if (await repo.NameExistsAsync(req.Name!, ct))
                return Results.Conflict(new ApiError("name_taken", "Ese nombre ya está en uso"));
            var created = await repo.CreateAsync(factory.Create(claims.AccountId, req.Name!, cls!), ct);
            if (created is null) return Results.Conflict(new ApiError("name_taken", "Ese nombre ya está en uso"));
            return Results.Created($"/api/characters/{created.Id}", new CharacterResponse(created.Id, created.Name, created.ClassId, created.Level, created.MapId));
        }).RequireJwt();

        chars.MapDelete("/{id:guid}", async (HttpContext http, Guid id, ICharacterRepository repo, CancellationToken ct) =>
        {
            var claims = JwtAuth.ClaimsOf(http)!;
            // 404 también para personajes de otra cuenta: no filtra existencia (HU-013 CA3).
            return await repo.SoftDeleteAsync(claims.AccountId, id, ct) ? Results.NoContent() : Results.NotFound(new ApiError("not_found", "Personaje no encontrado"));
        }).RequireJwt();

        app.MapPost("/api/game/ticket", async (HttpContext http, TicketRequest req, ICharacterRepository repo, TicketService tickets, CancellationToken ct) =>
        {
            var claims = JwtAuth.ClaimsOf(http)!;
            var list = await repo.ListByAccountAsync(claims.AccountId, ct);
            if (!list.Any(c => c.Id == req.CharacterId)) return Results.NotFound(new ApiError("not_found", "Personaje no encontrado"));
            var ticket = tickets.Issue(claims.AccountId, req.CharacterId, DateTimeOffset.UtcNow, claims.Admin);
            return Results.Ok(new TicketResponse(ticket, (int)tickets.Lifetime.TotalSeconds));
        }).RequireJwt();
    }
}
