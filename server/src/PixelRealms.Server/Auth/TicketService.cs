using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PixelRealms.Server.Auth;

public sealed record GameTicket(Guid AccountId, Guid CharacterId, DateTimeOffset ExpiresAt);

/// <summary>Tickets de un solo uso para /ws?ticket= (HU-014): 32 bytes aleatorios en base64url, 30 s de vida.</summary>
public sealed class TicketService(TimeSpan lifetime)
{
    private readonly ConcurrentDictionary<string, GameTicket> _tickets = new(StringComparer.Ordinal);

    public TimeSpan Lifetime { get; } = lifetime;

    public string Issue(Guid accountId, Guid characterId, DateTimeOffset now)
    {
        Sweep(now);
        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _tickets[ticket] = new GameTicket(accountId, characterId, now + Lifetime);
        return ticket;
    }

    /// <summary>Consume el ticket (un solo uso). Null si no existe o expiró.</summary>
    public GameTicket? Redeem(string ticket, DateTimeOffset now)
    {
        if (!_tickets.TryRemove(ticket, out var t)) return null;
        return t.ExpiresAt <= now ? null : t;
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (k, v) in _tickets)
            if (v.ExpiresAt <= now) _tickets.TryRemove(k, out _);
    }
}
