namespace PixelRealms.Server.Api;

public sealed record RegisterRequest(string? Username, string? Password);

public sealed record LoginRequest(string? Username, string? Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

public sealed record AccountResponse(Guid Id, string Username);

public sealed record FieldError(string Field, string Code, string Message);

public sealed record ApiError(string Code, string Message);

public sealed record CreateCharacterRequest(string? Name, string? ClassId);

public sealed record CharacterResponse(Guid Id, string Name, string ClassId, int Level, string MapId);

public sealed record TicketRequest(Guid CharacterId);

public sealed record TicketResponse(string Ticket, int ExpiresInSec);
