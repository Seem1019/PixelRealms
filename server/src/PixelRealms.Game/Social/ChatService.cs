using PixelRealms.Content.Defs;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Map;

namespace PixelRealms.Game.Social;

/// <summary>Mensaje de chat a entregar a una lista de jugadores (el servidor lo convierte en ChatMessage).</summary>
public sealed record ChatDeliveredEvent(int MapInstanceId, IReadOnlyList<Player> Recipients, string Channel, string From, string Text) : IGameEvent;

/// <summary>
/// HU-060: canales `say` (jugadores de la misma instancia a ≤ `rules.movement.sayRangeTiles`), `global`, `party`, `whisper`;
/// 1–200 caracteres sin caracteres de control; límite de 5 mensajes por 5 s por jugador (docs/architecture.md §Red).
/// </summary>
public sealed class ChatService
{
    public const int MaxLength = 200;
    public const int RateLimitCount = 5;
    public const int RateLimitWindowMs = 5000;

    private readonly Dictionary<Guid, Queue<long>> _recent = new();

    /// <summary>Normaliza el texto: recorta a 200, quita caracteres de control; null si queda vacío.</summary>
    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var chars = text.Where(c => !char.IsControl(c)).ToArray();
        var clean = new string(chars).Trim();
        if (clean.Length == 0) return null;
        return clean.Length > MaxLength ? clean[..MaxLength] : clean;
    }

    public bool AllowRate(Guid characterId, long nowMs)
    {
        if (!_recent.TryGetValue(characterId, out var q)) _recent[characterId] = q = new Queue<long>();
        while (q.Count > 0 && nowMs - q.Peek() >= RateLimitWindowMs) q.Dequeue();
        if (q.Count >= RateLimitCount) return false;
        q.Enqueue(nowMs);
        return true;
    }

    /// <summary>Resuelve destinatarios; devuelve código de error o null. `allPlayers` = conectados en todas las instancias.</summary>
    public string? Send(Player from, string channel, string? rawText, string? to, MapInstance map, IEnumerable<Player> allPlayers, Func<Guid, Party?> partyOf, TickContext ctx)
    {
        var text = Sanitize(rawText);
        if (text is null) return "invalid_payload";
        List<Player> recipients;
        switch (channel)
        {
            case "say":
            {
                var range = ctx.Rules.Movement.SayRangeTiles;
                recipients = map.Players.Values.Where(p => p.ConnectionId >= 0 && Vec2.Distance(p.Position, from.Position) <= range).ToList();
                break;
            }
            case "global":
                recipients = allPlayers.Where(p => p.ConnectionId >= 0).ToList();
                break;
            case "party":
            {
                var party = partyOf(from.CharacterId);
                if (party is null) return "not_found";
                recipients = allPlayers.Where(p => p.ConnectionId >= 0 && party.Contains(p.CharacterId)).ToList();
                break;
            }
            case "whisper":
            {
                if (string.IsNullOrWhiteSpace(to)) return "invalid_payload";
                var target = allPlayers.FirstOrDefault(p => p.ConnectionId >= 0 && string.Equals(p.Name, to, StringComparison.OrdinalIgnoreCase));
                if (target is null) return "not_found";
                recipients = [target, from];
                break;
            }
            default:
                return "invalid_payload";
        }
        if (!AllowRate(from.CharacterId, ctx.NowMs)) return "rate_limited"; // solo cuentan los mensajes que se envían
        ctx.Emit(new ChatDeliveredEvent(map.Id, recipients, channel, from.Name, text));
        return null;
    }
}
