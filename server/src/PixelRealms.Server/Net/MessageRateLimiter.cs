namespace PixelRealms.Server.Net;

/// <summary>Límites por conexión (docs/architecture.md §4). Técnicos: viven en appsettings `Net:RateLimits`, no en rules.json.</summary>
public sealed class RateLimitOptions
{
    public double MoveInputPerSec { get; set; } = 30;
    /// <summary>El cliente manda un MoveInput por tick (20/s): un corte de red breve los entrega de golpe y no debe desconectar.</summary>
    public double MoveInputBurst { get; set; } = 90;
    public double CastSpellPerSec { get; set; } = 10;
    public double ChatPerSec { get; set; } = 1; // 5 por 5 s
    public double ChatBurst { get; set; } = 5;
    public double DefaultPerSec { get; set; } = 20;
    /// <summary>Excesos tolerados en `ExcessWindowSec` antes de desconectar.</summary>
    public int MaxExcess { get; set; } = 3;
    public double ExcessWindowSec { get; set; } = 10;
    public int MaxConnectionsPerIp { get; set; } = 10;
}

/// <summary>
/// HU-071 CA1/CA2: token bucket por conexión y tipo de mensaje; 3 excesos en 10 s → desconexión. Vive en la tarea de la
/// conexión (sin compartir estado). Determinista para tests: recibe el reloj en ms.
/// </summary>
public sealed class MessageRateLimiter(RateLimitOptions options)
{
    private sealed class Bucket(double ratePerSec, double capacity, long nowMs)
    {
        private readonly double _capacity = capacity;
        private double _tokens = capacity;
        private long _lastMs = nowMs;

        public bool TryTake(long nowMs)
        {
            _tokens = Math.Min(_capacity, _tokens + (nowMs - _lastMs) / 1000.0 * ratePerSec);
            _lastMs = nowMs;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }

    private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly Queue<long> _excesses = new();

    public int ExcessCount => _excesses.Count;

    /// <summary>Devuelve Allowed, Limited (se descarta con Error) o Disconnect (demasiados excesos).</summary>
    public RateDecision Check(string type, long nowMs)
    {
        var key = type switch { "MoveInput" => "move", "CastSpell" => "cast", "ChatSend" => "chat", _ => "default" };
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = key switch
            {
                "move" => new Bucket(options.MoveInputPerSec, options.MoveInputBurst, nowMs),
                "cast" => new Bucket(options.CastSpellPerSec, options.CastSpellPerSec, nowMs),
                "chat" => new Bucket(options.ChatPerSec, options.ChatBurst, nowMs),
                _ => new Bucket(options.DefaultPerSec, options.DefaultPerSec, nowMs),
            };
            _buckets[key] = bucket;
        }
        if (bucket.TryTake(nowMs)) return RateDecision.Allowed;
        var windowMs = (long)(options.ExcessWindowSec * 1000);
        while (_excesses.Count > 0 && nowMs - _excesses.Peek() > windowMs) _excesses.Dequeue();
        _excesses.Enqueue(nowMs);
        return _excesses.Count >= options.MaxExcess ? RateDecision.Disconnect : RateDecision.Limited;
    }
}

public enum RateDecision { Allowed, Limited, Disconnect }
